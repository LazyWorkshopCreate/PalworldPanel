using System.Text.Json;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;
using Xunit;

namespace PalworldPanel.UnitTests;

public sealed class GameSettingsTests
{
    [Fact]
    public void AdvancedFileWritesAllTypesPreservesUnknownsAndNeverReturnsPasswords()
    {
        var root = Path.Combine(Path.GetTempPath(), "pp-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var keyFile = Path.Combine(root, "key");
            DurableFile.Write(keyFile, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var vault = new SecretVault(keyFile); var id = Guid.NewGuid().ToString("N");
            var rules = new GameRules("quoted $ # \" name", Additional: new()
            { ["ExpRate"] = "2.5", ["CrossplayPlatforms"] = "Steam,Xbox", ["DenyTechnologyList"] = "PALBOX,RepairBench",
                ["RandomizerSeed"] = "a,b\"c\\d", ["FishingDifficultyRate"] = "0.5", ["bEnableVoiceChat"] = "True" });
            var instance = new InstanceRecord(id, rules.Name, root, "pp-" + id, "palworld", "image", 18211, 18312, 18415,
                vault.Seal("synthetic-only-admin", id + ":admin"), vault.Seal("synthetic-only-game", id + ":game"), rules);
            GameSettingsFile.Write(instance, vault);
            var parsed = GameSettingsFile.Parse(File.ReadAllText(Path.Combine(root, GameSettingsFile.RelativePath)));
            Assert.Equal("2.5", parsed["ExpRate"]);
            Assert.Equal("(Steam,Xbox)", parsed["CrossplayPlatforms"]);
            Assert.Equal("a,b\"c\\d", GameSettingsFile.Decode(parsed["RandomizerSeed"]));
            Assert.Equal("True", parsed["RESTAPIEnabled"]);
            Assert.Equal("False", parsed["RCONEnabled"]);
            Assert.Equal(rules.Name, GameSettingsFile.Decode(parsed["ServerName"]));
            Assert.DoesNotContain("AdminPassword", GameSettingsFile.Read(instance).Keys);
            Assert.DoesNotContain("ServerPassword", GameSettingsFile.Read(instance).Keys);
            Assert.True(parsed.Count >= 119);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void SharedCatalogHasChineseNamesUniqueFieldsAndValidDefaults()
    {
        Assert.Equal(121, GameSettingCatalog.Definitions.Length);
        Assert.Equal(121, GameSettingCatalog.Definitions.Select(d => d.Key).Distinct().Count());
        foreach (var definition in GameSettingCatalog.Definitions)
        {
            Assert.Matches("[\u4e00-\u9fff]", definition.Label);
            if (definition.DefaultValue is not null && !definition.Secret && definition.RuleKey is null)
                Assert.NotNull(GameSettingCatalog.Encode(definition, definition.DefaultValue));
        }
    }
    [Theory]
    [InlineData("RESTAPIEnabled", "false")]
    [InlineData("AdminPassword", "synthetic-only")]
    [InlineData("ServerName", "alias-collision")]
    [InlineData("UnknownOption", "true")]
    [InlineData("ExpRate", "NaN")]
    [InlineData("ExpRate", "1,RESTAPIEnabled=False")]
    [InlineData("CrossplayPlatforms", "(Steam,Other)")]
    [InlineData("DenyTechnologyList", "(\"PALBOX\"),AdminPassword=oops")]
    [InlineData("BaseCampWorkerMaxNum", "51")]
    public void UnsafeOrProtectedParametersAreRejected(string key, string value) =>
        Assert.Throws<PanelException>(() => GameSettingCatalog.Validate(new() { [key] = value }));

    [Fact]
    public void IniPatchPreservesCommentsUnknownValuesAndQuotedCommas()
    {
        const string source = "; retained\n[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(ServerName=\"a,b\\\"c\",CrossplayPlatforms=(Steam,Xbox),Unknown=(One,Two),ExpRate=1)\n";
        var patched = GameSettingsFile.Patch(source, new Dictionary<string, string> { ["ExpRate"] = "2.5", ["DenyTechnologyList"] = "(\"PALBOX\",\"RepairBench\")" });
        var values = GameSettingsFile.Parse(patched);
        Assert.StartsWith("; retained", patched);
        Assert.Equal("a,b\"c", GameSettingsFile.Decode(values["ServerName"]));
        Assert.Equal("(One,Two)", values["Unknown"]);
        Assert.Equal("2.5", values["ExpRate"]);
        Assert.Equal("(\"PALBOX\",\"RepairBench\")", values["DenyTechnologyList"]);
    }
    [Theory]
    [InlineData("OptionSettings=(ExpRate=1,exprate=2)")]
    [InlineData("OptionSettings=(ExpRate=\"unterminated)")]
    [InlineData("OptionSettings=(CrossplayPlatforms=(Steam)")]
    [InlineData("OptionSettings=()\nOptionSettings=()")]
    public void MalformedIniCannotBeSilentlyOverwritten(string text) =>
        Assert.Throws<PanelException>(() => GameSettingsFile.Parse(text));

    [Fact]
    public void ObservationRedactsSecretsAndChecksAvailableExtendedValues()
    {
        using var document = JsonDocument.Parse("{\"ExpRate\":2.5,\"AdminPassword\":\"synthetic-only\",\"ServerPassword\":\"synthetic-only\"}");
        var projection = GameSettingsFile.Project(document.RootElement);
        Assert.Single(projection);
        Assert.True(GameSettingsFile.Matches(document.RootElement, new() { ["ExpRate"] = "2.500000" }));
        Assert.False(GameSettingsFile.Matches(document.RootElement, new() { ["ExpRate"] = "3" }));
    }
}
