using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Pages;

namespace WarThunderChatTranslator;

public sealed partial class ShellPage : Page
{
    public static ShellPage Instance { get; private set; }
    public ShellPageService shellPageService { get; set; }

    public ShellPage()
    {
        this.InitializeComponent();
        shellPageService = new ShellPageService();
        shellFrame.Navigate(typeof(APIPage));
    }

    private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string pageKey)
        {
            shellFrame.Navigate(shellPageService.GetPageType(pageKey));
        }
    }
}
