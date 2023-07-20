// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel;
using WinUICommunity;
using System.Threading.Tasks;
using WarThunderChatTranslator.Helpers;
using System.Diagnostics;
using WarThunderChatTranslator.Configurations;
using System.Runtime.ConstrainedExecution;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.AppNotifications;
using NLog;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class UpdatePage : Page
    {
        public NLog.Logger logger;
        public string Version = string.Format("V{0}.{1}.{2}.{3}",
                        Package.Current.Id.Version.Major,
                        Package.Current.Id.Version.Minor,
                        Package.Current.Id.Version.Build,
                        Package.Current.Id.Version.Revision);
        public UpdatePage()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
            logger.Info("�򿪲�������ҳ��");
            this.InitializeComponent();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdate();
        }

        private void CheckUpdate()
        {
            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Task.Run(async () => {
                try
                {
                    var ver = await WarThunderChatTranslator.Helpers.UpdateHelper.CheckUpdateAsync("IShiraiKurokoI", "WarThunderChatTranslator");
                    string SizeString = "";
                    var dic = ByteConversionGBMBKB(ver.Assets[0].Size);
                    foreach (KeyValuePair<string, double> key in dic)
                    {
                        var filetype = key.Key;
                        var filesize = key.Value;
                        SizeString = filesize + filetype;
                    }

                    if (ver.IsExistNewVersion)
                    {
                        dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
                        {
                            logger.Info($"�����°汾{ver.TagName}");
                            ContentDialog dialog = new ContentDialog();
                            dialog.XamlRoot = this.XamlRoot;
                            dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
                            dialog.Title = "�����°汾��";
                            dialog.PrimaryButtonText = "ǰ������";
                            dialog.CloseButtonText = "�ݲ�����";
                            dialog.DefaultButton = ContentDialogButton.Primary;
                            dialog.Content = $"��⵽�°汾��V{ver.TagName}\n����ʱ�䣺{ver.PublishedAt}\n��С��{SizeString}";
                            var result = await dialog.ShowAsync();
                            if (result == ContentDialogResult.Primary)
                            {
                                await Windows.System.Launcher.LaunchUriAsync(new Uri(ver.HtmlUrl.ToString()));
                            }
                        });
                    }
                    else
                    {
                        var builder = new AppNotificationBuilder()
                            .AddText($"����ǰʹ�õ������°汾��");
                        var notificationManager = AppNotificationManager.Default;
                        notificationManager.Show(builder.BuildNotification());
                    }
                    dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                    {
                        LastUpdateCheckDate.Text = DateTime.Now.ToString();
                        ApplicationConfig.SaveSettings("LastUpdateCheckDate", LastUpdateCheckDate.Text);
                    });
                }
                catch (Exception e)
                {
                    logger.Error(e);
                    var builder = new AppNotificationBuilder()
                        .AddText($"������ʧ�ܣ�{e.Message}");
                    var notificationManager = AppNotificationManager.Default;
                    notificationManager.Show(builder.BuildNotification());
                }
            });
        }

        public Dictionary<string, double> ByteConversionGBMBKB(int KSize)
        {
            var dic = new Dictionary<string, double>();
            int GB = 1024 * 1024 * 1024;//����GB�ļ��㳣��
            int MB = 1024 * 1024;//����MB�ļ��㳣��
            int KB = 1024;//����KB�ļ��㳣��

            if (KSize / GB >= 1)//�����ǰByte��ֵ���ڵ���1GB
            {
                dic.Add("GB", Math.Round(KSize / (float)GB, 2)); //����ת����GB
            }
            else if (KSize / MB >= 1)//�����ǰByte��ֵ���ڵ���1MB
            {
                dic.Add("MB", Math.Round(KSize / (float)MB, 2)); //����ת����MB
            }
            else if (KSize / KB >= 1)//�����ǰByte��ֵ���ڵ���1KB
            {
                dic.Add("KB", Math.Round(KSize / (float)KB, 2)); //����ת����KB
            }
            else
            {
                dic.Add("Byte", KSize);  //��ʾByteֵ
            }
            return dic;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LastUpdateCheckDate.Text = ApplicationConfig.GetSettings("LastUpdateCheckDate");
        }
    }
}
