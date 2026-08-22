using System.Buffers;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers.Zip;
using DomainProgressReport = RusZip.Core.Models.ProgressReport;

namespace RusZip.Core.Engines;

public sealed class SharpCompressArchiveEngine : IArchiveEngine
{
    private const int BufferSize = 81920; // 80 KB

    public async Task CompressAsync(
        ArchiveCompressionRequest request,
        IProgress<DomainProgressReport>? progress = null,
        CancellationToken ct = default)
    {
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

        var files = isDir
            ? Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories)
            : [sourcePath];

        long totalBytes = files.Sum(f => new FileInfo(f).Length);
        long processedBytes = 0;
        int processedFiles = 0;

        await Task.Run(async () =>
        {
            await using var outputStream = new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true);

            var compressionType = request.CompressionLevel == 0 ? SharpCompress.Common.CompressionType.None : SharpCompress.Common.CompressionType.Deflate;
            var writerOptions = new ZipWriterOptions(compressionType)
            {
                UseZip64 = true,
                LeaveStreamOpen = false
            };

            using var zipWriter = new ZipWriter(outputStream, writerOptions);

            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();

                var relPath = isDir
                    ? Path.GetRelativePath(sourcePath, filePath).Replace('\\', '/')
                    : Path.GetFileName(filePath);

                var fileInfo = new FileInfo(filePath);

                await using var fileStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    BufferSize,
                    useAsync: true);

                zipWriter.Write(relPath, fileStream, fileInfo.LastWriteTimeUtc);

                processedBytes += fileInfo.Length;
                processedFiles++;

                progress?.Report(new DomainProgressReport(
                    ProcessedBytes: processedBytes,
                    TotalBytes: totalBytes,
                    CurrentFileName: relPath,
                    Percentage: totalBytes > 0 ? (double)processedBytes / totalBytes * 100.0 : 0,
                    ProcessedFiles: processedFiles,
                    TotalFiles: files.Length
                ));
            }
        }, ct);
    }

    public async Task ExtractAsync(
        ArchiveExtractionRequest request,
        IProgress<DomainProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var archivePath = Path.GetFullPath(request.ArchivePath);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"Archive not found: {archivePath}");
        }

        var destDir = Path.GetFullPath(request.DestinationDirectory);
        Directory.CreateDirectory(destDir);

        var format = ArchiveFormatDetector.DetectFromPath(archivePath);

        if (format == ArchiveFormat.TarGz)
        {
            await ExtractTarGzAsync(archivePath, destDir, request.Overwrite, progress, ct);
            return;
        }

        if (format == ArchiveFormat.Gz)
        {
            await ExtractGzAsync(archivePath, destDir, request.Overwrite, progress, ct);
            return;
        }

        await Task.Run(async () =>
        {
            var readerOptions = new ReaderOptions { LeaveStreamOpen = false };
            using var archive = OpenArchiveByFormat(archivePath, format, readerOptions);

            if (archive is RarArchive rarArchive && !rarArchive.IsComplete)
            {
                throw new InvalidOperationException("Multi-volume RAR archive is missing subsequent volume parts.");
            }

            long totalBytes = 0;
            try
            {
                totalBytes = archive.Entries
                    .Where(e => !e.IsDirectory && e.Size > 0)
                    .Sum(e => e.Size);
            }
            catch
            {
                totalBytes = -1;
            }

            long processedBytes = 0;
            int processedFiles = 0;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

            try
            {
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (entry.IsEncrypted)
                    {
                        throw new NotSupportedException($"The entry '{entry.Key}' is password-protected. Encrypted archives are not supported.");
                    }

                    if (string.IsNullOrEmpty(entry.Key))
                        continue;

                    var targetPath = Path.GetFullPath(Path.Combine(destDir, entry.Key));
                    if (!targetPath.StartsWith(destDir, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new SecurityException($"Malicious entry detected attempting path traversal: {entry.Key}");
                    }

                    if (entry.IsDirectory)
                    {
                        Directory.CreateDirectory(targetPath);
                        continue;
                    }

                    var parentDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    using var entryStream = entry.OpenEntryStream();
                    await using var targetStream = new FileStream(
                        targetPath,
                        request.Overwrite ? FileMode.Create : FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        BufferSize,
                        useAsync: true);

                    int bytesRead;
                    while ((bytesRead = await entryStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        await targetStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        processedBytes += bytesRead;

                        progress?.Report(new DomainProgressReport(
                            ProcessedBytes: processedBytes,
                            TotalBytes: totalBytes,
                            CurrentFileName: entry.Key,
                            Percentage: totalBytes > 0 ? (double)processedBytes / totalBytes * 100.0 : 0,
                            ProcessedFiles: processedFiles
                        ));
                    }

                    processedFiles++;

                    if (entry.LastModifiedTime.HasValue)
                    {
                        File.SetLastWriteTimeUtc(targetPath, entry.LastModifiedTime.Value.ToUniversalTime());
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }, ct);
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

        var format = ArchiveFormatDetector.DetectFromPath(fullPath);

        if (format == ArchiveFormat.TarGz)
        {
            return await ListTarGzEntriesAsync(fullPath, ct);
        }

        if (format == ArchiveFormat.Gz)
        {
            var fileName = Path.GetFileNameWithoutExtension(fullPath);
            return [new ArchiveEntry(fileName, new FileInfo(fullPath).Length, null, File.GetLastWriteTimeUtc(fullPath), false)];
        }

        return await Task.Run(() =>
        {
            var results = new List<ArchiveEntry>();
            var readerOptions = new ReaderOptions { LeaveStreamOpen = false };

            using var archive = OpenArchiveByFormat(fullPath, format, readerOptions);
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(new ArchiveEntry(
                    RelativePath: entry.Key ?? string.Empty,
                    UncompressedSize: entry.Size,
                    CompressedSize: entry.CompressedSize,
                    LastModified: entry.LastModifiedTime.HasValue ? new DateTimeOffset(entry.LastModifiedTime.Value) : null,
                    IsDirectory: entry.IsDirectory,
                    IsEncrypted: entry.IsEncrypted
                ));
            }

            return (IReadOnlyList<ArchiveEntry>)results;
        }, ct);
    }

    private static IArchive OpenArchiveByFormat(string filePath, ArchiveFormat format, ReaderOptions options)
    {
        return format switch
        {
            ArchiveFormat.Zip => SharpCompress.Archives.Zip.ZipArchive.OpenArchive(filePath, options),
            ArchiveFormat.Rar => RarArchive.OpenArchive(filePath, options),
            ArchiveFormat.SevenZip => SevenZipArchive.OpenArchive(filePath, options),
            _ => throw new NotSupportedException($"Format '{format}' not directly supported via SharpCompress IArchive")
        };
    }

    private static async Task ExtractTarGzAsync(
        string archivePath,
        string destinationDir,
        bool overwrite,
        IProgress<DomainProgressReport>? progress,
        CancellationToken ct)
    {
        await using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream, leaveOpen: false);

        long totalExtractedBytes = 0;
        int extractedFiles = 0;

        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync(copyData: false, ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();

            var targetPath = Path.GetFullPath(Path.Combine(destinationDir, entry.Name));
            if (!targetPath.StartsWith(destinationDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException($"Malicious entry detected attempting path traversal: {entry.Name}");
            }

            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
            {
                var parent = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                if (entry.DataStream is not null)
                {
                    await using var outFs = new FileStream(
                        targetPath,
                        overwrite ? FileMode.Create : FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        BufferSize,
                        useAsync: true);

                    var buffer = new byte[BufferSize];
                    int bytesRead;

                    while ((bytesRead = await entry.DataStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        await outFs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        totalExtractedBytes += bytesRead;

                        progress?.Report(new DomainProgressReport(
                            ProcessedBytes: totalExtractedBytes,
                            TotalBytes: -1,
                            CurrentFileName: entry.Name,
                            Percentage: 0,
                            ProcessedFiles: extractedFiles
                        ));
                    }

                    extractedFiles++;
                }

                File.SetLastWriteTimeUtc(targetPath, entry.ModificationTime.UtcDateTime);
            }
        }
    }

    private static async Task ExtractGzAsync(
        string archivePath,
        string destinationDir,
        bool overwrite,
        IProgress<DomainProgressReport>? progress,
        CancellationToken ct)
    {
        var outFileName = Path.GetFileNameWithoutExtension(archivePath);
        var targetPath = Path.Combine(destinationDir, outFileName);

        await using var inStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        await using var gzipStream = new GZipStream(inStream, CompressionMode.Decompress);
        await using var outStream = new FileStream(targetPath, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        var buffer = new byte[BufferSize];
        int bytesRead;
        long totalBytes = 0;

        while ((bytesRead = await gzipStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await outStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalBytes += bytesRead;

            progress?.Report(new DomainProgressReport(
                ProcessedBytes: totalBytes,
                TotalBytes: -1,
                CurrentFileName: outFileName,
                Percentage: 0,
                ProcessedFiles: 1,
                TotalFiles: 1
            ));
        }
    }

    private static async Task<IReadOnlyList<ArchiveEntry>> ListTarGzEntriesAsync(
        string archivePath,
        CancellationToken ct)
    {
        var results = new List<ArchiveEntry>();
        await using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream, leaveOpen: false);

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

        return results;
    }
}
