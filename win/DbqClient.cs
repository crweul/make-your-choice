using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MakeYourChoice
{
    /// <summary>
    /// Client for the public Dead by Queue API (https://www.deadbyqueue.com), used to show real
    /// server online/offline status and killer queue times.
    ///
    ///   GET /regions                  -> {"regions":{"us-east-2":false,"eu-west-2":true, ...}}
    ///                                    true = region online, false = offline / ramped down
    ///   GET /queuetime?region=<code>  -> "Killer: 10m24s | Survivor: 12s"  (plain text)
    ///
    /// NOTE: the queue endpoint returns "HTTP 500" for browser-like ("Mozilla/...") User-Agents,
    /// so we send a simple product token instead.
    /// </summary>
    public static class DbqClient
    {
        private const string RegionsUrl = "https://api.deadbyqueue.com/regions";
        private const string QueueUrl = "https://api.deadbyqueue.com/queuetime?region=";

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("MakeYourChoice/1.0");
            return c;
        }

        /// <summary>
        /// AWS region code -> online(true)/offline(false), plus the unix time (UTC seconds) of DBQ's
        /// own last data refresh (its "lastupdated2" field) so callers can judge how current the data
        /// is. dataUnix is null when the field is missing or on failure (empty dict).
        /// </summary>
        public static async Task<(Dictionary<string, bool> regions, long? dataUnix)> GetRegionStatusAsync()
        {
            var result = new Dictionary<string, bool>();
            long? dataUnix = null;
            try
            {
                using var stream = await _http.GetStreamAsync(RegionsUrl);
                using var doc = await JsonDocument.ParseAsync(stream);
                if (doc.RootElement.TryGetProperty("lastupdated2", out var lu)
                    && lu.ValueKind == JsonValueKind.Number && lu.TryGetInt64(out var luv))
                {
                    dataUnix = luv;
                }
                if (doc.RootElement.TryGetProperty("regions", out var regions)
                    && regions.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in regions.EnumerateObject())
                    {
                        if (p.Value.ValueKind == JsonValueKind.True) result[p.Name] = true;
                        else if (p.Value.ValueKind == JsonValueKind.False) result[p.Name] = false;
                    }
                }
            }
            catch
            {
                // network/parse failure -> empty (callers keep prior state)
            }
            return (result, dataUnix);
        }

        /// <summary>
        /// Returns the raw queue text and the killer queue time in whole minutes.
        /// killerMinutes is 0 when the queue is under a minute, or -1 when unknown/unavailable.
        /// </summary>
        public static async Task<(string text, int killerMinutes)> GetQueueAsync(string awsRegion)
        {
            try
            {
                var text = (await _http.GetStringAsync(QueueUrl + Uri.EscapeDataString(awsRegion))).Trim();
                if (string.IsNullOrEmpty(text)
                    || text.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase)
                    || !text.Contains("Killer"))
                {
                    return (string.IsNullOrEmpty(text) ? "No data" : text, -1);
                }
                var m = Regex.Match(text, @"Killer:\s*(\d+)m");
                int min = m.Success ? int.Parse(m.Groups[1].Value) : 0;
                return (text, min);
            }
            catch (Exception ex)
            {
                return ("Queue unavailable: " + ex.Message, -1);
            }
        }
    }
}
