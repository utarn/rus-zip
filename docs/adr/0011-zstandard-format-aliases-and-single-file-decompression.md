# 0011: Zstandard Format Aliases and Single-File Decompression

We decided to support `.tar.zstd` and `.tzstd` as Tar+Zstandard container aliases of `.zrus`, introduce `ArchiveFormat.Zst` for single-file Zstandard stream decompression, and restrict GUI creation to `.zrus` and `.zip` while supporting registered format extensions on the CLI.

## Context
While `rus-zip` uses `.zrus` as its primary Tar+Zstd format, users frequently encounter standard `.tar.zstd` and `.tzstd` tarballs as well as standalone `.zst` single-file compressed streams.
- The Desktop GUI creation wizard should strictly guide users toward canonical `.zrus` and `.zip` containers.
- The CLI should be flexible, allowing users to decompress `.tar.zstd`, `.tzstd`, and `.zst`, create `.tar.zstd`/`.tzstd` Tar archives, and compress single files to `.zst`.

## Decision
1. **Tar+Zstandard Aliases (`.zrus`, `.tar.zstd`, `.tzstd`)**:
   - Update `ArchiveFormatRegistry.Zrus` in [`src/RusZip.Core/Models/ArchiveFormatRegistry.cs`](../../src/RusZip.Core/Models/ArchiveFormatRegistry.cs):
     ```csharp
     public static readonly ArchiveFormatDescriptor Zrus = new(
         Format: ArchiveFormat.Zrus,
         DisplayName: "Zstandard TAR Archive (.zrus, .tar.zstd, .tzstd)",
         PrimaryExtension: ".zrus",
         Extensions: [".zrus", ".tar.zstd", ".tzstd"],
         CanCompress: true,
         CanDecompress: true,
         MinCompressionLevel: 1,
         MaxCompressionLevel: 22,
         DefaultCompressionLevel: 9,
         MimeType: "application/x-zstd-tar",
         CategoryDescription: "High-performance POSIX Tar with Zstandard streaming compression"
     );
     ```
   - Extraction, listing, and appending are handled by [`ZstdTarArchiveEngine`](../../src/RusZip.Core/Engines/ZstdTarArchiveEngine.cs).
   - Compound extension stripping detects `.tar.zstd` to extract `name.tar.zstd` into directory `name/` rather than `name.tar/`.

2. **Single-File Zstandard Stream (`.zst`)**:
   - Add `ArchiveFormat.Zst` to [`src/RusZip.Core/Models/ArchiveFormat.cs`](../../src/RusZip.Core/Models/ArchiveFormat.cs) and register descriptor in [`ArchiveFormatRegistry.cs`](../../src/RusZip.Core/Models/ArchiveFormatRegistry.cs):
     ```csharp
     public static readonly ArchiveFormatDescriptor Zst = new(
         Format: ArchiveFormat.Zst,
         DisplayName: "Zstandard Compressed File (.zst)",
         PrimaryExtension: ".zst",
         Extensions: [".zst"],
         CanCompress: true,
         CanDecompress: true,
         MinCompressionLevel: 1,
         MaxCompressionLevel: 22,
         DefaultCompressionLevel: 9,
         MimeType: "application/zstd",
         CategoryDescription: "Single file Zstandard compressed stream"
     );
     ```
   - Extraction decompresses the stream directly without a Tar container (analogous to `.gz` in [`SharpCompressArchiveEngine.cs`](../../src/RusZip.Core/Engines/SharpCompressArchiveEngine.cs) or [`ZstdTarArchiveEngine.cs`](../../src/RusZip.Core/Engines/ZstdTarArchiveEngine.cs)):
     ```csharp
     private sealed class ZstSingleFileExtractionSource(string archivePath, IReadOnlyList<string>? entryFilter) : IArchiveExtractionSource
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
                     var inStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, SafeArchiveExtractor.BufferSize, useAsync: true);
                     var zstdStream = new DecompressionStream(inStream);
                     return ValueTask.FromResult<Stream>(zstdStream);
                 }
             );
             await Task.CompletedTask;
         }
     }
     ```
   - CLI compression of `.zst` validates that exactly one source file (not directory) is provided.

3. **GUI vs CLI Compression Scope**:
   - In [`src/RusZip.Desktop/ViewModels/CompressionSettingsViewModel.cs`](../../src/RusZip.Desktop/ViewModels/CompressionSettingsViewModel.cs), the selectable formats dropdown is explicitly restricted to `[".zrus", ".zip"]`.
   - In [`src/RusZip.Cli/Commands/CompressCommand.cs`](../../src/RusZip.Cli/Commands/CompressCommand.cs), the user may pass any registered extension (`.zrus`, `.tar.zstd`, `.tzstd`, `.zip`, `.zst`). Unregistered extensions produce a validation error.

4. **Desktop File Associations & Open File Dialogs**:
   - Update file picker filters in [`src/RusZip.Desktop/Views/MainWindow.axaml.cs`](../../src/RusZip.Desktop/Views/MainWindow.axaml.cs) to include `*.tar.zstd`, `*.tzstd`, `*.zst`.

## Consequences
- Full compatibility with broader Zstandard ecosystems (`.tar.zstd`, `.tzstd`, `.zst`) across Windows, macOS, and Linux.
- Clear separation between multi-file Tar+Zstd archives and raw single-file Zstd streams.
- Clean user experience in Desktop GUI with standard `.zrus` and `.zip` targets, while empowering power users on CLI.
