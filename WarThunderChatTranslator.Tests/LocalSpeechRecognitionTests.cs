using Microsoft.VisualStudio.TestTools.UnitTesting;
using SherpaOnnx;
using System.Text;

namespace WarThunderChatTranslator.Tests;

[TestClass]
public sealed class LocalSpeechRecognitionTests
{
    private const string ModelFolder = "sherpa-onnx-paraformer-zh-small-2024-03-09";

    [DataTestMethod]
    [DataRow("zh_short.wav")]
    [DataRow("zh_game.wav")]
    [DataRow("zh_long.wav")]
    [DataRow("en_short.wav")]
    [DataRow("en_game.wav")]
    [DataRow("en_mixed.wav")]
    public void ParaformerRecognizesBundledTestAudio(string fileName)
    {
        var modelDirectory = FindModelDirectory();
        var modelPath = Path.Combine(modelDirectory, "model.int8.onnx");
        var tokensPath = Path.Combine(modelDirectory, "tokens.txt");
        if (!File.Exists(modelPath) || !File.Exists(tokensPath))
        {
            Assert.Inconclusive(
                $"Local ASR model is not present. Run DownloadSpeechModel.ps1 before this test. Expected: {modelDirectory}");
        }

        var audioPath = Path.Combine(AppContext.BaseDirectory, "TestAudio", fileName);
        Assert.IsTrue(File.Exists(audioPath), $"Missing test audio: {audioPath}");
        var (sampleRate, samples) = ReadPcm16MonoWave(audioPath);

        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Tokens = tokensPath;
        config.ModelConfig.Paraformer.Model = modelPath;
        config.ModelConfig.NumThreads = 1;
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.ModelType = "paraformer";
        config.DecodingMethod = "greedy_search";

        using var recognizer = new OfflineRecognizer(config);
        using var stream = recognizer.CreateStream();
        stream.AcceptWaveform(sampleRate, samples);
        recognizer.Decode(stream);

        var text = stream.Result.Text?.Trim();
        Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"No ASR result for {fileName}.");
        TestContext.WriteLine($"{fileName}: {text}");
    }

    public TestContext TestContext { get; set; }

    private static string FindModelDirectory()
    {
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        while (cursor != null)
        {
            var candidate = Path.Combine(cursor.FullName, "WarThunderChatTranslator", "Assets", "SpeechModels", ModelFolder);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            cursor = cursor.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Assets", "SpeechModels", ModelFolder);
    }

    private static (int SampleRate, float[] Samples) ReadPcm16MonoWave(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

        Assert.AreEqual("RIFF", new string(reader.ReadChars(4)));
        _ = reader.ReadUInt32();
        Assert.AreEqual("WAVE", new string(reader.ReadChars(4)));

        ushort formatTag = 0;
        ushort channels = 0;
        ushort bitsPerSample = 0;
        int sampleRate = 0;
        byte[] data = null;

        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadUInt32();
            var next = reader.BaseStream.Position + chunkSize;

            if (chunkId == "fmt ")
            {
                formatTag = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(checked((int)chunkSize));
            }

            reader.BaseStream.Position = Math.Min(next + (chunkSize % 2), reader.BaseStream.Length);
        }

        Assert.AreEqual((ushort)1, formatTag, "Test WAV must be PCM.");
        Assert.AreEqual((ushort)1, channels, "Test WAV must be mono.");
        Assert.AreEqual((ushort)16, bitsPerSample, "Test WAV must be 16-bit.");
        Assert.IsNotNull(data, "WAV data chunk was not found.");

        var samples = new float[data.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(data, i * 2) / 32768f;
        }

        return (sampleRate, samples);
    }
}
