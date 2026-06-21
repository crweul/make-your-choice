using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MakeYourChoice
{
    /// <summary>
    /// EXPERIMENT — remembers the real DBD game-server IPs we've actually connected to, grouped by
    /// AWS region code, so the active beacon (LiveProbe) has concrete targets to probe even when the
    /// game isn't running. Fed by the traffic sniffer; persisted to disk across launches.
    ///
    /// This is the "work in just the app without connecting" piece: the more you've played, the
    /// bigger the per-region pool of known servers, so on a cold launch we can probe that pool and
    /// tell whether the fleet is up — no need to be in a match.
    ///
    /// PERSISTENCE: plain-text, one record per line ("regionCode|ip|port|lastSeenUnix"). We do NOT
    /// use System.Text.Json here because the app is published with PublishTrimmed, which breaks
    /// reflection-based JSON (de)serialization at runtime — that silently wiped the registry before.
    /// </summary>
    public sealed class ServerRegistry
    {
        public sealed class Entry
        {
            public string Ip { get; set; }
            public int Port { get; set; }
            public long LastSeenUnix { get; set; }
        }

        // Keep a generous history per region so a cold launch has many probe targets even as
        // GameLift churns individual instances.
        private const int MaxPerRegion = 256;

        private readonly object _lock = new();
        private readonly Dictionary<string, Dictionary<string, Entry>> _byRegion = new(); // region -> (ip -> entry)
        private readonly string _path;

        public ServerRegistry()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MakeYourChoice");
            try { Directory.CreateDirectory(dir); } catch { }
            _path = Path.Combine(dir, "known-servers.txt");
            // Shared address book shipped with the app (curated from many users' connections), so a
            // fresh install can probe known DBD subnets without discovering anything itself.
            LoadFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "servers-seed.txt"));
            Load(); // user's own learned servers (override/extend the shipped seed)
        }

        public void Record(string regionCode, string ip, int port)
        {
            if (string.IsNullOrEmpty(regionCode) || string.IsNullOrEmpty(ip)) return;
            lock (_lock)
            {
                if (!_byRegion.TryGetValue(regionCode, out var map))
                    _byRegion[regionCode] = map = new Dictionary<string, Entry>();
                map[ip + ":" + port] = new Entry { Ip = ip, Port = port, LastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                Trim(map);
            }
            Save();
        }

        private static void Trim(Dictionary<string, Entry> map)
        {
            if (map.Count <= MaxPerRegion) return;
            foreach (var old in map.Values.OrderBy(e => e.LastSeenUnix).Take(map.Count - MaxPerRegion).ToList())
                map.Remove(old.Ip + ":" + old.Port);
        }

        /// <summary>Most-recently-seen server endpoints for a region (newest first).</summary>
        public List<Entry> GetCandidates(string regionCode, int max)
        {
            lock (_lock)
            {
                if (regionCode == null || !_byRegion.TryGetValue(regionCode, out var map))
                    return new List<Entry>();
                return map.Values.OrderByDescending(e => e.LastSeenUnix).Take(max).ToList();
            }
        }

        public int RegionCount { get { lock (_lock) return _byRegion.Count; } }
        public int TotalServers { get { lock (_lock) return _byRegion.Values.Sum(m => m.Count); } }
        public int CountFor(string regionCode)
        {
            lock (_lock) return _byRegion.TryGetValue(regionCode, out var m) ? m.Count : 0;
        }

        /// <summary>Distinct /24 prefixes (e.g. "18.225.57") seen for a region — the subnets to sweep.</summary>
        public List<string> GetSubnets24(string regionCode)
        {
            lock (_lock)
            {
                if (regionCode == null || !_byRegion.TryGetValue(regionCode, out var map)) return new List<string>();
                var set = new HashSet<string>();
                foreach (var e in map.Values)
                {
                    var dot = e.Ip.LastIndexOf('.');
                    if (dot > 0) set.Add(e.Ip.Substring(0, dot));
                }
                return set.ToList();
            }
        }

        /// <summary>Distinct game ports DBD has used in a region — the ports to probe when sweeping.</summary>
        public List<int> GetKnownPorts(string regionCode)
        {
            lock (_lock)
            {
                if (regionCode == null || !_byRegion.TryGetValue(regionCode, out var map)) return new List<int>();
                return map.Values.Select(e => e.Port).Distinct().ToList();
            }
        }

        private void Load() => LoadFile(_path);

        // Merge a "regionCode|ip|port|lastSeenUnix" file into the registry (newer LastSeen wins).
        private void LoadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                lock (_lock)
                {
                    foreach (var raw in File.ReadAllLines(path))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#') continue;
                        var p = line.Split('|');
                        if (p.Length < 4) continue;
                        var region = p[0].Trim();
                        var ip = p[1].Trim();
                        if (region.Length == 0 || ip.Length == 0) continue;
                        if (!int.TryParse(p[2], out var port)) continue;
                        if (!long.TryParse(p[3], out var seen)) seen = 0;
                        if (!_byRegion.TryGetValue(region, out var map))
                            _byRegion[region] = map = new Dictionary<string, Entry>();
                        var key = ip + ":" + port;
                        if (!map.TryGetValue(key, out var existing) || existing.LastSeenUnix < seen)
                            map[key] = new Entry { Ip = ip, Port = port, LastSeenUnix = seen };
                    }
                }
            }
            catch { /* corrupt/missing -> ignore */ }
        }

        private void Save()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# DBD known servers — regionCode|ip|port|lastSeenUnix");
                lock (_lock)
                {
                    foreach (var region in _byRegion)
                        foreach (var e in region.Value.Values)
                            sb.Append(region.Key).Append('|').Append(e.Ip).Append('|')
                              .Append(e.Port).Append('|').Append(e.LastSeenUnix).Append('\n');
                }
                File.WriteAllText(_path, sb.ToString());
            }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// EXPERIMENT — appends active-beacon probe results to a plain-text log the user can read or send
    /// back for analysis (cross-checked against a Wireshark capture).
    /// </summary>
    public static class BeaconLog
    {
        // Disabled for release (the beacon still works; it just doesn't write beacon-log.txt).
        // Flip to true to re-enable the diagnostic log.
        private static readonly bool Enabled = false;

        private static readonly object _lock = new();
        public static readonly string Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MakeYourChoice", "beacon-log.txt");

        public static void Write(string line)
        {
            if (!Enabled) return;
            try
            {
                lock (_lock)
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                    File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
                }
            }
            catch { /* best effort */ }
        }
    }
}
