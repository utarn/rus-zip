using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace RusZip.Core.Tests;

public static class TestArchiveFixtures
{
    private static readonly uint[] Crc32Table = CreateCrc32Table();

    private static uint[] CreateCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
            {
                if ((c & 1) != 0)
                {
                    c = 0xEDB88320 ^ (c >> 1);
                }
                else
                {
                    c >>= 1;
                }
            }
            table[i] = c;
        }
        return table;
    }

    private static ushort ComputeRar4HeaderCrc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return (ushort)~crc;
    }

    private static uint ComputeRar4DataCrc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return ~crc;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return ~crc;
    }

    public static async Task CreateGzArchiveAsync(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var data = Encoding.UTF8.GetBytes(content);
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        await gz.WriteAsync(data);
    }

    public static async Task CreateTarGzArchiveAsync(string path, IDictionary<string, string> files)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        await using var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: false);

        foreach (var (entryPath, content) in files)
        {
            var data = Encoding.UTF8.GetBytes(content);
            using var ms = new MemoryStream(data);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, entryPath)
            {
                DataStream = ms
            };
            await tar.WriteEntryAsync(entry);
        }
    }

    public static async Task CreateTarSlipArchiveAsync(string path, string maliciousEntryName = "../../malicious.txt")
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        await using var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: false);

        var data = Encoding.UTF8.GetBytes("malicious tar payload");
        using var ms = new MemoryStream(data);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, maliciousEntryName)
        {
            DataStream = ms
        };
        await tar.WriteEntryAsync(entry);
    }

    public static void CreateEncryptedZipArchive(string path, string filename = "secret.txt", string content = "classified data")
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var fileBytes = Encoding.UTF8.GetBytes(content);
        var nameBytes = Encoding.UTF8.GetBytes(filename);
        uint crc = ComputeCrc32(fileBytes);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Local file header (PK\x03\x04)
        bw.Write(0x04034b50); // Signature
        bw.Write((ushort)20); // Version needed
        bw.Write((ushort)0x0001); // Flags: Bit 0 = Encrypted!
        bw.Write((ushort)0); // Method: Store
        bw.Write((ushort)0x4B3A); // Mod time
        bw.Write((ushort)0x7021); // Mod date
        bw.Write(crc);
        bw.Write((uint)fileBytes.Length); // Compressed
        bw.Write((uint)fileBytes.Length); // Uncompressed
        bw.Write((ushort)nameBytes.Length);
        bw.Write((ushort)0); // Extra field len
        bw.Write(nameBytes);
        bw.Write(fileBytes);

        uint centralDirOffset = (uint)ms.Position;

        // Central directory header (PK\x01\x02)
        bw.Write(0x02014b50); // Signature
        bw.Write((ushort)20); // Version made by
        bw.Write((ushort)20); // Version needed
        bw.Write((ushort)0x0001); // Flags: Bit 0 = Encrypted!
        bw.Write((ushort)0); // Method: Store
        bw.Write((ushort)0x4B3A);
        bw.Write((ushort)0x7021);
        bw.Write(crc);
        bw.Write((uint)fileBytes.Length);
        bw.Write((uint)fileBytes.Length);
        bw.Write((ushort)nameBytes.Length);
        bw.Write((ushort)0); // Extra len
        bw.Write((ushort)0); // Comment len
        bw.Write((ushort)0); // Disk start
        bw.Write((ushort)0); // Internal attr
        bw.Write((uint)0x20); // External attr
        bw.Write((uint)0); // Relative offset
        bw.Write(nameBytes);

        uint centralDirSize = (uint)ms.Position - centralDirOffset;

        // End of central directory (PK\x05\x06)
        bw.Write(0x06054b50);
        bw.Write((ushort)0); // Disk num
        bw.Write((ushort)0); // Start disk
        bw.Write((ushort)1); // Total entries on disk
        bw.Write((ushort)1); // Total entries
        bw.Write(centralDirSize);
        bw.Write(centralDirOffset);
        bw.Write((ushort)0); // Comment len

        File.WriteAllBytes(path, ms.ToArray());
    }

    public static void CreateZipSlipArchive(string path, string maliciousEntryName = "../../evil.txt", string content = "malicious payload")
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var fileBytes = Encoding.UTF8.GetBytes(content);
        var nameBytes = Encoding.UTF8.GetBytes(maliciousEntryName);
        uint crc = ComputeCrc32(fileBytes);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Local file header (PK\x03\x04)
        bw.Write(0x04034b50);
        bw.Write((ushort)20);
        bw.Write((ushort)0);
        bw.Write((ushort)0); // Store
        bw.Write((ushort)0x4B3A);
        bw.Write((ushort)0x7021);
        bw.Write(crc);
        bw.Write((uint)fileBytes.Length);
        bw.Write((uint)fileBytes.Length);
        bw.Write((ushort)nameBytes.Length);
        bw.Write((ushort)0);
        bw.Write(nameBytes);
        bw.Write(fileBytes);

        uint centralDirOffset = (uint)ms.Position;

        // Central directory header (PK\x01\x02)
        bw.Write(0x02014b50);
        bw.Write((ushort)20);
        bw.Write((ushort)20);
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write((ushort)0x4B3A);
        bw.Write((ushort)0x7021);
        bw.Write(crc);
        bw.Write((uint)fileBytes.Length);
        bw.Write((uint)fileBytes.Length);
        bw.Write((ushort)nameBytes.Length);
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write((uint)0x20);
        bw.Write((uint)0);
        bw.Write(nameBytes);

        uint centralDirSize = (uint)ms.Position - centralDirOffset;

        // End of central directory (PK\x05\x06)
        bw.Write(0x06054b50);
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)1);
        bw.Write(centralDirSize);
        bw.Write(centralDirOffset);
        bw.Write((ushort)0);

        File.WriteAllBytes(path, ms.ToArray());
    }

    public static void CreateSevenZipArchive(string path, string filename = "test.txt", string content = "Hello 7-Zip!")
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var data = Encoding.UTF8.GetBytes(content);
        var nameU16 = Encoding.Unicode.GetBytes(filename + "\0");
        uint contentCrc = ComputeCrc32(data);

        // NextHeader construction for Copy (Store) method
        using var nhMs = new MemoryStream();
        nhMs.WriteByte(0x01); // Header (0x01)

        // MainStreamsInfo (0x04)
        nhMs.WriteByte(0x04);

        // PackInfo (0x06)
        nhMs.WriteByte(0x06);
        nhMs.WriteByte(0x00); // PackPos = 0
        nhMs.WriteByte(0x01); // NumPackStreams = 1
        nhMs.WriteByte(0x09); // Size property (0x09)
        Write7zVarInt(nhMs, (ulong)data.Length);
        nhMs.WriteByte(0x0A); // CRC property (0x0A)
        nhMs.WriteByte(0x01); // All defined
        WriteUInt32Le(nhMs, contentCrc);
        nhMs.WriteByte(0x00); // End PackInfo (0x00)

        // UnpackInfo (0x07)
        nhMs.WriteByte(0x07);
        nhMs.WriteByte(0x0B); // Folder property (0x0B)
        nhMs.WriteByte(0x01); // NumFolders = 1
        nhMs.WriteByte(0x00); // External = 0
        nhMs.WriteByte(0x01); // NumCoders = 1
        nhMs.WriteByte(0x01); // Coder flags (1 byte method ID, simple coder)
        nhMs.WriteByte(0x00); // Method = Copy (0x00)
        nhMs.WriteByte(0x0C); // CodersUnpackSize property (0x0C)
        Write7zVarInt(nhMs, (ulong)data.Length); // UnpackSize
        nhMs.WriteByte(0x0A); // CRC property (0x0A)
        nhMs.WriteByte(0x01); // All defined
        WriteUInt32Le(nhMs, contentCrc);
        nhMs.WriteByte(0x00); // End UnpackInfo (0x00)

        // SubStreamsInfo (0x08)
        nhMs.WriteByte(0x08);
        nhMs.WriteByte(0x00); // End SubStreamsInfo

        nhMs.WriteByte(0x00); // End MainStreamsInfo

        // FilesInfo (0x05)
        nhMs.WriteByte(0x05);
        nhMs.WriteByte(0x01); // NumFiles = 1
        nhMs.WriteByte(0x11); // Name property (0x11)
        Write7zVarInt(nhMs, (ulong)(nameU16.Length + 1));
        nhMs.WriteByte(0x00); // External = 0
        nhMs.Write(nameU16);
        nhMs.WriteByte(0x00); // End FilesInfo

        nhMs.WriteByte(0x00); // End Header

        var nextHeaderBytes = nhMs.ToArray();
        uint nextHeaderCrc = ComputeCrc32(nextHeaderBytes);

        // StartHeader
        using var shBodyMs = new MemoryStream();
        WriteUInt64Le(shBodyMs, (ulong)data.Length); // NextHeaderOffset
        WriteUInt64Le(shBodyMs, (ulong)nextHeaderBytes.Length); // NextHeaderSize
        WriteUInt32Le(shBodyMs, nextHeaderCrc); // NextHeaderCRC
        var shBody = shBodyMs.ToArray();
        uint shBodyCrc = ComputeCrc32(shBody);

        using var fullMs = new MemoryStream();
        // 7z Signature: 37 7a bc af 27 1c
        fullMs.Write([0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]);
        // Version 0.4
        fullMs.Write([0x00, 0x04]);
        WriteUInt32Le(fullMs, shBodyCrc);
        fullMs.Write(shBody);
        fullMs.Write(data);
        fullMs.Write(nextHeaderBytes);

        File.WriteAllBytes(path, fullMs.ToArray());
    }

    public static void CreateEncryptedSevenZipArchive(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Encrypted 7z archive with EncodedHeader (0x17) and 7z AES256 coder ID [0x06, 0xF1, 0x07, 0x01]
        using var nhMs = new MemoryStream();
        nhMs.WriteByte(0x17); // EncodedHeader
        nhMs.WriteByte(0x06); // PackInfo
        nhMs.WriteByte(0x00); // PackPos
        nhMs.WriteByte(0x01); // NumStreams
        nhMs.WriteByte(0x09); // Size
        Write7zVarInt(nhMs, 16);
        nhMs.WriteByte(0x00); // End PackInfo
        nhMs.WriteByte(0x07); // UnpackInfo
        nhMs.WriteByte(0x0B); // Folder
        nhMs.WriteByte(0x01); // NumFolders
        nhMs.WriteByte(0x00); // External
        nhMs.WriteByte(0x01); // NumCoders
        nhMs.WriteByte(0x24); // 0x20 (has properties) | 0x04 (4-byte method ID)
        nhMs.Write([0x06, 0xF1, 0x07, 0x01]); // 7zAES
        nhMs.WriteByte(0x01); // 1 byte property len
        nhMs.WriteByte(0x13); // Property (NumCyclesPower = 19)
        nhMs.WriteByte(0x0C); // CodersUnpackSize
        Write7zVarInt(nhMs, 16);
        nhMs.WriteByte(0x00); // End UnpackInfo
        nhMs.WriteByte(0x00); // End EncodedHeader

        var nextHeaderBytes = nhMs.ToArray();
        uint nextHeaderCrc = ComputeCrc32(nextHeaderBytes);

        using var shBodyMs = new MemoryStream();
        WriteUInt64Le(shBodyMs, 16);
        WriteUInt64Le(shBodyMs, (ulong)nextHeaderBytes.Length);
        WriteUInt32Le(shBodyMs, nextHeaderCrc);
        var shBody = shBodyMs.ToArray();
        uint shBodyCrc = ComputeCrc32(shBody);

        using var fullMs = new MemoryStream();
        fullMs.Write([0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04]);
        WriteUInt32Le(fullMs, shBodyCrc);
        fullMs.Write(shBody);
        fullMs.Write(new byte[16]); // Dummy encrypted payload
        fullMs.Write(nextHeaderBytes);

        File.WriteAllBytes(path, fullMs.ToArray());
    }

    public static void CreateSevenZipSlipArchive(string path, string maliciousEntryName = "../../evil.txt")
    {
        CreateSevenZipArchive(path, maliciousEntryName, "malicious 7z payload");
    }

    public static void CreateRar4Archive(string path, string filename = "hello.txt", string content = "Hello RAR4!", bool encrypted = false, bool multiVolume = false)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var data = Encoding.UTF8.GetBytes(content);
        var nameBytes = Encoding.UTF8.GetBytes(filename);
        uint fileCrc32 = ComputeRar4DataCrc(data);

        using var ms = new MemoryStream();

        // 1. Marker block
        ms.Write([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00]);

        // 2. Main archive header (0x73)
        ushort mainFlags = 0;
        using (var mainBodyMs = new MemoryStream())
        {
            mainBodyMs.WriteByte(0x73); // Type
            WriteUInt16Le(mainBodyMs, mainFlags);
            WriteUInt16Le(mainBodyMs, 13); // Header Size
            WriteUInt16Le(mainBodyMs, 0); // Reserved1
            WriteUInt32Le(mainBodyMs, 0); // Reserved2
            var mainData = mainBodyMs.ToArray();
            ushort mainCrc16 = ComputeRar4HeaderCrc(mainData);
            WriteUInt16Le(ms, mainCrc16);
            ms.Write(mainData);
        }

        // 3. File header (0x74)
        ushort fileFlags = (ushort)(0x8000 | (encrypted ? 0x0404 : 0x0000) | (multiVolume ? 0x0002 : 0x0000));
        ushort headSize = (ushort)(32 + nameBytes.Length + (encrypted ? 8 : 0));
        using (var fileBodyMs = new MemoryStream())
        {
            fileBodyMs.WriteByte(0x74); // Type
            WriteUInt16Le(fileBodyMs, fileFlags);
            WriteUInt16Le(fileBodyMs, headSize);
            WriteUInt32Le(fileBodyMs, (uint)data.Length); // PackSize
            WriteUInt32Le(fileBodyMs, (uint)data.Length); // UnpSize
            fileBodyMs.WriteByte(0); // HostOS (MS-DOS)
            WriteUInt32Le(fileBodyMs, fileCrc32);
            WriteUInt32Le(fileBodyMs, 0x4B3A7021); // DOS datetime
            fileBodyMs.WriteByte(20); // UnpVer = 20 (RAR 2.0)
            fileBodyMs.WriteByte(0x30); // Method: Store
            WriteUInt16Le(fileBodyMs, (ushort)nameBytes.Length);
            WriteUInt32Le(fileBodyMs, 0x20); // Attr
            fileBodyMs.Write(nameBytes);
            if (encrypted)
            {
                fileBodyMs.Write(new byte[8]); // 8 bytes Salt
            }

            var fileHeadData = fileBodyMs.ToArray();
            ushort fileCrc16 = ComputeRar4HeaderCrc(fileHeadData);
            WriteUInt16Le(ms, fileCrc16);
            ms.Write(fileHeadData);
            ms.Write(data);
        }

        // 4. End of archive block (0x7B)
        using (var endBodyMs = new MemoryStream())
        {
            endBodyMs.WriteByte(0x7B);
            WriteUInt16Le(endBodyMs, 0x4000);
            WriteUInt16Le(endBodyMs, 7);
            var endData = endBodyMs.ToArray();
            ushort endCrc16 = ComputeRar4HeaderCrc(endData);
            WriteUInt16Le(ms, endCrc16);
            ms.Write(endData);
        }

        File.WriteAllBytes(path, ms.ToArray());
    }

    public static void CreateRar5Archive(string path, string filename = "hello5.txt", string content = "Hello RAR5!", bool encrypted = false)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var data = Encoding.UTF8.GetBytes(content);
        var nameBytes = Encoding.UTF8.GetBytes(filename);
        uint fileCrc32 = ComputeCrc32(data);

        using var ms = new MemoryStream();
        // Marker for RAR5: Rar!\x1a\x07\x01\x00
        ms.Write([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]);

        if (encrypted)
        {
            // Type 4: Archive Encryption Header in RAR5
            // Encryption Record: Type (1), Version (0), Flags (0), KDF Count (16), Salt (16 bytes), PswCheck (8 bytes)
            using var recMs = new MemoryStream();
            recMs.Write(EncodeRar5VInt(0)); // Version
            recMs.Write(EncodeRar5VInt(0)); // Flags
            recMs.WriteByte(16); // KDF Count
            recMs.Write(new byte[16]); // Salt
            recMs.Write(new byte[8]); // PswCheck
            var recBytes = recMs.ToArray();

            ms.Write(MakeRar5Header(4, 0, recBytes));
        }

        // Main archive header (Type = 1, Flags = 0)
        ms.Write(MakeRar5Header(1, 0, EncodeRar5VInt(0)));

        // File header (Type = 2, Flags = 0x0002 for data size)
        // FileFlags = 0x0004 (Has CRC32)
        ulong fileFlags = 0x0004;

        using var fileBodyMs = new MemoryStream();
        fileBodyMs.Write(EncodeRar5VInt(fileFlags)); // File flags
        fileBodyMs.Write(EncodeRar5VInt((ulong)data.Length)); // Uncompressed size
        fileBodyMs.Write(EncodeRar5VInt(0x20)); // Attributes
        WriteUInt32Le(fileBodyMs, fileCrc32);
        fileBodyMs.Write(EncodeRar5VInt(0)); // Method 0 (Store)
        fileBodyMs.Write(EncodeRar5VInt(0)); // Host OS
        fileBodyMs.Write(EncodeRar5VInt((ulong)nameBytes.Length));
        fileBodyMs.Write(nameBytes);

        var fileHeader = MakeRar5Header(2, 0x0002, fileBodyMs.ToArray(), (ulong)data.Length);
        ms.Write(fileHeader);
        ms.Write(data);

        // End header (Type = 5, Flags = 0)
        ms.Write(MakeRar5Header(5, 0, EncodeRar5VInt(0)));

        File.WriteAllBytes(path, ms.ToArray());
    }

    private static byte[] MakeRar5Header(ulong htype, ulong flags, byte[] body, ulong? dataSize = null)
    {
        using var hMs = new MemoryStream();
        hMs.Write(EncodeRar5VInt(htype));
        hMs.Write(EncodeRar5VInt(flags));
        if (dataSize.HasValue)
        {
            hMs.Write(EncodeRar5VInt(dataSize.Value));
        }
        hMs.Write(body);

        var hFields = hMs.ToArray();
        var hSize = EncodeRar5VInt((ulong)hFields.Length);

        using var allMs = new MemoryStream();
        allMs.Write(hSize);
        allMs.Write(hFields);
        var allBytes = allMs.ToArray();

        uint crc = ComputeCrc32(allBytes);

        using var resMs = new MemoryStream();
        WriteUInt32Le(resMs, crc);
        resMs.Write(allBytes);
        return resMs.ToArray();
    }

    private static byte[] EncodeRar5VInt(ulong value)
    {
        var list = new List<byte>();
        while (true)
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                list.Add((byte)(b | 0x80));
            }
            else
            {
                list.Add(b);
                break;
            }
        }
        return [.. list];
    }

    private static void Write7zVarInt(Stream stream, ulong value)
    {
        if (value < 0x80)
        {
            stream.WriteByte((byte)value);
        }
        else if (value < 0x4000)
        {
            stream.WriteByte((byte)(0x80 | (value >> 8)));
            stream.WriteByte((byte)(value & 0xFF));
        }
        else
        {
            var buf = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(buf, value);
            stream.Write(buf);
        }
    }

    private static void WriteUInt16Le(Stream stream, ushort value)
    {
        var buf = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
        stream.Write(buf);
    }

    private static void WriteUInt32Le(Stream stream, uint value)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        stream.Write(buf);
    }

    private static void WriteUInt64Le(Stream stream, ulong value)
    {
        var buf = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, value);
        stream.Write(buf);
    }
}
