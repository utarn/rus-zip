using System.Buffers;
using System.Formats.Tar;
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

    public async Task ExtractAsync(
        ArchiveExtractionRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var archivePath = Path.GetFullPath(request.ArchivePath);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"Archive not found: {archivePath}");
        }

        var destinationDir = Path.GetFullPath(request.DestinationDirectory);
        Directory.CreateDirectory(destinationDir);

        var normalizedDestDir = destinationDir.EndsWith(Path.DirectorySeparatorChar)
            ? destinationDir
            : destinationDir + Path.DirectorySeparatorChar;

        // Pre-scan uncompressed total size
        long totalBytes = 0;
        try
        {
            var entries = await ListEntriesAsync(archivePath, ct);
            totalBytes = entries.Where(e => !e.IsDirectory).Sum(e => e.UncompressedSize);
        }
        catch
        {
            totalBytes = -1;
        }

        long totalExtractedBytes = 0;
        int extractedFiles = 0;
        var extractedDirectories = new List<(string TargetPath, DateTimeOffset ModTime, UnixFileMode Mode)>();

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            await using var fileStream = new FileStream(
                archivePath,
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
                ct.ThrowIfCancellationRequested();

                var entryName = entry.Name.Replace('\\', '/');
                var targetPath = Path.GetFullPath(Path.Combine(destinationDir, entryName));

                if (!targetPath.StartsWith(normalizedDestDir, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(targetPath, destinationDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Malicious path traversal detected in archive entry: {entry.Name}");
                }

                if (entry.EntryType is TarEntryType.Directory)
                {
                    Directory.CreateDirectory(targetPath);
                    extractedDirectories.Add((targetPath, entry.ModificationTime, entry.Mode));
                    continue;
                }

                if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                {
                    var parentDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    await using (var outFs = new FileStream(
                        targetPath,
                        request.Overwrite ? FileMode.Create : FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        BufferSize,
                        useAsync: true))
                    {
                        if (entry.DataStream is not null)
                        {
                            int bytesRead;
                            while ((bytesRead = await entry.DataStream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
                            {
                                await outFs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                                totalExtractedBytes += bytesRead;

                                progress?.Report(new ProgressReport(
                                    ProcessedBytes: totalExtractedBytes,
                                    TotalBytes: totalBytes,
                                    CurrentFileName: entry.Name,
                                    Percentage: totalBytes > 0 ? (double)totalExtractedBytes / totalBytes * 100.0 : 0,
                                    ProcessedFiles: extractedFiles
                                ));
                            }
                        }
                    }

                    extractedFiles++;

                    File.SetLastWriteTimeUtc(targetPath, entry.ModificationTime.UtcDateTime);
                    if (!OperatingSystem.IsWindows() && entry.Mode != 0 && entry.Mode != (UnixFileMode)(-1))
                    {
                        File.SetUnixFileMode(targetPath, entry.Mode);
                    }
                }
            }

            // Restore directory modification times and permissions in bottom-up order
            foreach (var dir in extractedDirectories.OrderByDescending(d => d.TargetPath.Length))
            {
                if (Directory.Exists(dir.TargetPath))
                {
                    try
                    {
                        Directory.SetLastWriteTimeUtc(dir.TargetPath, dir.ModTime.UtcDateTime);
                        if (!OperatingSystem.IsWindows() && dir.Mode != 0 && dir.Mode != (UnixFileMode)(-1))
                        {
                            File.SetUnixFileMode(dir.TargetPath, dir.Mode);
                        }
                    }
                    catch
                    {
                        // Best effort for directory timestamps/permissions
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
        {
            throw new InvalidDataException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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

internal sealed class ProgressReportingStream(Stream innerStream, long length, Action<int> onBytesRead) : Stream
{
    public override bool CanRead => innerStream.CanRead;
    public override bool CanSeek => innerStream.CanSeek;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position
    {
        get => innerStream.Position;
        set => innerStream.Position = value;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await innerStream.ReadAsync(buffer, cancellationToken);
        if (read > 0) onBytesRead(read);
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = innerStream.Read(buffer, offset, count);
        if (read > 0) onBytesRead(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = innerStream.Read(buffer);
        if (read > 0) onBytesRead(read);
        return read;
    }

    public override void Flush() => innerStream.Flush();
    public override long Seek(long offset, SeekOrigin origin) => innerStream.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            innerStream.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await innerStream.DisposeAsync();
        await base.DisposeAsync();
    }
}
