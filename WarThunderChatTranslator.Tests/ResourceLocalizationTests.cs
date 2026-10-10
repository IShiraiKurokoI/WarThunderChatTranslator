using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WarThunderChatTranslator.Tests;

[TestClass]
public sealed class ResourceLocalizationTests
{
    private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "TestData");
    private static readonly Regex CompositeFormatPlaceholderRegex = new(
        @"(?<!\{)\{(\d+)(?:,[^}:]+)?(?:\:[^}]+)?\}(?!\})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [TestMethod]
    public void AllLanguageFiles_HaveTheSameResourceKeys()
    {
        var resources = LoadAll();
        var expected = resources["Resources.resx"].Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();

        foreach (var pair in resources)
        {
            var actual = pair.Value.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(
                expected,
                actual,
                $"{pair.Key} does not contain exactly the same resource keys as Resources.resx.");
        }
    }

    [TestMethod]
    public void NeutralResource_MatchesEnglishResourceExactly()
    {
        var neutral = Load("Resources.resx");
        var english = Load("Resources.en-US.resx");

        foreach (var pair in neutral)
        {
            Assert.IsTrue(english.TryGetValue(pair.Key, out var englishValue), $"Missing en-US resource: {pair.Key}");
            Assert.AreEqual(pair.Value, englishValue, $"Neutral/en-US resource mismatch for key: {pair.Key}");
        }
    }

    [TestMethod]
    public void LocalizedStrings_KeepTheSameCompositeFormatPlaceholders()
    {
        var resources = LoadAll();
        var neutral = resources["Resources.resx"];

        foreach (var pair in neutral)
        {
            var expected = GetPlaceholders(pair.Value);
            foreach (var localized in resources)
            {
                Assert.IsTrue(localized.Value.TryGetValue(pair.Key, out var localizedValue), $"{localized.Key} is missing {pair.Key}.");
                CollectionAssert.AreEqual(
                    expected,
                    GetPlaceholders(localizedValue ?? string.Empty),
                    $"Composite format placeholders differ for {pair.Key} in {localized.Key}.");
            }
        }
    }

    private static Dictionary<string, Dictionary<string, string>> LoadAll() => new(StringComparer.Ordinal)
    {
        ["Resources.resx"] = Load("Resources.resx"),
        ["Resources.en-US.resx"] = Load("Resources.en-US.resx"),
        ["Resources.zh-CN.resx"] = Load("Resources.zh-CN.resx"),
        ["Resources.zh-TW.resx"] = Load("Resources.zh-TW.resx")
    };

    private static Dictionary<string, string> Load(string fileName)
    {
        var path = Path.Combine(DataDirectory, fileName);
        Assert.IsTrue(File.Exists(path), $"Resource test data was not copied: {path}");

        var document = XDocument.Load(path);
        return document.Root!
            .Elements("data")
            .Where(x => x.Attribute("name") != null)
            .ToDictionary(
                x => x.Attribute("name")!.Value,
                x => x.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static string[] GetPlaceholders(string value) =>
        CompositeFormatPlaceholderRegex
            .Matches(value)
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
}
