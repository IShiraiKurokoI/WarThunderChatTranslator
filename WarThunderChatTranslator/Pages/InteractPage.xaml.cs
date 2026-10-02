using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Services;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Pickers;
using Windows.System;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class InteractPage : Page
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly SpeechRecognitionPrerequisiteService _prerequisiteService = new();
        private List<QuickTranslationHotkey> _hotkeys = new();
        private IReadOnlyList<VoiceInformation> _voices = Array.Empty<VoiceInformation>();
        private IReadOnlyList<AudioInputDeviceInfo> _microphoneDevices = Array.Empty<AudioInputDeviceInfo>();
        private DispatcherTimer _microphoneLevelTimer;
        private bool _loaded;
        private bool _rebuildingHotkeyList;
        private bool _updatingEnableToggle;
        private string _warningSettingsUri;
        private int? _capturingHotkeyId;
        private string _pendingCapturedShortcut;
        private TextBox _capturingHotkeyDisplay;
        private Button _capturingHotkeyButton;
        private QuickTranslationService _attachedQuickTranslationService;

        public InteractPage()
        {
            InitializeComponent();
            AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Page_KeyDown), true);
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded)
            {
                return;
            }

            LoadMicrophoneDevices();
            LoadVoices();
            LoadSettings();
            _loaded = true;
            StartMicrophoneLevelTimer();
            AttachSpeechRecognitionState();

            var status = await CheckSpeechPrerequisitesAsync(requestMicrophonePermission: false);
            UpdateSpeechPrerequisiteWarning(status);
            if (QuickTranslationEnabled.IsOn && !status.IsReady)
            {
                ApplicationConfig.SaveSettings(QuickTranslationConfig.EnabledKey, "false");
                _updatingEnableToggle = true;
                QuickTranslationEnabled.IsOn = false;
                _updatingEnableToggle = false;
                RefreshGlobalHotkeys();
            }
            else if (QuickTranslationEnabled.IsOn && Application.Current is App app)
            {
                await app.WarmUpQuickTranslationSpeechAsync();
                AttachSpeechRecognitionState();
                UpdateSpeechRecognitionStateDisplay();
            }
        }

        private void LoadMicrophoneDevices()
        {
            var savedDeviceId = ApplicationConfig.GetSettings(QuickTranslationConfig.MicrophoneDeviceIdKey) ?? string.Empty;
            _microphoneDevices = AudioDeviceService.GetCaptureDevices();

            MicrophoneDevice.Items.Clear();
            MicrophoneDevice.Items.Add(new ComboBoxItem
            {
                Content = Localization.GetString("QuickTranslationSystemDefaultMicrophone"),
                Tag = string.Empty
            });

            foreach (var device in _microphoneDevices)
            {
                var displayName = device.IsDefault
                    ? $"{device.Name} ({Localization.GetString("QuickTranslationDefaultDeviceSuffix")})"
                    : device.Name;
                MicrophoneDevice.Items.Add(new ComboBoxItem
                {
                    Content = displayName,
                    Tag = device.Id
                });
            }

            var selected = MicrophoneDevice.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString() ?? string.Empty, savedDeviceId, StringComparison.OrdinalIgnoreCase));

            // Preserve a saved endpoint that is currently missing instead of silently
            // falling back to the default device. This lets the prerequisite InfoBar
            // accurately tell the user that the configured microphone is unavailable.
            if (selected == null && !string.IsNullOrWhiteSpace(savedDeviceId))
            {
                selected = new ComboBoxItem
                {
                    Content = Localization.GetString("QuickTranslationSavedMicrophoneUnavailable"),
                    Tag = savedDeviceId
                };
                MicrophoneDevice.Items.Add(selected);
            }

            MicrophoneDevice.SelectedItem = selected ?? MicrophoneDevice.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }

        private string GetSelectedMicrophoneDeviceId()
        {
            return MicrophoneDevice?.SelectedItem is ComboBoxItem item
                ? item.Tag?.ToString() ?? string.Empty
                : ApplicationConfig.GetSettings(QuickTranslationConfig.MicrophoneDeviceIdKey) ?? string.Empty;
        }

        private void StartMicrophoneLevelTimer()
        {
            _microphoneLevelTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _microphoneLevelTimer.Tick -= MicrophoneLevelTimer_Tick;
            _microphoneLevelTimer.Tick += MicrophoneLevelTimer_Tick;
            _microphoneLevelTimer.Start();
        }

        private void StopMicrophoneLevelTimer()
        {
            if (_microphoneLevelTimer == null)
            {
                return;
            }

            _microphoneLevelTimer.Stop();
            _microphoneLevelTimer.Tick -= MicrophoneLevelTimer_Tick;
        }

        private void MicrophoneLevelTimer_Tick(object sender, object e)
        {
            var peak = AudioDeviceService.GetPeakLevel(GetSelectedMicrophoneDeviceId());
            var percent = Math.Clamp(peak * 100d, 0d, 100d);
            MicrophoneLevelBar.Value = percent;
            MicrophoneLevelValue.Text = $"{percent:0}%";
        }

        private async void RefreshMicrophones_Click(object sender, RoutedEventArgs e)
        {
            LoadMicrophoneDevices();
            await RefreshSpeechPrerequisiteWarningAsync(requestMicrophonePermission: false);
        }

        private async void MicrophoneDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded)
            {
                return;
            }

            ApplicationConfig.SaveSettings(QuickTranslationConfig.MicrophoneDeviceIdKey, GetSelectedMicrophoneDeviceId());
            await RefreshSpeechPrerequisiteWarningAsync(requestMicrophonePermission: false);
        }

        private void LoadVoices()
        {
            _voices = SuccessAudioService.GetVoices();
            PopulateVoiceComboBox(RecordingStartVoice);
            PopulateVoiceComboBox(RecordingEndVoice);
            PopulateVoiceComboBox(SuccessVoice);
            PopulateVoiceComboBox(TranslationFailureVoice);
        }

        private void PopulateVoiceComboBox(ComboBox comboBox)
        {
            comboBox.Items.Clear();
            foreach (var voice in _voices)
            {
                comboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{voice.DisplayName} ({voice.Language})",
                    Tag = voice.Id
                });
            }
        }

        private void LoadSettings()
        {
            _updatingEnableToggle = true;
            QuickTranslationEnabled.IsOn = QuickTranslationConfig.IsEnabled();
            _updatingEnableToggle = false;

            SelectRecordingStartTiming(QuickTranslationConfig.GetRecordingStartTiming());

            _hotkeys = QuickTranslationConfig.GetHotkeys().Select(item => item.Clone()).ToList();
            RebuildHotkeyList();

            LoadAudioCueSettings(
                QuickTranslationAudioCue.RecordingStart,
                RecordingStartSoundMode,
                RecordingStartPromptText,
                RecordingStartVoice,
                RecordingStartSpeakingRate,
                RecordingStartSpeakingRateValue);
            LoadAudioCueSettings(
                QuickTranslationAudioCue.RecordingEnd,
                RecordingEndSoundMode,
                RecordingEndPromptText,
                RecordingEndVoice,
                RecordingEndSpeakingRate,
                RecordingEndSpeakingRateValue);
            LoadAudioCueSettings(
                QuickTranslationAudioCue.Success,
                SuccessSoundMode,
                SuccessPromptText,
                SuccessVoice,
                SuccessSpeakingRate,
                SuccessSpeakingRateValue);
            LoadAudioCueSettings(
                QuickTranslationAudioCue.TranslationFailure,
                TranslationFailureSoundMode,
                TranslationFailurePromptText,
                TranslationFailureVoice,
                TranslationFailureSpeakingRate,
                TranslationFailureSpeakingRateValue);

            SuccessVolume.Value = QuickTranslationConfig.GetSuccessVolume();
        }

        private void LoadAudioCueSettings(
            QuickTranslationAudioCue cue,
            ComboBox modeComboBox,
            TextBox promptTextBox,
            ComboBox voiceComboBox,
            Slider speakingRateSlider,
            TextBlock speakingRateValueText)
        {
            var mode = ApplicationConfig.GetSettings(QuickTranslationConfig.GetSoundModeKey(cue))
                       ?? QuickTranslationConfig.SoundModeSystem;
            SelectSoundMode(modeComboBox, mode);

            promptTextBox.Text = ApplicationConfig.GetSettings(QuickTranslationConfig.GetPromptTextKey(cue))
                                 ?? QuickTranslationConfig.GetDefaultPromptText(cue);

            var savedVoiceId = ApplicationConfig.GetSettings(QuickTranslationConfig.GetVoiceIdKey(cue));
            var voiceIndex = _voices.ToList().FindIndex(voice =>
                string.Equals(voice.Id, savedVoiceId, StringComparison.Ordinal));
            if (voiceIndex < 0 && _voices.Count > 0)
            {
                var defaultVoice = SpeechSynthesizer.DefaultVoice;
                voiceIndex = defaultVoice == null
                    ? -1
                    : _voices.ToList().FindIndex(voice =>
                        string.Equals(voice.Id, defaultVoice.Id, StringComparison.Ordinal));
                if (voiceIndex < 0)
                {
                    voiceIndex = 0;
                }
            }
            voiceComboBox.SelectedIndex = voiceIndex;

            var speakingRate = QuickTranslationConfig.GetSpeakingRate(cue);
            speakingRateSlider.Value = speakingRate;
            speakingRateValueText.Text = FormatSpeakingRate(speakingRate);

            UpdateCustomAudioDisplay(cue);
            UpdateAudioModeVisibility(cue, mode);
        }

        private static void SelectSoundMode(ComboBox comboBox, string mode)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (string.Equals(item.Tag?.ToString(), mode, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            comboBox.SelectedIndex = 0;
        }

        private void SelectRecordingStartTiming(string timing)
        {
            foreach (ComboBoxItem item in RecordingStartTiming.Items)
            {
                if (string.Equals(item.Tag?.ToString(), timing, StringComparison.OrdinalIgnoreCase))
                {
                    RecordingStartTiming.SelectedItem = item;
                    return;
                }
            }

            RecordingStartTiming.SelectedIndex = 0;
        }

        private static string FormatSpeakingRate(double value)
            => $"{value:0.0}x";

        private void HotkeySettingsExpander_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // SettingsExpander can measure item content with a wider desired size than
            // its visible card. Explicitly cap the inner content to the current expander
            // width so description text wraps and dynamically-created rows stay inside.
            if (HotkeyContentRoot != null)
            {
                HotkeyContentRoot.MaxWidth = Math.Max(0, e.NewSize.Width - 96);
            }
        }

        private void RebuildHotkeyList()
        {
            _rebuildingHotkeyList = true;
            try
            {
                HotkeyList.Items.Clear();

                for (var index = 0; index < _hotkeys.Count; index++)
                {
                    var binding = _hotkeys[index];
                    var row = new Grid
                    {
                        Margin = new Thickness(0, 4, 0, 4),
                        MinWidth = 0,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Tag = binding.Id
                    };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var indexText = new TextBlock
                    {
                        Text = (index + 1).ToString(CultureInfo.InvariantCulture),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    Grid.SetColumn(indexText, 0);
                    row.Children.Add(indexText);

                    var hotkeyBox = new TextBox
                    {
                        Text = binding.Shortcut,
                        Tag = binding.Id,
                        MinWidth = 0,
                        Margin = new Thickness(0, 0, 8, 0),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Center,
                        IsReadOnly = true,
                        IsTabStop = false
                    };
                    Grid.SetColumn(hotkeyBox, 1);
                    row.Children.Add(hotkeyBox);

                    var captureButton = new Button
                    {
                        Tag = binding.Id,
                        Margin = new Thickness(0, 0, 10, 0),
                        MinWidth = 86,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                        Content = Localization.GetString("QuickTranslationCaptureHotkey")
                    };
                    captureButton.Click += CaptureHotkey_Click;
                    Grid.SetColumn(captureButton, 2);
                    row.Children.Add(captureButton);

                    var targetBox = new ComboBox
                    {
                        Tag = binding.Id,
                        MinWidth = 0,
                        Margin = new Thickness(0, 0, 10, 0),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    PopulateTargetLanguages(targetBox);
                    SelectTargetLanguage(targetBox, binding.TargetLanguage);
                    targetBox.SelectionChanged += TargetLanguage_SelectionChanged;
                    Grid.SetColumn(targetBox, 3);
                    row.Children.Add(targetBox);

                    var removeButton = new Button
                    {
                        Tag = binding.Id,
                        MinWidth = 40,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                        Content = new FontIcon { Glyph = "\uE74D" }
                    };
                    ToolTipService.SetToolTip(removeButton, Localization.GetString("QuickTranslationRemoveHotkey"));
                    removeButton.Click += RemoveHotkey_Click;
                    Grid.SetColumn(removeButton, 4);
                    row.Children.Add(removeButton);

                    HotkeyList.Items.Add(row);
                }
            }
            finally
            {
                _rebuildingHotkeyList = false;
            }
        }

        private static void PopulateTargetLanguages(ComboBox comboBox)
        {
            comboBox.Items.Clear();
            foreach (var language in GTranslate.Language.LanguageDictionary.Values
                         .OrderBy(item => item.NativeName, StringComparer.CurrentCultureIgnoreCase))
            {
                comboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{language.NativeName} ({language.ISO6391})",
                    Tag = language.ISO6391
                });
            }
        }

        private static void SelectTargetLanguage(ComboBox comboBox, string languageCode)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (string.Equals(item.Tag?.ToString(), languageCode, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private void AddHotkey_Click(object sender, RoutedEventArgs e)
        {
            CancelHotkeyCapture();
            var id = QuickTranslationConfig.FindFirstAvailableId(_hotkeys.Select(item => item.Id));
            _hotkeys.Add(new QuickTranslationHotkey
            {
                Id = id,
                Enabled = true,
                Shortcut = GetSuggestedShortcut(),
                TargetLanguage = "en"
            });

            SaveHotkeysAndRefresh();
            RebuildHotkeyList();
        }

        private void RemoveHotkey_Click(object sender, RoutedEventArgs e)
        {
            CancelHotkeyCapture();
            if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var id))
            {
                return;
            }

            _hotkeys.RemoveAll(item => item.Id == id);
            SaveHotkeysAndRefresh();
            RebuildHotkeyList();
        }

        private string GetSuggestedShortcut()
        {
            var used = _hotkeys.Select(item => item.Shortcut).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var modifiers = new[] { "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "Ctrl+Alt+Shift" };
            var keys = Enumerable.Range(1, 9).Select(number => number.ToString(CultureInfo.InvariantCulture))
                .Concat(Enumerable.Range(1, 24).Select(number => $"F{number}"))
                .Concat(Enumerable.Range('A', 26).Select(value => ((char)value).ToString()))
                .ToArray();

            foreach (var modifier in modifiers)
            {
                foreach (var key in keys)
                {
                    var candidate = $"{modifier}+{key}";
                    if (!used.Contains(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return "Ctrl+Alt+F24";
        }

        private async void QuickTranslationEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loaded || _updatingEnableToggle)
            {
                return;
            }

            if (!QuickTranslationEnabled.IsOn)
            {
                ApplicationConfig.SaveSettings(QuickTranslationConfig.EnabledKey, "false");
                RefreshGlobalHotkeys();
                if (Application.Current is App disabledApp)
                {
                    await disabledApp.ReleaseQuickTranslationSpeechAsync();
                }
                UpdateSpeechRecognitionStateDisplay();
                await RefreshSpeechPrerequisiteWarningAsync(requestMicrophonePermission: false);
                return;
            }

            var status = await CheckSpeechPrerequisitesAsync(requestMicrophonePermission: true);
            UpdateSpeechPrerequisiteWarning(status);
            if (!status.IsReady)
            {
                ApplicationConfig.SaveSettings(QuickTranslationConfig.EnabledKey, "false");
                _updatingEnableToggle = true;
                QuickTranslationEnabled.IsOn = false;
                _updatingEnableToggle = false;
                RefreshGlobalHotkeys();
                await ShowPrerequisiteDialogAsync(status);
                return;
            }

            ApplicationConfig.SaveSettings(QuickTranslationConfig.EnabledKey, "true");
            RefreshGlobalHotkeys();
            if (Application.Current is App enabledApp)
            {
                await enabledApp.WarmUpQuickTranslationSpeechAsync();
                AttachSpeechRecognitionState();
                UpdateSpeechRecognitionStateDisplay();
            }
        }

        private async void CaptureHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (!_loaded || sender is not Button button ||
                !int.TryParse(button.Tag?.ToString(), out var id))
            {
                return;
            }

            if (_capturingHotkeyId == id)
            {
                await FinishHotkeyCaptureAsync();
                return;
            }

            CancelHotkeyCapture();

            var row = FindHotkeyRow(id);
            if (row == null)
            {
                return;
            }

            var display = row.Children.OfType<TextBox>().FirstOrDefault(textBox =>
                int.TryParse(textBox.Tag?.ToString(), out var textId) && textId == id);
            if (display == null)
            {
                return;
            }

            _capturingHotkeyId = id;
            _pendingCapturedShortcut = null;
            _capturingHotkeyDisplay = display;
            _capturingHotkeyButton = button;
            display.Text = Localization.GetString("QuickTranslationPressHotkeyPrompt");
            button.Content = Localization.GetString("QuickTranslationFinishHotkeyCapture");

            if (Application.Current is App app)
            {
                app.SuspendQuickTranslationHotkeys();
            }

        }

        private async Task FinishHotkeyCaptureAsync()
        {
            if (!_capturingHotkeyId.HasValue)
            {
                return;
            }

            var id = _capturingHotkeyId.Value;
            var binding = _hotkeys.FirstOrDefault(item => item.Id == id);
            if (binding == null)
            {
                CancelHotkeyCapture();
                return;
            }

            if (string.IsNullOrWhiteSpace(_pendingCapturedShortcut))
            {
                await ShowMessageAsync(
                    Localization.GetString("QuickTranslationInvalidHotkey"),
                    Localization.GetString("QuickTranslationHotkeyCaptureNoKey"));
                return;
            }

            var duplicate = _hotkeys.Any(item => item.Id != id &&
                string.Equals(item.Shortcut, _pendingCapturedShortcut, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                await ShowMessageAsync(
                    Localization.GetString("QuickTranslationInvalidHotkey"),
                    Localization.GetString("QuickTranslationDuplicateHotkey"));
                return;
            }

            binding.Shortcut = _pendingCapturedShortcut;
            if (_capturingHotkeyDisplay != null)
            {
                _capturingHotkeyDisplay.Text = binding.Shortcut;
            }

            EndHotkeyCaptureUi();
            QuickTranslationConfig.SaveHotkeys(_hotkeys);
            if (Application.Current is App app)
            {
                app.ResumeQuickTranslationHotkeys();
            }
        }

        private void CancelHotkeyCapture()
        {
            if (!_capturingHotkeyId.HasValue)
            {
                return;
            }

            var binding = _hotkeys.FirstOrDefault(item => item.Id == _capturingHotkeyId.Value);
            if (_capturingHotkeyDisplay != null && binding != null)
            {
                _capturingHotkeyDisplay.Text = binding.Shortcut;
            }

            EndHotkeyCaptureUi();
            if (Application.Current is App app)
            {
                app.ResumeQuickTranslationHotkeys();
            }
        }

        private void EndHotkeyCaptureUi()
        {
            if (_capturingHotkeyButton != null)
            {
                _capturingHotkeyButton.Content = Localization.GetString("QuickTranslationCaptureHotkey");
            }

            _capturingHotkeyId = null;
            _pendingCapturedShortcut = null;
            _capturingHotkeyDisplay = null;
            _capturingHotkeyButton = null;
        }

        private Grid FindHotkeyRow(int id)
        {
            foreach (var item in HotkeyList.Items)
            {
                if (item is Grid row && int.TryParse(row.Tag?.ToString(), out var rowId) && rowId == id)
                {
                    return row;
                }
            }

            return null;
        }

        private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!_capturingHotkeyId.HasValue)
            {
                return;
            }

            e.Handled = true;
            var modifiers = GetCurrentHotkeyModifiers();
            if (IsModifierKey(e.Key))
            {
                if (_capturingHotkeyDisplay != null)
                {
                    _capturingHotkeyDisplay.Text = FormatModifierPreview(modifiers);
                }
                return;
            }

            if (!HotkeyGesture.TryCreateFromVirtualKey(modifiers, (uint)e.Key, out var gesture, out _))
            {
                return;
            }

            _pendingCapturedShortcut = gesture.NormalizedText;
            if (_capturingHotkeyDisplay != null)
            {
                _capturingHotkeyDisplay.Text = gesture.NormalizedText;
            }
        }

        private static string FormatModifierPreview(uint modifiers)
        {
            var parts = new List<string>();
            if ((modifiers & HotkeyGesture.ModControl) != 0) parts.Add("Ctrl");
            if ((modifiers & HotkeyGesture.ModAlt) != 0) parts.Add("Alt");
            if ((modifiers & HotkeyGesture.ModShift) != 0) parts.Add("Shift");
            if ((modifiers & HotkeyGesture.ModWin) != 0) parts.Add("Win");
            return parts.Count == 0 ? Localization.GetString("QuickTranslationPressHotkeyPrompt") : string.Join('+', parts) + "+…";
        }

        private static bool IsModifierKey(VirtualKey key)
        {
            var value = (uint)key;
            return value is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C;
        }

        private static uint GetCurrentHotkeyModifiers()
        {
            uint modifiers = 0;
            if (IsVirtualKeyDown(0x11)) modifiers |= HotkeyGesture.ModControl;
            if (IsVirtualKeyDown(0x12)) modifiers |= HotkeyGesture.ModAlt;
            if (IsVirtualKeyDown(0x10)) modifiers |= HotkeyGesture.ModShift;
            if (IsVirtualKeyDown(0x5B) || IsVirtualKeyDown(0x5C)) modifiers |= HotkeyGesture.ModWin;
            return modifiers;
        }

        private static bool IsVirtualKeyDown(int virtualKey)
            => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

        private void TargetLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded || _rebuildingHotkeyList || sender is not ComboBox comboBox ||
                comboBox.SelectedItem is not ComboBoxItem selected ||
                !int.TryParse(comboBox.Tag?.ToString(), out var id))
            {
                return;
            }

            var binding = _hotkeys.FirstOrDefault(item => item.Id == id);
            if (binding == null)
            {
                return;
            }

            binding.TargetLanguage = selected.Tag?.ToString() ?? "en";
            SaveHotkeysAndRefresh();
        }

        private void SaveHotkeysAndRefresh()
        {
            QuickTranslationConfig.SaveHotkeys(_hotkeys);
            if (!_capturingHotkeyId.HasValue)
            {
                RefreshGlobalHotkeys();
            }
        }

        private void RefreshGlobalHotkeys()
        {
            if (Application.Current is App app)
            {
                app.RefreshQuickTranslationHotkeys();
            }
        }

        private void RecordingStartTiming_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded || RecordingStartTiming.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            var value = item.Tag?.ToString() ?? QuickTranslationConfig.RecordingStartTimingOnPlaybackStart;
            ApplicationConfig.SaveSettings(QuickTranslationConfig.RecordingStartTimingKey, value);
        }

        private void AudioSoundMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox comboBox ||
                !TryGetAudioCue(comboBox.Tag, out var cue) ||
                comboBox.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            var mode = item.Tag?.ToString() ?? QuickTranslationConfig.SoundModeSystem;
            UpdateAudioModeVisibility(cue, mode);
            if (_loaded)
            {
                ApplicationConfig.SaveSettings(QuickTranslationConfig.GetSoundModeKey(cue), mode);
            }
        }

        private void UpdateAudioModeVisibility(QuickTranslationAudioCue cue, string mode)
        {
            var (ttsExpander, customCard) = cue switch
            {
                QuickTranslationAudioCue.RecordingStart =>
                    (RecordingStartTtsSettingsExpander, RecordingStartCustomAudioSettingsCard),
                QuickTranslationAudioCue.RecordingEnd =>
                    (RecordingEndTtsSettingsExpander, RecordingEndCustomAudioSettingsCard),
                QuickTranslationAudioCue.TranslationFailure =>
                    (TranslationFailureTtsSettingsExpander, TranslationFailureCustomAudioSettingsCard),
                _ => (SuccessTtsSettingsExpander, SuccessCustomAudioSettingsCard)
            };

            ttsExpander.Visibility = string.Equals(mode, QuickTranslationConfig.SoundModeTts, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
            customCard.Visibility = string.Equals(mode, QuickTranslationConfig.SoundModeCustom, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void AudioPromptText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loaded || sender is not TextBox textBox || !TryGetAudioCue(textBox.Tag, out var cue))
            {
                return;
            }

            ApplicationConfig.SaveSettings(QuickTranslationConfig.GetPromptTextKey(cue), textBox.Text);
        }

        private void AudioVoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded || sender is not ComboBox comboBox ||
                !TryGetAudioCue(comboBox.Tag, out var cue) ||
                comboBox.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            ApplicationConfig.SaveSettings(
                QuickTranslationConfig.GetVoiceIdKey(cue),
                item.Tag?.ToString() ?? string.Empty);
        }

        private void AudioSpeakingRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (sender is not Slider slider || !TryGetAudioCue(slider.Tag, out var cue))
            {
                return;
            }

            var normalized = Math.Clamp(e.NewValue, 0.5, 6.0);
            var valueText = cue switch
            {
                QuickTranslationAudioCue.RecordingStart => RecordingStartSpeakingRateValue,
                QuickTranslationAudioCue.RecordingEnd => RecordingEndSpeakingRateValue,
                QuickTranslationAudioCue.TranslationFailure => TranslationFailureSpeakingRateValue,
                _ => SuccessSpeakingRateValue
            };
            if (valueText != null)
            {
                valueText.Text = FormatSpeakingRate(normalized);
            }

            if (_loaded)
            {
                ApplicationConfig.SaveSettings(
                    QuickTranslationConfig.GetSpeakingRateKey(cue),
                    normalized.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void SuccessVolume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loaded)
            {
                ApplicationConfig.SaveSettings(
                    QuickTranslationConfig.SuccessVolumeKey,
                    e.NewValue.ToString(CultureInfo.InvariantCulture));
            }
        }

        private async void ChooseCustomAudio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || !TryGetAudioCue(button.Tag, out var cue))
            {
                return;
            }

            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".wav");
                picker.FileTypeFilter.Add(".mp3");
                picker.FileTypeFilter.Add(".m4a");
                picker.FileTypeFilter.Add(".wma");

                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
                var file = await picker.PickSingleFileAsync();
                if (file == null)
                {
                    return;
                }

                var sourceName = file.Name;
                var sourcePath = file.Path;
                var service = GetAudioService(out var ownsService);
                try
                {
                    var playbackPath = await service.ImportCustomAudioAsync(file, cue);
                    ApplicationConfig.SaveSettings(QuickTranslationConfig.GetCustomAudioPathKey(cue), playbackPath);
                    ApplicationConfig.SaveSettings(QuickTranslationConfig.GetCustomAudioDisplayNameKey(cue), sourceName);
                    ApplicationConfig.SaveSettings(QuickTranslationConfig.GetCustomAudioSourcePathKey(cue), sourcePath);
                    UpdateCustomAudioDisplay(cue);
                }
                finally
                {
                    if (ownsService)
                    {
                        service.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to import custom quick translation audio. Cue={0}", cue);
                await ShowMessageAsync(Localization.GetString("QuickTranslationAudioError"), ex.Message);
            }
        }

        private void UpdateCustomAudioDisplay(QuickTranslationAudioCue cue)
        {
            var displayName = ApplicationConfig.GetSettings(QuickTranslationConfig.GetCustomAudioDisplayNameKey(cue));
            var sourcePath = ApplicationConfig.GetSettings(QuickTranslationConfig.GetCustomAudioSourcePathKey(cue));
            var playbackPath = ApplicationConfig.GetSettings(QuickTranslationConfig.GetCustomAudioPathKey(cue));

            var effectiveName = !string.IsNullOrWhiteSpace(displayName)
                ? displayName
                : (!string.IsNullOrWhiteSpace(playbackPath) ? Path.GetFileName(playbackPath) : null);
            var effectivePath = !string.IsNullOrWhiteSpace(sourcePath) ? sourcePath : playbackPath;

            var (nameTextBlock, pathTextBlock) = GetCustomAudioTextBlocks(cue);
            nameTextBlock.Text = string.IsNullOrWhiteSpace(effectiveName)
                ? Localization.GetString("QuickTranslationNoCustomAudio")
                : effectiveName;
            pathTextBlock.Text = effectivePath ?? string.Empty;
            ToolTipService.SetToolTip(nameTextBlock, effectiveName ?? string.Empty);
            ToolTipService.SetToolTip(pathTextBlock, effectivePath ?? string.Empty);
        }

        private (TextBlock Name, TextBlock Path) GetCustomAudioTextBlocks(QuickTranslationAudioCue cue)
        {
            return cue switch
            {
                QuickTranslationAudioCue.RecordingStart =>
                    (RecordingStartAudioFileName, RecordingStartAudioFilePath),
                QuickTranslationAudioCue.RecordingEnd =>
                    (RecordingEndAudioFileName, RecordingEndAudioFilePath),
                QuickTranslationAudioCue.TranslationFailure =>
                    (TranslationFailureAudioFileName, TranslationFailureAudioFilePath),
                _ => (SuccessAudioFileName, SuccessAudioFilePath)
            };
        }

        private async void PreviewAudio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || !TryGetAudioCue(button.Tag, out var cue) || !button.IsEnabled)
            {
                return;
            }

            button.IsEnabled = false;
            using var previewCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                var service = GetAudioService(out var ownsService);
                try
                {
                    await service.PreviewAsync(cue, previewCts.Token);
                }
                finally
                {
                    // A temporary service owns its MediaPlayers. Keep it alive briefly
                    // after Play() so a non-blocking preview is not cut off.
                    if (ownsService)
                    {
                        _ = Task.Delay(TimeSpan.FromSeconds(10))
                            .ContinueWith(_ => service.Dispose(), TaskScheduler.Default);
                    }
                }
            }
            catch (OperationCanceledException) when (previewCts.IsCancellationRequested)
            {
                _logger.Warn("Quick translation audio preview timed out. Cue={0}", cue);
                await ShowMessageAsync(
                    Localization.GetString("QuickTranslationAudioError"),
                    Localization.GetString("QuickTranslationPreviewTimeout"));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to preview quick translation audio. Cue={0}", cue);
                await ShowMessageAsync(Localization.GetString("QuickTranslationAudioError"), ex.Message);
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var service = GetAudioService(out var ownsService);
                try
                {
                    await service.ClearTtsCacheAsync();
                }
                finally
                {
                    if (ownsService)
                    {
                        service.Dispose();
                    }
                }

                await ShowMessageAsync(
                    Localization.GetString("QuickTranslationCacheCleared"),
                    Localization.GetString("QuickTranslationCacheClearedDescription"));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to clear quick translation TTS cache.");
                await ShowMessageAsync(Localization.GetString("QuickTranslationAudioError"), ex.Message);
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            CancelHotkeyCapture();
            StopMicrophoneLevelTimer();
            DetachSpeechRecognitionState();
        }

        private void AttachSpeechRecognitionState()
        {
            var service = (Application.Current as App)?.QuickTranslationService;
            if (ReferenceEquals(service, _attachedQuickTranslationService))
            {
                UpdateSpeechRecognitionStateDisplay();
                return;
            }

            DetachSpeechRecognitionState();
            _attachedQuickTranslationService = service;
            if (_attachedQuickTranslationService != null)
            {
                _attachedQuickTranslationService.SpeechStateChanged += QuickTranslationSpeechStateChanged;
            }

            UpdateSpeechRecognitionStateDisplay();
        }

        private void DetachSpeechRecognitionState()
        {
            if (_attachedQuickTranslationService != null)
            {
                _attachedQuickTranslationService.SpeechStateChanged -= QuickTranslationSpeechStateChanged;
                _attachedQuickTranslationService = null;
            }
        }

        private void QuickTranslationSpeechStateChanged(object sender, SpeechRecognitionStateChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(UpdateSpeechRecognitionStateDisplay);
        }

        private void UpdateSpeechRecognitionStateDisplay()
        {
            if (SpeechRecognitionStateText == null)
            {
                return;
            }

            var service = _attachedQuickTranslationService ?? (Application.Current as App)?.QuickTranslationService;
            var state = service?.SpeechState ?? SpeechRecognitionServiceState.NotInitialized;
            SpeechRecognitionStateText.Text = state switch
            {
                SpeechRecognitionServiceState.WarmingUp => Localization.GetString("QuickTranslationSpeechStateWarmingUp"),
                SpeechRecognitionServiceState.Ready => Localization.GetString("QuickTranslationSpeechStateReady"),
                SpeechRecognitionServiceState.Recording => Localization.GetString("QuickTranslationSpeechStateRecording"),
                SpeechRecognitionServiceState.Recognizing => Localization.GetString("QuickTranslationSpeechStateRecognizing"),
                SpeechRecognitionServiceState.Unavailable => Localization.GetString("QuickTranslationSpeechStateUnavailable"),
                _ => Localization.GetString("QuickTranslationSpeechStateNotInitialized")
            };

            SpeechRecognitionStateDetail.Text = service?.SpeechStateDetail ?? string.Empty;
            SpeechRecognitionStateDetail.Visibility = string.IsNullOrWhiteSpace(SpeechRecognitionStateDetail.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private async Task RefreshSpeechPrerequisiteWarningAsync(bool requestMicrophonePermission)
        {
            var status = await CheckSpeechPrerequisitesAsync(requestMicrophonePermission);
            UpdateSpeechPrerequisiteWarning(status);
        }

        private async Task<SpeechPrerequisiteStatus> CheckSpeechPrerequisitesAsync(bool requestMicrophonePermission)
        {
            try
            {
                var deviceId = GetSelectedMicrophoneDeviceId();
                using var timeoutCts = new CancellationTokenSource(requestMicrophonePermission ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(12));
                return await _prerequisiteService.CheckAsync(
                    deviceId,
                    requestMicrophonePermission,
                    timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                var status = new SpeechPrerequisiteStatus
                {
                    Detail = Localization.GetString("QuickTranslationPrerequisiteCheckTimeout")
                };
                status.Problems.Add(SpeechPrerequisiteProblem.LocalModelUnavailable);
                return status;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to check quick translation local speech prerequisites.");
                var status = new SpeechPrerequisiteStatus
                {
                    Detail = ex.Message
                };
                status.Problems.Add(SpeechPrerequisiteProblem.LocalModelUnavailable);
                return status;
            }
        }

        private void UpdateSpeechPrerequisiteWarning(SpeechPrerequisiteStatus status)
        {
            if (status.IsReady)
            {
                SpeechPrerequisiteWarning.IsOpen = false;
                SpeechPrerequisiteWarning.Message = string.Empty;
                SpeechPrerequisiteWarningAction.Visibility = Visibility.Collapsed;
                _warningSettingsUri = null;
                return;
            }

            SpeechPrerequisiteWarning.Message = BuildPrerequisiteMessage(status);
            SpeechPrerequisiteWarning.IsOpen = true;
            _warningSettingsUri = GetSettingsUri(status);
            SpeechPrerequisiteWarningAction.Visibility = string.IsNullOrWhiteSpace(_warningSettingsUri)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private string BuildPrerequisiteMessage(SpeechPrerequisiteStatus status)
        {
            var messages = status.Problems
                .Select(problem => problem switch
                {
                    SpeechPrerequisiteProblem.MicrophonePermissionRequired =>
                        Localization.GetString("QuickTranslationMicrophonePermissionRequired"),
                    SpeechPrerequisiteProblem.MicrophonePermissionDenied =>
                        Localization.GetString("QuickTranslationMicrophonePermissionDenied"),
                    SpeechPrerequisiteProblem.MicrophoneCapabilityMissing =>
                        Localization.GetString("QuickTranslationMicrophoneCapabilityMissing"),
                    SpeechPrerequisiteProblem.MicrophoneUnavailable =>
                        Localization.GetString("QuickTranslationMicrophoneUnavailable"),
                    SpeechPrerequisiteProblem.SelectedMicrophoneUnavailable =>
                        Localization.GetString("QuickTranslationSelectedMicrophoneUnavailable"),
                    SpeechPrerequisiteProblem.LocalModelUnavailable =>
                        Localization.GetString("QuickTranslationLocalModelUnavailable"),
                    _ => string.Empty
                })
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.CurrentCulture)
                .ToList();

            if (messages.Count == 0)
            {
                messages.Add(Localization.GetString("QuickTranslationSpeechUnavailable"));
            }

            if (!string.IsNullOrWhiteSpace(status.Detail))
            {
                messages.Add(status.Detail);
            }

            return string.Join(Environment.NewLine, messages);
        }

        private static string GetSettingsUri(SpeechPrerequisiteStatus status)
        {
            if (status.Problems.Any(problem => problem is
                    SpeechPrerequisiteProblem.MicrophonePermissionRequired or
                    SpeechPrerequisiteProblem.MicrophonePermissionDenied or
                    SpeechPrerequisiteProblem.MicrophoneCapabilityMissing or
                    SpeechPrerequisiteProblem.MicrophoneUnavailable or
                    SpeechPrerequisiteProblem.SelectedMicrophoneUnavailable))
            {
                return "ms-settings:privacy-microphone";
            }

            return null;
        }

        private async void SpeechPrerequisiteWarningAction_Click(object sender, RoutedEventArgs e)
        {
            await OpenPrerequisiteSettingsAsync();
        }

        private async Task OpenPrerequisiteSettingsAsync()
        {
            if (string.IsNullOrWhiteSpace(_warningSettingsUri))
            {
                return;
            }

            try
            {
                await Launcher.LaunchUriAsync(new Uri(_warningSettingsUri));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to open Windows microphone privacy settings.");
            }
        }

        private async Task ShowPrerequisiteDialogAsync(SpeechPrerequisiteStatus status)
        {
            var settingsUri = GetSettingsUri(status);
            _warningSettingsUri = settingsUri;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = Localization.GetString("QuickTranslationPrerequisiteDialogTitle"),
                Content = BuildPrerequisiteMessage(status),
                CloseButtonText = Localization.GetString("QuickTranslationClose")
            };

            if (!string.IsNullOrWhiteSpace(settingsUri))
            {
                dialog.PrimaryButtonText = Localization.GetString("QuickTranslationOpenSettings");
            }

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await OpenPrerequisiteSettingsAsync();
            }
        }

        private static bool TryGetAudioCue(object tag, out QuickTranslationAudioCue cue)
        {
            return Enum.TryParse(tag?.ToString(), ignoreCase: true, out cue);
        }

        private static SuccessAudioService GetAudioService(out bool ownsService)
        {
            if (Application.Current is App app && app.QuickTranslationAudioService != null)
            {
                ownsService = false;
                return app.QuickTranslationAudioService;
            }

            ownsService = true;
            return new SuccessAudioService();
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = title,
                Content = message,
                CloseButtonText = Localization.GetString("QuickTranslationClose")
            };
            await dialog.ShowAsync();
        }
    }

}
