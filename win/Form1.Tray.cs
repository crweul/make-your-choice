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

        // AWS region code -> online(true)/offline(false), from Dead by Queue /regions.
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
            _dbqTimer = new Timer { Interval = 30_000 };
            _dbqTimer.Tick += async (_, __) => await RefreshDbqAsync();
            _dbqTimer.Start();
            _ = RefreshDbqAsync(); // immediate first fetch
        }

        private async System.Threading.Tasks.Task RefreshDbqAsync()
        {
            if (_exiting || IsDisposed || _tray == null) return;

            // 1) Region online/offline map (also drives the latency list ✓/⚠).
            var status = await DbqClient.GetRegionStatusAsync();
            if (status.Count > 0)
            {
                _dbqOnline.Clear();
                foreach (var kv in status) _dbqOnline[kv.Key] = kv.Value;
            }

            // 2) Preferred server status + its queue time, shown on the single tray icon.
            var preferred = GetPreferredRegionKey();
            if (preferred == null)
            {
                SetTrayIcon(MakeStatusIcon(Color.Gray));
                _tray.Text = Trunc("Make Your Choice — select a region to track");
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

            var (queueText, _) = await DbqClient.GetQueueAsync(code ?? "");
            if (_tray != null)
                _tray.Text = Trunc($"{shortName}: {state}  —  {queueText}");
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
