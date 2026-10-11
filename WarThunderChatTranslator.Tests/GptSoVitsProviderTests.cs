#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Services;
using WarThunderChatTranslator.Tests.TestSupport;

namespace WarThunderChatTranslator.Tests;

[TestClass]
[TestCategory("ModelFree")]
[DoNotParallelize]
public sealed class GptSoVitsProviderTests
{
    private sealed class Fixture : IDisposable
    {
        public IsolatedTestEnvironment Env { get; } = new();
        public MockGptSoVitsHttpServer Server { get; } = new();
        public GptSoVitsProfileStore Store { get; }
        public GptSoVitsTtsProvider Provider { get; }
        public GptSoVitsVoiceProfile Profile { get; } = new()
        {
            Id = "hero", Name = "Hero", GptWeightsPath = "models/gpt & v2.ckpt",
            SoVitsWeightsPath = "models/sovits+voice.pth", ReferenceAudioPath = "D:/dummy/reference.wav",
            PromptText = "Keep it short", PromptLanguage = "en", TextLanguage = "en"
        };

        public Fixture()
        {
            Env.DefaultTtsSettings();
            Store = new GptSoVitsProfileStore(Env.Subdirectory("profiles"));
            Store.Upsert(Profile);
            Provider = new GptSoVitsTtsProvider(Store, new HttpClient(Server));
        }

        public void Dispose() { Provider.Dispose(); Env.Dispose(); }
    }

    [TestMethod]
    public async Task Endpoints_SendEscapedWeightPathsAndCompleteTtsJsonPayload()
    {
        using var f = new Fixture();
        var audio = await f.Provider.SynthesizeAsync("  Hello pilot!  ", "hero", 1.3);

        Assert.IsTrue(File.Exists(audio.Path));
        CollectionAssert.AreEqual(f.Server.AudioBytes, await File.ReadAllBytesAsync(audio.Path));
        CollectionAssert.AreEqual(new[] { "/set_gpt_weights", "/set_sovits_weights", "/tts" },
            f.Server.Requests.Select(r => r.Path).ToArray());
        Assert.AreEqual("GET", f.Server.Requests[0].Method);
        Assert.AreEqual("GET", f.Server.Requests[1].Method);
        StringAssert.Contains(f.Server.Requests[0].Query, "weights_path=models%2Fgpt%20%26%20v2.ckpt");
        StringAssert.Contains(f.Server.Requests[1].Query, "weights_path=models%2Fsovits%2Bvoice.pth");
        Assert.AreEqual("POST", f.Server.Requests[2].Method);
        using var json = JsonDocument.Parse(f.Server.Requests[2].Body!);
        var body = json.RootElement;
        Assert.AreEqual("Hello pilot!", body.GetProperty("text").GetString());
        Assert.AreEqual("en", body.GetProperty("text_lang").GetString());
        Assert.AreEqual("D:/dummy/reference.wav", body.GetProperty("ref_audio_path").GetString());
        Assert.AreEqual("Keep it short", body.GetProperty("prompt_text").GetString());
        Assert.AreEqual("en", body.GetProperty("prompt_lang").GetString());
        Assert.AreEqual("cut5", body.GetProperty("text_split_method").GetString());
        Assert.AreEqual(1, body.GetProperty("batch_size").GetInt32());
        Assert.AreEqual(1.3, body.GetProperty("speed_factor").GetDouble(), 0.000001);
        Assert.AreEqual("wav", body.GetProperty("media_type").GetString());
        Assert.IsFalse(body.GetProperty("streaming_mode").GetBoolean());
        Assert.IsTrue(body.GetProperty("parallel_infer").GetBoolean());
    }

    [TestMethod]
    public async Task SynthesisCache_TrimsTextAvoidsDuplicateRequestsAndReturnsSameFile()
    {
        using var f = new Fixture();
        var a = await f.Provider.SynthesizeAsync(" Signal ", "hero", 1);
        var b = await f.Provider.SynthesizeAsync("Signal", "hero", 1);
        Assert.AreEqual(a.Path, b.Path);
        Assert.AreEqual(1, f.Server.Count("/tts"));
        Assert.AreEqual(1, f.Server.Count("/set_gpt_weights"));
        Assert.AreEqual(1, f.Server.Count("/set_sovits_weights"));
    }

    [TestMethod]
    public async Task ConcurrentIdenticalRequests_OnlyOneActualSynthesisOccurs()
    {
        using var f = new Fixture();
        var synthStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Server.Responder = async (route, cancellationToken) =>
        {
            if (route.Path == "/tts")
            {
                synthStarted.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(f.Server.AudioBytes) };
            }
            return MockGptSoVitsHttpServer.Json("{\"message\":\"ok\"}");
        };
        var first = f.Provider.SynthesizeAsync("Duplicated", "hero", 1);
        await synthStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = f.Provider.SynthesizeAsync("Duplicated", "hero", 1);
        Assert.IsFalse(second.IsCompleted);
        release.TrySetResult(true);
        var files = await Task.WhenAll(first, second);
        Assert.AreEqual(files[0].Path, files[1].Path);
        Assert.AreEqual(1, f.Server.Count("/tts"));
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(files[0].Path)!, "*.tmp.wav").Length);
    }

    [TestMethod]
    public async Task DifferentText_ReusesWeightsButNotAudioCache()
    {
        using var f = new Fixture();
        await f.Provider.SynthesizeAsync("Left", "hero", 1);
        await f.Provider.SynthesizeAsync("Right", "hero", 1);
        Assert.AreEqual(2, f.Server.Count("/tts"));
        Assert.AreEqual(1, f.Server.Count("/set_gpt_weights"));
        Assert.AreEqual(1, f.Server.Count("/set_sovits_weights"));
    }

    [TestMethod]
    public async Task WeightChange_ReappliesBothWeightsAndInvalidatesAudioCache()
    {
        using var f = new Fixture();
        await f.Provider.SynthesizeAsync("Target", "hero", 1);
        f.Profile.GptWeightsPath = "models/new-gpt.ckpt";
        f.Store.Upsert(f.Profile);
        await f.Provider.SynthesizeAsync("Target", "hero", 1);
        Assert.AreEqual(2, f.Server.Count("/tts"));
        Assert.AreEqual(2, f.Server.Count("/set_gpt_weights"));
        Assert.AreEqual(2, f.Server.Count("/set_sovits_weights"));
    }

    [TestMethod]
    public async Task PromptChange_ChangesAudioCacheButDoesNotReloadWeights()
    {
        using var f = new Fixture();
        await f.Provider.SynthesizeAsync("Target", "hero", 1);
        f.Profile.PromptText = "A different prompt";
        f.Store.Upsert(f.Profile);
        await f.Provider.SynthesizeAsync("Target", "hero", 1);
        Assert.AreEqual(2, f.Server.Count("/tts"));
        Assert.AreEqual(1, f.Server.Count("/set_gpt_weights"));
    }

    [TestMethod]
    public async Task InvalidateWeights_ForcesWeightReloadOnNextCacheMiss()
    {
        using var f = new Fixture();
        await f.Provider.SynthesizeAsync("Before", "hero", 1);
        f.Provider.InvalidateWeights();
        await f.Provider.SynthesizeAsync("After", "hero", 1);
        Assert.AreEqual(2, f.Server.Count("/set_gpt_weights"));
    }

    [TestMethod]
    public async Task SuccessfulConnectionCheck_ResetsWeightsSignature()
    {
        using var f = new Fixture();
        await f.Provider.SynthesizeAsync("Before", "hero", 1);
        Assert.IsTrue(await f.Provider.TestConnectionAsync());
        await f.Provider.SynthesizeAsync("After", "hero", 1);
        Assert.AreEqual(1, f.Server.Count("/openapi.json"));
        Assert.AreEqual(2, f.Server.Count("/set_gpt_weights"));
    }

    [TestMethod]
    public async Task ConnectionCheck_RejectsOpenApiWithoutTtsOrUnsuccessfulStatus()
    {
        using var f = new Fixture();
        f.Server.AdvertiseTtsEndpoint = false;
        Assert.IsFalse(await f.Provider.TestConnectionAsync());
        f.Server.Responder = (_, _) => Task.FromResult(MockGptSoVitsHttpServer.Json("{}", HttpStatusCode.ServiceUnavailable));
        Assert.IsFalse(await f.Provider.TestConnectionAsync());
    }

    [TestMethod]
    public async Task ConnectionCheck_CancellationAndTransportFailuresReturnFalse()
    {
        using var f = new Fixture();
        f.Server.Responder = (_, _) => throw new HttpRequestException("server offline");
        Assert.IsFalse(await f.Provider.TestConnectionAsync());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.IsFalse(await f.Provider.TestConnectionAsync(cancelled.Token));
    }

    [TestMethod]
    public async Task WithNoWeightPaths_OnlyTtsEndpointIsCalled()
    {
        using var f = new Fixture();
        f.Profile.GptWeightsPath = "";
        f.Profile.SoVitsWeightsPath = "";
        f.Store.Upsert(f.Profile);
        await f.Provider.SynthesizeAsync("Hey", "hero", 1);
        Assert.AreEqual(1, f.Server.Requests.Count);
        Assert.AreEqual("/tts", f.Server.Requests[0].Path);
    }

    [TestMethod]
    public async Task ApiTtsError_ExposesMessageAndExceptionAndRemovesTempFile()
    {
        using var f = new Fixture();
        f.Server.Responder = (request, _) => Task.FromResult(
            request.Path == "/tts"
                ? MockGptSoVitsHttpServer.Json("{\"message\":\"invalid reference\",\"Exception\":\"mock failure\"}", HttpStatusCode.BadRequest)
                : MockGptSoVitsHttpServer.Json("{}"));
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => f.Provider.SynthesizeAsync("Failure", "hero", 1));
        StringAssert.Contains(error.Message, "invalid reference: mock failure");
        Assert.AreEqual(0, Directory.GetFiles(f.Env.Subdirectory("AppData"), "*.tmp.wav", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task FailedWeightEndpoint_StopsBeforeSynthesisAndCleansUp()
    {
        using var f = new Fixture();
        f.Server.Responder = (request, _) => Task.FromResult(
            request.Path == "/set_gpt_weights"
                ? MockGptSoVitsHttpServer.Json("{\"message\":\"no model\"}", HttpStatusCode.InternalServerError)
                : MockGptSoVitsHttpServer.Json("{}"));
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => f.Provider.SynthesizeAsync("abc", "hero", 1));
        StringAssert.Contains(error.Message, "no model");
        Assert.AreEqual(0, f.Server.Count("/tts"));
        Assert.AreEqual(0, Directory.GetFiles(f.Env.Subdirectory("AppData"), "*.tmp.wav", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task ShortAudio_RejectsInvalidResponseWithoutCachedAudio()
    {
        using var f = new Fixture();
        f.Server.AudioBytes = [1, 2, 3];
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => f.Provider.SynthesizeAsync("abc", "hero", 1));
        StringAssert.Contains(error.Message, "empty audio");
        Assert.AreEqual(0, Directory.GetFiles(f.Env.Subdirectory("AppData"), "*.wav", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task TransportError_HasFriendlyMessageAndCanRecover()
    {
        using var f = new Fixture();
        f.Server.Responder = (_, _) => throw new HttpRequestException("refused");
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => f.Provider.SynthesizeAsync("Failed", "hero", 1));
        StringAssert.Contains(error.Message, "Cannot connect");
        Assert.IsInstanceOfType<HttpRequestException>(error.InnerException);
        f.Server.Responder = null;
        await f.Provider.SynthesizeAsync("Recovered", "hero", 1);
        Assert.AreEqual(1, f.Server.Count("/tts"));
    }

    [TestMethod]
    public async Task SwitchingVoiceProfiles_ReissuesWeightsAndUsesEachProfilesReferenceAudio()
    {
        using var f = new Fixture();
        f.Store.Upsert(new GptSoVitsVoiceProfile
        {
            Id = "second", Name = "Second", ReferenceAudioPath = "new-reference.wav",
            GptWeightsPath = "model-b.ckpt", SoVitsWeightsPath = "model-b.pth",
            PromptText = "Second character", PromptLanguage = "ja", TextLanguage = "all_ja"
        });
        var hero = await f.Provider.SynthesizeAsync("Same text", "hero", 1);
        var second = await f.Provider.SynthesizeAsync("Same text", "second", 1);
        Assert.AreNotEqual(hero.Path, second.Path);
        Assert.AreEqual(2, f.Server.Count("/set_gpt_weights"));
        Assert.AreEqual(2, f.Server.Count("/set_sovits_weights"));
        var requests = f.Server.Requests.Where(x => x.Path == "/tts").ToArray();
        Assert.AreEqual(2, requests.Length);
        using var body = JsonDocument.Parse(requests[1].Body!);
        Assert.AreEqual("new-reference.wav", body.RootElement.GetProperty("ref_audio_path").GetString());
        Assert.AreEqual("Second character", body.RootElement.GetProperty("prompt_text").GetString());
        Assert.AreEqual("all_ja", body.RootElement.GetProperty("text_lang").GetString());
    }

    [TestMethod]
    public async Task InvalidErrorBody_FallsBackToHttpReasonAndCleansTemporaryAudio()
    {
        using var f = new Fixture();
        f.Server.Responder = (route, _) => Task.FromResult(route.Path == "/tts"
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("not-json"),
                ReasonPhrase = "Mock rejected"
            }
            : MockGptSoVitsHttpServer.Json("{}"));
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => f.Provider.SynthesizeAsync("error", "hero", 1));
        StringAssert.Contains(ex.Message, "Mock rejected");
        Assert.AreEqual(0, Directory.GetFiles(f.Env.Subdirectory("AppData"), "*.tmp.wav", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task CancelledSynthesis_LeavesNoTemporaryCacheArtifacts()
    {
        using var f = new Fixture();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Server.Responder = async (route, cancellationToken) =>
        {
            if (route.Path == "/tts")
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return MockGptSoVitsHttpServer.Json("{}");
        };
        using var cts = new CancellationTokenSource();
        var synthesis = f.Provider.SynthesizeAsync("cancel", "hero", 1, cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await synthesis);
        Assert.AreEqual(0, Directory.GetFiles(f.Env.Subdirectory("AppData"), "*.tmp.wav", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task MissingVoiceReferenceAndBlankText_AreRejectedWithoutHttpRequests()
    {
        using var f = new Fixture();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Provider.SynthesizeAsync(" \n ", "hero", 1));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Provider.SynthesizeAsync("Hello", "does-not-exist", 1));
        f.Profile.ReferenceAudioPath = " ";
        f.Store.Upsert(f.Profile);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Provider.SynthesizeAsync("Hello", "hero", 1));
        Assert.AreEqual(0, f.Server.Requests.Count);
    }

    [TestMethod]
    public async Task SpeakingRate_IsClampedAndProducesDifferentCacheKeys()
    {
        using var f = new Fixture();
        await f.Provider.SynthesizeAsync("Rate", "hero", -2);
        await f.Provider.SynthesizeAsync("Rate", "hero", 12);
        var requests = f.Server.Requests.Where(r => r.Path == "/tts").ToArray();
        Assert.AreEqual(2, requests.Length);
        using var slow = JsonDocument.Parse(requests[0].Body!);
        using var fast = JsonDocument.Parse(requests[1].Body!);
        Assert.AreEqual(0.5, slow.RootElement.GetProperty("speed_factor").GetDouble());
        Assert.AreEqual(2.0, fast.RootElement.GetProperty("speed_factor").GetDouble());
    }

    [TestMethod]
    public void Voices_ExposeSortedProfilesAndMappedLanguages()
    {
        using var f = new Fixture();
        f.Store.Upsert(new() { Id = "z", Name = "zebra", TextLanguage = "all_zh" });
        f.Store.Upsert(new() { Id = "a", Name = "Alpha", TextLanguage = "all_ja" });
        var voices = f.Provider.GetVoices();
        CollectionAssert.AreEqual(new[] { "Alpha", "Hero", "zebra" }, voices.Select(v => v.DisplayName).ToArray());
        CollectionAssert.AreEqual(new[] { "日本語", "English", "中文" }, voices.Select(v => v.Language).ToArray());
        Assert.AreEqual(ChatTtsConfig.ProviderGptSoVits, f.Provider.Id);
        Assert.AreEqual("GPT-SoVITS", f.Provider.DisplayName);
    }

    [TestMethod]
    public async Task AfterDispose_OperationsCannotBeUsed()
    {
        using var f = new Fixture();
        f.Provider.Dispose();
        f.Provider.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => f.Provider.GetVoices());
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => f.Provider.TestConnectionAsync());
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => f.Provider.SynthesizeAsync("text", "hero", 1));
    }
}
