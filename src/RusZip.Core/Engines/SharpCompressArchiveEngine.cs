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
        if (request.CompressionLevel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.CompressionLevel), "Compression level cannot be negative.");
        }

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

        var tempOutput = destination + ".tmp." + Guid.NewGuid().ToString("N");

        try
        {
            await Task.Run(async () =>
            {
                await using (var outputStream = new FileStream(
                    tempOutput,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    useAsync: true))
                {
                    var compressionType = request.CompressionLevel == 0 ? SharpCompress.Common.CompressionType.None : SharpCompress.Common.CompressionType.Deflate;
                    var writerOptions = new ZipWriterOptions(compressionType)
                    {
                        UseZip64 = true,
                        LeaveStreamOpen = false
                    };

                    using var zipWriter = new ZipWriter(outputStream, writerOptions);

                    if (isDir)
                    {
                        var rootDirInfo = new DirectoryInfo(sourcePath);
                        var fileEntries = rootDirInfo.GetFiles("*", SearchOption.AllDirectories);
                        var emptyDirs = rootDirInfo.GetDirectories("*", SearchOption.AllDirectories)
                            .Where(d => d.GetFileSystemInfos().Length == 0)
                            .ToList();

                        long totalBytes = fileEntries.Sum(f => f.Length);
                        int totalFiles = fileEntries.Length;
                        long processedBytes = 0;
                        int processedFiles = 0;

                        foreach (var emptyDir in emptyDirs)
                        {
                            ct.ThrowIfCancellationRequested();
                            var relPath = Path.GetRelativePath(rootDirInfo.FullName, emptyDir.FullName).Replace('\\', '/');
                            zipWriter.WriteDirectory(relPath, emptyDir.LastWriteTimeUtc);
                        }

                        foreach (var fileInfo in fileEntries)
                        {
                            ct.ThrowIfCancellationRequested();
                            var relPath = Path.GetRelativePath(rootDirInfo.FullName, fileInfo.FullName).Replace('\\', '/');

                            await using var fileStream = new FileStream(
                                fileInfo.FullName,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.Read,
                                BufferSize,
                                useAsync: true);

                            await using var trackingStream = new ProgressReportingStream(
                                fileStream,
                                fileInfo.Length,
                                bytesRead =>
                                {
                                    Interlocked.Add(ref processedBytes, bytesRead);
                                    var currentTotal = Volatile.Read(ref processedBytes);
                                    progress?.Report(new DomainProgressReport(
                                        ProcessedBytes: currentTotal,
                                        TotalBytes: totalBytes,
                                        CurrentFileName: relPath,
                                        Percentage: totalBytes > 0 ? (double)currentTotal / totalBytes * 100.0 : 0,
                                        ProcessedFiles: Volatile.Read(ref processedFiles),
                                        TotalFiles: totalFiles
                                    ));
                                });

                            zipWriter.Write(relPath, trackingStream, fileInfo.LastWriteTimeUtc);
                            Interlocked.Increment(ref processedFiles);
                        }
                    }
                    else
                    {
                        var fileInfo = new FileInfo(sourcePath);
                        var relPath = fileInfo.Name;
                        long totalBytes = fileInfo.Length;
                        long processedBytes = 0;
                        int processedFiles = 0;

                        await using var fileStream = new FileStream(
                            fileInfo.FullName,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            BufferSize,
                            useAsync: true);

                        await using var trackingStream = new ProgressReportingStream(
                            fileStream,
                            fileInfo.Length,
                            bytesRead =>
                            {
                                Interlocked.Add(ref processedBytes, bytesRead);
                                var currentTotal = Volatile.Read(ref processedBytes);
                                progress?.Report(new DomainProgressReport(
                                    ProcessedBytes: currentTotal,
                                    TotalBytes: totalBytes,
                                    CurrentFileName: relPath,
                                    Percentage: totalBytes > 0 ? (double)currentTotal / totalBytes * 100.0 : 0,
                                    ProcessedFiles: Volatile.Read(ref processedFiles),
                                    TotalFiles: 1
                                ));
                            });

                        zipWriter.Write(relPath, trackingStream, fileInfo.LastWriteTimeUtc);
                        Interlocked.Increment(ref processedFiles);
                    }
                }

                File.Move(tempOutput, destination, overwrite: true);
            }, ct);
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
            IArchive archive;
            try
            {
                archive = OpenArchiveByFormat(archivePath, format, readerOptions);
            }
            catch (Exception ex) when (IsPasswordOrEncryptedException(ex))
            {
                throw new NotSupportedException($"The archive '{Path.GetFileName(archivePath)}' is password-protected or encrypted. Encrypted archives are not supported.", ex);
            }

            using (archive)
            {
                try
                {
                    bool isComplete = true;
                    try
                    {
                        if (archive is RarArchive rarArchive)
                        {
                            isComplete = rarArchive.IsComplete;
                        }
                    }
                    catch (Exception ex) when (ex is InvalidFormatException or ArchiveException)
                    {
                        isComplete = false;
                    }

                    if (!isComplete)
                    {
                        throw new InvalidOperationException("Multi-volume RAR archive is missing subsequent volume parts.");
                    }

                    long totalBytes = 0;
                    int totalFiles = 0;
                    try
                    {
                        foreach (var e in archive.Entries)
                        {
                            if (e.IsEncrypted)
                            {
                                throw new NotSupportedException($"The entry '{e.Key}' is password-protected. Encrypted archives are not supported.");
                            }

                            if (!e.IsDirectory)
                            {
                                totalFiles++;
                                if (e.Size > 0)
                                {
                                    totalBytes += e.Size;
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not NotSupportedException && IsPasswordOrEncryptedException(ex))
                    {
                        throw new NotSupportedException($"The archive '{Path.GetFileName(archivePath)}' is password-protected or encrypted. Encrypted archives are not supported.", ex);
                    }
                    catch (NotSupportedException)
                    {
                        throw;
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
                        var normalizedDestDir = destDir.EndsWith(Path.DirectorySeparatorChar)
                            ? destDir
                            : destDir + Path.DirectorySeparatorChar;

                        IEnumerable<IArchiveEntry> entries;
                        try
                        {
                            entries = archive.Entries;
                        }
                        catch (Exception ex) when (IsPasswordOrEncryptedException(ex))
                        {
                            throw new NotSupportedException($"The archive '{Path.GetFileName(archivePath)}' is password-protected or encrypted. Encrypted archives are not supported.", ex);
                        }

                        foreach (var entry in entries)
                        {
                            ct.ThrowIfCancellationRequested();

                            if (entry.IsEncrypted)
                            {
                                throw new NotSupportedException($"The entry '{entry.Key}' is password-protected. Encrypted archives are not supported.");
                            }

                            if (string.IsNullOrWhiteSpace(entry.Key))
                                continue;

                            var entryKey = entry.Key.Replace('\\', '/');
                            var targetPath = Path.GetFullPath(Path.Combine(destDir, entryKey));

                            if (!targetPath.StartsWith(normalizedDestDir, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(targetPath, destDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                            {
                                throw new SecurityException($"Malicious entry detected attempting path traversal: {entry.Key}");
                            }

                            if (entry.IsDirectory || entryKey.EndsWith('/') || Directory.Exists(targetPath))
                            {
                                Directory.CreateDirectory(targetPath);
                                continue;
                            }

                            var parentDir = Path.GetDirectoryName(targetPath);
                            if (!string.IsNullOrEmpty(parentDir))
                            {
                                Directory.CreateDirectory(parentDir);
                            }

                            if (!request.Overwrite && File.Exists(targetPath))
                            {
                                throw new IOException($"Destination file already exists and overwrite is false: '{targetPath}'");
                            }

                            Stream entryStream;
                            try
                            {
                                entryStream = entry.OpenEntryStream();
                            }
                            catch (Exception ex) when (IsPasswordOrEncryptedException(ex))
                            {
                                throw new NotSupportedException($"The entry '{entry.Key}' is password-protected. Encrypted archives are not supported.", ex);
                            }

                            using (entryStream)
                            await using (var targetStream = new FileStream(
                                targetPath,
                                request.Overwrite ? FileMode.Create : FileMode.CreateNew,
                                FileAccess.Write,
                                FileShare.None,
                                BufferSize,
                                useAsync: true))
                            {
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
                                        ProcessedFiles: processedFiles,
                                        TotalFiles: totalFiles
                                    ));
                                }
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
                }
                catch (Exception ex) when (ex is not SecurityException && ex is not NotSupportedException && ex is not InvalidOperationException && ex is not IOException && ex is not OperationCanceledException && IsPasswordOrEncryptedException(ex))
                {
                    throw new NotSupportedException($"The archive '{Path.GetFileName(archivePath)}' is password-protected or encrypted. Encrypted archives are not supported.", ex);
                }
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
            return await ListGzEntryAsync(fullPath, ct);
        }

        return await Task.Run(() =>
        {
            var results = new List<ArchiveEntry>();
            var readerOptions = new ReaderOptions { LeaveStreamOpen = false };

            try
            {
                using var archive = OpenArchiveByFormat(fullPath, format, readerOptions);
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    results.Add(new ArchiveEntry(
                        RelativePath: entry.Key ?? string.Empty,
                        UncompressedSize: entry.Size,
                        CompressedSize: entry.CompressedSize,
                        LastModified: entry.LastModifiedTime.HasValue ? new DateTimeOffset(entry.LastModifiedTime.Value) : null,
                        IsDirectory: entry.IsDirectory || (entry.Key != null && entry.Key.Replace('\\', '/').EndsWith('/')),
                        IsEncrypted: entry.IsEncrypted
                    ));
                }
            }
            catch (Exception ex) when (IsPasswordOrEncryptedException(ex))
            {
                throw new NotSupportedException($"The archive '{Path.GetFileName(fullPath)}' is password-protected or encrypted. Encrypted archives are not supported.", ex);
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
        var normalizedDestDir = destinationDir.EndsWith(Path.DirectorySeparatorChar)
            ? destinationDir
            : destinationDir + Path.DirectorySeparatorChar;

        await using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream, leaveOpen: false);

        long totalExtractedBytes = 0;
        int extractedFiles = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            TarEntry? entry;
            while ((entry = await tarReader.GetNextEntryAsync(copyData: false, ct)) is not null)
            {
                ct.ThrowIfCancellationRequested();

                var entryName = entry.Name.Replace('\\', '/');
                var targetPath = Path.GetFullPath(Path.Combine(destinationDir, entryName));

                if (!targetPath.StartsWith(normalizedDestDir, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(targetPath, destinationDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Malicious entry detected attempting path traversal: {entry.Name}");
                }

                if (entry.EntryType == TarEntryType.Directory || entryName.EndsWith('/') || Directory.Exists(targetPath))
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

                    if (!overwrite && File.Exists(targetPath))
                    {
                        throw new IOException($"Destination file already exists and overwrite is false: '{targetPath}'");
                    }

                    await using (var outFs = new FileStream(
                        targetPath,
                        overwrite ? FileMode.Create : FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        BufferSize,
                        useAsync: true))
                    {
                        if (entry.DataStream is not null)
                        {
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
                                    ProcessedFiles: extractedFiles,
                                    TotalFiles: 0,
                                    IsIndeterminate: true
                                ));
                            }
                        }
                    }

                    extractedFiles++;

                    File.SetLastWriteTimeUtc(targetPath, entry.ModificationTime.UtcDateTime);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task ExtractGzAsync(
        string archivePath,
        string destinationDir,
        bool overwrite,
        IProgress<DomainProgressReport>? progress,
        CancellationToken ct)
    {
        var normalizedDestDir = destinationDir.EndsWith(Path.DirectorySeparatorChar)
            ? destinationDir
            : destinationDir + Path.DirectorySeparatorChar;

        var outFileName = Path.GetFileNameWithoutExtension(archivePath);
        var targetPath = Path.GetFullPath(Path.Combine(destinationDir, outFileName));

        if (!targetPath.StartsWith(normalizedDestDir, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(targetPath, destinationDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Malicious entry detected attempting path traversal: {outFileName}");
        }

        if (!overwrite && File.Exists(targetPath))
        {
            throw new IOException($"Destination file already exists and overwrite is false: '{targetPath}'");
        }

        await using var inStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        await using var gzipStream = new GZipStream(inStream, CompressionMode.Decompress);
        await using var outStream = new FileStream(targetPath, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
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
                    TotalFiles: 1,
                    IsIndeterminate: true
                ));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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
                IsDirectory: entry.EntryType == TarEntryType.Directory || entry.Name.EndsWith('/'),
                IsEncrypted: false,
                Attributes: entry.Mode.ToString()
            ));
        }

        return results;
    }

    private static async Task<IReadOnlyList<ArchiveEntry>> ListGzEntryAsync(string fullPath, CancellationToken ct)
    {
        var fileName = Path.GetFileNameWithoutExtension(fullPath);
        long uncompressedSize = -1;
        try
        {
            await using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64, useAsync: true);
            if (fs.Length >= 18)
            {
                fs.Seek(-4, SeekOrigin.End);
                var b = new byte[4];
                var read = await fs.ReadAsync(b.AsMemory(0, 4), ct);
                if (read == 4)
                {
                    uncompressedSize = BitConverter.ToUInt32(b, 0);
                }
            }
        }
        catch
        {
            uncompressedSize = -1;
        }

        var fileInfo = new FileInfo(fullPath);
        return [new ArchiveEntry(
            RelativePath: fileName,
            UncompressedSize: uncompressedSize >= 0 ? uncompressedSize : fileInfo.Length,
            CompressedSize: fileInfo.Length,
            LastModified: fileInfo.LastWriteTimeUtc,
            IsDirectory: false,
            IsEncrypted: false)];
    }

    private static bool IsPasswordOrEncryptedException(Exception ex)
    {
        return ex is SharpCompress.Common.CryptographicException or System.Security.Cryptography.CryptographicException
            || ex.GetType().Name.Contains("Cryptographic", StringComparison.OrdinalIgnoreCase)
            || ex.GetType().Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase)
            || (ex is ArgumentNullException && ex.StackTrace?.Contains("DecoderRegistry", StringComparison.OrdinalIgnoreCase) == true)
            || ex.StackTrace?.Contains("IPasswordProvider", StringComparison.OrdinalIgnoreCase) == true
            || (ex.InnerException is not null && IsPasswordOrEncryptedException(ex.InnerException));
    }
}
