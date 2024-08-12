using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Pages;
using WinUICommunity;

namespace WarThunderChatTranslator;

public sealed partial class ShellPage : Page
{
    public static ShellPage Instance { get; private set; }
    public ShellPageService shellPageService { get; set; }

    public ShellPage()
    {
        this.InitializeComponent();
        shellPageService = new ShellPageService();
        shellPageService.SetDefaultPage(typeof(NetworkPage));
        INavigationViewServiceEx navigationViewService;
        INavigationServiceEx navigationService;
        navigationService = new NavigationServiceEx(shellPageService);
        navigationService.Frame = shellFrame;
        navigationViewService = new NavigationViewServiceEx(navigationService, shellPageService);
        navigationViewService.Initialize(navigationView);
    }
}
