#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Services.ContentFiltering;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class ContentFilterPage : Page
    {
        private readonly ContentFilterService _filterService = ContentFilterService.Shared;
        private bool _loaded;

        public ContentFilterPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _loaded = false;
            LoadSettings();
            RefreshLists();
            BuiltInDictionarySummary.Text = string.Format(
                CultureInfo.CurrentCulture,
                Localization.GetString("ContentFilterBuiltInSummary"),
                _filterService.BuiltInEnglishCount,
                _filterService.BuiltInChineseCount);
            _loaded = true;
        }

        private void LoadSettings()
        {
            ContentFilterDisplay.IsOn = ContentFilterConfig.ShouldFilterDisplay();
            ContentFilterTts.IsOn = ContentFilterConfig.ShouldFilterTts();

            var severity = ContentFilterConfig.GetMinimumSeverity();
            for (var i = 0; i < ContentFilterSeverity.Items.Count; i++)
            {
                if (ContentFilterSeverity.Items[i] is ComboBoxItem item
                    && int.TryParse(item.Tag?.ToString(), out var itemSeverity)
                    && itemSeverity == severity)
                {
                    ContentFilterSeverity.SelectedIndex = i;
                    break;
                }
            }

            var action = ContentFilterConfig.GetTtsAction();
            for (var i = 0; i < ContentFilterTtsAction.Items.Count; i++)
            {
                if (ContentFilterTtsAction.Items[i] is ComboBoxItem item
                    && string.Equals(item.Tag?.ToString(), action, StringComparison.Ordinal))
                {
                    ContentFilterTtsAction.SelectedIndex = i;
                    break;
                }
            }

            if (ContentFilterSeverity.SelectedIndex < 0)
            {
                ContentFilterSeverity.SelectedIndex = 1;
            }

            if (ContentFilterTtsAction.SelectedIndex < 0)
            {
                ContentFilterTtsAction.SelectedIndex = 0;
            }

            UpdateDependentControls();
        }

        private void ContentFilterSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded)
            {
                return;
            }

            ApplicationConfig.SaveSettings(
                ContentFilterConfig.FilterDisplayKey,
                ContentFilterDisplay.IsOn.ToString().ToLowerInvariant());
            ApplicationConfig.SaveSettings(
                ContentFilterConfig.FilterTtsKey,
                ContentFilterTts.IsOn.ToString().ToLowerInvariant());

            if (ContentFilterSeverity.SelectedItem is ComboBoxItem severityItem
                && int.TryParse(severityItem.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var severity))
            {
                ApplicationConfig.SaveSettings(
                    ContentFilterConfig.MinimumSeverityKey,
                    Math.Clamp(severity, ContentFilterConfig.MinSeverity, ContentFilterConfig.MaxSeverity)
                        .ToString(CultureInfo.InvariantCulture));
            }

            if (ContentFilterTtsAction.SelectedItem is ComboBoxItem actionItem)
            {
                ApplicationConfig.SaveSettings(
                    ContentFilterConfig.TtsActionKey,
                    actionItem.Tag?.ToString() ?? ContentFilterConfig.TtsActionRemoveMatchedTerms);
            }

            UpdateDependentControls();
            _filterService.NotifyConfigurationChanged();
        }

        private void UpdateDependentControls()
        {
            ContentFilterTtsAction.IsEnabled = ContentFilterTts.IsOn;
        }

        private void AddBlockTerm_Click(object sender, RoutedEventArgs e)
        {
            var term = BlockTermTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                ShowInfo(Localization.GetString("ContentFilterInvalidTerm"), string.Empty, InfoBarSeverity.Warning);
                return;
            }

            var severity = 2;
            if (BlockSeverityCombo.SelectedItem is ComboBoxItem severityItem)
            {
                _ = int.TryParse(severityItem.Tag?.ToString(), out severity);
            }

            var matchMode = ContentFilterMatchMode.Auto;
            if (BlockMatchCombo.SelectedItem is ComboBoxItem matchItem)
            {
                _ = Enum.TryParse(matchItem.Tag?.ToString(), ignoreCase: true, out matchMode);
            }

            _filterService.AddBlockEntry(new ContentFilterEntry
            {
                Term = term,
                Severity = Math.Clamp(severity, ContentFilterConfig.MinSeverity, ContentFilterConfig.MaxSeverity),
                MatchMode = matchMode
            });

            BlockTermTextBox.Text = string.Empty;
            RefreshLists();
        }

        private void RemoveBlockTerm_Click(object sender, RoutedEventArgs e)
        {
            if (BlocklistView.SelectedItem is not ListViewItem item || item.Tag is not ContentFilterEntry entry)
            {
                return;
            }

            _filterService.RemoveBlockEntry(entry.Term, entry.MatchMode);
            RefreshLists();
        }

        private void AddAllowTerm_Click(object sender, RoutedEventArgs e)
        {
            var term = AllowTermTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                ShowInfo(Localization.GetString("ContentFilterInvalidTerm"), string.Empty, InfoBarSeverity.Warning);
                return;
            }

            _filterService.AddAllowTerm(term);
            AllowTermTextBox.Text = string.Empty;
            RefreshLists();
        }

        private void RemoveAllowTerm_Click(object sender, RoutedEventArgs e)
        {
            if (AllowlistView.SelectedItem is not ListViewItem item || item.Tag is not string term)
            {
                return;
            }

            _filterService.RemoveAllowTerm(term);
            RefreshLists();
        }

        private async void ImportFilterPackage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".json");
                picker.FileTypeFilter.Add(".txt");
                WinRT.Interop.InitializeWithWindow.Initialize(
                    picker,
                    WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance));

                var file = await picker.PickSingleFileAsync();
                if (file is null)
                {
                    return;
                }

                var text = await FileIO.ReadTextAsync(file);
                _filterService.ImportPackage(
                    text,
                    string.Equals(file.FileType, ".txt", StringComparison.OrdinalIgnoreCase));
                RefreshLists();
                ShowInfo(Localization.GetString("ContentFilterImportSuccess"), string.Empty, InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowInfo(Localization.GetString("ContentFilterImportFailed"), ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void ExportFilterPackage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileSavePicker
                {
                    SuggestedFileName = "WarThunderChatTranslator-content-filter"
                };
                picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
                WinRT.Interop.InitializeWithWindow.Initialize(
                    picker,
                    WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance));

                var file = await picker.PickSaveFileAsync();
                if (file is null)
                {
                    return;
                }

                await FileIO.WriteTextAsync(file, _filterService.ExportPackage());
                ShowInfo(Localization.GetString("ContentFilterExportSuccess"), string.Empty, InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowInfo(Localization.GetString("ContentFilterExportFailed"), ex.Message, InfoBarSeverity.Error);
            }
        }

        private void RefreshLists()
        {
            BlocklistView.Items.Clear();
            foreach (var entry in _filterService.UserBlocklist
                         .OrderByDescending(item => item.Severity)
                         .ThenBy(item => item.Term, StringComparer.CurrentCultureIgnoreCase))
            {
                BlocklistView.Items.Add(new ListViewItem
                {
                    Content = $"{entry.Term}  ·  {GetSeverityName(entry.Severity)}  ·  {GetMatchModeName(entry.MatchMode)}",
                    Tag = entry
                });
            }

            AllowlistView.Items.Clear();
            foreach (var term in _filterService.UserAllowlist.OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase))
            {
                AllowlistView.Items.Add(new ListViewItem
                {
                    Content = term,
                    Tag = term
                });
            }
        }


        private static string GetSeverityName(int severity)
        {
            return severity switch
            {
                3 => Localization.GetString("ContentFilterLevel3"),
                2 => Localization.GetString("ContentFilterLevel2"),
                _ => Localization.GetString("ContentFilterLevel1")
            };
        }

        private static string GetMatchModeName(ContentFilterMatchMode mode)
        {
            return mode switch
            {
                ContentFilterMatchMode.Contains => Localization.GetString("ContentFilterMatchContains"),
                ContentFilterMatchMode.Word => Localization.GetString("ContentFilterMatchWord"),
                _ => Localization.GetString("ContentFilterMatchAuto")
            };
        }

        private void ShowInfo(string title, string message, InfoBarSeverity severity)
        {
            ContentFilterInfoBar.Title = title;
            ContentFilterInfoBar.Message = message;
            ContentFilterInfoBar.Severity = severity;
            ContentFilterInfoBar.IsOpen = true;
        }
    }
}
