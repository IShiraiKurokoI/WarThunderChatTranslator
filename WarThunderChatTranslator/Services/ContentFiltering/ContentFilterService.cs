#nullable enable

using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using Windows.Storage;

namespace WarThunderChatTranslator.Services.ContentFiltering
{
    internal sealed class ContentFilterService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        private static readonly string[] ZeroWidthCharacters = ["\u200B", "\u200C", "\u200D", "\u2060", "\uFEFF"];
        private readonly object _sync = new();
        private readonly ConcurrentDictionary<string, ContentFilterResult> _displayCache = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ContentFilterResult> _speechCache = new(StringComparer.Ordinal);
        private volatile ContentFilterEntry[] _entries = [];
        private volatile string[] _allowlist = [];
        private List<ContentFilterEntry> _userBlocklist = [];
        private List<string> _userAllowlist = [];
        private long _version;

        public static ContentFilterService Shared { get; } = new();

        public event Action? Changed;

        public long Version => Interlocked.Read(ref _version);

        public IReadOnlyList<ContentFilterEntry> UserBlocklist
        {
            get
            {
                lock (_sync)
                {
                    return _userBlocklist.Select(entry => entry.Clone()).ToArray();
                }
            }
        }

        public IReadOnlyList<string> UserAllowlist
        {
            get
            {
                lock (_sync)
                {
                    return _userAllowlist.ToArray();
                }
            }
        }

        public int BuiltInEnglishCount { get; private set; }
        public int BuiltInChineseCount { get; private set; }

        private ContentFilterService()
        {
            ReloadFromDisk(raiseChanged: false);
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        public void NotifyConfigurationChanged()
        {
            SignalChanged();
        }

        public ContentFilterResult FilterForDisplay(string? text)
        {
            var value = text ?? string.Empty;
            if (!ContentFilterConfig.ShouldFilterDisplay())
            {
                return Unchanged(value);
            }

            return _displayCache.GetOrAdd(value, static text => Shared.FilterCore(text, removeForSpeech: false));
        }

        public string? FilterForTts(string? text)
        {
            var value = text ?? string.Empty;
            if (!ContentFilterConfig.ShouldFilterTts())
            {
                return value;
            }

            var result = _speechCache.GetOrAdd(value, static text => Shared.FilterCore(text, removeForSpeech: true));
            if (!result.IsMatch)
            {
                return value;
            }

            if (string.Equals(
                ContentFilterConfig.GetTtsAction(),
                ContentFilterConfig.TtsActionSkipMessage,
                StringComparison.Ordinal))
            {
                return null;
            }

            return result.FilteredText;
        }

        public ChatMessage CreateDisplayMessage(ChatMessage source)
        {
            var original = string.IsNullOrEmpty(source.OriginalMessage) ? source.Msg ?? string.Empty : source.OriginalMessage;
            var translation = string.IsNullOrEmpty(source.TranslatedMessage) ? original : source.TranslatedMessage;
            var filteredOriginal = FilterForDisplay(original).FilteredText;
            var filteredTranslation = FilterForDisplay(translation).FilteredText;

            return new ChatMessage
            {
                Id = source.Id,
                Msg = filteredOriginal,
                Sender = source.Sender,
                Enemy = source.Enemy,
                Mode = source.Mode,
                Time = source.Time,
                OriginalMessage = filteredOriginal,
                TranslatedMessage = filteredTranslation,
                SourceLanguageIsoCode = source.SourceLanguageIsoCode,
                TranslationSucceeded = source.TranslationSucceeded,
                PrettyMessage = $"{source.Sender}: {filteredTranslation}"
            };
        }

        public void AddBlockEntry(ContentFilterEntry entry)
        {
            var normalized = SanitizeEntry(entry);
            if (normalized is null)
            {
                return;
            }

            lock (_sync)
            {
                var existing = _userBlocklist.FirstOrDefault(item =>
                    string.Equals(NormalizeSimple(item.Term), NormalizeSimple(normalized.Term), StringComparison.Ordinal)
                    && ResolveMatchMode(item) == ResolveMatchMode(normalized));
                if (existing is not null)
                {
                    existing.Severity = Math.Max(existing.Severity, normalized.Severity);
                    existing.Language = normalized.Language;
                }
                else
                {
                    _userBlocklist.Add(normalized);
                }

                PersistUserListsLocked();
                RebuildEntriesLocked();
            }

            SignalChanged();
        }

        public void RemoveBlockEntry(string term, ContentFilterMatchMode matchMode)
        {
            var normalizedTerm = NormalizeSimple(term);
            lock (_sync)
            {
                _userBlocklist.RemoveAll(item =>
                    string.Equals(NormalizeSimple(item.Term), normalizedTerm, StringComparison.Ordinal)
                    && item.MatchMode == matchMode);
                PersistUserListsLocked();
                RebuildEntriesLocked();
            }

            SignalChanged();
        }

        public void AddAllowTerm(string term)
        {
            var normalized = NormalizeSimple(term);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            lock (_sync)
            {
                if (!_userAllowlist.Any(item => string.Equals(NormalizeSimple(item), normalized, StringComparison.Ordinal)))
                {
                    _userAllowlist.Add(term.Trim());
                    PersistUserListsLocked();
                    RebuildEntriesLocked();
                }
            }

            SignalChanged();
        }

        public void RemoveAllowTerm(string term)
        {
            var normalized = NormalizeSimple(term);
            lock (_sync)
            {
                _userAllowlist.RemoveAll(item => string.Equals(NormalizeSimple(item), normalized, StringComparison.Ordinal));
                PersistUserListsLocked();
                RebuildEntriesLocked();
            }

            SignalChanged();
        }

        public void ImportPackage(string text, bool plainText)
        {
            ContentFilterPackage package;
            if (plainText)
            {
                package = new ContentFilterPackage
                {
                    Blocklist = SplitLines(text)
                        .Select(term => new ContentFilterEntry { Term = term, Severity = 2, MatchMode = ContentFilterMatchMode.Auto })
                        .ToList()
                };
            }
            else
            {
                package = JsonSerializer.Deserialize<ContentFilterPackage>(text, JsonOptions)
                    ?? throw new InvalidDataException("The content filter package is empty or invalid.");
            }

            lock (_sync)
            {
                foreach (var entry in package.Blocklist ?? [])
                {
                    var sanitized = SanitizeEntry(entry);
                    if (sanitized is null)
                    {
                        continue;
                    }

                    var existing = _userBlocklist.FirstOrDefault(item =>
                        string.Equals(NormalizeSimple(item.Term), NormalizeSimple(sanitized.Term), StringComparison.Ordinal)
                        && ResolveMatchMode(item) == ResolveMatchMode(sanitized));
                    if (existing is null)
                    {
                        _userBlocklist.Add(sanitized);
                    }
                    else
                    {
                        existing.Severity = Math.Max(existing.Severity, sanitized.Severity);
                    }
                }

                foreach (var term in package.Allowlist ?? [])
                {
                    var normalized = NormalizeSimple(term);
                    if (!string.IsNullOrWhiteSpace(normalized)
                        && !_userAllowlist.Any(item => string.Equals(NormalizeSimple(item), normalized, StringComparison.Ordinal)))
                    {
                        _userAllowlist.Add(term.Trim());
                    }
                }

                PersistUserListsLocked();
                RebuildEntriesLocked();
            }

            SignalChanged();
        }

        public string ExportPackage()
        {
            lock (_sync)
            {
                return JsonSerializer.Serialize(new ContentFilterPackage
                {
                    FormatVersion = 1,
                    Blocklist = _userBlocklist.Select(entry => entry.Clone()).ToList(),
                    Allowlist = _userAllowlist.ToList()
                }, JsonOptions);
            }
        }

        public void ReloadFromDisk(bool raiseChanged = true)
        {
            lock (_sync)
            {
                _userBlocklist = LoadUserBlocklist();
                _userAllowlist = LoadUserAllowlist();
                RebuildEntriesLocked();
            }

            if (raiseChanged)
            {
                SignalChanged();
            }
        }

        private ContentFilterResult FilterCore(string text, bool removeForSpeech)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Unchanged(text);
            }

            var normalized = NormalizeWithMap(text);
            if (normalized.Text.Length == 0)
            {
                return Unchanged(text);
            }

            var minimumSeverity = ContentFilterConfig.GetMinimumSeverity();
            var allowlist = _allowlist;
            var entries = _entries;
            var allowSpans = FindAllowSpans(normalized.Text, allowlist);
            var matches = new List<ContentFilterMatch>();

            foreach (var entry in entries)
            {
                if (entry.Severity < minimumSeverity)
                {
                    continue;
                }

                var term = NormalizeSimple(entry.Term);
                if (string.IsNullOrWhiteSpace(term))
                {
                    continue;
                }

                var matchMode = ResolveMatchMode(entry);
                foreach (var span in FindMatches(normalized.Text, term, matchMode))
                {
                    if (allowSpans.Any(allow => SpansOverlap(span.Start, span.Length, allow.Start, allow.Length)))
                    {
                        continue;
                    }

                    var originalStart = normalized.Map[span.Start].Start;
                    var lastMap = normalized.Map[span.Start + span.Length - 1];
                    var originalEnd = lastMap.Start + lastMap.Length;
                    matches.Add(new ContentFilterMatch(
                        originalStart,
                        Math.Max(1, originalEnd - originalStart),
                        entry.Term,
                        entry.Severity,
                        matchMode));
                }
            }

            if (matches.Count == 0)
            {
                return Unchanged(text);
            }

            var merged = MergeMatches(matches);
            var filtered = ApplyReplacement(text, merged, removeForSpeech);
            return new ContentFilterResult
            {
                OriginalText = text,
                FilteredText = filtered,
                IsMatch = true,
                MaxSeverity = merged.Max(match => match.Severity),
                Matches = merged
            };
        }

        private void RebuildEntriesLocked()
        {
            var builtIn = new List<ContentFilterEntry>();
            var basePath = Path.Combine(AppContext.BaseDirectory, "Assets", "ContentFilters");
            var english = LoadLines(Path.Combine(basePath, "en.txt"));
            var chinese = LoadLines(Path.Combine(basePath, "zh.txt"));
            BuiltInEnglishCount = english.Count;
            BuiltInChineseCount = chinese.Count;

            builtIn.AddRange(english.Select(term => new ContentFilterEntry
            {
                Term = term,
                Severity = 1,
                Language = "en",
                MatchMode = ContentFilterMatchMode.Word
            }));
            builtIn.AddRange(chinese.Select(term => new ContentFilterEntry
            {
                Term = term,
                Severity = 1,
                Language = "zh",
                MatchMode = ContentFilterMatchMode.Contains
            }));
            builtIn.AddRange(CreateGameChatRules());
            builtIn.AddRange(_userBlocklist.Select(entry => entry.Clone()));

            var deduplicated = builtIn
                .Select(SanitizeEntry)
                .Where(entry => entry is not null)
                .Cast<ContentFilterEntry>()
                .GroupBy(entry => $"{NormalizeSimple(entry.Term)}\u001F{ResolveMatchMode(entry)}", StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(entry => entry.Severity).First())
                .OrderByDescending(entry => NormalizeSimple(entry.Term).Length)
                .ToArray();

            _entries = deduplicated;
            _allowlist = _userAllowlist
                .Select(NormalizeSimple)
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(term => term.Length)
                .ToArray();
        }

        private static IEnumerable<ContentFilterEntry> CreateGameChatRules()
        {
            // Upstream LDNOOBW is deliberately broad. These overrides identify terms that are
            // much more likely to be direct profanity/abuse in fast game chat, so the default
            // severity-2 mode remains useful without censoring every adult-context term.
            string[] severity2English =
            [
                "apeshit", "arsehole", "asshole", "assmunch", "bastard", "bitch", "bitches",
                "bollocks", "bullshit", "clusterfuck", "cunt", "dick", "dumbass", "douchebag",
                "dipshit", "eat my ass", "fuck", "fuckin", "fucking", "fucktards", "god damn",
                "idiot", "jackass", "jerk off", "moron", "motherfucker", "piece of shit", "piss",
                "prick", "retard", "retarded", "shit", "shithead", "shitty", "slut", "stfu",
                "suck", "sucks", "tosser", "twat", "wank", "whore"
            ];

            string[] severity3English =
            [
                "beaner", "beaners", "chink", "coon", "coons", "darkie", "fag", "faggot", "gook",
                "honkey", "jigaboo", "jiggaboo", "jiggerboo", "kike", "nigga", "nigger", "paki",
                "pikey", "raghead", "slanteye", "spic", "towelhead", "tranny", "wetback", "white power"
            ];

            string[] severity2Chinese =
            [
                "下三烂", "下贱", "他妈", "他妈的", "你个傻比", "你妈", "你妈的", "傻比", "傻逼",
                "煞笔", "沙比", "吃屎", "妈的", "妈逼", "婊子", "小骚货", "屌", "干你", "干你妈",
                "干你娘", "干你老母", "干死你", "操你", "操你妈", "操你娘", "操你祖宗", "操你老母",
                "操比", "操逼", "日你", "日你妈", "杂种", "烂货", "烂逼", "狗屁", "狗日", "垃圾",
                "废物", "菜逼", "菜鸡", "脑残", "弱智", "智障", "王八蛋", "白痴", "白癡", "笨蛋",
                "贱人", "贱货", "逼样", "骚货", "骚逼", "鸡巴", "龟儿子", "仆街", "硬膠"
            ];

            string[] severity3Chinese =
            [
                "我操你祖宗十八代", "操你全家", "操妳全家", "婊子养的", "狗狼养的", "死全家", "咸家鏟", "冚家鏟"
            ];

            string[] severity2GameAbbreviations =
            [
                "cnm", "nmsl", "wdnmd", "mmp", "stfu", "gtfo"
            ];

            foreach (var term in severity2English)
                yield return new ContentFilterEntry { Term = term, Severity = 2, Language = "en", MatchMode = ContentFilterMatchMode.Word };
            foreach (var term in severity3English)
                yield return new ContentFilterEntry { Term = term, Severity = 3, Language = "en", MatchMode = ContentFilterMatchMode.Word };
            foreach (var term in severity2Chinese)
                yield return new ContentFilterEntry { Term = term, Severity = 2, Language = "zh", MatchMode = ContentFilterMatchMode.Contains };
            foreach (var term in severity3Chinese)
                yield return new ContentFilterEntry { Term = term, Severity = 3, Language = "zh", MatchMode = ContentFilterMatchMode.Contains };
            foreach (var term in severity2GameAbbreviations)
                yield return new ContentFilterEntry { Term = term, Severity = 2, Language = "auto", MatchMode = ContentFilterMatchMode.Word };
        }

        private static ContentFilterEntry? SanitizeEntry(ContentFilterEntry? entry)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Term))
            {
                return null;
            }

            return new ContentFilterEntry
            {
                Term = entry.Term.Trim(),
                Severity = Math.Clamp(entry.Severity, ContentFilterConfig.MinSeverity, ContentFilterConfig.MaxSeverity),
                Language = string.IsNullOrWhiteSpace(entry.Language) ? "auto" : entry.Language.Trim(),
                MatchMode = Enum.IsDefined(typeof(ContentFilterMatchMode), entry.MatchMode) ? entry.MatchMode : ContentFilterMatchMode.Auto
            };
        }

        private static ContentFilterMatchMode ResolveMatchMode(ContentFilterEntry entry)
        {
            if (entry.MatchMode == ContentFilterMatchMode.Contains)
            {
                return ContentFilterMatchMode.Contains;
            }

            if (entry.MatchMode == ContentFilterMatchMode.Word)
            {
                return entry.Term.Any(IsWordCharacter)
                    ? ContentFilterMatchMode.Word
                    : ContentFilterMatchMode.Contains;
            }

            return ContainsCjk(entry.Term) || !entry.Term.Any(IsWordCharacter)
                ? ContentFilterMatchMode.Contains
                : ContentFilterMatchMode.Word;
        }

        private static bool ContainsCjk(string text)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                var value = rune.Value;
                if ((value >= 0x3400 && value <= 0x9FFF) || (value >= 0xF900 && value <= 0xFAFF))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<(int Start, int Length)> FindMatches(string text, string term, ContentFilterMatchMode mode)
        {
            var start = 0;
            while (start <= text.Length - term.Length)
            {
                var index = text.IndexOf(term, start, StringComparison.Ordinal);
                if (index < 0)
                {
                    yield break;
                }

                if (mode == ContentFilterMatchMode.Contains || IsWordBoundaryMatch(text, index, term.Length))
                {
                    yield return (index, term.Length);
                }

                start = index + Math.Max(1, term.Length);
            }
        }

        private static bool IsWordBoundaryMatch(string text, int start, int length)
        {
            var leftOk = start == 0 || !IsWordCharacter(text[start - 1]);
            var end = start + length;
            var rightOk = end >= text.Length || !IsWordCharacter(text[end]);
            return leftOk && rightOk;
        }

        private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static List<(int Start, int Length)> FindAllowSpans(string normalizedText, string[] allowlist)
        {
            var spans = new List<(int Start, int Length)>();
            foreach (var term in allowlist)
            {
                var mode = ContainsCjk(term) ? ContentFilterMatchMode.Contains : ContentFilterMatchMode.Word;
                spans.AddRange(FindMatches(normalizedText, term, mode));
            }
            return spans;
        }

        private static bool SpansOverlap(int aStart, int aLength, int bStart, int bLength)
        {
            return aStart < bStart + bLength && bStart < aStart + aLength;
        }

        private static IReadOnlyList<ContentFilterMatch> MergeMatches(List<ContentFilterMatch> matches)
        {
            var ordered = matches
                .OrderBy(match => match.Start)
                .ThenByDescending(match => match.Length)
                .ThenByDescending(match => match.Severity)
                .ToList();
            var merged = new List<ContentFilterMatch>();

            foreach (var match in ordered)
            {
                if (merged.Count == 0)
                {
                    merged.Add(match);
                    continue;
                }

                var previous = merged[^1];
                var previousEnd = previous.Start + previous.Length;
                var currentEnd = match.Start + match.Length;
                if (match.Start <= previousEnd)
                {
                    merged[^1] = new ContentFilterMatch(
                        previous.Start,
                        Math.Max(previousEnd, currentEnd) - previous.Start,
                        previous.Severity >= match.Severity ? previous.Term : match.Term,
                        Math.Max(previous.Severity, match.Severity),
                        previous.MatchMode);
                }
                else
                {
                    merged.Add(match);
                }
            }

            return merged;
        }

        private static string ApplyReplacement(string text, IReadOnlyList<ContentFilterMatch> matches, bool removeForSpeech)
        {
            var builder = new StringBuilder(text);
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                var replacement = removeForSpeech ? " " : "****";
                builder.Remove(match.Start, Math.Min(match.Length, builder.Length - match.Start));
                builder.Insert(match.Start, replacement);
            }

            if (!removeForSpeech)
            {
                return builder.ToString();
            }

            return CollapseWhitespace(builder.ToString()).Trim(' ', ',', '.', ';', ':', '，', '。', '；', '：');
        }

        private static NormalizedText NormalizeWithMap(string input)
        {
            var text = new StringBuilder();
            var map = new List<OriginalSpan>();
            var originalIndex = 0;
            var lastWasWhitespace = false;

            foreach (var rune in input.EnumerateRunes())
            {
                var length = rune.Utf16SequenceLength;
                var piece = rune.ToString().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
                piece = RemoveZeroWidth(piece);

                foreach (var c in piece)
                {
                    if (char.IsWhiteSpace(c))
                    {
                        if (lastWasWhitespace)
                        {
                            continue;
                        }

                        text.Append(' ');
                        map.Add(new OriginalSpan(originalIndex, length));
                        lastWasWhitespace = true;
                    }
                    else
                    {
                        text.Append(c);
                        map.Add(new OriginalSpan(originalIndex, length));
                        lastWasWhitespace = false;
                    }
                }

                originalIndex += length;
            }

            return new NormalizedText(text.ToString(), map.ToArray());
        }

        private static string NormalizeSimple(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var normalized = input.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            normalized = RemoveZeroWidth(normalized);
            return CollapseWhitespace(normalized).Trim();
        }

        private static string RemoveZeroWidth(string value)
        {
            foreach (var character in ZeroWidthCharacters)
            {
                value = value.Replace(character, string.Empty, StringComparison.Ordinal);
            }
            return value;
        }

        private static string CollapseWhitespace(string value)
        {
            var builder = new StringBuilder(value.Length);
            var previousWhitespace = false;
            foreach (var c in value)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!previousWhitespace)
                    {
                        builder.Append(' ');
                    }
                    previousWhitespace = true;
                }
                else
                {
                    builder.Append(c);
                    previousWhitespace = false;
                }
            }
            return builder.ToString();
        }

        private static List<string> LoadLines(string path)
        {
            try
            {
                return File.Exists(path)
                    ? SplitLines(File.ReadAllText(path, Encoding.UTF8)).Distinct(StringComparer.Ordinal).ToList()
                    : [];
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not load built-in content filter list {0}.", path);
                return [];
            }
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.Length > 0 && !line.StartsWith('#'));
        }

        private static readonly object UserDirectorySync = new();
        private static string? _userDirectory;

        public static string UserDirectory
        {
            get
            {
                if (_userDirectory is not null)
                {
                    return _userDirectory;
                }

                lock (UserDirectorySync)
                {
                    if (_userDirectory is not null)
                    {
                        return _userDirectory;
                    }

                    // NLog writes to <Documents>\WTChatTranslator\Log. Keep custom
                    // dictionaries under the same WTChatTranslator parent so user-owned
                    // files are easy to find and back up. Application settings remain in
                    // ApplicationData.LocalSettings and are not moved here.
                    var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    var root = string.IsNullOrWhiteSpace(documents)
                        ? ApplicationData.Current.LocalFolder.Path
                        : Path.Combine(documents, "WTChatTranslator");
                    var path = Path.Combine(root, "ContentFilters");
                    Directory.CreateDirectory(path);

                    TryMigrateLegacyUserDictionaryFiles(path);
                    _userDirectory = path;
                    return path;
                }
            }
        }

        private static string UserBlocklistPath => Path.Combine(UserDirectory, "custom-blocklist.json");
        private static string UserAllowlistPath => Path.Combine(UserDirectory, "custom-allowlist.json");

        private static void TryMigrateLegacyUserDictionaryFiles(string destinationDirectory)
        {
            try
            {
                var legacyDirectory = Path.Combine(ApplicationData.Current.LocalFolder.Path, "ContentFilters");
                if (string.Equals(legacyDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase)
                    || !Directory.Exists(legacyDirectory))
                {
                    return;
                }

                foreach (var fileName in new[] { "custom-blocklist.json", "custom-allowlist.json" })
                {
                    var source = Path.Combine(legacyDirectory, fileName);
                    var destination = Path.Combine(destinationDirectory, fileName);
                    if (File.Exists(source) && !File.Exists(destination))
                    {
                        File.Copy(source, destination, overwrite: false);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not migrate the legacy custom content-filter dictionary files.");
            }
        }

        private static List<ContentFilterEntry> LoadUserBlocklist()
        {
            try
            {
                return File.Exists(UserBlocklistPath)
                    ? JsonSerializer.Deserialize<List<ContentFilterEntry>>(File.ReadAllText(UserBlocklistPath, Encoding.UTF8), JsonOptions) ?? []
                    : [];
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not load the custom content-filter blocklist.");
                return [];
            }
        }

        private static List<string> LoadUserAllowlist()
        {
            try
            {
                return File.Exists(UserAllowlistPath)
                    ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(UserAllowlistPath, Encoding.UTF8), JsonOptions) ?? []
                    : [];
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not load the custom content-filter allowlist.");
                return [];
            }
        }

        private void PersistUserListsLocked()
        {
            File.WriteAllText(UserBlocklistPath, JsonSerializer.Serialize(_userBlocklist, JsonOptions), Encoding.UTF8);
            File.WriteAllText(UserAllowlistPath, JsonSerializer.Serialize(_userAllowlist, JsonOptions), Encoding.UTF8);
        }

        private void SignalChanged()
        {
            _displayCache.Clear();
            _speechCache.Clear();
            Interlocked.Increment(ref _version);
            try
            {
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "A content-filter change subscriber failed.");
            }
        }

        private static ContentFilterResult Unchanged(string text) => new()
        {
            OriginalText = text,
            FilteredText = text,
            IsMatch = false,
            MaxSeverity = 0,
            Matches = []
        };

        private readonly record struct OriginalSpan(int Start, int Length);
        private sealed record NormalizedText(string Text, OriginalSpan[] Map);
    }
}
