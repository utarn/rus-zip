using System.Security.Cryptography;
using System.Text;
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

    [Fact]
    public async Task EndToEnd_Compress_AllPresetProfiles_ExecuteSuccessfully_WithDistinctCompressionPerLevel()
    {
        // F-25: the previous theory asserted nothing about the level actually applied — `expectedLevel`
        // only appeared in a filename. Compress the SAME deterministic corpus at every preset and assert
        // the archives' actual compression characteristics differ per level: on a repetitive text corpus
        // the higher Zstd presets must produce a strictly smaller archive (fast > balanced, fast > ultra)
        // and the four archives must not be byte-identical. This fails if ResolveLevel ever stops mapping
        // profiles to distinct levels (e.g. always returning the default 9). The exact level that reaches
        // the engine is locked by CompressCommandTests.Compress_Profile_ResolveLevelReachesEngine.
        var corpus = Path.Combine(TempDirectory, "profile_corpus.txt");
        await File.WriteAllTextAsync(corpus, BuildProfileCorpus());

        var profiles = new[] { ("fast", 3), ("balanced", 9), ("high", 15), ("ultra", 22) };
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var (profile, _) in profiles)
        {
            var archive = Path.Combine(TempDirectory, $"archive_{profile}.zrus");
            var (exitCode, stdout) = await RunCliAsync("compress", corpus, archive, "-p", profile, "--json");

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(archive));

            var res = ParseJson<CompressResult>(stdout);
            Assert.True(res.Success);
            Assert.Equal(1, res.TotalFiles);
            sizes[profile] = res.CompressedBytes;
        }

        Assert.True(sizes["ultra"] < sizes["fast"],
            $"Expected ultra preset to compress the corpus strictly smaller than fast (fast={sizes["fast"]}, ultra={sizes["ultra"]}).");
        Assert.True(sizes["balanced"] < sizes["fast"],
            $"Expected balanced preset to compress the corpus smaller than fast (fast={sizes["fast"]}, balanced={sizes["balanced"]}).");
        Assert.True(sizes.Values.Distinct().Count() > 1,
            $"All profiles produced identical archives; ResolveLevel likely regressed (sizes={string.Join(", ", sizes.Values)}).");
    }

    /// <summary>
    /// Deterministic repetitive text corpus (fixed seed) whose Zstd output is measurably smaller at
    /// higher presets. Used by the profile e2e test so compressed-size assertions are reproducible.
    /// </summary>
    private static string BuildProfileCorpus()
    {
        var random = new Random(42);
        var sb = new StringBuilder();
        for (int i = 0; i < 2500; i++)
        {
            sb.Append($"2026-08-22 12:{i % 60:00}:{(i * 7) % 60:00} INFO request={i} user=alice action={new[] { "read", "write", "delete", "list" }[i % 4]} item=item_{i % 100} size={random.Next(1, 9999)} flag={i % 7 == 0}\n");
        }
        return sb.ToString();
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
    [InlineData("append", "missing_archive_123.zrus file.txt", "SOURCE_NOT_FOUND", 2)]
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

    [Fact]
    public async Task EndToEnd_Append_FullCycle_JsonPayloadsAndMetricsValid()
    {
        // 1. Create base archive with 2 files
        var initialDir = CreateTempDirectory("append_e2e_base", fileCount: 2);
        var archivePath = Path.Combine(TempDirectory, "append_e2e.zrus");

        var (cExit, cOut) = await RunCliAsync("compress", initialDir, archivePath, "--json");
        Assert.Equal(0, cExit);
        var cRes = ParseJson<CompressResult>(cOut);
        Assert.True(cRes.Success);
        Assert.Equal(2, cRes.TotalFiles);

        // 2. Append new directory with 3 files
        var appendDir = CreateTempDirectory("append_e2e_new", fileCount: 3);
        var (aExit, aOut) = await RunCliAsync("append", archivePath, appendDir, "--json");
        Assert.Equal(0, aExit);

        var aRes = ParseJson<RusZip.Core.Models.AppendResult>(aOut);
        Assert.True(aRes.Success);
        Assert.Equal("zrus", aRes.Format);
        Assert.Equal(3, aRes.AddedFiles);
        Assert.Equal(0, aRes.UpdatedFiles);
        Assert.Equal(2, aRes.RetainedFiles);
        Assert.Equal(0, aRes.SkippedFiles);
        Assert.Equal(5, aRes.TotalFiles);
        Assert.True(aRes.UncompressedBytes > 0);
        Assert.True(aRes.CompressedBytes > 0);
        Assert.True(aRes.CompressionRatio > 0);
        Assert.True(aRes.ElapsedMilliseconds >= 0);

        // 3. Extract and verify total 5 files exist
        var extractDir = Path.Combine(TempDirectory, "append_e2e_extracted");
        var (xExit, xOut) = await RunCliAsync("extract", archivePath, "-o", extractDir, "--json");
        Assert.Equal(0, xExit);
        var xRes = ParseJson<ExtractResult>(xOut);
        Assert.True(xRes.Success);
        Assert.Equal(5, xRes.ExtractedFiles);
    }
}
