using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MakeYourChoice
{
    public class AwsIpService
    {
        private const string IpRangesUrl = "https://ip-ranges.amazonaws.com/ip-ranges.json";
        private List<AwsCidr> _cidrs = new List<AwsCidr>();
        private readonly System.Threading.SemaphoreSlim _fetchSemaphore = new System.Threading.SemaphoreSlim(1, 1);

        private readonly struct AwsCidr
        {
            public AwsCidr(uint network, uint mask, int prefixLength, string region)
            {
                Network = network;
                Mask = mask;
                PrefixLength = prefixLength;
                Region = region;
            }

            public uint Network { get; }
            public uint Mask { get; }
            public int PrefixLength { get; }
            public string Region { get; }
        }

        private async Task<List<AwsCidr>> FetchRangesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MakeYourChoice/1.0");
                using var stream = await client.GetStreamAsync(IpRangesUrl);
                using var doc = await JsonDocument.ParseAsync(stream);

                var list = new List<AwsCidr>();
                if (doc.RootElement.TryGetProperty("prefixes", out var prefixesEl) &&
                    prefixesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in prefixesEl.EnumerateArray())
                    {
                        if (!p.TryGetProperty("ip_prefix", out var ipPrefixEl))
                        {
                            continue;
                        }

                        var ipPrefix = ipPrefixEl.GetString();
                        if (string.IsNullOrWhiteSpace(ipPrefix))
                        {
                            continue;
                        }

                        var region = p.TryGetProperty("region", out var regionEl) ? regionEl.GetString() : null;

                        if (TryParseIpv4Cidr(ipPrefix, out var network, out var mask, out var prefixLength))
                        {
                            list.Add(new AwsCidr(network, mask, prefixLength, region));
                        }
                    }
                }

                return list;
            }
            catch (Exception ex)
            {
                // Log or handle error
                Console.WriteLine($"Failed to fetch AWS IP ranges: {ex.Message}");
                return new List<AwsCidr>();
            }
        }

        public string GetRegionForIp(string ipAddress)
        {
            // Always fetch fresh ranges per request (call from background thread).
            RefreshRangesAsync().GetAwaiter().GetResult();

            if (_cidrs.Count == 0) return null;

            if (!IPAddress.TryParse(ipAddress, out var ip)) return null;

            var ipBytes = ip.GetAddressBytes();
            if (ipBytes.Length != 4) return null;

            uint ipVal = (uint)((ipBytes[0] << 24) | (ipBytes[1] << 16) | (ipBytes[2] << 8) | ipBytes[3]);

            // Prefer the most specific (longest prefix) match when multiple ranges overlap.
            AwsCidr? best = null;
            foreach (var cidr in _cidrs)
            {
                if ((ipVal & cidr.Mask) == cidr.Network)
                {
                    if (best == null || cidr.PrefixLength > best.Value.PrefixLength)
                    {
                        best = cidr;
                    }
                }
            }
            return best.HasValue ? GetPrettyRegionName(best.Value.Region) : null;
        }

        private async Task RefreshRangesAsync()
        {
            await _fetchSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                _cidrs = await FetchRangesAsync().ConfigureAwait(false);
            }
            finally
            {
                _fetchSemaphore.Release();
            }
        }

        private bool TryParseIpv4Cidr(string cidr, out uint network, out uint mask, out int prefixLength)
        {
            network = 0;
            mask = 0;
            prefixLength = -1;

            if (string.IsNullOrWhiteSpace(cidr)) return false;

            var parts = cidr.Split('/');
            if (parts.Length != 2) return false;

            if (!IPAddress.TryParse(parts[0], out var baseIp)) return false;
            if (!int.TryParse(parts[1], out prefixLength)) return false;
            if (prefixLength < 0 || prefixLength > 32) return false;

            var bytes = baseIp.GetAddressBytes();
            if (bytes.Length != 4) return false;

            uint ipVal = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
            mask = prefixLength == 0 ? 0 : 0xFFFFFFFF << (32 - prefixLength);
            network = ipVal & mask;
            return true;
        }

        public static string GetPrettyRegionName(string regionCode)
        {
            // Map aws region codes to readable names matching the app's style if possible
            return regionCode switch
            {
                "us-east-1" => "US East (N. Virginia)",
                "us-east-2" => "US East (Ohio)",
                "us-west-1" => "US West (N. California)",
                "us-west-2" => "US West (Oregon)",
                "ca-central-1" => "Canada (Central)",
                "sa-east-1" => "South America (São Paulo)",
                "eu-west-1" => "Europe (Ireland)",
                "eu-west-2" => "Europe (London)",
                "eu-central-1" => "Europe (Frankfurt am Main)",
                "eu-north-1" => "Europe (Stockholm)",
                "eu-west-3" => "Europe (Paris)",
                "eu-south-1" => "Europe (Milan)",
                "ap-northeast-1" => "Asia Pacific (Tokyo)",
                "ap-northeast-2" => "Asia Pacific (Seoul)",
                "ap-south-1" => "Asia Pacific (Mumbai)",
                "ap-southeast-1" => "Asia Pacific (Singapore)",
                "ap-southeast-2" => "Asia Pacific (Sydney)",
                "ap-east-1" => "Asia Pacific (Hong Kong)",
                "af-south-1" => "Africa (Cape Town)",
                "me-south-1" => "Middle East (Bahrain)",
                "ap-northeast-3" => "Asia Pacific (Osaka)",
                _ => regionCode // Fallback
            };
        }
    }

}
