using Microsoft.VisualStudio.TestTools.UnitTesting;
using SherpaOnnx;
using System.Text;

namespace WarThunderChatTranslator.Tests;

[TestClass]
public sealed class LocalSpeechRecognitionTests
{
    private const string ModelFolder = "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17";

    [TestMethod]
    [DataRow("en_human_hello.wav", "hello")]
    [DataRow("en_human_intro.wav", "jersey|texas|chicago")]
    [DataRow("en_human_chicago.wav", "chicago|jersey")]
    [DataRow("en_human_yankee.wav", "yankee")]
    [DataRow("zh_human_nihao.wav", "你好")]
    [DataRow("zh_human_kebukeyi.wav", "可不可以")]
    [DataRow("zh_test.wav", "生活就像海洋|只有意志坚强的人才能到达彼岸")]
    public void SenseVoiceRecognizesBundledHumanTestAudio(string fileName, string expectedAnchors)
    {
        var modelDirectory = FindModelDirectory();
        var modelPath = Path.Combine(modelDirectory, "model.int8.onnx");
        var tokensPath = Path.Combine(modelDirectory, "tokens.txt");
        if (!File.Exists(modelPath) || !File.Exists(tokensPath))
        {
            Assert.Inconclusive(
                $"Local ASR model is not present. Ensure the Git LFS model files are available before running this test. Expected: {modelDirectory}");
        }

        var audioPath = Path.Combine(AppContext.BaseDirectory, "TestAudio", fileName);
        if (!File.Exists(audioPath))
        {
            Assert.Inconclusive($"Human test audio is not present in the test project. Expected: {audioPath}");
        }

        var (sampleRate, samples) = ReadPcm16MonoWave(audioPath);
        if (sampleRate != 16000)
        {
            samples = ResampleLinear(samples, sampleRate, 16000);
            sampleRate = 16000;
        }

        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Tokens = tokensPath;
        config.ModelConfig.SenseVoice.Model = modelPath;
        config.ModelConfig.SenseVoice.Language = "auto";
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
        config.ModelConfig.NumThreads = 1;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";

        using var recognizer = new OfflineRecognizer(config);
        using var stream = recognizer.CreateStream();
        stream.AcceptWaveform(sampleRate, samples);
        recognizer.Decode(stream);

        var text = stream.Result.Text?.Trim();
        Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"No ASR result for {fileName}.");
        TestContext.WriteLine($"{fileName}: {text}");

        var normalized = NormalizeForAnchorCheck(text);
        var anchors = expectedAnchors.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matched = anchors.Count(anchor => normalized.Contains(NormalizeForAnchorCheck(anchor), StringComparison.OrdinalIgnoreCase));
        var minimumMatches = anchors.Length >= 3 ? 2 : 1;
        Assert.IsTrue(
            matched >= minimumMatches,
            $"ASR output for {fileName} did not contain enough expected anchors. Expected at least {minimumMatches} of [{string.Join(", ", anchors)}], actual: {text}");
    }

    private static string NormalizeForAnchorCheck(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var buffer = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch >= 0x4E00)
            {
                buffer.Append(ch);
            }
        }
        return buffer.ToString();
    }

    private static float[] ResampleLinear(float[] input, int sourceRate, int targetRate)
    {
        if (input.Length == 0 || sourceRate == targetRate) return input;
        var outputLength = Math.Max(1, (int)Math.Round(input.Length * (double)targetRate / sourceRate));
        var output = new float[outputLength];
        var scale = (double)sourceRate / targetRate;
        for (var i = 0; i < outputLength; i++)
        {
            var sourcePosition = i * scale;
            var left = Math.Min((int)sourcePosition, input.Length - 1);
            var right = Math.Min(left + 1, input.Length - 1);
            var fraction = sourcePosition - left;
            output[i] = (float)(input[left] + (input[right] - input[left]) * fraction);
        }
        return output;
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
