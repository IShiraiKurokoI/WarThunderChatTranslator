using System;
using System.Diagnostics;
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
        private static readonly ResourceManager ResourceManager =
            new("WarThunderChatTranslator.Properties.Resources", typeof(Localization).Assembly);

        // Read the Windows user's preferred display language, rather than
        // a culture changed by a previous ApplicationLanguages.PrimaryLanguageOverride.
        public static string OriginalSystemLanguage { get; private set; } = ReadSystemLanguage();
        public static string SystemLanguage { get; private set; } =
            StartupLanguage.FromSystemCulture(OriginalSystemLanguage);
        public static bool UsedUnsupportedSystemLanguageFallback { get; private set; }
        public static string CurrentLanguage { get; private set; } = StartupLanguage.English;
        public static event Action CultureChanged = delegate { };

        public static void Initialize(string savedLanguage)
        {
            OriginalSystemLanguage = ReadSystemLanguage();
            SystemLanguage = StartupLanguage.FromSystemCulture(OriginalSystemLanguage);
            UsedUnsupportedSystemLanguageFallback = StartupLanguage.NeedsUnsupportedSystemLanguageNotice(
                savedLanguage, OriginalSystemLanguage);
            // A system-derived default is not a manual selection and must not be persisted.
            // This lets newly installed apps follow a future Windows language change.
            Apply(StartupLanguage.ResolveAppLanguage(savedLanguage, OriginalSystemLanguage),
                savePreference: false);
        }

        public static void Apply(string language, bool updateWindowsPreference = true,
            bool savePreference = true)
        {
            var selectedLanguage = StartupLanguage.FromSystemCulture(language);
            var changed = CurrentLanguage != selectedLanguage;
            CurrentLanguage = selectedLanguage;
            var culture = CultureInfo.GetCultureInfo(selectedLanguage);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            // A damaged settings store, missing MUI support, or unavailable Windows resource
            // should not prevent the process from creating its tray icon.
            if (savePreference)
            {
                try
                {
                    ApplicationConfig.SaveSettings(ApplicationConfig.ApplicationLanguageKey, selectedLanguage);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Could not persist UI language: {ex}");
                }
            }

            if (updateWindowsPreference)
            {
                try
                {
                    // Keep MRT/PRI XAML resources in sync with the managed .resx resources.
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = selectedLanguage;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Could not override app resource language: {ex}");
                }

                try
                {
                    if (!SetProcessPreferredUILanguages(MuiLanguageName, selectedLanguage + "\0\0", out _))
                    {
                        Trace.TraceWarning("Windows did not accept the preferred UI language.");
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Could not set Windows preferred UI language: {ex}");
                }
            }

            if (changed)
            {
                foreach (Action subscriber in CultureChanged.GetInvocationList())
                {
                    try
                    {
                        subscriber();
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"A localization listener failed: {ex}");
                    }
                }
            }
        }

        public static string GetString(string key)
        {
            return GetStringForLanguage(key, CurrentLanguage);
        }

        public static string GetSystemString(string key)
        {
            return GetStringForLanguage(key, SystemLanguage);
        }

        public static string GetStringForLanguage(string key, string language)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            try
            {
                // .resx has an English neutral resource and zh-CN / zh-TW satellites.
                return ResourceManager.GetString(key, CultureInfo.GetCultureInfo(
                    StartupLanguage.FromSystemCulture(language))) ?? key;
            }
            catch (MissingManifestResourceException ex)
            {
                Trace.TraceWarning($"Missing localization resources: {ex}");
                return key;
            }
            catch (MissingSatelliteAssemblyException ex)
            {
                Trace.TraceWarning($"Missing localized satellite assembly: {ex}");
                return key;
            }
        }

        private static string ReadSystemLanguage()
        {
            try
            {
                // The first Windows display language is the correct source, even for
                // Language Interface Packs which don't have a conventional LANGID.
                uint languageCount = 0;
                uint characterCount = 0;
                GetUserPreferredUILanguages(MuiLanguageName, ref languageCount,
                    IntPtr.Zero, ref characterCount);
                if (characterCount > 2 && characterCount <= 4096)
                {
                    var buffer = Marshal.AllocHGlobal(checked((int)characterCount * sizeof(char)));
                    try
                    {
                        if (GetUserPreferredUILanguages(MuiLanguageName, ref languageCount,
                            buffer, ref characterCount))
                        {
                            var language = Marshal.PtrToStringUni(buffer);
                            if (!string.IsNullOrWhiteSpace(language))
                            {
                                return language;
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }

                // Fallback for older or restricted Windows environments.
                var languageId = GetUserDefaultUILanguage();
                if (languageId != 0)
                {
                    return CultureInfo.GetCultureInfo(languageId).Name;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Could not read the Windows UI language: {ex}");
            }

            return CultureInfo.CurrentUICulture.Name;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserPreferredUILanguages(uint flags,
            ref uint languageCount, IntPtr languages, ref uint bufferCharacterCount);

        [DllImport("kernel32.dll")]
        private static extern ushort GetUserDefaultUILanguage();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessPreferredUILanguages(
            uint flags, string languages, out uint languageCount);
    }
}
