using System;
using System.Collections.Generic;
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
        Instance = this;
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
            "NavAiTranslation",
            "NavQuickVoiceTranslation",
            "NavChatTts",
            "NavNetwork",
            "NavRunSettings",
            "NavAppearance",
            "NavTheme",
            "NavFont",
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


    public void NavigateTo(string pageKey)
    {
        if (string.IsNullOrWhiteSpace(pageKey))
        {
            return;
        }

        Type pageType;
        try
        {
            pageType = shellPageService.GetPageType(pageKey);
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        if (shellFrame.CurrentSourcePageType != pageType)
        {
            shellFrame.Navigate(pageType);
        }

        var item = FindNavigationItem(navigationView.MenuItems, pageKey)
            ?? FindNavigationItem(navigationView.FooterMenuItems, pageKey);
        if (item != null)
        {
            navigationView.SelectedItem = item;
        }
    }

    private static NavigationViewItem FindNavigationItem(System.Collections.Generic.IEnumerable<object> items, string pageKey)
    {
        foreach (var itemObject in items)
        {
            if (itemObject is not NavigationViewItem item)
            {
                continue;
            }

            if (string.Equals(item.Tag?.ToString(), pageKey, StringComparison.Ordinal))
            {
                return item;
            }

            var child = FindNavigationItem(item.MenuItems, pageKey);
            if (child != null)
            {
                return child;
            }
        }

        return null;
    }

    private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string pageKey)
        {
            shellFrame.Navigate(shellPageService.GetPageType(pageKey));
        }
    }
}
