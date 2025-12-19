using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Net;
using System.Text;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;
using System.Reflection;

namespace MakeYourChoice
{
    public class Form1 : Form
    {
        private const string RepoUrl    = "https://codeberg.org/ky/make-your-choice";
        private const string WebsiteUrl = "https://kurocat.net";
        private const string DiscordUrl = "https://discord.gg/gnvtATeVc4";
        private const string CurrentVersion = "1.0.0-RC";
        private const string Developer = "ky";
        private const string Repo  = "make-your-choice";
        private const string UpdateMessage  = "If you updated from an older version, please reset your hosts file since this version handles the hosts file differently.\n\nGo to the version menu, and select \"Reset hosts file\".";

        // Holds endpoint list and stability flag for each region
        private record RegionInfo(string[] Hosts, bool Stable);
        private readonly Dictionary<string, RegionInfo> _regions = new()
        {
            // Europe
            { "Europe (London)",            new RegionInfo(new[]{ "gamelift.eu-west-2.amazonaws.com",    "gamelift-ping.eu-west-2.api.aws" }, false) },
            { "Europe (Ireland)",           new RegionInfo(new[]{ "gamelift.eu-west-1.amazonaws.com",    "gamelift-ping.eu-west-1.api.aws" }, true) },
            { "Europe (Frankfurt am Main)", new RegionInfo(new[]{ "gamelift.eu-central-1.amazonaws.com", "gamelift-ping.eu-central-1.api.aws" }, true) },

            // The Americas
            { "US East (N. Virginia)",      new RegionInfo(new[]{ "gamelift.us-east-1.amazonaws.com",    "gamelift-ping.us-east-1.api.aws" }, true) },
            { "US East (Ohio)",             new RegionInfo(new[]{ "gamelift.us-east-2.amazonaws.com",    "gamelift-ping.us-east-2.api.aws" }, false) },
            { "US West (N. California)",    new RegionInfo(new[]{ "gamelift.us-west-1.amazonaws.com",    "gamelift-ping.us-west-1.api.aws" }, true) },
            { "US West (Oregon)",           new RegionInfo(new[]{ "gamelift.us-west-2.amazonaws.com",    "gamelift-ping.us-west-2.api.aws" }, true) },
            { "Canada (Central)",           new RegionInfo(new[]{ "gamelift.ca-central-1.amazonaws.com", "gamelift-ping.ca-central-1.api.aws" }, false) },
            { "South America (São Paulo)",  new RegionInfo(new[]{ "gamelift.sa-east-1.amazonaws.com",   "gamelift-ping.sa-east-1.api.aws" }, true) },

            // Asia (excluding Mainland China)
            { "Asia Pacific (Tokyo)",       new RegionInfo(new[]{ "gamelift.ap-northeast-1.amazonaws.com","gamelift-ping.ap-northeast-1.api.aws" }, true) },
            { "Asia Pacific (Seoul)",       new RegionInfo(new[]{ "gamelift.ap-northeast-2.amazonaws.com","gamelift-ping.ap-northeast-2.api.aws" }, true) },
            { "Asia Pacific (Mumbai)",      new RegionInfo(new[]{ "gamelift.ap-south-1.amazonaws.com",   "gamelift-ping.ap-south-1.api.aws" }, true) },
            { "Asia Pacific (Singapore)",   new RegionInfo(new[]{ "gamelift.ap-southeast-1.amazonaws.com","gamelift-ping.ap-southeast-1.api.aws" }, true) },
            { "Asia Pacific (Hong Kong)",   new RegionInfo(new[]{ "ec2.ap-east-1.amazonaws.com","gamelift-ping.ap-east-1.api.aws" }, true) },

            // Oceania
            { "Asia Pacific (Sydney)",      new RegionInfo(new[]{ "gamelift.ap-southeast-2.amazonaws.com","gamelift-ping.ap-southeast-2.api.aws" }, true) },

            // Mainland China
            { "China (Beijing)",            new RegionInfo(new[]{ "gamelift.cn-north-1.amazonaws.com.cn" }, true) },
            { "China (Ningxia)",            new RegionInfo(new[]{ "gamelift.cn-northwest-1.amazonaws.com.cn" }, true) },
        };

        private MenuStrip      _menuStrip;
        private Label           _lblTip;
        private ListView        _lv;
        private FlowLayoutPanel _buttonPanel;
        private Button          _btnApply;
        private Button          _btnRevert;
        private Timer           _pingTimer;
        private enum ApplyMode { Gatekeep, UniversalRedirect }
        private ApplyMode _applyMode = ApplyMode.Gatekeep;
        private enum BlockMode { Both, OnlyPing, OnlyService }
        private BlockMode _blockMode = BlockMode.Both;
        private bool _mergeUnstable = true;

        // Tracks the last launched version for update message display
        private string _lastLaunchedVersion;

        // Path for saving user settings
        private static string SettingsFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Tigerbyte",
                "MakeYourChoice",
                "settings.json");

        // Hosts file section marker and path
        private const string SectionMarker = "# --+ Make Your Choice +--";
        private static string HostsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers\\etc\\hosts");

        private class UserSettings
        {
            public ApplyMode ApplyMode { get; set; }
            public BlockMode BlockMode { get; set; }
            public bool MergeUnstable { get; set; } = true;
            public string LastLaunchedVersion { get; set; }
        }

        private void LoadSettings()
        {
            try
            {
                var folder = Path.GetDirectoryName(SettingsFilePath);
                if (!Directory.Exists(folder))
                    return;
                if (!File.Exists(SettingsFilePath))
                    return;
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<UserSettings>(json);
                if (settings != null)
                {
                    _applyMode = settings.ApplyMode;
                    _blockMode = settings.BlockMode;
                    _mergeUnstable = settings.MergeUnstable;
                    _lastLaunchedVersion = settings.LastLaunchedVersion;
                }
            }
            catch
            {
                // ignore load errors
            }
            UpdateRegionListViewAppearance();
        }

        private void SaveSettings()
        {
            try
            {
                var folder = Path.GetDirectoryName(SettingsFilePath);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                var settings = new UserSettings
                {
                    ApplyMode = _applyMode,
                    BlockMode = _blockMode,
                    MergeUnstable = _mergeUnstable,
                    LastLaunchedVersion = string.IsNullOrWhiteSpace(_lastLaunchedVersion) ? CurrentVersion : _lastLaunchedVersion,
                };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // ignore save errors
            }
        }

        public Form1()
        {
            InitializeComponent();
            this.Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"));
            this.Shown += (_, __) => StartPingTimer();
            _ = CheckForUpdatesAsync(true);
            LoadSettings();
            // Show update message if version changed
            if (!string.Equals(CurrentVersion, _lastLaunchedVersion, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(UpdateMessage, $"What's new in {CurrentVersion}", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _lastLaunchedVersion = CurrentVersion;
                SaveSettings();
            }
            UpdateRegionListViewAppearance();
        }

        private void InitializeComponent()
        {
            // ── Form setup ────────────────────────────────────────────────
            Text            = "Make Your Choice (DbD Server Selector)";
            Width           = 405;
            Height          = 585;
            StartPosition   = FormStartPosition.CenterScreen;
            Padding         = new Padding(10);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize     = new Size(Width, 300);
            MaximumSize     = new Size(Width, Screen.PrimaryScreen.WorkingArea.Height);

            // ── MenuStrip ────────────────────────────────────────────────
            _menuStrip = new MenuStrip();

            var mSource = new ToolStripMenuItem($"v{CurrentVersion}");
            var miRepo  = new ToolStripMenuItem("Repository");
            miRepo.Click += (_,__) => OpenUrl(RepoUrl);
            var miAbout   = new ToolStripMenuItem("About");
            miAbout.Click += (_,__) => ShowAboutDialog();
            var miCheck  = new ToolStripMenuItem("Check for updates");
            miCheck.Click += async (_,__) => await CheckForUpdatesAsync(false);
            var miOpenHostsFolder = new ToolStripMenuItem("Open hosts file location");
            miOpenHostsFolder.Click += (_, __) =>
            {
                var hostsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "drivers\\etc");
                Process.Start(new ProcessStartInfo("explorer.exe", hostsFolder)
                {
                    UseShellExecute = true
                });
            };
            var miRestoreDefaultHosts = new ToolStripMenuItem("Reset hosts file");
            miRestoreDefaultHosts.Click += (_, __) => RestoreWindowsDefaultHostsFile();
            mSource.DropDownItems.Add(miCheck);
            mSource.DropDownItems.Add(miRepo);
            mSource.DropDownItems.Add(miAbout);
            mSource.DropDownItems.Add(miOpenHostsFolder);
            mSource.DropDownItems.Add(new ToolStripSeparator());
            mSource.DropDownItems.Add(miRestoreDefaultHosts);

            var mOptions = new ToolStripMenuItem("Options");
            var miSettings = new ToolStripMenuItem("Program settings");
            miSettings.Click += (_,__) => ShowSettingsDialog();
            mOptions.DropDownItems.Add(miSettings);

            var mHelp     = new ToolStripMenuItem("Help");
            var miWebsite = new ToolStripMenuItem("Website");
            var miDiscord = new ToolStripMenuItem("Discord (Get support)");
            miWebsite.Click += (_,__) => OpenUrl(WebsiteUrl);
            miDiscord.Click += (_,__) => OpenUrl(DiscordUrl);
            mHelp.DropDownItems.Add(miWebsite);
            mHelp.DropDownItems.Add(miDiscord);

            _menuStrip.Items.Add(mSource);
            _menuStrip.Items.Add(mOptions);
            _menuStrip.Items.Add(mHelp);

            // ── Tip label ────────────────────────────────────────────────
            _lblTip = new Label
            {
                Text        = "Tip: You can select multiple servers. The game will decide which one to use based on latency.",
                AutoSize    = true,
                MaximumSize = new Size(Width - Padding.Horizontal - 20, 0),
                TextAlign   = ContentAlignment.MiddleLeft,
                Padding     = new Padding(5),
                Margin      = new Padding(0, 0, 0, 10)
            };

            // ── ListView ─────────────────────────────────────────────────
            _lv = new ListView
            {
                View          = View.Details,
                CheckBoxes    = true,
                FullRowSelect = true,
                ShowGroups    = true,
                Dock          = DockStyle.Fill
            };
            _lv.ShowItemToolTips = true;
            // Enable double buffering to reduce flicker
            typeof(ListView).InvokeMember(
                "DoubleBuffered",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                null,
                _lv,
                new object[] { true }
            );
            _lv.Columns.Add("Server",  220);
            _lv.Columns.Add("Latency", 115);
            var grpEurope   = new ListViewGroup("Europe",     HorizontalAlignment.Left) { Name = "Europe" };
            var grpAmericas = new ListViewGroup("The Americas",HorizontalAlignment.Left) { Name = "Americas" };
            var grpAsia     = new ListViewGroup("Asia (Excl. Cn)",       HorizontalAlignment.Left) { Name = "Asia" };
            var grpOceania  = new ListViewGroup("Oceania",    HorizontalAlignment.Left) { Name = "Oceania" };
            var grpChina    = new ListViewGroup("Mainland China",      HorizontalAlignment.Left) { Name = "China" };
            _lv.Groups.AddRange(new[] { grpEurope, grpAmericas, grpAsia, grpOceania, grpChina });
            foreach (var kv in _regions)
            {
                var regionKey = kv.Key;
                var displayName = regionKey + (kv.Value.Stable ? string.Empty : " ⚠︎");
                var item = new ListViewItem(displayName)
                {
                    Tag = regionKey,
                    Group = _lv.Groups[GetGroupName(regionKey)],
                    Checked = false,
                    UseItemStyleForSubItems = false
                };
                if (!kv.Value.Stable)
                {
                    item.ForeColor = Color.Orange;
                    item.ToolTipText = "Unstable server: latency issues may occur.";
                }
                item.SubItems.Add("…");
                _lv.Items.Add(item);
            }

            // ── Buttons ─────────────────────────────────────────────────
            _btnApply  = new Button { Text = "Apply Selection",    AutoSize = true, Margin = new Padding(5) };
            _btnRevert = new Button { Text = "Revert to Default", AutoSize = true, Margin = new Padding(5) };
            _btnApply.Click  += BtnApply_Click;
            _btnRevert.Click += BtnRevert_Click;
            _buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock          = DockStyle.Bottom,
                Padding       = new Padding(5),
                AutoSize      = true
            };
            _buttonPanel.Controls.Add(_btnApply);
            _buttonPanel.Controls.Add(_btnRevert);

            // ── Layout ──────────────────────────────────────────────────
            var tlp = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 1,
                RowCount    = 4
            };
            tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // menu
            tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // tip
            tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons

            tlp.Controls.Add(_lv,          0, 0);
            tlp.Controls.Add(_menuStrip,   0, 1);
            tlp.Controls.Add(_lblTip,      0, 2);
            tlp.Controls.Add(_buttonPanel, 0, 3);

            Controls.Add(tlp);
        }

        private async Task CheckForUpdatesAsync(bool silent)
        {
            try
            {
                using var client = new HttpClient();
                // fetch all releases
                var url = $"https://codeberg.org/api/v1/repos/{Developer}/{Repo}/releases";
                var releases = await client.GetFromJsonAsync<List<Release>>(url);
                if (releases == null || releases.Count == 0)
                {
                    MessageBox.Show("No releases found.", "Check For Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // assume first is latest (API returns newest first)
                var latest = releases[0].TagName;
                if (string.Equals(latest, CurrentVersion, StringComparison.OrdinalIgnoreCase))
                {
                    if (!silent)
                    {
                        MessageBox.Show(
                            "You're already using the latest release! :D",
                            "Check For Updates",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
                }
                else
                {
                    var resp = MessageBox.Show(
                        $"A new version is available: {latest}.\nWould you like to update?\n\nYour version: {CurrentVersion}.",
                        "Update Available",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );
                    if (resp == DialogResult.Yes)
                        OpenUrl($"https://codeberg.org/{Developer}/{Repo}/releases");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error while checking for updates:\n" + ex.Message,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        // helper DTO for JSON deserialization
        private class Release
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; }
        }

        private void StartPingTimer()
        {
            var pinger = new Ping();
            _pingTimer = new Timer { Interval = 5_000 };
            _pingTimer.Tick += async (_, __) =>
            {
                // Collect ping results for all regions
                var results = new Dictionary<string, long>();
                foreach (ListViewItem item in _lv.Items)
                {
                    long ms;
                    try
                    {
                        var regionKey = (string)item.Tag;
                        var hosts = _regions[regionKey].Hosts;
                        var reply = await pinger.SendPingAsync(hosts[0], 2000);
                        ms = reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
                    }
                    catch
                    {
                        ms = -1;
                    }
                    results[(string)item.Tag] = ms;
                }

                // Update UI in one batch to avoid flicker (only after handles exist)
                if (!IsHandleCreated || IsDisposed || !_lv.IsHandleCreated || _lv.IsDisposed)
                    return;

                void UpdateLatencyUI()
                {
                    _lv.BeginUpdate();
                    try
                    {
                        foreach (ListViewItem item in _lv.Items)
                        {
                            var regionKey = (string)item.Tag;
                            var ms = results[regionKey];
                            var sub = item.SubItems[1];
                            sub.Text      = ms >= 0 ? $"{ms} ms" : "disconnected";
                            sub.ForeColor = GetColorForLatency(ms);
                            sub.Font      = new Font(sub.Font, FontStyle.Italic);
                        }
                    }
                    finally
                    {
                        _lv.EndUpdate();
                    }
                }

                if (InvokeRequired)
                    BeginInvoke((Action)UpdateLatencyUI);
                else
                    UpdateLatencyUI();
            };
            _pingTimer.Start();
        }

        private Color GetColorForLatency(long ms)
        {
            if (ms < 0)   return Color.Gray;
            if (ms < 80)  return Color.Green;
            if (ms < 130) return Color.Orange;
            if (ms < 250) return Color.Crimson;
            return Color.Purple;
        }

        private string GetGroupName(string region)
        {
            if (region.StartsWith("Europe")) return "Europe";
            if (region.StartsWith("US") || region.StartsWith("Canada") || region.StartsWith("South America"))
                return "Americas";
            if (region.Contains("Sydney")) return "Oceania";
            if (region.Contains("China"))  return "China";
            return "Asia";
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            // if universal redirect mode, redirect all endpoints to selected region's IPs
            if (_applyMode == ApplyMode.UniversalRedirect)
            {
                if (_lv.CheckedItems.Count != 1)
                {
                    MessageBox.Show(
                        "Please select only one server when using Universal Redirect mode.",
                        "Universal Redirect",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var regionKey = (string)_lv.CheckedItems[0].Tag;
                var hosts = _regions[regionKey].Hosts;
                var serviceHost = hosts[0];
                var pingHost    = hosts.Length > 1 ? hosts[1] : hosts[0];

                // resolve via DNS lookup to obtain IP addresses
                string svcIp, pingIp;
                try
                {
                    var svcAddrs  = Dns.GetHostAddresses(serviceHost);
                    var pingAddrs = Dns.GetHostAddresses(pingHost);
                    if (svcAddrs.Length == 0 || pingAddrs.Length == 0)
                        throw new Exception("DNS lookup returned no addresses");

                    svcIp  = svcAddrs[0].ToString();
                    pingIp = pingAddrs[0].ToString();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Failed to resolve IP addresses for Universal Redirect mode via DNS:\n" + ex.Message,
                        "Universal Redirect Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                try
                {
                    File.Copy(HostsPath, HostsPath + ".bak", true);

                    var sb = new StringBuilder();
                    sb.AppendLine("# Edited by Make Your Choice (DbD Server Selector)");
                    sb.AppendLine("# Universal Redirect mode: redirect all GameLift endpoints to selected region");
                    sb.AppendLine($"# Need help? Discord: {DiscordUrl}");
                    sb.AppendLine();

                    foreach (var kv in _regions)
                    {
                        var regionHosts = kv.Value.Hosts;
                        foreach (var h in regionHosts)
                        {
                            bool isPing = h.Contains("ping", StringComparison.OrdinalIgnoreCase);
                            var ip = isPing ? pingIp : svcIp;
                            sb.AppendLine($"{ip} {h}");
                        }
                        sb.AppendLine();
                    }

                    WriteWrappedHostsSection(sb.ToString());
                    try
                    {
                        var psi = new ProcessStartInfo("ipconfig", "/flushdns")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (var proc = Process.Start(psi))
                        {
                            proc.WaitForExit();
                        }
                    }
                    catch { /* ignore */ }
                    MessageBox.Show(
                        "The hosts file was updated successfully (Universal Redirect).\n\nPlease restart the game in order for changes to take effect.",
                        "Success",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show(
                        "Please run as Administrator to modify the hosts file.",
                        "Permission Denied",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                return;
            }

            // existing gatekeep mode logic
            if (_lv.CheckedItems.Count == 0)
            {
                MessageBox.Show(
                    "Please select at least one server to allow.",
                    "No Server Selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                File.Copy(HostsPath, HostsPath + ".bak", true);

                // Determine if user selected any stable servers
                var selectedRegions = _lv.CheckedItems.Cast<ListViewItem>()
                                        .Select(item => (string)item.Tag)
                                        .ToList();
                bool anyStableSelected = selectedRegions.Any(regionKey => _regions[regionKey].Stable);

                // If merge is on, ensure every selected unstable region has at least one stable alternative
                if (_mergeUnstable && !anyStableSelected)
                {
                    var missing = new List<string>();
                    foreach (var region in selectedRegions)
                    {
                        if (!_regions[region].Stable)
                        {
                            var group = GetGroupName(region);
                            bool stableExists = _regions.Any(kv => GetGroupName(kv.Key) == group && kv.Value.Stable);
                            if (!stableExists)
                                missing.Add(region);
                        }
                    }
                    if (missing.Count > 0)
                    {
                        MessageBox.Show(
                            "Merge unstable servers option is enabled, but no stable servers found for: " +
                            string.Join(", ", missing) + ".\nDisable merging unstable servers in the options menu or select a stable server manually.",
                            "No Stable Servers Found",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                }

                // Merge unstable servers with a stable alternative if enabled
                var allowedSet = new HashSet<string>(selectedRegions);
                if (_mergeUnstable && !anyStableSelected)
                {
                    var additional = new List<string>();
                    foreach (var region in allowedSet.ToList())
                    {
                        if (!_regions[region].Stable)
                        {
                            var group = GetGroupName(region);
                            var alternative = _regions.FirstOrDefault(kv => GetGroupName(kv.Key) == group && kv.Value.Stable);
                            if (!string.IsNullOrEmpty(alternative.Key))
                                additional.Add(alternative.Key);
                        }
                    }
                    foreach (var extra in additional)
                        allowedSet.Add(extra);
                }

                var sb = new StringBuilder();
                sb.AppendLine("# Edited by Make Your Choice (DbD Server Selector)");
                sb.AppendLine("# Unselected servers are blocked (Gatekeep Mode); selected servers are commented out.");
                sb.AppendLine($"# Need help? Discord: {DiscordUrl}");
                sb.AppendLine();

                foreach (ListViewItem item in _lv.Items)
                {
                    var regionKey = (string)item.Tag;
                    bool allow = allowedSet.Contains(regionKey);
                    var hosts = _regions[regionKey].Hosts;
                    foreach (var h in hosts)
                    {
                        bool isPing = h.Contains("ping", StringComparison.OrdinalIgnoreCase);
                        bool include = _blockMode == BlockMode.Both
                                       || (_blockMode == BlockMode.OnlyPing && isPing)
                                       || (_blockMode == BlockMode.OnlyService && !isPing);
                        if (!include)
                            continue;
                        var prefix = allow ? "#" : "0.0.0.0".PadRight(9);
                        sb.AppendLine($"{prefix} {h}");
                    }
                    sb.AppendLine();
                }

                WriteWrappedHostsSection(sb.ToString());
                try
                {
                    var psi = new ProcessStartInfo("ipconfig", "/flushdns")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using (var proc = Process.Start(psi))
                    {
                        proc.WaitForExit();
                    }
                }
                catch { /* ignore */ }
                MessageBox.Show(
                    "The hosts file was updated successfully (Gatekeep).\n\nPlease restart the game in order for changes to take effect.",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(
                    "Please run as Administrator to modify the hosts file.",
                    "Permission Denied",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void BtnRevert_Click(object sender, EventArgs e)
        {
            try
            {
                File.Copy(HostsPath, HostsPath + ".bak", true);
                WriteWrappedHostsSection(string.Empty);
                try
                {
                    var psi = new ProcessStartInfo("ipconfig", "/flushdns")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using (var proc = Process.Start(psi))
                    {
                        proc.WaitForExit();
                    }
                }
                catch { /* ignore */ }
                MessageBox.Show(
                    "Cleared Make Your Choice entries. Your existing hosts lines were left untouched.",
                    "Reverted",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(
                    "Please run as Administrator to modify the hosts file.",
                    "Permission Denied",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // Helper to write/update the wrapped hosts section (between SectionMarker lines)
        private void WriteWrappedHostsSection(string innerContent)
        {
            // Ensure Windows CRLF when writing
            string NormalizeToLf(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

            // Read current hosts (or empty if missing)
            string original = string.Empty;
            try { original = File.ReadAllText(HostsPath); } catch { /* ignore */ }

            string lf = NormalizeToLf(original);
            int first = lf.IndexOf(SectionMarker, StringComparison.Ordinal);
            int last  = first >= 0 ? lf.IndexOf(SectionMarker, first + SectionMarker.Length, StringComparison.Ordinal) : -1;

            // Build the new wrapped block (marker, content, marker) using LF first
            string innerLf = NormalizeToLf(innerContent ?? string.Empty);
            if (innerLf.Length > 0 && !innerLf.EndsWith("\n")) innerLf += "\n";
            string wrapped = SectionMarker + "\n" + innerLf + SectionMarker + "\n";

            string newLf;
            if (first >= 0 && last >= 0)
            {
                // Replace everything from first marker through the second marker
                int afterLast = last + SectionMarker.Length;
                newLf = lf.Substring(0, first) + wrapped + lf.Substring(afterLast);
            }
            else if (first >= 0 && last < 0)
            {
                // Corrupt/partial state: one marker only. Replace from that marker to end with a clean wrapped block.
                newLf = lf.Substring(0, first) + wrapped;
            }
            else
            {
                // No markers present: append two blank lines, then our wrapped block
                string suffix = (lf.EndsWith("\n") ? "\n" : "\n") + "\n" + wrapped; // ensures at least two newlines before the marker
                newLf = lf + suffix;
            }

            // Backup and write
            try { File.Copy(HostsPath, HostsPath + ".bak", true); } catch { /* ignore */ }
            try { File.WriteAllText(HostsPath, newLf.Replace("\n", "\r\n")); } catch { throw; }
        }

        private void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void ShowAboutDialog()
        {
            var about = new Form
            {
                Text            = "About Make Your Choice",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition   = FormStartPosition.CenterParent,
                ClientSize      = new Size(480, 140),
                MaximizeBox     = false,
                MinimizeBox     = false,
                ShowInTaskbar   = false,
                Padding         = new Padding(10)
            };

            var lblTitle = new Label
            {
                Text     = "Make Your Choice (DbD Server Selector)",
                Font     = new Font(Font.FontFamily, 10, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(10, 10)
            };

            var lblDeveloper = new LinkLabel
            {
                Text     = "Developer: Ky",
                Font     = new Font(Font.FontFamily, 8),
                AutoSize = true,
                Location = new Point(10, lblTitle.Bottom + 10)
            };
            lblDeveloper.Links.Add(11, 2, "https://kaneki.nz");
            lblDeveloper.LinkClicked += (s, e) =>
            {
                Process.Start(new ProcessStartInfo(e.Link.LinkData.ToString()) { UseShellExecute = true });
            };
            about.Controls.Add(lblDeveloper);

            var lblVersion = new Label
            {
                Text     = $"Version {CurrentVersion}\nWindows 7 Service Pack 1 or higher.",
                Font     = new Font(Font.FontFamily, 8, FontStyle.Italic),
                AutoSize = true,
                Location = new Point(10, lblDeveloper.Bottom + 10)
            };
            var btnOk = new Button
            {
                Text         = "Awesome!",
                DialogResult = DialogResult.OK,
                AutoSize     = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor       = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            btnOk.Location = new Point(
                about.ClientSize.Width - btnOk.Width - 10,
                about.ClientSize.Height - btnOk.Height - 10
            );

            about.Controls.Add(lblTitle);
            about.Controls.Add(lblVersion);
            about.Controls.Add(btnOk);
            about.AcceptButton = btnOk;
            about.ShowDialog(this);
        }
        private void ShowSettingsDialog()
        {
            var dialog = new Form
            {
                Text            = "Program Settings",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition   = FormStartPosition.CenterParent,
                //ClientSize      = new Size(350, 280),
                MaximizeBox     = false,
                MinimizeBox     = false,
                ShowInTaskbar   = false,
                Padding         = new Padding(10),
                AutoSize        = true,
                AutoSizeMode    = AutoSizeMode.GrowAndShrink
            };

            // ── Mode selection ─────────────────────────────────────────
            var modePanel = new GroupBox
            {
                Text     = "Method",
                Location = new Point(10, 10),
                Size     = new Size(320, 60)
            };
            var cbApplyMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location      = new Point(8, 20),
                Width         = 300
            };
            // populate mode choices
            cbApplyMode.Items.AddRange(new[] { "Gatekeep (default)", "Universal Redirect" });
            cbApplyMode.SelectedIndex = _applyMode == ApplyMode.UniversalRedirect ? 1 : 0;
            modePanel.Controls.Add(cbApplyMode);

            var blockPanel = new GroupBox
            {
                Text     = "Gatekeep Options",
                Location = new Point(10, modePanel.Bottom + 10),
                Width    = 320,
                Height   = 110,
                Padding  = new Padding(10)
            };
            var rbBoth = new RadioButton { Text = "Block both (default)", Location = new Point(10, 20), AutoSize = true };
            var rbPing = new RadioButton { Text = "Block UDP ping beacon endpoints", Location = new Point(10, rbBoth.Bottom + 5), AutoSize = true };
            var rbService = new RadioButton { Text = "Block service endpoints", Location = new Point(10, rbPing.Bottom + 5), AutoSize = true };
            blockPanel.Controls.AddRange(new Control[] { rbBoth, rbPing, rbService });

            // Initialize selections
            rbBoth.Checked    = _blockMode == BlockMode.Both;
            rbPing.Checked    = _blockMode == BlockMode.OnlyPing;
            rbService.Checked = _blockMode == BlockMode.OnlyService;
            blockPanel.Enabled = (_applyMode == ApplyMode.Gatekeep);


            // ── Merge unstable servers panel ─────────────────────────────
            var miscPanel = new GroupBox
            {
                Text = "Misc Options",
                Width = 320,
                Height = 50,
                Padding = new Padding(10)
            };
            var cbMergeUnstable = new CheckBox
            {
                Location = new Point(10, 20),
                Text = "Merge unstable servers (recommended)…",
                AutoSize = true,
                Checked = _mergeUnstable,
                MaximumSize = new Size(300, 0)
            };
            miscPanel.Controls.Add(cbMergeUnstable);
            var toolTipMerge = new ToolTip();
            toolTipMerge.SetToolTip(cbMergeUnstable,
                "Merge unstable servers with a stable alternative. (recommended)");
            // Disable merge option when not in Gatekeep mode
            cbMergeUnstable.Enabled = cbApplyMode.SelectedIndex == 0;
            // Toggle blockPanel and merge checkbox enabled state based on apply mode
            cbApplyMode.SelectedIndexChanged += (s, e) =>
            {
                bool isGatekeep = cbApplyMode.SelectedIndex == 0;
                blockPanel.Enabled = isGatekeep;
                cbMergeUnstable.Enabled = isGatekeep;
            };
            // Confirm before disabling merge option
            cbMergeUnstable.CheckedChanged += (s, e) =>
            {
                if (!cbMergeUnstable.Checked)
                {
                    var message = "Disabling this option means no stable alternative will be automatically set, " +
                                  "this will most likely cause severe latency issues. Are you sure you want to disable this?";
                    var result = MessageBox.Show(
                        message,
                        "Merge Unstable Servers",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (result == DialogResult.No)
                    {
                        cbMergeUnstable.Checked = true;
                    }
                }
            };

            // ── Tip label for settings ────────────────────────────────────────
            var lblTipSettings = new Label
            {
                Text = "Default options are recommended. If the default method doesn't work, try an alternative method above. Your experience may vary by using settings other than the default.",
                AutoSize = true,
                MaximumSize = new Size(320, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(5),
                Margin = new Padding(0, 0, 0, 10)
            };

            var btnOk = new Button
            {
                Text            = "Apply Changes",
                DialogResult    = DialogResult.OK,
                AutoSize        = true,
                AutoSizeMode    = AutoSizeMode.GrowAndShrink,
                Anchor          = AnchorStyles.Bottom | AnchorStyles.Right
            };
            var btnDefault = new Button
            {
                Text            = "Default Options",
                AutoSize        = true,
                AutoSizeMode    = AutoSizeMode.GrowAndShrink,
                Anchor          = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnDefault.Click += (s, e) =>
            {
                // Reset dialog controls to defaults
                cbApplyMode.SelectedIndex = 0;
                rbBoth.Checked = true;
                cbMergeUnstable.Checked = true;
            };

            // ── TableLayoutPanel for dynamic layout ──────────────────────────
            var tlpSettings = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 5,
            };
            tlpSettings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tlpSettings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tlpSettings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tlpSettings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tlpSettings.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            tlpSettings.Controls.Add(modePanel,       0, 0);
            tlpSettings.Controls.Add(blockPanel,      0, 1);
            tlpSettings.Controls.Add(miscPanel,      0, 2);
            tlpSettings.Controls.Add(lblTipSettings,  0, 3);
            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize      = true,
                Dock          = DockStyle.Fill
            };
            buttonPanel.Controls.Add(btnOk);
            buttonPanel.Controls.Add(btnDefault);
            tlpSettings.Controls.Add(buttonPanel, 0, 4);

            dialog.Controls.Add(tlpSettings);
            dialog.AcceptButton = btnOk;

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _applyMode = cbApplyMode.SelectedIndex == 1 ? ApplyMode.UniversalRedirect : ApplyMode.Gatekeep;
                if (_applyMode == ApplyMode.Gatekeep)
                {
                    if (rbBoth.Checked)       _blockMode = BlockMode.Both;
                    else if (rbPing.Checked)  _blockMode = BlockMode.OnlyPing;
                    else                      _blockMode = BlockMode.OnlyService;
                }
                _mergeUnstable = cbMergeUnstable.Checked;
                SaveSettings();
                UpdateRegionListViewAppearance();
            }
        }

        private void UpdateRegionListViewAppearance()
        {
            var defaultColor = _lv.ForeColor;
            foreach (ListViewItem item in _lv.Items)
            {
                var regionKey = (string)item.Tag;
                if (_mergeUnstable && !_regions[regionKey].Stable)
                {
                    item.Text = regionKey;
                    item.ForeColor = defaultColor;
                    item.ToolTipText = string.Empty;
                }
                else if (!_regions[regionKey].Stable)
                {
                    item.Text = regionKey + " ⚠︎";
                    item.ForeColor = Color.Orange;
                    item.ToolTipText = "Unstable server: latency issues may occur.";
                }
            }
        }

        private void RestoreWindowsDefaultHostsFile()
        {
            var confirm = MessageBox.Show(
                "If you are having problems, or the program doesn't seem to work correctly, try resetting your hosts file.\n\nThis will overwrite your entire hosts file with the Windows default.\n\nA backup will be saved as hosts.bak. Continue?",
                "Restore Windows default hosts file",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );
            if (confirm != DialogResult.Yes)
                return;

            try
            {
                // Backup current hosts
                try { File.Copy(HostsPath, HostsPath + ".bak", true); } catch { /* ignore backup errors */ }

                // Default Windows hosts file content (CRLF endings)
                var defaultHosts =
                    "# Copyright (c) 1993-2009 Microsoft Corp.\r\n" +
                    "#\r\n" +
                    "# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.\r\n" +
                    "#\r\n" +
                    "# This file contains the mappings of IP addresses to host names. Each\r\n" +
                    "# entry should be kept on an individual line. The IP address should\r\n" +
                    "# be placed in the first column followed by the corresponding host name.\r\n" +
                    "# The IP address and the host name should be separated by at least one\r\n" +
                    "# space.\r\n" +
                    "#\r\n" +
                    "# Additionally, comments (such as these) may be inserted on individual\r\n" +
                    "# lines or following the machine name denoted by a '#' symbol.\r\n" +
                    "#\r\n" +
                    "# For example:\r\n" +
                    "#\r\n" +
                    "#       102.54.94.97     rhino.acme.com          # source server\r\n" +
                    "#        38.25.63.10     x.acme.com              # x client host\r\n" +
                    "#\r\n" +
                    "# localhost name resolution is handled within DNS itself.\r\n" +
                    "#       127.0.0.1       localhost\r\n" +
                    "#       ::1             localhost\r\n";

                File.WriteAllText(HostsPath, defaultHosts);

                // Attempt to flush DNS (best effort)
                try
                {
                    var psi = new ProcessStartInfo("ipconfig", "/flushdns")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using (var proc = Process.Start(psi)) { proc.WaitForExit(); }
                }
                catch { /* ignore */ }

                MessageBox.Show(
                    "Hosts file restored to Windows default template.",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(
                    "Please run as Administrator to modify the hosts file.",
                    "Permission Denied",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}