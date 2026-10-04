using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WarThunderChatTranslator.Entities;

namespace WarThunderChatTranslator.Services
{
    // Messages contain fixed codes/field paths only, never input values or parser exceptions.
    public sealed class AiServiceImportException : Exception
    {
        public AiServiceImportException(string code) : base(code) { }
    }

    public sealed class AiServiceImportPlan
    {
        public List<AiProviderConfig> Models { get; } = new();
        public List<AiServiceDiscovery> Discoveries { get; } = new();
        public HashSet<string> Notices { get; } = new(StringComparer.Ordinal);
        public bool ChecksumVerified { get; internal set; }
    }

    public sealed class AiServiceDiscovery
    {
        public string Url { get; internal set; }
        public AiProviderConfig Template { get; internal set; }
        public List<AiProviderConfig> Models { get; } = new();
    }

    public static class AiServiceImport
    {
        public const int MaxUriLength = 400000;
        public const int MaxJsonBytes = 256 * 1024;
        public const int MaxModels = 512;
        private const string Blocks = "capabilities inference transport compatibility";

        public static AiServiceImportPlan Parse(string input)
        {
            try
            {
                return ParseCore(input);
            }
            catch (AiServiceImportException) { throw; }
            catch (Exception ex) when (ex is JsonException || ex is FormatException ||
                                       ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
            {
                throw Error("invalid_encoding_or_json");
            }
        }

        private static AiServiceImportPlan ParseCore(string input)
        {
            var uri = input?.Trim() ?? string.Empty;
            Check(uri.Length <= MaxUriLength, "uri_limit");
            var match = Regex.Match(uri, @"\Aaiservice://(model|provider|bundle)\?([^\s#]*)\z", RegexOptions.IgnoreCase);
            Check(match.Success, "invalid_uri");
            var resource = match.Groups[1].Value.ToLowerInvariant();
            var query = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in match.Groups[2].Value.Split('&'))
            {
                var pieces = part.Split('=', 2);
                Check(pieces.Length == 2 && !Regex.IsMatch(part, @"%(?![0-9a-fA-F]{2})"), "invalid_query");
                Check(query.TryAdd(Uri.UnescapeDataString(pieces[0]), Uri.UnescapeDataString(pieces[1])), "duplicate_query");
            }
            Check(!query.ContainsKey("sig") && !query.ContainsKey("kid"), "unsupported_signature");
            Check(query.TryGetValue("v", out var version) && version == "1", "unsupported_transport_version");
            Check(query.TryGetValue("config", out var encoded) && !string.IsNullOrEmpty(encoded), "missing_config");
            Check(Regex.IsMatch(encoded, @"\A[A-Za-z0-9_-]+={0,2}\z"), "invalid_base64url");
            var bare = encoded.TrimEnd('=');
            Check(bare.Length % 4 != 1, "invalid_base64url");
            var padding = (4 - bare.Length % 4) % 4;
            Check(encoded == bare || encoded == bare + new string('=', padding), "invalid_base64url");
            var bytes = Convert.FromBase64String(bare.Replace('-', '+').Replace('_', '/') + new string('=', padding));
            Check(bytes.Length <= MaxJsonBytes, "json_limit");
            Check(Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') == bare, "invalid_base64url");
            var plan = new AiServiceImportPlan();
            if (query.TryGetValue("checksum", out var checksum))
            {
                Check(Regex.IsMatch(checksum, @"\Asha256:[0-9a-f]{64}\z"), "unsupported_checksum");
                Check(checksum[7..] == Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "checksum_mismatch");
                plan.ChecksumVerified = true;
            }
            var json = new UTF8Encoding(false, true).GetString(bytes);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var count = 0;
            ValidateJson(document.RootElement, ref count);
            var root = JsonNode.Parse(json) as JsonObject;
            Check(root != null, "expected_object");
            Schema(root, "llm." + resource);
            var providers = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            if (resource == "bundle")
            {
                Fields(root, "schema version id label providers models", plan);
                var providerArray = RequiredArray(root, "providers");
                var models = RequiredArray(root, "models");
                Check(providerArray.Count + models.Count > 0, "empty_bundle");
                foreach (var item in providerArray)
                {
                    var provider = Object(item, "providers");
                    Schema(provider, "llm.provider");
                    Check(providers.TryAdd(Text(provider, "id", true), provider), "duplicate_provider_id");
                }
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in models)
                {
                    var model = Object(item, "models");
                    Schema(model, "llm.model");
                    Check(ids.Add(Text(model, "id", true)), "duplicate_model_id");
                    // Validate model references before resolving any provider.
                    Check(model.ContainsKey("endpoint") != model.ContainsKey("provider_ref"), "model_endpoint_or_reference");
                    JsonObject provider = null;
                    if (model.ContainsKey("provider_ref"))
                        Check(providers.TryGetValue(Text(model, "provider_ref", true), out provider), "unknown_provider_ref");
                    AddModel(model, provider, plan, true);
                }
                foreach (var provider in providers.Values) AddProvider(provider, plan);
            }
            else if (resource == "provider") AddProvider(root, plan);
            else
            {
                Check(root.ContainsKey("endpoint") && !root.ContainsKey("provider_ref"), "standalone_endpoint_required");
                AddModel(root, null, plan, true);
            }
            Check(plan.Models.Count <= MaxModels, "model_limit");
            return plan;
        }

        private static void AddProvider(JsonObject provider, AiServiceImportPlan plan)
        {
            Schema(provider, "llm.provider");
            Fields(provider, "schema version id label endpoint defaults models autodiscover", plan);
            Text(provider, "id", true);
            var endpoint = Object(provider["endpoint"], "endpoint");
            var defaults = provider.ContainsKey("defaults") ? Object(provider["defaults"], "defaults") : new JsonObject();
            ValidateBlocks(defaults, plan);
            var template = Map(endpoint, defaults, Text(provider, "label") ?? Text(provider, "id"), "", plan);
            var connection = new AiServiceDiscovery { Template = template };
            // Include top-level bundle models sharing this connection in discovery deduplication.
            connection.Models.AddRange(plan.Models.Where(model => model.BaseUrl == template.BaseUrl &&
                model.ApiType == template.ApiType && model.ApiKey == template.ApiKey));
            var modelIds = new HashSet<string>(StringComparer.Ordinal);
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            if (provider.ContainsKey("models"))
            {
                foreach (var item in RequiredArray(provider, "models"))
                {
                    var model = Object(item, "models");
                    Check(!model.ContainsKey("endpoint") && !model.ContainsKey("provider_ref") &&
                        !model.ContainsKey("schema") && !model.ContainsKey("version"), "invalid_provider_model");
                    Check(modelIds.Add(Text(model, "model", true)), "duplicate_model");
                    var id = Text(model, "id");
                    Check(id == null || entryIds.Add(id), "duplicate_model_id");
                    connection.Models.Add(AddModel(model, provider, plan, false));
                }
            }
            if (provider.ContainsKey("autodiscover"))
            {
                var discover = Object(provider["autodiscover"], "autodiscover");
                Fields(discover, "enabled url", plan);
                Bool(discover, "enabled");
                var url = Text(discover, "url");
                if (url != null) Url(url, true);
                if (discover["enabled"]?.GetValue<bool>() != false)
                {
                    connection.Url = url ?? template.BaseUrl.TrimEnd('/') + "/models";
                    plan.Discoveries.Add(connection);
                }
            }
            if (connection.Models.Count == 0)
            {
                // A service with no catalog is retained as an editable connection.
                plan.Models.Add(template);
                connection.Models.Add(template);
                plan.Notices.Add("model_required");
            }
        }

        private static AiProviderConfig AddModel(JsonObject model, JsonObject provider, AiServiceImportPlan plan, bool complete)
        {
            if (complete) Schema(model, "llm.model");
            Fields(model, "schema version id label model endpoint provider_ref " + Blocks, plan);
            ValidateBlocks(model, plan, false);
            var defaults = provider != null && provider.ContainsKey("defaults")
                ? Object(provider["defaults"], "defaults") : new JsonObject();
            ValidateBlocks(defaults, plan);
            var effective = new JsonObject();
            foreach (var block in Blocks.Split(' '))
                if (model.ContainsKey(block) || defaults.ContainsKey(block))
                    effective[block] = Merge(defaults[block], model[block]);
            var endpoint = Object(provider == null ? model["endpoint"] : provider["endpoint"], "endpoint");
            var modelName = Text(model, "model", true);
            var name = Text(model, "label") ?? (provider == null ? modelName :
                (Text(provider, "label") ?? Text(provider, "id", true)) + " / " + modelName);
            var config = Map(endpoint, effective, name, modelName, plan);
            plan.Models.Add(config);
            return config;
        }

        private static AiProviderConfig Map(JsonObject endpoint, JsonObject effective, string name, string model, AiServiceImportPlan plan)
        {
            Fields(endpoint, "base_url api_format provider credential", plan);
            Text(endpoint, "provider");
            var baseUrl = Text(endpoint, "base_url", true);
            Url(baseUrl, false);
            var type = Text(endpoint, "api_format", true) switch
            {
                "openai_chat_completions" => AiApiType.OpenAIChatCompletions,
                "openai_responses" => AiApiType.OpenAIResponses,
                "anthropic_messages" => AiApiType.Anthropic,
                _ => throw Error("unsupported_api_profile")
            };
            var key = "";
            if (endpoint.ContainsKey("credential"))
            {
                var credential = Object(endpoint["credential"], "credential");
                Fields(credential, "type value label", plan);
                Text(credential, "label");
                var credentialType = Text(credential, "type", true);
                Check(credentialType is "api_key" or "prompt", "unsupported_credential_type");
                if (credentialType == "api_key") key = Text(credential, "value", true);
                else if (credential.ContainsKey("value")) Text(credential, "value", true);
                Check(!key.Any(c => char.IsControl(c)), "invalid_credential");
            }
            var inference = effective["inference"] as JsonObject;
            var transport = effective["transport"] as JsonObject;
            var capabilities = effective["capabilities"] as JsonObject;
            var compatibility = effective["compatibility"] as JsonObject;
            var temperature = Number(inference, "temperature");
            Check(temperature == null || temperature <= (type == AiApiType.Anthropic ? 1 : 2), "inference.temperature_range");
            var limit = Number(inference, "max_output_tokens");
            var cap = Number(capabilities, "max_output_tokens");
            Check(limit == null || cap == null || limit <= cap, "inference.max_output_tokens_exceeds_capability");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (transport?["extra_headers"] is JsonObject extra)
            {
                using var request = new HttpRequestMessage();
                foreach (var header in extra)
                {
                    Check(headers.TryAdd(header.Key, header.Value.GetValue<string>()), "duplicate_header");
                    Check(!new[] { "Host", "Content-Length", "Transfer-Encoding", "Connection" }.Contains(header.Key, StringComparer.OrdinalIgnoreCase), "unsupported_header");
                    Check(key.Length == 0 || !header.Key.Equals(type == AiApiType.Anthropic ? "x-api-key" : "Authorization", StringComparison.OrdinalIgnoreCase), "credential_header_conflict");
                    Check(request.Headers.TryAddWithoutValidation(header.Key, header.Value.GetValue<string>()), "unsupported_header");
                }
            }
            if (transport?["user_agent"] is JsonValue userAgent)
            {
                Check(!headers.ContainsKey("User-Agent"), "user_agent_header_conflict");
                headers["User-Agent"] = userAgent.GetValue<string>();
            }
            if (key.Length == 0) plan.Notices.Add("credential_required");
            if (Number(transport, "timeout_seconds") > 100) plan.Notices.Add("timeout_client_limit");
            if (type != AiApiType.OpenAIChatCompletions && compatibility?.ContainsKey("max_tokens_field") == true)
                plan.Notices.Add("ignored.compatibility");
            return new AiProviderConfig
            {
                Name = name, Model = model, BaseUrl = baseUrl, ApiType = type, ApiKey = key,
                UseTemperature = temperature.HasValue, Temperature = temperature ?? 0.1,
                MaxOutputTokens = limit.HasValue ? (int)limit.Value : null,
                MaxTokensField = Text(compatibility, "max_tokens_field") ?? "max_tokens",
                TimeoutSeconds = Number(transport, "timeout_seconds"), ExtraHeaders = headers, UseExactBaseUrl = true
            };
        }

        private static void ValidateBlocks(JsonObject owner, AiServiceImportPlan plan, bool onlyBlocks = true)
        {
            if (onlyBlocks) Fields(owner, Blocks, plan);
            if (owner.ContainsKey("capabilities"))
            {
                var block = Object(owner["capabilities"], "capabilities");
                Fields(block, "context_window_tokens max_output_tokens input_modalities api_features", plan);
                Numeric(block, "context_window_tokens", true, 1);
                Numeric(block, "max_output_tokens", true, 1);
                if (block.ContainsKey("input_modalities")) StringArray(block, "input_modalities", true);
                if (block.ContainsKey("api_features"))
                {
                    var features = Object(block["api_features"], "capabilities.api_features");
                    const string flags = "store developer_role reasoning_effort stream_usage strict_tools long_cache_retention";
                    Fields(features, flags, plan);
                    foreach (var flag in flags.Split(' ')) Bool(features, flag);
                }
                plan.Notices.Add("ignored.capabilities");
            }
            if (owner.ContainsKey("inference"))
            {
                var block = Object(owner["inference"], "inference");
                Fields(block, "temperature max_output_tokens reasoning", plan);
                Numeric(block, "temperature", false, 0);
                Numeric(block, "max_output_tokens", true, 1);
                if (block.ContainsKey("reasoning"))
                {
                    var reasoning = Object(block["reasoning"], "inference.reasoning");
                    Fields(reasoning, "mode effort history budget_tokens", plan);
                    Enum(reasoning, "mode", "auto enabled disabled");
                    Enum(reasoning, "effort", "minimal low medium high xhigh");
                    Enum(reasoning, "history", "auto preserve discard");
                    Numeric(reasoning, "budget_tokens", true, 0);
                    plan.Notices.Add("ignored.inference.reasoning");
                }
            }
            if (owner.ContainsKey("transport"))
            {
                var block = Object(owner["transport"], "transport");
                Fields(block, "mode timeout_seconds retry cache_retention user_agent extra_headers", plan);
                Enum(block, "mode", "auto sse websocket websocket-cached");
                Enum(block, "cache_retention", "none short long");
                Numeric(block, "timeout_seconds", false, double.Epsilon, 86400);
                var agent = Text(block, "user_agent");
                Check(agent == null || agent.All(c => c >= 32 && c <= 126), "transport.user_agent");
                if (block.ContainsKey("extra_headers"))
                {
                    var headers = Object(block["extra_headers"], "transport.extra_headers");
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var header in headers)
                    {
                        Check(Regex.IsMatch(header.Key, @"\A[!#$%&'*+.^_`|~0-9A-Za-z-]+\z") && names.Add(header.Key), "invalid_header_name");
                        Check(header.Value is JsonValue value && value.TryGetValue<string>(out var text) &&
                            !text.Any(c => c == '\r' || c == '\n' || c == '\0'), "invalid_header_value");
                    }
                }
                if (block.ContainsKey("retry"))
                {
                    var retry = Object(block["retry"], "transport.retry");
                    Fields(retry, "max_retries initial_delay_seconds max_delay_seconds max_retry_after_seconds", plan);
                    Numeric(retry, "max_retries", true, 0);
                    foreach (var field in new[] { "initial_delay_seconds", "max_delay_seconds", "max_retry_after_seconds" }) Numeric(retry, field, false, 0);
                    Check(!(Number(retry, "initial_delay_seconds") > Number(retry, "max_delay_seconds")), "transport.retry_range");
                }
                foreach (var field in new[] { "mode", "retry", "cache_retention" })
                    if (block.ContainsKey(field)) plan.Notices.Add("ignored.transport." + field);
            }
            if (owner.ContainsKey("compatibility"))
            {
                var block = Object(owner["compatibility"], "compatibility");
                const string flags = "supports_store supports_developer_role supports_reasoning_effort supports_stream_usage supports_strict_tools supports_long_cache_retention requires_tool_result_name requires_assistant_after_tool_result requires_thinking_as_text requires_reasoning_content send_session_affinity_headers";
                Fields(block, "thinking_format max_tokens_field " + flags, plan);
                Text(block, "thinking_format");
                Enum(block, "max_tokens_field", "max_tokens max_completion_tokens");
                foreach (var field in flags.Split(' ')) Bool(block, field);
                if (block.Any(item => item.Key != "max_tokens_field")) plan.Notices.Add("ignored.compatibility");
            }
        }

        private static void Fields(JsonObject obj, string allowed, AiServiceImportPlan plan)
        {
            var names = (allowed + " extensions required_features").Split(' ').ToHashSet(StringComparer.Ordinal);
            if (obj.Any(item => !names.Contains(item.Key))) plan.Notices.Add("ignored.unknown_fields");
            if (obj.ContainsKey("extensions"))
            {
                Object(obj["extensions"], "extensions");
                plan.Notices.Add("ignored.extensions");
            }
            if (obj.ContainsKey("required_features"))
                foreach (var feature in StringArray(obj, "required_features", false))
                    Check(feature == "provider.autodiscover", "unsupported_required_feature");
            if (names.Contains("id")) Text(obj, "id");
            if (names.Contains("label")) Text(obj, "label");
        }

        private static void Schema(JsonObject obj, string schema)
        {
            Check(Text(obj, "schema", true) == schema, "schema_mismatch");
            Check(obj["version"] is JsonValue value && value.TryGetValue<int>(out var version) && version == 1, "unsupported_schema_version");
        }

        private static JsonNode Merge(JsonNode defaults, JsonNode model)
        {
            if (defaults is JsonObject source && model is JsonObject overrides)
            {
                var result = (JsonObject)source.DeepClone();
                foreach (var field in overrides) result[field.Key] = Merge(source[field.Key], field.Value);
                return result;
            }
            return (model ?? defaults)?.DeepClone();
        }

        private static void ValidateJson(JsonElement element, ref int count)
        {
            Check(++count <= 20000, "collection_limit");
            if (element.ValueKind == JsonValueKind.Object)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    ValidateUnicode(property.Name);
                    Check(keys.Add(property.Name), "duplicate_json_key");
                    Check(keys.Count <= 1000, "collection_limit");
                    ValidateJson(property.Value, ref count);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                Check(element.GetArrayLength() <= 1000, "collection_limit");
                foreach (var child in element.EnumerateArray()) ValidateJson(child, ref count);
            }
            else if (element.ValueKind == JsonValueKind.String) ValidateUnicode(element.GetString());
            else if (element.ValueKind == JsonValueKind.Number)
                Check(element.TryGetDouble(out var number) && double.IsFinite(number), "invalid_number");
        }

        private static void ValidateUnicode(string value)
        {
            for (var i = 0; i < value.Length; i++)
                if (char.IsSurrogate(value[i]))
                {
                    Check(char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]), "invalid_unicode");
                    i++;
                }
        }

        private static void Url(string text, bool discovery)
        {
            Check(text == text.Trim() && !text.Any(char.IsControl) && !text.Contains('\\') &&
                Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http") &&
                !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) && !text.Contains('#') &&
                (discovery || !text.Contains('?')), "invalid_endpoint_url");
        }

        private static JsonObject Object(JsonNode node, string field) => node as JsonObject ?? throw Error("expected_object." + field);
        private static JsonArray RequiredArray(JsonObject obj, string field) => obj[field] as JsonArray ?? throw Error("expected_array." + field);

        private static string Text(JsonObject obj, string field, bool required = false)
        {
            if (obj == null || !obj.ContainsKey(field))
            {
                Check(!required, "missing." + field);
                return null;
            }
            Check(obj[field] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text), "invalid_string." + field);
            return obj[field].GetValue<string>();
        }

        private static void Bool(JsonObject obj, string field)
        {
            if (obj.ContainsKey(field)) Check(obj[field] is JsonValue value && value.TryGetValue<bool>(out _), "invalid_boolean." + field);
        }

        private static void Enum(JsonObject obj, string field, string allowed)
        {
            var value = Text(obj, field);
            Check(value == null || allowed.Split(' ').Contains(value, StringComparer.Ordinal), "invalid_enum." + field);
        }

        private static double? Number(JsonObject obj, string field)
        {
            if (obj == null || !obj.ContainsKey(field)) return null;
            Check(obj[field] is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number), "invalid_number." + field);
            return obj[field].GetValue<double>();
        }

        private static void Numeric(JsonObject obj, string field, bool integer, double minimum, double maximum = int.MaxValue)
        {
            var number = Number(obj, field);
            Check(number == null || (number >= minimum && number <= maximum && (!integer || number == Math.Floor(number.Value))), "invalid_range." + field);
        }

        private static IEnumerable<string> StringArray(JsonObject obj, string field, bool nonEmpty)
        {
            var array = RequiredArray(obj, field);
            Check(!nonEmpty || array.Count > 0, "empty_array." + field);
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in array)
            {
                Check(item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text), "invalid_array." + field);
                Check(values.Add(item.GetValue<string>()), "duplicate_array_value." + field);
            }
            return values;
        }

        private static void Check(bool condition, string code) { if (!condition) throw Error(code); }
        private static AiServiceImportException Error(string code) => new(code);
    }
}
