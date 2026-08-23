using System.Buffers;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
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

        if (request.SourcePaths is null or { Count: 0 })
        {
            throw new ArgumentException("At least one source path must be specified.", nameof(request));
        }

        // Validate all sources upfront before creating any directories or temporary files
        var resolvedSources = new List<(string FullPath, string RawPath, bool IsDir)>();
        foreach (var raw in request.SourcePaths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new ArgumentException("Source path cannot be empty.");
            }

            var fullPath = !string.IsNullOrEmpty(request.BaseDirectory) && !Path.IsPathRooted(raw)
                ? Path.GetFullPath(Path.Combine(request.BaseDirectory, raw))
                : Path.GetFullPath(raw);

            var isDir = Directory.Exists(fullPath);
            var isFile = File.Exists(fullPath);

            if (!isDir && !isFile)
            {
                throw new FileNotFoundException($"Source path does not exist: {raw}", fullPath);
            }

            resolvedSources.Add((fullPath, raw, isDir));
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
                var modesByPath = new Dictionary<string, ushort>(StringComparer.Ordinal);

                // Calculate total uncompressed bytes & total files
                long totalBytes = 0;
                int totalFiles = 0;

                foreach (var (fullPath, _, isDir) in resolvedSources)
                {
                    if (isDir)
                    {
                        var files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
                        totalFiles += files.Length;
                        totalBytes += files.Sum(f => new FileInfo(f).Length);
                    }
                    else
                    {
                        totalFiles += 1;
                        totalBytes += new FileInfo(fullPath).Length;
                    }
                }

                long processedBytes = 0;
                int processedFiles = 0;
                var isSingleDir = resolvedSources.Count == 1 && resolvedSources[0].IsDir;

                await using (var outputStream = new FileStream(
                    tempOutput,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    useAsync: true))
                {
                    var compressionType = request.CompressionLevel == 0 ? SharpCompress.Common.CompressionType.None : SharpCompress.Common.CompressionType.Deflate;
                    // UTF-8 entry-name encoding: SharpCompress sets the EFS bit (bit 11) on both the
                    // local and central headers only when GetEncoding() equals Encoding.UTF8. The
                    // default ArchiveEncoding.Default is Encoding.Default, which is NOT object-equal to
                    // Encoding.UTF8, so the EFS flag is never set (F-12). Point Default at Encoding.UTF8
                    // so GetEncoding() returns UTF-8 (EFS bit set) and Encode() emits UTF-8 name bytes —
                    // third-party readers (python zipfile, unzip) then decode names correctly.
                    var writerOptions = new ZipWriterOptions(compressionType)
                    {
                        UseZip64 = true,
                        LeaveStreamOpen = false,
                        ArchiveEncoding = new ArchiveEncoding
                        {
                            Default = Encoding.UTF8,
                            UTF8 = Encoding.UTF8
                        }
                    };

                    using var zipWriter = new ZipWriter(outputStream, writerOptions);

                    foreach (var (fullPath, rawPath, isDir) in resolvedSources)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (isDir)
                        {
                            var rootDirInfo = new DirectoryInfo(fullPath);
                            var dirPrefix = isSingleDir
                                ? string.Empty
                                : EntryNameSanitizer.SanitizeRelativePath(rawPath, fullPath, request.BaseDirectory);

                            if (!string.IsNullOrEmpty(dirPrefix))
                            {
                                if (TryGetZipMode16(rootDirInfo.FullName, isDirectory: true) is ushort dirMode)
                                {
                                    modesByPath[dirPrefix] = dirMode;
                                }
                                zipWriter.WriteDirectory(dirPrefix, rootDirInfo.LastWriteTimeUtc);
                            }

                            var fileEntries = rootDirInfo.GetFiles("*", SearchOption.AllDirectories);
                            var emptyDirs = rootDirInfo.GetDirectories("*", SearchOption.AllDirectories)
                                .Where(d => d.GetFileSystemInfos().Length == 0)
                                .ToList();

                            foreach (var emptyDir in emptyDirs)
                            {
                                ct.ThrowIfCancellationRequested();
                                var relativeFromDir = Path.GetRelativePath(rootDirInfo.FullName, emptyDir.FullName).Replace('\\', '/');
                                var relPath = string.IsNullOrEmpty(dirPrefix)
                                    ? relativeFromDir
                                    : dirPrefix + "/" + relativeFromDir;

                                if (TryGetZipMode16(emptyDir.FullName, isDirectory: true) is ushort dirMode)
                                {
                                    modesByPath[relPath] = dirMode;
                                }
                                zipWriter.WriteDirectory(relPath, emptyDir.LastWriteTimeUtc);
                            }

                            foreach (var fileInfo in fileEntries)
                            {
                                ct.ThrowIfCancellationRequested();
                                var relativeFromDir = Path.GetRelativePath(rootDirInfo.FullName, fileInfo.FullName).Replace('\\', '/');
                                var relPath = string.IsNullOrEmpty(dirPrefix)
                                    ? relativeFromDir
                                    : dirPrefix + "/" + relativeFromDir;

                                if (TryGetZipMode16(fileInfo.FullName, isDirectory: false) is ushort fileMode)
                                {
                                    modesByPath[relPath] = fileMode;
                                }

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
                            var fileInfo = new FileInfo(fullPath);
                            var relPath = EntryNameSanitizer.SanitizeRelativePath(rawPath, fullPath, request.BaseDirectory);
                            if (TryGetZipMode16(fileInfo.FullName, isDirectory: false) is ushort fileMode)
                            {
                                modesByPath[relPath] = fileMode;
                            }

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
                }

                // SharpCompress's ZipWriter does not expose per-entry external attributes, so after the
                // archive is fully written we patch the central directory in place: set the "version made
                // by" upper byte to 3 (Unix) and store each source file's POSIX mode in the external
                // attributes field (F-13 write side). Best-effort: a failure here never fails compression.
                if (modesByPath.Count > 0)
                {
                    PatchZipExternalAttributes(tempOutput, modesByPath);
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

    public Task<AppendResult> AppendAsync(
        ArchiveAppendRequest request,
        IProgress<DomainProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var descriptor = ArchiveFormatRegistry.Detect(request.ArchivePath);
        throw new NotSupportedException($"Appending to '{descriptor.Format}' archive format is not supported.");
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
            return await ExtractTarGzAsync(archivePath, destDir, request.Overwrite, request.Limits, request.Entries, progress, ct);
        }

        if (format == ArchiveFormat.Gz)
        {
            return await ExtractGzAsync(archivePath, destDir, request.Overwrite, request.Limits, request.Entries, progress, ct);
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
                        // With a selective-extraction filter, entries outside the filter are ignored
                        // entirely — an unrelated encrypted entry cannot block a filtered extraction.
                        if (!EntryFilter.IsMatch(e.Key ?? string.Empty, request.Entries))
                            continue;

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

                var source = new SharpCompressExtractionSource(archive, archivePath, request.Entries);

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
        IReadOnlyList<string>? entries,
        IProgress<DomainProgressReport>? progress,
        CancellationToken ct)
    {
        var source = new TarGzExtractionSource(archivePath, entries);
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
        IReadOnlyList<string>? entries,
        IProgress<DomainProgressReport>? progress,
        CancellationToken ct)
    {
        var source = new GzExtractionSource(archivePath, entries);
        return await SafeArchiveExtractor.ExtractAllAsync(
            source,
            destinationDir,
            overwrite,
            totalBytes: -1,
            progress,
            ct,
            limits);
    }

    private sealed class TarGzExtractionSource(string archivePath, IReadOnlyList<string>? entryFilter) : IArchiveExtractionSource
    {
        public async IAsyncEnumerable<ExtractionEntry> ReadEntriesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, SafeArchiveExtractor.BufferSize, useAsync: true);
            await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            await using var tarReader = new TarReader(gzipStream, leaveOpen: false);

            bool matchedAny = false;
            TarEntry? entry;
            while ((entry = await tarReader.GetNextEntryAsync(copyData: false, ct)) is not null)
            {
                // Selective extraction: entries outside the filter are skipped entirely (the tar
                // reader auto-skips a non-requested entry's data on the next GetNextEntryAsync call).
                if (entryFilter is { Count: > 0 } && !EntryFilter.IsMatch(entry.Name, entryFilter))
                    continue;

                matchedAny = true;
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

            // A selective filter that matched nothing is a clear error, not a silent zero-entry success.
            if (entryFilter is { Count: > 0 } && !matchedAny)
            {
                throw new InvalidOperationException(EntryFilter.NoMatchMessage);
            }
        }
    }

    private sealed class GzExtractionSource(string archivePath, IReadOnlyList<string>? entryFilter) : IArchiveExtractionSource
    {
        public async IAsyncEnumerable<ExtractionEntry> ReadEntriesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            var outFileName = Path.GetFileNameWithoutExtension(archivePath);
            var fileInfo = new FileInfo(archivePath);

            // A .gz archive is a single decompressed file. The filter must match its entry name
            // exactly (or via a directory prefix, which never applies to a single file).
            if (entryFilter is { Count: > 0 } && !EntryFilter.IsMatch(outFileName, entryFilter))
            {
                throw new InvalidOperationException(EntryFilter.NoMatchMessage);
            }

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

    private sealed class SharpCompressExtractionSource(IArchive archive, string archivePath, IReadOnlyList<string>? entryFilter) : IArchiveExtractionSource
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

            // ZIP entry names encode their POSIX mode in the central-directory external attributes, but
            // only when the entry was created on Unix ("version made by" upper byte == 3). SharpCompress
            // does not surface the version byte, so parse the central directory once to recover modes
            // (F-13 read side). RAR/7z never carry Unix modes this way, so skip them.
            Dictionary<string, UnixFileMode?>? zipUnixModes = null;
            if (archive.Type == ArchiveType.Zip)
            {
                zipUnixModes = ParseZipEntryModes(archivePath);
            }

            bool matchedAny = false;
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                var key = entry.Key;
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                // Selective extraction: entries outside the filter are skipped entirely, so an
                // unrelated encrypted entry cannot block extraction of a selected subset.
                if (entryFilter is { Count: > 0 } && !EntryFilter.IsMatch(key, entryFilter))
                    continue;

                if (entry.IsEncrypted)
                {
                    throw new NotSupportedException($"The entry '{entry.Key}' is password-protected. Encrypted archives are not supported.");
                }

                matchedAny = true;
                bool isDir = entry.IsDirectory || key.Replace('\\', '/').EndsWith('/');
                DateTimeOffset? modTime = entry.LastModifiedTime.HasValue ? new DateTimeOffset(entry.LastModifiedTime.Value.ToUniversalTime()) : null;

                UnixFileMode? unixMode = null;
                if (zipUnixModes is not null && zipUnixModes.TryGetValue(key, out var parsedMode))
                {
                    unixMode = parsedMode;
                }

                yield return new ExtractionEntry(
                    RelativePath: key,
                    IsDirectory: isDir,
                    UncompressedSize: entry.Size,
                    ModificationTime: modTime,
                    UnixMode: unixMode,
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

            // A selective filter that matched nothing is a clear error, not a silent zero-entry success.
            if (entryFilter is { Count: > 0 } && !matchedAny)
            {
                throw new InvalidOperationException(EntryFilter.NoMatchMessage);
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

    private sealed record ZipCentralDirectoryRecord(string Name, ushort VersionMadeBy, uint ExternalFileAttributes, long RecordFileOffset);

    /// <summary>
    /// Reads a file's POSIX mode and encodes it as a 16-bit zip external-attribute value:
    /// the file-type bits (S_IFREG / S_IFDIR) in the upper nibble plus the 12 permission/special
    /// bits. Returns <see langword="null"/> on platforms or filesystems without POSIX modes so the
    /// caller can skip mode storage (best-effort on Windows).
    /// </summary>
    private static ushort? TryGetZipMode16(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows())
            return null;

        try
        {
            var mode = File.GetUnixFileMode(path);
            ushort fileType = (ushort)(isDirectory ? 0x4000 : 0x8000); // S_IFDIR : S_IFREG
            return (ushort)(fileType | ((int)mode & 0x0FFF));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Post-processes a finished zip archive, patching each central-directory entry that appears in
    /// <paramref name="modeByPath"/>: the "version made by" upper byte is set to 3 (Unix) and the
    /// external-attributes field is set to <c>(mode16 &lt;&lt; 16) | dosAttrs</c>. This is how POSIX modes
    /// are stored in zip — SharpCompress's <see cref="ZipWriter"/> offers no per-entry API for it (F-13
    /// write side). Best-effort: any failure leaves the archive as-written and never fails compression.
    /// </summary>
    private static void PatchZipExternalAttributes(string zipPath, IReadOnlyDictionary<string, ushort> modeByPath)
    {
        if (modeByPath.Count == 0)
            return;

        List<ZipCentralDirectoryRecord> records;
        try
        {
            records = ParseZipCentralDirectory(zipPath);
        }
        catch
        {
            return;
        }
        if (records.Count == 0)
            return;

        try
        {
            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Span<byte> buf = stackalloc byte[4];
            foreach (var rec in records)
            {
                if (!modeByPath.TryGetValue(rec.Name, out ushort mode16))
                    continue;

                // version-made-by high byte = 3 (Unix) tells readers the external attributes hold a mode.
                ushort newVersionMadeBy = (ushort)((rec.VersionMadeBy & 0x00FF) | (3 << 8));
                fs.Position = rec.RecordFileOffset + 4;
                fs.WriteByte((byte)(newVersionMadeBy & 0xFF));
                fs.WriteByte((byte)(newVersionMadeBy >> 8));

                // Upper 16 bits = (fileType | permissions); low byte keeps DOS attributes (archive/dir bit).
                bool isDir = (mode16 & 0xF000) == 0x4000;
                uint newExternalAttr = ((uint)mode16 << 16) | (rec.ExternalFileAttributes & 0xFF) | (isDir ? 0x10u : 0x20u);
                fs.Position = rec.RecordFileOffset + 38;
                buf[0] = (byte)(newExternalAttr & 0xFF);
                buf[1] = (byte)((newExternalAttr >> 8) & 0xFF);
                buf[2] = (byte)((newExternalAttr >> 16) & 0xFF);
                buf[3] = (byte)((newExternalAttr >> 24) & 0xFF);
                fs.Write(buf);
            }
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>
    /// Parses the central directory of a zip archive into per-entry records (name, version-made-by,
    /// external attributes, and the record's absolute file offset). Handles both classic EOCD records
    /// and the zip64 EOCD locator form. Returns an empty list if the directory cannot be parsed.
    /// </summary>
    private static List<ZipCentralDirectoryRecord> ParseZipCentralDirectory(string archivePath)
    {
        var result = new List<ZipCentralDirectoryRecord>();
        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = fs.Length;
        if (length < 22)
            return result;

        int scanLength = (int)Math.Min(length, 22 + ushort.MaxValue); // EOCD + up to 64 KB comment
        fs.Seek(-scanLength, SeekOrigin.End);
        var tail = new byte[scanLength];
        if (!ReadExactly(fs, tail))
            return result;

        // EOCD signature: PK\x05\x06 — scan backwards for the last occurrence.
        int eocdIndex = -1;
        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (tail[i] == 0x50 && tail[i + 1] == 0x4B && tail[i + 2] == 0x05 && tail[i + 3] == 0x06)
            {
                eocdIndex = i;
                break;
            }
        }
        if (eocdIndex < 0)
            return result;

        long eocdOffset = length - scanLength + eocdIndex;
        ushort totalEntries = (ushort)(tail[eocdIndex + 10] | (tail[eocdIndex + 11] << 8));
        uint cdSize = BitConverter.ToUInt32(tail, eocdIndex + 12);
        uint cdOffset = BitConverter.ToUInt32(tail, eocdIndex + 16);

        if (totalEntries == ushort.MaxValue || cdSize == uint.MaxValue || cdOffset == uint.MaxValue)
        {
            // Zip64 sentinels — the real values live in the zip64 EOCD, located via the locator that
            // sits immediately before the EOCD (signature PK\x06\x07).
            long locatorOffset = eocdOffset - 20;
            if (locatorOffset >= 0)
            {
                fs.Position = locatorOffset;
                var locator = new byte[20];
                if (ReadExactly(fs, locator) && locator[0] == 0x50 && locator[1] == 0x4B && locator[2] == 0x06 && locator[3] == 0x07)
                {
                    ulong z64EocdOffset = BitConverter.ToUInt64(locator, 8);
                    fs.Position = (long)z64EocdOffset;
                    var z64 = new byte[56];
                    if (ReadExactly(fs, z64))
                    {
                        // Zip64 EOCD: sig(4) size(8) verMade(2) verNeeded(2) disk(4) cdStart(4)
                        //              entriesOnDisk(8) entries(8) cdSize(8) cdOffset(8)
                        ulong entries = BitConverter.ToUInt64(z64, 32);
                        ulong z64CdSize = BitConverter.ToUInt64(z64, 40);
                        ulong z64CdOffset = BitConverter.ToUInt64(z64, 48);
                        if (entries > 0 || z64CdSize > 0)
                        {
                            totalEntries = entries > ushort.MaxValue ? ushort.MaxValue : (ushort)entries;
                            cdSize = z64CdSize > uint.MaxValue ? uint.MaxValue : (uint)z64CdSize;
                            cdOffset = z64CdOffset > uint.MaxValue ? uint.MaxValue : (uint)z64CdOffset;
                        }
                    }
                }
            }
        }

        if (cdSize == 0)
            return result;

        fs.Position = cdOffset;
        var cdBytes = new byte[cdSize];
        if (!ReadExactly(fs, cdBytes))
            return result;

        int pos = 0;
        while (pos + 46 <= cdBytes.Length &&
               cdBytes[pos] == 0x50 && cdBytes[pos + 1] == 0x4B && cdBytes[pos + 2] == 0x01 && cdBytes[pos + 3] == 0x02)
        {
            ushort versionMadeBy = (ushort)(cdBytes[pos + 4] | (cdBytes[pos + 5] << 8));
            ushort nameLen = (ushort)(cdBytes[pos + 28] | (cdBytes[pos + 29] << 8));
            ushort extraLen = (ushort)(cdBytes[pos + 30] | (cdBytes[pos + 31] << 8));
            ushort commentLen = (ushort)(cdBytes[pos + 32] | (cdBytes[pos + 33] << 8));
            uint externalAttr = BitConverter.ToUInt32(cdBytes, pos + 38);
            string name = Encoding.UTF8.GetString(cdBytes, pos + 46, nameLen);

            result.Add(new ZipCentralDirectoryRecord(name, versionMadeBy, externalAttr, cdOffset + pos));

            pos += 46 + nameLen + extraLen + commentLen;
            if (pos > cdBytes.Length)
                break;
        }

        return result;
    }

    /// <summary>
    /// Builds a map of zip entry name → POSIX mode for extraction. Only entries whose "version made by"
    /// upper byte is 3 (Unix) carry a mode in their external attributes; everything else falls back to
    /// default permissions. Returns an empty map on any parse failure so extraction still succeeds.
    /// </summary>
    private static Dictionary<string, UnixFileMode?> ParseZipEntryModes(string archivePath)
    {
        var result = new Dictionary<string, UnixFileMode?>(StringComparer.Ordinal);
        try
        {
            foreach (var rec in ParseZipCentralDirectory(archivePath))
            {
                if ((rec.VersionMadeBy >> 8) != 3)
                    continue;

                uint perms = (rec.ExternalFileAttributes >> 16) & 0x0FFF;
                result[rec.Name] = perms != 0 ? (UnixFileMode)perms : null;
            }
        }
        catch
        {
            // Mode restoration is best-effort; a malformed central directory must not block extraction.
        }
        return result;
    }

    /// <summary>Reads exactly <paramref name="buffer"/>.Length bytes, returning false on EOF.</summary>
    private static bool ReadExactly(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int n = stream.Read(buffer, offset, buffer.Length - offset);
            if (n <= 0)
                return false;
            offset += n;
        }
        return true;
    }

    /// <summary>
    /// Determines whether <paramref name="ex"/> indicates an encrypted or password-protected archive
    /// (as opposed to corruption, cancellation, or a security violation).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Typed signals are checked first. SharpCompress raises <see cref="SharpCompress.Common.CryptographicException"/>
    /// for AES-encrypted 7z/RAR5 archives opened without a password, and
    /// <see cref="System.Security.Cryptography.CryptographicException"/> surfaces from the underlying crypto
    /// primitives when decryption fails.
    /// </para>
    /// <para>
    /// SharpCompress does not surface a typed signal for every encrypted-archive scenario, so a narrow
    /// message/stack fallback is retained (F-18): a missing crypto-info block can surface as a bare
    /// <see cref="ArgumentNullException"/> from its <c>DecoderRegistry</c>, and the password path is the
    /// only place <c>IPasswordProvider</c> appears in a stack. The patterns are deliberately specific so
    /// an unrelated exception whose message merely contains "password" or "encrypted" is never
    /// misclassified as an unsupported encrypted archive.
    /// </para>
    /// </remarks>
    internal static bool IsPasswordOrEncryptedException(Exception ex)
    {
        // 1. Typed signals first.
        if (ex is SharpCompress.Common.CryptographicException
            or System.Security.Cryptography.CryptographicException)
        {
            return true;
        }

        // 2. Narrow fallback for cases where SharpCompress lacks a typed signal (see remarks).
        if (ex is ArgumentNullException
            && ex.StackTrace?.Contains("DecoderRegistry", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (ex.StackTrace?.Contains("IPasswordProvider", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (ex.Message.Contains("no password specified", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. Recurse into the inner chain — SharpCompress often wraps the root cause.
        return ex.InnerException is not null && IsPasswordOrEncryptedException(ex.InnerException);
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
