#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Services;
using WarThunderChatTranslator.Tests.TestSupport;
using Windows.Storage;

namespace WarThunderChatTranslator.Tests;

[TestClass]
[TestCategory("ModelFree")]
[DoNotParallelize]
public sealed class ChatTtsQueueTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public IsolatedTestEnvironment Env { get; } = new();
        public GameChatPollingService Chat { get; } = new();
        public MutableClock Clock { get; } = new();
        public RecordingTtsProvider Windows { get; } = new(ChatTtsConfig.ProviderWindows);
        public RecordingTtsProvider Sherpa { get; } = new(ChatTtsConfig.ProviderSherpaOnnx);
        public RecordingTtsProvider Gpt { get; } = new(ChatTtsConfig.ProviderGptSoVits);
        public ConcurrentQueue<(string File, double Volume)> Played { get; } = new();
        public ChatTtsService Service { get; }

        public Fixture(Func<string?, string?>? filter = null)
        {
            Env.DefaultTtsSettings();
            Service = new ChatTtsService(Chat, null,
                id => id switch
                {
                    ChatTtsConfig.ProviderGptSoVits => Gpt,
                    ChatTtsConfig.ProviderSherpaOnnx => Sherpa,
                    _ => Windows
                },
                (file, volume, _) => { Played.Enqueue((file.Path, volume)); return Task.CompletedTask; },
                Clock.GetNow,
                filter ?? (text => text));
        }

        public void Send(string text, string? sender = "ally", bool enemy = false, bool succeeded = true)
            => Chat.Emit(TestWait.Chat(text, sender, enemy, succeeded));

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            Env.Dispose();
        }
    }

    [TestMethod]
    public async Task BasicQueue_SpeaksSuccessfulAllyMessageWithSavedVoiceRateAndVolume()
    {
        await using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.SpeakingRateKey, "1.45");
        f.Env.Set(ChatTtsConfig.VolumeKey, "0.35");
        f.Send("  Guard the B point  ");
        await TestWait.UntilAsync(() => f.Played.Count == 1, "translated ally should play");
        Assert.AreEqual("Guard the B point", f.Windows.Calls.Single().Text);
        Assert.AreEqual("windows-voice", f.Windows.Calls.Single().VoiceId);
        Assert.AreEqual(1.45, f.Windows.Calls.Single().Rate);
        Assert.AreEqual(0.35, f.Played.Single().Volume);
    }

    [TestMethod]
    public async Task DisabledServiceOrFailedTranslation_DoesNotSynthesize()
    {
        await using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.EnabledKey, "false");
        f.Send("should be ignored");
        f.Env.Set(ChatTtsConfig.EnabledKey, "true");
        f.Send("translation failed", succeeded: false);
        f.Send("accepted");
        await TestWait.UntilAsync(() => f.Played.Count == 1, "accepted message should play");
        CollectionAssert.AreEqual(new[] { "accepted" }, f.Windows.Calls.Select(x => x.Text).ToArray());
    }

    [TestMethod]
    public async Task AllyEnemySystemFlags_AreEvaluatedIndependently()
    {
        await using var f = new Fixture();
        f.Send("ally allowed");
        f.Send("enemy denied", enemy: true);
        f.Send("system denied", sender: "");
        f.Env.Set(ChatTtsConfig.SpeakAllyKey, "false");
        f.Env.Set(ChatTtsConfig.SpeakEnemyKey, "true");
        f.Env.Set(ChatTtsConfig.SpeakSystemKey, "true");
        f.Send("ally denied");
        f.Send("enemy allowed", enemy: true);
        f.Send("system allowed", sender: null);
        await TestWait.UntilAsync(() => f.Played.Count == 3, "all three allowed messages");
        CollectionAssert.AreEqual(new[] { "ally allowed", "enemy allowed", "system allowed" },
            f.Windows.Calls.Select(x => x.Text).ToArray());
    }

    [TestMethod]
    public async Task QueueCapacity_DropsOldWaitingMessagesAndSkipsStaleInFlightPlayback()
    {
        await using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.QueueCapacityKey, "2");
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Windows.Gate = gate;
        f.Send("first");
        await TestWait.UntilAsync(() => f.Windows.Calls.Count == 1, "first synthesis started");
        f.Send("second");
        f.Send("third");
        f.Send("fourth");
        f.Send("fifth");
        gate.SetResult(true);
        await TestWait.UntilAsync(() => f.Played.Count == 2, "two newest messages played");
        CollectionAssert.AreEqual(new[] { "first", "fourth", "fifth" },
            f.Windows.Calls.Select(x => x.Text).ToArray());
        Assert.AreEqual(2, f.Played.Count); // The final two are in window; first was dropped after synthesis.
    }

    [TestMethod]
    public async Task QueueCapacityChange_IsAppliedToAlreadyQueuedJobs()
    {
        await using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.QueueCapacityKey, "10");
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Windows.Gate = gate;
        f.Send("busy");
        await TestWait.UntilAsync(() => f.Windows.Calls.Count == 1, "first synthesis started");
        f.Send("obsolete-one");
        f.Send("obsolete-two");
        f.Send("current");
        f.Env.Set(ChatTtsConfig.QueueCapacityKey, "1");
        gate.SetResult(true);
        await TestWait.UntilAsync(() => f.Played.Count == 1, "current job played");
        CollectionAssert.AreEqual(new[] { "busy", "current" }, f.Windows.Calls.Select(x => x.Text).ToArray());
    }

    [TestMethod]
    public async Task QueueExpiration_UsesEnqueueTimeAndSkipsOldJobsWithoutSleeping()
    {
        await using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.MaxQueueAgeSecondsKey, "2");
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Windows.Gate = gate;
        f.Send("expired-in-flight");
        await TestWait.UntilAsync(() => f.Windows.Calls.Count == 1, "first synthesis started");
        f.Send("expired-in-queue");
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        gate.SetResult(true);
        // The first job is already in flight. When it finishes, the second must be dropped by age.
        f.Send("fresh");
        await TestWait.UntilAsync(() => f.Played.Count == 1, "fresh job played");
        CollectionAssert.AreEqual(new[] { "expired-in-flight", "fresh" },
            f.Windows.Calls.Select(x => x.Text).ToArray());
    }

    [TestMethod]
    public async Task QueueMaximumAgeZero_DisablesExpiration()
    {
        await using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.MaxQueueAgeSecondsKey, "0");
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Windows.Gate = gate;
        f.Send("old-first");
        await TestWait.UntilAsync(() => f.Windows.Calls.Count == 1, "first synthesis started");
        f.Send("old-second");
        f.Clock.Advance(TimeSpan.FromHours(2));
        gate.SetResult(true);
        await TestWait.UntilAsync(() => f.Played.Count == 2, "no TTL means both play");
        Assert.AreEqual(2, f.Windows.Calls.Count);
    }

    [TestMethod]
    public async Task ProviderSwitch_KeepsProviderAndVoiceSnapshotForAlreadyQueuedJobs()
    {
        await using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.QueueCapacityKey, "10");
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Windows.Gate = gate;
        f.Send("on windows");
        await TestWait.UntilAsync(() => f.Windows.Calls.Count == 1, "windows busy");
        f.Env.Set(ChatTtsConfig.ProviderKey, ChatTtsConfig.ProviderGptSoVits);
        f.Env.Set(ChatTtsConfig.GptSoVitsVoiceIdKey, "character-42");
        f.Send("on gpt");
        gate.SetResult(true);
        await TestWait.UntilAsync(() => f.Played.Count == 2, "both provider jobs played");
        Assert.AreEqual("windows-voice", f.Windows.Calls.Single().VoiceId);
        Assert.AreEqual("character-42", f.Gpt.Calls.Single().VoiceId);
        Assert.AreEqual("on gpt", f.Gpt.Calls.Single().Text);
    }

    [TestMethod]
    public async Task FailureOfOneJob_DoesNotStopQueueProcessing()
    {
        await using var f = new Fixture();
        f.Windows.ErrorForCall = call => call.Text == "bad" ? new InvalidOperationException("fake synthesizer down") : null;
        f.Send("bad");
        f.Send("good");
        await TestWait.UntilAsync(() => f.Played.Count == 1, "queue continues after failed synthesis");
        CollectionAssert.AreEqual(new[] { "bad", "good" }, f.Windows.Calls.Select(x => x.Text).ToArray());
    }

    [TestMethod]
    public async Task Preview_UsesCurrentProviderAndConfiguredRateVolume()
    {
        await using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.ProviderKey, ChatTtsConfig.ProviderSherpaOnnx);
        f.Env.Set(ChatTtsConfig.SpeakingRateKey, "1.8");
        f.Env.Set(ChatTtsConfig.VolumeKey, "0.2");
        await f.Service.PreviewAsync("  Check six! ");
        Assert.AreEqual("Check six!", f.Sherpa.Calls.Single().Text);
        Assert.AreEqual("sherpa-voice", f.Sherpa.Calls.Single().VoiceId);
        Assert.AreEqual(1.8, f.Sherpa.Calls.Single().Rate);
        Assert.AreEqual(0.2, f.Played.Single().Volume);
        Assert.AreSame(f.Sherpa, f.Service.Provider);
        Assert.AreSame(f.Gpt, f.Service.GetProvider(ChatTtsConfig.ProviderGptSoVits));
    }

    [TestMethod]
    public async Task Preview_UsesFallbackTextWhenBlankAndHonorsCancellation()
    {
        await using var f = new Fixture();
        await f.Service.PreviewAsync(" \n ");
        Assert.AreEqual("Enemy on the capture point.", f.Windows.Calls.Single().Text);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Windows.Gate = gate;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => f.Service.PreviewAsync("cancelled", cancelled.Token));
        Assert.AreEqual(1, f.Played.Count);
        gate.TrySetResult(true);
    }

    [TestMethod]
    public async Task Dispose_CancelsActiveJobAndDetachesMessageEvent()
    {
        var f = new Fixture();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Windows.Gate = gate;
        f.Send("blocked");
        await TestWait.UntilAsync(() => f.Windows.Calls.Count == 1, "blocked synthesis started");
        await f.Service.DisposeAsync();
        f.Send("must not enqueue");
        Assert.AreEqual(1, f.Windows.Calls.Count);
        Assert.AreEqual(0, f.Played.Count);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => f.Service.PreviewAsync("after dispose"));
        await f.DisposeAsync();
    }
}
