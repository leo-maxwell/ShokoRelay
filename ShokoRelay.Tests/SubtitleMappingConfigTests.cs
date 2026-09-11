using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Shoko.Abstractions.Plugin;
using ShokoRelay.Config;
using ShokoRelay.Controllers;

namespace ShokoRelay.Tests;

public class SubtitleMappingConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "config-test-" + Guid.NewGuid().ToString("N"));
    private ConfigProvider Provider => new(new TestApplicationPaths(_root));

    [Fact]
    public void ConfigurationRoundTripPreservesMappingOrderAndValues()
    {
        var provider = Provider;
        var config = new RelayConfig { SeriesTitleLanguage = "EN" };
        config.Advanced.SubtitleLanguageMappings = new()
        {
            ["sc"] = "zh-Hans",
            ["chs"] = "zh-Hans",
            ["cht"] = "zh-Hant",
        };
        provider.SaveSettings(config);
        var loaded = Provider.GetSettings();
        Assert.Equal(["sc", "chs", "cht"], loaded.Advanced.SubtitleLanguageMappings.Keys);
        Assert.Equal(["zh-Hans", "zh-Hans", "zh-Hant"], loaded.Advanced.SubtitleLanguageMappings.Values);
        loaded.SeriesTitleLanguage = "JA";
        provider.SaveSettings(loaded);
        Assert.Equal(["sc", "chs", "cht"], Provider.GetSettings().Advanced.SubtitleLanguageMappings.Keys);
        Assert.Equal("JA", Provider.GetSettings().SeriesTitleLanguage);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("\"chs=zh-Hans\"")]
    [InlineData( /*lang=json,strict*/
        "{\"chs\":123}"
    )]
    [InlineData( /*lang=json,strict*/
        "{\"chs\":true}"
    )]
    [InlineData( /*lang=json,strict*/
        "{\"chs\":[]}"
    )]
    [InlineData( /*lang=json,strict*/
        "{\"chs\":{}}"
    )]
    [InlineData( /*lang=json,strict*/
        "{\"chs\":\"\\ud800\"}"
    )]
    public void MalformedMappingsUseTheExistingWholeConfigurationFallback(string mappings)
    {
        var provider = Provider;
        string path = Path.Combine(provider.ConfigDirectory, ShokoRelayConstants.FilePreferences);
        string json = "{\"SeriesTitleLanguage\":\"EN\",\"Advanced\":{\"VfsRootPath\":\"!Custom\",\"SubtitleLanguageMappings\":" + mappings + "}}";
        File.WriteAllText(path, json);
        var loaded = provider.GetSettings();
        Assert.Empty(loaded.Advanced.SubtitleLanguageMappings);
        Assert.Equal(new RelayConfig().SeriesTitleLanguage, loaded.SeriesTitleLanguage);
        Assert.Equal(new RelayConfig().Advanced.VfsRootPath, loaded.Advanced.VfsRootPath);
        Assert.Equal(json, File.ReadAllText(path)); // Loading defaults does not rewrite the user's file.
    }

    [Fact]
    public void InvalidJsonUsesExistingDefaults()
    {
        var provider = Provider;
        File.WriteAllText(Path.Combine(provider.ConfigDirectory, ShokoRelayConstants.FilePreferences), "{invalid");
        Assert.Empty(provider.GetSettings().Advanced.SubtitleLanguageMappings);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData( /*lang=json,strict*/
        "{\"Advanced\":{}}"
    )]
    [InlineData( /*lang=json,strict*/
        "{\"Advanced\":{\"SubtitleLanguageMappings\":{}}}"
    )]
    [InlineData( /*lang=json,strict*/
        "{\"Advanced\":{\"SubtitleRenameRules\":[{\"OriginalSuffix\":\"chs\",\"FinalSuffix\":\"zh-Hans\"}],\"SubtitleFormatPreference\":[\"srt\"]}}"
    )]
    public void MissingOrExperimentalSettingsDoNotEnableMappings(string json)
    {
        var provider = Provider;
        File.WriteAllText(Path.Combine(provider.ConfigDirectory, ShokoRelayConstants.FilePreferences), json);
        Assert.Empty(provider.GetSettings().Advanced.SubtitleLanguageMappings);
    }

    [Fact]
    public void NullMappingsAndNullValuesFollowStandardSerialization()
    {
        var provider = Provider;
        string path = Path.Combine(provider.ConfigDirectory, ShokoRelayConstants.FilePreferences);
        File.WriteAllText(
            path, /*lang=json,strict*/
            """{"SeriesTitleLanguage":"EN","Advanced":{"SubtitleLanguageMappings":null}}"""
        );
        var loaded = provider.GetSettings();
        Assert.Null(loaded.Advanced.SubtitleLanguageMappings);
        Assert.Equal("EN", loaded.SeriesTitleLanguage);
        provider.SaveSettings(loaded);
        Assert.Null(Provider.GetSettings().Advanced.SubtitleLanguageMappings);
        loaded.Advanced.SubtitleLanguageMappings = new() { ["chs"] = null! };
        provider.SaveSettings(loaded);
        Assert.Null(Provider.GetSettings().Advanced.SubtitleLanguageMappings["chs"]);
    }

    [Fact]
    public void DuplicateKeysUseTheExistingSerializerLastValueBehavior()
    {
        var provider = Provider;
        File.WriteAllText(
            Path.Combine(provider.ConfigDirectory, ShokoRelayConstants.FilePreferences), /*lang=json,strict*/
            """{"Advanced":{"SubtitleLanguageMappings":{"sc":"zh-Hans","chs":"ja","chs":"zh-Hans"}}}"""
        );
        var loaded = provider.GetSettings().Advanced.SubtitleLanguageMappings;
        Assert.Equal(["sc", "chs"], loaded.Keys);
        Assert.Equal("zh-Hans", loaded["chs"]);
    }

    [Fact]
    public void TrailingCommasAndUnknownPropertiesKeepExistingBehavior()
    {
        var provider = Provider;
        File.WriteAllText(
            Path.Combine(provider.ConfigDirectory, ShokoRelayConstants.FilePreferences),
            /*lang=json*/
            """{"SeriesTitleLanguage":"EN","Unused":123,"Advanced":{"SubtitleLanguageMappings":{"chs":"zh-Hans",},},}"""
        );
        Assert.Equal("EN", provider.GetSettings().SeriesTitleLanguage);
        Assert.Equal("zh-Hans", provider.GetSettings().Advanced.SubtitleLanguageMappings["chs"]);
    }

    [Fact]
    public void DashboardSchemaOmitsMappingsAndAnOrdinarySavePreservesThem()
    {
        var provider = Provider;
        var config = new RelayConfig();
        config.Advanced.SubtitleLanguageMappings = new() { ["sc"] = "zh-Hans", ["chs"] = "zh-Hans" };
        provider.SaveSettings(config);
        var controller = new DashboardController(provider, null!, null!, null!, new TestApplicationPaths(_root));
        string schema = JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(controller.GetConfigSchema()).Value);
        Assert.DoesNotContain("SubtitleLanguageMappings", schema);
        Assert.Contains("PathMappings", schema);
        var payload = JsonSerializer.Deserialize<RelayConfig>(JsonSerializer.Serialize(provider.GetDashboardConfig()))!;
        payload.SeriesTitleLanguage = "EN";
        Assert.IsType<OkObjectResult>(controller.SaveConfig(payload));
        var saved = Provider.GetSettings();
        Assert.Equal("EN", saved.SeriesTitleLanguage);
        Assert.Equal(["sc", "chs"], saved.Advanced.SubtitleLanguageMappings.Keys);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }
}

internal sealed class TestApplicationPaths(string root) : IApplicationPaths
{
    public string ApplicationPath => root;
    public string WebPath => root;
    public string DataPath => root;
    public string ImagesPath => root;
    public string PluginsPath => root;
    public string ThemesPath => root;
    public string ConfigurationsPath => root;
    public string LogsPath => root;
}
