using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Pages;
using WinUICommunity;

namespace WarThunderChatTranslator;

public sealed partial class ShellPage : Page
{
    public static ShellPage Instance { get; private set; }
    public NavigationManager navigationManager { get; set; }

    public ShellPage()
    {
        this.InitializeComponent();
        navigationManager = new NavigationManager(navigationView, new NavigationViewOptions
        {
            DefaultPage = typeof(NetworkPage)
        }, shellFrame);
    }
}
