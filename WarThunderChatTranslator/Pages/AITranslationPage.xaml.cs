using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Dialogs;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Services;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class AITranslationPage : Page
    {
        public ObservableCollection<AiProviderListItem> Providers { get; } = new();

        public AITranslationPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshProviders();
        }

        private void RefreshProviders()
        {
            Providers.Clear();
            var selectedId = AiProviderStore.GetSelectedProviderId();
            foreach (var provider in AiProviderStore.GetProviders())
            {
                Providers.Add(new AiProviderListItem(provider, provider.Id == selectedId));
            }

            EmptyState.Visibility = Providers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ProvidersList.Visibility = Providers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void AddProvider_Click(object sender, RoutedEventArgs e)
        {
            var provider = AiProviderStore.CreateDefault(AiApiType.OpenAIResponses);
            if (await ShowProviderDialogAsync(provider, isNew: true))
            {
                AiProviderStore.SaveProvider(provider);
                RefreshAiTranslatorIfNeeded();
                RefreshProviders();
            }
        }

        private async void EditProvider_Click(object sender, RoutedEventArgs e)
        {
            var provider = GetProviderFromSender(sender);
            if (provider == null)
            {
                return;
            }

            var editable = CloneProvider(provider);
            if (await ShowProviderDialogAsync(editable, isNew: false))
            {
                AiProviderStore.SaveProvider(editable);
                RefreshAiTranslatorIfNeeded();
                RefreshProviders();
            }
        }

        private void SelectProvider_Click(object sender, RoutedEventArgs e)
        {
            var provider = GetProviderFromSender(sender);
            if (provider == null)
            {
                return;
            }

            AiProviderStore.SetSelectedProvider(provider.Id);
            RefreshAiTranslatorIfNeeded();
            RefreshProviders();
        }

        private async void TestProvider_Click(object sender, RoutedEventArgs e)
        {
            var provider = GetProviderFromSender(sender);
            if (provider == null || sender is not Button testButton)
            {
                return;
            }

            var inputDialog = new InputDialog
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            };
            var result = await inputDialog.ShowAsync();
            if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(inputDialog.text))
            {
                return;
            }

            var originalContent = testButton.Content;
            testButton.IsEnabled = false;
            testButton.Content = CreateTestingButtonContent();

            try
            {
                var translationResult = await TranslationHelper.TestAiProviderAsync(provider, inputDialog.text);
                await TranslationTestDialogHelper.ShowResultAsync(XamlRoot, inputDialog.text, translationResult);
            }
            catch (Exception ex)
            {
                await TranslationTestDialogHelper.ShowFailureAsync(XamlRoot, ex.Message);
            }
            finally
            {
                testButton.Content = originalContent;
                testButton.IsEnabled = true;
            }
        }

        private static UIElement CreateTestingButtonContent()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };
            panel.Children.Add(new ProgressRing
            {
                Width = 16,
                Height = 16,
                IsActive = true
            });
            panel.Children.Add(new TextBlock
            {
                Text = Localization.GetString("AiTest"),
                VerticalAlignment = VerticalAlignment.Center
            });
            return panel;
        }

        private async void DeleteProvider_Click(object sender, RoutedEventArgs e)
        {
            var provider = GetProviderFromSender(sender);
            if (provider == null)
            {
                return;
            }

            var dialog = CreateDialog();
            dialog.Title = Localization.GetString("AiDeleteDialogTitle");
            dialog.Content = string.Format(Localization.GetString("AiDeleteDialogMessage"), provider.Name);
            dialog.PrimaryButtonText = Localization.GetString("AiDeleteConfirm");
            dialog.CloseButtonText = Localization.GetString("CommonCancel");
            dialog.DefaultButton = ContentDialogButton.Close;

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                AiProviderStore.DeleteProvider(provider.Id);
                RefreshAiTranslatorIfNeeded();
                RefreshProviders();
            }
        }

        private async Task<bool> ShowProviderDialogAsync(AiProviderConfig provider, bool isNew)
        {
            var apiTypeOptions = new[]
            {
                new AiApiTypeOption(AiApiType.OpenAIResponses, Localization.GetString("AiProviderTypeOpenAIResponses")),
                new AiApiTypeOption(AiApiType.OpenAIChatCompletions, Localization.GetString("AiProviderTypeOpenAIChatCompletions")),
                new AiApiTypeOption(AiApiType.Anthropic, Localization.GetString("AiProviderTypeAnthropic"))
            };
            var typeBox = new ComboBox
            {
                Header = Localization.GetString("AiApiType"),
                ItemsSource = apiTypeOptions,
                DisplayMemberPath = nameof(AiApiTypeOption.DisplayName),
                SelectedItem = apiTypeOptions.First(option => option.ApiType == provider.ApiType),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 520,
                MaxWidth = 520
            };

            var nameBox = new TextBox
            {
                Header = Localization.GetString("AiName"),
                Text = provider.Name,
                PlaceholderText = Localization.GetString("AiNamePlaceholder"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var baseUrlBox = new TextBox
            {
                Header = Localization.GetString("AiBaseUrl"),
                Text = provider.BaseUrl,
                PlaceholderText = "https://api.openai.com/v1",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var apiKeyBox = new PasswordBox
            {
                Header = Localization.GetString("AiApiKey"),
                Password = provider.ApiKey ?? string.Empty,
                PlaceholderText = Localization.GetString("AiApiKeyPlaceholder"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var apiKeyHint = new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                FontSize = 12,
                Text = Localization.GetString("AiApiKeyDescription"),
                TextWrapping = TextWrapping.Wrap
            };
            var modelBox = new TextBox
            {
                Header = Localization.GetString("AiModel"),
                Text = provider.Model,
                PlaceholderText = "gpt-4o-mini",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var temperatureToggle = new ToggleSwitch
            {
                Header = Localization.GetString("AiTemperatureEnabled"),
                IsOn = provider.UseTemperature
            };
            var temperatureBox = new NumberBox
            {
                Header = Localization.GetString("AiTemperature"),
                Value = provider.Temperature,
                Minimum = 0,
                Maximum = GetMaximumTemperature(provider.ApiType),
                SmallChange = 0.1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                IsEnabled = provider.UseTemperature,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var temperatureHint = new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                FontSize = 12,
                Text = Localization.GetString("AiTemperatureDescription"),
                TextWrapping = TextWrapping.Wrap
            };
            temperatureToggle.Toggled += (_, _) =>
            {
                temperatureBox.IsEnabled = temperatureToggle.IsOn;
            };
            var promptBox = new TextBox
            {
                Header = Localization.GetString("AiCustomPrompt"),
                Text = provider.CustomPrompt ?? string.Empty,
                PlaceholderText = Localization.GetString("AiCustomPromptPlaceholder"),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 120,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var promptHint = new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                FontSize = 12,
                Text = Localization.GetString("AiCustomPromptDescription"),
                TextWrapping = TextWrapping.Wrap
            };
            var validation = new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };

            // Use padding rather than a right margin here. A margin on ScrollViewer content can
            // extend beyond the viewport and clip the controls' right border. Padding reserves
            // space inside the content width, so the vertical scrollbar never overlaps the fields.
            var content = new StackPanel
            {
                Spacing = 12,
                Padding = new Thickness(0, 0, 28, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            content.Children.Add(typeBox);
            content.Children.Add(nameBox);
            content.Children.Add(baseUrlBox);
            content.Children.Add(apiKeyBox);
            content.Children.Add(apiKeyHint);
            content.Children.Add(modelBox);
            content.Children.Add(promptBox);
            content.Children.Add(promptHint);
            content.Children.Add(temperatureToggle);
            content.Children.Add(temperatureBox);
            content.Children.Add(temperatureHint);
            content.Children.Add(validation);

            typeBox.SelectionChanged += (_, _) =>
            {
                var type = GetSelectedApiType(typeBox);
                var defaults = AiProviderStore.CreateDefault(type);
                baseUrlBox.PlaceholderText = defaults.BaseUrl;
                modelBox.PlaceholderText = defaults.Model;
                temperatureBox.Maximum = GetMaximumTemperature(type);
                if (!double.IsNaN(temperatureBox.Value) && temperatureBox.Value > temperatureBox.Maximum)
                {
                    temperatureBox.Value = temperatureBox.Maximum;
                }

                if (isNew)
                {
                    nameBox.Text = defaults.Name;
                    baseUrlBox.Text = defaults.BaseUrl;
                    modelBox.Text = defaults.Model;
                    temperatureToggle.IsOn = defaults.UseTemperature;
                    temperatureBox.Value = defaults.Temperature;
                }
            };

            var dialogScrollViewer = new ScrollViewer
            {
                Content = content,
                MaxHeight = GetProviderDialogContentMaxHeight(),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            // Do not set Width/MaxWidth on ContentDialog itself. In WinUI that constrains the
            // overlay host and can leave the dialog visibly left-aligned. The content requests the
            // desired width, while this dialog gets a local ContentDialogMaxWidth override. Keeping
            // the override local also prevents popup controls such as ComboBox from inheriting an
            // application-wide dialog width resource.
            dialogScrollViewer.Width = GetProviderDialogContentWidth();

            var dialog = CreateDialog();
            dialog.Resources["ContentDialogMaxWidth"] = 860d;
            dialog.Title = Localization.GetString(isNew ? "AiAddDialogTitle" : "AiEditDialogTitle");
            dialog.Content = dialogScrollViewer;
            dialog.PrimaryButtonText = Localization.GetString("AiSave");
            dialog.CloseButtonText = Localization.GetString("CommonCancel");
            dialog.DefaultButton = ContentDialogButton.Primary;

            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text) ||
                    string.IsNullOrWhiteSpace(baseUrlBox.Text) ||
                    string.IsNullOrWhiteSpace(modelBox.Text) ||
                    !Uri.TryCreate(baseUrlBox.Text.Trim(), UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    validation.Text = Localization.GetString("AiValidationRequired");
                    validation.Visibility = Visibility.Visible;
                    args.Cancel = true;
                    return;
                }

                var selectedApiType = GetSelectedApiType(typeBox);
                var maximumTemperature = GetMaximumTemperature(selectedApiType);
                if (temperatureToggle.IsOn &&
                    (double.IsNaN(temperatureBox.Value) ||
                     double.IsInfinity(temperatureBox.Value) ||
                     temperatureBox.Value < 0 ||
                     temperatureBox.Value > maximumTemperature))
                {
                    validation.Text = string.Format(
                        Localization.GetString("AiTemperatureRangeError"),
                        maximumTemperature);
                    validation.Visibility = Visibility.Visible;
                    args.Cancel = true;
                    return;
                }

                provider.Name = nameBox.Text.Trim();
                provider.ApiType = selectedApiType;
                provider.BaseUrl = baseUrlBox.Text.Trim().TrimEnd('/');
                provider.ApiKey = apiKeyBox.Password.Trim();
                provider.Model = modelBox.Text.Trim();
                provider.UseTemperature = temperatureToggle.IsOn;
                provider.Temperature = double.IsNaN(temperatureBox.Value) ? 0.1 : temperatureBox.Value;
                provider.CustomPrompt = promptBox.Text?.Trim() ?? string.Empty;
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private double GetProviderDialogContentMaxHeight()
        {
            const double reservedDialogChromeHeight = 220;
            const double minimumContentHeight = 160;
            const double maximumContentHeight = 620;

            var rootHeight = XamlRoot?.Size.Height ?? 800;
            return Math.Clamp(
                rootHeight - reservedDialogChromeHeight,
                minimumContentHeight,
                maximumContentHeight);
        }

        private double GetProviderDialogContentWidth()
        {
            const double preferredContentWidth = 760;
            const double minimumContentWidth = 520;
            const double horizontalSafetyMargin = 160;

            var rootWidth = XamlRoot?.Size.Width ?? 1000;
            return Math.Clamp(
                rootWidth - horizontalSafetyMargin,
                minimumContentWidth,
                preferredContentWidth);
        }

        private ContentDialog CreateDialog()
        {
            return new ContentDialog
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            };
        }

        private AiProviderConfig GetProviderFromSender(object sender)
        {
            if (sender is not FrameworkElement element || element.Tag is not string id)
            {
                return null;
            }

            return AiProviderStore.GetProviders().FirstOrDefault(provider => provider.Id == id);
        }

        private static AiApiType GetSelectedApiType(ComboBox comboBox)
        {
            return comboBox.SelectedItem is AiApiTypeOption option
                ? option.ApiType
                : AiApiType.OpenAIResponses;
        }

        private static double GetMaximumTemperature(AiApiType apiType)
        {
            return apiType == AiApiType.Anthropic ? 1.0 : 2.0;
        }

        private static AiProviderConfig CloneProvider(AiProviderConfig provider)
        {
            return new AiProviderConfig
            {
                Id = provider.Id,
                Name = provider.Name,
                ApiType = provider.ApiType,
                BaseUrl = provider.BaseUrl,
                ApiKey = provider.ApiKey,
                Model = provider.Model,
                UseTemperature = provider.UseTemperature,
                Temperature = provider.Temperature,
                CustomPrompt = provider.CustomPrompt
            };
        }

        private static void RefreshAiTranslatorIfNeeded()
        {
            if (ApplicationConfig.GetSettings("TranslateAPI") == "AI")
            {
                TranslationHelper.UpdateTranslator();
            }
        }
    }

    internal sealed class AiApiTypeOption
    {
        public AiApiTypeOption(AiApiType apiType, string displayName)
        {
            ApiType = apiType;
            DisplayName = displayName;
        }

        public AiApiType ApiType { get; }
        public string DisplayName { get; }
    }

    public sealed class AiProviderListItem
    {
        public AiProviderListItem(AiProviderConfig provider, bool isSelected)
        {
            Id = provider.Id;
            Name = provider.Name;
            ApiTypeDisplay = Localization.GetString(provider.ApiType switch
            {
                AiApiType.OpenAIChatCompletions => "AiProviderTypeOpenAIChatCompletions",
                AiApiType.Anthropic => "AiProviderTypeAnthropic",
                _ => "AiProviderTypeOpenAIResponses"
            });
            ModelLine = $"{Localization.GetString("AiModelLabel")}: {provider.Model}";
            EndpointLine = $"{Localization.GetString("AiEndpointLabel")}: {provider.BaseUrl}";
            TemperatureLine = provider.UseTemperature
                ? $"{Localization.GetString("AiTemperature")}: {provider.Temperature:0.##}"
                : $"{Localization.GetString("AiTemperature")}: {Localization.GetString("AiTemperatureOmitted")}";
            PromptLine = string.IsNullOrWhiteSpace(provider.CustomPrompt)
                ? Localization.GetString("AiPromptDefault")
                : $"{Localization.GetString("AiPromptLabel")}: {provider.CustomPrompt.Replace('\r', ' ').Replace('\n', ' ')}";
            SelectedVisibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            CanSelect = !isSelected;
        }

        public string Id { get; }
        public string Name { get; }
        public string ApiTypeDisplay { get; }
        public string ModelLine { get; }
        public string EndpointLine { get; }
        public string TemperatureLine { get; }
        public string PromptLine { get; }
        public Visibility SelectedVisibility { get; }
        public bool CanSelect { get; }
    }
}
