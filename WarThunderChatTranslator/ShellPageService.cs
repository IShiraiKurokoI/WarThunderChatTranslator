using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WarThunderChatTranslator.Pages;

namespace WarThunderChatTranslator
{
    public class ShellPageService
    {
        private readonly Dictionary<string, Type> _pageKeyToTypeMap;

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

        public Type GetPageType(string pageKey) => _pageKeyToTypeMap[pageKey];
    }
}
