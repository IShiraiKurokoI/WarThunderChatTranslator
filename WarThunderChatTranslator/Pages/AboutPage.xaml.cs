using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class AboutPage : Page
    {
        public string Version { get; } = $"{Package.Current.Id.Version.Major}.{Package.Current.Id.Version.Minor}.{Package.Current.Id.Version.Build}.{Package.Current.Id.Version.Revision}";

        public AboutPage()
        {
            InitializeComponent();
        }

        private void HyperlinkButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var dataPackage = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy
            };
            dataPackage.SetText("https://github.com/IShiraiKurokoI/WarThunderChatTranslator");
            Clipboard.SetContent(dataPackage);
            CopyTip.IsOpen = true;
        }

        private void SettingsCard_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new System.Uri("https://github.com/IShiraiKurokoI/WarThunderChatTranslator"));
        }
    }
}