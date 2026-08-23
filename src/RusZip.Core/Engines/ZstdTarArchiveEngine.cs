using System.Buffers;
using System.Diagnostics;
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

        var tempOutput = destination + ".tmp." + Guid.NewGuid().ToString("N");
        long processedBytes = 0;
        int processedFiles = 0;
        int entriesWritten = 0;
        var isSingleDir = resolvedSources.Count == 1 && resolvedSources[0].IsDir;

        var descriptor = ArchiveFormatRegistry.Detect(destination);
        if (descriptor.Format == ArchiveFormat.Zst)
        {
            if (resolvedSources.Count != 1)
            {
                throw new ArgumentException("Single-file Zstandard compression (.zst) requires exactly one source file.", nameof(request));
            }

            if (resolvedSources[0].IsDir)
            {
                throw new ArgumentException("Single-file Zstandard compression (.zst) does not support directory input.", nameof(request));
            }

            var (srcFullPath, _, _) = resolvedSources[0];
            var fileInfo = new FileInfo(srcFullPath);
            var relPath = Path.GetFileName(srcFullPath);

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
                                TotalFiles: totalFiles
                            ));
                        });

                    await trackingStream.CopyToAsync(compressionStream, BufferSize, ct);
                    Interlocked.Increment(ref processedFiles);
                }

                File.Move(tempOutput, destination, overwrite: true);
                return;
            }
            finally
            {
                if (File.Exists(tempOutput))
                {
                    try { File.Delete(tempOutput); } catch { /* Ignore */ }
                }
            }
        }

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
                                var topDirEntry = new PaxTarEntry(TarEntryType.Directory, dirPrefix.TrimEnd('/') + "/")
                                {
                                    ModificationTime = rootDirInfo.LastWriteTimeUtc
                                };
                                if (!OperatingSystem.IsWindows())
                                {
                                    topDirEntry.Mode = rootDirInfo.UnixFileMode;
                                }
                                await tarWriter.WriteEntryAsync(topDirEntry, ct);
                                entriesWritten++;
                            }

                            foreach (var fsi in rootDirInfo.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                            {
                                ct.ThrowIfCancellationRequested();
                                var relativeFromDir = Path.GetRelativePath(rootDirInfo.FullName, fsi.FullName).Replace('\\', '/');
                                var relPath = string.IsNullOrEmpty(dirPrefix)
                                    ? relativeFromDir
                                    : dirPrefix + "/" + relativeFromDir;

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
                                                TotalFiles: totalFiles
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
                            var fileInfo = new FileInfo(fullPath);
                            var relPath = EntryNameSanitizer.SanitizeRelativePath(rawPath, fullPath, request.BaseDirectory);

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
                                        TotalFiles: totalFiles
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

    public async Task<AppendResult> AppendAsync(
        ArchiveAppendRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(request.CompressionLevel, CompressionProfiles.MinLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.CompressionLevel, CompressionProfiles.MaxLevel);

        if (request.SourcePaths is null or { Count: 0 })
        {
            throw new ArgumentException("At least one source path must be specified.", nameof(request));
        }

        var archivePath = Path.GetFullPath(request.ArchivePath);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);
        }

        var descriptor = ArchiveFormatRegistry.Detect(archivePath);
        if (descriptor.Format == ArchiveFormat.Zst)
        {
            throw new NotSupportedException("Appending is not supported for single-file streams.");
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
        var existingEntries = await ListEntriesAsync(archivePath, ct);

        var existingActions = new Dictionary<string, AppendEntryAction>(StringComparer.Ordinal);
        var incomingActions = new Dictionary<string, AppendEntryAction>(StringComparer.Ordinal);

        foreach (var existingEntry in existingEntries)
        {
            var existingPath = existingEntry.RelativePath;
            if (existingEntry.IsDirectory)
            {
                existingActions[existingPath] = AppendEntryAction.Retain;
            }
            else
            {
                if (incomingByPath.TryGetValue(existingPath, out var incoming))
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

            if (existingActions.TryGetValue(e.RelativePath, out var action) && action == AppendEntryAction.Retain)
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
        int entriesWritten = 0;
        var writtenDirPaths = new HashSet<string>(StringComparer.Ordinal);
        var sw = Stopwatch.StartNew();

        try
        {
            await using (var fileStreamIn = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
            await using (var decompStream = new DecompressionStream(fileStreamIn))
            await using (var countingStream = new CountingReadStream(decompStream))
            await using (var tarReader = new TarReader(countingStream, leaveOpen: false))
            await using (var fileStreamOut = new FileStream(tempOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            await using (var compStream = new CompressionStream(fileStreamOut, request.CompressionLevel))
            {
                compStream.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
                if (Environment.ProcessorCount > 1)
                {
                    compStream.SetParameter(ZSTD_cParameter.ZSTD_c_nbWorkers, Environment.ProcessorCount);
                }

                await using (var tarWriter = new TarWriter(compStream, TarEntryFormat.Pax, leaveOpen: true))
                {
                    // Phase 1: Stream preserved existing entries
                    TarEntry? entry;
                    try
                    {
                        while ((entry = await tarReader.GetNextEntryAsync(copyData: false, ct)) is not null)
                        {
                            ct.ThrowIfCancellationRequested();

                            var isDir = entry.EntryType == TarEntryType.Directory || entry.Name.Replace('\\', '/').EndsWith('/');

                            if (isDir)
                            {
                                var dirName = entry.Name.Replace('\\', '/');
                                if (!dirName.EndsWith('/')) dirName += "/";

                                if (writtenDirPaths.Add(dirName))
                                {
                                    var dirEntry = new PaxTarEntry(TarEntryType.Directory, dirName)
                                    {
                                        ModificationTime = entry.ModificationTime
                                    };
                                    if (!OperatingSystem.IsWindows() && entry.Mode != 0 && entry.Mode != (UnixFileMode)(-1))
                                    {
                                        dirEntry.Mode = entry.Mode;
                                    }
                                    await tarWriter.WriteEntryAsync(dirEntry, ct);
                                    entriesWritten++;
                                }
                            }
                            else
                            {
                                var entryName = entry.Name;
                                var action = existingActions.TryGetValue(entryName, out var act) ? act : AppendEntryAction.Retain;

                                if (action == AppendEntryAction.Retain)
                                {
                                    var entryLength = entry.Length;
                                    Stream seekableStream;
                                    if (entryLength > 10 * 1024 * 1024)
                                    {
                                        var tempFs = new FileStream(
                                            Path.Combine(Path.GetTempPath(), "ruszip_entry_" + Guid.NewGuid().ToString("N")),
                                            FileMode.Create,
                                            FileAccess.ReadWrite,
                                            FileShare.None,
                                            BufferSize,
                                            FileOptions.DeleteOnClose);
                                        if (entry.DataStream is not null)
                                        {
                                            await entry.DataStream.CopyToAsync(tempFs, ct);
                                        }
                                        tempFs.Position = 0;
                                        seekableStream = tempFs;
                                    }
                                    else
                                    {
                                        var ms = new MemoryStream((int)entryLength);
                                        if (entry.DataStream is not null)
                                        {
                                            await entry.DataStream.CopyToAsync(ms, ct);
                                        }
                                        ms.Position = 0;
                                        seekableStream = ms;
                                    }

                                    await using (seekableStream)
                                    {
                                        await using var trackingStream = new ProgressReportingStream(
                                            seekableStream,
                                            entryLength,
                                            bytesRead =>
                                            {
                                                Interlocked.Add(ref processedBytes, bytesRead);
                                                var currentTotal = Volatile.Read(ref processedBytes);
                                                progress?.Report(new ProgressReport(
                                                    ProcessedBytes: currentTotal,
                                                    TotalBytes: totalUncompressedBytes,
                                                    CurrentFileName: entryName,
                                                    Percentage: totalUncompressedBytes > 0 ? (double)currentTotal / totalUncompressedBytes * 100.0 : 0,
                                                    ProcessedFiles: Volatile.Read(ref processedFiles),
                                                    TotalFiles: totalFiles
                                                ));
                                            });

                                        var fileEntry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
                                        {
                                            DataStream = trackingStream,
                                            ModificationTime = entry.ModificationTime
                                        };
                                        if (!OperatingSystem.IsWindows() && entry.Mode != 0 && entry.Mode != (UnixFileMode)(-1))
                                        {
                                            fileEntry.Mode = entry.Mode;
                                        }

                                        await tarWriter.WriteEntryAsync(fileEntry, ct);
                                        entriesWritten++;
                                        Interlocked.Increment(ref processedFiles);
                                    }
                                }
                            }
                        }
                    }
                    catch (EndOfStreamException) when (countingStream.TotalBytesRead == 0)
                    {
                        // Legacy empty-directory archive
                    }

                    // Drain decompStream to verify frame checksum
                    byte[] drainBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                    try
                    {
                        int read;
                        while ((read = await decompStream.ReadAsync(drainBuffer.AsMemory(0, BufferSize), ct)) > 0) { }
                    }
                    catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
                    {
                        throw new ArchiveIntegrityException($"Zstandard frame checksum failed in '{archivePath}': {ex.Message}", innerException: ex);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(drainBuffer);
                    }

                    // Phase 2: Stream incoming new & updated entries
                    foreach (var inc in incomingEntries)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (inc.IsDirectory)
                        {
                            var dirName = inc.RelativePath.Replace('\\', '/');
                            if (!dirName.EndsWith('/')) dirName += "/";

                            if (writtenDirPaths.Add(dirName))
                            {
                                var dirEntry = new PaxTarEntry(TarEntryType.Directory, dirName)
                                {
                                    ModificationTime = inc.LastWriteTimeUtc
                                };
                                if (!OperatingSystem.IsWindows())
                                {
                                    dirEntry.Mode = inc.UnixMode;
                                }
                                await tarWriter.WriteEntryAsync(dirEntry, ct);
                                entriesWritten++;
                            }
                        }
                        else
                        {
                            var action = incomingActions.TryGetValue(inc.RelativePath, out var act) ? act : AppendEntryAction.Update;
                            if (action == AppendEntryAction.Skip)
                            {
                                continue;
                            }

                            await using var srcStream = new FileStream(
                                inc.FullPath,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.Read,
                                BufferSize,
                                useAsync: true);

                            await using var trackingStream = new ProgressReportingStream(
                                srcStream,
                                inc.Length,
                                bytesRead =>
                                {
                                    Interlocked.Add(ref processedBytes, bytesRead);
                                    var currentTotal = Volatile.Read(ref processedBytes);
                                    progress?.Report(new ProgressReport(
                                        ProcessedBytes: currentTotal,
                                        TotalBytes: totalUncompressedBytes,
                                        CurrentFileName: inc.RelativePath,
                                        Percentage: totalUncompressedBytes > 0 ? (double)currentTotal / totalUncompressedBytes * 100.0 : 0,
                                        ProcessedFiles: Volatile.Read(ref processedFiles),
                                        TotalFiles: totalFiles
                                    ));
                                });

                            var fileEntry = new PaxTarEntry(TarEntryType.RegularFile, inc.RelativePath)
                            {
                                DataStream = trackingStream,
                                ModificationTime = inc.LastWriteTimeUtc
                            };
                            if (!OperatingSystem.IsWindows())
                            {
                                fileEntry.Mode = inc.UnixMode;
                            }

                            await tarWriter.WriteEntryAsync(fileEntry, ct);
                            entriesWritten++;
                            Interlocked.Increment(ref processedFiles);
                        }
                    }
                }

                if (entriesWritten == 0)
                {
                    byte[] endOfArchive = new byte[1024]; // two 512-byte zero blocks
                    await compStream.WriteAsync(endOfArchive.AsMemory(), ct);
                }
            }

            File.Move(tempOutput, archivePath, overwrite: true);
        }
        catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
        {
            throw new ArchiveIntegrityException($"Zstandard frame error in '{archivePath}': {ex.Message}", innerException: ex);
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
            Format: "zrus",
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
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var archivePath = Path.GetFullPath(request.ArchivePath);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"Archive not found: {archivePath}");
        }

        var descriptor = ArchiveFormatRegistry.Detect(archivePath);
        if (descriptor.Format == ArchiveFormat.Zst)
        {
            return await ExtractZstAsync(archivePath, request.DestinationDirectory, request.Overwrite, request.Limits, request.Entries, progress, ct, request.ConflictResolver);
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
                totalIsEstimate: totalBytes >= 0,
                conflictResolver: request.ConflictResolver);
        }
        catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
        {
            throw new ArchiveIntegrityException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", innerException: ex);
        }
    }

    private static async Task<ExtractionResult> ExtractZstAsync(
        string archivePath,
        string destinationDir,
        bool overwrite,
        ExtractionLimits? limits,
        IReadOnlyList<string>? entries,
        IProgress<ProgressReport>? progress,
        CancellationToken ct,
        IFileConflictResolver? conflictResolver = null)
    {
        long totalBytes = 0;
        try
        {
            var list = await ListZstEntryAsync(archivePath, ct);
            totalBytes = list
                .Where(e => !e.IsDirectory && EntryFilter.IsMatch(e.RelativePath, entries))
                .Sum(e => e.UncompressedSize);
        }
        catch (ArchiveIntegrityException)
        {
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

        var source = new ZstExtractionSource(archivePath, entries);
        try
        {
            return await SafeArchiveExtractor.ExtractAllAsync(
                source,
                destinationDir,
                overwrite,
                totalBytes,
                progress,
                ct,
                limits,
                totalIsEstimate: false,
                conflictResolver: conflictResolver);
        }
        catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
        {
            throw new ArchiveIntegrityException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", innerException: ex);
        }
    }

    private sealed class ZstExtractionSource(string archivePath, IReadOnlyList<string>? entryFilter) : IArchiveExtractionSource
    {
        public async IAsyncEnumerable<ExtractionEntry> ReadEntriesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            var outFileName = Path.GetFileNameWithoutExtension(archivePath);
            var fileInfo = new FileInfo(archivePath);

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
                    FileStream inStream;
                    try
                    {
                        inStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, SafeArchiveExtractor.BufferSize, useAsync: true);
                    }
                    catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
                    {
                        throw new ArchiveIntegrityException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", outFileName, ex);
                    }

                    DecompressionStream zstdStream;
                    try
                    {
                        zstdStream = new DecompressionStream(inStream);
                    }
                    catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
                    {
                        inStream.Dispose();
                        throw new ArchiveIntegrityException($"Zstandard frame corrupted in '{archivePath}': {ex.Message}", outFileName, ex);
                    }

                    Stream stream = new ZstdIntegrityStream(zstdStream, archivePath, outFileName);
                    return ValueTask.FromResult(stream);
                }
            );
            await Task.CompletedTask;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await base.DisposeAsync();
        }
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

        var descriptor = ArchiveFormatRegistry.Detect(fullPath);
        if (descriptor.Format == ArchiveFormat.Zst)
        {
            return await ListZstEntryAsync(fullPath, ct);
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

    private static async Task<IReadOnlyList<ArchiveEntry>> ListZstEntryAsync(string fullPath, CancellationToken ct)
    {
        var fileName = Path.GetFileNameWithoutExtension(fullPath);
        var fileInfo = new FileInfo(fullPath);
        long uncompressedBytes = 0;

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

            byte[] drainBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                int read;
                while ((read = await decompressionStream.ReadAsync(drainBuffer.AsMemory(0, BufferSize), ct)) > 0)
                {
                    uncompressedBytes += read;
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

        return [new ArchiveEntry(
            RelativePath: fileName,
            UncompressedSize: uncompressedBytes,
            CompressedSize: fileInfo.Length,
            LastModified: fileInfo.LastWriteTimeUtc,
            IsDirectory: false,
            IsEncrypted: false,
            Attributes: ""
        )];
    }
}
