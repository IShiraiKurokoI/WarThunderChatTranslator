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
            _logger.Info("打开参数配置页面");
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LastUpdateCheckDate.Text = ApplicationConfig.GetSettings("LastUpdateCheckDate");
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
                var updateInfo = await UpdateHelper.CheckUpdateAsync("IShiraiKurokoI", "WarThunderChatTranslator");
                var sizeString = ConvertSizeToString(updateInfo.Assets[0].Size);

                if (updateInfo.IsExistNewVersion)
                {
                    dispatcherQueue.TryEnqueue(async () =>
                    {
                        await ShowUpdateDialogAsync(updateInfo.TagName, updateInfo.PublishedAt, sizeString, updateInfo.HtmlUrl.ToString());
                    });
                }
                else
                {
                    dispatcherQueue.TryEnqueue(() => ShowToast("您当前使用的是最新版本！"));
                }

                dispatcherQueue.TryEnqueue(() =>
                {
                    LastUpdateCheckDate.Text = DateTime.Now.ToString();
                    ApplicationConfig.SaveSettings("LastUpdateCheckDate", LastUpdateCheckDate.Text);
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                dispatcherQueue.TryEnqueue(() => ShowToast($"检查更新失败：{ex.Message}"));
            }
            finally
            {
                dispatcherQueue.TryEnqueue(() => Checking.Visibility = Visibility.Collapsed);
            }
        }

        private async Task ShowUpdateDialogAsync(string version, DateTimeOffset publishedAt, string sizeString, string updateUrl)
        {
            _logger.Info($"发现新版本{version}");
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "发现新版本！",
                PrimaryButtonText = "前往更新",
                CloseButtonText = "暂不更新",
                DefaultButton = ContentDialogButton.Primary,
                Content = $"检测到新版本：V{version}\n发布时间：{publishedAt}\n大小：{sizeString}"
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(updateUrl));
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

        private string ConvertSizeToString(int sizeInBytes)
        {
            const int GB = 1024 * 1024 * 1024;
            const int MB = 1024 * 1024;
            const int KB = 1024;

            return sizeInBytes switch
            {
                >= GB => $"{Math.Round(sizeInBytes / (double)GB, 2)} GB",
                >= MB => $"{Math.Round(sizeInBytes / (double)MB, 2)} MB",
                >= KB => $"{Math.Round(sizeInBytes / (double)KB, 2)} KB",
                _ => $"{sizeInBytes} Bytes"
            };
        }
    }
}
