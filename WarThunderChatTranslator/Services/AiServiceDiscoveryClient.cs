using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Entities;

namespace WarThunderChatTranslator.Services
{
    public static class AiServiceDiscoveryClient
    {
        public const int MaxResponseBytes = 2 * 1024 * 1024;

        // Call only after the user applies the preview. The caller's handler must disable
        // redirects and cookies; an injected client also makes network behavior testable.
        public static async Task<IReadOnlyList<AiProviderConfig>> ResolveAsync(
            AiServiceImportPlan plan, HttpClient client, CancellationToken cancellationToken = default)
        {
            var models = plan.Models.ToList();
            foreach (var discovery in plan.Discoveries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var template = discovery.Template;
                if (string.IsNullOrEmpty(template.ApiKey))
                {
                    plan.Notices.Add("discovery.credentials_needed");
                    continue;
                }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(template.TimeoutSeconds ?? 30, 30)));
                try
                {
                    var target = new Uri(discovery.Url);
                    var origin = new Uri(template.BaseUrl);
                    using var request = new HttpRequestMessage(HttpMethod.Get, target);
                    if (target.Scheme == origin.Scheme && target.IdnHost == origin.IdnHost && target.Port == origin.Port)
                    {
                        foreach (var header in template.ExtraHeaders)
                            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        if (template.ApiType == AiApiType.Anthropic)
                        {
                            request.Headers.TryAddWithoutValidation("x-api-key", template.ApiKey);
                            if (!request.Headers.Contains("anthropic-version"))
                                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                        }
                        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", template.ApiKey);
                    }
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException();
                    if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new InvalidDataException();
                    using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int read;
                    while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) != 0)
                    {
                        if (buffer.Length + read > MaxResponseBytes) throw new InvalidDataException();
                        buffer.Write(chunk, 0, read);
                    }
                    using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
                    var root = document.RootElement;
                    var entries = root;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (!root.TryGetProperty("data", out entries) || entries.ValueKind != JsonValueKind.Array)
                            root.TryGetProperty("models", out entries);
                        if (root.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True ||
                            root.TryGetProperty("next_page_token", out _) || root.TryGetProperty("next", out _))
                            plan.Notices.Add("discovery.incomplete");
                    }
                    if (entries.ValueKind != JsonValueKind.Array) throw new InvalidDataException();
                    var known = discovery.Models.Select(item => item.Model).ToHashSet(StringComparer.Ordinal);
                    var added = 0;
                    var inspected = 0;
                    foreach (var entry in entries.EnumerateArray())
                    {
                        if (++inspected > 1000 || models.Count >= AiServiceImport.MaxModels)
                        {
                            plan.Notices.Add("discovery.incomplete");
                            break;
                        }
                        var id = entry.ValueKind == JsonValueKind.String ? entry.GetString() :
                            ReadText(entry, "id") ?? ReadText(entry, "model") ?? ReadText(entry, "name");
                        id = id?.Trim();
                        if (string.IsNullOrEmpty(id)) { plan.Notices.Add("discovery.skipped_entries"); continue; }
                        if (!known.Add(id)) continue;
                        // Only the catalog ID/label is accepted from remote metadata.
                        var model = JsonSerializer.Deserialize<AiProviderConfig>(JsonSerializer.Serialize(template));
                        model.Id = Guid.NewGuid().ToString("N");
                        model.Model = id;
                        model.Name = template.Name + " / " + (ReadText(entry, "name") ?? id);
                        models.Add(model);
                        added++;
                    }
                    if (added > 0)
                    {
                        foreach (var placeholder in discovery.Models.Where(item => item.Model.Length == 0)) models.Remove(placeholder);
                        plan.Notices.Add("discovery.models_added");
                    }
                    else plan.Notices.Add("discovery.no_new_models");
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is JsonException ||
                                           ex is OperationCanceledException || ex is FormatException || ex is InvalidOperationException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // No response bodies, URLs, credentials or exception messages enter diagnostics.
                    plan.Notices.Add("discovery.failed");
                }
            }
            return models;
        }

        private static string ReadText(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;
        }
    }
}
