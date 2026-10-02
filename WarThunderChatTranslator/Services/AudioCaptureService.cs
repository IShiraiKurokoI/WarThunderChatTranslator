using NAudio.CoreAudioApi;
using NAudio.Wave;
using NLog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Services
{
    public sealed class CapturedAudio
    {
        public CapturedAudio(byte[] pcm16, int sampleRate)
        {
            Pcm16 = pcm16 ?? Array.Empty<byte>();
            SampleRate = sampleRate;
        }

        public byte[] Pcm16 { get; }
        public int SampleRate { get; }
        public int SampleCount => Pcm16.Length / sizeof(short);
        public TimeSpan Duration => SampleRate <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(SampleCount / (double)SampleRate);

        public float[] ToFloatSamples()
        {
            var sampleCount = SampleCount;
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                var sample = (short)(Pcm16[i * 2] | (Pcm16[i * 2 + 1] << 8));
                samples[i] = sample / 32768f;
            }

            return samples;
        }
    }

    /// <summary>
    /// Captures the selected microphone through WASAPI into 16 kHz, 16-bit mono PCM.
    /// Shared-mode WASAPI performs the required format conversion, keeping capture work
    /// lightweight while the game is running. Recognition happens only after capture stops.
    /// </summary>
    public sealed class AudioCaptureService : IDisposable
    {
        private const int SampleRate = 16000;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly object _gate = new();

        private WasapiRecorder _recorder;
        private MMDevice _selectedDevice;
        private MemoryStream _buffer;
        private TaskCompletionSource<StoppedEventArgs> _stoppedTcs;
        private bool _disposed;
        private bool _recording;

        public event EventHandler<float> LevelChanged;

        public bool IsRecording
        {
            get
            {
                lock (_gate)
                {
                    return _recording;
                }
            }
        }

        public Task StartAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_recording || _recorder != null)
                {
                    throw new InvalidOperationException(Localization.GetString("QuickTranslationAlreadyRecording"));
                }
            }

            var builder = new WasapiRecorderBuilder()
                .WithFormat(new WaveFormat(SampleRate, 16, 1))
                .WithBufferLength(50);

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                using var enumerator = new MMDeviceEnumerator();
                _selectedDevice = enumerator.GetDevice(deviceId);
                if (_selectedDevice == null || _selectedDevice.State != DeviceState.Active || _selectedDevice.DataFlow != DataFlow.Capture)
                {
                    _selectedDevice?.Dispose();
                    _selectedDevice = null;
                    throw new InvalidOperationException(Localization.GetString("QuickTranslationSelectedMicrophoneUnavailable"));
                }

                builder.WithDevice(_selectedDevice);
            }

            var recorder = builder.Build();
            var buffer = new MemoryStream(capacity: SampleRate * 2 * 15);
            var stoppedTcs = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

            recorder.DataAvailable += (data, flags, _, _) =>
            {
                float peak = 0f;
                lock (_gate)
                {
                    // Keep accepting the final WASAPI packet(s) after StopRecording() is
                    // requested. _recording is cleared immediately for responsive state/UI,
                    // but _recorder/_buffer stay attached until RecordingStopped/timeout.
                    if (!ReferenceEquals(_recorder, recorder) || !ReferenceEquals(_buffer, buffer))
                    {
                        return;
                    }

                    buffer.Write(data);
                    for (var i = 0; i + 1 < data.Length; i += 2)
                    {
                        var sample = (short)(data[i] | (data[i + 1] << 8));
                        var amplitude = Math.Abs(sample / 32768f);
                        if (amplitude > peak)
                        {
                            peak = amplitude;
                        }
                    }
                }

                LevelChanged?.Invoke(this, Math.Clamp(peak, 0f, 1f));
            };

            recorder.RecordingStopped += (_, args) => stoppedTcs.TrySetResult(args);

            lock (_gate)
            {
                _recorder = recorder;
                _buffer = buffer;
                _stoppedTcs = stoppedTcs;
                _recording = true;
            }

            try
            {
                recorder.StartRecording();
                _logger.Debug("WASAPI quick-translation capture started. Device={0}, Format={1}",
                    string.IsNullOrWhiteSpace(deviceId) ? "default" : deviceId,
                    recorder.WaveFormat);
            }
            catch
            {
                lock (_gate)
                {
                    _recording = false;
                    _recorder = null;
                    _buffer = null;
                    _stoppedTcs = null;
                }

                recorder.Dispose();
                buffer.Dispose();
                _selectedDevice?.Dispose();
                _selectedDevice = null;
                throw;
            }

            return Task.CompletedTask;
        }

        public async Task<CapturedAudio> StopAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            WasapiRecorder recorder;
            MemoryStream buffer;
            TaskCompletionSource<StoppedEventArgs> stoppedTcs;

            lock (_gate)
            {
                if (!_recording || _recorder == null || _buffer == null || _stoppedTcs == null)
                {
                    throw new InvalidOperationException(Localization.GetString("QuickTranslationNotRecording"));
                }

                _recording = false;
                recorder = _recorder;
                buffer = _buffer;
                stoppedTcs = _stoppedTcs;
            }

            try
            {
                recorder.StopRecording();
                var stopped = await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                if (stopped?.Exception != null)
                {
                    throw new InvalidOperationException(
                        Localization.GetString("QuickTranslationMicrophoneCaptureFailed"),
                        stopped.Exception);
                }
            }
            catch (TimeoutException)
            {
                _logger.Warn("WASAPI recorder did not raise RecordingStopped within 3 seconds; continuing with captured PCM.");
            }
            finally
            {
                lock (_gate)
                {
                    _recorder = null;
                    _buffer = null;
                    _stoppedTcs = null;
                }

                try
                {
                    await recorder.DisposeAsync();
                }
                catch
                {
                    recorder.Dispose();
                }

                _selectedDevice?.Dispose();
                _selectedDevice = null;
                LevelChanged?.Invoke(this, 0f);
            }

            var bytes = buffer.ToArray();
            buffer.Dispose();
            _logger.Debug("WASAPI quick-translation capture stopped. Bytes={0}, DurationMs={1:0}",
                bytes.Length,
                bytes.Length / 2d / SampleRate * 1000d);

            return new CapturedAudio(bytes, SampleRate);
        }

        public async Task AbortAsync()
        {
            WasapiRecorder recorder;
            MemoryStream buffer;
            lock (_gate)
            {
                _recording = false;
                recorder = _recorder;
                buffer = _buffer;
                _recorder = null;
                _buffer = null;
                _stoppedTcs = null;
            }

            if (recorder != null)
            {
                try { recorder.StopRecording(); } catch { }
                try { await recorder.DisposeAsync(); } catch { try { recorder.Dispose(); } catch { } }
            }

            buffer?.Dispose();
            _selectedDevice?.Dispose();
            _selectedDevice = null;
            LevelChanged?.Invoke(this, 0f);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try { AbortAsync().GetAwaiter().GetResult(); } catch { }
        }
    }
}
