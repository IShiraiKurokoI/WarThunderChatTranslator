using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Globalization;
using Windows.Media.Capture;
using Windows.Media.SpeechRecognition;

namespace WarThunderChatTranslator.Services
{
    public enum SpeechPrerequisiteProblem
    {
        None,
        MicrophonePermissionRequired,
        MicrophonePermissionDenied,
        MicrophoneCapabilityMissing,
        MicrophoneUnavailable,
        OnlineSpeechRecognitionDisabled,
        SpeechRecognitionUnavailable
    }

    public sealed class SpeechPrerequisiteStatus
    {
        public List<SpeechPrerequisiteProblem> Problems { get; } = new();
        public string Detail { get; set; }
        public bool IsReady => Problems.Count == 0;
        public SpeechPrerequisiteProblem PrimaryProblem => Problems.Count == 0 ? SpeechPrerequisiteProblem.None : Problems[0];
    }

    public sealed class SpeechRecognitionPrerequisiteService
    {
        private const int NoCaptureDevicesHResult = -1072845856;
        private const uint HResultPrivacyStatementDeclined = 0x80045509;

        public async Task<SpeechPrerequisiteStatus> CheckAsync(
            string languageTag,
            bool requestMicrophonePermission,
            CancellationToken cancellationToken = default)
        {
            var result = new SpeechPrerequisiteStatus();
            cancellationToken.ThrowIfCancellationRequested();

            await CheckMicrophoneAsync(result, requestMicrophonePermission, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await CheckOnlineSpeechRecognitionAsync(result, languageTag, cancellationToken);
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
                // This is a non-prompting status check, so merely opening the settings
                // page never causes Windows to display the microphone consent dialog.
                accessInfo = DeviceAccessInformation.CreateFromDeviceClass(DeviceClass.AudioCapture);
            }
            catch (Exception ex)
            {
                result.Detail = ex.Message;
                AddProblem(result, SpeechPrerequisiteProblem.MicrophoneUnavailable);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (accessInfo.CurrentStatus == DeviceAccessStatus.DeniedByUser ||
                accessInfo.CurrentStatus == DeviceAccessStatus.DeniedBySystem)
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

            // Only initialize MediaCapture from the explicit enable action when Windows
            // still needs to ask the user. InitializeAsync is the documented consent path
            // and this method is invoked from the page's UI event handler.
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
                    using var initializeCancellation = cancellationToken.Register(() =>
                    {
                        try { initializeOperation.Cancel(); } catch { }
                    });
                    await initializeOperation;
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (UnauthorizedAccessException)
                {
                    AddProblem(result, SpeechPrerequisiteProblem.MicrophonePermissionDenied);
                    return;
                }
                catch (TypeLoadException ex)
                {
                    result.Detail = ex.Message;
                    AddProblem(result, SpeechPrerequisiteProblem.MicrophoneUnavailable);
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

            cancellationToken.ThrowIfCancellationRequested();

            // Permission can be valid even when no capture endpoint exists, so verify
            // that the system currently exposes at least one audio-capture device.
            try
            {
                var devicesOperation = DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);
                using var devicesCancellation = cancellationToken.Register(() =>
                {
                    try { devicesOperation.Cancel(); } catch { }
                });
                var devices = await devicesOperation;
                cancellationToken.ThrowIfCancellationRequested();
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

        private static async Task CheckOnlineSpeechRecognitionAsync(
            SpeechPrerequisiteStatus result,
            string languageTag,
            CancellationToken cancellationToken)
        {
            try
            {
                var selectedLanguage = ResolveLanguage(languageTag);
                using var recognizer = selectedLanguage == null
                    ? new SpeechRecognizer()
                    : new SpeechRecognizer(selectedLanguage);

                // Topic constraints use the online speech service. Compiling one checks
                // the Windows Online speech recognition privacy setting without starting
                // microphone capture.
                recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
                    SpeechRecognitionScenario.Dictation,
                    "quick-translation-prerequisite-check"));

                var compileOperation = recognizer.CompileConstraintsAsync();
                using var compileCancellation = cancellationToken.Register(() =>
                {
                    try { compileOperation.Cancel(); } catch { }
                });
                var compileResult = await compileOperation;
                cancellationToken.ThrowIfCancellationRequested();
                if (compileResult.Status != SpeechRecognitionResultStatus.Success)
                {
                    result.Detail = compileResult.Status.ToString();
                    AddProblem(result, SpeechPrerequisiteProblem.SpeechRecognitionUnavailable);
                }
            }
            catch (Exception ex) when ((uint)ex.HResult == HResultPrivacyStatementDeclined)
            {
                result.Detail = ex.Message;
                AddProblem(result, SpeechPrerequisiteProblem.OnlineSpeechRecognitionDisabled);
            }
            catch (Exception ex)
            {
                result.Detail = ex.Message;
                AddProblem(result, SpeechPrerequisiteProblem.SpeechRecognitionUnavailable);
            }
        }

        private static Language ResolveLanguage(string languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                return SpeechRecognizer.SystemSpeechLanguage;
            }

            return SpeechRecognizer.SupportedTopicLanguages.FirstOrDefault(language =>
                       string.Equals(language.LanguageTag, languageTag, StringComparison.OrdinalIgnoreCase))
                   ?? SpeechRecognizer.SystemSpeechLanguage;
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
