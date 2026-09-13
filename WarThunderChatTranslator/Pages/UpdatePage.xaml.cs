using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Helpers;
using Windows.UI.Notifications;
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
            _logger.Info("�򿪲�������ҳ��");
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var lastUpdateCheckDate = ApplicationConfig.GetSettings("LastUpdateCheckDate");
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
                    ApplicationConfig.SaveSettings("LastUpdateCheckDate", LastUpdateCheckDate.Text);
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                dispatcherQueue.TryEnqueue(() => ShowToast($"{Localization.GetString("UpdateCheckFailed")}: {ex.Message}"));
            }
            finally
            {
                dispatcherQueue.TryEnqueue(() => Checking.Visibility = Visibility.Collapsed);
            }
        }

        private async Task ShowUpdateDialogAsync(IReadOnlyList<Windows.Services.Store.StorePackageUpdate> updates)
        {
            _logger.Info("Microsoft Store 中有可用更新");
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
            var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText01);
            var stringElements = toastXml.GetElementsByTagName("text");
            stringElements[0].AppendChild(toastXml.CreateTextNode(message));
            var toast = new ToastNotification(toastXml);
            ToastNotificationManager.CreateToastNotifier("WarThunderChatTranslator").Show(toast);
        }
    }
}
