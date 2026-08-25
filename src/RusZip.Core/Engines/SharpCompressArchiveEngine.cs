using System.Buffers;
using System.Diagnostics;
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
        var isMultiVolume = request.SplitSizeBytes.HasValue && request.SplitSizeBytes.Value > 0;

        if (!string.IsNullOrEmpty(request.Password))
        {
            await CompressWinZipAesAsync(request, resolvedSources, tempOutput, progress, ct);
            return;
        }

        try
        {
            await Task.Run(async () =>
            {
                var isSingleDir = resolvedSources.Count == 1 && resolvedSources[0].IsDir;
                var exclusionFilter = new CompressionExclusionFilter(request.ExcludedPaths, request.BaseDirectory);
                var modesByPath = new Dictionary<string, ushort>(StringComparer.Ordinal);

                // Calculate total uncompressed bytes & total files
                long totalBytes = 0;
                int totalFiles = 0;

                void CountFiles(DirectoryInfo dir, string rootPath, string dirPrefix)
                {
                    foreach (var subDir in dir.EnumerateDirectories())
                    {
                        var relativeFromDir = Path.GetRelativePath(rootPath, subDir.FullName).Replace('\\', '/');
                        var relPath = string.IsNullOrEmpty(dirPrefix) ? relativeFromDir : dirPrefix + "/" + relativeFromDir;
                        if (!exclusionFilter.IsExcluded(subDir.FullName, relPath, relativeFromDir))
                        {
                            CountFiles(subDir, rootPath, dirPrefix);
                        }
                    }

                    foreach (var fileInfo in dir.EnumerateFiles())
                    {
                        var relativeFromDir = Path.GetRelativePath(rootPath, fileInfo.FullName).Replace('\\', '/');
                        var relPath = string.IsNullOrEmpty(dirPrefix) ? relativeFromDir : dirPrefix + "/" + relativeFromDir;
                        if (!exclusionFilter.IsExcluded(fileInfo.FullName, relPath, relativeFromDir))
                        {
                            totalFiles++;
                            totalBytes += fileInfo.Length;
                        }
                    }
                }

                foreach (var (fullPath, rawPath, isDir) in resolvedSources)
                {
                    if (isDir)
                    {
                        var rootDirInfo = new DirectoryInfo(fullPath);
                        var dirPrefix = isSingleDir
                            ? string.Empty
                            : EntryNameSanitizer.SanitizeRelativePath(rawPath, fullPath, request.BaseDirectory);

                        if (exclusionFilter.IsExcluded(fullPath, dirPrefix, rawPath))
                        {
                            continue;
                        }

                        CountFiles(rootDirInfo, rootDirInfo.FullName, dirPrefix);
                    }
                    else
                    {
                        var relPath = EntryNameSanitizer.SanitizeRelativePath(rawPath, fullPath, request.BaseDirectory);
                        if (!exclusionFilter.IsExcluded(fullPath, relPath, rawPath))
                        {
                            totalFiles += 1;
                            totalBytes += new FileInfo(fullPath).Length;
                        }
                    }
                }

                long processedBytes = 0;
                int processedFiles = 0;

                Stream outputStream;
                if (isMultiVolume)
                {
                    if (request.SplitSizeBytes!.Value < MultiVolumeWriteStream.MinimumVolumeBytes)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(request.SplitSizeBytes),
                            request.SplitSizeBytes.Value,
                            $"Split volume size must be at least {MultiVolumeWriteStream.MinimumVolumeBytes:N0} bytes (64 KB).");
                    }
                    outputStream = new MultiVolumeWriteStream(destination, request.SplitSizeBytes.Value);
                }
                else
                {
                    outputStream = new FileStream(
                        tempOutput,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        BufferSize,
                        useAsync: true);
                }

                await using (outputStream)
                {
                    var compressionType = request.CompressionLevel == 0 ? SharpCompress.Common.CompressionType.None : SharpCompress.Common.CompressionType.Deflate;
                    var writerOptions = new ZipWriterOptions(compressionType)
                    {
                        UseZip64 = !isMultiVolume,
                        LeaveStreamOpen = false,
                        ArchiveEncoding = new ArchiveEncoding
                        {
                            Default = Encoding.UTF8,
                            UTF8 = Encoding.UTF8
                        }
                    };

                    using var zipWriter = new ZipWriter(outputStream, writerOptions);

                    async Task WriteDirectoryTreeAsync(DirectoryInfo dir, string rootPath, string dirPrefix)
                    {
                        var nonExcludedSubDirs = new List<(DirectoryInfo Dir, string RelPath, string RelFromDir)>();
                        var nonExcludedFiles = new List<(FileInfo File, string RelPath, string RelFromDir)>();

                        foreach (var subDir in dir.EnumerateDirectories())
                        {
                            ct.ThrowIfCancellationRequested();
                            var relativeFromDir = Path.GetRelativePath(rootPath, subDir.FullName).Replace('\\', '/');
                            var relPath = string.IsNullOrEmpty(dirPrefix) ? relativeFromDir : dirPrefix + "/" + relativeFromDir;
                            if (!exclusionFilter.IsExcluded(subDir.FullName, relPath, relativeFromDir))
                            {
                                nonExcludedSubDirs.Add((subDir, relPath, relativeFromDir));
                            }
                        }

                        foreach (var fileInfo in dir.EnumerateFiles())
                        {
                            ct.ThrowIfCancellationRequested();
                            var relativeFromDir = Path.GetRelativePath(rootPath, fileInfo.FullName).Replace('\\', '/');
                            var relPath = string.IsNullOrEmpty(dirPrefix) ? relativeFromDir : dirPrefix + "/" + relativeFromDir;
                            if (!exclusionFilter.IsExcluded(fileInfo.FullName, relPath, relativeFromDir))
                            {
                                nonExcludedFiles.Add((fileInfo, relPath, relativeFromDir));
                            }
                        }

                        if (nonExcludedSubDirs.Count == 0 && nonExcludedFiles.Count == 0)
                        {
                            var relativeFromDir = Path.GetRelativePath(rootPath, dir.FullName).Replace('\\', '/');
                            var dirRelPath = string.IsNullOrEmpty(dirPrefix) ? relativeFromDir : (relativeFromDir == "." ? dirPrefix : dirPrefix + "/" + relativeFromDir);
                            if (!string.IsNullOrEmpty(dirRelPath) && dirRelPath != ".")
                            {
                                if (TryGetZipMode16(dir.FullName, isDirectory: true) is ushort dirMode)
                                {
                                    modesByPath[dirRelPath] = dirMode;
                                }
                                zipWriter.WriteDirectory(dirRelPath, dir.LastWriteTimeUtc);
                            }
                        }

                        foreach (var (subDir, _, _) in nonExcludedSubDirs)
                        {
                            await WriteDirectoryTreeAsync(subDir, rootPath, dirPrefix);
                        }

                        foreach (var (fileInfo, relPath, _) in nonExcludedFiles)
                        {
                            ct.ThrowIfCancellationRequested();

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

                    foreach (var (fullPath, rawPath, isDir) in resolvedSources)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (isDir)
                        {
                            var rootDirInfo = new DirectoryInfo(fullPath);
                            var dirPrefix = isSingleDir
                                ? string.Empty
                                : EntryNameSanitizer.SanitizeRelativePath(rawPath, fullPath, request.BaseDirectory);

                            if (exclusionFilter.IsExcluded(fullPath, dirPrefix, rawPath))
                            {
                                continue;
                            }

                            if (!string.IsNullOrEmpty(dirPrefix))
                            {
                                if (TryGetZipMode16(rootDirInfo.FullName, isDirectory: true) is ushort dirMode)
                                {
                                    modesByPath[dirPrefix] = dirMode;
                                }
                                zipWriter.WriteDirectory(dirPrefix, rootDirInfo.LastWriteTimeUtc);
                            }

                            await WriteDirectoryTreeAsync(rootDirInfo, rootDirInfo.FullName, dirPrefix);
                        }
                        else
                        {
                            var fileInfo = new FileInfo(fullPath);
                            var relPath = EntryNameSanitizer.SanitizeRelativePath(rawPath, fullPath, request.BaseDirectory);

                            if (exclusionFilter.IsExcluded(fullPath, relPath, rawPath))
                            {
                                continue;
                            }

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
                if (modesByPath.Count > 0 && !isMultiVolume)
                {
                    PatchZipExternalAttributes(tempOutput, modesByPath);
                }

                if (!isMultiVolume)
                {
                    File.Move(tempOutput, destination, overwrite: true);
                }
            }, ct);
        }
        finally
        {
            if (!isMultiVolume && File.Exists(tempOutput))
            {
                try { File.Delete(tempOutput); } catch { /* Ignore */ }
            }
        }
    }

    private static async Task CompressWinZipAesAsync(
        ArchiveCompressionRequest request,
        List<(string FullPath, string RawPath, bool IsDir)> resolvedSources,
        string tempOutput,
        IProgress<DomainProgressReport>? progress,
        CancellationToken ct)
    {
        var isSingleDir = resolvedSources.Count == 1 && resolvedSources[0].IsDir;
        var exclusionFilter = new CompressionExclusionFilter(request.ExcludedPaths, request.BaseDirectory);
        var modesByPath = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var entriesToWrite = new List<(string RelPath, string? SourceFilePath, bool IsDir, DateTime LastModified)>();

        void CollectFiles(DirectoryInfo dir, string rootPath, string dirPrefix)
        {
            var nonExcludedSubDirs = new List<(DirectoryInfo Dir, string RelPath, string RelFromDir)>();
            var nonExcludedFiles = new List<(FileInfo File, string RelPath, string RelFromDir)>();

            foreach (var subDir in dir.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                var relativeFromDir = Path.GetRelativePath(rootPath, subDir.FullName).Replace('\\', '/');
                var relPath = string.IsNullOrEmpty(dirPrefix) ? relativeFromDir : dirPrefix + "/" + relativeFromDir;
                if (!exclusionFilter.IsExcluded(subDir.FullName, relPath, relativeFromDir))
                {
                    nonExcludedSubDirs.Add((subDir, relPath, relativeFromDir));
                }
            }

            foreach (var fileInfo in dir.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                var relativeFromDir = Path.GetRelativePath(rootPath, fileInfo.FullName).Replace('\\', '/');
                var relPath = string.IsNullOrEmpty(dirPrefix) ? relativeFromDir : dirPrefix + "/" + relativeFromDir;
                if (!exclusionFilter.IsExcluded(fileInfo.FullName, relPath, relativeFromDir))
                {
                    nonExcludedFiles.Add((fileInfo, relPath, relativeFromDir));
                }
            }

            if (nonExcludedSubDirs.Count == 0 && nonExcludedFiles.Count == 0)
            {
                var relativeFromDir = Path.GetRelativePath(rootPath, dir.FullName).Replace('\\', '/');
                var dirRelPath = string.IsNullOrEmpty(dirPrefix) ? relativeFromDir : (relativeFromDir == "." ? dirPrefix : dirPrefix + "/" + relativeFromDir);
                if (!string.IsNullOrEmpty(dirRelPath) && dirRelPath != ".")
                {
                    var normalized = dirRelPath.TrimEnd('/') + "/";
                    entriesToWrite.Add((normalized, null, true, dir.LastWriteTimeUtc));
                    if (TryGetZipMode16(dir.FullName, isDirectory: true) is ushort dirMode)
                    {
                        modesByPath[normalized] = dirMode;
                    }
                }
            }

            foreach (var (subDir, _, _) in nonExcludedSubDirs)
            {
                CollectFiles(subDir, rootPath, dirPrefix);
            }

            foreach (var (fileInfo, relPath, _) in nonExcludedFiles)
            {
                ct.ThrowIfCancellationRequested();
                entriesToWrite.Add((relPath, fileInfo.FullName, false, fileInfo.LastWriteTimeUtc));
                if (TryGetZipMode16(fileInfo.FullName, isDirectory: false) is ushort fileMode)
                {
                    modesByPath[relPath] = fileMode;
                }
            }
        }

        foreach (var (fullPath, rawPath, isDir) in resolvedSources)
        {
            ct.ThrowIfCancellationRequested();
            if (isDir)
            {
                var rootDirInfo = new DirectoryInfo(fullPath);
                var dirPrefix = isSingleDir ? string.Empty : EntryNameSanitizer.SanitizeRelativePath(rawPath, fullPath, request.BaseDirectory);
                if (exclusionFilter.IsExcluded(fullPath, dirPrefix, rawPath)) continue;
                if (!string.IsNullOrEmpty(dirPrefix))
                {
                    var normalized = dirPrefix.TrimEnd('/') + "/";
                    entriesToWrite.Add((normalized, null, true, rootDirInfo.LastWriteTimeUtc));
                    if (TryGetZipMode16(rootDirInfo.FullName, isDirectory: true) is ushort dirMode)
                    {
                        modesByPath[normalized] = dirMode;
                    }
                }
                CollectFiles(rootDirInfo, rootDirInfo.FullName, dirPrefix);
            }
            else
            {
                var fileInfo = new FileInfo(fullPath);
                var relPath = EntryNameSanitizer.SanitizeRelativePath(rawPath, fullPath, request.BaseDirectory);
                if (exclusionFilter.IsExcluded(fullPath, relPath, rawPath)) continue;
                entriesToWrite.Add((relPath, fileInfo.FullName, false, fileInfo.LastWriteTimeUtc));
                if (TryGetZipMode16(fileInfo.FullName, isDirectory: false) is ushort fileMode)
                {
                    modesByPath[relPath] = fileMode;
                }
            }
        }

        long totalBytes = entriesToWrite.Where(e => !e.IsDir && e.SourceFilePath != null).Sum(e => new FileInfo(e.SourceFilePath!).Length);
        int totalFiles = entriesToWrite.Count(e => !e.IsDir);
        long processedBytes = 0;
        int processedFiles = 0;

        var compLevel = request.CompressionLevel switch
        {
            0 => CompressionLevel.NoCompression,
            <= 4 => CompressionLevel.Fastest,
            <= 7 => CompressionLevel.Optimal,
            _ => CompressionLevel.SmallestSize
        };

        Stream fs;
        MultiVolumeWriteStream? multiVolumeStream = null;
        if (request.SplitSizeBytes.HasValue && request.SplitSizeBytes.Value > 0)
        {
            if (request.SplitSizeBytes.Value < MultiVolumeWriteStream.MinimumVolumeBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.SplitSizeBytes),
                    request.SplitSizeBytes.Value,
                    $"Split volume size must be at least {MultiVolumeWriteStream.MinimumVolumeBytes:N0} bytes (64 KB).");
            }
            multiVolumeStream = new MultiVolumeWriteStream(request.DestinationArchivePath, request.SplitSizeBytes.Value);
            fs = multiVolumeStream;
        }
        else
        {
            fs = new FileStream(tempOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        }

        await using (fs)
        {
            using var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);

            var centralDirectoryHeaders = new List<byte[]>();

            foreach (var (relPath, sourceFilePath, isDir, lastModified) in entriesToWrite)
            {
                ct.ThrowIfCancellationRequested();
                var nameBytes = Encoding.UTF8.GetBytes(relPath);
                long localHeaderPos = fs.Position;

                ushort time = (ushort)((lastModified.Hour << 11) | (lastModified.Minute << 5) | (lastModified.Second / 2));
                ushort date = (ushort)(((lastModified.Year - 1980) << 9) | (lastModified.Month << 5) | lastModified.Day);

                if (isDir)
                {
                    // Write directory entry (uncompressed, no encryption)
                    bw.Write(0x04034b50); // local header signature
                    bw.Write((short)20);   // version needed (2.0)
                    bw.Write((short)0x0800); // flags: UTF-8
                    bw.Write((short)0);    // compression: stored
                    bw.Write(time);
                    bw.Write(date);
                    bw.Write((int)0);      // CRC-32
                    bw.Write((int)0);      // compressed size
                    bw.Write((int)0);      // uncompressed size
                    bw.Write((short)nameBytes.Length);
                    bw.Write((short)0);    // extra length
                    bw.Write(nameBytes);

                    // Build central directory record
                    using var cdMs = new MemoryStream();
                    using var cdBw = new BinaryWriter(cdMs, Encoding.UTF8);
                    cdBw.Write(0x02014b50); // CD signature
                    cdBw.Write((short)0x0314); // Version made by: Unix (3) + 2.0 (20)
                    cdBw.Write((short)20);   // Version needed (20)
                    cdBw.Write((short)0x0800); // flags: UTF-8
                    cdBw.Write((short)0);    // compression
                    cdBw.Write(time);
                    cdBw.Write(date);
                    cdBw.Write((int)0);      // CRC
                    cdBw.Write((int)0);      // compressed size
                    cdBw.Write((int)0);      // uncompressed size
                    cdBw.Write((short)nameBytes.Length);
                    cdBw.Write((short)0);    // extra length
                    cdBw.Write((short)0);    // comment length
                    cdBw.Write((short)0);    // disk number
                    cdBw.Write((short)0);    // internal attrs
                    uint extAttrs = (0x41EDu << 16) | 0x10u; // Directory attributes
                    cdBw.Write(extAttrs);
                    cdBw.Write((int)localHeaderPos);
                    cdBw.Write(nameBytes);
                    centralDirectoryHeaders.Add(cdMs.ToArray());
                }
                else
                {
                    byte[] fileBytes = await File.ReadAllBytesAsync(sourceFilePath!, ct);
                    byte[] encryptedPayload = WinZipAesCrypto.EncryptPayload(fileBytes, request.Password!, compLevel);

                    bw.Write(0x04034b50); // local header signature
                    bw.Write((short)51);   // version needed (5.1 for AES)
                    bw.Write((short)(0x0800 | 1)); // flags: UTF-8 + encrypted
                    bw.Write(WinZipAesCrypto.WinZipAesCompressionMethod); // 99
                    bw.Write(time);
                    bw.Write(date);
                    bw.Write((int)0);      // CRC-32 (0 for AE-2)
                    bw.Write(encryptedPayload.Length);
                    bw.Write(fileBytes.Length);
                    bw.Write((short)nameBytes.Length);
                    bw.Write((short)WinZipAesCrypto.WinZipAesExtraField.Length);
                    bw.Write(nameBytes);
                    bw.Write(WinZipAesCrypto.WinZipAesExtraField);
                    bw.Write(encryptedPayload);

                    // Build central directory record
                    using var cdMs = new MemoryStream();
                    using var cdBw = new BinaryWriter(cdMs, Encoding.UTF8);
                    cdBw.Write(0x02014b50); // CD signature
                    cdBw.Write((short)0x0333); // Version made by: Unix (3) + 5.1 (51)
                    cdBw.Write((short)51);   // Version needed (51)
                    cdBw.Write((short)(0x0800 | 1)); // flags: UTF-8 + encrypted
                    cdBw.Write(WinZipAesCrypto.WinZipAesCompressionMethod); // 99
                    cdBw.Write(time);
                    cdBw.Write(date);
                    cdBw.Write((int)0);      // CRC
                    cdBw.Write(encryptedPayload.Length);
                    cdBw.Write(fileBytes.Length);
                    cdBw.Write((short)nameBytes.Length);
                    cdBw.Write((short)WinZipAesCrypto.WinZipAesExtraField.Length);
                    cdBw.Write((short)0);    // comment length
                    cdBw.Write((short)0);    // disk number
                    cdBw.Write((short)0);    // internal attrs
                    uint extAttrs = 0x81A4u << 16; // Regular file attributes (0644)
                    if (modesByPath.TryGetValue(relPath, out var customMode))
                    {
                        extAttrs = ((uint)customMode << 16);
                    }
                    cdBw.Write(extAttrs);
                    cdBw.Write((int)localHeaderPos);
                    cdBw.Write(nameBytes);
                    cdBw.Write(WinZipAesCrypto.WinZipAesExtraField);
                    centralDirectoryHeaders.Add(cdMs.ToArray());

                    processedBytes += fileBytes.Length;
                    processedFiles++;
                    progress?.Report(new DomainProgressReport(
                        ProcessedBytes: processedBytes,
                        TotalBytes: totalBytes,
                        CurrentFileName: relPath,
                        Percentage: totalBytes > 0 ? (double)processedBytes / totalBytes * 100.0 : 0,
                        ProcessedFiles: processedFiles,
                        TotalFiles: totalFiles
                    ));
                }
            }

            long centralDirStart = fs.Position;
            foreach (var cdRecord in centralDirectoryHeaders)
            {
                bw.Write(cdRecord);
            }
            long centralDirEnd = fs.Position;

            // Write EOCD
            bw.Write(0x06054b50); // EOCD signature
            bw.Write((short)0);    // disk number
            bw.Write((short)0);    // disk with CD
            bw.Write((short)centralDirectoryHeaders.Count); // entries on disk
            bw.Write((short)centralDirectoryHeaders.Count); // total entries
            bw.Write((int)(centralDirEnd - centralDirStart)); // CD size
            bw.Write((int)centralDirStart); // CD offset
            bw.Write((short)0);    // comment length
            bw.Flush();
        }

        if (multiVolumeStream == null)
        {
            File.Move(tempOutput, request.DestinationArchivePath, overwrite: true);
        }
    }

    public async Task<AppendResult> AppendAsync(
        ArchiveAppendRequest request,
        IProgress<DomainProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var descriptor = ArchiveFormatRegistry.Detect(request.ArchivePath);
        if (descriptor.Format != ArchiveFormat.Zip)
        {
            throw new NotSupportedException($"Appending to '{descriptor.Format}' archive format is not supported.");
        }

        if (VolumeNameResolver.IsMultiVolume(request.ArchivePath))
        {
            throw new NotSupportedException("Modifying multi-volume split archives is not supported.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(request.CompressionLevel, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.CompressionLevel, 9);

        if (request.SourcePaths is null or { Count: 0 })
        {
            throw new ArgumentException("At least one source path must be specified.", nameof(request));
        }

        var archivePath = Path.GetFullPath(request.ArchivePath);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);
        }

        // Validate all sources upfront before creating any temporary files
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

        // Collect incoming entries
        var incomingEntries = new List<IncomingAppendEntry>();
        var incomingByPath = new Dictionary<string, IncomingAppendEntry>(StringComparer.Ordinal);

        foreach (var (fullPath, rawPath, isDir) in resolvedSources)
        {
            if (isDir)
            {
                var rootDirInfo = new DirectoryInfo(fullPath);
                var dirPrefix = EntryNameSanitizer.SanitizeRelativePath(rawPath, fullPath, request.BaseDirectory);

                if (!string.IsNullOrEmpty(dirPrefix))
                {
                    var normalizedPrefix = dirPrefix.TrimEnd('/') + "/";
                    var topDirEntry = new IncomingAppendEntry(
                        normalizedPrefix,
                        fullPath,
                        IsDirectory: true,
                        Length: 0,
                        rootDirInfo.LastWriteTimeUtc,
                        OperatingSystem.IsWindows() ? default : rootDirInfo.UnixFileMode
                    );
                    incomingEntries.Add(topDirEntry);
                    incomingByPath[normalizedPrefix] = topDirEntry;
                    incomingByPath[dirPrefix.TrimEnd('/')] = topDirEntry;
                }

                foreach (var fsi in rootDirInfo.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                {
                    var relativeFromDir = Path.GetRelativePath(rootDirInfo.FullName, fsi.FullName).Replace('\\', '/');
                    var relPath = string.IsNullOrEmpty(dirPrefix)
                        ? relativeFromDir
                        : dirPrefix + "/" + relativeFromDir;

                    if (fsi is DirectoryInfo dirInfo)
                    {
                        var normalizedDir = relPath.TrimEnd('/') + "/";
                        var dirEntry = new IncomingAppendEntry(
                            normalizedDir,
                            dirInfo.FullName,
                            IsDirectory: true,
                            Length: 0,
                            dirInfo.LastWriteTimeUtc,
                            OperatingSystem.IsWindows() ? default : dirInfo.UnixFileMode
                        );
                        incomingEntries.Add(dirEntry);
                        incomingByPath[normalizedDir] = dirEntry;
                        incomingByPath[relPath.TrimEnd('/')] = dirEntry;
                    }
                    else if (fsi is FileInfo fileInfo)
                    {
                        var fileEntry = new IncomingAppendEntry(
                            relPath,
                            fileInfo.FullName,
                            IsDirectory: false,
                            Length: fileInfo.Length,
                            fileInfo.LastWriteTimeUtc,
                            OperatingSystem.IsWindows() ? default : fileInfo.UnixFileMode
                        );
                        incomingEntries.Add(fileEntry);
                        incomingByPath[relPath] = fileEntry;
                    }
                }
            }
            else
            {
                var fileInfo = new FileInfo(fullPath);
                var relPath = EntryNameSanitizer.SanitizeRelativePath(rawPath, fullPath, request.BaseDirectory);
                var fileEntry = new IncomingAppendEntry(
                    relPath,
                    fileInfo.FullName,
                    IsDirectory: false,
                    Length: fileInfo.Length,
                    fileInfo.LastWriteTimeUtc,
                    OperatingSystem.IsWindows() ? default : fileInfo.UnixFileMode
                );
                incomingEntries.Add(fileEntry);
                incomingByPath[relPath] = fileEntry;
            }
        }

        // List existing entries to inspect collisions and calculate metrics upfront
        var existingCdRecords = ParseZipCentralDirectory(archivePath);
        var existingModesByPath = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var rec in existingCdRecords)
        {
            if ((rec.VersionMadeBy >> 8) == 3)
            {
                ushort mode16 = (ushort)(rec.ExternalFileAttributes >> 16);
                if (mode16 != 0)
                {
                    existingModesByPath[rec.Name] = mode16;
                    var trimmed = rec.Name.TrimEnd('/');
                    if (trimmed.Length > 0)
                    {
                        existingModesByPath[trimmed] = mode16;
                    }
                }
            }
        }

        var existingEntries = await ListEntriesAsync(archivePath, ct);

        var existingActions = new Dictionary<string, AppendEntryAction>(StringComparer.Ordinal);
        var incomingActions = new Dictionary<string, AppendEntryAction>(StringComparer.Ordinal);

        foreach (var existingEntry in existingEntries)
        {
            var existingPath = existingEntry.RelativePath.Replace('\\', '/');
            if (existingEntry.IsDirectory)
            {
                existingActions[existingPath] = AppendEntryAction.Retain;
                existingActions[existingPath.TrimEnd('/')] = AppendEntryAction.Retain;
            }
            else
            {
                if (incomingByPath.TryGetValue(existingPath, out var incoming) ||
                    incomingByPath.TryGetValue(existingPath.TrimStart('/'), out incoming))
                {
                    if (request.UpdateOnly)
                    {
                        var existingModTime = existingEntry.LastModified?.UtcDateTime;
                        var isStrictlyNewer = !existingModTime.HasValue || incoming.LastWriteTimeUtc > existingModTime.Value;
                        if (isStrictlyNewer)
                        {
                            existingActions[existingPath] = AppendEntryAction.Update;
                            incomingActions[incoming.RelativePath] = AppendEntryAction.Update;
                        }
                        else
                        {
                            existingActions[existingPath] = AppendEntryAction.Retain;
                            incomingActions[incoming.RelativePath] = AppendEntryAction.Skip;
                        }
                    }
                    else
                    {
                        existingActions[existingPath] = AppendEntryAction.Update;
                        incomingActions[incoming.RelativePath] = AppendEntryAction.Update;
                    }
                }
                else
                {
                    existingActions[existingPath] = AppendEntryAction.Retain;
                }
            }
        }

        int addedFiles = 0;
        int updatedFiles = 0;
        int retainedFiles = 0;
        int skippedFiles = 0;
        long totalUncompressedBytes = 0;

        foreach (var e in existingEntries)
        {
            if (e.IsDirectory) continue;

            var normPath = e.RelativePath.Replace('\\', '/');
            if (existingActions.TryGetValue(normPath, out var action) && action == AppendEntryAction.Retain)
            {
                retainedFiles++;
                totalUncompressedBytes += e.UncompressedSize;
            }
        }

        foreach (var inc in incomingEntries)
        {
            if (inc.IsDirectory) continue;

            if (incomingActions.TryGetValue(inc.RelativePath, out var action))
            {
                if (action == AppendEntryAction.Skip)
                {
                    skippedFiles++;
                }
                else if (action == AppendEntryAction.Update)
                {
                    updatedFiles++;
                    totalUncompressedBytes += inc.Length;
                }
            }
            else
            {
                addedFiles++;
                totalUncompressedBytes += inc.Length;
            }
        }

        int totalFiles = retainedFiles + updatedFiles + addedFiles;
        var tempOutput = archivePath + ".tmp." + Guid.NewGuid().ToString("N");
        long processedBytes = 0;
        int processedFiles = 0;
        var writtenDirPaths = new HashSet<string>(StringComparer.Ordinal);
        var modesByPath = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var sw = Stopwatch.StartNew();

        try
        {
            await Task.Run(async () =>
            {
                var compressionType = request.CompressionLevel == 0 ? SharpCompress.Common.CompressionType.None : SharpCompress.Common.CompressionType.Deflate;
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

                await using (var outputStream = new FileStream(
                    tempOutput,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    useAsync: true))
                {
                    using var zipWriter = new ZipWriter(outputStream, writerOptions);

                    // Phase 1: Stream preserved existing entries
                    using (var existingArchive = SharpCompress.Archives.Zip.ZipArchive.OpenArchive(archivePath, new ReaderOptions { LeaveStreamOpen = false, Password = request.Password }))
                    {
                        List<IArchiveEntry> archiveEntries;
                        try
                        {
                            archiveEntries = existingArchive.Entries.ToList();
                        }
                        catch (Exception ex) when (IsCorruptionException(ex))
                        {
                            throw new ArchiveIntegrityException($"Archive '{archivePath}' is corrupted or unparseable: {ex.Message}", innerException: ex);
                        }
                        catch (Exception ex) when (IsPasswordOrEncryptedException(ex))
                        {
                            if (string.IsNullOrEmpty(request.Password))
                            {
                                throw new ArchiveIntegrityException($"The archive '{Path.GetFileName(archivePath)}' is password-protected. Password is required.", innerException: ex);
                            }
                            throw new ArchiveIntegrityException($"Invalid password for '{Path.GetFileName(archivePath)}'.", innerException: ex);
                        }

                        foreach (var entry in archiveEntries)
                        {
                            ct.ThrowIfCancellationRequested();

                            var entryKey = entry.Key ?? string.Empty;
                            var normalizedKey = entryKey.Replace('\\', '/');
                            bool isDir = entry.IsDirectory || normalizedKey.EndsWith('/');

                            if (isDir)
                            {
                                var dirName = normalizedKey.TrimEnd('/') + "/";
                                if (writtenDirPaths.Add(dirName))
                                {
                                    if (existingModesByPath.TryGetValue(normalizedKey, out var dirMode) ||
                                        existingModesByPath.TryGetValue(dirName, out dirMode) ||
                                        existingModesByPath.TryGetValue(dirName.TrimEnd('/'), out dirMode))
                                    {
                                        modesByPath[dirName] = dirMode;
                                        modesByPath[dirName.TrimEnd('/')] = dirMode;
                                    }
                                    zipWriter.WriteDirectory(dirName, entry.LastModifiedTime?.ToUniversalTime() ?? DateTime.UtcNow);
                                }
                            }
                            else
                            {
                                var action = existingActions.TryGetValue(normalizedKey, out var act) ? act : AppendEntryAction.Retain;
                                if (action == AppendEntryAction.Retain)
                                {
                                    if (existingModesByPath.TryGetValue(normalizedKey, out var fileMode) ||
                                        existingModesByPath.TryGetValue(entryKey, out fileMode))
                                    {
                                        modesByPath[normalizedKey] = fileMode;
                                    }

                                    var entryLength = entry.Size;
                                    await using var entryStream = entry.OpenEntryStream();
                                    await using var trackingStream = new ProgressReportingStream(
                                        entryStream,
                                        entryLength,
                                        bytesRead =>
                                        {
                                            Interlocked.Add(ref processedBytes, bytesRead);
                                            var currentTotal = Volatile.Read(ref processedBytes);
                                            progress?.Report(new DomainProgressReport(
                                                ProcessedBytes: currentTotal,
                                                TotalBytes: totalUncompressedBytes,
                                                CurrentFileName: normalizedKey,
                                                Percentage: totalUncompressedBytes > 0 ? (double)currentTotal / totalUncompressedBytes * 100.0 : 0,
                                                ProcessedFiles: Volatile.Read(ref processedFiles),
                                                TotalFiles: totalFiles
                                            ));
                                        });

                                    zipWriter.Write(normalizedKey, trackingStream, entry.LastModifiedTime?.ToUniversalTime() ?? DateTime.UtcNow);
                                    Interlocked.Increment(ref processedFiles);
                                }
                            }
                        }
                    }

                    // Phase 2: Stream incoming new & updated entries
                    foreach (var inc in incomingEntries)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (inc.IsDirectory)
                        {
                            var dirName = inc.RelativePath.Replace('\\', '/').TrimEnd('/') + "/";
                            if (writtenDirPaths.Add(dirName))
                            {
                                if (TryGetZipMode16(inc.FullPath, isDirectory: true) is ushort dirMode)
                                {
                                    modesByPath[dirName] = dirMode;
                                    modesByPath[dirName.TrimEnd('/')] = dirMode;
                                }
                                zipWriter.WriteDirectory(dirName, inc.LastWriteTimeUtc);
                            }
                        }
                        else
                        {
                            var action = incomingActions.TryGetValue(inc.RelativePath, out var act) ? act : AppendEntryAction.Update;
                            if (action == AppendEntryAction.Skip)
                            {
                                continue;
                            }

                            if (TryGetZipMode16(inc.FullPath, isDirectory: false) is ushort fileMode)
                            {
                                modesByPath[inc.RelativePath] = fileMode;
                            }

                            await using var fileStream = new FileStream(
                                inc.FullPath,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.Read,
                                BufferSize,
                                useAsync: true);

                            await using var trackingStream = new ProgressReportingStream(
                                fileStream,
                                inc.Length,
                                bytesRead =>
                                {
                                    Interlocked.Add(ref processedBytes, bytesRead);
                                    var currentTotal = Volatile.Read(ref processedBytes);
                                    progress?.Report(new DomainProgressReport(
                                        ProcessedBytes: currentTotal,
                                        TotalBytes: totalUncompressedBytes,
                                        CurrentFileName: inc.RelativePath,
                                        Percentage: totalUncompressedBytes > 0 ? (double)currentTotal / totalUncompressedBytes * 100.0 : 0,
                                        ProcessedFiles: Volatile.Read(ref processedFiles),
                                        TotalFiles: totalFiles
                                    ));
                                });

                            zipWriter.Write(inc.RelativePath, trackingStream, inc.LastWriteTimeUtc);
                            Interlocked.Increment(ref processedFiles);
                        }
                    }
                }

                if (modesByPath.Count > 0)
                {
                    PatchZipExternalAttributes(tempOutput, modesByPath);
                }

                File.Move(tempOutput, archivePath, overwrite: true);
            }, ct);
        }
        catch (Exception ex) when (IsCorruptionException(ex))
        {
            throw new ArchiveIntegrityException($"Archive '{archivePath}' is corrupted or unparseable: {ex.Message}", innerException: ex);
        }
        finally
        {
            if (File.Exists(tempOutput))
            {
                try { File.Delete(tempOutput); } catch { /* Ignore */ }
            }
        }

        var finalInfo = new FileInfo(archivePath);
        double ratio = totalUncompressedBytes > 0 ? (double)finalInfo.Length / totalUncompressedBytes : 1.0;
        sw.Stop();

        return new AppendResult(
            Success: true,
            ArchivePath: archivePath,
            Format: "zip",
            AddedFiles: addedFiles,
            UpdatedFiles: updatedFiles,
            RetainedFiles: retainedFiles,
            SkippedFiles: skippedFiles,
            TotalFiles: totalFiles,
            UncompressedBytes: totalUncompressedBytes,
            CompressedBytes: finalInfo.Length,
            CompressionRatio: Math.Round(ratio, 4),
            ElapsedMilliseconds: sw.ElapsedMilliseconds
        );
    }

    public async Task<ArchiveDeleteResult> DeleteEntriesAsync(
        ArchiveDeleteRequest request,
        IProgress<DomainProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ArchivePath))
        {
            throw new ArgumentException("Archive path cannot be empty.", nameof(request));
        }

        if (request.EntryPaths is null or { Count: 0 } || request.EntryPaths.All(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one entry path must be specified for deletion.", nameof(request));
        }

        var archivePath = Path.GetFullPath(request.ArchivePath);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);
        }

        var descriptor = ArchiveFormatRegistry.Detect(archivePath);
        if (descriptor.Format != ArchiveFormat.Zip)
        {
            throw new NotSupportedException($"Deleting entries from '{descriptor.Format}' archive format is not supported.");
        }

        if (VolumeNameResolver.IsMultiVolume(request.ArchivePath))
        {
            throw new NotSupportedException("Modifying multi-volume split archives is not supported.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(request.CompressionLevel, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.CompressionLevel, 9);

        var existingCdRecords = ParseZipCentralDirectory(archivePath);
        var existingModesByPath = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var rec in existingCdRecords)
        {
            if ((rec.VersionMadeBy >> 8) == 3)
            {
                ushort mode16 = (ushort)(rec.ExternalFileAttributes >> 16);
                if (mode16 != 0)
                {
                    existingModesByPath[rec.Name] = mode16;
                    var trimmed = rec.Name.TrimEnd('/');
                    if (trimmed.Length > 0)
                    {
                        existingModesByPath[trimmed] = mode16;
                    }
                }
            }
        }

        var existingEntries = await ListEntriesAsync(archivePath, ct);

        int deletedEntriesCount = 0;
        int retainedEntriesCount = 0;
        int retainedFilesCount = 0;
        long totalUncompressedBytes = 0;

        foreach (var entry in existingEntries)
        {
            var normPath = entry.RelativePath.Replace('\\', '/');
            if (EntryFilter.IsMatch(normPath, request.EntryPaths))
            {
                deletedEntriesCount++;
            }
            else
            {
                retainedEntriesCount++;
                if (!entry.IsDirectory)
                {
                    retainedFilesCount++;
                    totalUncompressedBytes += entry.UncompressedSize;
                }
            }
        }

        var tempOutput = archivePath + ".tmp." + Guid.NewGuid().ToString("N");
        long processedBytes = 0;
        int processedFiles = 0;
        var writtenDirPaths = new HashSet<string>(StringComparer.Ordinal);
        var modesByPath = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var sw = Stopwatch.StartNew();

        try
        {
            await Task.Run(async () =>
            {
                var compressionType = request.CompressionLevel == 0 ? SharpCompress.Common.CompressionType.None : SharpCompress.Common.CompressionType.Deflate;
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

                await using (var outputStream = new FileStream(
                    tempOutput,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    useAsync: true))
                {
                    using var zipWriter = new ZipWriter(outputStream, writerOptions);

                    using (var existingArchive = SharpCompress.Archives.Zip.ZipArchive.OpenArchive(archivePath, new ReaderOptions { LeaveStreamOpen = false, Password = request.Password }))
                    {
                        List<IArchiveEntry> archiveEntries;
                        try
                        {
                            archiveEntries = existingArchive.Entries.ToList();
                        }
                        catch (Exception ex) when (IsCorruptionException(ex))
                        {
                            throw new ArchiveIntegrityException($"Archive '{archivePath}' is corrupted or unparseable: {ex.Message}", innerException: ex);
                        }
                        catch (Exception ex) when (IsPasswordOrEncryptedException(ex))
                        {
                            if (string.IsNullOrEmpty(request.Password))
                            {
                                throw new ArchiveIntegrityException($"The archive '{Path.GetFileName(archivePath)}' is password-protected. Password is required.", innerException: ex);
                            }
                            throw new ArchiveIntegrityException($"Invalid password for '{Path.GetFileName(archivePath)}'.", innerException: ex);
                        }

                        foreach (var entry in archiveEntries)
                        {
                            ct.ThrowIfCancellationRequested();

                            var entryKey = entry.Key ?? string.Empty;
                            var normalizedKey = entryKey.Replace('\\', '/');

                            if (EntryFilter.IsMatch(normalizedKey, request.EntryPaths))
                            {
                                continue;
                            }

                            bool isDir = entry.IsDirectory || normalizedKey.EndsWith('/');

                            if (isDir)
                            {
                                var dirName = normalizedKey.TrimEnd('/') + "/";
                                if (writtenDirPaths.Add(dirName))
                                {
                                    if (existingModesByPath.TryGetValue(normalizedKey, out var dirMode) ||
                                        existingModesByPath.TryGetValue(dirName, out dirMode) ||
                                        existingModesByPath.TryGetValue(dirName.TrimEnd('/'), out dirMode))
                                    {
                                        modesByPath[dirName] = dirMode;
                                        modesByPath[dirName.TrimEnd('/')] = dirMode;
                                    }
                                    zipWriter.WriteDirectory(dirName, entry.LastModifiedTime?.ToUniversalTime() ?? DateTime.UtcNow);
                                }
                            }
                            else
                            {
                                if (existingModesByPath.TryGetValue(normalizedKey, out var fileMode) ||
                                    existingModesByPath.TryGetValue(entryKey, out fileMode))
                                {
                                    modesByPath[normalizedKey] = fileMode;
                                }

                                var entryLength = entry.Size;
                                await using var entryStream = entry.OpenEntryStream();
                                await using var trackingStream = new ProgressReportingStream(
                                    entryStream,
                                    entryLength,
                                    bytesRead =>
                                    {
                                        Interlocked.Add(ref processedBytes, bytesRead);
                                        var currentTotal = Volatile.Read(ref processedBytes);
                                        progress?.Report(new DomainProgressReport(
                                            ProcessedBytes: currentTotal,
                                            TotalBytes: totalUncompressedBytes,
                                            CurrentFileName: normalizedKey,
                                            Percentage: totalUncompressedBytes > 0 ? (double)currentTotal / totalUncompressedBytes * 100.0 : 0,
                                            ProcessedFiles: Volatile.Read(ref processedFiles),
                                            TotalFiles: retainedFilesCount
                                        ));
                                    });

                                zipWriter.Write(normalizedKey, trackingStream, entry.LastModifiedTime?.ToUniversalTime() ?? DateTime.UtcNow);
                                Interlocked.Increment(ref processedFiles);
                            }
                        }
                    }
                }

                if (modesByPath.Count > 0)
                {
                    PatchZipExternalAttributes(tempOutput, modesByPath);
                }

                File.Move(tempOutput, archivePath, overwrite: true);
            }, ct);
        }
        catch (Exception ex) when (IsCorruptionException(ex))
        {
            throw new ArchiveIntegrityException($"Archive '{archivePath}' is corrupted or unparseable: {ex.Message}", innerException: ex);
        }
        finally
        {
            if (File.Exists(tempOutput))
            {
                try { File.Delete(tempOutput); } catch { /* Ignore */ }
            }
        }

        var finalInfo = new FileInfo(archivePath);
        sw.Stop();

        return new ArchiveDeleteResult(
            Success: true,
            ArchivePath: archivePath,
            DeletedEntriesCount: deletedEntriesCount,
            RetainedEntriesCount: retainedEntriesCount,
            UncompressedBytes: totalUncompressedBytes,
            CompressedBytes: finalInfo.Length,
            ElapsedMilliseconds: sw.ElapsedMilliseconds
        );
    }

    private sealed record IncomingAppendEntry(
        string RelativePath,
        string FullPath,
        bool IsDirectory,
        long Length,
        DateTime LastWriteTimeUtc,
        UnixFileMode UnixMode
    );

    private enum AppendEntryAction
    {
        Retain,
        Update,
        Skip
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
            return await ExtractTarGzAsync(archivePath, destDir, request.Overwrite, request.Limits, request.Entries, progress, ct, request.ConflictResolver);
        }

        if (format == ArchiveFormat.Gz)
        {
            return await ExtractGzAsync(archivePath, destDir, request.Overwrite, request.Limits, request.Entries, progress, ct, request.ConflictResolver);
        }

        var readerOptions = new ReaderOptions { LeaveStreamOpen = false, Password = request.Password };
        IArchive archive;
        try
        {
            archive = OpenArchiveByFormat(archivePath, format, readerOptions);
        }
        catch (Exception ex) when (IsPasswordOrEncryptedException(ex))
        {
            if (string.IsNullOrEmpty(request.Password))
            {
                throw new ArchiveIntegrityException($"The archive '{Path.GetFileName(archivePath)}' is password-protected. Password is required.", innerException: ex);
            }
            throw new ArchiveIntegrityException($"Invalid password for '{Path.GetFileName(archivePath)}'.", innerException: ex);
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

                        if (e.IsEncrypted && string.IsNullOrEmpty(request.Password))
                        {
                            throw new ArchiveIntegrityException($"The entry '{e.Key}' is password-protected. Password is required.");
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
                    if (string.IsNullOrEmpty(request.Password))
                    {
                        throw new ArchiveIntegrityException($"The archive '{Path.GetFileName(archivePath)}' is password-protected. Password is required.", innerException: ex);
                    }
                    throw new ArchiveIntegrityException($"Invalid password for '{Path.GetFileName(archivePath)}'.", innerException: ex);
                }
                catch (NotSupportedException)
                {
                    throw;
                }
                catch
                {
                    totalBytes = -1;
                }

                var source = new SharpCompressExtractionSource(archive, archivePath, request.Entries, request.Password);

                return await SafeArchiveExtractor.ExtractAllAsync(
                    source,
                    destDir,
                    request.Overwrite,
                    totalBytes,
                    progress,
                    ct,
                    request.Limits,
                    totalIsEstimate: totalBytes >= 0,
                    conflictResolver: request.ConflictResolver);
            }
            catch (Exception ex) when (ex is not SecurityException && ex is not NotSupportedException && ex is not InvalidOperationException && ex is not IOException && ex is not OperationCanceledException && IsPasswordOrEncryptedException(ex))
            {
                if (string.IsNullOrEmpty(request.Password))
                {
                    throw new ArchiveIntegrityException($"The archive '{Path.GetFileName(archivePath)}' is password-protected. Password is required.", innerException: ex);
                }
                throw new ArchiveIntegrityException($"Invalid password for '{Path.GetFileName(archivePath)}'.", innerException: ex);
            }
        }
    }

    public Task<ArchiveTestResult> TestArchiveAsync(
        string archivePath,
        IProgress<DomainProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        return TestArchiveAsync(archivePath, password: null, progress, ct);
    }

    public async Task<ArchiveTestResult> TestArchiveAsync(
        string archivePath,
        string? password,
        IProgress<DomainProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Archive not found: {fullPath}", fullPath);
        }

        var descriptor = ArchiveFormatRegistry.Detect(fullPath);
        var format = descriptor.Format;
        var formatName = format.ToString().ToLowerInvariant();
        var errors = new List<string>();
        int totalEntries = 0;
        long uncompressedBytes = 0;
        var sw = Stopwatch.StartNew();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            if (format == ArchiveFormat.Gz)
            {
                try
                {
                    await using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
                    await using var gzStream = new GZipStream(fileStream, CompressionMode.Decompress);

                    int read;
                    while ((read = await gzStream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
                    {
                        uncompressedBytes += read;
                        progress?.Report(new DomainProgressReport(
                            ProcessedBytes: uncompressedBytes,
                            TotalBytes: fileStream.Length,
                            CurrentFileName: Path.GetFileNameWithoutExtension(fullPath),
                            Percentage: fileStream.Length > 0 ? (double)fileStream.Position / fileStream.Length * 100.0 : 0,
                            ProcessedFiles: 1,
                            TotalFiles: 1
                        ));
                    }
                    totalEntries = 1;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"GZip stream error: {ex.Message}");
                }
            }
            else if (format == ArchiveFormat.TarGz)
            {
                try
                {
                    await using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
                    await using var gzStream = new GZipStream(fileStream, CompressionMode.Decompress);
                    await using var tarReader = new TarReader(gzStream, leaveOpen: false);

                    while (true)
                    {
                        TarEntry? entry = null;
                        try
                        {
                            entry = await tarReader.GetNextEntryAsync(copyData: false, ct);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            errors.Add($"Tar header error: {ex.Message}");
                            break;
                        }

                        if (entry is null) break;

                        totalEntries++;
                        var entryName = entry.Name;
                        var isDir = entry.EntryType == TarEntryType.Directory || entryName.Replace('\\', '/').EndsWith('/');

                        if (!isDir && entry.DataStream is not null)
                        {
                            try
                            {
                                int bytesRead;
                                while ((bytesRead = await entry.DataStream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
                                {
                                    uncompressedBytes += bytesRead;
                                    progress?.Report(new DomainProgressReport(
                                        ProcessedBytes: uncompressedBytes,
                                        TotalBytes: fileStream.Length,
                                        CurrentFileName: entryName,
                                        Percentage: fileStream.Length > 0 ? (double)fileStream.Position / fileStream.Length * 100.0 : 0,
                                        ProcessedFiles: totalEntries,
                                        TotalFiles: totalEntries
                                    ));
                                }
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                errors.Add($"Entry '{entryName}' corrupted: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"Archive stream error: {ex.Message}");
                }
            }
            else
            {
                await Task.Run(() =>
                {
                    var readerOptions = new ReaderOptions { LeaveStreamOpen = false, Password = password };
                    IArchive? archive = null;
                    try
                    {
                        archive = OpenArchiveByFormat(fullPath, format, readerOptions);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (IsPasswordOrEncryptedException(ex))
                        {
                            errors.Add(string.IsNullOrEmpty(password) ? "Password required for encrypted archive." : "Invalid archive password.");
                        }
                        else
                        {
                            errors.Add($"Failed to open archive: {ex.Message}");
                        }
                    }

                    if (archive != null)
                    {
                        using (archive)
                        {
                            List<IArchiveEntry>? entries = null;
                            try
                            {
                                entries = archive.Entries.ToList();
                                if (entries.Count == 0 && format == ArchiveFormat.Zip && ZipDeclaresEntries(fullPath))
                                {
                                    errors.Add("ZIP central directory declared entries but none could be read.");
                                }
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                if (IsPasswordOrEncryptedException(ex))
                                {
                                    errors.Add(string.IsNullOrEmpty(password) ? "Password required for encrypted archive." : "Invalid archive password.");
                                }
                                else
                                {
                                    errors.Add($"Failed to read archive entries: {ex.Message}");
                                }
                            }

                            if (entries != null)
                            {
                                long totalSize = entries.Where(e => !e.IsDirectory).Sum(e => e.Size);
                                foreach (var entry in entries)
                                {
                                    ct.ThrowIfCancellationRequested();
                                    totalEntries++;

                                    if (entry.IsDirectory) continue;

                                    if (entry.IsEncrypted && string.IsNullOrEmpty(password))
                                    {
                                        errors.Add($"Entry '{entry.Key}' is password-protected.");
                                        continue;
                                    }

                                    var entryName = entry.Key ?? $"Entry_{totalEntries}";
                                    try
                                    {
                                        using var entryStream = entry.OpenEntryStream();
                                        int read;
                                        while ((read = entryStream.Read(buffer, 0, BufferSize)) > 0)
                                        {
                                            uncompressedBytes += read;
                                            progress?.Report(new DomainProgressReport(
                                                ProcessedBytes: uncompressedBytes,
                                                TotalBytes: totalSize > 0 ? totalSize : uncompressedBytes,
                                                CurrentFileName: entryName,
                                                Percentage: totalSize > 0 ? Math.Min(100.0, (double)uncompressedBytes / totalSize * 100.0) : 0,
                                                ProcessedFiles: totalEntries,
                                                TotalFiles: entries.Count
                                            ));
                                        }
                                    }
                                    catch (Exception ex) when (ex is not OperationCanceledException)
                                    {
                                        if (IsPasswordOrEncryptedException(ex))
                                        {
                                            errors.Add(string.IsNullOrEmpty(password) ? $"Entry '{entryName}' is password-protected." : $"Invalid password for entry '{entryName}'.");
                                        }
                                        else
                                        {
                                            errors.Add($"Entry '{entryName}' corrupted: {ex.Message}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }, ct);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        sw.Stop();
        var duration = sw.Elapsed;
        double throughputMBps = duration.TotalSeconds > 0
            ? (uncompressedBytes / (1024.0 * 1024.0)) / duration.TotalSeconds
            : 0.0;

        return new ArchiveTestResult(
            IsSuccess: errors.Count == 0,
            ArchivePath: fullPath,
            Format: formatName,
            TotalEntries: totalEntries,
            UncompressedBytes: uncompressedBytes,
            ThroughputMBps: Math.Round(throughputMBps, 2),
            Duration: duration,
            Errors: errors
        );
    }

    public async Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        CancellationToken ct = default)
    {
        return await ListEntriesAsync(archivePath, password: null, ct);
    }

    public async Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password,
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
            var readerOptions = new ReaderOptions { LeaveStreamOpen = false, Password = password };

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
                if (string.IsNullOrEmpty(password))
                {
                    throw new ArchiveIntegrityException($"The archive '{Path.GetFileName(fullPath)}' is password-protected. Password is required.", innerException: ex);
                }
                throw new ArchiveIntegrityException($"Invalid password for '{Path.GetFileName(fullPath)}'.", innerException: ex);
            }

            return (IReadOnlyList<ArchiveEntry>)results;
        }, ct);
    }

    private static IArchive OpenArchiveByFormat(string filePath, ArchiveFormat format, ReaderOptions options)
    {
        if (VolumeNameResolver.IsMultiVolume(filePath))
        {
            var parts = VolumeNameResolver.DiscoverVolumeSequence(filePath);
            var stream = new MultiVolumeReadStream(parts);
            return OpenArchiveByStream(stream, format, options);
        }

        return format switch
        {
            ArchiveFormat.Zip => SharpCompress.Archives.Zip.ZipArchive.OpenArchive(filePath, options),
            ArchiveFormat.Rar => RarArchive.OpenArchive(filePath, options),
            ArchiveFormat.SevenZip => SevenZipArchive.OpenArchive(filePath, options),
            _ => throw new NotSupportedException($"Format '{format}' not directly supported via SharpCompress IArchive")
        };
    }

    private static IArchive OpenArchiveByStream(Stream stream, ArchiveFormat format, ReaderOptions options)
    {
        return format switch
        {
            ArchiveFormat.Zip => SharpCompress.Archives.Zip.ZipArchive.OpenArchive(stream, options),
            ArchiveFormat.Rar => RarArchive.OpenArchive(stream, options),
            ArchiveFormat.SevenZip => SevenZipArchive.OpenArchive(stream, options),
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
        CancellationToken ct,
        IFileConflictResolver? conflictResolver = null)
    {
        var source = new TarGzExtractionSource(archivePath, entries);
        return await SafeArchiveExtractor.ExtractAllAsync(
            source,
            destinationDir,
            overwrite,
            totalBytes: -1,
            progress,
            ct,
            limits,
            conflictResolver: conflictResolver);
    }

    private static async Task<ExtractionResult> ExtractGzAsync(
        string archivePath,
        string destinationDir,
        bool overwrite,
        ExtractionLimits? limits,
        IReadOnlyList<string>? entries,
        IProgress<DomainProgressReport>? progress,
        CancellationToken ct,
        IFileConflictResolver? conflictResolver = null)
    {
        var source = new GzExtractionSource(archivePath, entries);
        return await SafeArchiveExtractor.ExtractAllAsync(
            source,
            destinationDir,
            overwrite,
            totalBytes: -1,
            progress,
            ct,
            limits,
            conflictResolver: conflictResolver);
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

    private sealed class SharpCompressExtractionSource(IArchive archive, string archivePath, IReadOnlyList<string>? entryFilter, string? password = null) : IArchiveExtractionSource
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
                if (string.IsNullOrEmpty(password))
                {
                    throw new ArchiveIntegrityException($"The archive '{Path.GetFileName(archivePath)}' is password-protected. Password is required.", innerException: ex);
                }
                throw new ArchiveIntegrityException($"Invalid password for '{Path.GetFileName(archivePath)}'.", innerException: ex);
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

                if (entry.IsEncrypted && string.IsNullOrEmpty(password))
                {
                    throw new ArchiveIntegrityException($"The entry '{entry.Key}' is password-protected. Password is required.");
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
                        if (entry.IsEncrypted && string.IsNullOrEmpty(password))
                        {
                            throw new ArchiveIntegrityException($"The entry '{entry.Key}' is password-protected. Password is required.");
                        }

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
                            if (string.IsNullOrEmpty(password))
                            {
                                throw new ArchiveIntegrityException($"The entry '{entry.Key}' is password-protected. Password is required.", entry.Key, ex);
                            }
                            throw new ArchiveIntegrityException($"Invalid password for entry '{entry.Key}'.", entry.Key, ex);
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
                if (!modeByPath.TryGetValue(rec.Name, out ushort mode16) &&
                    !modeByPath.TryGetValue(rec.Name.TrimEnd('/'), out mode16))
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
            if (_eofReached || _expectedCrc == 0)
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

    public async Task<bool> IsEncryptedAsync(string archivePath, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullPath)) return false;
        var format = ArchiveFormatRegistry.Detect(fullPath).Format;
        if (format is ArchiveFormat.TarGz or ArchiveFormat.Gz) return false;

        return await Task.Run(() =>
        {
            try
            {
                using var archive = OpenArchiveByFormat(fullPath, format, new ReaderOptions { LeaveStreamOpen = false });
                return archive.Entries.Any(e => e.IsEncrypted);
            }
            catch (Exception ex) when (IsPasswordOrEncryptedException(ex))
            {
                return true;
            }
            catch
            {
                return false;
            }
        }, ct);
    }

    public Task<IReadOnlyList<string>> GetVolumePartsAsync(string archivePath, CancellationToken ct = default)
    {
        if (VolumeNameResolver.IsMultiVolume(archivePath))
        {
            return Task.FromResult(VolumeNameResolver.DiscoverVolumeSequence(archivePath));
        }
        return Task.FromResult<IReadOnlyList<string>>([archivePath]);
    }
}
