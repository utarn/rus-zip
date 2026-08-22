using System.Buffers;
using System.Formats.Tar;
using System.Runtime.CompilerServices;
using System.Security;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace RusZip.Core.Engines;

public sealed class ZstdTarArchiveEngine : IArchiveEngine
{
    private const int BufferSize = 81920; // 80 KB
    public static readonly byte[] ZstdMagic = [0x28, 0xB5, 0x2F, 0xFD];

    public async Task CompressAsync(
        ArchiveCompressionRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(request.CompressionLevel, CompressionProfiles.MinLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.CompressionLevel, CompressionProfiles.MaxLevel);

        var sourcePath = Path.GetFullPath(request.SourcePath);
        var isDir = Directory.Exists(sourcePath);
        var isFile = File.Exists(sourcePath);

        if (!isDir && !isFile)
        {
            throw new FileNotFoundException($"Source path does not exist: {request.SourcePath}");
        }

        var destination = Path.GetFullPath(request.DestinationArchivePath);
        var destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // Calculate total uncompressed bytes & file list
        var fileList = new List<string>();
        long totalBytes = 0;

        if (isDir)
        {
            fileList.AddRange(Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories));
            totalBytes = fileList.Sum(f => new FileInfo(f).Length);
        }
        else
        {
            fileList.Add(sourcePath);
            totalBytes = new FileInfo(sourcePath).Length;
        }

        var tempOutput = destination + ".tmp." + Guid.NewGuid().ToString("N");
        long processedBytes = 0;
        int processedFiles = 0;

        try
        {
            await using (var fileStream = new FileStream(
                tempOutput,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true))
            await using (var compressionStream = new CompressionStream(fileStream, request.CompressionLevel))
            {
                compressionStream.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
                if (Environment.ProcessorCount > 1)
                {
                    compressionStream.SetParameter(ZSTD_cParameter.ZSTD_c_nbWorkers, Environment.ProcessorCount);
                }

                await using (var tarWriter = new TarWriter(compressionStream, TarEntryFormat.Pax, leaveOpen: false))
                {
                    if (isDir)
                    {
                        var rootDirInfo = new DirectoryInfo(sourcePath);

                        foreach (var fsi in rootDirInfo.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                        {
                            ct.ThrowIfCancellationRequested();
                            var relPath = Path.GetRelativePath(rootDirInfo.FullName, fsi.FullName).Replace('\\', '/');

                            if (fsi is DirectoryInfo dirInfo)
                            {
                                var dirEntry = new PaxTarEntry(TarEntryType.Directory, relPath.TrimEnd('/') + "/")
                                {
                                    ModificationTime = dirInfo.LastWriteTimeUtc
                                };
                                if (!OperatingSystem.IsWindows())
                                {
                                    dirEntry.Mode = dirInfo.UnixFileMode;
                                }
                                await tarWriter.WriteEntryAsync(dirEntry, ct);
                            }
                            else if (fsi is FileInfo fileInfo)
                            {
                                await using var srcStream = new FileStream(
                                    fileInfo.FullName,
                                    FileMode.Open,
                                    FileAccess.Read,
                                    FileShare.Read,
                                    BufferSize,
                                    useAsync: true);

                                await using var trackingStream = new ProgressReportingStream(
                                    srcStream,
                                    fileInfo.Length,
                                    bytesRead =>
                                    {
                                        Interlocked.Add(ref processedBytes, bytesRead);
                                        var currentTotal = Volatile.Read(ref processedBytes);
                                        progress?.Report(new ProgressReport(
                                            ProcessedBytes: currentTotal,
                                            TotalBytes: totalBytes,
                                            CurrentFileName: relPath,
                                            Percentage: totalBytes > 0 ? (double)currentTotal / totalBytes * 100.0 : 0,
                                            ProcessedFiles: Volatile.Read(ref processedFiles),
                                            TotalFiles: fileList.Count
                                        ));
                                    });

                                var fileEntry = new PaxTarEntry(TarEntryType.RegularFile, relPath)
                                {
                                    DataStream = trackingStream,
                                    ModificationTime = fileInfo.LastWriteTimeUtc
                                };
                                if (!OperatingSystem.IsWindows())
                                {
                                    fileEntry.Mode = fileInfo.UnixFileMode;
                                }

                                await tarWriter.WriteEntryAsync(fileEntry, ct);
                                Interlocked.Increment(ref processedFiles);
                            }
                        }
                    }
                    else
                    {
                        var fileInfo = new FileInfo(sourcePath);
                        var fileName = fileInfo.Name;

                        await using var srcStream = new FileStream(
                            fileInfo.FullName,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            BufferSize,
                            useAsync: true);

                        await using var trackingStream = new ProgressReportingStream(
                            srcStream,
                            fileInfo.Length,
                            bytesRead =>
                            {
                                Interlocked.Add(ref processedBytes, bytesRead);
                                var currentTotal = Volatile.Read(ref processedBytes);
                                progress?.Report(new ProgressReport(
                                    ProcessedBytes: currentTotal,
                                    TotalBytes: totalBytes,
                                    CurrentFileName: fileName,
                                    Percentage: totalBytes > 0 ? (double)currentTotal / totalBytes * 100.0 : 0,
                                    ProcessedFiles: Volatile.Read(ref processedFiles),
                                    TotalFiles: 1
                                ));
                            });

                        var fileEntry = new PaxTarEntry(TarEntryType.RegularFile, fileName)
                        {
                            DataStream = trackingStream,
                            ModificationTime = fileInfo.LastWriteTimeUtc
                        };
                        if (!OperatingSystem.IsWindows())
                        {
                            fileEntry.Mode = fileInfo.UnixFileMode;
                        }

                        await tarWriter.WriteEntryAsync(fileEntry, ct);
                        Interlocked.Increment(ref processedFiles);
                    }
                }
            }

            File.Move(tempOutput, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempOutput))
            {
                try { File.Delete(tempOutput); } catch { /* Ignore */ }
            }
        }
    }

    public async Task<ExtractionResult> ExtractAsync(
        ArchiveExtractionRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var archivePath = Path.GetFullPath(request.ArchivePath);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"Archive not found: {archivePath}");
        }

        // Pre-scan uncompressed total size. These totals are derived from header metadata and are
        // therefore spoofable — they drive the progress bar only (labeled as estimates) and are never
        // used for enforcement (see ADR-0007). Enforcement reads actual streamed bytes/entries.
        long totalBytes = 0;
        try
        {
            var entries = await ListEntriesAsync(archivePath, ct);
            totalBytes = entries.Where(e => !e.IsDirectory).Sum(e => e.UncompressedSize);
        }
        catch (ArchiveIntegrityException)
        {
            // Integrity failures are detected and reported by the actual extraction pass below
            // (with the entry name and partial-file cleanup). Swallow here so extraction proceeds
            // and surfaces the integrity error through the normal extraction path.
            totalBytes = -1;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch
        {
            totalBytes = -1;
        }

        var source = new ZstdTarExtractionSource(archivePath);

        try
        {
            return await SafeArchiveExtractor.ExtractAllAsync(
                source,
                request.DestinationDirectory,
                request.Overwrite,
                totalBytes,
                progress,
                ct,
                request.Limits,
                totalIsEstimate: totalBytes >= 0);
        }
        catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
        {
            throw new ArchiveIntegrityException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", innerException: ex);
        }
    }

    private sealed class ZstdTarExtractionSource(string archivePath) : IArchiveExtractionSource
    {
        public async IAsyncEnumerable<ExtractionEntry> ReadEntriesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            FileStream fileStream;
            try
            {
                fileStream = new FileStream(
                    archivePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    SafeArchiveExtractor.BufferSize,
                    useAsync: true);
            }
            catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
            {
                throw new ArchiveIntegrityException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", innerException: ex);
            }

            await using (fileStream)
            {
                DecompressionStream decompressionStream;
                try
                {
                    decompressionStream = new DecompressionStream(fileStream);
                }
                catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
                {
                    throw new ArchiveIntegrityException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", innerException: ex);
                }

                await using (decompressionStream)
                {
                    TarReader tarReader;
                    try
                    {
                        tarReader = new TarReader(decompressionStream, leaveOpen: false);
                    }
                    catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
                    {
                        throw new ArchiveIntegrityException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", innerException: ex);
                    }

                    await using (tarReader)
                    {
                        string? lastEntryName = null;
                        while (true)
                        {
                            TarEntry? entry;
                            try
                            {
                                entry = await tarReader.GetNextEntryAsync(copyData: false, ct);
                            }
                            catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
                            {
                                throw new ArchiveIntegrityException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", lastEntryName, ex);
                            }

                            if (entry is null)
                                break;

                            lastEntryName = entry.Name;
                            bool isDir = entry.EntryType == TarEntryType.Directory || entry.Name.Replace('\\', '/').EndsWith('/');
                            UnixFileMode? unixMode = entry.Mode != 0 && entry.Mode != (UnixFileMode)(-1) ? entry.Mode : null;
                            var dataStream = entry.DataStream;
                            if (dataStream is not null)
                            {
                                // Wrap so any mid-stream zstd corruption surfaces as an
                                // ArchiveIntegrityException with the offending entry name.
                                dataStream = new ZstdIntegrityStream(dataStream, archivePath, entry.Name);
                            }

                            yield return new ExtractionEntry(
                                RelativePath: entry.Name,
                                IsDirectory: isDir,
                                UncompressedSize: entry.Length,
                                ModificationTime: entry.ModificationTime,
                                UnixMode: unixMode,
                                OpenStreamAsync: _ => ValueTask.FromResult(dataStream ?? Stream.Null)
                            );
                        }

                        // Drain the decompression stream to EOF so the zstd frame content checksum is
                        // validated. Without this the tar end-of-archive markers are consumed but the
                        // trailing frame checksum trailer is never read, so a corrupted archive would
                        // silently extract with exit 0 (F-08).
                        byte[] drainBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                        try
                        {
                            int read;
                            while ((read = await decompressionStream.ReadAsync(drainBuffer.AsMemory(0, BufferSize), ct)) > 0)
                            {
                            }
                        }
                        catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
                        {
                            throw new ArchiveIntegrityException(
                                $"Zstandard frame content checksum validation failed in '{archivePath}'" +
                                (lastEntryName is not null ? $", entry '{lastEntryName}'" : string.Empty) +
                                $": {ex.Message}",
                                lastEntryName,
                                ex);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(drainBuffer);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Wraps a tar entry data stream (backed by the zstd decompression stream) and converts
    /// <see cref="ZstdException"/> / <see cref="EndOfStreamException"/> raised while reading into an
    /// <see cref="ArchiveIntegrityException"/> carrying the offending entry name. This lets the generic
    /// <see cref="SafeArchiveExtractor"/> treat corrupt data like any other abort condition: partial
    /// files are cleaned up and the failure maps to exit 1.
    /// </summary>
    private sealed class ZstdIntegrityStream : Stream
    {
        private readonly Stream _inner;
        private readonly string _archivePath;
        private readonly string _entryName;

        public ZstdIntegrityStream(Stream inner, string archivePath, string entryName)
        {
            _inner = inner;
            _archivePath = archivePath;
            _entryName = entryName;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                return _inner.Read(buffer, offset, count);
            }
            catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
            {
                throw new ArchiveIntegrityException(
                    $"Zstandard frame corrupted in '{_archivePath}', entry '{_entryName}': {ex.Message}",
                    _entryName,
                    ex);
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            try
            {
                return await _inner.ReadAsync(buffer, ct);
            }
            catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
            {
                throw new ArchiveIntegrityException(
                    $"Zstandard frame corrupted in '{_archivePath}', entry '{_entryName}': {ex.Message}",
                    _entryName,
                    ex);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public async Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Archive not found: {fullPath}");
        }

        var results = new List<ArchiveEntry>();

        try
        {
            await using var fileStream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                useAsync: true);

            await using var decompressionStream = new DecompressionStream(fileStream);
            await using var tarReader = new TarReader(decompressionStream, leaveOpen: false);

            TarEntry? entry;
            while ((entry = await tarReader.GetNextEntryAsync(copyData: false, ct)) is not null)
            {
                results.Add(new ArchiveEntry(
                    RelativePath: entry.Name,
                    UncompressedSize: entry.Length,
                    CompressedSize: null,
                    LastModified: entry.ModificationTime,
                    IsDirectory: entry.EntryType == TarEntryType.Directory,
                    IsEncrypted: false,
                    Attributes: entry.Mode.ToString()
                ));
            }

            // Drain the decompression stream to EOF so the zstd frame content checksum is validated.
            // `list` already reads through every entry's data (the decompression stream is not seekable,
            // so TarReader must consume data to reach the next header); the trailing frame checksum
            // trailer is the only part left unread. Draining it makes `list` fail on a checksum-broken
            // archive instead of silently reporting success (F-08).
            byte[] drainBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                int read;
                while ((read = await decompressionStream.ReadAsync(drainBuffer.AsMemory(0, BufferSize), ct)) > 0)
                {
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(drainBuffer);
            }
        }
        catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
        {
            throw new ArchiveIntegrityException($"Zstandard frame error in '{fullPath}': {ex.Message}", innerException: ex);
        }

        return results;
    }
}
