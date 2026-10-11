#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Services;
using WarThunderChatTranslator.Tests.TestSupport;

namespace WarThunderChatTranslator.Tests;

[TestClass]
[TestCategory("ModelFree")]
[DoNotParallelize]
public sealed class TtsProviderSwitchTests
{
    [TestMethod]
    public void Manager_ResolvesAllThreeProvidersAndKeepsInstancesStable()
    {
        using var env = new IsolatedTestEnvironment();
        env.DefaultTtsSettings();
        using var manager = new TtsProviderManager();
        var windows = manager.GetProvider(ChatTtsConfig.ProviderWindows);
        var sherpa = manager.GetProvider(ChatTtsConfig.ProviderSherpaOnnx);
        var gpt = manager.GetProvider(ChatTtsConfig.ProviderGptSoVits);
        Assert.AreEqual(ChatTtsConfig.ProviderWindows, windows.Id);
        Assert.AreEqual(ChatTtsConfig.ProviderSherpaOnnx, sherpa.Id);
        Assert.AreEqual(ChatTtsConfig.ProviderGptSoVits, gpt.Id);
        Assert.AreSame(windows, manager.GetProvider(ChatTtsConfig.ProviderWindows));
        Assert.AreSame(sherpa, manager.Sherpa);
        Assert.AreSame(gpt, manager.GptSoVits);
        Assert.AreNotSame(windows, sherpa);
        Assert.AreNotSame(sherpa, gpt);
    }

    [TestMethod]
    public void SwitchingConfig_AffectsDefaultSelectionWithoutRecreatingProviders()
    {
        using var env = new IsolatedTestEnvironment();
        env.DefaultTtsSettings();
        using var manager = new TtsProviderManager();
        var windows = manager.GetProvider();
        env.Set(ChatTtsConfig.ProviderKey, ChatTtsConfig.ProviderSherpaOnnx);
        var sherpa = manager.GetProvider();
        env.Set(ChatTtsConfig.ProviderKey, ChatTtsConfig.ProviderGptSoVits);
        var gpt = manager.GetProvider();
        env.Set(ChatTtsConfig.ProviderKey, ChatTtsConfig.ProviderWindows);
        Assert.AreSame(windows, manager.GetProvider());
        Assert.AreNotSame(sherpa, gpt);
        Assert.AreSame(gpt, manager.GetProvider(ChatTtsConfig.ProviderGptSoVits));
    }

    [TestMethod]
    public void UnknownOrDifferentCaseProvider_FallsBackToWindows()
    {
        using var env = new IsolatedTestEnvironment();
        env.DefaultTtsSettings();
        env.Set(ChatTtsConfig.ProviderKey, "gptsovits");
        Assert.AreEqual(ChatTtsConfig.ProviderWindows, ChatTtsConfig.GetProvider());
        using var manager = new TtsProviderManager();
        Assert.AreEqual(ChatTtsConfig.ProviderWindows, manager.GetProvider("NotInstalled").Id);
        Assert.AreEqual(ChatTtsConfig.ProviderWindows, manager.GetProvider(null).Id);
    }

    [TestMethod]
    public void EachProviderRetainsItsIndependentVoiceAfterSwitching()
    {
        using var env = new IsolatedTestEnvironment();
        env.DefaultTtsSettings();
        env.Set(ChatTtsConfig.WindowsVoiceIdKey, "win-1");
        env.Set(ChatTtsConfig.SherpaVoiceIdKey, "sherpa-2");
        env.Set(ChatTtsConfig.GptSoVitsVoiceIdKey, "gpt-3");
        env.Set(ChatTtsConfig.ProviderKey, ChatTtsConfig.ProviderGptSoVits);
        Assert.AreEqual("gpt-3", ChatTtsConfig.GetVoiceId());
        Assert.AreEqual("win-1", ChatTtsConfig.GetVoiceId(ChatTtsConfig.ProviderWindows));
        env.Set(ChatTtsConfig.ProviderKey, ChatTtsConfig.ProviderSherpaOnnx);
        Assert.AreEqual("sherpa-2", ChatTtsConfig.GetVoiceId());
        env.Set(ChatTtsConfig.ProviderKey, ChatTtsConfig.ProviderWindows);
        Assert.AreEqual("win-1", ChatTtsConfig.GetVoiceId());
    }

    [TestMethod]
    public void ManagerDisposal_RejectsAllFurtherSelections()
    {
        using var manager = new TtsProviderManager();
        manager.Dispose();
        manager.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => manager.GetProvider());
    }

    [TestMethod]
    public void CacheKey_IsDifferentForProviderVoiceRateAndText()
    {
        var windows = TtsCacheKey.Compute("hi", "Windows", "voice", 1.0);
        var gpt = TtsCacheKey.Compute("hi", "GptSoVits", "voice", 1.0);
        Assert.AreNotEqual(windows, gpt);
        Assert.AreNotEqual(gpt, TtsCacheKey.Compute("hi", "GptSoVits", "another-voice", 1));
        Assert.AreNotEqual(gpt, TtsCacheKey.Compute("hi", "GptSoVits", "voice", 1.5));
        Assert.AreNotEqual(gpt, TtsCacheKey.Compute("hello", "GptSoVits", "voice", 1));
        Assert.AreEqual(gpt, TtsCacheKey.Compute("hi", "GptSoVits", "voice", 1));
    }
}
