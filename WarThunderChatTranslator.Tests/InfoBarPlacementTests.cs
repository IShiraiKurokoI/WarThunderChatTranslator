#nullable enable

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace WarThunderChatTranslator.Tests;

[TestClass]
[TestCategory("ModelFree")]
public sealed class InfoBarPlacementTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement ReadPage(string pageName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Pages", pageName);
        Assert.IsTrue(File.Exists(path), $"Page markup is missing from the test output: {path}");
        return XDocument.Load(path).Root!;
    }

    // A contextual InfoBar must follow the control that triggered its message,
    // rather than appear in a shared notification area at the top of the page.
    private static void AssertImmediatelyBelow(XElement page, string infoBarName, params string[] controlIdsOrHandlers)
    {
        var infoBar = page.Descendants().Single(element =>
            element.Name.LocalName == "InfoBar" &&
            (string?)element.Attribute(XamlNamespace + "Name") == infoBarName);
        var previous = infoBar.ElementsBeforeSelf().LastOrDefault();
        Assert.IsNotNull(previous, $"{infoBarName} must be placed below its setting.");

        foreach (var idOrHandler in controlIdsOrHandlers)
        {
            Assert.IsTrue(previous.DescendantsAndSelf().Any(element =>
                (string?)element.Attribute(XamlNamespace + "Name") == idOrHandler ||
                (string?)element.Attribute("Click") == idOrHandler),
                $"{infoBarName} is not immediately below {idOrHandler}.");
        }

        Assert.AreEqual("False", (string?)infoBar.Attribute("IsOpen"),
            $"{infoBarName} must start closed.");
        Assert.AreEqual("True", (string?)infoBar.Attribute("IsClosable"),
            $"{infoBarName} must be dismissible.");
    }

    [TestMethod]
    public void ChatVoiceWarningsAppearBelowTheRelatedControls()
    {
        var page = ReadPage("ChatTtsPage.xaml");
        Assert.AreEqual(5, page.Descendants().Count(element => element.Name.LocalName == "InfoBar"));
        AssertImmediatelyBelow(page, "ChatTtsOutputDeviceInfoBar", "ChatTtsOutputDevice");
        AssertImmediatelyBelow(page, "GptSoVitsConnectionInfoBar", "GptSoVitsStatusText");
        AssertImmediatelyBelow(page, "GptSoVitsProfilesInfoBar", "GptSoVitsAddProfileButton_Click");
        AssertImmediatelyBelow(page, "ChatTtsVoiceInfoBar", "ChatTtsVoice");
        AssertImmediatelyBelow(page, "ChatTtsPreviewInfoBar", "ChatTtsPreviewText");
    }

    [TestMethod]
    public void ContentFilterWarningsAppearBelowTheRelatedControls()
    {
        var page = ReadPage("ContentFilterPage.xaml");
        Assert.AreEqual(3, page.Descendants().Count(element => element.Name.LocalName == "InfoBar"));
        AssertImmediatelyBelow(page, "ContentFilterBlocklistInfoBar", "BlockTermTextBox");
        AssertImmediatelyBelow(page, "ContentFilterAllowlistInfoBar", "AllowTermTextBox");
        AssertImmediatelyBelow(page, "ContentFilterImportExportInfoBar",
            "ImportFilterPackage_Click", "ExportFilterPackage_Click");
    }
}
