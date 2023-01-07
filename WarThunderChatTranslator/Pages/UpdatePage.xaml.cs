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
using WinUICommunity.Common.Helpers;
using System.Threading.Tasks;
using WarThunderChatTranslator.Helpers;
using System.Diagnostics;
using WarThunderChatTranslator.Configurations;
using System.Runtime.ConstrainedExecution;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class UpdatePage : Page
    {
        public string Version = string.Format("V{0}.{1}.{2}.{3}",
                        Package.Current.Id.Version.Major,
                        Package.Current.Id.Version.Minor,
                        Package.Current.Id.Version.Build,
                        Package.Current.Id.Version.Revision);
        public UpdatePage()
        {
            this.InitializeComponent();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            await CheckUpdate();
        }

        private async Task CheckUpdate()
        {
            try
            {
                Checking.Visibility = Visibility.Visible;
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
                    InfoBar.Title = $"��⵽�°汾��V{ver.TagName}  ������{ver.PublishedAt}  ��С{SizeString}";
                    InfoBar.Severity = InfoBarSeverity.Informational;
                    Windows.System.Launcher.LaunchUriAsync(new Uri(ver.Assets[0].Url));
                    VersionPage.NavigateUri = new Uri(ver.HtmlUrl.ToString());
                    VersionPage.Visibility= Visibility.Visible;
                }
                else
                {
                    VersionPage.Visibility = Visibility.Collapsed;
                }

                Checking.Visibility = Visibility.Collapsed;
                LastUpdateCheckDate.Text = DateTime.Now.ToString();
                ApplicationConfig.SaveSettings("LastUpdateCheckDate", LastUpdateCheckDate.Text);
            }
            catch (Exception e)
            {
                InfoBar.Title = $"������ʧ�ܣ�{e.Message}";
                InfoBar.Severity = InfoBarSeverity.Error;
                Checking.Visibility = Visibility.Collapsed;
            }
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
