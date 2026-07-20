using System;
using System.Collections.Generic;
using System.Linq;
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
        // Two DBQ mirrors. They sometimes diverge: one can glitch and report EVERY region offline
        // (even always-on stable ones), which is never real. We query both, throw out an all-offline
        // (glitched) response, and use the sane/fresher one. api2 has been the more reliable mirror,
        // so it's listed first.
        private static readonly string[] RegionsUrls =
        {
            "https://api2.deadbyqueue.com/regions",
            "https://api.deadbyqueue.com/regions",
        };
        private static readonly string[] QueueUrls =
        {
            "https://api2.deadbyqueue.com/queuetime?region=",
            "https://api.deadbyqueue.com/queuetime?region=",
        };

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
            (Dictionary<string, bool> regions, long? dataUnix) best = (new Dictionary<string, bool>(), null);
            bool haveSane = false;
            foreach (var url in RegionsUrls)
            {
                var r = await FetchRegionsAsync(url);
                if (r.regions.Count == 0) continue;                 // fetch/parse failed
                bool sane = r.regions.Values.Any(v => v);           // all-offline = glitched mirror, ignore
                if (!sane) continue;
                // Among sane responses, keep the one with the fresher data timestamp.
                if (!haveSane || (r.dataUnix ?? 0) > (best.dataUnix ?? 0)) best = r;
                haveSane = true;
            }
            // If neither mirror was sane, return empty so the caller keeps its prior (good) values
            // instead of flipping everything offline on a transient glitch.
            return best;
        }

        private static async Task<(Dictionary<string, bool> regions, long? dataUnix)> FetchRegionsAsync(string url)
        {
            var result = new Dictionary<string, bool>();
            long? dataUnix = null;
            try
            {
                using var stream = await _http.GetStreamAsync(url);
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
                // network/parse failure -> empty
            }
            return (result, dataUnix);
        }

        /// <summary>
        /// Returns the raw queue text and the killer queue time in whole minutes.
        /// killerMinutes is 0 when the queue is under a minute, or -1 when unknown/unavailable.
        /// </summary>
        public static async Task<(string text, int killerMinutes)> GetQueueAsync(string awsRegion)
        {
            // Try each mirror; return the first that gives a real queue time.
            foreach (var baseUrl in QueueUrls)
            {
                try
                {
                    var text = (await _http.GetStringAsync(baseUrl + Uri.EscapeDataString(awsRegion))).Trim();
                    if (string.IsNullOrEmpty(text)
                        || text.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase)
                        || !text.Contains("Killer"))
                    {
                        continue; // no valid queue from this mirror -> try the next
                    }
                    var m = Regex.Match(text, @"Killer:\s*(\d+)m");
                    int min = m.Success ? int.Parse(m.Groups[1].Value) : 0;
                    return (text, min);
                }
                catch { /* try next mirror */ }
            }
            return ("", -1); // no mirror had a queue (e.g. region down) -> caller shows nothing
        }
    }
}
