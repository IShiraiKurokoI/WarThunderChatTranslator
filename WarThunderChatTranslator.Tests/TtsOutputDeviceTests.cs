#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Services;
using WarThunderChatTranslator.Tests.TestSupport;
using Windows.Devices.Enumeration;
using Windows.Media.Playback;

namespace WarThunderChatTranslator.Tests;

[TestClass]
[TestCategory("ModelFree")]
[DoNotParallelize]
public sealed class TtsOutputDeviceTests
{
    private static DeviceInformation Device(string id, string name, bool enabled = true) => new()
    {
        Id = id, Name = name, IsEnabled = enabled
    };

    private sealed class Fixture : IAsyncDisposable
    {
        public IsolatedTestEnvironment Env { get; } = new();
        public GameChatPollingService Chat { get; } = new();
        public ChatTtsService Service { get; }
        public MediaPlayer Player { get; }

        public Fixture()
        {
            Env.DefaultTtsSettings();
            Service = new ChatTtsService(Chat, null,
                id => new RecordingTtsProvider(id), null, null, text => text);
            Player = MediaPlayer.LastCreated!;
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            Env.Dispose();
            DeviceInformation.SetTestDevices();
        }
    }

    [TestMethod]
    public async Task SystemDefaultDoesNotRequireEnumeratingAnyAudioDevice()
    {
        DeviceInformation.SetTestDevices();
        await using var f = new Fixture();
        await f.Service.PreviewAsync("hello");
        Assert.IsNull(f.Player.AudioDevice);
        Assert.AreEqual(0, DeviceInformation.FindAllCalls);
        Assert.AreEqual(1, f.Player.PlayCalls);
    }

    [TestMethod]
    public async Task DeviceListExcludesDisabledEndpointsAndSortsNames()
    {
        DeviceInformation.SetTestDevices(
            Device("b", "Z headset"), Device("disabled", "Hidden device", false), Device("a", "A speaker"));
        var devices = await TtsOutputDeviceService.GetAvailableDevicesAsync();
        CollectionAssert.AreEqual(new[] { "a", "b" }, devices.Select(device => device.Id).ToArray());
    }

    [TestMethod]
    public async Task PreviewRoutesWindowsKokoroAndGptToSelectedDevice()
    {
        DeviceInformation.SetTestDevices(Device("render-a", "Headphones"), Device("render-b", "Virtual cable"));
        await using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.OutputDeviceIdKey, "render-b");
        foreach (var provider in new[] { ChatTtsConfig.ProviderWindows,
            ChatTtsConfig.ProviderSherpaOnnx, ChatTtsConfig.ProviderGptSoVits })
        {
            f.Env.Set(ChatTtsConfig.ProviderKey, provider);
            await f.Service.PreviewAsync("route check");
            Assert.AreEqual("render-b", f.Player.AudioDevice?.Id, provider);
        }
        Assert.AreEqual(3, f.Player.PlayCalls);
    }

    [TestMethod]
    public async Task SwitchingToSystemDefaultClearsPreviouslyPinnedDevice()
    {
        DeviceInformation.SetTestDevices(Device("render-a", "Headphones"));
        await using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.OutputDeviceIdKey, "render-a");
        await f.Service.PreviewAsync("specific");
        Assert.AreEqual("render-a", f.Player.AudioDevice?.Id);
        f.Env.Set(ChatTtsConfig.OutputDeviceIdKey, string.Empty);
        await f.Service.PreviewAsync("default");
        Assert.IsNull(f.Player.AudioDevice);
        Assert.AreEqual(2, f.Player.PlayCalls);
    }

    [TestMethod]
    public async Task DisconnectedOrDisabledExplicitDeviceFailsWithoutUnexpectedPlayback()
    {
        DeviceInformation.SetTestDevices(Device("disabled", "Disconnected headset", false));
        await using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.OutputDeviceIdKey, "disabled");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.PreviewAsync("must fail"));
        Assert.AreEqual(0, f.Player.PlayCalls);
        f.Env.Set(ChatTtsConfig.OutputDeviceIdKey, "not-found");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.PreviewAsync("still fails"));
        Assert.AreEqual("not-found", ChatTtsConfig.GetOutputDeviceId());
        Assert.AreEqual(0, f.Player.PlayCalls);
        f.Env.Set(ChatTtsConfig.OutputDeviceIdKey, string.Empty);
        await f.Service.PreviewAsync("default recovers");
        Assert.AreEqual(1, f.Player.PlayCalls);
    }

    [TestMethod]
    public async Task QueuedChatUsesSameOutputRoutingAsPreview()
    {
        DeviceInformation.SetTestDevices(Device("render-a", "Headphones"));
        await using var f = new Fixture();
        f.Env.Set(ChatTtsConfig.OutputDeviceIdKey, "render-a");
        f.Chat.Emit(TestWait.Chat("point A", sender: "ally"));
        await TestWait.UntilAsync(() => f.Player.PlayCalls == 1, "queued TTS routed to selected device");
        Assert.AreEqual("render-a", f.Player.AudioDevice?.Id);
    }
}
