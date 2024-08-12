using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WarThunderChatTranslator.Pages;
using WinUICommunity;

namespace WarThunderChatTranslator
{
    public class ShellPageService : PageServiceEx
    {
        public ShellPageService()
        {
            _pageKeyToTypeMap = new Dictionary<string, Type>
            {
                { "NetworkPage", typeof(NetworkPage) },
                { "FontPage", typeof(FontPage) },
                { "ThemePage", typeof(ThemePage) },
                { "LocationPage", typeof(LocationPage) },
                { "InteractPage", typeof(InteractPage) },
                { "APIPage", typeof(APIPage) },
                { "UpdatePage", typeof(UpdatePage) },
                { "AboutPage", typeof(AboutPage) },
            };
        }
    }
}
