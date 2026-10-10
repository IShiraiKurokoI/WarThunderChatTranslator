using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
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
        private bool _updatingLanAccess;
        private IReadOnlyList<Uri> _lanDashboardUris = Array.Empty<Uri>();

        public RunSettingsPage()
        {
            InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _loaded = true;
            await RefreshLanDashboardAccessAsync();
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
            LanAuthenticationEnabled.IsOn = ApplicationConfig.GetBooleanSetting(ApplicationConfig.DashboardLanAuthenticationEnabledKey);
        }

        private async Task RefreshLanDashboardAccessAsync()
        {
            if (_updatingLanAccess)
            {
                return;
            }

            _updatingLanAccess = true;
            try
            {
                if (Application.Current is not App app)
                {
                    SetLanDashboardUnavailable();
                    return;
                }

                var authenticationEnabled = ApplicationConfig.GetBooleanSetting(ApplicationConfig.DashboardLanAuthenticationEnabledKey);
                LanTokenPanel.Visibility = authenticationEnabled ? Visibility.Visible : Visibility.Collapsed;
                LanAccessTokenText.Text = authenticationEnabled ? app.DashboardAccessToken : string.Empty;

                var previousHost = (LanDashboardAddress.SelectedItem as ComboBoxItem)?.Tag is Uri previousUri
                    ? previousUri.Host
                    : null;
                _lanDashboardUris = app.GetLanDashboardUris(authenticationEnabled);

                LanDashboardAddress.Items.Clear();
                foreach (var uri in _lanDashboardUris)
                {
                    LanDashboardAddress.Items.Add(new ComboBoxItem
                    {
                        Content = uri.GetLeftPart(UriPartial.Authority),
                        Tag = uri
                    });
                }

                if (_lanDashboardUris.Count == 0)
                {
                    SetLanDashboardUnavailable();
                    return;
                }

                var selectedIndex = 0;
                if (!string.IsNullOrWhiteSpace(previousHost))
                {
                    for (var index = 0; index < _lanDashboardUris.Count; index++)
                    {
                        if (string.Equals(_lanDashboardUris[index].Host, previousHost, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedIndex = index;
                            break;
                        }
                    }
                }

                LanDashboardAddress.SelectedIndex = selectedIndex;
                await UpdateLanDashboardQrAsync(_lanDashboardUris[selectedIndex]);
            }
            finally
            {
                _updatingLanAccess = false;
            }
        }

        private void SetLanDashboardUnavailable()
        {
            LanDashboardUrlText.Text = Localization.GetString("LanDashboardUnavailable");
            LanQrCodeImage.Source = null;
            if (LanDashboardAddress.Items.Count == 0)
            {
                LanDashboardAddress.PlaceholderText = Localization.GetString("LanDashboardUnavailable");
            }
        }

        private async Task UpdateLanDashboardQrAsync(Uri uri)
        {
            LanDashboardUrlText.Text = uri.ToString();

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(uri.ToString(), QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            var pngBytes = qrCode.GetGraphic(8, drawQuietZones: true);

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(pngBytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            LanQrCodeImage.Source = bitmap;
        }

        private async void LanAuthenticationEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loaded || _updatingLanAccess)
            {
                return;
            }

            ApplicationConfig.SaveSettings(
                ApplicationConfig.DashboardLanAuthenticationEnabledKey,
                LanAuthenticationEnabled.IsOn.ToString().ToLowerInvariant());
            await RefreshLanDashboardAccessAsync();
        }

        private async void LanDashboardAddress_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingLanAccess || LanDashboardAddress.SelectedItem is not ComboBoxItem item || item.Tag is not Uri uri)
            {
                return;
            }

            await UpdateLanDashboardQrAsync(uri);
        }

        private async void LanAccessRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshLanDashboardAccessAsync();
        }

        private void LanAccessCopyUrl_Click(object sender, RoutedEventArgs e)
        {
            CopyTextToClipboard(LanDashboardUrlText.Text);
        }

        private void LanAccessCopyToken_Click(object sender, RoutedEventArgs e)
        {
            CopyTextToClipboard(LanAccessTokenText.Text);
        }

        private async void LanAccessRegenerateToken_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is App app)
            {
                app.RegenerateDashboardAccessToken();
                await RefreshLanDashboardAccessAsync();
            }
        }

        private static void CopyTextToClipboard(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
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
