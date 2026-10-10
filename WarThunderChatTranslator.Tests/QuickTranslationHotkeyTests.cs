using Microsoft.VisualStudio.TestTools.UnitTesting;
using WarThunderChatTranslator.Entities;

namespace WarThunderChatTranslator.Tests;

[TestClass]
public sealed class QuickTranslationHotkeyTests
{
    [TestMethod]
    public void Clone_CopiesValuesIntoIndependentInstance()
    {
        var source = new QuickTranslationHotkey
        {
            Id = 7,
            Enabled = true,
            Shortcut = "Ctrl+Alt+7",
            TargetLanguage = "ja"
        };

        var clone = source.Clone();
        clone.Shortcut = "Ctrl+Alt+8";

        Assert.AreNotSame(source, clone);
        Assert.AreEqual(7, clone.Id);
        Assert.IsTrue(clone.Enabled);
        Assert.AreEqual("ja", clone.TargetLanguage);
        Assert.AreEqual("Ctrl+Alt+7", source.Shortcut);
    }
}
