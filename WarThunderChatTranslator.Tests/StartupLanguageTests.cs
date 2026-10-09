using Microsoft.VisualStudio.TestTools.UnitTesting;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Tests;

[TestClass]
public sealed class StartupLanguageTests
{
    [TestMethod]
    [DataRow("zh-CN", StartupLanguage.SimplifiedChinese)]
    [DataRow("zh-SG", StartupLanguage.SimplifiedChinese)]
    [DataRow("zh-MY", StartupLanguage.SimplifiedChinese)]
    [DataRow("zh", StartupLanguage.SimplifiedChinese)]
    [DataRow("zh-Hans", StartupLanguage.SimplifiedChinese)]
    [DataRow("zh-Hans-CN", StartupLanguage.SimplifiedChinese)]
    [DataRow("zh-Hans-SG", StartupLanguage.SimplifiedChinese)]
    [DataRow("zh-Hans-HK", StartupLanguage.SimplifiedChinese)]
    [DataRow("zh-CHS", StartupLanguage.SimplifiedChinese)]
    [DataRow(" ZH_cn ", StartupLanguage.SimplifiedChinese)]
    [DataRow("zh-TW", StartupLanguage.TraditionalChinese)]
    [DataRow("zh-HK", StartupLanguage.TraditionalChinese)]
    [DataRow("zh-MO", StartupLanguage.TraditionalChinese)]
    [DataRow("zh-Hant", StartupLanguage.TraditionalChinese)]
    [DataRow("zh-Hant-TW", StartupLanguage.TraditionalChinese)]
    [DataRow("zh-Hant-HK", StartupLanguage.TraditionalChinese)]
    [DataRow("zh-Hant-CN", StartupLanguage.TraditionalChinese)]
    [DataRow("zh-CHT", StartupLanguage.TraditionalChinese)]
    [DataRow("en-US", StartupLanguage.English)]
    [DataRow("en-GB", StartupLanguage.English)]
    [DataRow("en-AU", StartupLanguage.English)]
    [DataRow("en", StartupLanguage.English)]
    [DataRow("es-ES", StartupLanguage.English)]
    [DataRow("fr-FR", StartupLanguage.English)]
    [DataRow("de-DE", StartupLanguage.English)]
    [DataRow("ja-JP", StartupLanguage.English)]
    [DataRow("ko-KR", StartupLanguage.English)]
    [DataRow("zh-YUE", StartupLanguage.English)]
    [DataRow("", StartupLanguage.English)]
    [DataRow(null, StartupLanguage.English)]
    public void RoutesSystemLanguagesToThreeResources(string systemCulture, string expected)
    {
        Assert.AreEqual(expected, StartupLanguage.FromSystemCulture(systemCulture));
    }

    [TestMethod]
    [DataRow("zh-CN", true)]
    [DataRow("zh-Hans", true)]
    [DataRow("zh-TW", true)]
    [DataRow("zh-HK", true)]
    [DataRow("zh-Hant-MO", true)]
    [DataRow("en-GB", true)]
    [DataRow("en-US", true)]
    [DataRow("es-ES", false)]
    [DataRow("fr-FR", false)]
    [DataRow("ja-JP", false)]
    [DataRow("zh-YUE", false)]
    [DataRow("zh-TW-other", true)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void RecognizesSupportedSystemLanguageFamilies(string culture, bool supported)
    {
        Assert.AreEqual(supported, StartupLanguage.IsSupportedSystemCulture(culture));
    }

    [TestMethod]
    [DataRow(null, "es-ES", StartupLanguage.English)]
    [DataRow(null, "zh-CN", StartupLanguage.SimplifiedChinese)]
    [DataRow(null, "zh-HK", StartupLanguage.TraditionalChinese)]
    [DataRow(null, "zh-TW", StartupLanguage.TraditionalChinese)]
    [DataRow("", "es-ES", StartupLanguage.English)]
    [DataRow("zh-CN", "es-ES", StartupLanguage.SimplifiedChinese)]
    [DataRow("zh-TW", "es-ES", StartupLanguage.TraditionalChinese)]
    [DataRow("es-ES", "zh-CN", StartupLanguage.SimplifiedChinese)]
    [DataRow("en-US", "zh-CN", StartupLanguage.English)]
    [DataRow("garbled", "zh-HK", StartupLanguage.TraditionalChinese)]
    [DataRow("zh-TW", "en-US", StartupLanguage.TraditionalChinese)]
    public void SavedPreferenceIsRespectedOrFallsBackToSystem(
        string savedLanguage, string systemCulture, string expected)
    {
        Assert.AreEqual(expected, StartupLanguage.ResolveAppLanguage(savedLanguage, systemCulture));
    }

    [TestMethod]
    [DataRow(null, "es-ES", true)]
    [DataRow("", "ja-JP", true)]
    [DataRow(null, "en-GB", false)]
    [DataRow(null, "zh-HK", false)]
    [DataRow(null, "zh-SG", false)]
    [DataRow("zh-CN", "es-ES", false)]
    [DataRow("en-US", "es-ES", false)]
    [DataRow("es-ES", "es-ES", true)]
    public void WarningOnlyForUnsupportedAutomaticFallback(
        string savedLanguage, string systemCulture, bool needsNotice)
    {
        Assert.AreEqual(needsNotice, StartupLanguage.NeedsUnsupportedSystemLanguageNotice(
            savedLanguage, systemCulture));
    }
}
