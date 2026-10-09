using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Services;
using NLog;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class UpdatePage : Page
    {
        private readonly Logger _logger;
        public string Version { get; } = $"V{Package.Current.Id.Version.Major}.{Package.Current.Id.Version.Minor}.{Package.Current.Id.Version.Build}.{Package.Current.Id.Version.Revision}";

        public UpdatePage()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _logger.Info("Update page opened.");
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var lastUpdateCheckDate = ApplicationConfig.GetSettings(ApplicationConfig.LastUpdateCheckDateKey);
            LastUpdateCheckDate.Text = lastUpdateCheckDate is null or "Never" or "从未"
                ? Localization.GetString("UpdateNever")
                : lastUpdateCheckDate;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            CheckForUpdateAsync();
        }

        private async void CheckForUpdateAsync()
        {
            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            dispatcherQueue.TryEnqueue(() => Checking.Visibility = Visibility.Visible);


            try
            {
                var updates = await UpdateHelper.GetAvailableUpdatesAsync();

                if (updates.Count > 0)
                {
                    dispatcherQueue.TryEnqueue(async () =>
                    {
                        await ShowUpdateDialogAsync(updates);
                    });
                }
                else
                {
                    dispatcherQueue.TryEnqueue(() => ShowToast(Localization.GetString("UpdateLatest")));
                }

                dispatcherQueue.TryEnqueue(() =>
                {
                    LastUpdateCheckDate.Text = DateTime.Now.ToString("g");
                    ApplicationConfig.SaveSettings(ApplicationConfig.LastUpdateCheckDateKey, LastUpdateCheckDate.Text);
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to check for application updates.");
                dispatcherQueue.TryEnqueue(() => ShowToast($"{Localization.GetString("UpdateCheckFailed")}: {ex.Message}"));
            }
            finally
            {
                dispatcherQueue.TryEnqueue(() => Checking.Visibility = Visibility.Collapsed);
            }
        }

        private async Task ShowUpdateDialogAsync(IReadOnlyList<Windows.Services.Store.StorePackageUpdate> updates)
        {
            _logger.Info("An update is available in Microsoft Store.");
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = Localization.GetString("UpdateAvailable"),
                PrimaryButtonText = Localization.GetString("UpdateDownload"),
                CloseButtonText = Localization.GetString("UpdateLater"),
                DefaultButton = ContentDialogButton.Primary,
                Content = Localization.GetString("UpdateAvailableContent")
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await UpdateHelper.InstallUpdatesAsync(updates);
            }
        }

        private void ShowToast(string message)
        {
            ApplicationNotifications.Show(message);
        }
    }
}
