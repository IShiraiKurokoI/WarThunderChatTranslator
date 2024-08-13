using GTranslate.Results;
using GTranslate.Translators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Pages;

namespace WarThunderChatTranslator.Helpers
{
    public class TranslationHelper
    {
        static AggregateTranslator translator;
        TranslationHelper() { 
            UpdateTranslator();
        }

        public static AggregateTranslator getCurrentTranslator()
        {
            if (translator == null)
            {
                UpdateTranslator();
            }
            return translator;
        }

        public static void UpdateTranslator()
        {
            switch (ApplicationConfig.GetSettings("TranslateAPI"))
            {
                case "Microsoft":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new MicrosoftTranslator() });
                        break;
                    }
                case "Yandex":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new YandexTranslator() });
                        break;
                    }
                case "Bing":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new BingTranslator() });
                        break;
                    }
                case "Google":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new GoogleTranslator2() });
                        break;
                    }
            }
        }

        public static async Task<ITranslationResult> TranslateAsync(string text)
        {
            return await translator.TranslateAsync(text, ApplicationConfig.GetSettings("TargetLanguage"));
        }
    }
}
