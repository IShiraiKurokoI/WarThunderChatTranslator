#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;
using System.Diagnostics;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Services;
using Windows.Storage;

namespace WarThunderChatTranslator.Tests.TestSupport;

internal sealed class IsolatedTestEnvironment : IDisposable
{
    private readonly StorageFolder _previousFolder;
    private readonly Dictionary<string, object?> _originalSettings = new(StringComparer.Ordinal);
    public string DirectoryPath { get; }

    public IsolatedTestEnvironment()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "WTCT-MockTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        _previousFolder = ApplicationData.Current.LocalFolder;
        ApplicationData.Current.LocalFolder = new StorageFolder(Path.Combine(DirectoryPath, "AppData"));
    }

    public string Subdirectory(string name) => Path.Combine(DirectoryPath, name);

    public void Set(string key, string? value)
    {
        var settings = ApplicationData.Current.LocalSettings.Values;
        if (!_originalSettings.ContainsKey(key)) _originalSettings.Add(key, settings[key]);
        if (value is null) settings.Remove(key);
        else ApplicationConfig.SaveSettings(key, value);
    }

    public void DefaultTtsSettings()
    {
        Set(ChatTtsConfig.EnabledKey, "true");
        Set(ChatTtsConfig.ProviderKey, ChatTtsConfig.ProviderWindows);
        Set(ChatTtsConfig.WindowsVoiceIdKey, "windows-voice");
        Set(ChatTtsConfig.SherpaVoiceIdKey, "sherpa-voice");
        Set(ChatTtsConfig.GptSoVitsVoiceIdKey, "gpt-voice");
        Set(ChatTtsConfig.GptSoVitsApiUrlKey, "http://127.0.0.1:9880");
        Set(ChatTtsConfig.SpeakAllyKey, "true");
        Set(ChatTtsConfig.SpeakEnemyKey, "false");
        Set(ChatTtsConfig.SpeakSystemKey, "false");
        Set(ChatTtsConfig.SpeakingRateKey, "1");
        Set(ChatTtsConfig.VolumeKey, "0.8");
        Set(ChatTtsConfig.OutputDeviceIdKey, string.Empty);
        Set(ChatTtsConfig.QueueCapacityKey, "3");
        Set(ChatTtsConfig.MaxQueueAgeSecondsKey, "8");
        Set(ContentFilterConfig.FilterTtsKey, "false");
        Set(ContentFilterConfig.FilterDisplayKey, "false");
        Set(ContentFilterConfig.MinimumSeverityKey, "2");
        Set(ContentFilterConfig.TtsActionKey, ContentFilterConfig.TtsActionRemoveMatchedTerms);
    }

    public void Dispose()
    {
        foreach (var setting in _originalSettings)
        {
            if (setting.Value is null) ApplicationData.Current.LocalSettings.Values.Remove(setting.Key);
            else ApplicationData.Current.LocalSettings.Values[setting.Key] = setting.Value;
        }
        ApplicationData.Current.LocalFolder = _previousFolder;
        try { Directory.Delete(DirectoryPath, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

internal sealed record SpokenCall(string Text, string VoiceId, double Rate);

internal sealed class RecordingTtsProvider : ITtsProvider
{
    private readonly ConcurrentQueue<SpokenCall> _calls = new();
    public string Id { get; }
    public string DisplayName => "Recording " + Id;
    public IReadOnlyList<SpokenCall> Calls => _calls.ToArray();
    public TaskCompletionSource<bool>? Gate { get; set; }
    public Exception? Error { get; set; }
    public Func<SpokenCall, Exception?>? ErrorForCall { get; set; }
    public IReadOnlyList<TtsVoiceInfo> GetVoices() => [new("voice", "Fake voice", "en")];
    public RecordingTtsProvider(string id) { Id = id; }
    public async Task<StorageFile> SynthesizeAsync(string text, string voiceId, double speakingRate, CancellationToken cancellationToken = default)
    {
        var call = new SpokenCall(text, voiceId, speakingRate);
        _calls.Enqueue(call);
        if (Gate is not null) await Gate.Task.WaitAsync(cancellationToken);
        if (ErrorForCall?.Invoke(call) is Exception specificError) throw specificError;
        if (Error is not null) throw Error;
        return new StorageFile("recorded-" + Id + ".wav");
    }
}

internal static class TestWait
{
    public static async Task UntilAsync(Func<bool> condition, string message, int timeoutMilliseconds = 2000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds) Assert.Fail("Timed out: " + message);
            await Task.Delay(5);
        }
    }
    public static ChatMessage Chat(string text, string? sender = "ally", bool enemy = false, bool succeeded = true)
        => new()
        {
            Msg = text,
            OriginalMessage = text,
            TranslatedMessage = text,
            TranslationSucceeded = succeeded,
            Sender = sender!,
            Enemy = enemy
        };
}

internal sealed class MutableClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset GetNow() => Now;
    public void Advance(TimeSpan elapsed) => Now += elapsed;
}
