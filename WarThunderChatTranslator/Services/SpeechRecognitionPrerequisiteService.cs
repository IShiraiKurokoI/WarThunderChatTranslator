using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;

namespace WarThunderChatTranslator.Services
{
    public enum SpeechPrerequisiteProblem
    {
        None,
        MicrophonePermissionRequired,
        MicrophonePermissionDenied,
        MicrophoneCapabilityMissing,
        MicrophoneUnavailable,
        SelectedMicrophoneUnavailable,
        LocalModelUnavailable
    }

    public sealed class SpeechPrerequisiteStatus
    {
        public List<SpeechPrerequisiteProblem> Problems { get; } = new();
        public string Detail { get; set; }
        public bool IsReady => Problems.Count == 0;
        public SpeechPrerequisiteProblem PrimaryProblem => Problems.Count == 0 ? SpeechPrerequisiteProblem.None : Problems[0];
    }

    /// <summary>
    /// Checks only local-ASR prerequisites: microphone permission/device and packaged Paraformer model.
    /// Windows Online speech recognition is intentionally not required.
    /// </summary>
    public sealed class SpeechRecognitionPrerequisiteService
    {
        private const int NoCaptureDevicesHResult = -1072845856;

        public async Task<SpeechPrerequisiteStatus> CheckAsync(
            string microphoneDeviceId,
            bool requestMicrophonePermission,
            CancellationToken cancellationToken = default)
        {
            var result = new SpeechPrerequisiteStatus();
            cancellationToken.ThrowIfCancellationRequested();

            await CheckMicrophoneAsync(result, requestMicrophonePermission, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!result.Problems.Contains(SpeechPrerequisiteProblem.MicrophonePermissionDenied) &&
                !result.Problems.Contains(SpeechPrerequisiteProblem.MicrophonePermissionRequired) &&
                !result.Problems.Contains(SpeechPrerequisiteProblem.MicrophoneUnavailable) &&
                !AudioDeviceService.CaptureDeviceExists(microphoneDeviceId))
            {
                AddProblem(result, string.IsNullOrWhiteSpace(microphoneDeviceId)
                    ? SpeechPrerequisiteProblem.MicrophoneUnavailable
                    : SpeechPrerequisiteProblem.SelectedMicrophoneUnavailable);
            }

            if (!SpeechRecognitionService.ModelFilesAvailable)
            {
                AddProblem(result, SpeechPrerequisiteProblem.LocalModelUnavailable);
                result.Detail = SpeechRecognitionService.ModelDirectory;
            }

            return result;
        }

        private static async Task CheckMicrophoneAsync(
            SpeechPrerequisiteStatus result,
            bool requestPermission,
            CancellationToken cancellationToken)
        {
            DeviceAccessInformation accessInfo;
            try
            {
                accessInfo = DeviceAccessInformation.CreateFromDeviceClass(DeviceClass.AudioCapture);
            }
            catch (Exception ex)
            {
                result.Detail = ex.Message;
                AddProblem(result, SpeechPrerequisiteProblem.MicrophoneUnavailable);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (accessInfo.CurrentStatus is DeviceAccessStatus.DeniedByUser or DeviceAccessStatus.DeniedBySystem)
            {
                AddProblem(result, SpeechPrerequisiteProblem.MicrophonePermissionDenied);
                return;
            }

            if (!requestPermission &&
                (accessInfo.UserPromptRequired || accessInfo.CurrentStatus == DeviceAccessStatus.Unspecified))
            {
                AddProblem(result, SpeechPrerequisiteProblem.MicrophonePermissionRequired);
                return;
            }

            if (requestPermission &&
                (accessInfo.UserPromptRequired || accessInfo.CurrentStatus == DeviceAccessStatus.Unspecified))
            {
                try
                {
                    var settings = new MediaCaptureInitializationSettings
                    {
                        StreamingCaptureMode = StreamingCaptureMode.Audio,
                        MediaCategory = MediaCategory.Speech
                    };

                    using var capture = new MediaCapture();
                    var initializeOperation = capture.InitializeAsync(settings);
                    using var registration = cancellationToken.Register(() =>
                    {
                        try { initializeOperation.Cancel(); } catch { }
                    });
                    await initializeOperation;
                }
                catch (UnauthorizedAccessException)
                {
                    AddProblem(result, SpeechPrerequisiteProblem.MicrophonePermissionDenied);
                    return;
                }
                catch (TypeLoadException ex)
                {
                    result.Detail = ex.Message;
                    AddProblem(result, SpeechPrerequisiteProblem.MicrophoneCapabilityMissing);
                    return;
                }
                catch (Exception ex) when (ex.HResult == NoCaptureDevicesHResult)
                {
                    result.Detail = ex.Message;
                    AddProblem(result, SpeechPrerequisiteProblem.MicrophoneUnavailable);
                    return;
                }
                catch (Exception ex)
                {
                    result.Detail = ex.Message;
                    AddProblem(result, SpeechPrerequisiteProblem.MicrophoneUnavailable);
                    return;
                }
            }

            try
            {
                var operation = DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);
                using var registration = cancellationToken.Register(() =>
                {
                    try { operation.Cancel(); } catch { }
                });
                var devices = await operation;
                if (devices.Count == 0)
                {
                    AddProblem(result, SpeechPrerequisiteProblem.MicrophoneUnavailable);
                }
            }
            catch (Exception ex)
            {
                result.Detail = ex.Message;
                AddProblem(result, SpeechPrerequisiteProblem.MicrophoneUnavailable);
            }
        }

        private static void AddProblem(SpeechPrerequisiteStatus result, SpeechPrerequisiteProblem problem)
        {
            if (!result.Problems.Contains(problem))
            {
                result.Problems.Add(problem);
            }
        }
    }
}
