using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using YamlDotNet.Serialization;
// Windows 11-style themed message boxes (drop-in for System.Windows.MessageBox)
using MessageBox = MakeYourChoice.Dialogs;

namespace MakeYourChoice
{
    // One row of the Servers list: either a group divider or a checkable region.
    public class RegionRow : INotifyPropertyChanged
    {
        public string Key { get; init; }
        public bool IsDivider { get; init; }

        private string _display;
        public string Display { get => _display; set { _display = value; Raise(nameof(Display)); } }

        private bool _isChecked;
        public bool IsChecked { get => _isChecked; set { _isChecked = value; Raise(nameof(IsChecked)); } }

        private string _latencyText = "…";
        public string LatencyText { get => _latencyText; set { _latencyText = value; Raise(nameof(LatencyText)); } }

        private Brush _latencyBrush = Brushes.Gray;
        public Brush LatencyBrush { get => _latencyBrush; set { _latencyBrush = value; Raise(nameof(LatencyBrush)); } }

        private string _statusText = "Unknown";
        public string StatusText { get => _statusText; set { _statusText = value; Raise(nameof(StatusText)); } }

        private Brush _statusBrush = Brushes.Gray;
        public Brush StatusBrush { get => _statusBrush; set { _statusBrush = value; Raise(nameof(StatusBrush)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private const string DiscordUrl = "https://discord.gg/xEMyAA8gn8";
        private const string Repo = "make-your-choice"; // Repository name
        private string Developer; // Fetched from API
        private string RepoUrl => Developer != null ? $"https://github.com/{Developer}/{Repo}" : null;
        private static readonly string CurrentVersion = LoadVersion();

        private class VersionInfo
        {
            public string Version { get; set; }
        }

        // Read just the app version from the embedded VERSINF.yml (shown in the sidebar and used for
        // update checks).
        private static string LoadVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "MakeYourChoice.VERSINF.yml";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                using (var reader = new StreamReader(stream))
                {
                    var yaml = reader.ReadToEnd();
                    var deserializer = new DeserializerBuilder()
                        .IgnoreUnmatchedProperties()
                        .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
                        .Build();
                    var versionInfo = deserializer.Deserialize<VersionInfo>(yaml);
                    return versionInfo.Version;
                }
            }
            catch
            {
                return "v0.0.0";
            }
        }

        // Holds the endpoint list for each region
        private record RegionInfo(string[] Hosts);
        private readonly Dictionary<string, RegionInfo> _regions = new()
        {
            // Europe
            { "Europe (London)",            new RegionInfo(new[]{ "gamelift.eu-west-2.amazonaws.com",    "gamelift-ping.eu-west-2.api.aws" }) },
            { "Europe (Ireland)",           new RegionInfo(new[]{ "gamelift.eu-west-1.amazonaws.com",    "gamelift-ping.eu-west-1.api.aws" }) },
            { "Europe (Frankfurt am Main)", new RegionInfo(new[]{ "gamelift.eu-central-1.amazonaws.com", "gamelift-ping.eu-central-1.api.aws" }) },

            // The Americas
            { "US East (N. Virginia)",      new RegionInfo(new[]{ "gamelift.us-east-1.amazonaws.com",    "gamelift-ping.us-east-1.api.aws" }) },
            { "US East (Ohio)",             new RegionInfo(new[]{ "gamelift.us-east-2.amazonaws.com",    "gamelift-ping.us-east-2.api.aws" }) },
            { "US West (N. California)",    new RegionInfo(new[]{ "gamelift.us-west-1.amazonaws.com",    "gamelift-ping.us-west-1.api.aws" }) },
            { "US West (Oregon)",           new RegionInfo(new[]{ "gamelift.us-west-2.amazonaws.com",    "gamelift-ping.us-west-2.api.aws" }) },
            { "Canada (Central)",           new RegionInfo(new[]{ "gamelift.ca-central-1.amazonaws.com", "gamelift-ping.ca-central-1.api.aws" }) },
            { "South America (São Paulo)",  new RegionInfo(new[]{ "gamelift.sa-east-1.amazonaws.com",   "gamelift-ping.sa-east-1.api.aws" }) },

            // Asia (excluding Mainland China)
            { "Asia Pacific (Tokyo)",       new RegionInfo(new[]{ "gamelift.ap-northeast-1.amazonaws.com","gamelift-ping.ap-northeast-1.api.aws" }) },
            { "Asia Pacific (Seoul)",       new RegionInfo(new[]{ "gamelift.ap-northeast-2.amazonaws.com","gamelift-ping.ap-northeast-2.api.aws" }) },
            { "Asia Pacific (Mumbai)",      new RegionInfo(new[]{ "gamelift.ap-south-1.amazonaws.com",   "gamelift-ping.ap-south-1.api.aws" }) },
            { "Asia Pacific (Singapore)",   new RegionInfo(new[]{ "gamelift.ap-southeast-1.amazonaws.com","gamelift-ping.ap-southeast-1.api.aws" }) },
            { "Asia Pacific (Hong Kong)",   new RegionInfo(new[]{ "ec2.ap-east-1.amazonaws.com","gamelift-ping.ap-east-1.api.aws" }) },

            // Oceania
            { "Asia Pacific (Sydney)",      new RegionInfo(new[]{ "gamelift.ap-southeast-2.amazonaws.com","gamelift-ping.ap-southeast-2.api.aws" }) },
        };

        // These regions are always blocked regardless of user choice. DbD doesn't use them so they're not shown in the UI. They are just blocked for stability purposes.
        private readonly Dictionary<string, RegionInfo> _blockedRegions = new()
        {
            { "Africa (Cape Town)",         new RegionInfo(new[]{ "gamelift.af-south-1.amazonaws.com",     "gamelift-ping.af-south-1.api.aws" }) },
            { "Asia Pacific (Osaka)",       new RegionInfo(new[]{ "gamelift.ap-northeast-3.amazonaws.com","gamelift-ping.ap-northeast-3.api.aws" }) },
            { "Europe (Stockholm)",         new RegionInfo(new[]{ "gamelift.eu-north-1.amazonaws.com",    "gamelift-ping.eu-north-1.api.aws" }) },
            { "Europe (Paris)",             new RegionInfo(new[]{ "gamelift.eu-west-3.amazonaws.com",     "gamelift-ping.eu-west-3.api.aws" }) },
            { "Europe (Milan)",             new RegionInfo(new[]{ "gamelift.eu-south-1.amazonaws.com",    "gamelift-ping.eu-south-1.api.aws" }) },
            { "Middle East (Bahrain)",      new RegionInfo(new[]{ "gamelift.me-south-1.amazonaws.com",    "gamelift-ping.me-south-1.api.aws" }) },
            { "Asia Pacific (Malaysia)",    new RegionInfo(new[]{ "gamelift.ap-southeast-5.amazonaws.com", "gamelift-ping.ap-southeast-5.api.aws" }) },
            { "Asia Pacific (Thailand)",    new RegionInfo(new[]{ "gamelift.ap-southeast-7.amazonaws.com", "gamelift-ping.ap-southeast-7.api.aws" }) },
            { "China (Beijing)",            new RegionInfo(new[]{ "gamelift.cn-north-1.amazonaws.com.cn",  "gamelift-ping.cn-north-1.api.aws" }) },
            { "China (Ningxia)",            new RegionInfo(new[]{ "gamelift.cn-northwest-1.amazonaws.com.cn", "gamelift-ping.cn-northwest-1.api.aws" }) },
        };

        // Enforced = Gatekeep hosts PLUS a firewall enforcement
        private enum ApplyMode { Gatekeep, Enforced, UniversalRedirect }
        private ApplyMode _applyMode = ApplyMode.Gatekeep;
        private enum BlockMode { Both, OnlyPing, OnlyService }
        private BlockMode _blockMode = BlockMode.Both;
        // App theme: follow Windows, or force light/dark. Replaces the old DarkMode checkbox.
        private enum ThemeSetting { System, Light, Dark }
        private ThemeSetting _theme = ThemeSetting.System;
        private string _gamePath;
        // Enforced mode also firewall-blocks the game-server data plane (UDP) of every region you did
        // NOT choose, so DBD's fallback (e.g. N. Virginia) can't place you there. Derived from the
        // apply method — there is no separate hard-lock flag anymore.
        private bool _useHardLock => _applyMode == ApplyMode.Enforced;
        // When minimized, hide to the system tray instead of the taskbar (default on).
        private bool _minimizeToTray = true;
        // Notify (tray balloon) when the preferred server transitions offline -> online.
        private bool _notifyServerOnline = false;
        // How often (seconds) the Dead by Queue poll runs. Fixed at 30s.
        private const int PollIntervalSeconds = 30;
        // Start automatically at Windows logon (via a scheduled task so the elevated app launches
        // without a UAC prompt each login).
        private bool _startWithWindows = false;
        private const string AutoStartTaskName = "MakeYourChoice AutoStart";
        // Set when launched by the autostart task (--autostart): start in the tray, or minimized
        // to the taskbar if the tray is disabled.
        private bool _startMinimized = false;
        // Last session's ticked regions, restored on launch so the selection (and any matching
        // firewall rules) is repopulated. Updated whenever settings are saved.
        private List<string> _savedSelection = new();

        private readonly ObservableCollection<RegionRow> _rows = new();
        private TrafficSniffer _sniffer;
        private bool _snifferStarted;
        private AwsIpService _awsService;
        private string _lastDetectedIp;
        private int _lastDetectedPort;
        private string _lastDetectedRegion;
        private DispatcherTimer _pingTimer;
        private DispatcherTimer _connectionTooltipTimer;
        private DateTime? _lastConnectionUpdate;
        private bool _closed;

        private string _autoUpdateCheckPausedUntil;

        private enum Tab { Servers, Misc, Options }
        private Tab _tab = Tab.Servers;

        // Path for saving user settings
        private static string SettingsFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MakeYourChoice",
                "config.yaml");

        // Hosts file section marker and path
        private const string SectionMarker = "# --+ Make Your Choice +--";
        private static string HostsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers\\etc\\hosts");

        private class UserSettings
        {
            // ApplyMode.Enforced replaces the retired UseHardRegionLock flag (firewall). Theme replaces
            // the retired DarkMode checkbox (kept below for migration). Old configs holding retired
            // keys load fine thanks to IgnoreUnmatchedProperties.
            public ApplyMode ApplyMode { get; set; }
            public BlockMode BlockMode { get; set; }
            public string GamePath { get; set; }
            public string AutoUpdateCheckPausedUntil { get; set; }
            public bool DarkMode { get; set; }
            public string Theme { get; set; }
            public bool MinimizeToTray { get; set; } = true;
            public bool NotifyServerOnline { get; set; }
            public bool StartWithWindows { get; set; }
            public List<string> SelectedRegions { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();

            VersionText.Text = CurrentVersion;

            _startMinimized = Environment.GetCommandLineArgs().Any(a =>
                string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

            try
            {
                Icon = BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "icon.ico")),
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            catch { /* icon is cosmetic */ }

            // Initialize AWS Service and Sniffer
            _awsService = new AwsIpService();
            _sniffer = new TrafficSniffer();
            _sniffer.TrafficDetected += OnTrafficDetected;

            BuildRegionRows();
            RegionList.ItemsSource = _rows;

            LoadSettings();
            // Self-heal autostart: a portable, version-named exe gets moved/renamed between updates,
            // which leaves the scheduled task pointing at a stale path that no longer exists (so it
            // silently fails at logon). If autostart is enabled, re-point the task at THIS exe on
            // every launch. Background + only when the registered path differs, to avoid needless work.
            if (_startWithWindows)
                Task.Run(() => RefreshAutoStartIfStale());

            ApplyThemeSetting();
            // Re-assert the Windows accent whenever the theme flips (e.g. system light/dark change).
            ApplicationThemeManager.Changed += (_, __) => ApplyWindowsAccent();
            SetTab(Tab.Servers);

            _connectionTooltipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _connectionTooltipTimer.Tick += (_, __) => UpdateConnectionTooltip();
            _connectionTooltipTimer.Start();
            UpdateConnectionTooltip();

            Loaded += async (_, __) =>
            {
                StartSniffer();
                StartPingTimer();
                SetupTray();
                StartDbqTimer();

                // Auto-started at logon: go straight to the tray, or minimize to the taskbar if the
                // tray is disabled. Deferred to ContextIdle so WPF-UI's post-load window setup
                // (backdrop/theme) doesn't re-show the window after we hide it.
                if (_startMinimized)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_minimizeToTray && _tray != null)
                            Hide();
                        else
                            WindowState = WindowState.Minimized;
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }

                await FetchGitIdentityAsync();
                _ = CheckForUpdatesAsync(true);
            };
        }

        // ── Region list ─────────────────────────────────────────────

        private void BuildRegionRows()
        {
            var groupOrder = new (string Key, string Label)[]
            {
                ("Europe", "Europe"),
                ("Americas", "The Americas"),
                ("Asia", "Asia (Excl. Cn)"),
                ("Oceania", "Oceania")
            };

            foreach (var (key, label) in groupOrder)
            {
                _rows.Add(new RegionRow { IsDivider = true, Display = label });
                foreach (var kv in _regions.Where(kv => GetGroupName(kv.Key) == key))
                {
                    _rows.Add(new RegionRow
                    {
                        Key = kv.Key,
                        Display = kv.Key,
                    });
                }
            }
        }

        private IEnumerable<RegionRow> RegionRowsOnly() => _rows.Where(r => !r.IsDivider);

        // Region keys currently ticked in the main list. Also refreshes _savedSelection so it stays current.
        private List<string> GetCheckedRegionKeys()
        {
            var list = RegionRowsOnly().Where(r => r.IsChecked).Select(r => r.Key).ToList();
            _savedSelection = list;
            return list;
        }

        // Re-tick the regions selected in the previous session.
        private void RestoreSelection()
        {
            if (_savedSelection == null || _savedSelection.Count == 0)
                return;
            var set = new HashSet<string>(_savedSelection);
            foreach (var row in RegionRowsOnly())
                row.IsChecked = set.Contains(row.Key);
        }

        // ── Settings ────────────────────────────────────────────────

        private void LoadSettings()
        {
            try
            {
                if (Directory.Exists(Path.GetDirectoryName(SettingsFilePath)) && File.Exists(SettingsFilePath))
                {
                    var yaml = File.ReadAllText(SettingsFilePath);
                    // IgnoreUnmatchedProperties so retired keys (e.g. PollIntervalSeconds) in an older
                    // config don't fail the whole load.
                    var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
                    var settings = deserializer.Deserialize<UserSettings>(yaml);
                    if (settings != null)
                    {
                        _applyMode = settings.ApplyMode;
                        // Migrate the retired hard-lock flag: it used to be a bool layered on Gatekeep and
                        // is now the Enforced apply method. The property is gone, so read it from the raw YAML.
                        if (_applyMode == ApplyMode.Gatekeep &&
                            System.Text.RegularExpressions.Regex.IsMatch(
                                yaml, @"(?im)^\s*UseHardRegionLock\s*:\s*true\s*$"))
                            _applyMode = ApplyMode.Enforced;
                        _blockMode = settings.BlockMode;
                        _gamePath = settings.GamePath;
                        _autoUpdateCheckPausedUntil = settings.AutoUpdateCheckPausedUntil;
                        // Theme replaces the retired DarkMode checkbox: migrate an old DarkMode=true to Dark.
                        if (!string.IsNullOrEmpty(settings.Theme) && Enum.TryParse<ThemeSetting>(settings.Theme, true, out var theme))
                            _theme = theme;
                        else
                            _theme = settings.DarkMode ? ThemeSetting.Dark : ThemeSetting.System;
                        _minimizeToTray = settings.MinimizeToTray;
                        _notifyServerOnline = settings.NotifyServerOnline;
                        _startWithWindows = settings.StartWithWindows;
                        _savedSelection = settings.SelectedRegions ?? new List<string>();
                    }
                }
            }
            catch
            {
                // ignore load errors
            }
            RestoreSelection();
            SyncOptionsUi();
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
                    GamePath = _gamePath,
                    AutoUpdateCheckPausedUntil = _autoUpdateCheckPausedUntil,
                    DarkMode = _theme == ThemeSetting.Dark, // kept for config back-compat
                    Theme = _theme.ToString(),
                    MinimizeToTray = _minimizeToTray,
                    NotifyServerOnline = _notifyServerOnline,
                    StartWithWindows = _startWithWindows,
                    SelectedRegions = GetCheckedRegionKeys(),
                };
                var serializer = new SerializerBuilder().Build();
                File.WriteAllText(SettingsFilePath, serializer.Serialize(settings));
            }
            catch
            {
                // ignore save errors
            }
        }

        // Create/remove a logon scheduled task that launches the app elevated at startup. A
        // scheduled task with highest privileges avoids the per-login UAC prompt a Run-key entry
        // would trigger for this admin-required app.
        private static void SetAutoStart(bool enable)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                if (enable)
                {
                    var exe = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exe)) return;
                    psi.ArgumentList.Add("/create");
                    psi.ArgumentList.Add("/tn"); psi.ArgumentList.Add(AutoStartTaskName);
                    psi.ArgumentList.Add("/tr"); psi.ArgumentList.Add("\"" + exe + "\" --autostart");
                    psi.ArgumentList.Add("/sc"); psi.ArgumentList.Add("onlogon");
                    psi.ArgumentList.Add("/rl"); psi.ArgumentList.Add("highest");
                    psi.ArgumentList.Add("/f");
                }
                else
                {
                    psi.ArgumentList.Add("/delete");
                    psi.ArgumentList.Add("/tn"); psi.ArgumentList.Add(AutoStartTaskName);
                    psi.ArgumentList.Add("/f");
                }
                using var p = Process.Start(psi);
                p?.WaitForExit();
            }
            catch { /* ignore */ }
        }

        // If autostart is enabled but the scheduled task points at a different (stale) exe path than
        // the one running now, re-register it at the current path.
        private static void RefreshAutoStartIfStale()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                var psi = new ProcessStartInfo("schtasks")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add("/query");
                psi.ArgumentList.Add("/tn"); psi.ArgumentList.Add(AutoStartTaskName);
                psi.ArgumentList.Add("/fo"); psi.ArgumentList.Add("list");
                psi.ArgumentList.Add("/v");
                string output;
                using (var p = Process.Start(psi))
                {
                    if (p == null) return;
                    output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0) { SetAutoStart(true); return; } // task missing -> create it
                }
                // The registered command must reference the exe running right now; if not, re-point it.
                if (output.IndexOf(exe, StringComparison.OrdinalIgnoreCase) < 0)
                    SetAutoStart(true);
            }
            catch { /* best effort */ }
        }

        // ── Theme ───────────────────────────────────────────────────

        private void ApplyThemeSetting()
        {
            // UnWatch throws before the window is loaded; it's also only needed to undo an earlier
            // Watch, which can only have happened after load.
            switch (_theme)
            {
                case ThemeSetting.Light:
                    if (IsLoaded) SystemThemeWatcher.UnWatch(this);
                    ApplicationThemeManager.Apply(ApplicationTheme.Light, Wpf.Ui.Controls.WindowBackdropType.Mica);
                    break;
                case ThemeSetting.Dark:
                    if (IsLoaded) SystemThemeWatcher.UnWatch(this);
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.Mica);
                    break;
                default:
                    ApplicationThemeManager.ApplySystemTheme();
                    SystemThemeWatcher.Watch(this, Wpf.Ui.Controls.WindowBackdropType.Mica, false);
                    break;
            }
            ApplyWindowsAccent();
        }

        // Use the Windows accent color for the app's accent (Primary buttons, toggles, checkboxes),
        // same approach as Aero Cut: read DWM's AccentColor and hand it to WPF-UI.
        private void ApplyWindowsAccent()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (key?.GetValue("AccentColor") is int raw)
                {
                    uint v = unchecked((uint)raw);
                    var color = Color.FromRgb(
                        (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
                    ApplicationAccentColorManager.Apply(color, ApplicationThemeManager.GetAppTheme(), false, true);
                }
            }
            catch { /* accent is cosmetic */ }
        }

        // ── Sidebar navigation ──────────────────────────────────────

        private void SetTab(Tab tab)
        {
            _tab = tab;
            NavServers.Appearance = tab == Tab.Servers ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
            NavMisc.Appearance = tab == Tab.Misc ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
            NavOptions.Appearance = tab == Tab.Options ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
            ServersPanel.Visibility = tab == Tab.Servers ? Visibility.Visible : Visibility.Collapsed;
            MiscPanel.Visibility = tab == Tab.Misc ? Visibility.Visible : Visibility.Collapsed;
            OptionsPanel.Visibility = tab == Tab.Options ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnNavServers(object sender, RoutedEventArgs e) => SetTab(Tab.Servers);
        private void OnNavMisc(object sender, RoutedEventArgs e) => SetTab(Tab.Misc);
        private void OnNavOptions(object sender, RoutedEventArgs e) => SetTab(Tab.Options);

        private void OnKofi(object sender, RoutedEventArgs e) => OpenUrl("https://ko-fi.com/kylo");

        private void OnGitHub(object sender, RoutedEventArgs e)
        {
            if (RepoUrl == null)
            {
                MessageBox.Show(this,
                    "Unable to open repository.\n\nThe application was unable to fetch the git identity and therefore couldn't determine the repository URL.\n\nThis may be due to network issues or GitHub API issues.\nAn update to fix this issue has most likely been released, please check manually by joining the Discord server or doing a web search.",
                    "Repository",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                var result = MessageBox.Show(this,
                    "Pressing \"OK\" will open the project's public repository.\n\nPlease star the repository if you are able to do so as it increases awareness of the project! <3",
                    "Repository",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Information);
                if (result == MessageBoxResult.OK)
                    OpenUrl(RepoUrl);
            }
        }

        private void OnAbout(object sender, RoutedEventArgs e) =>
            Dialogs.ShowAbout(this, CurrentVersion, Developer);

        // Sidebar Update button: only visible once a check has found a newer release.
        private void OnUpdateNow(object sender, RoutedEventArgs e)
        {
            if (Developer != null)
                OpenUrl($"https://github.com/{Developer}/{Repo}/releases/latest");
        }

        // ── Firewall / hard lock helpers ────────────────────────────

        // Extract the AWS region code (e.g. "us-east-2") from a GameLift hostname such as
        // "gamelift.us-east-2.amazonaws.com" or "gamelift-ping.us-east-2.api.aws".
        private static string AwsCodeFromHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return null;
            var parts = host.Split('.');
            return parts.Length > 1 ? parts[1] : null;
        }

        private string AwsCodeForRegion(string regionKey)
        {
            if (_regions.TryGetValue(regionKey, out var info) && info.Hosts.Length > 0)
                return AwsCodeFromHost(info.Hosts[0]);
            return null;
        }

        // AWS region codes to firewall-block when the hard lock is on: every known region whose
        // game-server data plane should be unreachable, i.e. all regions NOT in allowedRegions.
        private HashSet<string> ComputeHardLockBlockCodes(IEnumerable<string> allowedRegions)
        {
            var allowedCodes = allowedRegions
                .Select(AwsCodeForRegion)
                .Where(c => c != null)
                .ToHashSet();

            var blockCodes = new HashSet<string>();
            foreach (var key in _regions.Keys)
            {
                var c = AwsCodeForRegion(key);
                if (c != null && !allowedCodes.Contains(c)) blockCodes.Add(c);
            }
            foreach (var kv in _blockedRegions)
            {
                var c = AwsCodeFromHost(kv.Value.Hosts.Length > 0 ? kv.Value.Hosts[0] : null);
                if (c != null && !allowedCodes.Contains(c)) blockCodes.Add(c);
            }
            return blockCodes;
        }

        // Apply or remove the firewall hard lock to match the current toggle + allowed selection.
        private async Task<(bool ok, string message)> ReconcileHardLockAsync(IEnumerable<string> allowedRegions)
        {
            if (!_useHardLock)
            {
                await Task.Run(() => FirewallManager.RemoveLock());
                return (true, "Hard region lock is off; firewall rules removed.");
            }
            var blockCodes = ComputeHardLockBlockCodes(allowedRegions);
            // Show the title-bar progress chip while the (potentially slow) firewall rules are written.
            SetFirewallProgress(true);
            try
            {
                return await FirewallManager.ApplyLockAsync(_awsService, blockCodes, GetDbdExePaths());
            }
            finally
            {
                SetFirewallProgress(false);
            }
        }

        // Show/hide the "Writing firewall rules…" progress bar next to the apply buttons (thread-safe).
        private void SetFirewallProgress(bool active)
        {
            if (_closed) return;
            void Apply() => FirewallProgressPanel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (Dispatcher.CheckAccess()) Apply();
            else Dispatcher.BeginInvoke((Action)Apply);
        }

        // Every DBD executable under the configured game folder, so the hard lock can be scoped to the
        // game only. Empty if none are found, which blocks Enforced mode (see EnsureHardLockCanBeScoped).
        private List<string> GetDbdExePaths() => GameInstalls.Find(_gamePath);

        /// <summary>
        /// Gate for Enforced mode: true when the lock can be scoped to real DBD executables. When it
        /// can't, tells the user why and offers to open the Options tab so they can fix it.
        /// Returns false if the caller should abort. Always true when Enforced is off.
        /// </summary>
        private bool EnsureHardLockCanBeScoped()
        {
            if (!_useHardLock) return true;
            if (GetDbdExePaths().Count > 0) return true;

            bool pathSet = !string.IsNullOrWhiteSpace(_gamePath);
            var reason = pathSet
                ? $"No Dead by Daylight executable was found in your game folder:\n\n{_gamePath}\n\n" +
                  "Make Your Choice looks for " + string.Join(", ", GameInstalls.ExeNames) + "."
                : "Enforced mode needs your game folder so the firewall rules can be limited to Dead by Daylight.";

            var choice = MessageBox.Show(this,
                reason +
                "\n\nWithout it the rules would have to block every app on this PC, so Enforced mode " +
                "can't be applied.\n\nTip: pick the folder that opens via Steam → right-click Dead by " +
                "Daylight → Manage → Browse local files (or the equivalent in the Epic Games launcher). " +
                "You can also point the setting straight at the .exe.\n\nOpen Options now to set it?",
                "Game folder required for Enforced mode",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (choice == MessageBoxResult.Yes)
                SetTab(Tab.Options);
            return false; // abort this apply; the user can re-apply after fixing the folder
        }

        // ── Lifecycle ───────────────────────────────────────────────

        private void StartSniffer()
        {
            if (_snifferStarted || _sniffer == null)
                return;
            _snifferStarted = true;
            _sniffer.Start();
        }

        private async Task FetchGitIdentityAsync()
        {
            const string UID = "109703063"; // Changing this, or the final result of this functionality may break license compliance
            string url = $"https://api.github.com/user/{UID}";

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MakeYourChoice/1.0");
                using var stream = await client.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);
                if (doc.RootElement.TryGetProperty("login", out var login))
                {
                    Developer = login.GetString();
                }
            }
            catch
            {
                // API call failed, Developer remains null
            }
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            // Clicking the X minimizes to the tray instead of quitting; only the tray's "Exit" (which
            // sets _exiting) actually closes. When the tray is disabled there's nowhere to hide, so
            // the X exits normally.
            if (!_exiting && _minimizeToTray && _tray != null)
            {
                e.Cancel = true;
                MinimizeToTrayIfEnabled();
                return;
            }

            _closed = true;
            // Remember this session's ticked regions for next launch.
            try { SaveSettings(); } catch { /* ignore */ }
            if (_sniffer != null)
            {
                _sniffer.TrafficDetected -= OnTrafficDetected;
                _sniffer.Stop();
            }
            _pingTimer?.Stop();
            _connectionTooltipTimer?.Stop();
            DisposeTray();
            // ShutdownMode is OnExplicitShutdown (so hiding to the tray doesn't kill the app), so a
            // real close — the X with the tray disabled, or the tray's Exit — must shut down explicitly.
            Application.Current.Shutdown();
        }

        // WPF-UI's caption-drag (NCHITTEST → HTCAPTION) stops working once ResizeMode locks the
        // window, so drag manually from anywhere on the title-bar row. The caption buttons handle
        // their own mouse presses, which never bubble here.
        private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); } catch { /* mouse released mid-call */ }
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            // Minimize-to-tray: when enabled, minimizing hides the window (and its taskbar button);
            // the tray icon remains. Otherwise it behaves like a normal taskbar minimize.
            if (WindowState == WindowState.Minimized)
                MinimizeToTrayIfEnabled();
        }

        // ── Connected-to (sniffer) ──────────────────────────────────

        private void OnTrafficDetected(string ip, int port, int localPort)
        {
            if (_closed) return;

            void ApplyUi(string regionName)
            {
                if (_closed) return;

                string text = !string.IsNullOrEmpty(regionName)
                    ? regionName
                    : $"Unknown Region [{ip}]";

                ConnectedValue.Text = text;

                Brush dotBrush;
                if (string.IsNullOrEmpty(regionName))
                {
                    dotBrush = text.Contains("Waiting") ? Brushes.LightSlateGray : Brushes.Orange;
                }
                else
                {
                    var blockedHosts = GetBlockedHostnamesFromHostsSection();
                    var isBlocked = IsRegionBlockedByHosts(regionName, blockedHosts);
                    dotBrush = isBlocked ? Brushes.Red : Brushes.Green;
                }

                ConnectionDot.Foreground = dotBrush;
                _lastConnectionUpdate = DateTime.Now;
                UpdateConnectionTooltip();
            }

            void UpdateUi(string regionName)
            {
                if (_closed) return;
                try { Dispatcher.BeginInvoke((Action)(() => ApplyUi(regionName))); }
                catch { /* ignore if window is gone mid-invoke */ }
            }

            if (string.Equals(_lastDetectedIp, ip, StringComparison.Ordinal))
            {
                // Same server we're already connected to: refresh the "live" timestamp so the region
                // stays live for the whole match, not just the first packet.
                MarkRegionOnlineFromConnection(_lastDetectedRegion);
                UpdateUi(_lastDetectedRegion);
                return;
            }

            _lastDetectedIp = ip;
            _lastDetectedPort = port;

            Task.Run(() =>
            {
                try
                {
                    if (_awsService == null) return;
                    var regionName = _awsService.GetRegionForIp(ip);

                    _lastDetectedRegion = regionName;
                    // Live ground truth: we actually connected to a server in this region, so it's
                    // online right now — override DBQ's lagged data immediately.
                    MarkRegionOnlineFromConnection(regionName);
                    UpdateUi(regionName);
                }
                catch { /* Ignore if UI is gone */ }
            });
        }

        private void UpdateConnectionTooltip()
        {
            if (_closed) return;

            string header;
            if (_lastConnectionUpdate.HasValue)
            {
                var seconds = (int)Math.Max(0, (DateTime.Now - _lastConnectionUpdate.Value).TotalSeconds);
                var time = FormatSmallCapsTime(_lastConnectionUpdate.Value);
                header = $"Most recent connection: {seconds}s ago, at {time}";

                if (seconds >= 5)
                {
                    ConnectedValue.Text = "Waiting for match…";
                    ConnectionDot.Foreground = Brushes.LightSlateGray;
                }
            }
            else
            {
                header = "Most recent connection: —";
            }

            ConnectedValue.ToolTip =
                header + "\n\n" +
                "This is the region that Dead by Daylight chose\n" +
                "when connecting you to their game.";
        }

        private static string FormatSmallCapsTime(DateTime dt)
        {
            var time = dt.ToString("h:mmtt");
            return time.Replace("AM", "ᴀᴍ").Replace("PM", "ᴘᴍ");
        }

        // ── Latency + status polling ────────────────────────────────

        private void StartPingTimer()
        {
            var pinger = new Ping();
            _pingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _pingTimer.Tick += async (_, __) =>
            {
                // Collect ping results for all regions
                var results = new Dictionary<string, long>();
                foreach (var row in RegionRowsOnly())
                {
                    long ms;
                    try
                    {
                        var hosts = _regions[row.Key].Hosts;
                        var reply = await pinger.SendPingAsync(hosts[0], 2000);
                        ms = reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
                    }
                    catch
                    {
                        ms = -1;
                    }
                    results[row.Key] = ms;
                }

                if (_closed) return;

                var blockedHosts = GetBlockedHostnamesFromHostsSection();
                foreach (var row in RegionRowsOnly())
                {
                    var ms = results[row.Key];
                    if (IsRegionBlockedByHosts(row.Key, blockedHosts))
                    {
                        row.LatencyText = "disconnected";
                        row.LatencyBrush = Brushes.Gray;
                    }
                    else
                    {
                        row.LatencyText = ms >= 0 ? $"{ms} ms" : "disconnected";
                        row.LatencyBrush = GetBrushForLatency(ms);
                    }

                    // Status column: real online/offline for every server, sourced from Dead
                    // by Queue plus recent live connections (resolved in _dbqOnline).
                    var code = AwsCodeForRegion(row.Key);
                    bool? online = (code != null && _dbqOnline.TryGetValue(code, out var on))
                        ? on : (bool?)null;
                    row.StatusText = online == true ? "Online"
                                   : online == false ? "Offline"
                                   : "Unknown";
                    row.StatusBrush = online == true ? Brushes.MediumSeaGreen
                                    : online == false ? Brushes.Crimson
                                    : Brushes.Gray;
                }
            };
            _pingTimer.Start();
        }

        private static Brush GetBrushForLatency(long ms)
        {
            if (ms < 0) return Brushes.LightSlateGray;
            if (ms < 80) return Brushes.Green;
            if (ms < 130) return Brushes.Orange;
            if (ms < 250) return Brushes.Crimson;
            return Brushes.MediumVioletRed;
        }

        private string GetGroupName(string region)
        {
            if (region.StartsWith("Europe")) return "Europe";
            if (region.StartsWith("US") || region.StartsWith("Canada") || region.StartsWith("South America"))
                return "Americas";
            if (region.Contains("Sydney")) return "Oceania";
            if (region.Contains("China")) return "China";
            return "Asia";
        }

        // ── Hosts file helpers ──────────────────────────────────────

        private List<string> GetAllManagedHostnames()
        {
            var hostnames = new HashSet<string>();
            foreach (var region in _regions.Values)
                foreach (var host in region.Hosts)
                    hostnames.Add(host.ToLowerInvariant());
            foreach (var region in _blockedRegions.Values)
                foreach (var host in region.Hosts)
                    hostnames.Add(host.ToLowerInvariant());
            return hostnames.ToList();
        }

        private HashSet<string> GetBlockedHostnamesFromHostsSection()
        {
            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var hostsContent = File.ReadAllText(HostsPath);
                var normalized = hostsContent.Replace("\r\n", "\n").Replace("\r", "\n");
                var firstMarker = normalized.IndexOf(SectionMarker, StringComparison.Ordinal);
                if (firstMarker < 0) return blocked;
                var secondMarker = normalized.IndexOf(SectionMarker, firstMarker + SectionMarker.Length, StringComparison.Ordinal);
                if (secondMarker < 0) return blocked;

                var inner = normalized.Substring(firstMarker + SectionMarker.Length, secondMarker - (firstMarker + SectionMarker.Length));
                foreach (var rawLine in inner.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    if (!string.Equals(parts[0], "0.0.0.0", StringComparison.Ordinal)) continue;

                    for (int i = 1; i < parts.Length; i++)
                        blocked.Add(parts[i].ToLowerInvariant());
                }
            }
            catch
            {
                // Ignore read errors
            }

            return blocked;
        }

        private bool IsRegionBlockedByHosts(string regionKey, HashSet<string> blockedHosts)
        {
            if (blockedHosts.Count == 0) return false;

            if (_regions.TryGetValue(regionKey, out var info))
                return info.Hosts.Any(h => blockedHosts.Contains(h.ToLowerInvariant()));

            if (_blockedRegions.TryGetValue(regionKey, out var blockedInfo))
                return blockedInfo.Hosts.Any(h => blockedHosts.Contains(h.ToLowerInvariant()));

            return false;
        }

        private List<string> DetectConflictingEntries()
        {
            var conflicts = new List<string>();
            var managedHosts = GetAllManagedHostnames();

            try
            {
                string hostsContent = File.ReadAllText(HostsPath);
                string normalized = hostsContent.Replace("\r\n", "\n").Replace("\r", "\n");

                // Find the section markers
                int firstMarker = normalized.IndexOf(SectionMarker, StringComparison.Ordinal);
                int secondMarker = firstMarker >= 0
                    ? normalized.IndexOf(SectionMarker, firstMarker + SectionMarker.Length, StringComparison.Ordinal)
                    : -1;

                // Get content outside markers
                string outsideContent;
                if (firstMarker >= 0 && secondMarker >= 0)
                {
                    int afterSecond = secondMarker + SectionMarker.Length;
                    outsideContent = normalized.Substring(0, firstMarker) +
                                   (afterSecond < normalized.Length ? normalized.Substring(afterSecond) : "");
                }
                else if (firstMarker >= 0)
                {
                    outsideContent = normalized.Substring(0, firstMarker);
                }
                else
                {
                    outsideContent = normalized;
                }

                // Parse lines and check for conflicts
                foreach (var line in outsideContent.Split('\n'))
                {
                    var trimmed = line.Trim();

                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var hostname = parts[1].ToLowerInvariant();
                        if (managedHosts.Contains(hostname) && !conflicts.Contains(trimmed))
                            conflicts.Add(trimmed);
                    }
                }
            }
            catch
            {
                // If we can't read the file, no conflicts detected
            }

            return conflicts;
        }

        private void ClearConflictingEntries(List<string> conflicts)
        {
            try
            {
                string hostsContent = File.ReadAllText(HostsPath);
                string normalized = hostsContent.Replace("\r\n", "\n").Replace("\r", "\n");

                var lines = normalized.Split('\n').ToList();
                var conflictSet = new HashSet<string>(conflicts.Select(c => c.Trim()));

                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    if (conflictSet.Contains(lines[i].Trim()))
                        lines.RemoveAt(i);
                }

                string cleaned = string.Join("\n", lines).Replace("\n", "\r\n");
                File.Copy(HostsPath, HostsPath + ".bak", true);
                File.WriteAllText(HostsPath, cleaned);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Failed to clear conflicting entries:\n" + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                throw;
            }
        }

        // ── Apply / revert ──────────────────────────────────────────

        private async void OnApply(object sender, RoutedEventArgs e)
        {
            // Enforced mode scopes its firewall rules to the DBD executables, so it cannot run without
            // knowing where the game is. Check BEFORE touching anything: otherwise we'd rewrite the
            // hosts file and only then fail at the firewall step, leaving a half-applied state.
            if (!EnsureHardLockCanBeScoped()) return;

            // Check for conflicting entries before proceeding
            var conflicts = DetectConflictingEntries();
            if (conflicts.Count > 0)
            {
                if (!Dialogs.ShowConflictDialog(this, out var clearConflicts))
                    return; // User cancelled

                if (clearConflicts)
                {
                    try
                    {
                        ClearConflictingEntries(conflicts);
                    }
                    catch
                    {
                        return; // Error already shown in ClearConflictingEntries
                    }
                }
            }

            // if universal redirect mode, redirect all endpoints to selected region's IPs
            if (_applyMode == ApplyMode.UniversalRedirect)
            {
                var selectedRows = RegionRowsOnly().Where(r => r.IsChecked).ToList();

                if (selectedRows.Count != 1)
                {
                    MessageBox.Show(this,
                        "Please select only one server when using Universal Redirect mode.",
                        "Universal Redirect",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var regionKey = selectedRows[0].Key;
                var hosts = _regions[regionKey].Hosts;
                var serviceHost = hosts[0];
                var pingHost = hosts.Length > 1 ? hosts[1] : hosts[0];

                // resolve via DNS lookup to obtain IP addresses
                string svcIp, pingIp;
                try
                {
                    var svcAddrs = Dns.GetHostAddresses(serviceHost);
                    var pingAddrs = Dns.GetHostAddresses(pingHost);
                    if (svcAddrs.Length == 0 || pingAddrs.Length == 0)
                        throw new Exception("DNS lookup returned no addresses");

                    svcIp = svcAddrs[0].ToString();
                    pingIp = pingAddrs[0].ToString();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "Failed to resolve IP addresses for Universal Redirect mode via DNS:\n" + ex.Message,
                        "Universal Redirect Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
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
                        foreach (var h in kv.Value.Hosts)
                        {
                            bool isPing = h.Contains("ping", StringComparison.OrdinalIgnoreCase);
                            var ip = isPing ? pingIp : svcIp;
                            sb.AppendLine($"{ip} {h}");
                        }
                        sb.AppendLine();
                    }

                    foreach (var kv in _blockedRegions)
                    {
                        foreach (var h in kv.Value.Hosts)
                            sb.AppendLine($"0.0.0.0 {h}");
                        sb.AppendLine();
                    }

                    WriteWrappedHostsSection(sb.ToString());
                    FlushDns();
                    MessageBox.Show(this,
                        "The hosts file was updated successfully (Universal Redirect).\n\nPlease restart the game in order for changes to take effect.",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show(this,
                        "Please run as Administrator to modify the hosts file.",
                        "Permission Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                return;
            }

            // existing gatekeep mode logic
            var selectedRegions = RegionRowsOnly().Where(r => r.IsChecked).Select(r => r.Key).ToList();
            if (selectedRegions.Count == 0)
            {
                // No bubbles ticked -> clear all restrictions (host entries + firewall rules),
                // same as Revert to Default.
                await ClearAllRestrictionsAsync(true);
                return;
            }

            try
            {
                File.Copy(HostsPath, HostsPath + ".bak", true);

                var allowedSet = new HashSet<string>(selectedRegions);

                var sb = new StringBuilder();
                sb.AppendLine("# Edited by Make Your Choice (DbD Server Selector)");
                sb.AppendLine("# Unselected servers are blocked (Gatekeep Mode); selected servers are commented out.");
                sb.AppendLine($"# Need help? Discord: {DiscordUrl}");
                sb.AppendLine();

                foreach (var row in RegionRowsOnly())
                {
                    bool allow = allowedSet.Contains(row.Key);
                    foreach (var h in _regions[row.Key].Hosts)
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

                foreach (var kv in _blockedRegions)
                {
                    foreach (var h in kv.Value.Hosts)
                        sb.AppendLine($"0.0.0.0 {h}");
                    sb.AppendLine();
                }

                WriteWrappedHostsSection(sb.ToString());
                FlushDns();

                // Persist the ticked selection so it's repopulated next launch (matching any
                // firewall rules we're about to apply).
                SaveSettings();

                // Enforced mode: also firewall-block the game-server data plane of every region NOT
                // chosen, so DBD's server-side fallback can't place you there.
                string lockNote = "";
                var (lockOk, lockMsg) = await ReconcileHardLockAsync(allowedSet);
                if (_useHardLock)
                    lockNote = lockOk
                        ? "\n\nFirewall enforcement applied: unchosen regions' game-server traffic is blocked."
                        : "\n\nFirewall enforcement could NOT be applied: " + lockMsg;

                // Enforced = Gatekeep hosts (steer matchmaking) + firewall (hard-block); plain Gatekeep = hosts only.
                var header = _applyMode == ApplyMode.Enforced
                    ? "Applied Enforced mode.\n\nYour hosts file was updated to steer matchmaking, and the firewall blocks unchosen regions."
                    : "The hosts file was updated successfully (Gatekeep).";

                MessageBox.Show(this,
                    header + lockNote +
                    "\n\nPlease restart the game in order for changes to take effect.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this,
                    "Please run as Administrator to modify the hosts file.",
                    "Permission Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void FlushDns()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                p?.WaitForExit();
            }
            catch { /* ignore */ }
        }

        // Clear all Make Your Choice restrictions: empty the hosts section and remove the firewall
        // lock. Used by "Apply Selection" with nothing ticked (same effect as Revert to Default).
        private async Task ClearAllRestrictionsAsync(bool showMessage)
        {
            try
            {
                try { File.Copy(HostsPath, HostsPath + ".bak", true); } catch { /* ignore backup */ }
                WriteWrappedHostsSection(string.Empty);
                FlushDns();
                await Task.Run(() => FirewallManager.RemoveLock());
                SaveSettings();
                if (showMessage)
                    MessageBox.Show(this,
                        "All Make Your Choice restrictions were cleared.\n\nPlease restart the game for changes to take effect.",
                        "Restrictions cleared",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Please run as Administrator to modify the hosts file.",
                    "Permission Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Re-apply the current Gatekeep selection silently (no dialogs). Called when the hard-lock
        // method changes in Options, so the hosts entries + firewall always match the UI without
        // having to click Apply or Revert.
        private async Task ReapplyGatekeepSilentlyAsync()
        {
            var selectedRegions = RegionRowsOnly().Where(r => r.IsChecked).Select(r => r.Key).ToList();
            if (selectedRegions.Count == 0)
            {
                await ClearAllRestrictionsAsync(false);
                return;
            }

            var allowedSet = new HashSet<string>(selectedRegions);

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Edited by Make Your Choice (DbD Server Selector)");
                sb.AppendLine("# Unselected servers are blocked (Gatekeep Mode); selected servers are commented out.");
                sb.AppendLine($"# Need help? Discord: {DiscordUrl}");
                sb.AppendLine();
                foreach (var row in RegionRowsOnly())
                {
                    bool allow = allowedSet.Contains(row.Key);
                    foreach (var h in _regions[row.Key].Hosts)
                    {
                        bool isPing = h.Contains("ping", StringComparison.OrdinalIgnoreCase);
                        bool include = _blockMode == BlockMode.Both
                                       || (_blockMode == BlockMode.OnlyPing && isPing)
                                       || (_blockMode == BlockMode.OnlyService && !isPing);
                        if (!include) continue;
                        sb.AppendLine($"{(allow ? "#" : "0.0.0.0".PadRight(9))} {h}");
                    }
                    sb.AppendLine();
                }
                foreach (var kv in _blockedRegions)
                {
                    foreach (var h in kv.Value.Hosts) sb.AppendLine($"0.0.0.0 {h}");
                    sb.AppendLine();
                }
                WriteWrappedHostsSection(sb.ToString());
                FlushDns();
                SaveSettings();
                await ReconcileHardLockAsync(allowedSet); // applies or removes the firewall to match
            }
            catch { /* best-effort; manual Apply surfaces any errors */ }
        }

        // True if the hosts file currently has an active Make Your Choice region section.
        private bool IsHostsSectionActive()
        {
            try
            {
                var text = File.ReadAllText(HostsPath);
                int first = text.IndexOf(SectionMarker, StringComparison.Ordinal);
                int last = first >= 0 ? text.IndexOf(SectionMarker, first + SectionMarker.Length, StringComparison.Ordinal) : -1;
                if (first < 0 || last < 0) return false;
                var inner = text.Substring(first + SectionMarker.Length, last - first - SectionMarker.Length);
                return inner.Contains("amazonaws") || inner.Contains("api.aws");
            }
            catch { return false; }
        }

        private async void OnRevert(object sender, RoutedEventArgs e)
        {
            // Untick every region in the UI so the list matches the cleared backend.
            foreach (var row in RegionRowsOnly())
                if (row.IsChecked) row.IsChecked = false;
            _savedSelection = new List<string>();
            // Clear both backends (hosts entries AND any firewall hard lock) and persist the now-empty
            // selection so it doesn't repopulate next launch.
            await ClearAllRestrictionsAsync(true);
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
            int last = first >= 0 ? lf.IndexOf(SectionMarker, first + SectionMarker.Length, StringComparison.Ordinal) : -1;

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
            File.WriteAllText(HostsPath, newLf.Replace("\n", "\r\n"));
        }

        internal static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        // ── Misc tab ────────────────────────────────────────────────

        private void OnOpenHostsFolder(object sender, RoutedEventArgs e)
        {
            var hostsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "drivers\\etc");
            Process.Start(new ProcessStartInfo("explorer.exe", hostsFolder)
            {
                UseShellExecute = true
            });
        }

        private void OnResetHosts(object sender, RoutedEventArgs e) => RestoreWindowsDefaultHostsFile();

        private async void OnCheckUpdates(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(false);

        private void OnDiscord(object sender, RoutedEventArgs e) => OpenUrl(DiscordUrl);

        private void OnCustomSplash(object sender, RoutedEventArgs e) => HandleCustomSplashArt();

        private void OnSkipTrailer(object sender, RoutedEventArgs e) => HandleSkipTrailer();

        private void HandleCustomSplashArt()
        {
            // Resolve the install root: the setting may hold a direct .exe path, which the firewall can
            // use but content paths can't be built from.
            var gamePath = GameInstalls.ResolveInstallRoot(_gamePath);
            if (string.IsNullOrWhiteSpace(gamePath))
            {
                MessageBox.Show(this,
                    "Please set the game folder in Options.\n\nTip: In Steam, right-click Dead by Daylight → Manage → Browse local files. The folder that opens is the one you should select.\n\nThis setting is only required for some features like custom splash art and auto-skip trailer.",
                    "Game folder required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var choice = Dialogs.ShowChoicePrompt(this,
                "Custom splash art",
                "This lets you use custom artwork for the EAC splash screen that pops up when you launch the game.\n\nRequirements:\n• PNG image\n• 800 x 450 pixels\n\nChoose Upload to select an image, or Revert to restore default.",
                "Upload image…",
                "Revert to default");

            if (choice == PromptChoice.Primary)
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select splash image (800x450)",
                    Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp",
                    Multiselect = false
                };
                if (ofd.ShowDialog(this) != true)
                    return;

                using (var img = System.Drawing.Image.FromFile(ofd.FileName))
                {
                    if (img.Width != 800 || img.Height != 450)
                    {
                        MessageBox.Show(this, "Image must be exactly 800x450 pixels.", "Custom splash art",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                try
                {
                    var targetDir = Path.Combine(gamePath, "EasyAntiCheat");
                    var targetPath = Path.Combine(targetDir, "SplashScreen.png");
                    var backupPath = targetPath + ".bak";
                    Directory.CreateDirectory(targetDir);
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    if (File.Exists(targetPath)) File.Move(targetPath, backupPath);
                    File.Copy(ofd.FileName, targetPath, true);
                    MessageBox.Show(this, "Custom splash art applied.", "Custom splash art",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to apply custom splash art:\n{ex.Message}", "Custom splash art",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (choice == PromptChoice.Secondary)
            {
                try
                {
                    var targetPath = Path.Combine(gamePath, "EasyAntiCheat", "SplashScreen.png");
                    var backupPath = targetPath + ".bak";
                    if (!File.Exists(backupPath))
                    {
                        MessageBox.Show(this, "No backup found to restore.", "Custom splash art",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                    File.Move(backupPath, targetPath);
                    MessageBox.Show(this, "Reverted to default splash art.", "Custom splash art",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to revert splash art:\n{ex.Message}", "Custom splash art",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void HandleSkipTrailer()
        {
            // Resolve the install root: the setting may hold a direct .exe path, which the firewall can
            // use but content paths can't be built from.
            var gamePath = GameInstalls.ResolveInstallRoot(_gamePath);
            if (string.IsNullOrWhiteSpace(gamePath))
            {
                MessageBox.Show(this,
                    "Please set the game folder in Options.\n\nTip: In Steam, right-click Dead by Daylight → Manage → Browse local files. The folder that opens is the one you should select.",
                    "Game folder required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var choice = Dialogs.ShowChoicePrompt(this,
                "Auto-skip loading screen trailer",
                "This will automatically skip the current DbD chapter's trailer video that plays everytime you launch the game.\n\nChoose Disable trailer to turn this on, or Revert to restore default.",
                "Disable trailer",
                "Revert to default");

            var targetPath = Path.Combine(gamePath, "DeadByDaylight", "Content", "Movies", "LoadingScreen.bk2");
            var backupPath = targetPath + ".bak";

            if (choice == PromptChoice.Primary)
            {
                try
                {
                    if (!File.Exists(targetPath))
                    {
                        MessageBox.Show(this, "LoadingScreen.bk2 not found.", "Auto-skip",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    File.Move(targetPath, backupPath);
                    MessageBox.Show(this, "Loading screen trailer will be skipped.", "Auto-skip",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to enable auto-skip:\n{ex.Message}", "Auto-skip",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (choice == PromptChoice.Secondary)
            {
                try
                {
                    if (!File.Exists(backupPath))
                    {
                        MessageBox.Show(this, "No backup found to restore.", "Auto-skip",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                    File.Move(backupPath, targetPath);
                    MessageBox.Show(this, "Reverted to default trailer.", "Auto-skip",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to revert trailer:\n{ex.Message}", "Auto-skip",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task CheckForUpdatesAsync(bool silent)
        {
            // A paused automatic check still fetches: the sidebar Update button should appear even
            // while the user has snoozed the prompt — only the dialog stays quiet.
            bool promptPaused = silent && !string.IsNullOrEmpty(_autoUpdateCheckPausedUntil)
                && DateTime.TryParse(_autoUpdateCheckPausedUntil, out var pausedUntil)
                && DateTime.Now < pausedUntil;

            if (Developer == null)
            {
                // Always notify if identity fetch failed, even if silent
                MessageBox.Show(this,
                    "Unable to check for updates.\n\nThe application was unable to fetch the git identity and therefore couldn't determine the repository URL.\n\nThis may be due to network issues or GitHub API issues.\nAn update to fix this issue has most likely been released, please check manually by joining the Discord server or doing a web search.",
                    "Check For Updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MakeYourChoice/1.0");
                // fetch all releases
                var url = $"https://api.github.com/repos/{Developer}/{Repo}/releases";

                using var stream = await client.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                {
                    if (!silent)
                    {
                        MessageBox.Show(this, "No releases found.", "Check For Updates",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }

                // assume first is latest (API returns newest first)
                var latest = root[0].GetProperty("tag_name").GetString();
                if (string.Equals(latest, CurrentVersion, StringComparison.OrdinalIgnoreCase))
                {
                    if (!silent)
                    {
                        MessageBox.Show(this,
                            "You're already using the latest release! :D",
                            "Check For Updates",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    // Newer release available: light up the sidebar Update button (like Aero Cut).
                    UpdateButton.Visibility = Visibility.Visible;

                    if (promptPaused)
                        return;

                    var (ok, updateNow, days) = Dialogs.ShowUpdatePrompt(this, latest, CurrentVersion);
                    if (ok)
                    {
                        if (updateNow)
                        {
                            OpenUrl($"https://github.com/{Developer}/{Repo}/releases/latest");
                        }
                        else
                        {
                            _autoUpdateCheckPausedUntil = DateTime.Now.AddDays(days).ToString("o");
                            SaveSettings();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show(this,
                        "Error while checking for updates:\n" + ex.Message,
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void RestoreWindowsDefaultHostsFile()
        {
            var confirm = MessageBox.Show(this,
                "If you are having problems, or the program doesn't seem to work correctly, try resetting your hosts file.\n\nThis will overwrite your entire hosts file with the Windows default.\n\nA backup will be saved as hosts.bak. Continue?",
                "Restore Windows default hosts file",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
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

                // Reverting to default also clears the firewall rules; drop Enforced back to Gatekeep.
                FirewallManager.RemoveLock();
                if (_applyMode == ApplyMode.Enforced) _applyMode = ApplyMode.Gatekeep;
                SaveSettings();
                SyncOptionsUi();

                FlushDns();

                MessageBox.Show(this,
                    "Hosts file restored to Windows default template.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this,
                    "Please run as Administrator to modify the hosts file.",
                    "Permission Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Options tab ─────────────────────────────────────────────

        // Push the current settings into the Options controls.
        private void SyncOptionsUi()
        {
            CbApplyMode.SelectedIndex =
                _applyMode == ApplyMode.UniversalRedirect ? 2 :
                _applyMode == ApplyMode.Enforced ? 1 : 0;
            RbBoth.IsChecked = _blockMode == BlockMode.Both;
            RbPing.IsChecked = _blockMode == BlockMode.OnlyPing;
            RbService.IsChecked = _blockMode == BlockMode.OnlyService;
            TsMinTray.IsChecked = _minimizeToTray;
            TsNotify.IsChecked = _notifyServerOnline;
            TsStartup.IsChecked = _startWithWindows;
            CbTheme.SelectedIndex = _theme == ThemeSetting.Dark ? 2 : _theme == ThemeSetting.Light ? 1 : 0;
            TbGamePath.Text = _gamePath ?? string.Empty;
            GatekeepCard.IsEnabled = CbApplyMode.SelectedIndex != 2;
        }

        private void OnApplyModeChanged(object sender, SelectionChangedEventArgs e)
        {
            // Both Gatekeep (0) and Enforced (1) are gatekeep-based, so the block options apply
            // to them; only Universal Redirect (2) disables them.
            if (GatekeepCard != null)
                GatekeepCard.IsEnabled = CbApplyMode.SelectedIndex != 2;
        }

        private void OnBrowseGameFolder(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select the game install folder"
            };
            if (dialog.ShowDialog(this) == true)
            {
                var selected = dialog.FolderName;
                var name = Path.GetFileName(selected.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.Equals(name, "Dead by Daylight", StringComparison.Ordinal))
                {
                    MessageBox.Show(this,
                        "Please select the folder named \"Dead by Daylight\".",
                        "Invalid game folder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                TbGamePath.Text = selected;
            }
        }

        private void OnOptionsDefaults(object sender, RoutedEventArgs e)
        {
            CbApplyMode.SelectedIndex = 0; // Gatekeep, hard-lock firewall off
            RbBoth.IsChecked = true;
            TbGamePath.Text = string.Empty;
            CbTheme.SelectedIndex = 0;
            TsMinTray.IsChecked = true;
            TsNotify.IsChecked = false;
            TsStartup.IsChecked = false;
        }

        private void OnOptionsApply(object sender, RoutedEventArgs e)
        {
            var gamePathText = TbGamePath.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(gamePathText))
            {
                var name = Path.GetFileName(gamePathText.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                // Accept the install folder, or a direct path to one of the storefront binaries —
                // the latter is the only route for installs we can't browse to (e.g. WindowsApps).
                bool isGameFolder = string.Equals(name, "Dead by Daylight", StringComparison.Ordinal);
                bool isGameExe = GameInstalls.ExeNames.Any(
                    n => string.Equals(name, n, StringComparison.OrdinalIgnoreCase));
                if (!isGameFolder && !isGameExe)
                {
                    MessageBox.Show(this,
                        "Please select the folder named \"Dead by Daylight\", or point directly at one of:\n\n" +
                        string.Join("\n", GameInstalls.ExeNames),
                        "Invalid game folder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            // Enforced mode writes firewall rules scoped to the DBD executables, so it can't be
            // saved without a game path we can actually resolve one from. The game-folder field is
            // right here, so the user can fix it without navigating away.
            if (CbApplyMode.SelectedIndex == 1 && GameInstalls.Find(gamePathText).Count == 0)
            {
                MessageBox.Show(this,
                    (string.IsNullOrEmpty(gamePathText)
                        ? "Enforced mode needs your game folder so the firewall rules can be limited to Dead by Daylight."
                        : $"No Dead by Daylight executable was found in:\n\n{gamePathText}") +
                    "\n\nWithout it the rules would have to block every app on this PC.\n\n" +
                    "Set the game folder below, or choose a different method.",
                    "Game folder required for Enforced mode",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                TbGamePath.Focus();
                return;
            }

            bool oldHardLock = _useHardLock; // capture before _applyMode changes (it's derived)
            // Method dropdown: 0 = Gatekeep, 1 = Enforced (Gatekeep + firewall), 2 = Universal Redirect.
            _applyMode = CbApplyMode.SelectedIndex switch
            {
                2 => ApplyMode.UniversalRedirect,
                1 => ApplyMode.Enforced,
                _ => ApplyMode.Gatekeep,
            };
            // Block options apply to every gatekeep-based method (Gatekeep and Enforced).
            if (_applyMode != ApplyMode.UniversalRedirect)
            {
                if (RbBoth.IsChecked == true) _blockMode = BlockMode.Both;
                else if (RbPing.IsChecked == true) _blockMode = BlockMode.OnlyPing;
                else _blockMode = BlockMode.OnlyService;
            }
            bool hardLockChanged = oldHardLock != _useHardLock;
            bool hardLockTurnedOff = oldHardLock && !_useHardLock;
            _gamePath = gamePathText;
            _theme = CbTheme.SelectedIndex switch
            {
                2 => ThemeSetting.Dark,
                1 => ThemeSetting.Light,
                _ => ThemeSetting.System,
            };
            _minimizeToTray = TsMinTray.IsChecked == true;
            _notifyServerOnline = TsNotify.IsChecked == true;
            bool startupChanged = _startWithWindows != (TsStartup.IsChecked == true);
            _startWithWindows = TsStartup.IsChecked == true;
            SaveSettings();
            if (startupChanged) SetAutoStart(_startWithWindows);

            // Keep the backend in sync with the UI on demand: if a gatekeep-based selection is
            // already applied and the firewall rules changed, re-apply now so the hosts entries and
            // firewall rules match — no need to click Apply or Revert to Default.
            if (_applyMode != ApplyMode.UniversalRedirect && hardLockChanged && IsHostsSectionActive())
                _ = ReapplyGatekeepSilentlyAsync();
            else if (hardLockTurnedOff)
                FirewallManager.RemoveLock();

            ApplyThemeSetting();
        }
    }
}
