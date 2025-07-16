using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Forms;

namespace MakeYourChoice
{
    public class Form1 : Form
    {
        // TODO: Fix these links
        private const string RepoUrl    = "https://codeberg.org/ky/make-your-choice";
        private const string WebsiteUrl = "https://kurocat.net";
        private const string DiscordUrl = "https://discord.gg/gnvtATeVc4";

        private readonly Dictionary<string, string[]> _regions = new()
        {
            // Europe
            { "Europe (London)",          new[]{ "gamelift.eu-west-2.amazonaws.com",    "gamelift-ping.eu-west-2.api.aws" } },
            { "Europe (Ireland)",         new[]{ "gamelift.eu-west-1.amazonaws.com",    "gamelift-ping.eu-west-1.api.aws" } },
            { "Europe (Frankfurt)",       new[]{ "gamelift.eu-central-1.amazonaws.com", "gamelift-ping.eu-central-1.api.aws" } },

            // The Americas
            { "US East (N. Virginia)",    new[]{ "gamelift.us-east-1.amazonaws.com",    "gamelift-ping.us-east-1.api.aws" } },
            { "US East (Ohio)",           new[]{ "gamelift.us-east-2.amazonaws.com",    "gamelift-ping.us-east-2.api.aws" } },
            { "US West (N. California)",  new[]{ "gamelift.us-west-1.amazonaws.com",    "gamelift-ping.us-west-1.api.aws" } },
            { "US West (Oregon)",         new[]{ "gamelift.us-west-2.amazonaws.com",    "gamelift-ping.us-west-2.api.aws" } },
            { "Canada (Central)",         new[]{ "gamelift.ca-central-1.amazonaws.com", "gamelift-ping.ca-central-1.api.aws" } },
            { "South America (São Paulo)",new[]{ "gamelift.sa-east-1.amazonaws.com",   "gamelift-ping.sa-east-1.api.aws" } },

            // Asia (excluding China)
            { "Asia Pacific (Tokyo)",     new[]{ "gamelift.ap-northeast-1.amazonaws.com","gamelift-ping.ap-northeast-1.api.aws" } },
            { "Asia Pacific (Seoul)",     new[]{ "gamelift.ap-northeast-2.amazonaws.com","gamelift-ping.ap-northeast-2.api.aws" } },
            { "Asia Pacific (Mumbai)",    new[]{ "gamelift.ap-south-1.amazonaws.com",   "gamelift-ping.ap-south-1.api.aws" } },
            { "Asia Pacific (Singapore)", new[]{ "gamelift.ap-southeast-1.amazonaws.com","gamelift-ping.ap-southeast-1.api.aws" } },

            // Oceania
            { "Asia Pacific (Sydney)",    new[]{ "gamelift.ap-southeast-2.amazonaws.com","gamelift-ping.ap-southeast-2.api.aws" } },

            // China
            { "China (Beijing)",          new[]{ "gamelift.cn-north-1.amazonaws.com.cn" } },
            { "China (Ningxia)",          new[]{ "gamelift.cn-northwest-1.amazonaws.com.cn" } },
        };

        private MenuStrip      _menuStrip;
        private Label           _lblTip;
        private ListView        _lv;
        private FlowLayoutPanel _buttonPanel;
        private Button          _btnApply;
        private Button          _btnRevert;
        private Timer           _pingTimer;

        public Form1()
        {
            InitializeComponent();
            this.Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"));
            StartPingTimer();
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
            var mSource = new ToolStripMenuItem("Source");
            var miRepo  = new ToolStripMenuItem("Repository");
            miRepo.Click += (_,__) => OpenUrl(RepoUrl);
            mSource.DropDownItems.Add(miRepo);

            var mHelp     = new ToolStripMenuItem("Help");
            var miWebsite = new ToolStripMenuItem("Website");
            var miDiscord = new ToolStripMenuItem("Discord");
            var miAbout   = new ToolStripMenuItem("About");
            miWebsite.Click += (_,__) => OpenUrl(WebsiteUrl);
            miDiscord.Click += (_,__) => OpenUrl(DiscordUrl);
            miAbout.Click   += (_,__) => ShowAboutDialog();
            mHelp.DropDownItems.Add(miWebsite);
            mHelp.DropDownItems.Add(miDiscord);
            mHelp.DropDownItems.Add(new ToolStripSeparator());
            mHelp.DropDownItems.Add(miAbout);

            _menuStrip.Items.Add(mSource);
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
                var item = new ListViewItem(kv.Key) {
                    Group = _lv.Groups[GetGroupName(kv.Key)],
                    Checked = false
                };
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

        private void StartPingTimer()
        {
            var pinger = new Ping();
            _pingTimer = new Timer { Interval = 10_000 };
            _pingTimer.Tick += async (_,__) =>
            {
                foreach (ListViewItem item in _lv.Items)
                {
                    long ms;
                    try
                    {
                        var reply = await pinger.SendPingAsync(_regions[item.Text][0], 2000);
                        ms = reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
                    }
                    catch
                    {
                        ms = -1;
                    }

                    _lv.Invoke((Action)(() =>
                    {
                        var sub = item.SubItems[1];
                        sub.Text      = ms >= 0 ? $"{ms} ms" : "err";
                        sub.ForeColor = GetColorForLatency(ms);
                    }));
                }
            };
            _pingTimer.Start();
        }

        private Color GetColorForLatency(long ms)
        {
            if (ms < 0)   return Color.Gray;
            if (ms < 80)  return Color.Green;
            if (ms < 110) return Color.Yellow;
            if (ms < 160) return Color.Orange;
            return Color.Red;
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
            if (_lv.CheckedItems.Count == 0)
            {
                MessageBox.Show(
                    "Please select at least one server to allow.",
                    "No Server Selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var hostsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "drivers\\etc\\hosts");
            try
            {
                File.Copy(hostsPath, hostsPath + ".bak", true);

                var sb = new StringBuilder();
                sb.AppendLine("# Edited by Make Your Choice (DbD Server Selector)");
                sb.AppendLine("# Unselected servers are blocked; selected servers are commented out.");
                sb.AppendLine("# Need help? Discord: https://discord.gg/gnvtATeVc4");
                sb.AppendLine();

                foreach (ListViewItem item in _lv.Items)
                {
                    bool allow = item.Checked;
                    foreach (var h in _regions[item.Text])
                    {
                        var prefix = allow ? "#" : "0.0.0.0".PadRight(9);
                        sb.AppendLine($"{prefix} {h}");
                    }
                    sb.AppendLine();
                }

                File.WriteAllText(hostsPath, sb.ToString());
                MessageBox.Show(
                    "Hosts file updated successfully!",
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
                LinkArea = new LinkArea(11, 2),
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
                Text     = "Version 0.6.1\nWindows 7 Service Pack 1 or higher.",
                Font     = new Font(Font.FontFamily, 8, FontStyle.Italic),
                AutoSize = true,
                Location = new Point(10, lblDeveloper.Bottom + 10)
            };
            var btnOk = new Button
            {
                Text         = "Awesome!",
                DialogResult = DialogResult.OK,
                Size         = new Size(125, 25),
                Location     = new Point(about.ClientSize.Width - 85, about.ClientSize.Height - 35)
            };

            about.Controls.Add(lblTitle);
            about.Controls.Add(lblVersion);
            about.Controls.Add(btnOk);
            about.AcceptButton = btnOk;
            about.ShowDialog(this);
        }
    }
}