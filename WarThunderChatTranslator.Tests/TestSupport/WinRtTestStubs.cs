// Headless substitutes for the small WinRT surface exercised by the *linked production sources*.
// These classes are compiled only into the net10 test project and are not part of the WinUI app.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Windows.Storage
{
    public enum CreationCollisionOption { OpenIfExists, ReplaceExisting }
    public enum NameCollisionOption { ReplaceExisting }
    public enum StorageDeleteOption { PermanentDelete }

    public sealed class TestPropertySet : Dictionary<string, object>
    {
        public new object? this[string key]
        {
            get => TryGetValue(key, out var value) ? value : null;
            set
            {
                if (value is null) Remove(key);
                else base[key] = value;
            }
        }
    }

    public sealed class ApplicationDataContainer
    {
        public TestPropertySet Values { get; } = new();
    }

    public sealed class ApplicationData
    {
        public static ApplicationData Current { get; } = new();
        public ApplicationDataContainer LocalSettings { get; } = new();
        public StorageFolder LocalFolder { get; set; } = new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wtct-headless-test-default"));
    }

    public sealed class StorageFolder
    {
        public string Path { get; }
        public StorageFolder(string path) { Path = path; Directory.CreateDirectory(path); }
        public Task<StorageFolder> CreateFolderAsync(string name, CreationCollisionOption option)
            => Task.FromResult(new StorageFolder(System.IO.Path.Combine(Path, name)));
        public Task<StorageFile> CreateFileAsync(string name, CreationCollisionOption option)
        {
            var path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using (var _ = File.Create(path)) { }
            return Task.FromResult(new StorageFile(path));
        }
        public Task<object?> TryGetItemAsync(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            return Task.FromResult<object?>(File.Exists(path) ? new StorageFile(path) : null);
        }
        public Task<StorageFile> GetFileAsync(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            if (!File.Exists(path)) throw new FileNotFoundException(name, path);
            return Task.FromResult(new StorageFile(path));
        }
    }

    public sealed class StorageFile
    {
        public string Path { get; private set; }
        public StorageFile(string path) => Path = path;
        public Task DeleteAsync(StorageDeleteOption option)
        {
            File.Delete(Path);
            return Task.CompletedTask;
        }
        public Task RenameAsync(string name, NameCollisionOption option)
        {
            var destination = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, name);
            File.Move(Path, destination, overwrite: option == NameCollisionOption.ReplaceExisting);
            Path = destination;
            return Task.CompletedTask;
        }
    }

    public static class FileIO
    {
        public static Task WriteBytesAsync(StorageFile file, byte[] bytes) => File.WriteAllBytesAsync(file.Path, bytes);
    }
}

namespace Windows.Devices.Enumeration
{
    public sealed class DeviceInformation
    {
        private static DeviceInformation[] _testDevices = Array.Empty<DeviceInformation>();
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public static int FindAllCalls { get; private set; }
        public static void SetTestDevices(params DeviceInformation[] devices)
        {
            _testDevices = devices;
            FindAllCalls = 0;
        }
        public static Task<IReadOnlyList<DeviceInformation>> FindAllAsync(string selector)
        {
            if (selector != Windows.Media.Devices.MediaDevice.GetAudioRenderSelector())
                throw new ArgumentException("Only audio render endpoints are supported by this test stub.");
            FindAllCalls++;
            return Task.FromResult<IReadOnlyList<DeviceInformation>>(_testDevices);
        }
    }
}

namespace Windows.Media.Devices
{
    public static class MediaDevice
    {
        public static string GetAudioRenderSelector() => "mock-audio-render-selector";
    }
}

namespace Windows.Foundation
{
    public delegate void TypedEventHandler<TSender, TResult>(TSender sender, TResult args);
}

namespace Windows.Media.Core
{
    public sealed class MediaSource
    {
        public static MediaSource CreateFromStorageFile(Windows.Storage.StorageFile file) => new();
    }
}

namespace Windows.Media.Playback
{
    using Windows.Foundation;
    using Windows.Media.Core;
    public sealed class MediaPlayerFailedEventArgs
    {
        public string? ErrorMessage { get; set; }
    }
    public sealed class MediaPlayerCommandManager
    {
        public bool IsEnabled { get; set; }
    }
    public sealed class MediaPlayer : IDisposable
    {
        public MediaPlayer() => LastCreated = this;
        public static MediaPlayer? LastCreated { get; private set; }
        public MediaPlayerCommandManager CommandManager { get; } = new();
        public double Volume { get; set; }
        public MediaSource? Source { get; set; }
        public Windows.Devices.Enumeration.DeviceInformation? AudioDevice { get; set; }
        public int PlayCalls { get; private set; }
        public event TypedEventHandler<MediaPlayer, object>? MediaEnded;
        public event TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs>? MediaFailed;
        public void Pause() { }
        public void Play()
        {
            PlayCalls++;
            MediaEnded?.Invoke(this, new object());
        }
        public void FailPlayback(string message) => MediaFailed?.Invoke(this, new MediaPlayerFailedEventArgs { ErrorMessage = message });
        public void Dispose() { }
    }
}
