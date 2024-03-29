﻿using Microsoft.UI.Xaml.Controls;
using System.Runtime.ConstrainedExecution;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;

namespace WarThunderChatTranslator.Pages;
public sealed partial class AboutPage : Page
{
    public string Version = string.Format("版本：{0}.{1}.{2}.{3}",
                    Package.Current.Id.Version.Major,
                    Package.Current.Id.Version.Minor,
                    Package.Current.Id.Version.Build,
                    Package.Current.Id.Version.Revision);
    public AboutPage()
    {
        this.InitializeComponent();
    }

    private void HyperlinkButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        DataPackage dataPackage = new DataPackage();
        dataPackage.RequestedOperation = DataPackageOperation.Copy;
        dataPackage.SetText("https://github.com/IShiraiKurokoI/WarThunderChatTranslator");
        Clipboard.SetContent(dataPackage);
        CopyTip.IsOpen = true;
    }

    private void SettingsCard_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Windows.System.Launcher.LaunchUriAsync(new System.Uri("https://github.com/IShiraiKurokoI/WarThunderChatTranslator"));
    }
}
