using System.Buffers;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.CompilerServices;
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

        var format = ArchiveFormatRegistry.Detect(archivePath).Format;

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
                try
                {
                    foreach (var e in archive.Entries)
                    {
                        if (e.IsEncrypted)
                        {
                            throw new NotSupportedException($"The entry '{e.Key}' is password-protected. Encrypted archives are not supported.");
                        }

                        if (!e.IsDirectory && e.Size > 0)
                        {
                            totalBytes += e.Size;
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

                var source = new SharpCompressExtractionSource(archive, archivePath);

                await SafeArchiveExtractor.ExtractAllAsync(
                    source,
                    destDir,
                    request.Overwrite,
                    totalBytes,
                    progress,
                    ct);
            }
            catch (Exception ex) when (ex is not SecurityException && ex is not NotSupportedException && ex is not InvalidOperationException && ex is not IOException && ex is not OperationCanceledException && IsPasswordOrEncryptedException(ex))
            {
                throw new NotSupportedException($"The archive '{Path.GetFileName(archivePath)}' is password-protected or encrypted. Encrypted archives are not supported.", ex);
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

        var format = ArchiveFormatRegistry.Detect(fullPath).Format;

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
        var source = new TarGzExtractionSource(archivePath);
        await SafeArchiveExtractor.ExtractAllAsync(
            source,
            destinationDir,
            overwrite,
            totalBytes: -1,
            progress,
            ct);
    }

    private static async Task ExtractGzAsync(
        string archivePath,
        string destinationDir,
        bool overwrite,
        IProgress<DomainProgressReport>? progress,
        CancellationToken ct)
    {
        var source = new GzExtractionSource(archivePath);
        await SafeArchiveExtractor.ExtractAllAsync(
            source,
            destinationDir,
            overwrite,
            totalBytes: -1,
            progress,
            ct);
    }

    private sealed class TarGzExtractionSource(string archivePath) : IArchiveExtractionSource
    {
        public async IAsyncEnumerable<ExtractionEntry> ReadEntriesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, SafeArchiveExtractor.BufferSize, useAsync: true);
            await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            await using var tarReader = new TarReader(gzipStream, leaveOpen: false);

            TarEntry? entry;
            while ((entry = await tarReader.GetNextEntryAsync(copyData: false, ct)) is not null)
            {
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

    private sealed class GzExtractionSource(string archivePath) : IArchiveExtractionSource
    {
        public async IAsyncEnumerable<ExtractionEntry> ReadEntriesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            var outFileName = Path.GetFileNameWithoutExtension(archivePath);
            var fileInfo = new FileInfo(archivePath);

            yield return new ExtractionEntry(
                RelativePath: outFileName,
                IsDirectory: false,
                UncompressedSize: -1,
                ModificationTime: fileInfo.LastWriteTimeUtc,
                UnixMode: null,
                OpenStreamAsync: _ =>
                {
                    var inStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, SafeArchiveExtractor.BufferSize, useAsync: true);
                    var gzipStream = new GZipStream(inStream, CompressionMode.Decompress);
                    return ValueTask.FromResult<Stream>(gzipStream);
                }
            );
            await Task.CompletedTask;
        }
    }

    private sealed class SharpCompressExtractionSource(IArchive archive, string archivePath) : IArchiveExtractionSource
    {
        public async IAsyncEnumerable<ExtractionEntry> ReadEntriesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
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

                bool isDir = entry.IsDirectory || entry.Key.Replace('\\', '/').EndsWith('/');
                DateTimeOffset? modTime = entry.LastModifiedTime.HasValue ? new DateTimeOffset(entry.LastModifiedTime.Value.ToUniversalTime()) : null;

                yield return new ExtractionEntry(
                    RelativePath: entry.Key,
                    IsDirectory: isDir,
                    UncompressedSize: entry.Size,
                    ModificationTime: modTime,
                    UnixMode: null,
                    OpenStreamAsync: _ =>
                    {
                        try
                        {
                            var stream = entry.OpenEntryStream();
                            return ValueTask.FromResult(stream);
                        }
                        catch (Exception ex) when (IsPasswordOrEncryptedException(ex))
                        {
                            throw new NotSupportedException($"The entry '{entry.Key}' is password-protected. Encrypted archives are not supported.", ex);
                        }
                    }
                );
            }

            await Task.CompletedTask;
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
