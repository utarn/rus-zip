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
        int entriesWritten = 0;

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

                // leaveOpen: true so the CompressionStream survives the TarWriter's disposal — an
                // empty directory produces zero tar entries and .NET's TarWriter then writes no
                // end-of-archive blocks, so the two zero blocks must be appended below.
                await using (var tarWriter = new TarWriter(compressionStream, TarEntryFormat.Pax, leaveOpen: true))
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
                                entriesWritten++;
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
                                entriesWritten++;
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
                        entriesWritten++;
                        Interlocked.Increment(ref processedFiles);
                    }
                }

                // A valid tar archive ends with two 512-byte zero blocks. .NET's TarWriter writes
                // them on dispose only when at least one entry was written; an empty directory
                // (zero entries) otherwise yields a zstd frame with empty content, which is not a
                // valid tar stream and is unreadable by tar readers (F-11). Append the two zero
                // blocks so an empty archive is a valid Tar+Zstd stream (an "empty tar").
                if (entriesWritten == 0)
                {
                    byte[] endOfArchive = new byte[1024]; // two 512-byte zero blocks
                    await compressionStream.WriteAsync(endOfArchive.AsMemory(), ct);
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
        // With a selective-extraction filter, only the matching subset contributes to the total so the
        // progress bar reflects exactly what will be written.
        long totalBytes = 0;
        try
        {
            var entries = await ListEntriesAsync(archivePath, ct);
            totalBytes = entries
                .Where(e => !e.IsDirectory && EntryFilter.IsMatch(e.RelativePath, request.Entries))
                .Sum(e => e.UncompressedSize);
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

        var source = new ZstdTarExtractionSource(archivePath, request.Entries);

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

    private sealed class ZstdTarExtractionSource(string archivePath, IReadOnlyList<string>? entryFilter) : IArchiveExtractionSource
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
                    var countingStream = new CountingReadStream(decompressionStream);
                    await using (countingStream)
                    {
                        TarReader tarReader;
                        try
                        {
                            tarReader = new TarReader(countingStream, leaveOpen: false);
                        }
                        catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
                        {
                            throw new ArchiveIntegrityException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", innerException: ex);
                        }

                        await using (tarReader)
                        {
                            string? lastEntryName = null;
                            bool matchedAny = false;
                            while (true)
                            {
                                TarEntry? entry;
                                try
                                {
                                    entry = await tarReader.GetNextEntryAsync(copyData: false, ct);
                                }
                                catch (EndOfStreamException) when (countingStream.TotalBytesRead == 0)
                                {
                                    // Legacy empty-directory output (F-11): a valid zstd frame whose
                                    // content is 0 bytes (the pre-fix 13-byte archive) is not a valid
                                    // tar stream but is unambiguous — there are no entries. Treat it as
                                    // an empty archive so legacy archives list/extract with exit 0.
                                    break;
                                }
                                catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
                                {
                                    throw new ArchiveIntegrityException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", lastEntryName, ex);
                                }

                                if (entry is null)
                                    break;

                                lastEntryName = entry.Name;

                                // Selective extraction: entries outside the filter are skipped entirely.
                                // The tar reader auto-skips a non-requested entry's data on the next
                                // GetNextEntryAsync call, so the zstd frame is still read to the end.
                                if (entryFilter is { Count: > 0 } && !EntryFilter.IsMatch(entry.Name, entryFilter))
                                    continue;

                                matchedAny = true;
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

                            // A selective filter that matched nothing is a clear error, not a silent
                            // zero-entry success (the user asked for a specific path that is absent).
                            if (entryFilter is { Count: > 0 } && !matchedAny)
                            {
                                throw new InvalidOperationException(EntryFilter.NoMatchMessage);
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

    /// <summary>
    /// Pass-through stream that counts the bytes read from the underlying stream. The reader uses it
    /// to distinguish a genuinely empty zstd frame (legacy empty-directory output: a valid frame with
    /// 0 decompressed bytes) from a truncated archive (partial bytes then EOF). The former is treated
    /// as an empty tar archive; the latter is corruption.
    /// </summary>
    private sealed class CountingReadStream(Stream inner) : Stream
    {
        private readonly Stream _inner = inner;

        /// <summary>Cumulative bytes read through this wrapper.</summary>
        public long TotalBytesRead { get; private set; }

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
            int n = _inner.Read(buffer, offset, count);
            TotalBytesRead += n;
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            int n = _inner.Read(buffer);
            TotalBytesRead += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => ReadAsyncCore(buffer, ct);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsyncCore(buffer.AsMemory(offset, count), ct).AsTask();

        private async ValueTask<int> ReadAsyncCore(Memory<byte> buffer, CancellationToken ct)
        {
            int n = await _inner.ReadAsync(buffer, ct);
            TotalBytesRead += n;
            return n;
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
            await using var countingStream = new CountingReadStream(decompressionStream);
            await using var tarReader = new TarReader(countingStream, leaveOpen: false);

            TarEntry? entry;
            try
            {
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
            }
            catch (EndOfStreamException) when (countingStream.TotalBytesRead == 0)
            {
                // Legacy empty-directory output (F-11): a valid zstd frame with 0 decompressed bytes
                // (the pre-fix 13-byte archive) is not a valid tar stream but is unambiguous — there
                // are no entries. Treat it as an empty archive so legacy archives list with exit 0.
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
