using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class RunSettingsPage : Page
    {
        private bool _loaded;
        private bool _firewallActionInProgress;
        private FirewallRuleState _firewallRuleState = FirewallRuleState.CheckFailed;
        private bool _hasManagedFirewallRules;

        public RunSettingsPage()
        {
            InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _loaded = true;
            await RefreshFirewallStatusAsync();
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

        private async Task RefreshFirewallStatusAsync()
        {
            if (_firewallActionInProgress)
            {
                return;
            }

            FirewallStatusText.Text = Localization.GetString("FirewallStatusChecking");
            FirewallStatusDetailText.Text = string.Empty;
            FirewallRuleButton.IsEnabled = false;
            FirewallRemoveButton.IsEnabled = false;

            try
            {
                if (Application.Current is not App app)
                {
                    SetFirewallStatus(FirewallRuleState.CheckFailed, 0, Localization.GetString("FirewallStatusCheckFailedDetail"), false);
                    return;
                }

                var status = await app.GetFirewallStatusAsync();
                SetFirewallStatus(status.State, status.Port, status.Detail, status.HasManagedRules);
            }
            catch (Exception ex)
            {
                SetFirewallStatus(FirewallRuleState.CheckFailed, 0, ex.Message, false);
            }
        }

        private void SetFirewallStatus(FirewallRuleState state, int port, string technicalDetail, bool hasManagedRules)
        {
            _firewallRuleState = state;
            _hasManagedFirewallRules = hasManagedRules;
            FirewallRuleButton.Visibility = state == FirewallRuleState.Allowed ? Visibility.Collapsed : Visibility.Visible;
            FirewallRemoveButton.Visibility = hasManagedRules ? Visibility.Visible : Visibility.Collapsed;
            FirewallRuleButton.IsEnabled = !_firewallActionInProgress && state != FirewallRuleState.ServiceNotRunning;
            FirewallRemoveButton.IsEnabled = !_firewallActionInProgress && hasManagedRules;

            switch (state)
            {
                case FirewallRuleState.Allowed:
                    FirewallStatusText.Text = Localization.GetString("FirewallStatusAllowed");
                    FirewallStatusDetailText.Text = string.Format(
                        CultureInfo.CurrentCulture,
                        Localization.GetString("FirewallStatusAllowedDetailFormat"),
                        port);
                    break;

                case FirewallRuleState.Missing:
                    FirewallStatusText.Text = Localization.GetString("FirewallStatusMissing");
                    FirewallStatusDetailText.Text = string.Format(
                        CultureInfo.CurrentCulture,
                        Localization.GetString("FirewallStatusMissingDetailFormat"),
                        port);
                    SetFirewallRuleButtonAppearance(Localization.GetString("FirewallAllowButton"), Symbol.Add);
                    break;

                case FirewallRuleState.Outdated:
                    FirewallStatusText.Text = Localization.GetString("FirewallStatusOutdated");
                    FirewallStatusDetailText.Text = string.Format(
                        CultureInfo.CurrentCulture,
                        Localization.GetString("FirewallStatusOutdatedDetailFormat"),
                        port);
                    SetFirewallRuleButtonAppearance(Localization.GetString("FirewallRepairButton"), Symbol.Refresh);
                    break;

                case FirewallRuleState.ServiceNotRunning:
                    FirewallStatusText.Text = Localization.GetString("FirewallStatusServiceNotRunning");
                    FirewallStatusDetailText.Text = Localization.GetString("FirewallStatusServiceNotRunningDetail");
                    SetFirewallRuleButtonAppearance(Localization.GetString("FirewallAllowButton"), Symbol.Add);
                    break;

                default:
                    FirewallStatusText.Text = Localization.GetString("FirewallStatusCheckFailed");
                    FirewallStatusDetailText.Text = string.IsNullOrWhiteSpace(technicalDetail)
                        ? Localization.GetString("FirewallStatusCheckFailedDetail")
                        : string.Format(
                            CultureInfo.CurrentCulture,
                            Localization.GetString("FirewallStatusCheckFailedWithDetailFormat"),
                            technicalDetail);
                    SetFirewallRuleButtonAppearance(Localization.GetString("FirewallRepairButton"), Symbol.Refresh);
                    break;
            }
        }

        private void SetFirewallRuleButtonAppearance(string text, Symbol icon)
        {
            FirewallRuleButtonText.Text = text;
            FirewallRuleButtonIcon.Symbol = icon;
        }

        private async void FirewallRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_firewallActionInProgress || Application.Current is not App app)
            {
                return;
            }

            var port = app.LocalDashboardPort;
            if (port <= 0)
            {
                SetFirewallStatus(FirewallRuleState.ServiceNotRunning, 0, null, false);
                return;
            }

            bool isAdmin;
            try
            {
                isAdmin = FirewallHelper.IsAdministrator();
            }
            catch
            {
                isAdmin = false;
            }

            // Standard-user launches never trigger UAC by themselves. The user first
            // sees why elevation is needed and explicitly chooses to continue.
            if (!isAdmin)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = Localization.GetString("FirewallUacDialogTitle"),
                    Content = string.Format(
                        CultureInfo.CurrentCulture,
                        Localization.GetString("FirewallUacDialogContentFormat"),
                        port),
                    PrimaryButtonText = Localization.GetString("FirewallUacContinue"),
                    CloseButtonText = Localization.GetString("CommonCancel"),
                    DefaultButton = ContentDialogButton.Primary
                };

                var dialogResult = await dialog.ShowAsync();
                if (dialogResult != ContentDialogResult.Primary)
                {
                    return;
                }
            }

            _firewallActionInProgress = true;
            FirewallRuleButton.IsEnabled = false;
            FirewallRemoveButton.IsEnabled = false;
            FirewallStatusText.Text = Localization.GetString("FirewallStatusApplying");
            FirewallStatusDetailText.Text = Localization.GetString("FirewallStatusApplyingDetail");

            try
            {
                var result = await app.RepairFirewallRuleAsync(requestElevation: !isAdmin);
                switch (result.State)
                {
                    case FirewallOperationState.Succeeded:
                        ApplicationConfig.SaveSettings(
                            ApplicationConfig.LanFirewallRuleDisabledByUserKey,
                            bool.FalseString.ToLowerInvariant());
                        _firewallActionInProgress = false;
                        await RefreshFirewallStatusAsync();
                        return;

                    case FirewallOperationState.Cancelled:
                        FirewallStatusText.Text = Localization.GetString("FirewallUacCancelled");
                        FirewallStatusDetailText.Text = Localization.GetString("FirewallUacCancelledDetail");
                        break;

                    default:
                        FirewallStatusText.Text = Localization.GetString("FirewallRepairFailed");
                        FirewallStatusDetailText.Text = string.IsNullOrWhiteSpace(result.Detail)
                            ? Localization.GetString("FirewallRepairFailedDetail")
                            : string.Format(
                                CultureInfo.CurrentCulture,
                                Localization.GetString("FirewallStatusCheckFailedWithDetailFormat"),
                                result.Detail);
                        break;
                }
            }
            catch (Exception ex)
            {
                FirewallStatusText.Text = Localization.GetString("FirewallRepairFailed");
                FirewallStatusDetailText.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    Localization.GetString("FirewallStatusCheckFailedWithDetailFormat"),
                    ex.Message);
            }
            finally
            {
                _firewallActionInProgress = false;
                FirewallRuleButton.IsEnabled = _firewallRuleState != FirewallRuleState.ServiceNotRunning;
                FirewallRemoveButton.IsEnabled = _hasManagedFirewallRules;
            }
        }

        private async void FirewallRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_firewallActionInProgress || Application.Current is not App app)
            {
                return;
            }

            bool isAdmin;
            try
            {
                isAdmin = FirewallHelper.IsAdministrator();
            }
            catch
            {
                isAdmin = false;
            }

            var contentKey = isAdmin
                ? "FirewallRemoveDialogContent"
                : "FirewallRemoveDialogContentWithUac";
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Localization.GetString("FirewallRemoveDialogTitle"),
                Content = Localization.GetString(contentKey),
                PrimaryButtonText = Localization.GetString("FirewallRemoveButton"),
                CloseButtonText = Localization.GetString("CommonCancel"),
                DefaultButton = ContentDialogButton.Close
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            _firewallActionInProgress = true;
            FirewallRuleButton.IsEnabled = false;
            FirewallRemoveButton.IsEnabled = false;
            FirewallStatusText.Text = Localization.GetString("FirewallStatusRemoving");
            FirewallStatusDetailText.Text = Localization.GetString("FirewallStatusRemovingDetail");

            try
            {
                var result = await app.RemoveFirewallRulesAsync(requestElevation: !isAdmin);
                switch (result.State)
                {
                    case FirewallOperationState.Succeeded:
                        ApplicationConfig.SaveSettings(
                            ApplicationConfig.LanFirewallRuleDisabledByUserKey,
                            bool.TrueString.ToLowerInvariant());
                        _firewallActionInProgress = false;
                        await RefreshFirewallStatusAsync();
                        return;

                    case FirewallOperationState.Cancelled:
                        FirewallStatusText.Text = Localization.GetString("FirewallUacCancelled");
                        FirewallStatusDetailText.Text = Localization.GetString("FirewallUacCancelledDetail");
                        break;

                    default:
                        FirewallStatusText.Text = Localization.GetString("FirewallRemoveFailed");
                        FirewallStatusDetailText.Text = string.IsNullOrWhiteSpace(result.Detail)
                            ? Localization.GetString("FirewallRemoveFailedDetail")
                            : string.Format(
                                CultureInfo.CurrentCulture,
                                Localization.GetString("FirewallStatusCheckFailedWithDetailFormat"),
                                result.Detail);
                        break;
                }
            }
            catch (Exception ex)
            {
                FirewallStatusText.Text = Localization.GetString("FirewallRemoveFailed");
                FirewallStatusDetailText.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    Localization.GetString("FirewallStatusCheckFailedWithDetailFormat"),
                    ex.Message);
            }
            finally
            {
                _firewallActionInProgress = false;
                FirewallRuleButton.IsEnabled = _firewallRuleState != FirewallRuleState.ServiceNotRunning;
                FirewallRemoveButton.IsEnabled = _hasManagedFirewallRules;
            }
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
