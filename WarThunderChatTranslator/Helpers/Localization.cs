using System;
using System.Globalization;
using System.Resources;
using System.Runtime.InteropServices;
using System.Threading;
using WarThunderChatTranslator.Configurations;

namespace WarThunderChatTranslator.Helpers
{
    public static class Localization
    {
        private const uint MuiLanguageName = 0x00000008;
        private static readonly ResourceManager ResourceManager = new("WarThunderChatTranslator.Properties.Resources", typeof(Localization).Assembly);

        public static string CurrentLanguage { get; private set; } = "en-US";
        public static event Action CultureChanged = delegate { };

        public static void Initialize(string savedLanguage)
        {
            Apply(string.IsNullOrWhiteSpace(savedLanguage) ? GetSystemLanguage() : savedLanguage);
        }

        public static void Apply(string language, bool updateWindowsPreference = true)
        {
            var selectedLanguage = language == "zh-CN" ? "zh-CN" : "en-US";
            var languageChanged = CurrentLanguage != selectedLanguage;
            CurrentLanguage = selectedLanguage;
            var culture = CultureInfo.GetCultureInfo(CurrentLanguage);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            ApplicationConfig.SaveSettings("ApplicationLanguage", CurrentLanguage);

            if (updateWindowsPreference)
            {
                SetProcessPreferredUILanguages(MuiLanguageName, CurrentLanguage + "\0\0", out _);
            }

            if (languageChanged)
            {
                CultureChanged();
            }
        }

        public static string GetString(string key)
        {
            return ResourceManager.GetString(key, CultureInfo.GetCultureInfo(CurrentLanguage)) ?? key;
        }

        private static string GetSystemLanguage()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
                ? "zh-CN"
                : "en-US";
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessPreferredUILanguages(
            uint dwFlags,
            string pwszLanguagesBuffer,
            out uint pulNumLanguages);
    }
}