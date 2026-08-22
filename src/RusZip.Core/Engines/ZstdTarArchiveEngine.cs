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
            throw new InvalidDataException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", ex);
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
                throw new InvalidDataException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", ex);
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
                    throw new InvalidDataException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", ex);
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
                        throw new InvalidDataException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", ex);
                    }

                    await using (tarReader)
                    {
                        while (true)
                        {
                            TarEntry? entry;
                            try
                            {
                                entry = await tarReader.GetNextEntryAsync(copyData: false, ct);
                            }
                            catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
                            {
                                throw new InvalidDataException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", ex);
                            }

                            if (entry is null)
                                break;

                            bool isDir = entry.EntryType == TarEntryType.Directory || entry.Name.Replace('\\', '/').EndsWith('/');
                            UnixFileMode? unixMode = entry.Mode != 0 && entry.Mode != (UnixFileMode)(-1) ? entry.Mode : null;
                            var dataStream = entry.DataStream;

                            yield return new ExtractionEntry(
                                RelativePath: entry.Name,
                                IsDirectory: isDir,
                                UncompressedSize: entry.Length,
                                ModificationTime: entry.ModificationTime,
                                UnixMode: unixMode,
                                OpenStreamAsync: _ => ValueTask.FromResult(dataStream ?? Stream.Null)
                            );
                        }
                    }
                }
            }
        }
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
        }
        catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
        {
            throw new InvalidDataException($"Zstandard frame error in '{fullPath}': {ex.Message}", ex);
        }

        return results;
    }
}
