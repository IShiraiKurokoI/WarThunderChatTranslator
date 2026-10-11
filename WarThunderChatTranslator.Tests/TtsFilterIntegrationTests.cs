#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Services;
using WarThunderChatTranslator.Services.ContentFiltering;
using WarThunderChatTranslator.Tests.TestSupport;

namespace WarThunderChatTranslator.Tests;

[TestClass]
[TestCategory("ModelFree")]
[DoNotParallelize]
public sealed class TtsFilterIntegrationTests
{
    private sealed class Fixture : IDisposable
    {
        public IsolatedTestEnvironment Env { get; } = new();
        public ContentFilterService Filter { get; }
        public Fixture()
        {
            Env.DefaultTtsSettings();
            Env.Set(ContentFilterConfig.FilterTtsKey, "true");
            Filter = new ContentFilterService(Env.Subdirectory("UserFilters"));
            Filter.AddBlockEntry(new() { Term = "bluenova", Severity = 2, MatchMode = ContentFilterMatchMode.Word });
        }
        public void Dispose() => Env.Dispose();
    }

    [TestMethod]
    public void RemoveMatchedTerms_ProducesCleanSpeechButKeepsSourceUnchanged()
    {
        using var f = new Fixture();
        var input = "Guard bluenova point, please";
        Assert.AreEqual("Guard point, please", f.Filter.FilterForTts(input));
        Assert.AreEqual(input, f.Filter.FilterForDisplay(input).FilteredText); // display mode is separate
    }

    [TestMethod]
    public void SkipMessageAction_DropsWholeMessageOnlyIfMatched()
    {
        using var f = new Fixture();
        f.Env.Set(ContentFilterConfig.TtsActionKey, ContentFilterConfig.TtsActionSkipMessage);
        Assert.IsNull(f.Filter.FilterForTts("Pilot says bluenova now"));
        Assert.AreEqual("Pilot says hello now", f.Filter.FilterForTts("Pilot says hello now"));
        Assert.AreEqual(string.Empty, f.Filter.FilterForTts(null));
    }

    [TestMethod]
    public void FiltersDisabled_TakesOriginalTextEvenWhenCustomBlocklistMatches()
    {
        using var f = new Fixture();
        f.Env.Set(ContentFilterConfig.FilterTtsKey, "false");
        Assert.AreEqual("bluenova", f.Filter.FilterForTts("bluenova"));
        f.Env.Set(ContentFilterConfig.FilterTtsKey, "true");
        Assert.AreEqual(string.Empty, f.Filter.FilterForTts("bluenova"));
    }

    [TestMethod]
    public async Task SkipMessageAndEmptyAfterRemoval_PreventEnqueueing()
    {
        using var f = new Fixture();
        var chat = new GameChatPollingService();
        var provider = new RecordingTtsProvider(ChatTtsConfig.ProviderWindows);
        var played = new ConcurrentQueue<string>();
        using var manager = new TtsProviderManager();
        await using var service = new ChatTtsService(chat, manager, _ => provider,
            (file, _, _) => { played.Enqueue(file.Path); return Task.CompletedTask; },
            null, f.Filter.FilterForTts);
        chat.Emit(TestWait.Chat("bluenova")); // removing the only word leaves no speech
        f.Env.Set(ContentFilterConfig.TtsActionKey, ContentFilterConfig.TtsActionSkipMessage);
        chat.Emit(TestWait.Chat("Guard bluenova point"));
        chat.Emit(TestWait.Chat("Guard the point"));
        await TestWait.UntilAsync(() => played.Count == 1, "safe message played");
        CollectionAssert.AreEqual(new[] { "Guard the point" }, provider.Calls.Select(x => x.Text).ToArray());
    }

    [TestMethod]
    public async Task IntegratedGptSynthesis_SendsOnlyFilteredTextToMockTtsEndpoint()
    {
        using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.ProviderKey, ChatTtsConfig.ProviderGptSoVits);
        f.Env.Set(ChatTtsConfig.GptSoVitsVoiceIdKey, "profile-1");
        var store = new GptSoVitsProfileStore(f.Env.Subdirectory("VoiceProfiles"));
        store.Upsert(new GptSoVitsVoiceProfile
        {
            Id = "profile-1", Name = "Voice", ReferenceAudioPath = "fake-reference.wav"
        });
        var server = new MockGptSoVitsHttpServer();
        using var provider = new GptSoVitsTtsProvider(store, new HttpClient(server));
        var chat = new GameChatPollingService();
        var played = 0;
        await using var service = new ChatTtsService(chat, null, _ => provider,
            (audio, _, _) => { Interlocked.Increment(ref played); return Task.CompletedTask; },
            null, f.Filter.FilterForTts);

        chat.Emit(TestWait.Chat("Target bluenova detected"));
        await TestWait.UntilAsync(() => Volatile.Read(ref played) == 1, "mock WAV delivered");
        Assert.AreEqual(1, server.Count("/tts"));
        using var json = JsonDocument.Parse(server.Requests.Single(r => r.Path == "/tts").Body!);
        Assert.AreEqual("Target detected", json.RootElement.GetProperty("text").GetString());
        Assert.AreEqual(0, server.Count("/set_gpt_weights"));
        Assert.AreEqual(0, server.Count("/set_sovits_weights"));
    }

    [TestMethod]
    public async Task IntegratedSkipAction_NeverCallsMockTtsForRejectedMessage()
    {
        using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.ProviderKey, ChatTtsConfig.ProviderGptSoVits);
        f.Env.Set(ChatTtsConfig.GptSoVitsVoiceIdKey, "p");
        f.Env.Set(ContentFilterConfig.TtsActionKey, ContentFilterConfig.TtsActionSkipMessage);
        var profiles = new GptSoVitsProfileStore(f.Env.Subdirectory("voices"));
        profiles.Upsert(new() { Id = "p", Name = "P", ReferenceAudioPath = "fake.wav" });
        var mock = new MockGptSoVitsHttpServer();
        using var provider = new GptSoVitsTtsProvider(profiles, new HttpClient(mock));
        var chat = new GameChatPollingService();
        var played = 0;
        await using var tts = new ChatTtsService(chat, null, _ => provider,
            (_, _, _) => { Interlocked.Increment(ref played); return Task.CompletedTask; },
            null, f.Filter.FilterForTts);
        chat.Emit(TestWait.Chat("bluenova target"));
        chat.Emit(TestWait.Chat("safe target"));
        await TestWait.UntilAsync(() => Volatile.Read(ref played) == 1, "safe text played");
        Assert.AreEqual(1, mock.Count("/tts"));
        using var json = JsonDocument.Parse(mock.Requests.Single(r => r.Path == "/tts").Body!);
        Assert.AreEqual("safe target", json.RootElement.GetProperty("text").GetString());
    }

    [TestMethod]
    public void AllowlistProtectsOverlappingMatchAndRebuildInvalidatesCache()
    {
        using var f = new Fixture();
        Assert.AreEqual("guard", f.Filter.FilterForTts("bluenova guard"));
        f.Filter.AddAllowTerm("bluenova");
        Assert.AreEqual("bluenova guard", f.Filter.FilterForTts("bluenova guard"));
        f.Filter.RemoveAllowTerm("bluenova");
        Assert.AreEqual("guard", f.Filter.FilterForTts("bluenova guard"));
    }

    [TestMethod]
    public void FullWidthCaseAndZeroWidthNormalization_DetectsEquivalentWords()
    {
        using var f = new Fixture();
        Assert.AreEqual("move", f.Filter.FilterForTts("ＢＬＵＥＮＯＶＡ move"));
        Assert.AreEqual("move", f.Filter.FilterForTts("blue\u200bnova move"));
        Assert.AreEqual("move", f.Filter.FilterForTts("BlueNova move"));
    }

    [TestMethod]
    public void WordModeHonorsWordBoundaries_ContainsModeMatchesChineseInsideText()
    {
        using var f = new Fixture();
        f.Filter.AddBlockEntry(new() { Term = "code", Severity = 2, MatchMode = ContentFilterMatchMode.Word });
        f.Filter.AddBlockEntry(new() { Term = "秘密", Severity = 2, MatchMode = ContentFilterMatchMode.Contains });
        Assert.AreEqual("decode now", f.Filter.FilterForTts("decode code now"));
        Assert.AreEqual("打听", f.Filter.FilterForTts("打听秘密"));
    }

    [TestMethod]
    public void MinimumSeverity_SuppressesLowSeverityButKeepsHighSeverity()
    {
        using var f = new Fixture();
        f.Filter.AddBlockEntry(new() { Term = "hazardvibe", Severity = 3 });
        f.Env.Set(ContentFilterConfig.MinimumSeverityKey, "3");
        f.Filter.NotifyConfigurationChanged();
        Assert.AreEqual("bluenova", f.Filter.FilterForTts("bluenova hazardvibe"));
        f.Env.Set(ContentFilterConfig.MinimumSeverityKey, "2");
        f.Filter.NotifyConfigurationChanged();
        Assert.AreEqual(string.Empty, f.Filter.FilterForTts("bluenova hazardvibe"));
    }

    [TestMethod]
    public void UserFilters_ExportImportAndReloadPreserveBlocklistAndAllowlist()
    {
        using var f = new Fixture();
        f.Filter.AddAllowTerm("something safe");
        var serialized = f.Filter.ExportPackage();
        var other = new ContentFilterService(f.Env.Subdirectory("SecondFilters"));
        other.ImportPackage(serialized, plainText: false);
        Assert.AreEqual(1, other.UserBlocklist.Count);
        Assert.AreEqual("bluenova", other.UserBlocklist[0].Term);
        Assert.AreEqual("something safe", other.UserAllowlist.Single());
        var fromDisk = new ContentFilterService(f.Env.Subdirectory("SecondFilters"));
        Assert.AreEqual("bluenova", fromDisk.UserBlocklist.Single().Term);
        other.RemoveBlockEntry("bluenova", ContentFilterMatchMode.Word);
        Assert.AreEqual(0, other.UserBlocklist.Count);
    }

    [TestMethod]
    public void DisplayAndTtsActions_AreIndependentAndCreateDisplayMessageDoesNotMutateSource()
    {
        using var f = new Fixture();
        f.Env.Set(ContentFilterConfig.FilterDisplayKey, "true");
        var source = TestWait.Chat("bluenova point");
        source.Id = 42;
        var filtered = f.Filter.CreateDisplayMessage(source);
        Assert.AreEqual("**** point", filtered.TranslatedMessage);
        Assert.AreEqual("**** point", filtered.OriginalMessage);
        Assert.AreEqual("bluenova point", source.TranslatedMessage);
        Assert.AreEqual(42, filtered.Id);
        Assert.AreEqual("point", f.Filter.FilterForTts(source.TranslatedMessage));
    }

    [TestMethod]
    public void ChangeEventsAndReload_BumpVersionAndPersistUserEntries()
    {
        using var f = new Fixture();
        var previous = f.Filter.Version;
        var raised = 0;
        f.Filter.Changed += () => Interlocked.Increment(ref raised);
        f.Filter.ImportPackage("# comment\nsecretsign\n", plainText: true);
        Assert.IsTrue(f.Filter.Version > previous);
        Assert.AreEqual(1, raised);
        Assert.AreEqual("move", f.Filter.FilterForTts("secretsign move"));
        f.Filter.ReloadFromDisk();
        Assert.AreEqual(2, raised);
        Assert.AreEqual("move", f.Filter.FilterForTts("secretsign move"));
    }
}
