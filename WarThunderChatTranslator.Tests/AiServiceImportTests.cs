using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Services;
using WarThunderChatTranslator.Translators;

namespace WarThunderChatTranslator.Tests
{
    [TestClass]
    public class AiServiceImportTests
    {
        private const string ModelJson = """
            {"schema":"llm.model","version":1,"label":"测试 🧪","model":"models/test","endpoint":{"base_url":"https://gateway.example/custom/V2","api_format":"openai_chat_completions","credential":{"type":"api_key","value":"test-secret"}},"inference":{"temperature":0,"max_output_tokens":256},"compatibility":{"max_tokens_field":"max_completion_tokens"}}
            """;

        internal static string Link(string json, string resource = "model", bool checksum = false)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            return "aiservice://" + resource + "?v=1&config=" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') +
                (checksum ? "&checksum=sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() : "");
        }

        [TestMethod]
        public void UnicodeChecksumAndExplicitZeroSurvive()
        {
            var plan = AiServiceImport.Parse(Link(ModelJson, checksum: true));
            var model = plan.Models.Single();
            Assert.IsTrue(plan.ChecksumVerified);
            Assert.AreEqual("测试 🧪", model.Name);
            Assert.AreEqual("models/test", model.Model);
            Assert.AreEqual("test-secret", model.ApiKey);
            Assert.AreEqual("https://gateway.example/custom/V2", model.BaseUrl);
            Assert.IsTrue(model.UseTemperature);
            Assert.AreEqual(0d, model.Temperature);
            Assert.AreEqual(256, model.MaxOutputTokens);
        }

        [TestMethod]
        public void CorrectPaddingAndCaseInsensitiveSchemeAreAccepted()
        {
            var link = Link(ModelJson).Replace("aiservice://model", "AISERVICE://MODEL");
            var encoded = link.Split("config=")[1];
            link += new string('=', (4 - encoded.Length % 4) % 4);
            Assert.AreEqual(1, AiServiceImport.Parse("  " + link + "  ").Models.Count);
        }

        [TestMethod]
        public void RejectsBadTransportWithoutEchoingSecrets()
        {
            var link = Link(ModelJson);
            foreach (var bad in new[]
            {
                link + "#", link.Replace("model?", "model/?"), link.Replace("model?", "user@model?"),
                link.Replace("model?", "model:1?"), link + "&v=1", link + "&%76=1", link + "&sig=secret",
                link.Replace("v=1", "v=2"), link + "\ninternal", link + "&checksum=sha256:" + new string('0', 64),
                "aiservice://model?v=1&config=e31", "aiservice://model?v=1&config=e30===",
                "aiservice://model?v=1&config=e3+", "aiservice://model?v=1&config=%2565%2533%2530",
                "aiservice://model?v=1&config=__8", "aiservice://model?v=1&config=A"
            })
            {
                var exception = Assert.ThrowsExactly<AiServiceImportException>(() => AiServiceImport.Parse(bad));
                Assert.IsFalse(exception.Message.Contains("test-secret"));
                Assert.IsFalse(exception.Message.Contains("config="));
            }
        }

        [TestMethod]
        public void RejectsInvalidSchemasAndKnownFieldTypes()
        {
            foreach (var bad in new[]
            {
                "[]", ModelJson.Replace("\"version\":1", "\"version\":2"),
                ModelJson.Replace("\"version\":1", "\"version\":1,\"version\":1"),
                ModelJson.Replace("\"temperature\":0", "\"temperature\":false"),
                ModelJson.Replace("\"temperature\":0", "\"temperature\":null"),
                ModelJson.Replace("\"temperature\":0", "\"temperature\":1e999"),
                ModelJson.Replace("\"temperature\":0", "\"temperature\":3"),
                ModelJson.Replace("openai_chat_completions", "google_vertex"),
                ModelJson.Replace("\"type\":\"api_key\"", "\"type\":\"local_file\""),
                ModelJson.Replace("https://gateway.example/custom/V2", "https://user:pass@example.com/v1"),
                ModelJson.Replace("https://gateway.example/custom/V2", "https://gateway.example/v1?"),
                ModelJson.Replace("测试 🧪", "\\uD800"),
                ModelJson.Replace("\"model\":", "\"provider_ref\":\"outside\",\"model\":")
            }) Assert.ThrowsExactly<AiServiceImportException>(() => AiServiceImport.Parse(Link(bad)));
        }

        [TestMethod]
        public void UnknownOptionalFieldsAreIgnoredButRequiredFeaturesFail()
        {
            var root = JsonNode.Parse(ModelJson);
            root["future"] = new JsonObject { ["command"] = "never execute" };
            var plan = AiServiceImport.Parse(Link(root.ToJsonString()));
            Assert.IsTrue(plan.Notices.Contains("ignored.unknown_fields"));
            root["required_features"] = new JsonArray("transport.websocket");
            Assert.ThrowsExactly<AiServiceImportException>(() => AiServiceImport.Parse(Link(root.ToJsonString())));
        }

        [TestMethod]
        public void MissingCredentialDoesNotUseExistingKey()
        {
            var root = JsonNode.Parse(ModelJson);
            root["endpoint"].AsObject().Remove("credential");
            Assert.AreEqual("", AiServiceImport.Parse(Link(root.ToJsonString())).Models.Single().ApiKey);
        }

        private static string ProviderJson(bool discovery = true) => $$$$"""
            {"schema":"llm.provider","version":1,"id":"p","endpoint":{"base_url":"https://gateway.example/prefix","api_format":"openai_chat_completions","credential":{"type":"api_key","value":"test-secret"}},"defaults":{"inference":{"temperature":0.8,"max_output_tokens":256},"transport":{"extra_headers":{"X-Custom":"header-secret"}}},"models":[{"model":"explicit","inference":{"temperature":0}}],"autodiscover":{"enabled":{{{{discovery.ToString().ToLowerInvariant()}}}}}}
            """;

        [TestMethod]
        public void ProviderAndBundleResolveDefaultsAndReferences()
        {
            var provider = JsonNode.Parse(ProviderJson(false));
            var root = new JsonObject
            {
                ["schema"] = "llm.bundle", ["version"] = 1,
                ["providers"] = new JsonArray(provider),
                ["models"] = new JsonArray(new JsonObject
                {
                    ["schema"] = "llm.model", ["version"] = 1, ["id"] = "m", ["model"] = "reference",
                    ["provider_ref"] = "p", ["inference"] = new JsonObject { ["temperature"] = 0 }
                })
            };
            var plan = AiServiceImport.Parse(Link(root.ToJsonString(), "bundle"));
            Assert.AreEqual(2, plan.Models.Count);
            Assert.AreEqual(0, plan.Discoveries.Count);
            foreach (var model in plan.Models)
            {
                Assert.AreEqual(0d, model.Temperature);
                Assert.AreEqual(256, model.MaxOutputTokens);
                Assert.AreEqual("header-secret", model.ExtraHeaders["X-Custom"]);
            }
            root["models"][0]["provider_ref"] = "missing";
            Assert.ThrowsExactly<AiServiceImportException>(() => AiServiceImport.Parse(Link(root.ToJsonString(), "bundle")));
        }

        [TestMethod]
        public void HeaderConflictsAndCapabilityOverflowsAreRejected()
        {
            var root = JsonNode.Parse(ModelJson);
            root["transport"] = new JsonObject { ["extra_headers"] = new JsonObject { ["Authorization"] = "secret" } };
            Assert.ThrowsExactly<AiServiceImportException>(() => AiServiceImport.Parse(Link(root.ToJsonString())));
            root["transport"]["extra_headers"] = new JsonObject { ["X-Foo"] = "a", ["x-foo"] = "b" };
            Assert.ThrowsExactly<AiServiceImportException>(() => AiServiceImport.Parse(Link(root.ToJsonString())));
            root["transport"]["extra_headers"] = new JsonObject { ["X-Foo"] = "a\r\nb" };
            Assert.ThrowsExactly<AiServiceImportException>(() => AiServiceImport.Parse(Link(root.ToJsonString())));
            root.AsObject().Remove("transport");
            root["capabilities"] = new JsonObject { ["max_output_tokens"] = 128 };
            Assert.ThrowsExactly<AiServiceImportException>(() => AiServiceImport.Parse(Link(root.ToJsonString())));
        }

        [TestMethod]
        public void LargePayloadSupportedAndExplicitLimitsEnforced()
        {
            var root = JsonNode.Parse(ModelJson);
            root["metadata"] = new string('a', 65536);
            Assert.AreEqual(1, AiServiceImport.Parse(Link(root.ToJsonString())).Models.Count);
            root["metadata"] = new string('a', AiServiceImport.MaxJsonBytes);
            Assert.ThrowsExactly<AiServiceImportException>(() => AiServiceImport.Parse(Link(root.ToJsonString())));
            Assert.ThrowsExactly<AiServiceImportException>(() => AiServiceImport.Parse(Link(new string('[', 33) + "0" + new string(']', 33))));
        }

        [TestMethod]
        public async Task DiscoveryUsesSameOriginCredentialsAndKeepsExplicitOverrides()
        {
            var plan = AiServiceImport.Parse(Link(ProviderJson(), "provider"));
            var requests = 0;
            using var client = new HttpClient(new Handler(request =>
            {
                requests++;
                Assert.AreEqual("https://gateway.example/prefix/models", request.RequestUri.ToString());
                Assert.AreEqual("test-secret", request.Headers.Authorization.Parameter);
                Assert.AreEqual("header-secret", request.Headers.GetValues("X-Custom").Single());
                return Json("""{"data":[{"id":"explicit"},{"id":"new","name":"New"},null,{"model":"models/kept"},"new"]}""");
            }));
            Assert.AreEqual(0, requests);
            var models = await AiServiceDiscoveryClient.ResolveAsync(plan, client);
            Assert.AreEqual(1, requests);
            Assert.AreEqual(3, models.Count);
            Assert.AreEqual(0d, models.Single(item => item.Model == "explicit").Temperature);
            Assert.AreEqual(0.8, models.Single(item => item.Model == "new").Temperature);
            Assert.IsTrue(models.Any(item => item.Model == "models/kept"));
        }

        [TestMethod]
        public async Task CrossOriginDiscoveryDoesNotLeakHeadersAndFailureKeepsModels()
        {
            var root = JsonNode.Parse(ProviderJson());
            root["autodiscover"]["url"] = "https://other.example/catalog";
            var plan = AiServiceImport.Parse(Link(root.ToJsonString(), "provider"));
            using var client = new HttpClient(new Handler(request =>
            {
                Assert.AreEqual(0, request.Headers.Count());
                return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://gateway.example/leak") } };
            }));
            var models = await AiServiceDiscoveryClient.ResolveAsync(plan, client);
            Assert.AreEqual(1, models.Count);
            Assert.IsTrue(plan.Notices.Contains("discovery.failed"));
        }

        [TestMethod]
        public async Task DiscoveryWithoutCredentialOrWhenDisabledDoesNotConnect()
        {
            using var client = new HttpClient(new Handler(_ => throw new AssertFailedException("Unexpected network access")));
            var root = JsonNode.Parse(ProviderJson());
            root["endpoint"].AsObject().Remove("credential");
            await AiServiceDiscoveryClient.ResolveAsync(AiServiceImport.Parse(Link(root.ToJsonString(), "provider")), client);
            await AiServiceDiscoveryClient.ResolveAsync(AiServiceImport.Parse(Link(ProviderJson(false), "provider")), client);
        }

        [TestMethod]
        public async Task EmptyProviderSurvivesFailureAndFillsFromDiscovery()
        {
            var root = JsonNode.Parse(ProviderJson());
            root["models"] = new JsonArray();
            var plan = AiServiceImport.Parse(Link(root.ToJsonString(), "provider"));
            Assert.AreEqual("", plan.Models.Single().Model);
            using var badClient = new HttpClient(new Handler(_ => Json("<html>error</html>")));
            Assert.AreEqual("", (await AiServiceDiscoveryClient.ResolveAsync(plan, badClient)).Single().Model);
            using var client = new HttpClient(new Handler(_ => Json("""{"models":[{"name":" discovered "}]}""")));
            Assert.AreEqual("discovered", (await AiServiceDiscoveryClient.ResolveAsync(plan, client)).Single().Model);
        }

        [TestMethod]
        public async Task ImportedConfigurationReachesActualTranslationRequest()
        {
            var root = JsonNode.Parse(ModelJson);
            root["transport"] = new JsonObject { ["extra_headers"] = new JsonObject { ["X-Route"] = "route" } };
            using var client = new HttpClient(new Handler(request =>
            {
                Assert.AreEqual("https://gateway.example/custom/V2/chat/completions", request.RequestUri.ToString());
                Assert.AreEqual("route", request.Headers.GetValues("X-Route").Single());
                var body = JsonNode.Parse(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                Assert.AreEqual(0d, body["temperature"].GetValue<double>());
                Assert.AreEqual(256, body["max_completion_tokens"].GetValue<int>());
                var result = JsonSerializer.Serialize(new { translation = "Hello", source_language = "zh", source_language_name = "Chinese", target_language = "en", target_language_name = "English" });
                return Json(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = result } } } }));
            }));
            var translator = new AiTranslator(client, AiServiceImport.Parse(Link(root.ToJsonString())).Models.Single());
            Assert.AreEqual("Hello", (await translator.TranslateAsync("你好", "en")).Translation);
        }

        [TestMethod]
        public void BatchPersistencePreservesSelectionAndExistingModelsAndIsAtomic()
        {
            ApplicationConfig.Settings.Clear();
            var directory = Path.Combine(Path.GetTempPath(), "wt-aiservice-test-" + Guid.NewGuid().ToString("N"));
            ApplicationConfig.AiProvidersFilePath = Path.Combine(directory, "ai-providers.json");
            Directory.CreateDirectory(directory);
            var existing = new AiProviderConfig { Name = "测试 🧪", Model = "old", ApiKey = "old-secret" };
            AiProviderStore.SaveProvider(existing);
            ApplicationConfig.WriteCount = 0;
            var plan = AiServiceImport.Parse(Link(ModelJson));
            AiProviderStore.ImportProviders(plan.Models);
            Assert.AreEqual(0, ApplicationConfig.WriteCount);
            Assert.AreEqual(existing.Id, AiProviderStore.GetSelectedProviderId());
            var saved = AiProviderStore.GetProviders();
            Assert.AreEqual("old-secret", saved.First().ApiKey);
            Assert.AreEqual("测试 🧪 (2)", saved.Last().Name);
            Assert.AreNotEqual(existing.Id, saved.Last().Id);
            var snapshot = File.ReadAllText(ApplicationConfig.AiProvidersFilePath);
            File.SetAttributes(ApplicationConfig.AiProvidersFilePath, FileAttributes.ReadOnly);
            try
            {
                Assert.Throws<Exception>(() => AiProviderStore.ImportProviders(plan.Models));
                Assert.AreEqual(snapshot, File.ReadAllText(ApplicationConfig.AiProvidersFilePath));
                Assert.AreEqual("测试 🧪", plan.Models.Single().Name);
            }
            finally
            {
                File.SetAttributes(ApplicationConfig.AiProvidersFilePath, FileAttributes.Normal);
                File.Delete(ApplicationConfig.AiProvidersFilePath);
                Directory.Delete(directory);
            }
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        [TestMethod]
        [DataRow("model", 1)]
        [DataRow("provider", 2)]
        [DataRow("bundle", 2)]
        public void PublishedProtocolExamplesImport(string resource, int count)
        {
            var uri = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "AiService", resource + ".uri"));
            var plan = AiServiceImport.Parse(uri);
            Assert.AreEqual(count, plan.Models.Count);
            Assert.IsTrue(plan.ChecksumVerified);
        }

        [TestMethod]
        public void LegacySettingsMigrateAndLargeCatalogsPersistWithoutOverwritingCorruptData()
        {
            var directory = Path.Combine(Path.GetTempPath(), "wt-aiservice-test-" + Guid.NewGuid().ToString("N"));
            ApplicationConfig.AiProvidersFilePath = Path.Combine(directory, "ai-providers.json");
            ApplicationConfig.Settings.Clear();
            ApplicationConfig.Settings[ApplicationConfig.AiTranslationProvidersKey] = """[{"id":"legacy","name":"Legacy","apiKey":"keep","model":"old"}]""";
            ApplicationConfig.Settings[ApplicationConfig.AiSelectedProviderIdKey] = "legacy";
            try
            {
                var models = Enumerable.Range(0, 100).Select(index => new AiProviderConfig { Name = "Model " + index, Model = "m" + index }).ToArray();
                AiProviderStore.ImportProviders(models);
                Assert.IsTrue(new FileInfo(ApplicationConfig.AiProvidersFilePath).Length > 8192);
                Assert.AreEqual(101, AiProviderStore.GetProviders().Count);
                Assert.AreEqual("keep", AiProviderStore.GetSelectedProvider().ApiKey);
                Assert.IsTrue(ApplicationConfig.Settings.ContainsKey(ApplicationConfig.AiTranslationProvidersKey));
                File.WriteAllText(ApplicationConfig.AiProvidersFilePath, "broken config");
                Assert.ThrowsExactly<JsonException>(() => AiProviderStore.ImportProviders(models));
                Assert.AreEqual("broken config", File.ReadAllText(ApplicationConfig.AiProvidersFilePath));
            }
            finally
            {
                if (File.Exists(ApplicationConfig.AiProvidersFilePath)) File.Delete(ApplicationConfig.AiProvidersFilePath);
                if (Directory.Exists(directory)) Directory.Delete(directory);
            }
        }

        [TestMethod]
        [DataRow("openai_responses", "https://api.example/custom/V2", "/custom/V2/responses", "max_output_tokens")]
        [DataRow("anthropic_messages", "https://api.example", "/v1/messages", "max_tokens")]
        [DataRow("anthropic_messages", "https://api.example/proxy/v1", "/proxy/v1/messages", "max_tokens")]
        public async Task ImportedProtocolsUseCorrectPathsAndTokenFields(string format, string baseUrl, string path, string tokenField)
        {
            var root = JsonNode.Parse(ModelJson);
            root["endpoint"]["api_format"] = format;
            root["endpoint"]["base_url"] = baseUrl;
            using var client = new HttpClient(new Handler(request =>
            {
                Assert.AreEqual(path, request.RequestUri.AbsolutePath);
                var body = JsonNode.Parse(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                Assert.AreEqual(256, body[tokenField].GetValue<int>());
                var result = JsonSerializer.Serialize(new { translation = "Hello", source_language = "zh", source_language_name = "Chinese", target_language = "en", target_language_name = "English" });
                return format == "openai_responses" ? Json(JsonSerializer.Serialize(new { output_text = result })) :
                    Json(JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = result } } }));
            }));
            var translator = new AiTranslator(client, AiServiceImport.Parse(Link(root.ToJsonString())).Models.Single());
            Assert.AreEqual("Hello", (await translator.TranslateAsync("你好", "en")).Translation);
        }

        [TestMethod]
        public async Task CancelledDiscoveryDoesNotReturnAnImportablePartialCatalog()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            using var client = new HttpClient(new Handler(_ => throw new AssertFailedException("Unexpected request")));
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => AiServiceDiscoveryClient.ResolveAsync(
                AiServiceImport.Parse(Link(ProviderJson(), "provider")), client, cancellation.Token));
        }

        private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
        }
    }
}
