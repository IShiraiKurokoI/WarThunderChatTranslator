#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Services;
using Windows.Media.SpeechSynthesis;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class ChatTtsPage : Page
    {
        private bool _loaded;
        private bool _updatingUi;
        private TtsVoiceInfo[] _voices = Array.Empty<TtsVoiceInfo>();

        public ChatTtsPage()
        {
            InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _loaded = false;
            _updatingUi = true;
            try
            {
                LoadSettings();
                SelectProvider(ChatTtsConfig.GetProvider());
            }
            finally
            {
                _updatingUi = false;
                _loaded = true;
            }

            await RefreshProviderUiAsync();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _loaded = false;
        }

        private void LoadSettings()
        {
            ChatTtsEnabled.IsOn = ChatTtsConfig.IsEnabled();
            ChatTtsSpeakingRate.Value = ChatTtsConfig.GetSpeakingRate();
            ChatTtsSpeakingRateValue.Text = $"{ChatTtsConfig.GetSpeakingRate():0.0}x";
            ChatTtsVolume.Value = ChatTtsConfig.GetVolume() * 100.0;
            ChatTtsVolumeValue.Text = $"{Math.Round(ChatTtsConfig.GetVolume() * 100):0}%";
            ChatTtsSpeakAlly.IsOn = ChatTtsConfig.ShouldSpeakAlly();
            ChatTtsSpeakEnemy.IsOn = ChatTtsConfig.ShouldSpeakEnemy();
            ChatTtsSpeakSystem.IsOn = ChatTtsConfig.ShouldSpeakSystem();
            ChatTtsQueueCapacity.Value = ChatTtsConfig.GetQueueCapacity();
            SherpaThreads.Value = ChatTtsConfig.GetSherpaNumThreads();

            var maxQueueAgeSeconds = ChatTtsConfig.GetMaxQueueAgeSecondsOrZero();
            ChatTtsExpirationSeconds.Value = maxQueueAgeSeconds > 0
                ? maxQueueAgeSeconds
                : ChatTtsConfig.DefaultMaxQueueAgeSeconds;
            ChatTtsExpirationMode.SelectedIndex = maxQueueAgeSeconds <= 0 ? 0 : 1;
            UpdateExpirationControls();

            ChatTtsPreviewText.Text = Localization.GetString("ChatTtsPreviewDefaultText");
        }

        private void SelectProvider(string providerId)
        {
            for (var i = 0; i < ChatTtsProvider.Items.Count; i++)
            {
                if (ChatTtsProvider.Items[i] is ComboBoxItem item
                    && string.Equals(item.Tag?.ToString(), providerId, StringComparison.Ordinal))
                {
                    ChatTtsProvider.SelectedIndex = i;
                    return;
                }
            }

            ChatTtsProvider.SelectedIndex = 0;
        }

        private async Task RefreshProviderUiAsync()
        {
            var providerId = GetSelectedProviderId();
            var isLocal = string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal);
            SherpaSettingsSection.Visibility = isLocal ? Visibility.Visible : Visibility.Collapsed;
            if (isLocal)
            {
                RefreshSherpaModelStatus();
            }

            await LoadVoicesAsync(providerId);
        }

        private void RefreshSherpaModelStatus(string? detailOverride = null)
        {
            if (BundledKokoroTtsModel.IsAvailable)
            {
                SherpaModelStateText.Text = Localization.GetString("ChatTtsModelStateReady");
                SherpaModelStateDetail.Text = detailOverride ?? string.Empty;
                SherpaModelStateDetail.Visibility = string.IsNullOrWhiteSpace(SherpaModelStateDetail.Text)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                return;
            }

            SherpaModelStateText.Text = Localization.GetString("ChatTtsModelStateMissing");
            SherpaModelStateDetail.Text = detailOverride ?? Localization.GetString("ChatTtsModelStateMissingDetail");
            SherpaModelStateDetail.Visibility = Visibility.Visible;
        }

        private async Task LoadVoicesAsync(string providerId)
        {
            var needsLocalModel = string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal)
                && !BundledKokoroTtsModel.IsAvailable;
            SetVoiceControlAvailability(false, needsLocalModel);
            ChatTtsVoice.Items.Clear();
            _voices = Array.Empty<TtsVoiceInfo>();

            if (needsLocalModel)
            {
                return;
            }

            IDisposable? ownedProvider = null;
            try
            {
                var provider = GetProvider(providerId, out ownedProvider);
                if (string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal))
                {
                    RefreshSherpaModelStatus(Localization.GetString("ChatTtsSherpaVoicesLoading"));
                    _voices = await Task.Run(() => provider.GetVoices().ToArray());
                    RefreshSherpaModelStatus();
                }
                else
                {
                    _voices = provider.GetVoices().ToArray();
                }

                foreach (var voice in _voices)
                {
                    ChatTtsVoice.Items.Add(new ComboBoxItem
                    {
                        Content = $"{voice.DisplayName} ({voice.Language})",
                        Tag = voice.Id
                    });
                }

                var savedVoiceId = ChatTtsConfig.GetVoiceId(providerId);
                var voiceIndex = Array.FindIndex(_voices, voice =>
                    string.Equals(voice.Id, savedVoiceId, StringComparison.Ordinal));

                if (voiceIndex < 0 && string.Equals(providerId, ChatTtsConfig.ProviderWindows, StringComparison.Ordinal))
                {
                    var defaultVoiceId = SpeechSynthesizer.DefaultVoice?.Id ?? string.Empty;
                    voiceIndex = Array.FindIndex(_voices, voice =>
                        string.Equals(voice.Id, defaultVoiceId, StringComparison.Ordinal));
                }

                if (voiceIndex < 0 && _voices.Length > 0)
                {
                    if (string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal)
                        && Localization.CurrentLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                    {
                        voiceIndex = Array.FindIndex(_voices, voice =>
                            voice.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
                    }
                    if (voiceIndex < 0)
                    {
                        voiceIndex = 0;
                    }
                }

                _updatingUi = true;
                ChatTtsVoice.SelectedIndex = voiceIndex;
                _updatingUi = false;
                SetVoiceControlAvailability(_voices.Length > 0, missingLocalModel: false);

                if (voiceIndex >= 0
                    && !string.Equals(savedVoiceId, _voices[voiceIndex].Id, StringComparison.Ordinal))
                {
                    SaveVoiceId(providerId, _voices[voiceIndex].Id);
                }
            }
            catch (Exception ex)
            {
                ShowInfo(Localization.GetString("ChatTtsVoiceLoadFailed"), ex.Message, InfoBarSeverity.Warning);
                if (string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal))
                {
                    RefreshSherpaModelStatus(ex.Message);
                }
            }
            finally
            {
                ownedProvider?.Dispose();
            }
        }

        private ITtsProvider GetProvider(string providerId, out IDisposable? ownedProvider)
        {
            ownedProvider = null;
            if (Application.Current is App app && app.ChatTtsService is not null)
            {
                return app.ChatTtsService.GetProvider(providerId);
            }

            if (string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal))
            {
                var provider = new SherpaOnnxTtsProvider();
                ownedProvider = provider;
                return provider;
            }

            var windows = new WindowsTtsProvider();
            ownedProvider = windows;
            return windows;
        }

        private string GetSelectedProviderId()
        {
            return ChatTtsProvider.SelectedItem is ComboBoxItem item
                && string.Equals(item.Tag?.ToString(), ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal)
                ? ChatTtsConfig.ProviderSherpaOnnx
                : ChatTtsConfig.ProviderWindows;
        }

        private static void SaveVoiceId(string providerId, string voiceId)
        {
            var key = string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal)
                ? ChatTtsConfig.SherpaVoiceIdKey
                : ChatTtsConfig.WindowsVoiceIdKey;
            ApplicationConfig.SaveSettings(key, voiceId);
        }

        private void ChatTtsEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            ApplicationConfig.SaveSettings(ChatTtsConfig.EnabledKey, ChatTtsEnabled.IsOn.ToString().ToLowerInvariant());
        }

        private async void ChatTtsProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded || _updatingUi)
            {
                return;
            }

            var providerId = GetSelectedProviderId();
            ApplicationConfig.SaveSettings(ChatTtsConfig.ProviderKey, providerId);
            await RefreshProviderUiAsync();
        }

        private void ChatTtsVoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded || _updatingUi || ChatTtsVoice.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            SaveVoiceId(GetSelectedProviderId(), item.Tag?.ToString() ?? string.Empty);
        }

        private async void SherpaModelInfoButton_Click(object sender, RoutedEventArgs e)
        {
            var content = new StackPanel
            {
                Spacing = 4,
                MaxWidth = 620
            };
            content.Children.Add(new TextBlock
            {
                Text = BuildSherpaModelInfoText(),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            });

            var links = new StackPanel
            {
                Margin = new Thickness(0, 8, 0, 0),
                Spacing = 0
            };
            links.Children.Add(CreateModelInfoLink(
                Localization.GetString("QuickTranslationSpeechModelOriginalLink"),
                BundledKokoroTtsModel.UpstreamModelUrl));
            links.Children.Add(CreateModelInfoLink(
                Localization.GetString("QuickTranslationSpeechModelPackageLink"),
                BundledKokoroTtsModel.SourceUrl));
            links.Children.Add(CreateModelInfoLink(
                Localization.GetString("QuickTranslationSpeechModelLicenseLink"),
                "https://www.apache.org/licenses/LICENSE-2.0"));
            links.Children.Add(CreateModelInfoLink(
                Localization.GetString("QuickTranslationSpeechRuntimeLink"),
                "https://github.com/k2-fsa/sherpa-onnx"));
            content.Children.Add(links);

            var scrollViewer = new ScrollViewer
            {
                Content = content,
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Enabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled
            };

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = Localization.GetString("ChatTtsModelInfoTitle"),
                Content = scrollViewer,
                CloseButtonText = Localization.GetString("QuickTranslationClose")
            };
            dialog.Resources["ContentDialogMaxWidth"] = 700d;
            await dialog.ShowAsync();
        }

        private static HyperlinkButton CreateModelInfoLink(string text, string uri)
        {
            return new HyperlinkButton
            {
                Content = text,
                Padding = new Thickness(0, 4, 0, 4),
                NavigateUri = new Uri(uri)
            };
        }

        private string BuildSherpaModelInfoText()
        {
            var fileState = Localization.GetString("ChatTtsModelFileMissing");
            try
            {
                if (File.Exists(BundledKokoroTtsModel.ModelFile))
                {
                    var sizeMiB = new FileInfo(BundledKokoroTtsModel.ModelFile).Length / 1024d / 1024d;
                    fileState = string.Format(
                        CultureInfo.CurrentCulture,
                        Localization.GetString("ChatTtsModelFilePresentFormat"),
                        sizeMiB);
                }
            }
            catch
            {
                // Keep the model status readable even if file metadata cannot be queried.
            }

            var availability = BundledKokoroTtsModel.IsAvailable
                ? Localization.GetString("ChatTtsModelStateReady")
                : Localization.GetString("ChatTtsModelStateMissing");

            return string.Format(
                CultureInfo.CurrentCulture,
                Localization.GetString("ChatTtsModelInfoBodyFormat"),
                BundledKokoroTtsModel.DisplayName,
                availability,
                fileState,
                BundledKokoroTtsModel.RuntimeDisplayName,
                BundledKokoroTtsModel.RuntimeVersion,
                BundledKokoroTtsModel.LicenseName,
                BundledKokoroTtsModel.ArchiveSha256,
                BundledKokoroTtsModel.DocumentationUrl);
        }

        private void SetVoiceControlAvailability(bool enabled, bool missingLocalModel)
        {
            if (ChatTtsVoiceCard is null || ChatTtsVoice is null)
            {
                return;
            }

            ChatTtsVoiceCard.IsEnabled = enabled;
            ChatTtsVoiceCard.Opacity = enabled ? 1.0 : 0.55;
            ChatTtsVoice.IsEnabled = enabled;
            ChatTtsVoice.PlaceholderText = missingLocalModel
                ? Localization.GetString("ChatTtsVoiceRequiresModel")
                : string.Empty;
            if (ChatTtsPreviewButton is not null)
            {
                ChatTtsPreviewButton.IsEnabled = enabled;
            }
        }

        private void SherpaThreads_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (!_loaded || double.IsNaN(args.NewValue))
            {
                return;
            }

            var threads = Math.Clamp(
                (int)Math.Round(args.NewValue),
                ChatTtsConfig.MinSherpaNumThreads,
                ChatTtsConfig.MaxSherpaNumThreads);
            ApplicationConfig.SaveSettings(ChatTtsConfig.SherpaNumThreadsKey, threads.ToString(CultureInfo.InvariantCulture));
            if (Application.Current is App app && app.ChatTtsService is not null)
            {
                app.ChatTtsService.SherpaProvider.InvalidateModel();
            }
        }

        private void ChatTtsSpeakingRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var value = Math.Clamp(e.NewValue, 0.5, 2.0);
            if (ChatTtsSpeakingRateValue is not null)
            {
                ChatTtsSpeakingRateValue.Text = $"{value:0.0}x";
            }
            if (_loaded)
            {
                ApplicationConfig.SaveSettings(ChatTtsConfig.SpeakingRateKey, value.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void ChatTtsVolume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var percent = Math.Clamp(e.NewValue, 0.0, 100.0);
            if (ChatTtsVolumeValue is not null)
            {
                ChatTtsVolumeValue.Text = $"{Math.Round(percent):0}%";
            }
            if (_loaded)
            {
                ApplicationConfig.SaveSettings(ChatTtsConfig.VolumeKey, (percent / 100.0).ToString(CultureInfo.InvariantCulture));
            }
        }

        private void ChatTtsQueueCapacity_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (!_loaded || double.IsNaN(args.NewValue)) return;
            var capacity = Math.Clamp((int)Math.Round(args.NewValue), ChatTtsConfig.MinQueueCapacity, ChatTtsConfig.MaxQueueCapacity);
            ApplicationConfig.SaveSettings(ChatTtsConfig.QueueCapacityKey, capacity.ToString(CultureInfo.InvariantCulture));
        }

        private void ChatTtsExpirationMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            UpdateExpirationControls();
            if (ChatTtsExpirationMode.SelectedItem is not ComboBoxItem item) return;
            if (string.Equals(item.Tag?.ToString(), "Never", StringComparison.Ordinal))
            {
                ApplicationConfig.SaveSettings(ChatTtsConfig.MaxQueueAgeSecondsKey, "0");
                return;
            }
            SaveExpirationSeconds();
        }

        private void ChatTtsExpirationSeconds_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (!_loaded || double.IsNaN(args.NewValue) || !IsExpirationBySeconds()) return;
            SaveExpirationSeconds();
        }

        private void SaveExpirationSeconds()
        {
            var rawValue = double.IsNaN(ChatTtsExpirationSeconds.Value)
                ? ChatTtsConfig.DefaultMaxQueueAgeSeconds
                : ChatTtsExpirationSeconds.Value;
            var seconds = Math.Clamp((int)Math.Round(rawValue), ChatTtsConfig.MinMaxQueueAgeSeconds, ChatTtsConfig.MaxMaxQueueAgeSeconds);
            ApplicationConfig.SaveSettings(ChatTtsConfig.MaxQueueAgeSecondsKey, seconds.ToString(CultureInfo.InvariantCulture));
        }

        private bool IsExpirationBySeconds()
        {
            return ChatTtsExpirationMode.SelectedItem is ComboBoxItem item
                && string.Equals(item.Tag?.ToString(), "Seconds", StringComparison.Ordinal);
        }

        private void UpdateExpirationControls()
        {
            if (ChatTtsExpirationSeconds is not null)
            {
                ChatTtsExpirationSeconds.IsEnabled = IsExpirationBySeconds();
            }
        }

        private void ChatTtsMessageType_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loaded || sender is not ToggleSwitch toggle) return;
            var key = toggle.Tag?.ToString() switch
            {
                "Enemy" => ChatTtsConfig.SpeakEnemyKey,
                "System" => ChatTtsConfig.SpeakSystemKey,
                _ => ChatTtsConfig.SpeakAllyKey
            };
            ApplicationConfig.SaveSettings(key, toggle.IsOn.ToString().ToLowerInvariant());
        }

        private async void ChatTtsPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            ChatTtsPreviewInfoBar.IsOpen = false;
            if (Application.Current is not App app || app.ChatTtsService is null)
            {
                ShowPreviewInfo(Localization.GetString("ChatTtsPreviewUnavailable"), Localization.GetString("ChatTtsPreviewUnavailableDescription"), InfoBarSeverity.Warning);
                return;
            }

            ChatTtsPreviewButton.IsEnabled = false;
            try
            {
                await app.ChatTtsService.PreviewAsync(ChatTtsPreviewText.Text);
                ShowPreviewInfo(Localization.GetString("ChatTtsPreviewSuccess"), string.Empty, InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowPreviewInfo(Localization.GetString("ChatTtsPreviewFailed"), ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                ChatTtsPreviewButton.IsEnabled = ChatTtsVoice.IsEnabled;
            }
        }

        private void ShowPreviewInfo(string title, string message, InfoBarSeverity severity)
        {
            ChatTtsPreviewInfoBar.Title = title;
            ChatTtsPreviewInfoBar.Message = message;
            ChatTtsPreviewInfoBar.Severity = severity;
            ChatTtsPreviewInfoBar.IsOpen = true;
        }

        private void ShowInfo(string title, string message, InfoBarSeverity severity)
        {
            ChatTtsInfoBar.Title = title;
            ChatTtsInfoBar.Message = message;
            ChatTtsInfoBar.Severity = severity;
            ChatTtsInfoBar.IsOpen = true;
        }
    }
}
