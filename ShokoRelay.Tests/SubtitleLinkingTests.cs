using System.Collections.Concurrent;
using System.Reflection;
using ShokoRelay.Config;
using ShokoRelay.Vfs;

namespace ShokoRelay.Tests;

[CollectionDefinition("Subtitle linking", DisableParallelization = true)]
public sealed class SubtitleLinkingCollection;

[Collection("Subtitle linking")]
public class SubtitleLinkingTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "subtitles-" + Guid.NewGuid().ToString("N"));
    private readonly FieldInfo _providerField = typeof(ShokoRelay).GetField("s_configProvider", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object? _previousProvider;
    private readonly RelayConfig _settings;
    private readonly string _sourceDir;
    private readonly string _destDir;
    private readonly string _video;

    public SubtitleLinkingTests()
    {
        _sourceDir = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        _destDir = Directory.CreateDirectory(Path.Combine(_root, "vfs")).FullName;
        _video = Add("Episode.mkv");
        var provider = new ConfigProvider(new TestApplicationPaths(_root));
        _settings = provider.GetSettings();
        _previousProvider = _providerField.GetValue(null);
        _providerField.SetValue(null, provider);
    }

    private static OrderedDictionary<string, string> Mappings =>
        new()
        {
            ["chs"] = "zh-Hans",
            ["sc"] = "zh-Hans",
            ["cht"] = "zh-Hant",
            ["scjp"] = "zh-Hans",
        };

    [Theory]
    [InlineData(new[] { "sc.ass", "chs.ass", "zh-Hans.ass" }, new[] { "zh-Hans.ass=zh-Hans.ass" })]
    [InlineData(new[] { "sc.ass", "chs.ass" }, new[] { "zh-Hans.ass=chs.ass" })]
    [InlineData(new[] { "sc.ass" }, new[] { "zh-Hans.ass=sc.ass" })]
    [InlineData(new[] { "sc.ass", "chs.srt" }, new[] { "zh-Hans.ass=sc.ass", "zh-Hans.srt=chs.srt" })]
    [InlineData(new[] { "chs.ass", "chs.srt", "zh-Hans.srt" }, new[] { "zh-Hans.ass=chs.ass", "zh-Hans.srt=zh-Hans.srt" })]
    [InlineData(new[] { "chs.ass", "sc.forced.ass", "chs.forced.ass" }, new[] { "zh-Hans.ass=chs.ass", "zh-Hans.forced.ass=chs.forced.ass" })]
    [InlineData(new[] { "CHS.ASS", "sC.srt", "CHT.vtt" }, new[] { "zh-Hans.ASS=CHS.ASS", "zh-Hans.srt=sC.srt", "zh-Hant.vtt=CHT.vtt" })]
    [InlineData(new[] { "CHS.ass", "ZH-HANS.ASS" }, new[] { "ZH-HANS.ASS=ZH-HANS.ASS" })]
    [InlineData(new[] { "scjp.ass", "cht.ass" }, new[] { "zh-Hans.ass=scjp.ass", "zh-Hant.ass=cht.ass" })]
    [InlineData(new[] { "en.ass", "chs2.ass", "chsjp.ass" }, new[] { "chs2.ass=chs2.ass", "chsjp.ass=chsjp.ass", "en.ass=en.ass" })]
    public void SelectsSourcesByCompleteDestinationRegardlessOfDirectoryOrder(string[] suffixes, string[] expected)
    {
        var sources = suffixes.Select(s => Add("Episode." + s)).ToArray();
        Assert.Equal(expected, Refresh(Mappings, sources));
        Assert.Equal(expected, Refresh(Mappings, sources.Reverse()));
    }

    [Fact]
    public void ReorderingAndClearingMappingsUpdatesLinksAndPreservesSources()
    {
        string chs = Add("Episode.chs.ass");
        string sc = Add("Episode.sc.ass");
        Assert.Equal(["zh-Hans.ass=chs.ass"], Refresh(Mappings));
        Assert.Equal(["zh-Hans.ass=sc.ass"], Refresh(new() { ["sc"] = "zh-Hans", ["chs"] = "zh-Hans" }));
        Assert.Equal(["zh-Hans.ass=sc.ass"], Refresh(new() { ["sc"] = "zh-Hans", ["chs"] = "zh-Hans" }));
        Assert.Equal(["chs.ass=chs.ass", "sc.ass=sc.ass"], Refresh([]));
        Assert.Equal("Episode.chs.ass", File.ReadAllText(chs));
        Assert.Equal("Episode.sc.ass", File.ReadAllText(sc));
        Assert.Equal("Episode.mkv", File.ReadAllText(_video));
    }

    [Fact]
    public void PreservesEverySupportedFormatWithoutEnablingOtherFormats()
    {
        foreach (string extension in new[] { "ass", "ssa", "srt", "vtt", "smi", "sub", "sup" })
            Add("Episode.chs." + extension);
        Assert.Equal(["zh-Hans.ass=chs.ass", "zh-Hans.smi=chs.smi", "zh-Hans.srt=chs.srt", "zh-Hans.ssa=chs.ssa", "zh-Hans.vtt=chs.vtt"], Refresh(Mappings));
    }

    [Theory]
    [InlineData("forced")]
    [InlineData("FORCED")]
    [InlineData("sdh")]
    [InlineData("cc")]
    public void PreservesFlagsEvenWhenAConfigurationKeyNamesOne(string flag)
    {
        Add("Episode.chs." + flag + ".ass");
        Assert.Equal([$"zh-Hans.{flag}.ass=chs.{flag}.ass"], Refresh(new() { [flag] = "wrong", ["chs"] = "zh-Hans" }));
    }

    [Fact]
    public void MapsMultipleTokensOnceAndUsesTheirEarliestRuleForCollisions()
    {
        Add("Episode.chs.jp.ass");
        Add("Episode.sc.ja.ass");
        Assert.Equal(
            ["zh-Hans.ja.ass=chs.jp.ass"],
            Refresh(
                new()
                {
                    ["jp"] = "ja",
                    ["sc"] = "zh-Hans",
                    ["chs"] = "zh-Hans",
                }
            )
        );
    }

    [Fact]
    public void ReplacementsDoNotChainOrDuplicateASource()
    {
        Add("Episode.chs.ass");
        Assert.Equal(["zh-Hans.ass=chs.ass"], Refresh(new() { ["chs"] = "zh-Hans", ["zh-Hans"] = "ja" }));
        Add("Episode.zh-Hans.ass");
        Assert.Equal(["ja.ass=zh-Hans.ass", "zh-Hans.ass=chs.ass"], Refresh(new() { ["chs"] = "zh-Hans", ["zh-Hans"] = "ja" }));
    }

    [Fact]
    public void CircularAndIdentityMappingsStillProduceAtMostOneLinkPerSource()
    {
        Add("Episode.chs.ass");
        Add("Episode.zh-Hans.srt");
        Assert.Equal(["chs.srt=zh-Hans.srt", "zh-Hans.ass=chs.ass"], Refresh(new() { ["chs"] = "zh-Hans", ["zh-Hans"] = "chs" }));
        Assert.Equal(["chs.ass=chs.ass", "zh-Hans.srt=zh-Hans.srt"], Refresh(new() { ["chs"] = "CHS" }));
    }

    [Theory]
    [InlineData(".nfo")]
    [InlineData(".jpg")]
    [InlineData(".png")]
    [InlineData(".NFO")]
    public void LeavesOtherSidecarsUntouched(string extension)
    {
        Add("Episode.chs" + extension);
        Assert.Equal([$"chs{extension}=chs{extension}"], Refresh(Mappings));
    }

    [Theory]
    [InlineData("_en.srt")]
    [InlineData("-en.srt")]
    [InlineData(" [English].ass")]
    [InlineData("[English].ass")]
    [InlineData("(English).srt")]
    [InlineData("+en.srt")]
    [InlineData(".srt")]
    public void PreservesLegacyAndSuffixlessSubtitles(string suffix)
    {
        Add("Episode" + suffix);
        foreach (var mappings in new[] { Mappings, [], null! })
        {
            Refresh(mappings);
            Assert.Equal("S01E01" + suffix, Assert.Single(Directory.EnumerateFiles(_destDir).Select(Path.GetFileName)));
        }
    }

    [Fact]
    public void DoesNotClaimSubtitlesFromLongerVideoBasenames()
    {
        Add("Episode.chs.ass");
        Add("Episode2.chs.ass");
        Add("EpisodeExtra.en.srt");
        Assert.Equal(["zh-Hans.ass=chs.ass"], Refresh(Mappings));
        Assert.Equal(["chs.ass=chs.ass"], Refresh([]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../escape")]
    [InlineData("zh.Hans")]
    [InlineData("zh/Hans")]
    [InlineData("zh\0Hans")]
    public void InvalidReplacementTokensLeaveOriginalSubtitleUsable(string? value)
    {
        Add("Episode.chs.ass");
        Assert.Equal(["chs.ass=chs.ass"], Refresh(new() { ["chs"] = value! }));
    }

    [Fact]
    public void TrimsEntriesAndUsesFirstCaseInsensitiveKeyMatch()
    {
        Add("Episode.CHS.ass");
        Assert.Equal(["zh-Hans.ass=CHS.ass"], Refresh(new() { [" chs "] = " zh-Hans ", ["CHS"] = "ja" }));
    }

    [CaseSensitiveFileSystemFact]
    public void CaseOnlyAliasDuplicatesUseStableFilenameOrder()
    {
        string upper = Add("Episode.CHS.ass");
        string lower = Add("Episode.chs.ass");
        Assert.Equal(["zh-Hans.ass=CHS.ass"], Refresh(Mappings, [lower, upper]));
        Assert.Equal(["zh-Hans.ass=CHS.ass"], Refresh(Mappings, [upper, lower]));
        Assert.Equal(["CHS.ass=CHS.ass", "chs.ass=chs.ass"], Refresh([]));
    }

    [CaseSensitiveFileSystemFact]
    public void UnchangedCaseVariantsAndOtherSidecarsRemainAvailable()
    {
        foreach (string suffix in new[] { "ZH-HANS.ass", "zh-Hans.ass", "chs.ass", "en.srt", "EN.srt", "chs.NFO", "chs.nfo" })
            Add("Episode." + suffix);
        Assert.Equal(["EN.srt=EN.srt", "ZH-HANS.ass=ZH-HANS.ass", "chs.NFO=chs.NFO", "chs.nfo=chs.nfo", "en.srt=en.srt", "zh-Hans.ass=zh-Hans.ass"], Refresh(Mappings));
    }

    [Theory]
    [InlineData("missing", "chs")]
    [InlineData("cycle", "chs")]
    [InlineData("directory", "chs")]
    [InlineData("missing", "zh-Hans")]
    [InlineData("cycle", "zh-Hans")]
    public void UnavailableSourcesCannotWinCollisions(string kind, string suffix)
    {
        Add("Episode.sc.ass");
        File.CreateSymbolicLink(Path.Combine(_sourceDir, "Episode." + suffix + ".ass"), kind == "directory" ? "." : "unavailable");
        if (kind == "cycle")
            File.CreateSymbolicLink(Path.Combine(_sourceDir, "unavailable"), "Episode." + suffix + ".ass");
        Assert.Equal(["zh-Hans.ass=sc.ass"], Refresh(Mappings));
    }

    [Fact]
    public void AvailableSymlinkChainsRetainTheirImmediateSource()
    {
        Add("content");
        File.CreateSymbolicLink(Path.Combine(_sourceDir, "intermediate"), "content");
        string subtitle = Path.Combine(_sourceDir, "Episode.chs.ass");
        File.CreateSymbolicLink(subtitle, "intermediate");
        Assert.Equal(["zh-Hans.ass=chs.ass"], Refresh(Mappings));
        Assert.Equal(subtitle, File.ResolveLinkTarget(Path.Combine(_destDir, "S01E01.zh-Hans.ass"), false)!.FullName);
        Assert.Equal("intermediate", new FileInfo(subtitle).LinkTarget);
    }

    [Theory]
    [InlineData(6, 300, false)]
    [InlineData(6, 300, true)]
    [InlineData(247, 7, false)]
    [InlineData(247, 7, true)]
    public void RejectedDestinationFallsBackAndSurvivesCleanup(int baseLength, int suffixLength, bool skipExistenceCheck)
    {
        string source = Add("Episode.chs.ass");
        string destBase = new('E', baseLength);
        var mappings = new OrderedDictionary<string, string> { ["chs"] = new('x', suffixLength) };
        Refresh(mappings, destBase: destBase, skipExistenceCheck: skipExistenceCheck);
        Refresh(mappings, destBase: destBase);
        string output = Assert.Single(Directory.EnumerateFiles(_destDir));
        Assert.Equal(destBase + ".chs.ass", Path.GetFileName(output));
        Assert.Equal(source, File.ResolveLinkTarget(output, true)!.FullName);
    }

    [Theory]
    [InlineData("S01E01 [42]")]
    [InlineData("Movie (2024)")]
    public void LinksWithTvAndMovieNamesAndEitherExistenceCheckMode(string destBase)
    {
        Add("Episode.chs.ass");
        Add("Episode.sc.ass");
        Refresh(Mappings, destBase: destBase, skipExistenceCheck: true);
        Refresh(Mappings, destBase: destBase);
        Assert.Equal(destBase + ".zh-Hans.ass", Path.GetFileName(Assert.Single(Directory.EnumerateFiles(_destDir))));
    }

    [Fact]
    public void AStaleVfsLinkIsNotAnOriginalSource()
    {
        string source = Add("Episode.chs.ass");
        File.CreateSymbolicLink(Path.Combine(_destDir, "S01E01.zh-Hans.ass"), Add("stale"));
        Assert.Equal(["zh-Hans.ass=chs.ass"], Refresh(Mappings));
        Assert.Equal(source, File.ResolveLinkTarget(Path.Combine(_destDir, "S01E01.zh-Hans.ass"), true)!.FullName);
    }

    [Fact]
    public void BlueprintOnlyModeSelectsTheSameWinnerWithoutCreatingLinks()
    {
        Add("Episode.sc.ass");
        Add("Episode.chs.ass");
        _settings.Advanced.DisableVfsGeneration = true;
        Assert.Equal(["zh-Hans.ass=chs.ass"], Refresh(Mappings));
        Assert.Empty(Directory.EnumerateFiles(_destDir));
        _settings.Advanced.DisableVfsGeneration = false;
        Assert.Equal(["zh-Hans.ass=chs.ass"], Refresh(Mappings));
    }

    [Fact]
    public void UnrecoverableLinkFailureIsReportedAndOtherSubtitlesStillLink()
    {
        Add("Episode.chs.ass");
        string traditional = Add("Episode.cht.ass");
        Directory.CreateDirectory(Path.Combine(_destDir, "S01E01.zh-Hans.ass"));
        Directory.CreateDirectory(Path.Combine(_destDir, "S01E01.chs.ass"));
        _settings.Advanced.SubtitleLanguageMappings = Mappings;
        var callbacks = new Dictionary<string, string?>();
        var errors = new List<string>();
        int planned = 0,
            skipped = 0,
            created = 0;
        new VfsAssetLinker(null!).LinkEpisodeMetadata(_video, _sourceDir, "S01E01", _destDir, new(), ref planned, ref skipped, errors, ref created, (name, source) => callbacks.Add(name, source));
        Assert.Equal(1, planned);
        Assert.Equal(1, created);
        Assert.Equal(1, skipped);
        Assert.Contains("Episode.chs.ass", Assert.Single(errors));
        var callback = Assert.Single(callbacks);
        Assert.Equal("S01E01.zh-Hant.ass", callback.Key);
        Assert.Equal(traditional, callback.Value);
        Assert.Equal(traditional, File.ResolveLinkTarget(Path.Combine(_destDir, callback.Key), false)!.FullName);
    }

    private string Add(string name)
    {
        string path = Path.Combine(_sourceDir, name);
        File.WriteAllText(path, name);
        return path;
    }

    private string[] Refresh(OrderedDictionary<string, string> mappings, IEnumerable<string>? candidateOrder = null, string destBase = "S01E01", bool skipExistenceCheck = false)
    {
        _settings.Advanced.SubtitleLanguageMappings = mappings;
        var cache = new ConcurrentDictionary<string, Lazy<string[]>>(StringComparer.Ordinal);
        if (candidateOrder != null)
            cache[_sourceDir] = new Lazy<string[]>(() => [.. candidateOrder]);
        var expected = new HashSet<string>(StringComparer.Ordinal);
        var expectedTargets = new Dictionary<string, string>(StringComparer.Ordinal);
        var descriptions = new List<string>();
        var errors = new List<string>();
        int planned = 0,
            skipped = 0,
            created = 0;
        new VfsAssetLinker(null!).LinkEpisodeMetadata(
            _video,
            _sourceDir,
            destBase,
            _destDir,
            cache,
            ref planned,
            ref skipped,
            errors,
            ref created,
            (name, source) =>
            {
                expected.Add(Path.Combine(_destDir, name));
                expectedTargets.Add(Path.Combine(_destDir, name), source!);
                descriptions.Add(name[(destBase.Length + 1)..] + "=" + Path.GetFileName(source)!["Episode.".Length..]);
            },
            skipExistenceCheck
        );
        if (!_settings.Advanced.DisableVfsGeneration)
        {
            VfsHelper.CleanupOrphanedFilesAndFolders([_destDir], expected);
            Assert.Equal(expected.Order(StringComparer.Ordinal), Directory.EnumerateFiles(_destDir).Order(StringComparer.Ordinal));
            foreach (var (path, source) in expectedTargets)
                Assert.Equal(source, File.ResolveLinkTarget(path, false)!.FullName);
        }
        Assert.Empty(errors);
        Assert.Equal(0, skipped);
        Assert.Equal(expected.Count, planned);
        Assert.Equal(expected.Count, created);
        return [.. descriptions.Order(StringComparer.Ordinal)];
    }

    public void Dispose()
    {
        _providerField.SetValue(null, _previousProvider);
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }
}

public sealed class CaseSensitiveFileSystemFactAttribute : FactAttribute
{
    public CaseSensitiveFileSystemFactAttribute()
    {
        string probe = Path.Combine(AppContext.BaseDirectory, "case-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probe);
        try
        {
            File.WriteAllText(Path.Combine(probe, "lower"), "probe");
            if (File.Exists(Path.Combine(probe, "LOWER")))
                Skip = "Requires a case-sensitive test-output volume.";
        }
        finally
        {
            Directory.Delete(probe, true);
        }
    }
}
