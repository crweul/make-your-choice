using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MakeYourChoice
{
    /// <summary>
    /// "Hard region lock" — blocks the game-server DATA PLANE (UDP) to unwanted AWS regions using
    /// Windows Firewall, while leaving the TCP control plane (EAC, matchmaking, startup) untouched.
    ///
    /// Why this exists: the hosts file can only block DNS for the GameLift latency beacons and the
    /// service endpoint. It cannot stop DBD from connecting to a game server it has already been
    /// placed on, because that connection is made to a raw EC2 IP over UDP (ports ~7777-7820), not
    /// to a hostname. DBD's fallback to N. Virginia (us-east-1) is decided server-side and ignores
    /// the client's latency reports, so the only client-side way to NOT play on Virginia is to make
    /// its game servers unreachable. We block outbound UDP 7770-7820 to the region's IP ranges:
    ///   - 7770 = GameLift ping beacon (so the client can't even report latency for the region)
    ///   - 7777-7820 = the actual game-server port range (so a fallback match can't connect)
    /// EAC and matchmaking use TCP 443, which we never touch, so the game still starts normally.
    /// If DBD is placed on a blocked region the connection simply fails and DBD re-queues / errors
    /// instead of dropping you into that region's match.
    /// </summary>
    public static class FirewallManager
    {
        public const string RuleGroup = "MakeYourChoice RegionLock";
        private const string BlockedUdpPorts = "7770-7820";
        private const int ChunkSize = 250; // CIDRs per firewall rule (keeps the command line small)

        public static async Task<(bool ok, string message)> ApplyLockAsync(
            AwsIpService aws, ISet<string> blockRegionCodes)
        {
            if (blockRegionCodes == null || blockRegionCodes.Count == 0)
                return (false, "No regions to block were specified.");

            try
            {
                var cidrs = await aws.GetCidrStringsForRegionsAsync(blockRegionCodes);
                if (cidrs.Count == 0)
                    return (false, "Could not fetch AWS IP ranges (no internet?). No changes were made.");

                // Replace any previous lock so we never stack stale rules.
                RemoveLock();

                int idx = 0, ruleNum = 0;
                while (idx < cidrs.Count)
                {
                    var chunk = cidrs.Skip(idx).Take(ChunkSize).ToList();
                    idx += ChunkSize;
                    ruleNum++;
                    var addrs = string.Join(",", chunk);
                    var name = $"{RuleGroup} {ruleNum}";
                    var ps = $"New-NetFirewallRule -DisplayName '{name}' -Group '{RuleGroup}' " +
                             $"-Description 'Blocks DBD game-server UDP traffic to unwanted AWS regions.' " +
                             $"-Direction Outbound -Action Block -Protocol UDP " +
                             $"-RemotePort {BlockedUdpPorts} -RemoteAddress {addrs} | Out-Null";
                    var (code, err) = RunPowerShell(ps);
                    if (code != 0)
                    {
                        RemoveLock();
                        return (false, $"Failed to create firewall rule (run as Administrator?). {err}".Trim());
                    }
                }

                return (true,
                    $"Hard lock applied.\nBlocked UDP {BlockedUdpPorts} to {cidrs.Count} IP ranges " +
                    $"across {blockRegionCodes.Count} region(s).\n\nEAC / matchmaking (TCP 443) are untouched, " +
                    "so the game still starts. A blocked-region match simply won't connect.");
            }
            catch (Exception ex)
            {
                return (false, $"Error applying lock: {ex.Message}");
            }
        }

        public static bool IsLockActive()
        {
            var (code, _) = RunPowerShell(
                $"if (Get-NetFirewallRule -Group '{RuleGroup}' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}");
            return code == 0;
        }

        public static void RemoveLock()
        {
            RunPowerShell($"Remove-NetFirewallRule -Group '{RuleGroup}' -ErrorAction SilentlyContinue");
        }

        private static (int code, string stderr) RunPowerShell(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \""
                                + command.Replace("\"", "\\\"") + "\"",
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
