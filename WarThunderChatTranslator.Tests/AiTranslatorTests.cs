using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Translators;

namespace WarThunderChatTranslator.Tests
{
    [TestClass]
    public sealed class AiTranslatorTests
    {
        [TestMethod]
        public async Task OpenAiResponses_UsesTextFormatJsonSchema_OmitsTemperature_AndParsesTranslationResult()
        {
            Uri requestUri = null;
            AuthenticationHeaderValue authorization = null;
            string requestBody = null;

            var handler = new MockHttpMessageHandler(async request =>
            {
                requestUri = request.RequestUri;
                authorization = request.Headers.Authorization;
                requestBody = await request.Content.ReadAsStringAsync();

                var translationJson = JsonSerializer.Serialize(new
                {
                    translation = "攻击 D 点！",
                    source_language = "en",
                    source_language_name = "English",
                    target_language = "zh-CN",
                    target_language_name = "Simplified Chinese"
                });

                return JsonResponse(new
                {
                    id = "resp_mock",
                    @object = "response",
                    status = "completed",
                    output = new object[]
                    {
                        new
                        {
                            type = "message",
                            role = "assistant",
                            content = new object[]
                            {
                                new
                                {
                                    type = "output_text",
                                    text = translationJson,
                                    annotations = Array.Empty<object>()
                                }
                            }
                        }
                    }
                });
            });

            using var client = new HttpClient(handler);
            var provider = new AiProviderConfig
            {
                Name = "Mock OpenAI Responses",
                ApiType = AiApiType.OpenAIResponses,
                BaseUrl = "https://openai.example/v1",
                ApiKey = "openai-test-key",
                Model = "mock-gpt",
                UseTemperature = false,
                Temperature = 0.8,
                CustomPrompt = "Prefer concise military game terminology. Target={target_language}; Source={source_language}; Input={text}"
            };
            var translator = new AiTranslator(client, provider);
            var targetLanguage = new AiLanguage("Simplified Chinese", "zh-CN");

            var result = await translator.TranslateAsync("Attack the D point!", targetLanguage);

            Assert.AreEqual("https://openai.example/v1/responses", requestUri.ToString());
            Assert.AreEqual("Bearer", authorization.Scheme);
            Assert.AreEqual("openai-test-key", authorization.Parameter);

            using (var requestJson = JsonDocument.Parse(requestBody))
            {
                var root = requestJson.RootElement;
                Assert.AreEqual("mock-gpt", root.GetProperty("model").GetString());
                Assert.IsFalse(root.TryGetProperty("temperature", out _));
                Assert.IsFalse(root.TryGetProperty("response_format", out _));

                var format = root.GetProperty("text").GetProperty("format");
                Assert.AreEqual("json_schema", format.GetProperty("type").GetString());
                Assert.AreEqual("translation_result", format.GetProperty("name").GetString());
                Assert.IsTrue(format.GetProperty("strict").GetBoolean());
                AssertTranslationSchema(format.GetProperty("schema"));

                var instructions = root.GetProperty("instructions").GetString();
                StringAssert.Contains(instructions, "Prefer concise military game terminology.");
                StringAssert.Contains(instructions, "Target=zh-CN");
                StringAssert.Contains(instructions, "Source=auto");
                StringAssert.Contains(instructions, "Input=Attack the D point!");
                StringAssert.Contains(instructions, "\"target_language\": \"zh-CN\"");

                var input = root.GetProperty("input").GetString();
                StringAssert.Contains(input, "Attack the D point!");
                StringAssert.Contains(input, "Simplified Chinese (zh-CN)");
            }

            Assert.AreEqual("攻击 D 点！", result.Translation);
            Assert.AreEqual("Attack the D point!", result.Source);
            Assert.AreEqual("en", result.SourceLanguage.ISO6391);
            Assert.AreEqual("zh-CN", result.TargetLanguage.ISO6391);
            Assert.AreEqual("AI / Mock OpenAI Responses", result.Service);
        }

        [TestMethod]
        public async Task OpenAiChatCompletions_UsesResponseFormatJsonSchema_SendsOptionalTemperature_AndParsesTranslationResult()
        {
            Uri requestUri = null;
            AuthenticationHeaderValue authorization = null;
            string requestBody = null;

            var handler = new MockHttpMessageHandler(async request =>
            {
                requestUri = request.RequestUri;
                authorization = request.Headers.Authorization;
                requestBody = await request.Content.ReadAsStringAsync();

                using (var requestJson = JsonDocument.Parse(requestBody))
                {
                    var responseFormatType = requestJson.RootElement
                        .GetProperty("response_format")
                        .GetProperty("type")
                        .GetString();

                    if (!string.Equals(responseFormatType, "json_schema", StringComparison.Ordinal))
                    {
                        return new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent(
                                "{\"error\":{\"message\":\"'response_format.type' must be 'json_schema' or 'text'\"}}",
                                Encoding.UTF8,
                                "application/json")
                        };
                    }
                }

                var translationJson = JsonSerializer.Serialize(new
                {
                    translation = "攻击 D 点！",
                    source_language = "en",
                    source_language_name = "English",
                    target_language = "zh-CN",
                    target_language_name = "Simplified Chinese"
                });

                return JsonResponse(new
                {
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                role = "assistant",
                                content = translationJson
                            }
                        }
                    }
                });
            });

            using var client = new HttpClient(handler);
            var provider = new AiProviderConfig
            {
                Name = "Mock OpenAI Chat",
                ApiType = AiApiType.OpenAIChatCompletions,
                BaseUrl = "https://openai-compatible.example/v1",
                ApiKey = "chat-test-key",
                Model = "mock-chat-model",
                UseTemperature = true,
                Temperature = 0.35
            };
            var translator = new AiTranslator(client, provider);
            var targetLanguage = new AiLanguage("Simplified Chinese", "zh-CN");

            var result = await translator.TranslateAsync("Attack the D point!", targetLanguage);

            Assert.AreEqual("https://openai-compatible.example/v1/chat/completions", requestUri.ToString());
            Assert.AreEqual("Bearer", authorization.Scheme);
            Assert.AreEqual("chat-test-key", authorization.Parameter);

            using (var requestJson = JsonDocument.Parse(requestBody))
            {
                var root = requestJson.RootElement;
                Assert.AreEqual("mock-chat-model", root.GetProperty("model").GetString());
                Assert.AreEqual(0.35, root.GetProperty("temperature").GetDouble(), 0.000001);
                Assert.IsFalse(root.TryGetProperty("text", out _));

                var responseFormat = root.GetProperty("response_format");
                Assert.AreEqual("json_schema", responseFormat.GetProperty("type").GetString());

                var jsonSchema = responseFormat.GetProperty("json_schema");
                Assert.AreEqual("translation_result", jsonSchema.GetProperty("name").GetString());
                Assert.IsTrue(jsonSchema.GetProperty("strict").GetBoolean());
                AssertTranslationSchema(jsonSchema.GetProperty("schema"));

                var messages = root.GetProperty("messages");
                Assert.AreEqual("system", messages[0].GetProperty("role").GetString());
                Assert.AreEqual("user", messages[1].GetProperty("role").GetString());
            }

            Assert.AreEqual("攻击 D 点！", result.Translation);
            Assert.AreEqual("en", result.SourceLanguage.ISO6391);
            Assert.AreEqual("zh-CN", result.TargetLanguage.ISO6391);
            Assert.AreEqual("AI / Mock OpenAI Chat", result.Service);
        }

        [TestMethod]
        public async Task AnthropicMessages_UsesOutputConfigJsonSchema_SendsOptionalTemperature_AndParsesTranslationResult()
        {
            Uri requestUri = null;
            string apiKey = null;
            string anthropicVersion = null;
            string requestBody = null;

            var handler = new MockHttpMessageHandler(async request =>
            {
                requestUri = request.RequestUri;
                apiKey = GetHeader(request, "x-api-key");
                anthropicVersion = GetHeader(request, "anthropic-version");
                requestBody = await request.Content.ReadAsStringAsync();

                var translationJson = JsonSerializer.Serialize(new
                {
                    translation = "Capture point A.",
                    source_language = "ru",
                    source_language_name = "Russian",
                    target_language = "en",
                    target_language_name = "English"
                });

                return JsonResponse(new
                {
                    id = "msg_mock",
                    type = "message",
                    role = "assistant",
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = translationJson
                        }
                    }
                });
            });

            using var client = new HttpClient(handler);
            var provider = new AiProviderConfig
            {
                Name = "Mock Anthropic",
                ApiType = AiApiType.Anthropic,
                BaseUrl = "https://anthropic.example/v1",
                ApiKey = "anthropic-test-key",
                Model = "mock-claude",
                UseTemperature = true,
                Temperature = 0.4
            };
            var translator = new AiTranslator(client, provider);
            var sourceLanguage = new AiLanguage("Russian", "ru");
            var targetLanguage = new AiLanguage("English", "en");

            var result = await translator.TranslateAsync("Захватите точку A.", targetLanguage, sourceLanguage);

            Assert.AreEqual("https://anthropic.example/v1/messages", requestUri.ToString());
            Assert.AreEqual("anthropic-test-key", apiKey);
            Assert.AreEqual("2023-06-01", anthropicVersion);

            using (var requestJson = JsonDocument.Parse(requestBody))
            {
                var root = requestJson.RootElement;
                Assert.AreEqual("mock-claude", root.GetProperty("model").GetString());
                Assert.AreEqual(0.4, root.GetProperty("temperature").GetDouble(), 0.000001);
                Assert.AreEqual("json_schema", root.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
                AssertTranslationSchema(root.GetProperty("output_config").GetProperty("format").GetProperty("schema"));

                var systemPrompt = root.GetProperty("system").GetString();
                StringAssert.Contains(systemPrompt, "\"target_language\": \"en\"");

                var userPrompt = root.GetProperty("messages")[0].GetProperty("content").GetString();
                StringAssert.Contains(userPrompt, "The source language is Russian (ru).");
                StringAssert.Contains(userPrompt, "Захватите точку A.");
            }

            Assert.AreEqual("Capture point A.", result.Translation);
            Assert.AreEqual("Захватите точку A.", result.Source);
            Assert.AreEqual("ru", result.SourceLanguage.ISO6391);
            Assert.AreEqual("en", result.TargetLanguage.ISO6391);
            Assert.AreEqual("AI / Mock Anthropic", result.Service);
        }

        private static void AssertTranslationSchema(JsonElement schema)
        {
            Assert.AreEqual("object", schema.GetProperty("type").GetString());
            Assert.IsFalse(schema.GetProperty("additionalProperties").GetBoolean());

            var required = schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray();
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "translation",
                    "source_language",
                    "source_language_name",
                    "target_language",
                    "target_language_name"
                },
                required);
        }

        private static HttpResponseMessage JsonResponse(object payload)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
        }

        private static string GetHeader(HttpRequestMessage request, string name)
        {
            return request.Headers.TryGetValues(name, out var values)
                ? values.SingleOrDefault()
                : null;
        }

        private sealed class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

            public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _handler(request);
            }
        }
    }
}
