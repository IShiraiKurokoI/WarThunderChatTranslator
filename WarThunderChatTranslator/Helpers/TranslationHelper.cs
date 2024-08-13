using GTranslate.Results;
using GTranslate.Translators;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Pages;

namespace WarThunderChatTranslator.Helpers
{
    public static class TranslationHelper
    {
        static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        static AggregateTranslator translator;
        static HttpClient client;
        public static void init() {
            UpdateHttpClient();
        }

        public static AggregateTranslator getCurrentTranslator()
        {
            if (translator == null)
            {
                UpdateHttpClient();
            }
            return translator;
        }

        public static void UpdateHttpClient()
        {
            string networkProxyMode = ApplicationConfig.GetSettings("NetworkProxyMode");
            string proxyAddress = ApplicationConfig.GetSettings("ProxyAddress");
            string proxyAccount = ApplicationConfig.GetSettings("ProxyAccount");
            string proxyPassword = ApplicationConfig.GetSettings("ProxyPassword");

            switch (networkProxyMode)
            {
                case "System":
                    client = new HttpClient(new HttpClientHandler
                    {
                        UseProxy = true
                    });
                    logger.Info("更新代理配置，使用系统代理");
                    break;
                case "Custom":
                    try
                    {
                        WebProxy webProxy;
                        if (String.IsNullOrEmpty(proxyAccount) || String.IsNullOrEmpty(proxyPassword))
                        {
                            webProxy = new WebProxy(proxyAddress);
                            logger.Info($"更新代理配置，使用自定义无密码代理 {proxyAddress}");
                        }
                        else
                        {
                            webProxy = new WebProxy(proxyAddress)
                            {
                                Credentials = new NetworkCredential(proxyAccount, proxyPassword)
                            };
                            logger.Info($"更新代理配置，使用自定义代理 {proxyAddress} || {proxyAccount} || {proxyPassword}");
                        }
                        client = new HttpClient(new HttpClientHandler
                        {
                            Proxy = webProxy,
                            UseProxy = true
                        });
                    }
                    catch (Exception)
                    {
                        logger.Info($"用户选择了自定义代理，但配置无效，改为使用系统代理");
                        client = new HttpClient(new HttpClientHandler
                        {
                            UseProxy = true
                        });
                        return;
                    }
                    break;
                case "Default":
                default:
                    client = new HttpClient(new HttpClientHandler
                    {
                        Proxy = null,
                        UseProxy = false
                    });
                    logger.Info("更新代理配置，不使用代理");
                    break;
            }

            UpdateTranslator();
        }

        public static void UpdateTranslator()
        {
            logger.Info($"选择使用{ApplicationConfig.GetSettings("TranslateAPI")}翻译器");
            switch (ApplicationConfig.GetSettings("TranslateAPI"))
            {
                case "Microsoft":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new MicrosoftTranslator(client) });
                        break;
                    }
                case "Yandex":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new YandexTranslator(client) });
                        break;
                    }
                case "Bing":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new BingTranslator(client) });
                        break;
                    }
                case "Google":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new GoogleTranslator2(client) });
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
