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
        private Timer _beaconTimer;
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

            // Slow poll: fills the fallback online map for non-selected regions and the preferred
            // region's queue-time text. The fast beacon timer owns the live status + notifications.
            _dbqTimer = new Timer { Interval = ms };
            _dbqTimer.Tick += async (_, __) => await RefreshDbqAsync();
            _dbqTimer.Start();
            _ = RefreshDbqAsync(); // immediate first fetch

            // Fast poll: probe ONLY the selected region's GameLift beacon for real-time up/down.
            _beaconTimer = new Timer { Interval = ms };
            _beaconTimer.Tick += async (_, __) => await UpdateTrayFromBeaconAsync();
            _beaconTimer.Start();
            _ = UpdateTrayFromBeaconAsync(); // immediate first probe
        }

        // Re-apply the configured poll interval to both timers (call after the option changes).
        private void ApplyPollInterval()
        {
            int ms = Math.Max(5, _pollIntervalSeconds) * 1000;
            if (_dbqTimer != null) _dbqTimer.Interval = ms;
            if (_beaconTimer != null) _beaconTimer.Interval = ms;
        }

        // Slow Dead by Queue poll — caches data only; does not touch the tray (the beacon owns it).
        private async System.Threading.Tasks.Task RefreshDbqAsync()
        {
            if (_exiting || IsDisposed || _tray == null) return;

            var status = await DbqClient.GetRegionStatusAsync();
            if (status.Count > 0)
            {
                foreach (var kv in status) _dbqOnline[kv.Key] = kv.Value;
            }

            var preferred = GetPreferredRegionKey();
            var code = preferred != null ? AwsCodeForRegion(preferred) : null;
            if (!string.IsNullOrEmpty(code))
            {
                var (queueText, _) = await DbqClient.GetQueueAsync(code);
                _lastQueueText = queueText;
            }
        }

        // Fast live status: probe the selected region's GameLift beacon directly (beacon-primary),
        // falling back to the cached Dead by Queue map only when the probe is inconclusive.
        private async System.Threading.Tasks.Task UpdateTrayFromBeaconAsync()
        {
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
            var hosts = _regions.TryGetValue(preferred, out var info) ? info.Hosts : null;
            var pingHost = hosts != null && hosts.Length > 1 ? hosts[1]
                         : hosts != null && hosts.Length > 0 ? hosts[0] : null;

            // Beacon is primary; fall back to the cached Dead by Queue map when inconclusive (null).
            bool? online = await GameLiftBeacon.IsFleetOnlineAsync(pingHost);
            if (online == null && code != null && _dbqOnline.TryGetValue(code, out var cached))
                online = cached;

            // Feed the live result back into the map so the latency list ✓/⚠ tracks it too.
            if (online != null && code != null)
                _dbqOnline[code] = online.Value;

            if (_exiting || IsDisposed || _tray == null) return;

            var shortName = preferred.Contains("(")
                ? preferred.Substring(preferred.IndexOf('(') + 1).TrimEnd(')')
                : preferred;

            Color bubble = online == true ? Color.LimeGreen : online == false ? Color.Red : Color.Gray;
            string state = online == true ? "ONLINE" : online == false ? "OFFLINE" : "status unknown";
            SetTrayIcon(MakeStatusIcon(bubble));

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

            var queue = string.IsNullOrEmpty(_lastQueueText) ? "" : "  —  " + _lastQueueText;
            _tray.Text = Trunc($"{shortName}: {state}{queue}");
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
            try { _beaconTimer?.Stop(); _beaconTimer?.Dispose(); } catch { }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            if (_trayHandle != IntPtr.Zero) { DestroyIcon(_trayHandle); _trayHandle = IntPtr.Zero; }
            _appIconBmp?.Dispose();
            _trayMenu?.Dispose();
        }
    }
}
