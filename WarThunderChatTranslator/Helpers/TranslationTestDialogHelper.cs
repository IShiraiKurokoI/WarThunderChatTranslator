using GTranslate.Results;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace WarThunderChatTranslator.Helpers
{
    public static class TranslationTestDialogHelper
    {
        public static async Task ShowResultAsync(XamlRoot xamlRoot, string requestedSource, ITranslationResult translationResult)
        {
            var sourceLanguage = translationResult?.SourceLanguage;
            var targetLanguage = translationResult?.TargetLanguage;
            var source = translationResult?.Source ?? requestedSource ?? string.Empty;
            var translation = translationResult?.Translation ?? string.Empty;

            var message = string.Format(
                Localization.GetString("TranslationTestResultFormat"),
                source,
                translation,
                sourceLanguage?.ISO6391 ?? string.Empty,
                sourceLanguage?.Name ?? string.Empty,
                sourceLanguage?.ISO6391 ?? string.Empty,
                sourceLanguage?.ISO6393 ?? string.Empty,
                targetLanguage?.Name ?? string.Empty,
                targetLanguage?.ISO6391 ?? string.Empty,
                targetLanguage?.ISO6393 ?? string.Empty,
                translationResult?.Service ?? string.Empty);

            await ShowMessageAsync(
                xamlRoot,
                Localization.GetString("TranslationTestSuccessTitle"),
                message);
        }

        public static Task ShowFailureAsync(XamlRoot xamlRoot, string message)
        {
            return ShowMessageAsync(
                xamlRoot,
                Localization.GetString("TranslationTestFailureTitle"),
                message ?? string.Empty);
        }

        private static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = title,
                Content = new ScrollViewer
                {
                    MaxHeight = 520,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true
                    }
                },
                CloseButtonText = Localization.GetString("CommonClose")
            };

            await dialog.ShowAsync();
        }
    }
}
