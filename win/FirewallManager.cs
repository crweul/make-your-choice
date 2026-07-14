using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MakeYourChoice
{
    /// <summary>
    /// "Hard region lock" — blocks the game-server DATA PLANE (UDP) to unwanted AWS regions using
    /// Windows Firewall, while leaving the TCP control plane (EAC, matchmaking, startup) untouched.
    ///
    /// The hosts file can only block DNS for the GameLift latency beacons and service endpoint; it
    /// can't stop DBD connecting to a game server it has already been placed on, because that
    /// connection is to a raw EC2 IP over UDP (~7777-7820), not a hostname. DBD's server-side
    /// fallback to N. Virginia ignores the client's latency reports, so the only client-side way to
    /// NOT play there is to make those game servers unreachable. We block outbound UDP 7770-7820
    /// (ping beacon + game-server range) to the region IP ranges; EAC/matchmaking use TCP 443 and
    /// are never touched, so the game still launches. A blocked-region match simply can't connect.
    /// </summary>
    public static class FirewallManager
    {
        public const string RuleGroup = "MakeYourChoice RegionLock";
        private const string BlockedUdpPorts = "7770-7820";
        private const int ChunkSize = 1000; // CIDRs per firewall rule

        public static async Task<(bool ok, string message)> ApplyLockAsync(
            AwsIpService aws, ISet<string> blockRegionCodes, string gameProgramPath = null)
        {
            if (blockRegionCodes == null || blockRegionCodes.Count == 0)
            {
                // Nothing to block (e.g. every region is allowed) -> ensure no stale lock remains.
                RemoveLock();
                return (true, "No regions to block; any existing lock was removed.");
            }

            try
            {
                var cidrs = await aws.GetCidrStringsForRegionsAsync(blockRegionCodes);
                if (cidrs.Count == 0)
                    return (false, "Could not fetch AWS IP ranges (no internet?). No changes were made.");

                // Scope the block to the DBD executable when we know it, so ONLY the game is blocked
                // from the unchosen regions — not the whole PC. This also lets Make Your Choice's own
                // beacon probes reach those regions (a global rule would block them too).
                bool scoped = !string.IsNullOrWhiteSpace(gameProgramPath) && File.Exists(gameProgramPath);

                var (code, err) = await Task.Run(() => RunPowerShellScript(BuildApplyScript(cidrs, scoped ? gameProgramPath : null)));
                if (code != 0)
                {
                    RemoveLock();
                    return (false, $"Failed to create firewall rules (run as Administrator?). {err}".Trim());
                }
                var scopeMsg = scoped ? "the DBD game only" : "all apps (set your game folder to limit this to DBD)";
                return (true,
                    $"blocked UDP {BlockedUdpPorts} to {cidrs.Count} IP ranges across {blockRegionCodes.Count} region(s) for {scopeMsg}");
            }
            catch (Exception ex)
            {
                return (false, $"Error applying lock: {ex.Message}");
            }
        }

        private static string BuildApplyScript(List<string> cidrs, string gameProgramPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine($"Remove-NetFirewallRule -Group '{RuleGroup}' -ErrorAction SilentlyContinue");
            // PowerShell single-quoted literal; escape any embedded single quotes by doubling them.
            string programClause = string.IsNullOrEmpty(gameProgramPath)
                ? ""
                : $"-Program '{gameProgramPath.Replace("'", "''")}' ";
            int ruleNum = 0;
            for (int i = 0; i < cidrs.Count; i += ChunkSize)
            {
                ruleNum++;
                var chunk = cidrs.Skip(i).Take(ChunkSize).Select(c => "'" + c + "'");
                sb.AppendLine($"$a{ruleNum} = @({string.Join(",", chunk)})");
                sb.AppendLine(
                    $"New-NetFirewallRule -DisplayName '{RuleGroup} {ruleNum}' -Group '{RuleGroup}' " +
                    $"-Description 'Blocks DBD game-server UDP traffic to unchosen AWS regions.' " +
                    $"-Direction Outbound -Action Block -Protocol UDP -RemotePort {BlockedUdpPorts} " +
                    programClause +
                    $"-RemoteAddress $a{ruleNum} | Out-Null");
            }
            return sb.ToString();
        }

        public static bool IsLockActive()
        {
            var (code, _) = RunPowerShellCommand(
                $"if (Get-NetFirewallRule -Group '{RuleGroup}' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}");
            return code == 0;
        }

        public static void RemoveLock()
        {
            RunPowerShellCommand($"Remove-NetFirewallRule -Group '{RuleGroup}' -ErrorAction SilentlyContinue");
        }

        private static (int code, string stderr) RunPowerShellScript(string script)
        {
            string path = Path.Combine(Path.GetTempPath(), "myc_regionlock_" + Guid.NewGuid().ToString("N") + ".ps1");
            try
            {
                File.WriteAllText(path, script, new UTF8Encoding(false));
                return RunPowerShell($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{path}\"");
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
            finally
            {
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }

        private static (int code, string stderr) RunPowerShellCommand(string command)
        {
            return RunPowerShell(
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"");
        }

        private static (int code, string stderr) RunPowerShell(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return (-1, "could not start powershell");
                string err = p.StandardError.ReadToEnd();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return (p.ExitCode, err);
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }
    }
}
