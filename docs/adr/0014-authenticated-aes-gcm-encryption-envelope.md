# 0014: Authenticated AES-256-GCM Envelope for .zrus Password Protection

We decided to implement password-based archive encryption across `rus-zip` using an authenticated AES-256-GCM envelope stream for `.zrus` archives, WinZip AES-256 encryption for `.zip`, and standard password forwarding to SharpCompress for foreign formats (`.7z`, `.rar`, `.zip`).

## Context
`rus-zip` required support for password protection across compression and decompression workflows.
- `.zip` archives have an industry-standard WinZip AES-256 encryption specification supported by SharpCompress.
- `.zrus` (POSIX Tar + Zstandard) lacks an inherent container-level encryption specification in the tar or zstd RFCs. We need a modern, high-speed, cryptographically authenticated encryption wrapper that protects file metadata, directory structures, and file payloads while enabling fast password verification before full decompression.
- Decompression across CLI and Desktop must seamlessly prompt for missing passwords in interactive sessions and fail fast with meaningful error codes in headless or automated environments.

## Decision

### 1. `.zrus` Cryptographic Envelope Specification
An encrypted `.zrus` file starts with a binary header followed by an authenticated AES-256-GCM stream.

#### Binary Header Layout
- **Magic Bytes** (4 bytes): `0x7A, 0x65, 0x6E, 0x63` (ASCII `"zenc"`).
- **Version** (1 byte): `0x01`.
- **Salt** (16 bytes): Cryptographically secure random salt for key derivation.
- **KDF Iterations** (4 bytes, Little-Endian int32): Default `100,000` iterations of PBKDF2-HMAC-SHA256.
- **Master Nonce / Base IV** (12 bytes): Base 96-bit initialization vector.
- **Password Verification Tag** (16 bytes): Derived verification digest `HMAC-SHA256(derivedKey, "RUSZIP_AUTH_CHECK")` truncated to 16 bytes. This allows the engine to validate password correctness immediately without reading the full compressed payload.

#### Authenticated Chunk Framing
Following the header, the underlying Zstandard stream is encrypted in sequential chunks (e.g. 64 KB per chunk):
- **Chunk Length** (4 bytes, Little-Endian int32): Length of ciphertext payload.
- **Chunk Nonce** (12 bytes): Sequential counter derived nonce.
- **Ciphertext Payload** (N bytes).
- **Authentication Tag** (16 bytes): AES-256-GCM tag ensuring stream integrity and tamper protection.
- An end-of-stream chunk with length `0` marks the end of the encrypted envelope.

### 2. Password Verification and Stream Decryption Skeleton
In [`src/RusZip.Core/Engines/ZstdTarArchiveEngine.cs`](../../src/RusZip.Core/Engines/ZstdTarArchiveEngine.cs):
```csharp
public static class ZrusCryptoEnvelope
{
    public static readonly byte[] Magic = [0x7A, 0x65, 0x6E, 0x63]; // "zenc"
    public const byte Version = 0x01;
    public const int DefaultIterations = 100_000;
    public const int ChunkSize = 65536; // 64 KB

    public static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        using var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(32); // 256-bit AES key
    }

    public static byte[] ComputePasswordVerificationTag(byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes("RUSZIP_AUTH_CHECK"))[..16];
    }

    public static bool IsEncrypted(Stream stream)
    {
        Span<byte> header = stackalloc byte[4];
        var read = stream.Read(header);
        stream.Seek(-read, SeekOrigin.Current);
        return read == 4 && header.SequenceEqual(Magic);
    }
}
```

### 3. Engine & Request Model Updates
- In [`src/RusZip.Core/Models/ArchiveRequests.cs`](../../src/RusZip.Core/Models/ArchiveRequests.cs):
  - Add optional `string? Password = null` to `ArchiveCompressionRequest`, `ArchiveExtractionRequest`, and `ArchiveAppendRequest`.
- In [`src/RusZip.Core/Engines/SharpCompressArchiveEngine.cs`](../../src/RusZip.Core/Engines/SharpCompressArchiveEngine.cs):
  - Configure `ZipWriterOptions` with AES-256 encryption when `request.Password` is present.
  - Pass `ReaderOptions { Password = request.Password }` for extracting encrypted `.zip`, `.7z`, and `.rar` archives.

### 4. CLI Password Integration
- In [`src/RusZip.Cli/Commands/CompressCommand.cs`](../../src/RusZip.Cli/Commands/CompressCommand.cs) and [`src/RusZip.Cli/Commands/ExtractCommand.cs`](../../src/RusZip.Cli/Commands/ExtractCommand.cs):
  - Support `-p, --password <PWD>`.
  - When extracting an encrypted archive and no password flag was supplied:
    - If `Console.IsInputRedirected` is `false` (interactive terminal), prompt for password securely using `AnsiConsole.Prompt(new TextPrompt<string>("Enter password:").Secret())`.
    - If `Console.IsInputRedirected` is `true` or `--json` is enabled, abort immediately with exit code 1 (`PASSWORD_REQUIRED`).

### 5. Desktop GUI Integration
- In [`src/RusZip.Desktop/ViewModels/CompressionSettingsViewModel.cs`](../../src/RusZip.Desktop/ViewModels/CompressionSettingsViewModel.cs):
  - Add `IsPasswordProtected`, `Password`, and `ConfirmPassword` properties with UI validation.
- In [`src/RusZip.Desktop/Views/CompressionSettingsView.axaml`](../../src/RusZip.Desktop/Views/CompressionSettingsView.axaml):
  - Add Password Protection toggle and input controls with reveal eye icon.
- When opening an encrypted archive in `RusZip.Desktop`, show `PasswordPromptDialog` to authenticate and unlock archive browsing/extraction.

## Consequences
- High-grade, authenticated AES-256-GCM encryption for native `.zrus` archives with immediate password verification.
- Cross-platform compatibility for `.zip` password creation and `.7z`/`.rar`/`.zip` decompression.
- Clean interactive masked terminal prompts and secure desktop UI workflows.
