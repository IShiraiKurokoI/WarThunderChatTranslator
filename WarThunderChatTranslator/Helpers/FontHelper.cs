using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WarThunderChatTranslator.Helpers
{
    public class FontHelper
    {
        public static List<FontFamily> GetFontFamilies() 
        {
            List<FontFamily> result = new List<FontFamily>();
            var fonts = Microsoft.Graphics.Canvas.Text.CanvasTextFormat.GetSystemFontFamilies();
            foreach (string font in fonts)
            {
                result.Add(new FontFamily(font));
            }
            return result;
        }
    }
}
