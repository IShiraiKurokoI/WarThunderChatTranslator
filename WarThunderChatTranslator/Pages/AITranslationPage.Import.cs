using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Services;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class AITranslationPage
    {
        private async void ImportAiService_Click(object sender, RoutedEventArgs e)
        {
            AiServiceImportPlan plan = null;
            var input = new PasswordBox
            {
                Header = "aiservice://",
                PlaceholderText = Localization.GetString("AiImportPaste"),
                MaxLength = AiServiceImport.MaxUriLength + 1
            };
            var error = ImportText("");
            var panel = new StackPanel { Spacing = 12, Width = GetProviderDialogContentWidth() };
            panel.Children.Add(ImportText(Localization.GetString("AiImportDescription")));
            panel.Children.Add(input);
            panel.Children.Add(error);
            var inputDialog = CreateDialog();
            inputDialog.Resources["ContentDialogMaxWidth"] = 860d;
            inputDialog.Title = Localization.GetString("AiImportButton");
            inputDialog.Content = panel;
            inputDialog.PrimaryButtonText = Localization.GetString("AiImportPreview");
            inputDialog.CloseButtonText = Localization.GetString("CommonCancel");
            inputDialog.PrimaryButtonClick += (_, args) =>
            {
                try { plan = AiServiceImport.Parse(input.Password); }
                catch (AiServiceImportException ex)
                {
                    error.Text = Localization.GetString("AiImportInvalid") + "\n" + ex.Message;
                    args.Cancel = true;
                }
            };
            var result = await inputDialog.ShowAsync();
            input.Password = string.Empty;
            if (result != ContentDialogResult.Primary || plan == null) return;

            var preview = ImportText(BuildImportPreview(plan));
            var status = ImportText("");
            var content = new StackPanel { Spacing = 12, Padding = new Thickness(0, 0, 28, 0) };
            content.Children.Add(preview);
            content.Children.Add(status);
            var dialog = CreateDialog();
            dialog.Resources["ContentDialogMaxWidth"] = 860d;
            dialog.Title = Localization.GetString("AiImportPreview");
            dialog.Content = new ScrollViewer
            {
                Content = content, Width = GetProviderDialogContentWidth(),
                MaxHeight = GetProviderDialogContentMaxHeight(),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            dialog.PrimaryButtonText = Localization.GetString("AiImportApply");
            dialog.CloseButtonText = Localization.GetString("CommonCancel");
            dialog.DefaultButton = ContentDialogButton.Close;
            using var cancellation = new CancellationTokenSource();
            var imported = false;
            var importing = false;
            var importedCount = 0;
            dialog.Closing += (_, _) => { if (!imported) cancellation.Cancel(); };
            dialog.CloseButtonClick += (_, _) => cancellation.Cancel();
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                args.Cancel = true;
                if (importing) return;
                importing = true;
                dialog.IsPrimaryButtonEnabled = false;
                var deferral = args.GetDeferral();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                deadline.CancelAfter(TimeSpan.FromMinutes(2));
                status.Text = Localization.GetString("AiImportWorking");
                try
                {
                    using var client = new HttpClient(CreateDiscoveryHandler()) { Timeout = Timeout.InfiniteTimeSpan };
                    var models = await AiServiceDiscoveryClient.ResolveAsync(plan, client, deadline.Token);
                    deadline.Token.ThrowIfCancellationRequested();
                    AiProviderStore.ImportProviders(models.ToArray());
                    importedCount = models.Count;
                    imported = true;
                    args.Cancel = false;
                }
                catch (OperationCanceledException)
                {
                    status.Text = Localization.GetString("AiImportCancelled");
                }
                catch (Exception)
                {
                    // Settings/network exception messages can contain secret configuration.
                    status.Text = Localization.GetString("AiImportSaveFailed");
                }
                finally
                {
                    importing = false;
                    dialog.IsPrimaryButtonEnabled = !cancellation.IsCancellationRequested;
                    deferral.Complete();
                }
            };
            await dialog.ShowAsync();
            if (!imported) return;
            RefreshProviders();
            var completed = CreateDialog();
            completed.Title = Localization.GetString("AiImportButton");
            completed.Content = ImportText(string.Format(Localization.GetString("AiImportDone"), importedCount) +
                "\n\n" + string.Join("\n", plan.Notices.Where(note => note.StartsWith("discovery.", StringComparison.Ordinal)).Select(ImportNotice)));
            completed.CloseButtonText = Localization.GetString("CommonClose");
            await completed.ShowAsync();
        }

        private static HttpClientHandler CreateDiscoveryHandler()
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            var mode = ApplicationConfig.GetSettings(ApplicationConfig.NetworkProxyModeKey);
            handler.UseProxy = mode is "System" or "Custom";
            if (mode == "Custom")
            {
                handler.Proxy = new WebProxy(ApplicationConfig.GetSettings(ApplicationConfig.ProxyAddressKey))
                {
                    Credentials = new NetworkCredential(ApplicationConfig.GetSettings(ApplicationConfig.ProxyAccountKey),
                        ApplicationConfig.GetSettings(ApplicationConfig.ProxyPasswordKey))
                };
            }
            return handler;
        }

        private static TextBlock ImportText(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
        private static string ImportNotice(string note) => Localization.GetString("AiImportNotice_" + note.Replace('.', '_'));

        private static string BuildImportPreview(AiServiceImportPlan plan)
        {
            var text = new StringBuilder();
            text.AppendLine(Localization.GetString("AiImportTarget"));
            text.AppendLine(Localization.GetString(plan.ChecksumVerified ? "AiImportChecksumPassed" : "AiImportChecksumMissing"));
            var names = new HashSet<string>(AiProviderStore.GetProviders().Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var model in plan.Models)
            {
                var name = model.Name;
                for (var suffix = 2; !names.Add(name); suffix++) name = $"{model.Name} ({suffix})";
                text.AppendLine();
                text.AppendLine(name);
                text.AppendLine("base_url: " + model.BaseUrl);
                text.AppendLine("api_format: " + (model.ApiType switch
                {
                    AiApiType.OpenAIResponses => "openai_responses",
                    AiApiType.Anthropic => "anthropic_messages",
                    _ => "openai_chat_completions"
                }));
                text.AppendLine(Localization.GetString("AiModel") + ": " + model.Model);
                text.AppendLine(Localization.GetString(string.IsNullOrEmpty(model.ApiKey) ? "AiImportPromptKey" : "AiImportHasKey"));
                if (model.UseTemperature) text.AppendLine("temperature: " + model.Temperature);
                if (model.MaxOutputTokens.HasValue) text.AppendLine("max_output_tokens: " + model.MaxOutputTokens);
                if (model.TimeoutSeconds.HasValue) text.AppendLine("timeout_seconds: " + model.TimeoutSeconds);
                if (model.ExtraHeaders.Count > 0) text.AppendLine(Localization.GetString("AiImportHeaders") + ": " + model.ExtraHeaders.Count);
            }
            foreach (var discovery in plan.Discoveries)
                text.AppendLine("\nautodiscover: " + discovery.Url);
            text.AppendLine();
            foreach (var notice in plan.Notices.OrderBy(item => item, StringComparer.Ordinal)) text.AppendLine(ImportNotice(notice));
            return text.ToString();
        }
    }
}
