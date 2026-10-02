using GTranslate.Results;
using GTranslate.Translators;
using NLog;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Services;
using WarThunderChatTranslator.Translators;

namespace WarThunderChatTranslator.Helpers
{
    public static class TranslationHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static ITranslator translator;
        private static HttpClient client;

        public static void init()
        {
            UpdateHttpClient();
        }

        public static ITranslator getCurrentTranslator()
        {
            if (translator == null)
            {
                UpdateHttpClient();
            }

            return translator;
        }

        public static void UpdateHttpClient()
        {
            var networkProxyMode = ApplicationConfig.GetSettings(ApplicationConfig.NetworkProxyModeKey);
            var proxyAddress = ApplicationConfig.GetSettings(ApplicationConfig.ProxyAddressKey);
            var proxyAccount = ApplicationConfig.GetSettings(ApplicationConfig.ProxyAccountKey);
            var proxyPassword = ApplicationConfig.GetSettings(ApplicationConfig.ProxyPasswordKey);

            switch (networkProxyMode)
            {
                case "System":
                    client = new HttpClient(new HttpClientHandler
                    {
                        UseProxy = true
                    });
                    Logger.Info("Proxy configuration updated: using the system proxy.");
                    break;
                case "Custom":
                    try
                    {
                        WebProxy webProxy;
                        if (string.IsNullOrEmpty(proxyAccount) || string.IsNullOrEmpty(proxyPassword))
                        {
                            webProxy = new WebProxy(proxyAddress);
                            Logger.Info($"Proxy configuration updated: using custom proxy {proxyAddress} without credentials.");
                        }
                        else
                        {
                            webProxy = new WebProxy(proxyAddress)
                            {
                                Credentials = new NetworkCredential(proxyAccount, proxyPassword)
                            };
                            Logger.Info($"Proxy configuration updated: using custom proxy {proxyAddress} with account {proxyAccount}.");
                        }

                        client = new HttpClient(new HttpClientHandler
                        {
                            Proxy = webProxy,
                            UseProxy = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "The custom proxy configuration is invalid. Falling back to the system proxy.");
                        client = new HttpClient(new HttpClientHandler
                        {
                            UseProxy = true
                        });
                    }
                    break;
                case "Default":
                default:
                    client = new HttpClient(new HttpClientHandler
                    {
                        Proxy = null,
                        UseProxy = false
                    });
                    Logger.Info("Proxy configuration updated: proxy disabled.");
                    break;
            }

            UpdateTranslator();
        }

        public static void UpdateTranslator()
        {
            client ??= new HttpClient();
            var selectedApi = ApplicationConfig.GetSettings(ApplicationConfig.TranslateApiKey) ?? "Microsoft";
            Logger.Info($"Selected translator: {selectedApi}.");

            translator = selectedApi switch
            {
                "Yandex" => new YandexTranslator(client),
                "Google" => new GoogleTranslator2(client),
                "AI" => new AiTranslator(client, AiProviderStore.GetSelectedProvider()),
                _ => new MicrosoftTranslator(client)
            };
        }

        public static async Task<ITranslationResult> TranslateAsync(string text)
        {
            return await TranslateAsync(
                text,
                ApplicationConfig.GetSettings(ApplicationConfig.TargetLanguageKey) ?? "zh-CN");
        }

        public static async Task<ITranslationResult> TranslateAsync(string text, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Text cannot be empty.", nameof(text));
            }

            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                throw new ArgumentException("Target language cannot be empty.", nameof(targetLanguage));
            }

            var currentTranslator = getCurrentTranslator();
            return await currentTranslator.TranslateAsync(text, targetLanguage);
        }

        public static async Task<ITranslationResult> TestAiProviderAsync(AiProviderConfig provider, string text)
        {
            client ??= new HttpClient();
            var aiTranslator = new AiTranslator(client, provider);
            return await aiTranslator.TranslateAsync(
                text,
                ApplicationConfig.GetSettings(ApplicationConfig.TargetLanguageKey) ?? "zh-CN");
        }
    }
}
