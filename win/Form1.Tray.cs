using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MakeYourChoice
{
    public partial class Form1
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        // A single tray icon: the app icon with a status bubble in the bottom-right corner
        // (green = preferred server online, red = offline, gray = unknown). The tooltip shows the
        // server status and the current killer/survivor queue time, sourced from Dead by Queue.
        private NotifyIcon _tray;
        private IntPtr _trayHandle = IntPtr.Zero;
        private Bitmap _appIconBmp;
        private ContextMenuStrip _trayMenu;
        private Timer _dbqTimer;
        private bool _minimizeBalloonShown;
        private bool _exiting;
        // For offline -> online notifications: the preferred region and its last seen online state.
        private string _prevPreferredRegion;
        private bool? _prevPreferredOnline;
        // Last queue-time text for the preferred region, cached by the (slow) Dead by Queue poll and
        // shown in the tray tooltip alongside the live (fast) beacon status.
        private string _lastQueueText = "";

        // AWS region code -> online(true)/offline(false). Primary source is the live GameLift beacon
        // probe (for the selected region); Dead by Queue /regions fills in the rest as a fallback.
        // Read by the latency list to show a ✓ / ⚠ next to unstable servers.
        private readonly Dictionary<string, bool> _dbqOnline = new();

        // ── EXPERIMENT: active beacon ───────────────────────────────────────────────────────────
        // Probe the real game-server IPs we learned from the sniffer (ServerRegistry) to detect
        // fleet up/down in real time, going around DBQ's lag. A definite "Replied" from any learned
        // server flips that region online immediately and is tagged as the status source. DBQ stays
        // as the baseline/fallback. All probe results are written to BeaconLog for analysis.
        // Flip to false to disable the active beacon (DBQ-only). Search: ExperimentActiveBeacon.
        private static readonly bool ExperimentActiveBeacon = true;
        // AWS region code -> where its current status came from ("beacon" | "live" | "dbq").
        private readonly Dictionary<string, string> _statusSource = new();
        private bool _beaconStartupLogged;

        // Status is RESOLVED fresh each poll. Priority:
        //   live   — you're actually connected right now (sniffer), authoritative while recent
        //   dbq-down (fresh) — Dead by Queue is the GUARANTEE: when its data is current and says a
        //            region is down, it's down — even if the beacon still sees a few servers (those
        //            are stragglers: the longest games still finishing that you can't matchmake to)
        //   beacon — the active probe, best at catching a region COMING ONLINE early (speculative)
        //   dbq    — Dead by Queue's value otherwise (incl. when its data is stale)
        // _dbqOnline/_statusSource below hold the RESOLVED result (read by the tray + latency list).
        private enum BeaconState { Unknown, Online } // a probe can only confirm ONLINE, never OFFLINE
        private readonly Dictionary<string, bool> _dbqRaw = new();         // DBQ's own /regions view
        private long? _dbqDataUnix;                                        // DBQ's data refresh time (unix)
        private readonly Dictionary<string, DateTime> _lastConnection = new(); // UTC of last real connection
        private readonly Dictionary<string, BeaconState> _beaconResult = new();
        private const int LiveWindowSeconds = 120; // a connection counts as "live" for this long
        // DBQ counts as "fresh enough to be the guarantee" if its data is at most this old.
        private const int DbqFreshSeconds = 120;

        // Beacon-v2 tuning + state.
        private const int HotSetSize = 8;          // instances probed every poll
        private const int MaxInstances = 48;       // cap distinct IPs probed (dedup keeps this small)
        private const int ProbeJitterMs = 400;     // spread probes so it's not a burst scan
        private readonly Dictionary<string, int> _prevLiveCount = new();  // last poll's live count
        private readonly Dictionary<string, int> _deadPolls = new();      // consecutive all-dead polls (backoff)
        private int _beaconPollCount;
        // Debug toggle (Experimental): verbose beacon-log + live-count/data-age in the tray tooltip.
        private bool _debugBeacon;

        private void SetupTray()
        {
            if (_tray != null) return; // already set up

            _appIconBmp = LoadAppIconBitmap();

            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Show Make Your Choice", null, (_, __) => RestoreFromTray());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit", null, (_, __) => ExitFromTray());

            _tray = new NotifyIcon
            {
                Text = "Make Your Choice — waiting for server status…",
                Visible = true,
                ContextMenuStrip = _trayMenu,
            };
            _tray.DoubleClick += (_, __) => RestoreFromTray();

            SetTrayIcon(MakeStatusIcon(Color.Gray));
        }

        private void StartDbqTimer()
        {
            int ms = Math.Max(5, _pollIntervalSeconds) * 1000;

            // Single poll: refresh real online/offline + queue from Dead by Queue, then update the
            // tray. (The old GameLift-beacon probe was removed: the beacon can't read DBD fleet
            // state — UDP 443 never echoes, and UDP 7770 echoes for every region regardless of
            // whether DBD has servers there — so it always reported the selected region as down.)
            _dbqTimer = new Timer { Interval = ms };
            _dbqTimer.Tick += async (_, __) => await RefreshStatusAsync();
            _dbqTimer.Start();
            _ = RefreshStatusAsync(); // immediate first fetch
        }

        // Re-apply the configured poll interval (call after the option changes).
        private void ApplyPollInterval()
        {
            int ms = Math.Max(5, _pollIntervalSeconds) * 1000;
            if (_dbqTimer != null) _dbqTimer.Interval = ms;
        }

        // Real-time override: the moment we actually connect to a server in a region (seen by the
        // traffic sniffer), that region is definitively ONLINE — more reliable and immediate than
        // any ping or DBQ's lagged data. Called from OnTrafficDetected.
        public void MarkRegionOnlineFromConnection(string regionName)
        {
            if (string.IsNullOrEmpty(regionName)) return;
            var code = AwsCodeForRegion(regionName);
            if (code == null) return;
            // Record WHEN we connected; "live" is authoritative only while recent (see ResolveStatuses),
            // so it no longer sticks online forever after you stop playing. Set immediate feedback too.
            _lastConnection[code] = DateTime.UtcNow;
            _dbqOnline[code] = true;
            _statusSource[code] = "live";
        }

        // EXPERIMENT — for every unstable region we have learned servers for, actively probe them and
        // override status to ONLINE the instant any server replies. This is the real-time beacon that
        // goes around DBQ. We never force OFFLINE from a probe (a learned IP can be a recycled/dead
        // instance even when the fleet is up elsewhere), so DBQ remains the source for the down case.
        private async System.Threading.Tasks.Task RunActiveBeaconAsync()
        {
            if (!ExperimentActiveBeacon) return;
            // User toggle: when live scanning is off, send no probe traffic and clear any beacon
            // verdicts so status falls back to live connections + Dead by Queue.
            if (!_liveServerScanning) { _beaconResult.Clear(); return; }
            if (_regions == null) return;

            _beaconPollCount++;
            if (_beaconPollCount % 40 == 1) _serverRegistry.Prune(); // keep the pool lean periodically

            // Distinct unstable region codes from the UI list.
            var codes = new HashSet<string>();
            foreach (var kv in _regions)
            {
                if (kv.Value.Stable) continue;
                var code = AwsCodeForRegion(kv.Key);
                if (code != null) codes.Add(code);
            }

            bool anyLiveThisPoll = false;
            foreach (var code in codes)
            {
                // Dedup to one (ip,port) per instance, newest/most-reliable first.
                var instances = _serverRegistry.GetInstancesRanked(code, MaxInstances);
                int sample = instances.Count;
                if (sample == 0)
                {
                    _beaconResult[code] = BeaconState.Unknown; // nothing to probe -> defer to DBQ
                    if (_debugBeacon) BeaconLog.Write($"BEACON {code}  no learned servers yet");
                    continue;
                }

                // Confidence-scaled threshold; a rising trend lowers the bar by 1 so the beacon leads
                // the "coming online" case, while stragglers (below threshold) never count as up.
                int needed = RequiredLive(sample);
                int prev = _prevLiveCount.TryGetValue(code, out var pc) ? pc : -1;

                // Hot/cold + backoff: always probe the hot set (recently-live IPs); sweep the cold set
                // only every Nth poll, backing off the longer a region has been all-dead.
                int dead = _deadPolls.TryGetValue(code, out var dp) ? dp : 0;
                int coldEvery = 1 + Math.Min(dead, 6);               // 1 (fresh) .. 7 (long dead)
                bool sweepCold = (_beaconPollCount % coldEvery) == 0;
                var toProbe = (sweepCold ? instances : instances.Take(HotSetSize))
                    .Select(i => (i.Ip, i.Port)).ToList();

                LiveProbe.LiveResult res;
                try { res = await LiveProbe.ProbeForLiveAsync(toProbe, Math.Max(1, needed), 1000, 24, ProbeJitterMs); }
                catch { res = new LiveProbe.LiveResult(); }

                int liveCount = res.LiveCount;
                foreach (var (ip, port) in res.Live) _serverRegistry.MarkLive(code, ip, port);
                if (liveCount > 0) anyLiveThisPoll = true;

                bool rising = prev >= 0 && liveCount > prev;
                int effNeeded = rising ? Math.Max(1, needed - 1) : needed;
                bool online = liveCount >= effNeeded;

                _beaconResult[code] = online ? BeaconState.Online : BeaconState.Unknown;
                _prevLiveCount[code] = liveCount;
                _deadPolls[code] = liveCount == 0 ? dead + 1 : 0;

                if (_debugBeacon)
                {
                    string trend = prev < 0 ? "" : rising ? " rising" : liveCount < prev ? " falling" : " flat";
                    BeaconLog.Write($"BEACON {code}  sample={sample} probed={res.Probed}({(sweepCold ? "full" : "hot")}) " +
                        $"live={liveCount} need={effNeeded}{trend} -> {(online ? "ONLINE" : "unknown")}  " +
                        $"[{res.Replied} rep, {res.PortUnreach} unreach, {res.Timeout} to]");

                    // A2S exploration: ask one live server for player info (distinguishes a busy
                    // in-match server from an idle/ready one). Logged only; not used for the verdict yet.
                    if (res.Live.Count > 0)
                    {
                        try
                        {
                            var (ip, port) = res.Live[0];
                            var a2s = await LiveProbe.ProbeA2sAsync(ip, port, 700);
                            BeaconLog.Write($"BEACON {code}  A2S {a2s}");
                        }
                        catch { }
                    }
                }
            }
            if (anyLiveThisPoll) _serverRegistry.Flush(); // persist MarkLive updates
        }

        // How many distinct live servers we require before calling a region ONLINE [beacon]. DBQ's
        // fresh "down" already vetoes stragglers ahead of the beacon (see ResolveStatuses), so the
        // bar here is deliberately low: the beacon's job is to catch a region COMING ONLINE, which in
        // the captured logs means as few as 1-2 live (London routinely shows up on a single responder;
        // Ohio peaks ~10). A rising edge lowers this by 1, so even a large pool can fire on first hit.
        private static int RequiredLive(int sampleSize)
            => sampleSize <= 2 ? 1 : 2;

        // Resolve each region's displayed status. Priority:
        //   1. recent live connection                -> ONLINE [live]   (you're on it right now)
        //   2. DBQ fresh AND DBQ says down           -> OFFLINE [dbq]   (guarantee; cuts beacon
        //      stragglers — the last long games still running that you can't matchmake to)
        //   3. beacon says online                    -> ONLINE [beacon] (speculative; catches a
        //      region coming online before DBQ does)
        //   4. DBQ has a value (even if stale)       -> [dbq]
        //   5. nothing knows                         -> unknown
        private void ResolveStatuses()
        {
            var now = DateTime.UtcNow;
            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool dbqFresh = _dbqDataUnix.HasValue && (nowUnix - _dbqDataUnix.Value) <= DbqFreshSeconds;

            var codes = new HashSet<string>(_dbqRaw.Keys);
            if (_regions != null)
                foreach (var kv in _regions)
                {
                    var c = AwsCodeForRegion(kv.Key);
                    if (c != null) codes.Add(c);
                }

            foreach (var code in codes)
            {
                bool hasDbq = _dbqRaw.TryGetValue(code, out var dbqVal);

                if (_lastConnection.TryGetValue(code, out var t) && (now - t).TotalSeconds < LiveWindowSeconds)
                {
                    _dbqOnline[code] = true; _statusSource[code] = "live"; continue;
                }
                // DBQ is the guarantee: a current "down" cuts the beacon's stragglers.
                if (dbqFresh && hasDbq && !dbqVal)
                {
                    _dbqOnline[code] = false; _statusSource[code] = "dbq"; continue;
                }
                // Beacon leads the "coming online" case.
                var bs = _beaconResult.TryGetValue(code, out var b) ? b : BeaconState.Unknown;
                if (bs == BeaconState.Online) { _dbqOnline[code] = true; _statusSource[code] = "beacon"; continue; }
                // Otherwise defer to DBQ's value (online, or a stale down).
                if (hasDbq) { _dbqOnline[code] = dbqVal; _statusSource[code] = "dbq"; continue; }
                _dbqOnline.Remove(code); _statusSource.Remove(code); // nothing knows -> unknown
            }
        }

        // Poll Dead by Queue for real online/offline status + queue time, and update the tray.
        private async System.Threading.Tasks.Task RefreshStatusAsync()
        {
            if (_exiting || IsDisposed || _tray == null) return;

            if (!_beaconStartupLogged)
            {
                _beaconStartupLogged = true;
                BeaconLog.Write($"=== session start; active beacon={(ExperimentActiveBeacon ? "ON" : "off")}; " +
                                $"known servers={_serverRegistry.TotalServers} across {_serverRegistry.RegionCount} regions; " +
                                $"handshake magic={LiveProbe.ActiveMagicHex()} ({(LiveProbe.UsingLearnedHandshake ? "learned" : "bootstrap")}) ===");
            }

            var (status, dataUnix) = await DbqClient.GetRegionStatusAsync();
            if (status.Count > 0)
            {
                foreach (var kv in status) _dbqRaw[kv.Key] = kv.Value; // DBQ's own view
                _dbqDataUnix = dataUnix;                               // how current that view is
            }

            // Active beacon: probe learned servers (fills _beaconResult per region).
            await RunActiveBeaconAsync();

            // Resolve the displayed status: recent live > beacon > DBQ.
            ResolveStatuses();

            if (_exiting || IsDisposed || _tray == null) return;

            var preferred = GetPreferredRegionKey();
            if (preferred == null)
            {
                SetTrayIcon(MakeStatusIcon(Color.Gray));
                _tray.Text = Trunc("Make Your Choice — select a region to track");
                _prevPreferredRegion = null;
                _prevPreferredOnline = null;
                return;
            }

            var code = AwsCodeForRegion(preferred);
            bool? online = (code != null && _dbqOnline.TryGetValue(code, out var on)) ? on : (bool?)null;

            var shortName = preferred.Contains("(")
                ? preferred.Substring(preferred.IndexOf('(') + 1).TrimEnd(')')
                : preferred;

            Color bubble = online == true ? Color.LimeGreen : online == false ? Color.Red : Color.Gray;
            string state = online == true ? "ONLINE" : online == false ? "OFFLINE" : "status unknown";
            SetTrayIcon(MakeStatusIcon(bubble));

            string queueText = "";
            if (!string.IsNullOrEmpty(code))
            {
                var (qt, min) = await DbqClient.GetQueueAsync(code);
                // Only show an actual queue time. When a region is down DBQ's endpoint returns a
                // verbose "no queue" paragraph (min = -1) — never put that in the tray.
                queueText = min >= 0 ? qt : "";
                _lastQueueText = queueText;
            }
            if (_exiting || IsDisposed || _tray == null) return;

            // Notify on an offline -> online transition for the same preferred region.
            if (_notifyServerOnline
                && string.Equals(_prevPreferredRegion, preferred, StringComparison.Ordinal)
                && _prevPreferredOnline == false && online == true)
            {
                _tray.BalloonTipTitle = "Server online";
                _tray.BalloonTipText = $"{shortName} is now online.";
                try { _tray.ShowBalloonTip(4000); } catch { }
            }
            _prevPreferredRegion = preferred;
            _prevPreferredOnline = online;

            var queue = string.IsNullOrEmpty(queueText) ? "" : "  -  " + queueText;
            // Show which source produced this status (beacon = real-time active probe), for online
            // and offline alike.
            string src = "";
            if (code != null && online != null && _statusSource.TryGetValue(code, out var s))
                src = "  [" + s + "]";
            string dbg = "";
            if (_debugBeacon && code != null)
            {
                int live = _prevLiveCount.TryGetValue(code, out var lc) ? lc : 0;
                string age = _dbqDataUnix.HasValue
                    ? (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _dbqDataUnix.Value) + "s"
                    : "?";
                dbg = $"  ({live} live, DBQ {age})";
            }
            _tray.Text = Trunc($"{shortName}: {state}{queue}{src}{dbg}");
        }

        // The region the tray reports on: first checked unstable region, else first checked region.
        private string GetPreferredRegionKey()
        {
            if (_lv == null || _lv.IsDisposed) return null;
            var checkedKeys = _lv.CheckedItems.Cast<ListViewItem>()
                .Select(i => i.Tag as string)
                .Where(s => s != null && _regions.ContainsKey(s))
                .ToList();
            if (checkedKeys.Count == 0) return null;
            var unstable = checkedKeys.FirstOrDefault(k => !_regions[k].Stable);
            return unstable ?? checkedKeys[0];
        }

        private static string Trunc(string s) => s != null && s.Length > 120 ? s.Substring(0, 119) + "…" : s;

        private void SetTrayIcon(Bitmap bmp)
        {
            if (_tray == null) { bmp.Dispose(); return; }
            IntPtr h = bmp.GetHicon();
            try
            {
                _tray.Icon = Icon.FromHandle(h);
                if (_trayHandle != IntPtr.Zero) DestroyIcon(_trayHandle);
                _trayHandle = h;
            }
            finally
            {
                bmp.Dispose();
            }
        }

        private static Bitmap LoadAppIconBitmap()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (File.Exists(path))
                {
                    using var ico = new Icon(path, 32, 32);
                    return new Bitmap(ico.ToBitmap(), 32, 32);
                }
            }
            catch { /* fall through */ }
            // Fallback: a neutral placeholder so the tray still works.
            var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(110, 84, 148)); // app accent purple
            return bmp;
        }

        // App icon with a colored status bubble drawn in the bottom-right corner.
        private Bitmap MakeStatusIcon(Color bubble)
        {
            var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);

            if (_appIconBmp != null)
                g.DrawImage(_appIconBmp, new Rectangle(0, 0, 32, 32));

            const int d = 15;
            int x = 32 - d - 1, y = 32 - d - 1;
            // White ring for contrast on any taskbar colour.
            using (var ring = new SolidBrush(Color.White))
                g.FillEllipse(ring, x - 2, y - 2, d + 4, d + 4);
            using (var fill = new SolidBrush(bubble))
                g.FillEllipse(fill, x, y, d, d);
            using (var pen = new Pen(Color.FromArgb(140, 0, 0, 0), 1))
                g.DrawEllipse(pen, x, y, d, d);
            return bmp;
        }

        // Minimize-to-tray: when enabled, minimizing hides the window (and its taskbar button);
        // the tray icon remains. Otherwise it behaves like a normal taskbar minimize.
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_minimizeToTray && WindowState == FormWindowState.Minimized && _tray != null)
            {
                Hide();
                if (!_minimizeBalloonShown)
                {
                    _minimizeBalloonShown = true;
                    _tray.BalloonTipTitle = "Make Your Choice";
                    _tray.BalloonTipText = "Still running in the system tray. Double-click to restore.";
                    try { _tray.ShowBalloonTip(2000); } catch { }
                }
            }
        }

        private void RestoreFromTray()
        {
            if (IsDisposed) return;
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
            BringToFront();
        }

        private void ExitFromTray()
        {
            _exiting = true;
            Close();
        }

        private void DisposeTray()
        {
            _exiting = true;
            try { _dbqTimer?.Stop(); _dbqTimer?.Dispose(); } catch { }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            if (_trayHandle != IntPtr.Zero) { DestroyIcon(_trayHandle); _trayHandle = IntPtr.Zero; }
            _appIconBmp?.Dispose();
            _trayMenu?.Dispose();
        }
    }
}
