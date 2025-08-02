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
        private const string CurrentVersion = "0.7.2";
        private const string Developer = "ky";
        private const string Repo  = "make-your-choice";

        // Holds endpoint list and stability flag for each region
        private record RegionInfo(string[] Hosts, bool Stable);
        private readonly Dictionary<string, RegionInfo> _regions = new()
        {
            // Europe
            { "Europe (London)",          new RegionInfo(new[]{ "gamelift.eu-west-2.amazonaws.com",    "gamelift-ping.eu-west-2.api.aws" }, false) },
            { "Europe (Ireland)",         new RegionInfo(new[]{ "gamelift.eu-west-1.amazonaws.com",    "gamelift-ping.eu-west-1.api.aws" }, true) },
            { "Europe (Frankfurt)",       new RegionInfo(new[]{ "gamelift.eu-central-1.amazonaws.com", "gamelift-ping.eu-central-1.api.aws" }, true) },

            // The Americas
            { "US East (N. Virginia)",    new RegionInfo(new[]{ "gamelift.us-east-1.amazonaws.com",    "gamelift-ping.us-east-1.api.aws" }, true) },
            { "US East (Ohio)",           new RegionInfo(new[]{ "gamelift.us-east-2.amazonaws.com",    "gamelift-ping.us-east-2.api.aws" }, true) },
            { "US West (N. California)",  new RegionInfo(new[]{ "gamelift.us-west-1.amazonaws.com",    "gamelift-ping.us-west-1.api.aws" }, true) },
            { "US West (Oregon)",         new RegionInfo(new[]{ "gamelift.us-west-2.amazonaws.com",    "gamelift-ping.us-west-2.api.aws" }, true) },
            { "Canada (Central)",         new RegionInfo(new[]{ "gamelift.ca-central-1.amazonaws.com", "gamelift-ping.ca-central-1.api.aws" }, false) },
            { "South America (São Paulo)",new RegionInfo(new[]{ "gamelift.sa-east-1.amazonaws.com",   "gamelift-ping.sa-east-1.api.aws" }, true) },

            // Asia (excluding China)
            { "Asia Pacific (Tokyo)",     new RegionInfo(new[]{ "gamelift.ap-northeast-1.amazonaws.com","gamelift-ping.ap-northeast-1.api.aws" }, true) },
            { "Asia Pacific (Seoul)",     new RegionInfo(new[]{ "gamelift.ap-northeast-2.amazonaws.com","gamelift-ping.ap-northeast-2.api.aws" }, true) },
            { "Asia Pacific (Mumbai)",    new RegionInfo(new[]{ "gamelift.ap-south-1.amazonaws.com",   "gamelift-ping.ap-south-1.api.aws" }, true) },
            { "Asia Pacific (Singapore)", new RegionInfo(new[]{ "gamelift.ap-southeast-1.amazonaws.com","gamelift-ping.ap-southeast-1.api.aws" }, true) },

            // Oceania
            { "Asia Pacific (Sydney)",    new RegionInfo(new[]{ "gamelift.ap-southeast-2.amazonaws.com","gamelift-ping.ap-southeast-2.api.aws" }, true) },

            // China
            { "China (Beijing)",          new RegionInfo(new[]{ "gamelift.cn-north-1.amazonaws.com.cn" }, true) },
            { "China (Ningxia)",          new RegionInfo(new[]{ "gamelift.cn-northwest-1.amazonaws.com.cn" }, true) },
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

        // Path for saving user settings
        private static string SettingsFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MakeYourChoice",
                "settings.json");

        private class UserSettings
        {
            public ApplyMode ApplyMode { get; set; }
            public BlockMode BlockMode { get; set; }
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
                }
            }
            catch
            {
                // ignore load errors
            }
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
                    BlockMode = _blockMode
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
            StartPingTimer();
            _ = CheckForUpdatesAsync(true);
            LoadSettings();
        }

        private void InitializeComponent()
        {
            // ── Form setup ────────────────────────────────────────────────
            Text            = "Make Your Choice (DbD Server Selector)";
            Width           = 460;
            Height          = 650;
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
            var miOpenHostsFolder = new ToolStripMenuItem("Open Hosts Folder");
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
            mSource.DropDownItems.Add(miCheck);
            mSource.DropDownItems.Add(miRepo);
            mSource.DropDownItems.Add(miAbout);
            mSource.DropDownItems.Add(miOpenHostsFolder);

            var mOptions = new ToolStripMenuItem("Options");
            var miSettings = new ToolStripMenuItem("Program settings");
            miSettings.Click += (_,__) => ShowSettingsDialog();
            mOptions.DropDownItems.Add(miSettings);

            var mHelp     = new ToolStripMenuItem("Help");
            var miWebsite = new ToolStripMenuItem("Website");
            var miDiscord = new ToolStripMenuItem("Discord");
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
            _lv.Columns.Add("Server",  280);
            _lv.Columns.Add("Latency", 120);
            var grpEurope   = new ListViewGroup("Europe",     HorizontalAlignment.Left) { Name = "Europe" };
            var grpAmericas = new ListViewGroup("The Americas",HorizontalAlignment.Left) { Name = "Americas" };
            var grpAsia     = new ListViewGroup("Asia (Excl. Cn)",       HorizontalAlignment.Left) { Name = "Asia" };
            var grpOceania  = new ListViewGroup("Oceania",    HorizontalAlignment.Left) { Name = "Oceania" };
            var grpChina    = new ListViewGroup("China",      HorizontalAlignment.Left) { Name = "China" };
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
                    item.ToolTipText = "Unstable server";
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
            _pingTimer = new Timer { Interval = 10_000 };
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

                // Update UI in one batch to avoid flicker
                _lv.Invoke((Action)(() =>
                {
                    _lv.BeginUpdate();
                    foreach (ListViewItem item in _lv.Items)
                    {
                        var regionKey = (string)item.Tag;
                        var ms = results[regionKey];
                        var sub = item.SubItems[1];
                        sub.Text      = ms >= 0 ? $"{ms} ms" : "disconnected";
                        sub.ForeColor = GetColorForLatency(ms);
                        sub.Font      = new Font(sub.Font, FontStyle.Italic);
                    }
                    _lv.EndUpdate();
                }));
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
            // Clear hosts file before applying changes
            var initialHostsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "drivers\\etc\\hosts");
            try
            {
                File.WriteAllText(initialHostsPath, string.Empty);
            }
            catch
            {
                // ignore errors clearing hosts file
            }
            // Flush DNS cache before proceeding (clear any cached lookups so hosts file emptiness takes effect)
            try
            {
                var flushInfo = new ProcessStartInfo("ipconfig", "/flushdns")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var flushProc = Process.Start(flushInfo))
                {
                    flushProc.WaitForExit();
                }
            }
            catch
            {
                // ignore DNS flush errors
            }
            // if universal redirect mode, redirect all endpoints to selected region's IPs
            if (_applyMode == ApplyMode.UniversalRedirect)
            {
                if (_lv.CheckedItems.Count != 1)
                {
                    MessageBox.Show(
                        "Please select exactly one region when using Universal Redirect mode.",
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
                    var hostsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "drivers\\etc\\hosts");
                    File.Copy(hostsPath, hostsPath + ".bak", true);

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

                    File.WriteAllText(hostsPath, sb.ToString());
                    // Flush DNS cache after writing hosts file (force OS to reload new entries)
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
                    catch
                    {
                        // ignore DNS flush errors
                    }
                    MessageBox.Show(
                        "Hosts file updated successfully!\nDNS cache flushed.",
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
                var hostsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "drivers\\etc\\hosts");
                File.Copy(hostsPath, hostsPath + ".bak", true);

                var sb = new StringBuilder();
                sb.AppendLine("# Edited by Make Your Choice (DbD Server Selector)");
                sb.AppendLine("# Unselected servers are blocked (Gatekeep Mode); selected servers are commented out.");
                sb.AppendLine($"# Need help? Discord: {DiscordUrl}");
                sb.AppendLine();

                foreach (ListViewItem item in _lv.Items)
                {
                    bool allow = item.Checked;
                    var regionKey = (string)item.Tag;
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

                File.WriteAllText(hostsPath, sb.ToString());
                // Flush DNS cache after writing hosts file (force OS to reload new entries)
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
                catch
                {
                    // ignore DNS flush errors
                }
                MessageBox.Show(
                    "Hosts file updated successfully!\nDNS cache flushed.",
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
            var hostsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "drivers\\etc\\hosts");
            try
            {
                File.Copy(hostsPath, hostsPath + ".bak", true);

                const string defaultHosts = @"# Copyright (c) 1993-2009 Microsoft Corp.
#
# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.
#
# This file contains the mappings of IP addresses to host names. Each
# entry should be kept on an individual line. The IP address should
# be placed in the first column followed by the corresponding host name.
# The IP address and the host name should be separated by at least one
# space.
#
# Additionally, comments (such as these) may be inserted on individual
# lines or following the machine name denoted by a '#' symbol.
#
# For example:
#
#       102.54.94.97     rhino.acme.com          # source server
#        38.25.63.10     x.acme.com              # x client host
#
# localhost name resolution is handled within DNS itself.
#       127.0.0.1       localhost
#       ::1             localhost
";
                File.WriteAllText(hostsPath, defaultHosts);
                MessageBox.Show(
                    "Hosts file reverted to the Windows default.",
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
                ClientSize      = new Size(350, 235),
                MaximizeBox     = false,
                MinimizeBox     = false,
                ShowInTaskbar   = false,
                Padding         = new Padding(10)
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

            // Toggle blockPanel enabled state based on apply mode
            cbApplyMode.SelectedIndexChanged += (s, e) =>
                blockPanel.Enabled = cbApplyMode.SelectedIndex == 0;

            var btnOk = new Button
            {
                Text            = "Apply",
                DialogResult    = DialogResult.OK,
                AutoSize        = true,
                AutoSizeMode    = AutoSizeMode.GrowAndShrink,
                Anchor          = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnOk.Location = new Point(
                dialog.ClientSize.Width - btnOk.Width - 10,
                dialog.ClientSize.Height - btnOk.Height - 10
            );

            dialog.Controls.AddRange(new Control[] { modePanel, blockPanel, btnOk });
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
                SaveSettings();
            }
        }
    }
}