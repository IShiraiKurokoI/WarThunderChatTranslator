using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Configurations;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class RunSettingsPage : Page
    {
        private bool _loaded;

        public RunSettingsPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _loaded = true;
        }

        private void LoadSettings()
        {
            GamePollingInterval.Value = ApplicationConfig.GetPollingIntervalSeconds(ApplicationConfig.GamePollingIntervalSecondsKey);
            WebPollingInterval.Value = ApplicationConfig.GetPollingIntervalSeconds(ApplicationConfig.WebPollingIntervalSecondsKey);

            BattleChatClearMode.SelectedIndex = ApplicationConfig.GetBattleChatClearMode() switch
            {
                ApplicationConfig.BattleChatClearModeNone => 0,
                ApplicationConfig.BattleChatClearModePhysical => 2,
                _ => 1
            };

            PhysicalChatCacheLimit.Value = ApplicationConfig.GetPhysicalChatCacheLimit();
            OpenOverlayOnStartup.IsOn = ApplicationConfig.GetBooleanSetting(ApplicationConfig.OpenOverlayOnStartupKey);
            OpenDashboardOnStartup.IsOn = ApplicationConfig.GetBooleanSetting(ApplicationConfig.OpenDashboardOnStartupKey);
        }

        private void GamePollingInterval_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            SavePollingInterval(sender, args.NewValue, ApplicationConfig.GamePollingIntervalSecondsKey);
        }

        private void WebPollingInterval_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            SavePollingInterval(sender, args.NewValue, ApplicationConfig.WebPollingIntervalSecondsKey);
        }

        private void SavePollingInterval(NumberBox numberBox, double newValue, string settingKey)
        {
            if (!_loaded) return;

            if (double.IsNaN(newValue))
            {
                numberBox.Value = ApplicationConfig.GetPollingIntervalSeconds(settingKey);
                return;
            }

            var seconds = Math.Clamp(
                (int)Math.Round(newValue, MidpointRounding.AwayFromZero),
                ApplicationConfig.MinPollingIntervalSeconds,
                ApplicationConfig.MaxPollingIntervalSeconds);

            if (Math.Abs(numberBox.Value - seconds) > double.Epsilon)
            {
                numberBox.Value = seconds;
            }

            ApplicationConfig.SaveSettings(settingKey, seconds.ToString(CultureInfo.InvariantCulture));
        }

        private void BattleChatClearMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded || BattleChatClearMode.SelectedItem is not ComboBoxItem selectedItem) return;

            var mode = selectedItem.Tag?.ToString();
            if (mode != ApplicationConfig.BattleChatClearModeNone
                && mode != ApplicationConfig.BattleChatClearModeLogical
                && mode != ApplicationConfig.BattleChatClearModePhysical)
            {
                return;
            }

            ApplicationConfig.SaveSettings(ApplicationConfig.BattleChatClearModeKey, mode);
        }

        private void PhysicalChatCacheLimit_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (!_loaded) return;

            if (double.IsNaN(args.NewValue))
            {
                sender.Value = ApplicationConfig.GetPhysicalChatCacheLimit();
                return;
            }

            var limit = Math.Clamp(
                (int)Math.Round(args.NewValue, MidpointRounding.AwayFromZero),
                ApplicationConfig.MinPhysicalChatCacheLimit,
                ApplicationConfig.MaxPhysicalChatCacheLimit);

            if (Math.Abs(sender.Value - limit) > double.Epsilon)
            {
                sender.Value = limit;
            }

            ApplicationConfig.SaveSettings(
                ApplicationConfig.PhysicalChatCacheLimitKey,
                limit.ToString(CultureInfo.InvariantCulture));
        }

        private void OpenOverlayOnStartup_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;

            ApplicationConfig.SaveSettings(
                ApplicationConfig.OpenOverlayOnStartupKey,
                OpenOverlayOnStartup.IsOn.ToString().ToLowerInvariant());
        }

        private void OpenDashboardOnStartup_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;

            ApplicationConfig.SaveSettings(
                ApplicationConfig.OpenDashboardOnStartupKey,
                OpenDashboardOnStartup.IsOn.ToString().ToLowerInvariant());
        }
    }
}
