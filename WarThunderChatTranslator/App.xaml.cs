// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using WarThunderChatTranslator.Configurations;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using WinUICommunity.Common.Helpers;
using static System.Net.Mime.MediaTypeNames;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Microsoft.UI.Xaml.Application
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            ThemeHelper.Initialize(m_window, BackdropType.MicaAlt);
            if (ApplicationConfig.GetSettings("Theme") != null)
            {
                ThemeHelper.ChangeTheme(GeneralHelper.GetEnum<ElementTheme>(ApplicationConfig.GetSettings("Theme")));
            }
            if (ApplicationConfig.GetSettings("BackgroundBrush") == null)
            {
                ApplicationConfig.SaveSettings("BackgroundBrush", "MicaAlt");
            }
            if (ApplicationConfig.GetSettings("BackgroundOpacity") == null)
            {
                ApplicationConfig.SaveSettings("BackgroundOpacity", "0.5");
            }
            if (ApplicationConfig.GetSettings("BackgroundLuminosityOpacity") == null)
            {
                ApplicationConfig.SaveSettings("BackgroundLuminosityOpacity", "0.5");
            }
            if (ApplicationConfig.GetSettings("NetworkProxyMode") == null)
            {
                ApplicationConfig.SaveSettings("NetworkProxyMode", "Default");
            }
            if (ApplicationConfig.GetSettings("ProxyAddress") == null)
            {
                ApplicationConfig.SaveSettings("ProxyAddress", "");
            }
            if (ApplicationConfig.GetSettings("ProxyPort") == null)
            {
                ApplicationConfig.SaveSettings("ProxyPort", "");
            }
            if (ApplicationConfig.GetSettings("ChatWidth") == null)
            {
                ApplicationConfig.SaveSettings("ChatWidth", "500");
            }
            if (ApplicationConfig.GetSettings("ChatHeight") == null)
            {
                ApplicationConfig.SaveSettings("ChatHeight", "300");
            }
            if (ApplicationConfig.GetSettings("ChatStartUpLoactionX") == null)
            {
                ApplicationConfig.SaveSettings("ChatStartUpLoactionX", "30");
            }
            if (ApplicationConfig.GetSettings("ChatStartUpLoactionY") == null)
            {
                ApplicationConfig.SaveSettings("ChatStartUpLoactionY", "350");
            }
            m_window.Activate();
        }

        private Window m_window;
    }
}
