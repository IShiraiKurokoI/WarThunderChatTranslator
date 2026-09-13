using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Pages;

namespace WarThunderChatTranslator;

public sealed partial class ShellPage : Page
{
    public static ShellPage Instance { get; private set; }
    public ShellPageService shellPageService { get; set; }

    public ShellPage()
    {
        this.InitializeComponent();
        Loaded += ShellPage_Loaded;
        Localization.CultureChanged += UpdateNavigationPaneWidth;
        shellPageService = new ShellPageService();
        shellFrame.Navigate(typeof(APIPage));
    }

    private void ShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateNavigationPaneWidth();
    }

    private void UpdateNavigationPaneWidth()
    {
        var navigationKeys = new[]
        {
            "NavApi",
            "NavNetwork",
            "NavAppearance",
            "NavFont",
            "NavTheme",
            "NavInteraction",
            "NavLayout",
            "NavUpdate",
            "NavAbout"
        };

        var widestText = 0.0;
        foreach (var key in navigationKeys)
        {
            var textBlock = new TextBlock
            {
                FontSize = 16,
                Text = Localization.GetString(key)
            };
            textBlock.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            widestText = Math.Max(widestText, textBlock.DesiredSize.Width);
        }

        const double iconAndIndentWidth = 104;
        const double comfortablePadding = 32;
        navigationView.OpenPaneLength = Math.Max(240, Math.Ceiling(widestText + iconAndIndentWidth + comfortablePadding));
    }

    private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string pageKey)
        {
            shellFrame.Navigate(shellPageService.GetPageType(pageKey));
        }
    }
}
