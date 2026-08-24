using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace RusZip.Core.Engines;

/// <summary>
/// Implements standard WinZip AES-256 (AE-2 specification) encryption for ZIP archives.
/// Uses PBKDF2-HMAC-SHA1 key derivation, AES-256-CTR payload encryption, and HMAC-SHA1-80 authentication.
/// </summary>
public static class WinZipAesCrypto
{
    public const int SaltSize = 16;
    public const int KeySize = 32;
    public const int AuthKeySize = 32;
    public const int PvSize = 2;
    public const int AuthCodeSize = 10;
    public const int Iterations = 1000;
    public const short WinZipAesCompressionMethod = 99;

    public static readonly byte[] WinZipAesExtraField =
    [
        0x01, 0x99, // Header ID: 0x9901
        0x07, 0x00, // Data Size: 7 bytes
        0x02, 0x00, // Version 2 (AE-2: CRC is 0)
        0x41, 0x45, // Vendor ID: "AE"
        0x03,       // AES Strength: 3 (256-bit AES)
        0x08, 0x00  // Actual Compression Method: 8 (Deflate)
    ];

    public static byte[] EncryptPayload(byte[] plaintext, string password, CompressionLevel compressionLevel)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA1, KeySize + AuthKeySize + PvSize);
        byte[] encKey = derived[..KeySize];
        byte[] authKey = derived[KeySize..(KeySize + AuthKeySize)];
        byte[] pv = derived[(KeySize + AuthKeySize)..];

        byte[] deflatedData;
        using (var msDef = new MemoryStream())
        {
            using (var def = new DeflateStream(msDef, compressionLevel, leaveOpen: true))
            {
                def.Write(plaintext, 0, plaintext.Length);
            }
            deflatedData = msDef.ToArray();
        }

        byte[] ciphertext = new byte[deflatedData.Length];
        using (var aes = Aes.Create())
        {
            aes.Key = encKey;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            using var encryptor = aes.CreateEncryptor();

            byte[] counter = new byte[16];
            counter[0] = 1;
            byte[] counterOut = new byte[16];

            for (int i = 0; i < deflatedData.Length; i += 16)
            {
                encryptor.TransformBlock(counter, 0, 16, counterOut, 0);
                int blockSize = Math.Min(16, deflatedData.Length - i);
                for (int b = 0; b < blockSize; b++)
                {
                    ciphertext[i + b] = (byte)(deflatedData[i + b] ^ counterOut[b]);
                }

                for (int c = 0; c < 16; c++)
                {
                    if (++counter[c] != 0) break;
                }
            }
        }

        byte[] authCode;
        using (var hmac = new HMACSHA1(authKey))
        {
            authCode = hmac.ComputeHash(ciphertext)[..AuthCodeSize];
        }

        byte[] result = new byte[SaltSize + PvSize + ciphertext.Length + AuthCodeSize];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(pv, 0, result, SaltSize, PvSize);
        Buffer.BlockCopy(ciphertext, 0, result, SaltSize + PvSize, ciphertext.Length);
        Buffer.BlockCopy(authCode, 0, result, SaltSize + PvSize + ciphertext.Length, AuthCodeSize);

        CryptographicOperations.ZeroMemory(derived);
        CryptographicOperations.ZeroMemory(encKey);
        CryptographicOperations.ZeroMemory(authKey);

        return result;
    }
}
