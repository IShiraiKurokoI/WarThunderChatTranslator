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
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class ChatTtsPage : Page
    {
        private readonly SherpaTtsModelManager _modelManager = SherpaTtsModelManager.Shared;
        private bool _loaded;
        private bool _updatingUi;
        private TtsVoiceInfo[] _voices = Array.Empty<TtsVoiceInfo>();
        private SherpaTtsModelInfo[] _models = Array.Empty<SherpaTtsModelInfo>();

        public ChatTtsPage()
        {
            InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _modelManager.DownloadStateChanged -= ModelManager_DownloadStateChanged;
            _modelManager.DownloadStateChanged += ModelManager_DownloadStateChanged;

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

            await RefreshProviderUiAsync(persistFallbackSelection: true);
            ApplyDownloadState(_modelManager.DownloadState);
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _loaded = false;
            _modelManager.DownloadStateChanged -= ModelManager_DownloadStateChanged;
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

        private async Task RefreshProviderUiAsync(bool persistFallbackSelection)
        {
            var providerId = GetSelectedProviderId();
            SherpaSettingsSection.Visibility = string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal))
            {
                RefreshSherpaModels(persistFallbackSelection);
            }

            await LoadVoicesAsync(providerId);
            ApplyDownloadState(_modelManager.DownloadState);
        }

        private void RefreshSherpaModels(bool persistFallbackSelection)
        {
            _updatingUi = true;
            try
            {
                _models = _modelManager.GetInstalledModels().ToArray();
                SherpaModelCombo.Items.Clear();
                foreach (var model in _models)
                {
                    SherpaModelCombo.Items.Add(new ComboBoxItem
                    {
                        Content = model.IsRecommended
                            ? Localization.GetString("ChatTtsRecommendedModelLabel")
                            : model.DisplayName,
                        Tag = model.Id
                    });
                }

                var configuredId = ChatTtsConfig.GetSherpaModelId();
                var selectedIndex = Array.FindIndex(_models, model =>
                    string.Equals(model.Id, configuredId, StringComparison.OrdinalIgnoreCase));
                if (selectedIndex < 0)
                {
                    selectedIndex = Array.FindIndex(_models, model => model.IsRecommended);
                }
                if (selectedIndex < 0 && _models.Length > 0)
                {
                    selectedIndex = 0;
                }

                SherpaModelCombo.SelectedIndex = selectedIndex;
                if (selectedIndex >= 0 && persistFallbackSelection
                    && !string.Equals(configuredId, _models[selectedIndex].Id, StringComparison.OrdinalIgnoreCase))
                {
                    ApplicationConfig.SaveSettings(ChatTtsConfig.SherpaModelIdKey, _models[selectedIndex].Id);
                }

                var recommendedInstalled = _modelManager.IsRecommendedModelInstalled();
                SherpaDownloadButton.IsEnabled = !recommendedInstalled && !_modelManager.DownloadState.IsBusy;
                SherpaImportButton.IsEnabled = !_modelManager.DownloadState.IsBusy;
                SherpaDeleteButton.IsEnabled = selectedIndex >= 0 && !_modelManager.DownloadState.IsBusy;
                if (!_modelManager.DownloadState.IsBusy)
                {
                    SherpaModelStatus.Text = string.Empty;
                }
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private async Task LoadVoicesAsync(string providerId)
        {
            var needsLocalModel = string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal)
                && _models.Length == 0;
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
                    SherpaModelStatus.Text = Localization.GetString("ChatTtsSherpaVoicesLoading");
                    _voices = await Task.Run(() => provider.GetVoices().ToArray());
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

                if (string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal))
                {
                    SherpaModelStatus.Text = string.Empty;
                }
            }
            catch (Exception ex)
            {
                ShowInfo(Localization.GetString("ChatTtsVoiceLoadFailed"), ex.Message, InfoBarSeverity.Warning);
                if (string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal))
                {
                    SherpaModelStatus.Text = ex.Message;
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
            await RefreshProviderUiAsync(persistFallbackSelection: true);
        }

        private void ChatTtsVoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded || _updatingUi || ChatTtsVoice.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            SaveVoiceId(GetSelectedProviderId(), item.Tag?.ToString() ?? string.Empty);
        }

        private async void SherpaModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded || _updatingUi || SherpaModelCombo.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            var modelId = item.Tag?.ToString() ?? string.Empty;
            SherpaDeleteButton.IsEnabled = !_modelManager.DownloadState.IsBusy && !string.IsNullOrWhiteSpace(modelId);
            ApplicationConfig.SaveSettings(ChatTtsConfig.SherpaModelIdKey, modelId);
            if (Application.Current is App app && app.ChatTtsService is not null)
            {
                app.ChatTtsService.SherpaProvider.InvalidateModel();
            }

            await LoadVoicesAsync(ChatTtsConfig.ProviderSherpaOnnx);
        }

        private void SherpaModelInfoButton_Click(object sender, RoutedEventArgs e)
        {
            SherpaModelInfoTip.IsOpen = true;
        }

        private void SherpaDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            SherpaModelInfoBar.IsOpen = false;
            _modelManager.StartRecommendedModelDownload();
            ApplyDownloadState(_modelManager.DownloadState);
        }

        private void ModelManager_DownloadStateChanged(SherpaTtsDownloadState state)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (!_loaded)
                {
                    return;
                }

                ApplyDownloadState(state);

                if (state.Status == SherpaTtsDownloadStatus.Completed)
                {
                    if (Application.Current is App app && app.ChatTtsService is not null)
                    {
                        app.ChatTtsService.SherpaProvider.InvalidateModel();
                    }

                    RefreshSherpaModels(persistFallbackSelection: true);
                    if (string.Equals(GetSelectedProviderId(), ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal))
                    {
                        await LoadVoicesAsync(ChatTtsConfig.ProviderSherpaOnnx);
                    }

                    ShowModelInfo(
                        Localization.GetString("ChatTtsModelDownloadSuccess"),
                        SherpaTtsModelManager.RecommendedModelDisplayName,
                        InfoBarSeverity.Success);
                }
                else if (state.Status == SherpaTtsDownloadStatus.Failed)
                {
                    ShowModelInfo(
                        Localization.GetString("ChatTtsModelDownloadFailed"),
                        state.ErrorMessage ?? string.Empty,
                        InfoBarSeverity.Error);
                }
            });
        }

        private void ApplyDownloadState(SherpaTtsDownloadState state)
        {
            var busy = state.IsBusy;
            SherpaDownloadButton.IsEnabled = !busy && !_modelManager.IsRecommendedModelInstalled();
            SherpaImportButton.IsEnabled = !busy;
            SherpaDeleteButton.IsEnabled = !busy && SherpaModelCombo.SelectedIndex >= 0;
            SherpaDownloadProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

            switch (state.Status)
            {
                case SherpaTtsDownloadStatus.Downloading:
                    if (state.Progress?.Percent is double percent)
                    {
                        SherpaDownloadProgress.IsIndeterminate = false;
                        SherpaDownloadProgress.Value = percent;
                        SherpaModelStatus.Text = string.Format(
                            CultureInfo.CurrentCulture,
                            Localization.GetString("ChatTtsModelDownloadProgress"),
                            percent);
                    }
                    else
                    {
                        SherpaDownloadProgress.IsIndeterminate = true;
                        SherpaModelStatus.Text = Localization.GetString("ChatTtsModelDownloading");
                    }
                    break;

                case SherpaTtsDownloadStatus.Installing:
                    SherpaDownloadProgress.IsIndeterminate = true;
                    SherpaModelStatus.Text = Localization.GetString("ChatTtsModelInstalling");
                    break;

                case SherpaTtsDownloadStatus.Failed:
                    SherpaDownloadProgress.IsIndeterminate = false;
                    SherpaModelStatus.Text = string.IsNullOrWhiteSpace(state.ErrorMessage)
                        ? Localization.GetString("ChatTtsModelDownloadFailed")
                        : state.ErrorMessage;
                    break;

                case SherpaTtsDownloadStatus.Cancelled:
                    SherpaDownloadProgress.IsIndeterminate = false;
                    SherpaModelStatus.Text = Localization.GetString("ChatTtsModelDownloadCancelled");
                    break;
            }
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

        private async void SherpaImportButton_Click(object sender, RoutedEventArgs e)
        {
            SherpaModelInfoBar.IsOpen = false;
            try
            {
                var picker = new FolderPicker();
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(
                    picker,
                    WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance));
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null)
                {
                    return;
                }

                SherpaImportButton.IsEnabled = false;
                SherpaModelStatus.Text = Localization.GetString("ChatTtsModelImporting");
                var model = await _modelManager.ImportModelAsync(folder.Path);
                ApplicationConfig.SaveSettings(ChatTtsConfig.SherpaModelIdKey, model.Id);
                if (Application.Current is App app && app.ChatTtsService is not null)
                {
                    app.ChatTtsService.SherpaProvider.InvalidateModel();
                }

                RefreshSherpaModels(persistFallbackSelection: true);
                await LoadVoicesAsync(ChatTtsConfig.ProviderSherpaOnnx);
                ShowModelInfo(Localization.GetString("ChatTtsModelImportSuccess"), model.DisplayName, InfoBarSeverity.Success);
            }
            catch (InvalidDataException)
            {
                ShowModelInfo(
                    Localization.GetString("ChatTtsModelImportFailed"),
                    Localization.GetString("ChatTtsUnsupportedImportModel"),
                    InfoBarSeverity.Error);
            }
            catch (Exception ex)
            {
                ShowModelInfo(Localization.GetString("ChatTtsModelImportFailed"), ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                SherpaImportButton.IsEnabled = !_modelManager.DownloadState.IsBusy;
            }
        }

        private async void SherpaDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_modelManager.DownloadState.IsBusy
                || SherpaModelCombo.SelectedItem is not ComboBoxItem selectedItem)
            {
                return;
            }

            var modelId = selectedItem.Tag?.ToString() ?? string.Empty;
            var model = _modelManager.GetModel(modelId);
            if (model is null)
            {
                RefreshSherpaModels(persistFallbackSelection: true);
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Localization.GetString("ChatTtsDeleteModelDialogTitle"),
                Content = string.Format(
                    CultureInfo.CurrentCulture,
                    Localization.GetString("ChatTtsDeleteModelDialogContentFormat"),
                    model.DisplayName),
                PrimaryButtonText = Localization.GetString("ChatTtsDeleteModel"),
                CloseButtonText = Localization.GetString("CommonCancel"),
                DefaultButton = ContentDialogButton.Close
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            SherpaModelInfoBar.IsOpen = false;
            SherpaDeleteButton.IsEnabled = false;
            SherpaModelCombo.IsEnabled = false;
            try
            {
                if (Application.Current is App app && app.ChatTtsService is not null)
                {
                    await app.ChatTtsService.SherpaProvider.DeleteModelAsync(model.Id);
                }
                else
                {
                    _modelManager.DeleteModel(model.Id);
                }

                ApplicationConfig.SaveSettings(ChatTtsConfig.SherpaModelIdKey, string.Empty);
                ApplicationConfig.SaveSettings(ChatTtsConfig.SherpaVoiceIdKey, string.Empty);
                RefreshSherpaModels(persistFallbackSelection: true);

                if (string.Equals(GetSelectedProviderId(), ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal))
                {
                    await LoadVoicesAsync(ChatTtsConfig.ProviderSherpaOnnx);
                }

                ShowModelInfo(
                    Localization.GetString("ChatTtsDeleteModelSuccess"),
                    model.DisplayName,
                    InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowModelInfo(
                    Localization.GetString("ChatTtsDeleteModelFailed"),
                    ex.Message,
                    InfoBarSeverity.Error);
            }
            finally
            {
                SherpaModelCombo.IsEnabled = true;
                SherpaDeleteButton.IsEnabled = !_modelManager.DownloadState.IsBusy && SherpaModelCombo.SelectedIndex >= 0;
            }
        }

        private async void SherpaOpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(SherpaTtsModelManager.RootDirectory);
                await Launcher.LaunchFolderAsync(folder);
            }
            catch (Exception ex)
            {
                ShowModelInfo(Localization.GetString("ChatTtsOpenModelFolderFailed"), ex.Message, InfoBarSeverity.Warning);
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

            // ValueChanged can fire while InitializeComponent is still creating the XAML tree.
            // The value TextBlock is declared after the Slider, so it can legitimately be null here.
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

            // Same initialization-order guard as the speaking-rate control above.
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
            if (!_loaded || double.IsNaN(args.NewValue))
            {
                return;
            }

            var capacity = Math.Clamp(
                (int)Math.Round(args.NewValue),
                ChatTtsConfig.MinQueueCapacity,
                ChatTtsConfig.MaxQueueCapacity);
            ApplicationConfig.SaveSettings(
                ChatTtsConfig.QueueCapacityKey,
                capacity.ToString(CultureInfo.InvariantCulture));
        }

        private void ChatTtsExpirationMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ignore XAML construction-time selection events. LoadSettings updates the UI once
            // the full page has been created.
            if (!_loaded)
            {
                return;
            }

            UpdateExpirationControls();
            if (ChatTtsExpirationMode.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            if (string.Equals(item.Tag?.ToString(), "Never", StringComparison.Ordinal))
            {
                ApplicationConfig.SaveSettings(ChatTtsConfig.MaxQueueAgeSecondsKey, "0");
                return;
            }

            SaveExpirationSeconds();
        }

        private void ChatTtsExpirationSeconds_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (!_loaded || double.IsNaN(args.NewValue) || !IsExpirationBySeconds())
            {
                return;
            }

            SaveExpirationSeconds();
        }

        private void SaveExpirationSeconds()
        {
            var rawValue = double.IsNaN(ChatTtsExpirationSeconds.Value)
                ? ChatTtsConfig.DefaultMaxQueueAgeSeconds
                : ChatTtsExpirationSeconds.Value;
            var seconds = Math.Clamp(
                (int)Math.Round(rawValue),
                ChatTtsConfig.MinMaxQueueAgeSeconds,
                ChatTtsConfig.MaxMaxQueueAgeSeconds);
            ApplicationConfig.SaveSettings(
                ChatTtsConfig.MaxQueueAgeSecondsKey,
                seconds.ToString(CultureInfo.InvariantCulture));
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
            if (!_loaded || sender is not ToggleSwitch toggle)
            {
                return;
            }

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
                ShowPreviewInfo(
                    Localization.GetString("ChatTtsPreviewUnavailable"),
                    Localization.GetString("ChatTtsPreviewUnavailableDescription"),
                    InfoBarSeverity.Warning);
                return;
            }

            ChatTtsPreviewButton.IsEnabled = false;
            try
            {
                await app.ChatTtsService.PreviewAsync(ChatTtsPreviewText.Text);
                ShowPreviewInfo(
                    Localization.GetString("ChatTtsPreviewSuccess"),
                    string.Empty,
                    InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowPreviewInfo(Localization.GetString("ChatTtsPreviewFailed"), ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                ChatTtsPreviewButton.IsEnabled = true;
            }
        }

        private void ShowModelInfo(string title, string message, InfoBarSeverity severity)
        {
            SherpaModelInfoBar.Title = title;
            SherpaModelInfoBar.Message = message;
            SherpaModelInfoBar.Severity = severity;
            SherpaModelInfoBar.IsOpen = true;
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
