#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Services;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Pickers;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class ChatTtsPage : Page
    {
        private bool _loaded;
        private bool _updatingUi;
        private TtsVoiceInfo[] _voices = Array.Empty<TtsVoiceInfo>();

        private sealed class GptSoVitsProfileListItem
        {
            public string Id { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string ModelLine { get; init; } = string.Empty;
            public string ReferenceLine { get; init; } = string.Empty;
            public string PromptLine { get; init; } = string.Empty;
            public bool CanSelect { get; init; }
            public Visibility SelectedVisibility { get; init; }
        }

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

            await RefreshTtsOutputDevicesAsync();
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
            GptSoVitsApiUrl.Text = ChatTtsConfig.GetGptSoVitsApiUrl();

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

        private async Task RefreshTtsOutputDevicesAsync()
        {
            ChatTtsOutputDeviceInfoBar.IsOpen = false;
            try
            {
                var devices = await TtsOutputDeviceService.GetAvailableDevicesAsync();
                if (!_loaded)
                {
                    return;
                }

                var savedId = ChatTtsConfig.GetOutputDeviceId();
                _updatingUi = true;
                try
                {
                    ChatTtsOutputDevice.Items.Clear();
                    ChatTtsOutputDevice.Items.Add(new ComboBoxItem
                    {
                        Content = Localization.GetString("ChatTtsSystemDefaultOutputDevice"),
                        Tag = string.Empty
                    });
                    foreach (var device in devices)
                    {
                        ChatTtsOutputDevice.Items.Add(new ComboBoxItem
                        {
                            Content = device.Name,
                            Tag = device.Id
                        });
                    }

                    var selected = ChatTtsOutputDevice.Items.OfType<ComboBoxItem>()
                        .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), savedId,
                            StringComparison.OrdinalIgnoreCase));
                    if (selected is null && !string.IsNullOrWhiteSpace(savedId))
                    {
                        selected = new ComboBoxItem
                        {
                            Content = Localization.GetString("ChatTtsSavedOutputDeviceUnavailable"),
                            Tag = savedId
                        };
                        ChatTtsOutputDevice.Items.Add(selected);
                    }

                    ChatTtsOutputDevice.SelectedItem = selected ?? ChatTtsOutputDevice.Items[0];
                }
                finally
                {
                    _updatingUi = false;
                }
            }
            catch (Exception ex)
            {
                ShowInfo(ChatTtsOutputDeviceInfoBar, Localization.GetString("ChatTtsOutputDevicesLoadFailed"),
                    ex.Message, InfoBarSeverity.Warning);
            }
        }

        private async void RefreshTtsOutputDevices_Click(object sender, RoutedEventArgs e)
        {
            await RefreshTtsOutputDevicesAsync();
        }

        private void ChatTtsOutputDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded || _updatingUi || ChatTtsOutputDevice.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            ApplicationConfig.SaveSettings(ChatTtsConfig.OutputDeviceIdKey,
                item.Tag?.ToString() ?? string.Empty);
        }

        private async Task RefreshProviderUiAsync()
        {
            var providerId = GetSelectedProviderId();
            var isLocal = string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal);
            var isGptSoVits = string.Equals(providerId, ChatTtsConfig.ProviderGptSoVits, StringComparison.Ordinal);
            ChatTtsVoiceInfoBar.IsOpen = false;
            GptSoVitsConnectionInfoBar.IsOpen = false;
            GptSoVitsProfilesInfoBar.IsOpen = false;
            SherpaSettingsSection.Visibility = isLocal ? Visibility.Visible : Visibility.Collapsed;
            GptSoVitsSettingsSection.Visibility = isGptSoVits ? Visibility.Visible : Visibility.Collapsed;
            ChatTtsVoiceCard.Visibility = isGptSoVits ? Visibility.Collapsed : Visibility.Visible;
            ChatTtsVoiceInfoBar.Visibility = isGptSoVits ? Visibility.Collapsed : Visibility.Visible;
            if (isLocal)
            {
                RefreshSherpaModelStatus();
            }
            if (isGptSoVits)
            {
                await RefreshGptSoVitsStatusAsync();
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
            var voiceInfoBar = string.Equals(providerId, ChatTtsConfig.ProviderGptSoVits, StringComparison.Ordinal)
                ? GptSoVitsProfilesInfoBar
                : ChatTtsVoiceInfoBar;
            voiceInfoBar.IsOpen = false;
            var needsLocalModel = string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal)
                && !BundledKokoroTtsModel.IsAvailable;
            var needsGptProfile = string.Equals(providerId, ChatTtsConfig.ProviderGptSoVits, StringComparison.Ordinal)
                && GptSoVitsProfileStore.Shared.GetProfiles().Count == 0;
            SetVoiceControlAvailability(false, needsLocalModel, needsGptProfile);
            ChatTtsVoice.Items.Clear();
            _voices = Array.Empty<TtsVoiceInfo>();

            ChatTtsVoiceCard.Header = string.Equals(providerId, ChatTtsConfig.ProviderGptSoVits, StringComparison.Ordinal)
                ? Localization.GetString("ChatTtsGptSoVitsCharacter")
                : Localization.GetString("ChatTtsVoice");
            ChatTtsVoiceCard.Description = string.Equals(providerId, ChatTtsConfig.ProviderGptSoVits, StringComparison.Ordinal)
                ? Localization.GetString("ChatTtsGptSoVitsCharacterDescription")
                : Localization.GetString("ChatTtsVoiceDescription");

            if (needsLocalModel || needsGptProfile)
            {
                UpdateGptSoVitsProfileButtons();
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
                SetVoiceControlAvailability(_voices.Length > 0, missingLocalModel: false, missingGptProfile: false);
                UpdateGptSoVitsProfileButtons();

                if (voiceIndex >= 0
                    && !string.Equals(savedVoiceId, _voices[voiceIndex].Id, StringComparison.Ordinal))
                {
                    SaveVoiceId(providerId, _voices[voiceIndex].Id);
                }
            }
            catch (Exception ex)
            {
                if (string.Equals(GetSelectedProviderId(), providerId, StringComparison.Ordinal))
                {
                    ShowInfo(voiceInfoBar, Localization.GetString("ChatTtsVoiceLoadFailed"), ex.Message,
                        InfoBarSeverity.Warning);
                }
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
            if (string.Equals(providerId, ChatTtsConfig.ProviderGptSoVits, StringComparison.Ordinal))
            {
                var provider = new GptSoVitsTtsProvider();
                ownedProvider = provider;
                return provider;
            }

            var windows = new WindowsTtsProvider();
            ownedProvider = windows;
            return windows;
        }

        private string GetSelectedProviderId()
        {
            if (ChatTtsProvider.SelectedItem is not ComboBoxItem item)
            {
                return ChatTtsConfig.ProviderWindows;
            }

            var tag = item.Tag?.ToString();
            if (string.Equals(tag, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal))
            {
                return ChatTtsConfig.ProviderSherpaOnnx;
            }
            if (string.Equals(tag, ChatTtsConfig.ProviderGptSoVits, StringComparison.Ordinal))
            {
                return ChatTtsConfig.ProviderGptSoVits;
            }
            return ChatTtsConfig.ProviderWindows;
        }

        private static void SaveVoiceId(string providerId, string voiceId)
        {
            var key = string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal)
                ? ChatTtsConfig.SherpaVoiceIdKey
                : string.Equals(providerId, ChatTtsConfig.ProviderGptSoVits, StringComparison.Ordinal)
                    ? ChatTtsConfig.GptSoVitsVoiceIdKey
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
            UpdateGptSoVitsProfileButtons();
        }

        private void GptSoVitsApiUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loaded || _updatingUi)
            {
                return;
            }

            var value = GptSoVitsApiUrl.Text?.Trim() ?? string.Empty;
            ApplicationConfig.SaveSettings(ChatTtsConfig.GptSoVitsApiUrlKey, value);
            if (Application.Current is App app && app.ChatTtsService is not null)
            {
                app.ChatTtsService.GptSoVitsProvider.InvalidateWeights();
            }
            GptSoVitsConnectionInfoBar.IsOpen = false;
            GptSoVitsStatusText.Text = Localization.GetString("ChatTtsGptSoVitsStatusUnchecked");
        }

        private async void GptSoVitsCheckButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshGptSoVitsStatusAsync(showFailureInfo: true);
        }

        private async Task RefreshGptSoVitsStatusAsync(bool showFailureInfo = false)
        {
            if (GptSoVitsStatusText is null || GptSoVitsCheckButton is null)
            {
                return;
            }

            GptSoVitsConnectionInfoBar.IsOpen = false;
            GptSoVitsStatusText.Text = Localization.GetString("ChatTtsGptSoVitsStatusChecking");
            GptSoVitsCheckButton.IsEnabled = false;
            IDisposable? ownedProvider = null;
            try
            {
                var provider = (GptSoVitsTtsProvider)GetProvider(ChatTtsConfig.ProviderGptSoVits, out ownedProvider);
                var connected = await provider.TestConnectionAsync();
                GptSoVitsStatusText.Text = Localization.GetString(
                    connected ? "ChatTtsGptSoVitsStatusConnected" : "ChatTtsGptSoVitsStatusDisconnected");
                if (!connected && showFailureInfo)
                {
                    ShowInfo(
                        GptSoVitsConnectionInfoBar,
                        Localization.GetString("ChatTtsGptSoVitsConnectionFailed"),
                        Localization.GetString("ChatTtsGptSoVitsConnectionFailedDescription"),
                        InfoBarSeverity.Warning);
                }
            }
            catch (Exception ex)
            {
                GptSoVitsStatusText.Text = Localization.GetString("ChatTtsGptSoVitsStatusDisconnected");
                if (showFailureInfo)
                {
                    ShowInfo(GptSoVitsConnectionInfoBar, Localization.GetString("ChatTtsGptSoVitsConnectionFailed"),
                        ex.Message, InfoBarSeverity.Warning);
                }
            }
            finally
            {
                ownedProvider?.Dispose();
                GptSoVitsCheckButton.IsEnabled = true;
            }
        }

        private async void GptSoVitsInfoButton_Click(object sender, RoutedEventArgs e)
        {
            var panel = new StackPanel { Spacing = 8, MaxWidth = 620 };
            panel.Children.Add(new TextBlock
            {
                Text = Localization.GetString("ChatTtsGptSoVitsInfoBody"),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new HyperlinkButton
            {
                Content = Localization.GetString("ChatTtsGptSoVitsProjectLink"),
                Padding = new Thickness(0, 4, 0, 4),
                NavigateUri = new Uri("https://github.com/RVC-Boss/GPT-SoVITS")
            });
            panel.Children.Add(new HyperlinkButton
            {
                Content = Localization.GetString("ChatTtsGptSoVitsApiLink"),
                Padding = new Thickness(0, 4, 0, 4),
                NavigateUri = new Uri("https://github.com/RVC-Boss/GPT-SoVITS/blob/main/api_v2.py")
            });

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "GPT-SoVITS",
                Content = panel,
                CloseButtonText = Localization.GetString("QuickTranslationClose")
            };
            dialog.Resources["ContentDialogMaxWidth"] = 700d;
            await dialog.ShowAsync();
        }

        private async void GptSoVitsAddProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var profile = await ShowGptSoVitsProfileDialogAsync(null);
            if (profile is null)
            {
                return;
            }

            GptSoVitsProfileStore.Shared.Upsert(profile);
            InvalidateGptSoVitsWeights();
            SaveVoiceId(ChatTtsConfig.ProviderGptSoVits, profile.Id);
            await LoadVoicesAsync(ChatTtsConfig.ProviderGptSoVits);
        }

        private async void GptSoVitsEditProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedId = GetSelectedGptSoVitsProfileId();
            var existing = GptSoVitsProfileStore.Shared.GetById(selectedId);
            if (existing is null)
            {
                return;
            }

            var profile = await ShowGptSoVitsProfileDialogAsync(existing);
            if (profile is null)
            {
                return;
            }

            GptSoVitsProfileStore.Shared.Upsert(profile);
            InvalidateGptSoVitsWeights();
            SaveVoiceId(ChatTtsConfig.ProviderGptSoVits, profile.Id);
            await LoadVoicesAsync(ChatTtsConfig.ProviderGptSoVits);
        }

        private async void GptSoVitsDeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedId = GetSelectedGptSoVitsProfileId();
            var profile = GptSoVitsProfileStore.Shared.GetById(selectedId);
            if (profile is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = Localization.GetString("ChatTtsGptSoVitsDeleteConfirmTitle"),
                Content = string.Format(
                    CultureInfo.CurrentCulture,
                    Localization.GetString("ChatTtsGptSoVitsDeleteConfirmDescription"),
                    profile.Name),
                PrimaryButtonText = Localization.GetString("AiDelete"),
                CloseButtonText = Localization.GetString("CommonCancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            if (GptSoVitsProfileStore.Shared.Delete(profile.Id))
            {
                InvalidateGptSoVitsWeights();
                ApplicationConfig.SaveSettings(ChatTtsConfig.GptSoVitsVoiceIdKey, string.Empty);
                await LoadVoicesAsync(ChatTtsConfig.ProviderGptSoVits);
            }
        }

        private void UpdateGptSoVitsProfileButtons()
        {
            RefreshGptSoVitsProfilesList();
        }

        private string? GetSelectedGptSoVitsProfileId()
        {
            if (!string.Equals(GetSelectedProviderId(), ChatTtsConfig.ProviderGptSoVits, StringComparison.Ordinal))
            {
                return null;
            }

            if (ChatTtsVoice.SelectedItem is ComboBoxItem item)
            {
                return item.Tag?.ToString();
            }

            var savedId = ChatTtsConfig.GetVoiceId(ChatTtsConfig.ProviderGptSoVits);
            return string.IsNullOrWhiteSpace(savedId) ? null : savedId;
        }

        private void RefreshGptSoVitsProfilesList()
        {
            if (GptSoVitsProfilesList is null || GptSoVitsProfilesEmptyState is null)
            {
                return;
            }

            var selectedId = ChatTtsConfig.GetVoiceId(ChatTtsConfig.ProviderGptSoVits);
            var profiles = GptSoVitsProfileStore.Shared
                .GetProfiles()
                .Select(profile => new GptSoVitsProfileListItem
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    ModelLine = $"GPT: {FormatPathForDisplay(profile.GptWeightsPath)}    SoVITS: {FormatPathForDisplay(profile.SoVitsWeightsPath)}",
                    ReferenceLine = $"{Localization.GetString("ChatTtsGptSoVitsReferenceAudio")}: {FormatPathForDisplay(profile.ReferenceAudioPath)}    {Localization.GetString("ChatTtsGptSoVitsReferenceLanguage")}: {profile.PromptLanguage} → {profile.TextLanguage}",
                    PromptLine = string.IsNullOrWhiteSpace(profile.PromptText)
                        ? $"{Localization.GetString("ChatTtsGptSoVitsReferenceText")}: -"
                        : $"{Localization.GetString("ChatTtsGptSoVitsReferenceText")}: {profile.PromptText}",
                    CanSelect = !string.Equals(profile.Id, selectedId, StringComparison.Ordinal),
                    SelectedVisibility = string.Equals(profile.Id, selectedId, StringComparison.Ordinal)
                        ? Visibility.Visible
                        : Visibility.Collapsed
                })
                .ToList();

            GptSoVitsProfilesList.ItemsSource = profiles;
            var hasProfiles = profiles.Count > 0;
            GptSoVitsProfilesEmptyState.Visibility = hasProfiles ? Visibility.Collapsed : Visibility.Visible;
            GptSoVitsProfilesList.Visibility = hasProfiles ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string FormatPathForDisplay(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "-";
            }

            try
            {
                return Path.GetFileName(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }

        private GptSoVitsVoiceProfile? GetProfileFromSender(object sender)
        {
            if (sender is not FrameworkElement element)
            {
                return null;
            }

            return GptSoVitsProfileStore.Shared.GetById(element.Tag?.ToString());
        }

        private void SelectVoiceItem(string voiceId)
        {
            if (ChatTtsVoice is null)
            {
                return;
            }

            _updatingUi = true;
            try
            {
                for (var i = 0; i < ChatTtsVoice.Items.Count; i++)
                {
                    if (ChatTtsVoice.Items[i] is ComboBoxItem item
                        && string.Equals(item.Tag?.ToString(), voiceId, StringComparison.Ordinal))
                    {
                        ChatTtsVoice.SelectedIndex = i;
                        return;
                    }
                }

                ChatTtsVoice.SelectedIndex = -1;
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private void SelectGptSoVitsProfile_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetProfileFromSender(sender);
            if (profile is null)
            {
                return;
            }

            SaveVoiceId(ChatTtsConfig.ProviderGptSoVits, profile.Id);
            SelectVoiceItem(profile.Id);
            RefreshGptSoVitsProfilesList();
            SetVoiceControlAvailability(true, missingLocalModel: false, missingGptProfile: false);
        }

        private async void EditGptSoVitsProfileListItem_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetProfileFromSender(sender);
            if (profile is null)
            {
                return;
            }

            var edited = await ShowGptSoVitsProfileDialogAsync(profile);
            if (edited is null)
            {
                return;
            }

            GptSoVitsProfileStore.Shared.Upsert(edited);
            InvalidateGptSoVitsWeights();
            SaveVoiceId(ChatTtsConfig.ProviderGptSoVits, edited.Id);
            await LoadVoicesAsync(ChatTtsConfig.ProviderGptSoVits);
        }

        private async void DeleteGptSoVitsProfileListItem_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetProfileFromSender(sender);
            if (profile is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = Localization.GetString("ChatTtsGptSoVitsDeleteConfirmTitle"),
                Content = string.Format(
                    CultureInfo.CurrentCulture,
                    Localization.GetString("ChatTtsGptSoVitsDeleteConfirmDescription"),
                    profile.Name),
                PrimaryButtonText = Localization.GetString("AiDelete"),
                CloseButtonText = Localization.GetString("CommonCancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            if (GptSoVitsProfileStore.Shared.Delete(profile.Id))
            {
                InvalidateGptSoVitsWeights();
                ApplicationConfig.SaveSettings(ChatTtsConfig.GptSoVitsVoiceIdKey, string.Empty);
                await LoadVoicesAsync(ChatTtsConfig.ProviderGptSoVits);
            }
        }

        private void InvalidateGptSoVitsWeights()
        {
            if (Application.Current is App app && app.ChatTtsService is not null)
            {
                app.ChatTtsService.GptSoVitsProvider.InvalidateWeights();
            }
        }

        private async Task<GptSoVitsVoiceProfile?> ShowGptSoVitsProfileDialogAsync(GptSoVitsVoiceProfile? existing)
        {
            var profile = existing?.Clone() ?? new GptSoVitsVoiceProfile();
            var nameBox = new TextBox
            {
                Header = Localization.GetString("ChatTtsGptSoVitsProfileName"),
                Text = profile.Name,
                PlaceholderText = Localization.GetString("ChatTtsGptSoVitsProfileNamePlaceholder")
            };
            var gptPathBox = CreatePathTextBox(
                Localization.GetString("ChatTtsGptSoVitsGptModel"),
                profile.GptWeightsPath,
                Localization.GetString("ChatTtsGptSoVitsModelOptional"));
            var sovitsPathBox = CreatePathTextBox(
                Localization.GetString("ChatTtsGptSoVitsSovitsModel"),
                profile.SoVitsWeightsPath,
                Localization.GetString("ChatTtsGptSoVitsModelOptional"));
            var referenceBox = CreatePathTextBox(
                Localization.GetString("ChatTtsGptSoVitsReferenceAudio"),
                profile.ReferenceAudioPath,
                Localization.GetString("ChatTtsGptSoVitsReferenceAudioPlaceholder"));
            var promptBox = new TextBox
            {
                Header = Localization.GetString("ChatTtsGptSoVitsReferenceText"),
                Text = profile.PromptText,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 72,
                PlaceholderText = Localization.GetString("ChatTtsGptSoVitsReferenceTextPlaceholder")
            };
            var promptLanguage = CreateLanguageComboBox(
                Localization.GetString("ChatTtsGptSoVitsReferenceLanguage"),
                profile.PromptLanguage,
                promptLanguagesOnly: true);
            var textLanguage = CreateLanguageComboBox(
                Localization.GetString("ChatTtsGptSoVitsOutputLanguage"),
                profile.TextLanguage,
                promptLanguagesOnly: false);

            // WinUI's overlay scrollbar occupies the right edge of the ScrollViewer viewport.
            // Reserve the gutter INSIDE the fixed-width form so file-picker buttons,
            // textbox clear buttons and language dropdowns never sit under the scrollbar.
            // Padding (rather than a right margin) also avoids extending the scrolled
            // content beyond the viewport on smaller dialog sizes.
            var content = new StackPanel
            {
                Spacing = 10,
                Width = 640,
                Padding = new Thickness(0, 0, 32, 0)
            };
            content.Children.Add(nameBox);
            content.Children.Add(CreatePickerRow(gptPathBox, new[] { ".ckpt" }));
            content.Children.Add(CreatePickerRow(sovitsPathBox, new[] { ".pth" }));
            content.Children.Add(CreatePickerRow(referenceBox, new[] { ".wav", ".mp3", ".flac", ".ogg", ".m4a" }));
            content.Children.Add(promptBox);
            content.Children.Add(promptLanguage);
            content.Children.Add(textLanguage);

            var scrollViewer = new ScrollViewer
            {
                Content = content,
                MaxHeight = 580,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Enabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled
            };

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = existing is null
                    ? Localization.GetString("ChatTtsGptSoVitsAddProfile")
                    : Localization.GetString("ChatTtsGptSoVitsEditProfile"),
                Content = scrollViewer,
                PrimaryButtonText = Localization.GetString("AiSave"),
                CloseButtonText = Localization.GetString("CommonCancel"),
                DefaultButton = ContentDialogButton.Primary
            };
            dialog.Resources["ContentDialogMaxWidth"] = 760d;

            void UpdateSaveState()
            {
                dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(nameBox.Text)
                    && !string.IsNullOrWhiteSpace(referenceBox.Text);
            }
            nameBox.TextChanged += (_, _) => UpdateSaveState();
            referenceBox.TextChanged += (_, _) => UpdateSaveState();
            UpdateSaveState();

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return null;
            }

            profile.Name = nameBox.Text.Trim();
            profile.GptWeightsPath = gptPathBox.Text.Trim();
            profile.SoVitsWeightsPath = sovitsPathBox.Text.Trim();
            profile.ReferenceAudioPath = referenceBox.Text.Trim();
            profile.PromptText = promptBox.Text.Trim();
            profile.PromptLanguage = GetSelectedLanguageTag(promptLanguage, "zh");
            profile.TextLanguage = GetSelectedLanguageTag(textLanguage, "auto");
            return profile;
        }

        private static TextBox CreatePathTextBox(string header, string value, string placeholder)
        {
            return new TextBox
            {
                Header = header,
                Text = value,
                PlaceholderText = placeholder,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        private Grid CreatePickerRow(TextBox textBox, IReadOnlyList<string> extensions)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(textBox, 0);
            grid.Children.Add(textBox);

            var button = new Button
            {
                VerticalAlignment = VerticalAlignment.Bottom
            };
            button.Content = new FontIcon { Glyph = "\uE8B7" };
            button.Click += async (_, _) => await PickProfileFileAsync(textBox, extensions);
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
            return grid;
        }

        private static ComboBox CreateLanguageComboBox(string header, string selectedTag, bool promptLanguagesOnly)
        {
            var combo = new ComboBox
            {
                Header = header,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var items = promptLanguagesOnly
                ? new (string Label, string Tag)[]
                {
                    ("中文", "zh"), ("English", "en"), ("日本語", "ja"), ("한국어", "ko"), ("粤语", "yue")
                }
                : new (string Label, string Tag)[]
                {
                    ("Auto", "auto"), ("中文", "all_zh"), ("English", "en"), ("日本語", "all_ja"),
                    ("한국어", "all_ko"), ("粤语", "all_yue"), ("中英混合", "zh"), ("日英混合", "ja"),
                    ("韩英混合", "ko"), ("粤英混合", "yue"), ("多语种 / 粤语", "auto_yue")
                };
            foreach (var (label, tag) in items)
            {
                combo.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
            }

            var index = Array.FindIndex(items, item => string.Equals(item.Tag, selectedTag, StringComparison.Ordinal));
            combo.SelectedIndex = index >= 0 ? index : 0;
            return combo;
        }

        private static string GetSelectedLanguageTag(ComboBox combo, string fallback)
        {
            return combo.SelectedItem is ComboBoxItem item && item.Tag is string tag
                ? tag
                : fallback;
        }

        private static async Task PickProfileFileAsync(TextBox target, IReadOnlyList<string> extensions)
        {
            var picker = new FileOpenPicker();
            foreach (var extension in extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance));
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                target.Text = file.Path;
            }
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

        private void SetVoiceControlAvailability(bool enabled, bool missingLocalModel, bool missingGptProfile = false)
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
                : missingGptProfile
                    ? Localization.GetString("ChatTtsGptSoVitsVoiceRequiresProfile")
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
            ShowInfo(ChatTtsPreviewInfoBar, title, message, severity);
        }

        private static void ShowInfo(InfoBar target, string title, string message, InfoBarSeverity severity)
        {
            target.Title = title;
            target.Message = message;
            target.Severity = severity;
            target.IsOpen = true;
        }
    }
}
