#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WarThunderChatTranslator.Tests;

[TestClass]
[TestCategory("ModelFree")]
public sealed class LocalizationCompletenessTests
{
    private static readonly string[] ExpectedWinUiLocales =
    [
        "en-US", "zh-Hans", "zh-Hant", "zh-Hans-CN", "zh-SG",
        "zh-HK", "zh-MO", "zh-MY", "zh-TW"
    ];

    private static readonly string[] ExpectedDesktopResources =
    [
        "Resources.resx", "Resources.en-US.resx", "Resources.zh-CN.resx", "Resources.zh-TW.resx"
    ];

    private static readonly Regex Placeholder = new(
        @"(?<!\{)\{(\d+)(?:,[^}:]+)?(?:\:[^}]+)?\}(?!\})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string[] ExtractPlaceholders(string text) => Placeholder.Matches(text)
        .Cast<Match>()
        .Select(match => match.Groups[1].Value)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(text => text, StringComparer.Ordinal)
        .ToArray();

    private static Dictionary<string, string> ReadXmlResource(string path)
    {
        Assert.IsTrue(File.Exists(path), "Resource file missing from test output: " + path);
        var root = XDocument.Load(path).Root!;
        var entries = root.Elements("data").ToArray();
        Assert.IsTrue(entries.Length > 0, "Localization file has no resources: " + path);
        Assert.AreEqual(entries.Length, entries.Select(e => (string?)e.Attribute("name"))
            .Distinct(StringComparer.Ordinal).Count(), "Duplicate resource keys in " + path);
        var values = entries.ToDictionary(e => (string)e.Attribute("name")!,
            e => e.Element("value")?.Value ?? "", StringComparer.Ordinal);
        foreach (var pair in values)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Value), $"Empty localized string {pair.Key} in {path}");
            Assert.IsFalse(pair.Value.Contains("\0", StringComparison.Ordinal), $"NUL in {pair.Key} ({path})");
        }
        return values;
    }

    [TestMethod]
    public void AllNineWinUiLocales_HaveIdenticalKeysAndValidNonEmptyTranslations()
    {
        var baseDirectory = Path.Combine(AppContext.BaseDirectory, "TestData", "Strings");
        var neutral = ReadXmlResource(Path.Combine(baseDirectory, "en-US", "Resources.resw"));
        var expected = neutral.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        foreach (var locale in ExpectedWinUiLocales)
        {
            var resource = ReadXmlResource(Path.Combine(baseDirectory, locale, "Resources.resw"));
            CollectionAssert.AreEqual(expected, resource.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                "Mismatching or missing WinUI strings in locale: " + locale);
        }
    }

    [TestMethod]
    public void WinUiLocales_HaveMatchingNumericInterpolationPlaceholders()
    {
        var baseDirectory = Path.Combine(AppContext.BaseDirectory, "TestData", "Strings");
        var reference = ReadXmlResource(Path.Combine(baseDirectory, "en-US", "Resources.resw"));
        foreach (var locale in ExpectedWinUiLocales)
        {
            var actual = ReadXmlResource(Path.Combine(baseDirectory, locale, "Resources.resw"));
            foreach (var pair in reference)
            {
                CollectionAssert.AreEqual(ExtractPlaceholders(pair.Value), ExtractPlaceholders(actual[pair.Key]),
                    $"Broken format arguments in WinUI locale {locale}, key {pair.Key}");
            }
        }
    }

    [TestMethod]
    public void AllDesktopResxLocales_HaveNoMissingKeysOrBlankTranslations()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData");
        var reference = ReadXmlResource(Path.Combine(root, "Resources.resx"));
        var expected = reference.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        foreach (var file in ExpectedDesktopResources)
        {
            var actual = ReadXmlResource(Path.Combine(root, file));
            CollectionAssert.AreEqual(expected, actual.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                "Missing or extra desktop resources in " + file);
            foreach (var pair in reference)
            {
                CollectionAssert.AreEqual(ExtractPlaceholders(pair.Value), ExtractPlaceholders(actual[pair.Key]),
                    $"Broken format placeholders in desktop locale {file}, key {pair.Key}");
            }
        }
    }
}
