using System.Security.Cryptography;
using RusZip.Cli.Models;
using RusZip.Core.Tests;
using Xunit;

namespace RusZip.Cli.Tests;

/// <summary>
/// End-to-end regression tests verifying full CLI command cycles (compress -> list -> extract),
/// cross-format command handling, JSON machine-readable outputs, and error mapping pipelines.
/// </summary>
[Collection("CliTests")]
public sealed class CliEndToEndRegressionTests : CliTestBase
{
    [Fact]
    public async Task EndToEnd_CompressListExtract_FullCycle_JsonPayloadsValid()
    {
        // 1. Arrange source hierarchy
        var srcDir = CreateTempDirectory("cli_e2e_source", fileCount: 0);
        Directory.CreateDirectory(Path.Combine(srcDir, "sub", "deep"));

        var file1 = Path.Combine(srcDir, "alpha.txt");
        var file2 = Path.Combine(srcDir, "sub", "beta.json");
        var file3 = Path.Combine(srcDir, "sub", "deep", "gamma.bin");

        var alphaContent = "Alpha test stream data";
        var betaContent = "{\"project\":\"RusZip\",\"stage\":\"release\"}";
        var gammaContent = new byte[32 * 1024];
        Random.Shared.NextBytes(gammaContent);

        await File.WriteAllTextAsync(file1, alphaContent);
        await File.WriteAllTextAsync(file2, betaContent);
        await File.WriteAllBytesAsync(file3, gammaContent);

        var archivePath = Path.Combine(TempDirectory, "cli_e2e.zrus");

        // 2. Compress via CLI in JSON mode
        var (compressCode, compressJson) = await RunCliAsync("compress", srcDir, archivePath, "-p", "high", "--json");
        Assert.Equal(0, compressCode);
        Assert.True(File.Exists(archivePath));

        var compressResult = ParseJson<CompressResult>(compressJson);
        Assert.True(compressResult.Success);
        Assert.Equal("zrus", compressResult.Format);
        Assert.Equal(3, compressResult.TotalFiles);
        Assert.True(compressResult.CompressedBytes > 0);
        Assert.True(compressResult.UncompressedBytes > 0);

        // 3. List via CLI in JSON mode
        var (listCode, listJson) = await RunCliAsync("list", archivePath, "--json");
        Assert.Equal(0, listCode);

        var listResult = ParseJson<ListResult>(listJson);
        Assert.True(listResult.Success);
        Assert.Equal("zrus", listResult.Format);
        Assert.True(listResult.TotalEntries >= 3);
        Assert.Contains(listResult.Entries, e => e.Path.EndsWith("alpha.txt"));
        Assert.Contains(listResult.Entries, e => e.Path.Contains("beta.json"));
        Assert.Contains(listResult.Entries, e => e.Path.EndsWith("gamma.bin"));

        // 4. Extract via CLI in JSON mode
        var extractDir = Path.Combine(TempDirectory, "cli_e2e_extracted");
        var (extractCode, extractJson) = await RunCliAsync("extract", archivePath, "-o", extractDir, "--json");
        Assert.Equal(0, extractCode);

        var extractResult = ParseJson<ExtractResult>(extractJson);
        Assert.True(extractResult.Success);
        Assert.Equal(3, extractResult.ExtractedFiles);

        // Verify extracted files
        Assert.Equal(alphaContent, await File.ReadAllTextAsync(Path.Combine(extractDir, "alpha.txt")));
        Assert.Equal(betaContent, await File.ReadAllTextAsync(Path.Combine(extractDir, "sub", "beta.json")));
        Assert.Equal(gammaContent, await File.ReadAllBytesAsync(Path.Combine(extractDir, "sub", "deep", "gamma.bin")));
    }

    [Theory]
    [InlineData("fast", 3)]
    [InlineData("balanced", 9)]
    [InlineData("high", 15)]
    [InlineData("ultra", 22)]
    public async Task EndToEnd_Compress_AllPresetProfiles_ExecuteSuccessfully(string profile, int expectedLevel)
    {
        var src = CreateTempFile($"file_{profile}.txt", $"Payload for profile {profile} (level {expectedLevel})");
        var archive = Path.Combine(TempDirectory, $"archive_{profile}.zrus");

        var (exitCode, stdout) = await RunCliAsync("compress", src, archive, "-p", profile, "--json");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(archive));

        var res = ParseJson<CompressResult>(stdout);
        Assert.True(res.Success);
        Assert.Equal(1, res.TotalFiles);
    }

    [Fact]
    public async Task EndToEnd_DecompressVariousFormats_ViaCliJson_ExtractsProperly()
    {
        // 1. Zip
        var zipFile = CreateTempFile("item.txt", "Zip content via CLI");
        var zipArchive = Path.Combine(TempDirectory, "archive.zip");
        await RunCliAsync("compress", zipFile, zipArchive, "--json");

        var zipExtract = Path.Combine(TempDirectory, "zip_extracted");
        var (zipExit, zipOut) = await RunCliAsync("extract", zipArchive, "-o", zipExtract, "--json");
        Assert.Equal(0, zipExit);
        Assert.True(ParseJson<ExtractResult>(zipOut).Success);
        Assert.True(File.Exists(Path.Combine(zipExtract, "item.txt")));

        // 2. 7z
        var sevenArchive = Path.Combine(TempDirectory, "test.7z");
        TestArchiveFixtures.CreateSevenZipArchive(sevenArchive, "seven.txt", "7z CLI payload");
        var sevenExtract = Path.Combine(TempDirectory, "seven_extracted");
        var (sevenExit, sevenOut) = await RunCliAsync("extract", sevenArchive, "-o", sevenExtract, "--json");
        Assert.Equal(0, sevenExit);
        Assert.True(ParseJson<ExtractResult>(sevenOut).Success);
        Assert.True(File.Exists(Path.Combine(sevenExtract, "seven.txt")));

        // 3. Tar.gz
        var tarGzArchive = Path.Combine(TempDirectory, "test.tar.gz");
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tarGzArchive, new Dictionary<string, string> { ["tar.txt"] = "TarGz CLI payload" });
        var tarGzExtract = Path.Combine(TempDirectory, "targz_extracted");
        var (tarGzExit, tarGzOut) = await RunCliAsync("extract", tarGzArchive, "-o", tarGzExtract, "--json");
        Assert.Equal(0, tarGzExit);
        Assert.True(ParseJson<ExtractResult>(tarGzOut).Success);
        Assert.True(File.Exists(Path.Combine(tarGzExtract, "tar.txt")));

        // 4. Rar4
        var rarArchive = Path.Combine(TempDirectory, "test.rar");
        TestArchiveFixtures.CreateRar4Archive(rarArchive, "rar.txt", "RAR CLI payload");
        var rarExtract = Path.Combine(TempDirectory, "rar_extracted");
        var (rarExit, rarOut) = await RunCliAsync("extract", rarArchive, "-o", rarExtract, "--json");
        Assert.Equal(0, rarExit);
        Assert.True(ParseJson<ExtractResult>(rarOut).Success);
        Assert.True(File.Exists(Path.Combine(rarExtract, "rar.txt")));
    }

    [Theory]
    [InlineData("compress", "missing_path_123.txt out.zrus", "SOURCE_NOT_FOUND", 2)]
    [InlineData("extract", "missing_archive_123.zrus -o out", "SOURCE_NOT_FOUND", 2)]
    [InlineData("list", "missing_archive_123.zrus", "SOURCE_NOT_FOUND", 2)]
    public async Task EndToEnd_ErrorPipeline_TranslatesSourceNotFoundCorrectly(string command, string extraArgs, string expectedCode, int expectedExitCode)
    {
        var args = new List<string> { command };
        if (!string.IsNullOrWhiteSpace(extraArgs))
        {
            args.AddRange(extraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        args.Add("--json");

        var (exitCode, stdout) = await RunCliAsync(args.ToArray());

        Assert.Equal(expectedExitCode, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal(expectedCode, err.Error.Code);
    }
}
