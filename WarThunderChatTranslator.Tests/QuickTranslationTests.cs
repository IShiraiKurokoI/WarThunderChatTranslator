using Microsoft.VisualStudio.TestTools.UnitTesting;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Tests
{
    [TestClass]
    public sealed class QuickTranslationTests
    {
        [TestMethod]
        public void HotkeyGesture_ParsesAndNormalizes_CommonShortcut()
        {
            var ok = HotkeyGesture.TryParse("control + alt + 1", out var gesture, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual("Ctrl+Alt+1", gesture.NormalizedText);
            Assert.AreEqual((uint)'1', gesture.VirtualKey);
            Assert.IsTrue((gesture.Modifiers & HotkeyGesture.ModControl) != 0);
            Assert.IsTrue((gesture.Modifiers & HotkeyGesture.ModAlt) != 0);
            Assert.IsTrue((gesture.Modifiers & HotkeyGesture.ModNoRepeat) != 0);
        }

        [TestMethod]
        public void HotkeyGesture_RejectsShortcutWithoutModifier()
        {
            var ok = HotkeyGesture.TryParse("F8", out _, out var error);

            Assert.IsFalse(ok);
            Assert.AreEqual("QuickTranslationHotkeyErrorModifierRequired", error);
        }

        [TestMethod]
        public void HotkeyGesture_CreatesCapturedShortcut_FromVirtualKey()
        {
            var ok = HotkeyGesture.TryCreateFromVirtualKey(
                HotkeyGesture.ModControl | HotkeyGesture.ModAlt,
                (uint)'1',
                out var gesture,
                out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual("Ctrl+Alt+1", gesture.NormalizedText);
            Assert.IsTrue((gesture.Modifiers & HotkeyGesture.ModNoRepeat) != 0);
        }

        [TestMethod]
        public void TtsCacheKey_IsStableAndChangesWithVoiceOrText()
        {
            var first = TtsCacheKey.Compute("翻译完成", "voice-a");
            var same = TtsCacheKey.Compute("翻译完成", "voice-a");
            var changedVoice = TtsCacheKey.Compute("翻译完成", "voice-b");
            var changedText = TtsCacheKey.Compute("Translation complete", "voice-a");
            var changedRate = TtsCacheKey.Compute("翻译完成", "voice-a", 1.5);

            Assert.AreEqual(first, same);
            Assert.AreEqual(64, first.Length);
            Assert.AreNotEqual(first, changedVoice);
            Assert.AreNotEqual(first, changedText);
            Assert.AreNotEqual(first, changedRate);
        }
    }
}
