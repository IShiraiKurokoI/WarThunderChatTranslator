#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Services;
using WarThunderChatTranslator.Tests.TestSupport;

namespace WarThunderChatTranslator.Tests;

[TestClass]
[TestCategory("ModelFree")]
[DoNotParallelize]
public sealed class GptSoVitsVoiceProfileTests
{
    [TestMethod]
    public void NewStore_StartsEmptyAndDoesNotCreateFilesUntilFirstWrite()
    {
        using var env = new IsolatedTestEnvironment();
        var store = new GptSoVitsProfileStore(env.Subdirectory("profiles"));
        Assert.AreEqual(0, store.GetProfiles().Count);
        Assert.IsFalse(File.Exists(store.StorageFilePath));
        Assert.IsNull(store.GetById("missing"));
        Assert.IsNull(store.GetById(null));
        Assert.IsFalse(store.Delete("missing"));
        Assert.IsFalse(store.Delete(null));
    }

    [TestMethod]
    public void Upsert_NormalizesPathsLanguagesAndNamesAndGeneratesAnId()
    {
        using var env = new IsolatedTestEnvironment();
        var store = new GptSoVitsProfileStore(env.Subdirectory("profiles"));
        store.Upsert(new GptSoVitsVoiceProfile
        {
            Id = " ", Name = "  Captain  ", GptWeightsPath = " gpt.ckpt ",
            SoVitsWeightsPath = " sovits.pth ", ReferenceAudioPath = " audio.wav ",
            PromptText = " test ", PromptLanguage = "INVALID", TextLanguage = "INVALID"
        });
        var p = store.GetProfiles().Single();
        Assert.AreEqual(32, p.Id.Length);
        Assert.AreEqual("Captain", p.Name);
        Assert.AreEqual("gpt.ckpt", p.GptWeightsPath);
        Assert.AreEqual("sovits.pth", p.SoVitsWeightsPath);
        Assert.AreEqual("audio.wav", p.ReferenceAudioPath);
        Assert.AreEqual("test", p.PromptText);
        Assert.AreEqual("zh", p.PromptLanguage);
        Assert.AreEqual("auto", p.TextLanguage);
        Assert.IsTrue(File.Exists(store.StorageFilePath));
    }

    [TestMethod]
    public void Store_ReadsAndWritesAllFieldsAcrossFreshInstances()
    {
        using var env = new IsolatedTestEnvironment();
        var directory = env.Subdirectory("profiles");
        var store = new GptSoVitsProfileStore(directory);
        var p = new GptSoVitsVoiceProfile
        {
            Id = "stable-id", Name = "Pilot", GptWeightsPath = "foo.ckpt",
            SoVitsWeightsPath = "foo.pth", ReferenceAudioPath = "foo.wav",
            PromptText = "Pilot voice", PromptLanguage = "ja", TextLanguage = "auto_yue"
        };
        store.Upsert(p);
        var reloaded = new GptSoVitsProfileStore(directory);
        var loaded = reloaded.GetById(p.Id)!;
        Assert.IsNotNull(loaded);
        Assert.AreEqual(p.Name, loaded.Name);
        Assert.AreEqual(p.GptWeightsPath, loaded.GptWeightsPath);
        Assert.AreEqual(p.SoVitsWeightsPath, loaded.SoVitsWeightsPath);
        Assert.AreEqual(p.ReferenceAudioPath, loaded.ReferenceAudioPath);
        Assert.AreEqual(p.PromptText, loaded.PromptText);
        Assert.AreEqual(p.PromptLanguage, loaded.PromptLanguage);
        Assert.AreEqual(p.TextLanguage, loaded.TextLanguage);
        Assert.IsFalse(File.Exists(store.StorageFilePath + ".tmp"));
        using var document = JsonDocument.Parse(File.ReadAllText(store.StorageFilePath));
        Assert.AreEqual("stable-id", document.RootElement[0].GetProperty("Id").GetString());
    }

    [TestMethod]
    public void UpdateBySameId_DoesNotDuplicateAndPersistsReplacement()
    {
        using var env = new IsolatedTestEnvironment();
        var store = new GptSoVitsProfileStore(env.Subdirectory("profiles"));
        store.Upsert(new() { Id = "same", Name = "Old" });
        store.Upsert(new() { Id = "same", Name = "New", PromptLanguage = "ko" });
        Assert.AreEqual(1, store.GetProfiles().Count);
        Assert.AreEqual("New", new GptSoVitsProfileStore(store.StorageDirectory).GetById("same")!.Name);
        Assert.AreEqual("ko", store.GetById("same")!.PromptLanguage);
    }

    [TestMethod]
    public void ProfilesAreClonedOnInputAndOutput()
    {
        using var env = new IsolatedTestEnvironment();
        var store = new GptSoVitsProfileStore(env.Subdirectory("profiles"));
        var input = new GptSoVitsVoiceProfile { Id = "clone", Name = "Original" };
        store.Upsert(input);
        input.Name = "Modified externally";
        var output = store.GetById("clone")!;
        output.Name = "Changed output";
        store.GetProfiles()[0].Name = "Changed list";
        Assert.AreEqual("Original", store.GetById("clone")!.Name);
        Assert.AreEqual("Original", new GptSoVitsProfileStore(store.StorageDirectory).GetById("clone")!.Name);
    }

    [TestMethod]
    public void Delete_PersistsRemovalAndLeavesOtherProfiles()
    {
        using var env = new IsolatedTestEnvironment();
        var store = new GptSoVitsProfileStore(env.Subdirectory("profiles"));
        store.Upsert(new() { Id = "1", Name = "One" });
        store.Upsert(new() { Id = "2", Name = "Two" });
        Assert.IsTrue(store.Delete("1"));
        Assert.IsFalse(store.Delete("1"));
        Assert.IsNull(store.GetById("1"));
        var disk = new GptSoVitsProfileStore(store.StorageDirectory);
        Assert.AreEqual(1, disk.GetProfiles().Count);
        Assert.AreEqual("Two", disk.GetById("2")!.Name);
    }

    [TestMethod]
    public void CorruptJsonOrNullJson_GracefullyLoadsEmptyProfileSet()
    {
        using var env = new IsolatedTestEnvironment();
        var directory = env.Subdirectory("profiles");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "profiles.json"), "{not-json");
        Assert.AreEqual(0, new GptSoVitsProfileStore(directory).GetProfiles().Count);
        File.WriteAllText(Path.Combine(directory, "profiles.json"), "null");
        Assert.AreEqual(0, new GptSoVitsProfileStore(directory).GetProfiles().Count);
    }

    [TestMethod]
    public void SavedProfilesWithoutNames_AreSkippedOnReload()
    {
        using var env = new IsolatedTestEnvironment();
        var directory = env.Subdirectory("profiles");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "profiles.json"),
            "[{\"Id\":\"one\",\"Name\":\"\"},{\"Id\":\"two\",\"Name\":\"Two\"}]");
        var store = new GptSoVitsProfileStore(directory);
        Assert.AreEqual(1, store.GetProfiles().Count);
        Assert.AreEqual("two", store.GetProfiles()[0].Id);
    }

    [TestMethod]
    [DataRow("zh", "all_zh")]
    [DataRow("en", "en")]
    [DataRow("ja", "all_ja")]
    [DataRow("ko", "all_ko")]
    [DataRow("yue", "all_yue")]
    public void SupportedLanguages_AreRetained(string promptLanguage, string textLanguage)
    {
        using var env = new IsolatedTestEnvironment();
        var store = new GptSoVitsProfileStore(env.Subdirectory("profiles"));
        store.Upsert(new() { Id = "v", Name = "v", PromptLanguage = promptLanguage, TextLanguage = textLanguage });
        Assert.AreEqual(promptLanguage, store.GetById("v")!.PromptLanguage);
        Assert.AreEqual(textLanguage, store.GetById("v")!.TextLanguage);
    }

    [TestMethod]
    public void NullArgument_IsRejected()
    {
        using var env = new IsolatedTestEnvironment();
        var store = new GptSoVitsProfileStore(env.Subdirectory("profiles"));
        Assert.ThrowsExactly<ArgumentNullException>(() => store.Upsert(null!));
        Assert.ThrowsExactly<ArgumentException>(() => new GptSoVitsProfileStore(""));
    }
}
