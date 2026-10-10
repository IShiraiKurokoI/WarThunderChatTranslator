#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WarThunderChatTranslator.Helpers
{
    internal enum FirewallRuleState
    {
        ServiceNotRunning,
        Allowed,
        Missing,
        Outdated,
        CheckFailed
    }

    internal sealed record FirewallRuleStatus(
        FirewallRuleState State,
        int Port,
        string ExecutablePath,
        string? Detail = null,
        bool HasManagedRules = false);

    internal enum FirewallOperationState
    {
        Succeeded,
        Cancelled,
        Failed
    }

    internal sealed record FirewallOperationResult(FirewallOperationState State, string? Detail = null);

    /// <summary>
    /// Manages the optional inbound Windows Firewall rule used by the LAN dashboard.
    /// Reading rule state does not request elevation. Creating or repairing a rule can
    /// be elevated independently, so the main application can continue to run normally.
    ///
    /// Status detection deliberately distinguishes "our preferred rule shape" from
    /// "Windows already allows the traffic". If Windows Security, Group Policy, or the
    /// user has already created an enabled inbound allow rule for the current executable
    /// that covers the Dashboard TCP port on the active network profile, LAN access is
    /// considered allowed and the user is not asked to repair a perfectly usable rule.
    /// </summary>
    internal static class FirewallHelper
    {
        public const string RuleName = "WarThunderChatTranslator LAN";
        private const string RuleDisplayNamePattern = "WarThunderChatTranslator*";

        public static bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static async Task<FirewallRuleStatus> CheckAsync(int port, CancellationToken cancellationToken = default)
        {
            var executablePath = GetExecutablePath();
            if (port <= 0)
            {
                return new FirewallRuleStatus(FirewallRuleState.ServiceNotRunning, port, executablePath);
            }

            try
            {
                var script = BuildStatusScript(executablePath);
                var result = await RunPowerShellAsync(script, elevate: false, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0)
                {
                    return new FirewallRuleStatus(
                        FirewallRuleState.CheckFailed,
                        port,
                        executablePath,
                        string.IsNullOrWhiteSpace(result.StandardError) ? $"PowerShell exited with code {result.ExitCode}." : result.StandardError.Trim());
                }

                using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(result.StandardOutput)
                    ? "{\"ManagedItems\":[],\"ApplicationItems\":[],\"ActiveProfiles\":[]}" : result.StandardOutput);

                var root = document.RootElement;
                var managedItems = GetArray(root, "ManagedItems");
                var applicationItems = GetArray(root, "ApplicationItems");
                var activeProfiles = GetStringArray(root, "ActiveProfiles");
                var hasManagedRules = managedItems.Count > 0;

                // Prefer actual usability over our own preferred rule layout. Windows can
                // create broader application rules from the native "Allow access" prompt
                // (for example Program + Any protocol/port). Those rules are already
                // sufficient and should not be reported as needing repair.
                var hasEffectiveAllowRule = applicationItems.Any(item =>
                    RuleAllowsCurrentLanTraffic(item, port, executablePath, activeProfiles));

                if (hasEffectiveAllowRule)
                {
                    return new FirewallRuleStatus(
                        FirewallRuleState.Allowed,
                        port,
                        executablePath,
                        "An enabled Windows Firewall inbound allow rule already covers the current executable and Dashboard port.",
                        hasManagedRules);
                }

                // No effective rule for the current process. If an app-owned/legacy rule
                // still exists, it is stale (commonly after a Store update changed the
                // executable path); otherwise LAN firewall access is simply unconfigured.
                return hasManagedRules
                    ? new FirewallRuleStatus(FirewallRuleState.Outdated, port, executablePath, null, true)
                    : new FirewallRuleStatus(FirewallRuleState.Missing, port, executablePath, null, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new FirewallRuleStatus(FirewallRuleState.CheckFailed, port, executablePath, ex.Message);
            }
        }

        public static async Task<FirewallOperationResult> RepairAsync(
            int port,
            bool requestElevation,
            CancellationToken cancellationToken = default)
        {
            if (port <= 0)
            {
                return new FirewallOperationResult(FirewallOperationState.Failed, "The local HTTP service is not running.");
            }

            var executablePath = GetExecutablePath();
            if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
            {
                return new FirewallOperationResult(FirewallOperationState.Failed, "The current executable path could not be determined.");
            }

            try
            {
                var script = BuildRepairScript(port, executablePath);
                var result = await RunPowerShellAsync(script, requestElevation, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    return new FirewallOperationResult(FirewallOperationState.Succeeded);
                }

                var detail = !string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardError.Trim()
                    : $"PowerShell exited with code {result.ExitCode}.";
                return new FirewallOperationResult(FirewallOperationState.Failed, detail);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED: the user declined or closed the UAC prompt.
                return new FirewallOperationResult(FirewallOperationState.Cancelled);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new FirewallOperationResult(FirewallOperationState.Failed, ex.Message);
            }
        }

        public static async Task<FirewallOperationResult> RemoveAsync(
            bool requestElevation,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var script = BuildRemoveScript();
                var result = await RunPowerShellAsync(script, requestElevation, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    return new FirewallOperationResult(FirewallOperationState.Succeeded);
                }

                var detail = !string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardError.Trim()
                    : $"PowerShell exited with code {result.ExitCode}.";
                return new FirewallOperationResult(FirewallOperationState.Failed, detail);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new FirewallOperationResult(FirewallOperationState.Cancelled);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new FirewallOperationResult(FirewallOperationState.Failed, ex.Message);
            }
        }

        private static bool RuleAllowsCurrentLanTraffic(
            JsonElement item,
            int port,
            string executablePath,
            IReadOnlyCollection<string> activeProfiles)
        {
            var enabled = GetString(item, "Enabled");
            var direction = GetString(item, "Direction");
            var action = GetString(item, "Action");
            var profile = GetString(item, "Profile");
            var program = GetString(item, "Program");
            var protocol = GetString(item, "Protocol");
            var localPort = GetString(item, "LocalPort");
            var remoteAddress = GetString(item, "RemoteAddress");

            return string.Equals(enabled, "True", StringComparison.OrdinalIgnoreCase)
                && string.Equals(direction, "Inbound", StringComparison.OrdinalIgnoreCase)
                && string.Equals(action, "Allow", StringComparison.OrdinalIgnoreCase)
                && string.Equals(program, executablePath, StringComparison.OrdinalIgnoreCase)
                && ProtocolCoversTcp(protocol)
                && PortCovers(localPort, port)
                && RemoteAddressCoversLocalSubnet(remoteAddress)
                && ProfileCoversActiveNetwork(profile, activeProfiles);
        }

        private static bool ProtocolCoversTcp(string protocol)
        {
            return string.Equals(protocol, "TCP", StringComparison.OrdinalIgnoreCase)
                || string.Equals(protocol, "6", StringComparison.OrdinalIgnoreCase)
                || string.Equals(protocol, "Any", StringComparison.OrdinalIgnoreCase)
                || string.Equals(protocol, "256", StringComparison.OrdinalIgnoreCase);
        }

        private static bool PortCovers(string ports, int port)
        {
            if (string.IsNullOrWhiteSpace(ports))
            {
                return false;
            }

            return ports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(x => string.Equals(x, "Any", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x, "*", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x, port.ToString(), StringComparison.Ordinal)
                    || PortRangeContains(x, port));
        }

        private static bool PortRangeContains(string value, int port)
        {
            var dash = value.IndexOf('-');
            if (dash <= 0 || dash >= value.Length - 1)
            {
                return false;
            }

            return int.TryParse(value.AsSpan(0, dash), out var start)
                && int.TryParse(value.AsSpan(dash + 1), out var end)
                && port >= start
                && port <= end;
        }

        private static bool RemoteAddressCoversLocalSubnet(string remoteAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteAddress))
            {
                return false;
            }

            return remoteAddress.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(x => string.Equals(x, "Any", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x, "*", StringComparison.OrdinalIgnoreCase)
                    || x.StartsWith("LocalSubnet", StringComparison.OrdinalIgnoreCase));
        }

        private static bool ProfileCoversActiveNetwork(string profile, IReadOnlyCollection<string> activeProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile))
            {
                return false;
            }

            var ruleProfiles = profile.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ruleProfiles.Any(x => string.Equals(x, "Any", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // If Windows could not report an active network category, stay conservative:
            // only an Any-profile rule can be considered definitely sufficient.
            if (activeProfiles.Count == 0)
            {
                return false;
            }

            return activeProfiles.Any(active => ruleProfiles.Any(rule =>
                string.Equals(rule, active, StringComparison.OrdinalIgnoreCase)));
        }

        private static List<JsonElement> GetArray(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return new List<JsonElement>();
            }

            return element.EnumerateArray().Select(x => x.Clone()).ToList();
        }

        private static IReadOnlyCollection<string> GetStringArray(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return element.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string GetString(JsonElement item, string propertyName)
        {
            if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                return string.Empty;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
        }

        private static string GetExecutablePath()
        {
            try
            {
                return Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule?.FileName
                    ?? string.Empty;
            }
            catch
            {
                return Environment.ProcessPath ?? string.Empty;
            }
        }

        private static string BuildStatusScript(string executablePath)
        {
            var escapedPath = EscapePowerShellSingleQuoted(executablePath);
            var escapedPattern = EscapePowerShellSingleQuoted(RuleDisplayNamePattern);

            return $$"""
                $ErrorActionPreference = 'Stop'
                $exe = '{{escapedPath}}'

                function Convert-Rule($rule) {
                    $app = $rule | Get-NetFirewallApplicationFilter
                    $port = $rule | Get-NetFirewallPortFilter
                    $address = $rule | Get-NetFirewallAddressFilter
                    [pscustomobject]@{
                        DisplayName = [string]$rule.DisplayName
                        Enabled = [string]$rule.Enabled
                        Direction = [string]$rule.Direction
                        Action = [string]$rule.Action
                        Profile = [string]$rule.Profile
                        Program = [string]$app.Program
                        Package = [string]$app.Package
                        Protocol = [string]$port.Protocol
                        LocalPort = [string]($port.LocalPort -join ',')
                        RemoteAddress = [string]($address.RemoteAddress -join ',')
                    }
                }

                $managedRules = @(Get-NetFirewallRule -DisplayName '{{escapedPattern}}' -ErrorAction SilentlyContinue)
                $managedItems = @($managedRules | ForEach-Object { Convert-Rule $_ })

                # Query by application filter instead of display name. This also finds rules
                # created by the native Windows "Allow access" dialog or by an administrator.
                $applicationRules = @(
                    Get-NetFirewallApplicationFilter -PolicyStore ActiveStore -Program $exe -ErrorAction SilentlyContinue |
                        Get-NetFirewallRule -ErrorAction SilentlyContinue
                )
                $applicationItems = @($applicationRules | ForEach-Object { Convert-Rule $_ })

                $activeProfiles = @()
                try {
                    $activeProfiles = @(
                        Get-NetConnectionProfile -ErrorAction Stop |
                            Where-Object { $_.IPv4Connectivity -ne 'Disconnected' -or $_.IPv6Connectivity -ne 'Disconnected' } |
                            ForEach-Object {
                                $category = [string]$_.NetworkCategory
                                if ($category -eq 'DomainAuthenticated') { 'Domain' } else { $category }
                            } |
                            Sort-Object -Unique
                    )
                } catch {
                    $activeProfiles = @()
                }

                [pscustomobject]@{
                    ManagedItems = $managedItems
                    ApplicationItems = $applicationItems
                    ActiveProfiles = $activeProfiles
                } | ConvertTo-Json -Compress -Depth 6
                """;
        }

        private static string BuildRepairScript(int port, string executablePath)
        {
            var escapedPath = EscapePowerShellSingleQuoted(executablePath);
            var escapedRuleName = EscapePowerShellSingleQuoted(RuleName);
            var escapedPattern = EscapePowerShellSingleQuoted(RuleDisplayNamePattern);
            var description = EscapePowerShellSingleQuoted(
                $"Allows War Thunder Chat Translator to receive dashboard connections from devices on the local subnet on TCP port {port}.");

            return $$"""
                $ErrorActionPreference = 'Stop'
                Get-NetFirewallRule -DisplayName '{{escapedPattern}}' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
                New-NetFirewallRule -DisplayName '{{escapedRuleName}}' `
                    -Description '{{description}}' `
                    -Direction Inbound `
                    -Action Allow `
                    -Enabled True `
                    -Profile Domain,Private `
                    -Program '{{escapedPath}}' `
                    -Protocol TCP `
                    -LocalPort {{port}} `
                    -RemoteAddress LocalSubnet | Out-Null
                """;
        }

        private static string BuildRemoveScript()
        {
            var escapedPattern = EscapePowerShellSingleQuoted(RuleDisplayNamePattern);
            return $$"""
                $ErrorActionPreference = 'Stop'
                Get-NetFirewallRule -DisplayName '{{escapedPattern}}' -ErrorAction SilentlyContinue |
                    Remove-NetFirewallRule -ErrorAction Stop
                """;
        }

        private static string EscapePowerShellSingleQuoted(string value) => value.Replace("'", "''", StringComparison.Ordinal);

        private static async Task<PowerShellResult> RunPowerShellAsync(
            string script,
            bool elevate,
            CancellationToken cancellationToken)
        {
            // -EncodedCommand avoids command-line quoting problems for package paths and localized text.
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = elevate,
                Verb = elevate ? "runas" : string.Empty,
                CreateNoWindow = !elevate,
                RedirectStandardOutput = !elevate,
                RedirectStandardError = !elevate
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            if (elevate)
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return new PowerShellResult(process.ExitCode, string.Empty, string.Empty);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new PowerShellResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
        }

        private readonly record struct PowerShellResult(int ExitCode, string StandardOutput, string StandardError);
    }
}
