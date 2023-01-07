// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Controls;
using WinUICommunity.Common.ViewModel;
using WarThunderChatTranslator.Pages;
using Microsoft.UI.Xaml;
using WinUICommunity.Common.Helpers;
using WinUICommunity.Common.Tools;
using System.Runtime.InteropServices;
using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    /// 

    public sealed partial class MainWindow : Window
    {
        public string TitleText = "Õ½ÕùÀ×öªÁÄÌì·­ÒëÆ÷";
        public Grid ApplicationTitleBar => AppTitleBar;
        internal static MainWindow Instance { get; private set; }
        public MainWindow()
        {
            this.InitializeComponent();
            Instance = this;
            TitleBarHelper.Initialize(this, TitleTextBlock, AppTitleBar, LeftPaddingColumn, IconColumn, TitleColumn, LeftDragColumn, SearchColumn, RightDragColumn, RightPaddingColumn);
            Localizer.Get().InitializeWindow(Root, Content);

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon("Assets/favicon.ico");
            appWindow.Resize(new Windows.Graphics.SizeInt32(1400, 800));
        }
    }
}
