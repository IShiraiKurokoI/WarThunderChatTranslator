#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WarThunderChatTranslator.Services
{
    internal static class BundledKokoroTtsModel
    {
        public const string Id = "kokoro-int8-multi-lang-v1_1";
        public const string DisplayName = "Kokoro v1.1 INT8";
        public const string RuntimeDisplayName = "sherpa-onnx";
        public const string RuntimeVersion = "1.13.8";
        public const string LicenseName = "Apache-2.0";
        public const string SourceUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-int8-multi-lang-v1_1.tar.bz2";
        public const string DocumentationUrl = "https://k2-fsa.github.io/sherpa/onnx/tts/all/Chinese-English/kokoro-multi-lang-v1_1.html";
        public const string UpstreamModelUrl = "https://huggingface.co/hexgrad/Kokoro-82M-v1.1-zh";
        public const string ArchiveSha256 = "a1e94694776049035c4f2c6529f003aaece993c76aae9a78995831c3c4dcafc6";

        public static readonly IReadOnlyList<string> SpeakerNames = new[]
        {
            "af_maple", "af_sol", "bf_vale",
            "zf_001", "zf_002", "zf_003", "zf_004", "zf_005", "zf_006", "zf_007", "zf_008",
            "zf_017", "zf_018", "zf_019", "zf_021", "zf_022", "zf_023", "zf_024", "zf_026", "zf_027",
            "zf_028", "zf_032", "zf_036", "zf_038", "zf_039", "zf_040", "zf_042", "zf_043", "zf_044",
            "zf_046", "zf_047", "zf_048", "zf_049", "zf_051", "zf_059", "zf_060", "zf_067", "zf_070",
            "zf_071", "zf_072", "zf_073", "zf_074", "zf_075", "zf_076", "zf_077", "zf_078", "zf_079",
            "zf_083", "zf_084", "zf_085", "zf_086", "zf_087", "zf_088", "zf_090", "zf_092", "zf_093",
            "zf_094", "zf_099",
            "zm_009", "zm_010", "zm_011", "zm_012", "zm_013", "zm_014", "zm_015", "zm_016", "zm_020",
            "zm_025", "zm_029", "zm_030", "zm_031", "zm_033", "zm_034", "zm_035", "zm_037", "zm_041",
            "zm_045", "zm_050", "zm_052", "zm_053", "zm_054", "zm_055", "zm_056", "zm_057", "zm_058",
            "zm_061", "zm_062", "zm_063", "zm_064", "zm_065", "zm_066", "zm_068", "zm_069", "zm_080",
            "zm_081", "zm_082", "zm_089", "zm_091", "zm_095", "zm_096", "zm_097", "zm_098", "zm_100"
        };

        public static string DirectoryPath => Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "SpeechModels",
            "TTS",
            Id);

        public static string ModelFile => Path.Combine(DirectoryPath, "model.int8.onnx");
        public static string VoicesFile => Path.Combine(DirectoryPath, "voices.bin");
        public static string TokensFile => Path.Combine(DirectoryPath, "tokens.txt");
        public static string DataDirectory => Path.Combine(DirectoryPath, "espeak-ng-data");
        public static string LicenseFile => Path.Combine(DirectoryPath, "LICENSE");

        public static string Lexicon => string.Join(',', new[]
        {
            Path.Combine(DirectoryPath, "lexicon-us-en.txt"),
            Path.Combine(DirectoryPath, "lexicon-zh.txt")
        }.Where(File.Exists));

        public static string RuleFsts => string.Join(',', new[]
        {
            Path.Combine(DirectoryPath, "phone-zh.fst"),
            Path.Combine(DirectoryPath, "date-zh.fst"),
            Path.Combine(DirectoryPath, "number-zh.fst")
        }.Where(File.Exists));

        public static bool IsAvailable =>
            IsLargeFilePresent(ModelFile, 50L * 1024 * 1024)
            && IsLargeFilePresent(VoicesFile, 1L * 1024 * 1024)
            && File.Exists(TokensFile)
            && Directory.Exists(DataDirectory);

        public static IReadOnlyList<string> GetMissingComponents()
        {
            var missing = new List<string>();
            if (!IsLargeFilePresent(ModelFile, 50L * 1024 * 1024)) missing.Add("model.int8.onnx");
            if (!IsLargeFilePresent(VoicesFile, 1L * 1024 * 1024)) missing.Add("voices.bin");
            if (!File.Exists(TokensFile)) missing.Add("tokens.txt");
            if (!Directory.Exists(DataDirectory)) missing.Add("espeak-ng-data");
            return missing;
        }

        private static bool IsLargeFilePresent(string path, long minimumBytes)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length >= minimumBytes;
            }
            catch
            {
                return false;
            }
        }
    }
}
