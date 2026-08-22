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

    public async Task<ExtractionResult> ExtractAsync(
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
            return await ExtractTarGzAsync(archivePath, destDir, request.Overwrite, request.Limits, progress, ct);
        }

        if (format == ArchiveFormat.Gz)
        {
            return await ExtractGzAsync(archivePath, destDir, request.Overwrite, request.Limits, progress, ct);
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

                // Metadata-derived total for the progress bar only (spoofable, labeled as an estimate).
                // Enforcement never reads it — see ADR-0007.
                long totalBytes = 0;
                List<IArchiveEntry> allEntries;
                try
                {
                    allEntries = archive.Entries.ToList();

                    if (allEntries.Count == 0 && format == ArchiveFormat.Zip && ZipDeclaresEntries(archivePath))
                    {
                        // An unparseable central directory must not be reported as an empty success (F-10).
                        throw new ArchiveIntegrityException(
                            $"ZIP archive '{archivePath}' has an unparseable central directory: the end-of-central-directory record declares entries but none could be read.");
                    }

                    foreach (var e in allEntries)
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
                catch (ArchiveIntegrityException)
                {
                    throw;
                }
                catch (Exception ex) when (IsCorruptionException(ex))
                {
                    throw new ArchiveIntegrityException($"Archive '{archivePath}' is corrupted or unparseable: {ex.Message}", innerException: ex);
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

                return await SafeArchiveExtractor.ExtractAllAsync(
                    source,
                    destDir,
                    request.Overwrite,
                    totalBytes,
                    progress,
                    ct,
                    request.Limits,
                    totalIsEstimate: totalBytes >= 0);
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
                List<IArchiveEntry> entries;
                try
                {
                    entries = archive.Entries.ToList();
                }
                catch (Exception ex) when (IsCorruptionException(ex))
                {
                    throw new ArchiveIntegrityException($"Archive '{fullPath}' is corrupted or unparseable: {ex.Message}", innerException: ex);
                }

                if (entries.Count == 0 && format == ArchiveFormat.Zip && ZipDeclaresEntries(fullPath))
                {
                    // The end-of-central-directory record declares entries but none could be read:
                    // an unparseable central directory must not be reported as an empty success (F-10).
                    throw new ArchiveIntegrityException(
                        $"ZIP archive '{fullPath}' has an unparseable central directory: the end-of-central-directory record declares entries but none could be read.");
                }

                foreach (var entry in entries)
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
            catch (ArchiveIntegrityException)
            {
                throw;
            }
            catch (Exception ex) when (IsCorruptionException(ex))
            {
                throw new ArchiveIntegrityException($"Archive '{fullPath}' is corrupted or unparseable: {ex.Message}", innerException: ex);
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

    private static async Task<ExtractionResult> ExtractTarGzAsync(
        string archivePath,
        string destinationDir,
        bool overwrite,
        ExtractionLimits? limits,
        IProgress<DomainProgressReport>? progress,
        CancellationToken ct)
    {
        var source = new TarGzExtractionSource(archivePath);
        return await SafeArchiveExtractor.ExtractAllAsync(
            source,
            destinationDir,
            overwrite,
            totalBytes: -1,
            progress,
            ct,
            limits);
    }

    private static async Task<ExtractionResult> ExtractGzAsync(
        string archivePath,
        string destinationDir,
        bool overwrite,
        ExtractionLimits? limits,
        IProgress<DomainProgressReport>? progress,
        CancellationToken ct)
    {
        var source = new GzExtractionSource(archivePath);
        return await SafeArchiveExtractor.ExtractAllAsync(
            source,
            destinationDir,
            overwrite,
            totalBytes: -1,
            progress,
            ct,
            limits);
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
            List<IArchiveEntry> entries;
            try
            {
                entries = archive.Entries.ToList();
            }
            catch (Exception ex) when (IsCorruptionException(ex))
            {
                throw new ArchiveIntegrityException($"Archive '{archivePath}' is corrupted or unparseable: {ex.Message}", innerException: ex);
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
                            // CRC-32 verification applies to ZIP entries only: SharpCompress verifies
                            // RAR payload CRCs internally, and 7z entry.Crc is not populated in this
                            // version (returning 0), so a zip-style check would wrongly fail valid 7z.
                            if (archive.Type == ArchiveType.Zip)
                            {
                                return ValueTask.FromResult<Stream>(new ZipCrcVerifyingStream(
                                    stream,
                                    archivePath,
                                    entry.Key ?? string.Empty,
                                    unchecked((uint)entry.Crc)));
                            }

                            return ValueTask.FromResult(stream);
                        }
                        catch (Exception ex) when (IsCorruptionException(ex))
                        {
                            throw new ArchiveIntegrityException(
                                $"ZIP entry '{entry.Key}' in '{archivePath}' is corrupted: {ex.Message}",
                                entry.Key,
                                ex);
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

    /// <summary>
    /// True for exceptions that indicate structurally corrupt or unparseable archive data
    /// (as opposed to password/encryption issues, cancellation, or security violations).
    /// </summary>
    private static bool IsCorruptionException(Exception ex)
    {
        return ex is not OperationCanceledException
            && ex is not SecurityException
            && ex is not NotSupportedException
            && !IsPasswordOrEncryptedException(ex)
            && (ex is SharpCompress.Common.SharpCompressException or InvalidDataException or EndOfStreamException);
    }

    /// <summary>
    /// Parses the end-of-central-directory record of a ZIP archive to determine whether it declares
    /// any entries. Returns <c>false</c> for genuinely-empty zips (EOCD with a zero entry count and a
    /// zero central-directory size) or when the EOCD cannot be located. Used to distinguish a legal
    /// empty archive from an archive whose central directory is unparseable and silently yields zero
    /// entries (F-10).
    /// </summary>
    private static bool ZipDeclaresEntries(string archivePath)
    {
        try
        {
            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = fs.Length;
            if (length < 22)
                return false;

            int scanLength = (int)Math.Min(length, 22 + ushort.MaxValue); // EOCD + up to 64 KB comment
            fs.Seek(-scanLength, SeekOrigin.End);
            var tail = new byte[scanLength];
            int offset = 0;
            while (offset < scanLength)
            {
                int n = fs.Read(tail, offset, scanLength - offset);
                if (n <= 0)
                    break;
                offset += n;
            }

            // EOCD signature: PK\x05\x06 — scan backwards for the last occurrence.
            for (int i = tail.Length - 22; i >= 0; i--)
            {
                if (tail[i] == 0x50 && tail[i + 1] == 0x4B && tail[i + 2] == 0x05 && tail[i + 3] == 0x06)
                {
                    int totalEntries = tail[i + 10] | (tail[i + 11] << 8);
                    uint centralDirSize = BitConverter.ToUInt32(tail, i + 12);
                    // 0xFFFF/0xFFFFFFFF are zip64 sentinels — the real counts live elsewhere and a
                    // non-trivial zip is declared, so treat it as declaring entries.
                    return totalEntries != 0 || centralDirSize != 0;
                }
            }
        }
        catch
        {
            // If the EOCD cannot be read, fall back to the other parse-error paths.
        }

        return false;
    }

    /// <summary>
    /// Incremental CRC-32 (IEEE 802.3, polynomial 0xEDB88320) computed over decompressed entry bytes.
    /// </summary>
    private sealed class Crc32
    {
        private static readonly uint[] Table = BuildTable();
        private uint _value = 0xFFFFFFFF;

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }
                table[i] = c;
            }
            return table;
        }

        public void Append(ReadOnlySpan<byte> data)
        {
            uint crc = _value;
            foreach (byte b in data)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }
            _value = crc;
        }

        public uint Value => ~_value;
    }

    /// <summary>
    /// Wraps a per-entry zip decompression stream, computing CRC-32 as bytes flow through and verifying
    /// the result against the central-directory CRC when the entry stream reaches EOF. A mismatch (or a
    /// decompression failure) surfaces as an <see cref="ArchiveIntegrityException"/> so the generic
    /// <see cref="SafeArchiveExtractor"/> cleans up the partial file and the CLI exits 1 (F-09).
    /// Verification happens while streaming — there is no second read pass.
    /// </summary>
    private sealed class ZipCrcVerifyingStream : Stream
    {
        private readonly Stream _inner;
        private readonly string _archivePath;
        private readonly string _entryName;
        private readonly uint _expectedCrc;
        private readonly Crc32 _crc32 = new();
        private bool _eofReached;

        public ZipCrcVerifyingStream(Stream inner, string archivePath, string entryName, uint expectedCrc)
        {
            _inner = inner;
            _archivePath = archivePath;
            _entryName = entryName;
            _expectedCrc = expectedCrc;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read;
            try
            {
                read = _inner.Read(buffer, offset, count);
            }
            catch (Exception ex) when (IsCorruptionException(ex))
            {
                throw new ArchiveIntegrityException($"ZIP entry '{_entryName}' in '{_archivePath}' is corrupted: {ex.Message}", _entryName, ex);
            }

            if (read > 0)
            {
                _crc32.Append(buffer.AsSpan(offset, read));
                return read;
            }

            VerifyAtEof();
            return 0;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            int read;
            try
            {
                read = await _inner.ReadAsync(buffer, ct);
            }
            catch (Exception ex) when (IsCorruptionException(ex))
            {
                throw new ArchiveIntegrityException($"ZIP entry '{_entryName}' in '{_archivePath}' is corrupted: {ex.Message}", _entryName, ex);
            }

            if (read > 0)
            {
                _crc32.Append(buffer.Span[..read]);
                return read;
            }

            VerifyAtEof();
            return 0;
        }

        private void VerifyAtEof()
        {
            if (_eofReached)
                return;

            _eofReached = true;
            uint computed = _crc32.Value;
            if (computed != _expectedCrc)
            {
                throw new ArchiveIntegrityException(
                    $"CRC-32 mismatch for entry '{_entryName}' in '{_archivePath}': expected {_expectedCrc:X8}, computed {computed:X8}",
                    _entryName);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
