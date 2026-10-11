#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Services;
using WarThunderChatTranslator.Tests.TestSupport;

namespace WarThunderChatTranslator.Tests;

[TestClass]
[TestCategory("ModelFree")]
[DoNotParallelize]
public sealed class ConfigurationPersistenceTests
{
    [TestMethod]
    public void ChatTtsSettings_ReadBackAllVoiceIdsAndBooleanFlags()
    {
        using var env = new IsolatedTestEnvironment();
        env.DefaultTtsSettings();
        Assert.IsTrue(ChatTtsConfig.IsEnabled());
        Assert.IsTrue(ChatTtsConfig.ShouldSpeakAlly());
        Assert.IsFalse(ChatTtsConfig.ShouldSpeakEnemy());
        Assert.IsFalse(ChatTtsConfig.ShouldSpeakSystem());
        env.Set(ChatTtsConfig.EnabledKey, "false");
        env.Set(ChatTtsConfig.SpeakAllyKey, "false");
        env.Set(ChatTtsConfig.SpeakEnemyKey, "true");
        env.Set(ChatTtsConfig.SpeakSystemKey, "true");
        Assert.IsFalse(ChatTtsConfig.IsEnabled());
        Assert.IsFalse(ChatTtsConfig.ShouldSpeakAlly());
        Assert.IsTrue(ChatTtsConfig.ShouldSpeakEnemy());
        Assert.IsTrue(ChatTtsConfig.ShouldSpeakSystem());
        Assert.AreEqual("windows-voice", ChatTtsConfig.GetVoiceId(ChatTtsConfig.ProviderWindows));
        Assert.AreEqual("sherpa-voice", ChatTtsConfig.GetVoiceId(ChatTtsConfig.ProviderSherpaOnnx));
        Assert.AreEqual("gpt-voice", ChatTtsConfig.GetVoiceId(ChatTtsConfig.ProviderGptSoVits));
    }

    [TestMethod]
    public void EmptyAndInvalidSettings_UseDocumentedDefaults()
    {
        using var env = new IsolatedTestEnvironment();
        foreach (var key in new[] { ChatTtsConfig.EnabledKey, ChatTtsConfig.ProviderKey,
            ChatTtsConfig.SpeakingRateKey, ChatTtsConfig.VolumeKey,
            ChatTtsConfig.QueueCapacityKey, ChatTtsConfig.MaxQueueAgeSecondsKey,
            ChatTtsConfig.SherpaNumThreadsKey, ChatTtsConfig.GptSoVitsApiUrlKey })
            env.Set(key, null);
        Assert.IsFalse(ChatTtsConfig.IsEnabled());
        Assert.AreEqual(ChatTtsConfig.ProviderWindows, ChatTtsConfig.GetProvider());
        Assert.AreEqual(ChatTtsConfig.DefaultSpeakingRate, ChatTtsConfig.GetSpeakingRate());
        Assert.AreEqual(ChatTtsConfig.DefaultVolume, ChatTtsConfig.GetVolume());
        Assert.AreEqual(ChatTtsConfig.DefaultQueueCapacity, ChatTtsConfig.GetQueueCapacity());
        Assert.AreEqual(ChatTtsConfig.DefaultMaxQueueAgeSeconds, ChatTtsConfig.GetMaxQueueAgeSecondsOrZero());
        Assert.AreEqual(ChatTtsConfig.DefaultSherpaNumThreads, ChatTtsConfig.GetSherpaNumThreads());
        Assert.AreEqual(ChatTtsConfig.DefaultGptSoVitsApiUrl, ChatTtsConfig.GetGptSoVitsApiUrl());
        env.Set(ChatTtsConfig.VolumeKey, "invalid");
        env.Set(ChatTtsConfig.QueueCapacityKey, "invalid");
        env.Set(ChatTtsConfig.SherpaNumThreadsKey, "NaN");
        Assert.AreEqual(ChatTtsConfig.DefaultVolume, ChatTtsConfig.GetVolume());
        Assert.AreEqual(ChatTtsConfig.DefaultQueueCapacity, ChatTtsConfig.GetQueueCapacity());
        Assert.AreEqual(ChatTtsConfig.DefaultSherpaNumThreads, ChatTtsConfig.GetSherpaNumThreads());
    }

    [TestMethod]
    public void OutputDevice_EmptyMeansDefaultAndSelectedEndpointPersistsAcrossProviders()
    {
        using var env = new IsolatedTestEnvironment();
        env.Set(ChatTtsConfig.OutputDeviceIdKey, null);
        Assert.AreEqual(string.Empty, ChatTtsConfig.GetOutputDeviceId());
        env.Set(ChatTtsConfig.OutputDeviceIdKey, "  render-endpoint-a  ");
        foreach (var providerId in new[] { ChatTtsConfig.ProviderWindows,
            ChatTtsConfig.ProviderSherpaOnnx, ChatTtsConfig.ProviderGptSoVits })
        {
            env.Set(ChatTtsConfig.ProviderKey, providerId);
            Assert.AreEqual("render-endpoint-a", ChatTtsConfig.GetOutputDeviceId());
        }
        env.Set(ChatTtsConfig.OutputDeviceIdKey, string.Empty);
        Assert.AreEqual(string.Empty, ChatTtsConfig.GetOutputDeviceId());
    }

    [TestMethod]
    [DataRow("0.1", 0.5)]
    [DataRow("1.25", 1.25)]
    [DataRow("10", 2.0)]
    public void SpeakingRate_ClampsConfiguredValue(string raw, double expected)
    {
        using var env = new IsolatedTestEnvironment();
        env.Set(ChatTtsConfig.SpeakingRateKey, raw);
        Assert.AreEqual(expected, ChatTtsConfig.GetSpeakingRate(), 0.00001);
    }

    [TestMethod]
    [DataRow("-5", 0.0)]
    [DataRow("0.4", 0.4)]
    [DataRow("3.1", 1.0)]
    public void Volume_ClampsToValidRange(string raw, double expected)
    {
        using var env = new IsolatedTestEnvironment();
        env.Set(ChatTtsConfig.VolumeKey, raw);
        Assert.AreEqual(expected, ChatTtsConfig.GetVolume(), 0.00001);
    }

    [TestMethod]
    public void QueueCapacity_ClampsBoundsAndSupportsDefault()
    {
        using var env = new IsolatedTestEnvironment();
        env.Set(ChatTtsConfig.QueueCapacityKey, "-100");
        Assert.AreEqual(ChatTtsConfig.MinQueueCapacity, ChatTtsConfig.GetQueueCapacity());
        env.Set(ChatTtsConfig.QueueCapacityKey, "200");
        Assert.AreEqual(ChatTtsConfig.MaxQueueCapacity, ChatTtsConfig.GetQueueCapacity());
        env.Set(ChatTtsConfig.QueueCapacityKey, "7");
        Assert.AreEqual(7, ChatTtsConfig.GetQueueCapacity());
        env.Set(ChatTtsConfig.QueueCapacityKey, "garbage");
        Assert.AreEqual(ChatTtsConfig.DefaultQueueCapacity, ChatTtsConfig.GetQueueCapacity());
    }

    [TestMethod]
    public void QueueExpiration_ZeroDisablesAndPositiveValuesClamp()
    {
        using var env = new IsolatedTestEnvironment();
        env.Set(ChatTtsConfig.MaxQueueAgeSecondsKey, "0");
        Assert.IsNull(ChatTtsConfig.GetMaxQueueAge());
        Assert.AreEqual(0, ChatTtsConfig.GetMaxQueueAgeSecondsOrZero());
        env.Set(ChatTtsConfig.MaxQueueAgeSecondsKey, "-50");
        Assert.IsNull(ChatTtsConfig.GetMaxQueueAge());
        env.Set(ChatTtsConfig.MaxQueueAgeSecondsKey, "999");
        Assert.AreEqual(TimeSpan.FromSeconds(300), ChatTtsConfig.GetMaxQueueAge());
        env.Set(ChatTtsConfig.MaxQueueAgeSecondsKey, "1");
        Assert.AreEqual(TimeSpan.FromSeconds(1), ChatTtsConfig.GetMaxQueueAge());
    }

    [TestMethod]
    public void GptEndpoint_RejectsInvalidOrUnsafeSchemesAndTrimsTrailingSlashes()
    {
        using var env = new IsolatedTestEnvironment();
        foreach (var invalid in new[] { "", "example.com:9880", "file:///tmp/model", "ftp://test.example/", "http://" })
        {
            env.Set(ChatTtsConfig.GptSoVitsApiUrlKey, invalid);
            Assert.AreEqual(ChatTtsConfig.DefaultGptSoVitsApiUrl, ChatTtsConfig.GetGptSoVitsApiUrl(), invalid);
        }
        env.Set(ChatTtsConfig.GptSoVitsApiUrlKey, " https://speech.example:9880/// ");
        Assert.AreEqual("https://speech.example:9880", ChatTtsConfig.GetGptSoVitsApiUrl());
    }

    [TestMethod]
    public void SherpaThreads_UsesConfiguredIntegerAndClampsBounds()
    {
        using var env = new IsolatedTestEnvironment();
        env.Set(ChatTtsConfig.SherpaNumThreadsKey, "7");
        Assert.AreEqual(7, ChatTtsConfig.GetSherpaNumThreads());
        env.Set(ChatTtsConfig.SherpaNumThreadsKey, "0");
        Assert.AreEqual(1, ChatTtsConfig.GetSherpaNumThreads());
        env.Set(ChatTtsConfig.SherpaNumThreadsKey, "999");
        Assert.AreEqual(16, ChatTtsConfig.GetSherpaNumThreads());
    }

    [TestMethod]
    public void ContentFilterSettings_HaveSafeDefaultsAndValidSeverityBounds()
    {
        using var env = new IsolatedTestEnvironment();
        env.Set(ContentFilterConfig.MinimumSeverityKey, null);
        env.Set(ContentFilterConfig.TtsActionKey, null);
        env.Set(ContentFilterConfig.FilterTtsKey, null);
        env.Set(ContentFilterConfig.FilterDisplayKey, null);
        Assert.AreEqual(2, ContentFilterConfig.GetMinimumSeverity());
        Assert.AreEqual(ContentFilterConfig.TtsActionRemoveMatchedTerms, ContentFilterConfig.GetTtsAction());
        Assert.IsFalse(ContentFilterConfig.ShouldFilterTts());
        Assert.IsFalse(ContentFilterConfig.ShouldFilterDisplay());
        env.Set(ContentFilterConfig.MinimumSeverityKey, "-1");
        Assert.AreEqual(1, ContentFilterConfig.GetMinimumSeverity());
        env.Set(ContentFilterConfig.MinimumSeverityKey, "100");
        Assert.AreEqual(3, ContentFilterConfig.GetMinimumSeverity());
        env.Set(ContentFilterConfig.TtsActionKey, ContentFilterConfig.TtsActionSkipMessage);
        Assert.AreEqual(ContentFilterConfig.TtsActionSkipMessage, ContentFilterConfig.GetTtsAction());
        env.Set(ContentFilterConfig.TtsActionKey, "unsafe unknown option");
        Assert.AreEqual(ContentFilterConfig.TtsActionRemoveMatchedTerms, ContentFilterConfig.GetTtsAction());
    }

    [TestMethod]
    public void AiProviders_SaveUpdateSelectDeleteAndRestoreSelection()
    {
        using var env = new IsolatedTestEnvironment();
        env.Set(ApplicationConfig.AiTranslationProvidersKey, null);
        env.Set(ApplicationConfig.AiSelectedProviderIdKey, null);
        var first = AiProviderStore.CreateDefault(AiApiType.OpenAIResponses);
        first.Id = "openai";
        first.ApiKey = "test-api-key";
        var second = AiProviderStore.CreateDefault(AiApiType.Anthropic);
        second.Id = "anthropic";
        AiProviderStore.SaveProvider(first);
        AiProviderStore.SaveProvider(second);
        Assert.AreEqual("openai", AiProviderStore.GetSelectedProviderId());
        AiProviderStore.SetSelectedProvider("anthropic");
        Assert.AreEqual("anthropic", AiProviderStore.GetSelectedProvider()!.Id);
        Assert.AreEqual(2, AiProviderStore.GetProviders().Count);
        first.Name = "Updated OpenAI";
        AiProviderStore.SaveProvider(first);
        Assert.AreEqual(2, AiProviderStore.GetProviders().Count);
        Assert.AreEqual("Updated OpenAI", AiProviderStore.GetProviders().Single(p => p.Id == "openai").Name);
        Assert.AreEqual("test-api-key", AiProviderStore.GetProviders().Single(p => p.Id == "openai").ApiKey);
        AiProviderStore.DeleteProvider("anthropic");
        Assert.AreEqual("openai", AiProviderStore.GetSelectedProviderId());
        AiProviderStore.DeleteProvider("openai");
        Assert.AreEqual(0, AiProviderStore.GetProviders().Count);
        Assert.AreEqual(string.Empty, AiProviderStore.GetSelectedProviderId());
    }

    [TestMethod]
    public void InvalidAiProviderDataAndUnknownSelection_DoNotCrashOrOverwriteSelection()
    {
        using var env = new IsolatedTestEnvironment();
        env.Set(ApplicationConfig.AiTranslationProvidersKey, "{not json");
        env.Set(ApplicationConfig.AiSelectedProviderIdKey, "previous");
        Assert.AreEqual(0, AiProviderStore.GetProviders().Count);
        Assert.IsNull(AiProviderStore.GetSelectedProvider());
        AiProviderStore.SetSelectedProvider("unknown");
        Assert.AreEqual("previous", AiProviderStore.GetSelectedProviderId());
    }

    [TestMethod]
    public void AiProviderTemplates_HaveCorrectTypeAndUsableDefaults()
    {
        Assert.AreEqual(AiApiType.OpenAIResponses, AiProviderStore.CreateDefault(AiApiType.OpenAIResponses).ApiType);
        Assert.AreEqual(AiApiType.OpenAIChatCompletions, AiProviderStore.CreateDefault(AiApiType.OpenAIChatCompletions).ApiType);
        var anthropic = AiProviderStore.CreateDefault(AiApiType.Anthropic);
        Assert.AreEqual("Anthropic", anthropic.Name);
        Assert.AreEqual(1.0, anthropic.Temperature);
        Assert.IsFalse(string.IsNullOrWhiteSpace(anthropic.Model));
    }

    [TestMethod]
    public void AppSettingsSnapshotAndDashboardVersion_TrackPersistentUpdates()
    {
        using var env = new IsolatedTestEnvironment();
        env.Set(ApplicationConfig.FontFamilyKey, "Arial");
        env.Set(ApplicationConfig.FontSizeKey, "16");
        var previous = ApplicationConfig.GetDashboardStyleVersion();
        env.Set(ApplicationConfig.FontFamilyKey, "Consolas");
        Assert.IsTrue(ApplicationConfig.GetDashboardStyleVersion() > previous);
        var snapshot = ApplicationConfig.GetSettingsSnapshot(ApplicationConfig.FontFamilyKey, ApplicationConfig.FontSizeKey, "nonexistent-key");
        Assert.AreEqual("Consolas", snapshot[ApplicationConfig.FontFamilyKey]);
        Assert.AreEqual("16", snapshot[ApplicationConfig.FontSizeKey]);
        Assert.IsFalse(snapshot.ContainsKey("nonexistent-key"));
        var version = ApplicationConfig.GetDashboardStyleVersion();
        env.Set(ApplicationConfig.FontFamilyKey, "Consolas"); // no change
        Assert.AreEqual(version, ApplicationConfig.GetDashboardStyleVersion());
    }

    [TestMethod]
    public void BattleChatClearMode_SavesNormalizedModeAndEmitsChangeNotification()
    {
        using var env = new IsolatedTestEnvironment();
        env.Set(ApplicationConfig.ClearBattleChatCacheKey, null);
        env.Set(ApplicationConfig.BattleChatClearModeKey, null);
        Assert.AreEqual(ApplicationConfig.BattleChatClearModeLogical, ApplicationConfig.GetBattleChatClearMode());
        var calls = new List<(string Before, string After)>();
        void Handler(string before, string after) => calls.Add((before, after));
        ApplicationConfig.BattleChatClearModeChanged += Handler;
        try
        {
            env.Set(ApplicationConfig.BattleChatClearModeKey, "physical");
            Assert.AreEqual(ApplicationConfig.BattleChatClearModePhysical, ApplicationConfig.GetBattleChatClearMode());
            env.Set(ApplicationConfig.BattleChatClearModeKey, "none");
            Assert.AreEqual(ApplicationConfig.BattleChatClearModeNone, ApplicationConfig.GetBattleChatClearMode());
            Assert.AreEqual("false", ApplicationConfig.GetSettings(ApplicationConfig.ClearBattleChatCacheKey));
            Assert.AreEqual(2, calls.Count);
            Assert.AreEqual((ApplicationConfig.BattleChatClearModeLogical, ApplicationConfig.BattleChatClearModePhysical), calls[0]);
        }
        finally
        {
            ApplicationConfig.BattleChatClearModeChanged -= Handler;
        }
    }
}
