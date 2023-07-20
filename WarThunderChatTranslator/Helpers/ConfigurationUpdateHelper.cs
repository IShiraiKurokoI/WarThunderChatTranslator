using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WarThunderChatTranslator.Helpers
{
    public static class ConfigurationUpdateHelper
    {
        public static event RoutedEventHandler CallChatUpdate;

        public static MainWindow MainWindow { get;  set; }

        internal static void CallUpdate(object sender, RoutedEventArgs e)
        {
            if (CallChatUpdate != null)
            {
                CallChatUpdate(sender, e);
            }
        }
        public static void CallClose(object sender, RoutedEventArgs e)
        {
            if (MainWindow != null)
            {
                MainWindow.Close();
            }
        }
    }
}
