mod hosts;
mod ping;
mod region;
mod settings;
mod update;
mod sniff;
mod aws_ranges;

use gio::{Menu, SimpleAction};
use glib::Type;
use gtk4::prelude::*;
use gtk4::{
    gio, glib, pango, Application, ApplicationWindow, Box as GtkBox, Button, ButtonsType,
    CellRendererText, CheckButton, ComboBoxText, Dialog, Entry, FileChooserAction,
    FileChooserNative, FileFilter, Label, ListStore, MenuButton, MessageDialog, MessageType,
    Orientation, PolicyType, ResponseType, ScrolledWindow, SelectionMode, Separator, TreeView,
    TreeViewColumn,
};
use std::cell::RefCell;
use std::collections::{HashMap, HashSet};
use std::rc::Rc;
use std::sync::{Arc, Mutex};
use chrono::{DateTime, Local};
use tokio::runtime::Runtime;

use hosts::HostsManager;
use region::*;
use settings::UserSettings;
use update::UpdateChecker;
use sniff::TrafficSniffer;
use aws_ranges::AwsIpService;

const APP_ID: &str = "dev.lawliet.makeyourchoice";

#[derive(Debug, serde::Deserialize)]
struct PatchNotes {
    version: String,
    notes: Vec<String>,
}

fn load_versinf() -> (String, String) {
    const VERSINF_YAML: &str = include_str!("../../VERSINF.yaml");

    match serde_yaml::from_str::<PatchNotes>(VERSINF_YAML) {
        Ok(versinf) => {
            let version = versinf.version;
            let notes = versinf.notes
                .iter()
                .map(|note| format!("- {}", note))
                .collect::<Vec<_>>()
                .join("\n");

            let message = format!("Here are some new features and changes:\n\n{}", notes);
            (version, message)
        }
        Err(_) => {
            ("v0.0.0".to_string(), "Failed to get version info.".to_string()) // Return placeholder on error
        }
    }
}

#[derive(Clone)]
struct AppConfig {
    repo_url: Option<String>,
    current_version: String,
    developer: Option<String>,
    repo: String,
    update_message: String,
    discord_url: String,
}

#[allow(dead_code)]
struct AppState {
    config: AppConfig,
    regions: HashMap<String, RegionInfo>,
        blocked_regions: HashMap<String, RegionInfo>,
    settings: Arc<Mutex<UserSettings>>,
    hosts_manager: HostsManager,
    update_checker: UpdateChecker,
    selected_regions: RefCell<HashSet<String>>,
    list_store: ListStore,
    tokio_runtime: Arc<Runtime>,
    sniffer: Arc<TrafficSniffer>,
    aws_service: Arc<AwsIpService>,
    connected_to_label: Label,
    connection_dot: Label,
}

fn get_color_for_latency(ms: i64) -> &'static str {
    if ms < 0 {
        return "gray";
    }
    if ms < 80 {
        return "green";
    }
    if ms < 130 {
        return "orange";
    }
    if ms < 250 {
        return "crimson";
    }
    "purple"
}

fn refresh_warning_symbols(
    list_store: &ListStore,
    regions: &HashMap<String, RegionInfo>,
    merge_unstable: bool,
) {
    if let Some(iter) = list_store.iter_first() {
        loop {
            let is_divider = list_store.get::<bool>(&iter, 4);

            // Skip dividers
            if !is_divider {
                let name = list_store.get::<String>(&iter, 0);
                let clean_name = name.replace(" ⚠︎", "");

                if let Some(region_info) = regions.get(&clean_name) {
                    // Update display name based on merge_unstable setting
                    let display_name = if !region_info.stable && !merge_unstable {
                        format!("{} ⚠︎", clean_name)
                    } else {
                        clean_name
                    };

                    // Update tooltip based on merge_unstable setting
                    let tooltip = if !region_info.stable && !merge_unstable {
                        "Unstable: issues may occur.".to_string()
                    } else {
                        String::new()
                    };

                    list_store.set(&iter, &[(0, &display_name), (6, &tooltip)]);
                }
            }

            if !list_store.iter_next(&iter) {
                break;
            }
        }
    }
}

async fn fetch_git_identity() -> Option<String> {
    const UID: &str = "109703063"; // Changing this, or the final result of this functionality may break license compliance
    let url = format!("https://api.github.com/user/{}", UID);

    let client = reqwest::Client::builder()
        .timeout(std::time::Duration::from_secs(5))
        .build()
        .ok()?;

    match client
        .get(&url)
        .header("User-Agent", "make-your-choice")
        .send()
        .await
    {
        Ok(response) => {
            if let Ok(json) = response.json::<serde_json::Value>().await {
                if let Some(login) = json.get("login").and_then(|v| v.as_str()) {
                    return Some(login.to_string());
                }
            }
        }
        Err(_) => {}
    }

    None
}

fn main() -> glib::ExitCode {
    // Prevent running as root
    if is_running_as_root() {
        eprintln!("Error: This application should not be run as root or using sudo.");
        eprintln!("The program will request sudo permissions when needed.");
        eprintln!("Please run without sudo.");
        std::process::exit(1);
    }

    ensure_capabilities_or_exit();

    let app = Application::builder().application_id(APP_ID).build();
    app.connect_activate(build_ui);
    app.run()
}

fn is_running_as_root() -> bool {
    unsafe { libc::geteuid() == 0 }
}

fn ensure_capabilities_or_exit() {
    let exe = match std::env::current_exe() {
        Ok(path) => path,
        Err(e) => {
            eprintln!("Failed to get executable path: {}", e);
            std::process::exit(1);
        }
    };

    if has_required_caps(&exe) {
        return;
    }

    let status = std::process::Command::new("pkexec")
        .arg("setcap")
        .arg("cap_net_raw,cap_dac_override+ep")
        .arg(&exe)
        .status();

    match status {
        Ok(s) if s.success() => {
            let _ = std::process::Command::new(&exe).spawn();
            std::process::exit(0);
        }
        Ok(_) => {
            eprintln!("Failed to set required capabilities. Operation cancelled or permission denied.");
            std::process::exit(1);
        }
        Err(e) => {
            eprintln!("Failed to run pkexec/setcap: {}", e);
            std::process::exit(1);
        }
    }
}

fn has_required_caps(exe: &std::path::Path) -> bool {
    let output = std::process::Command::new("getcap")
        .arg(exe)
        .output();

    let Ok(output) = output else { return false; };
    if !output.status.success() {
        return false;
    }

    let stdout = String::from_utf8_lossy(&output.stdout).to_lowercase();
    stdout.contains("cap_net_raw") && stdout.contains("cap_dac_override")
}

fn build_ui(app: &Application) {
    // Create tokio runtime for async operations
    let tokio_runtime = Arc::new(Runtime::new().expect("Failed to create tokio runtime"));

    // Load settings first
    let settings = Arc::new(Mutex::new(UserSettings::load().unwrap_or_default()));

    // Fetch git identifier from API
    let developer = tokio_runtime.block_on(async {
        fetch_git_identity().await
    });

    // Load configuration
    let (current_version, update_message) = load_versinf();
    let config = AppConfig {
        repo_url: developer.as_ref().map(|dev| format!("https://github.com/{}/make-your-choice", dev)),
        current_version,
        developer, // Fetched from API
        repo: "make-your-choice".to_string(), // Repository name
        update_message,
        discord_url: "https://discord.gg/xEMyAA8gn8".to_string(),
    };

    let regions = get_selectable_regions();
        let blocked_regions = get_blocked_regions();
    let hosts_manager = HostsManager::new(config.discord_url.clone());
    let update_checker = UpdateChecker::new(
        config.developer.clone().unwrap_or_else(|| "unknown".to_string()),
        config.repo.clone(),
        config.current_version.clone(),
    );

    // Check if the user's previously used version differs from current version and show patch notes
    {
        let mut settings_lock = settings.lock().unwrap();
        if settings_lock.last_launched_version != config.current_version
            && !config.update_message.is_empty()
        {
            // Show patch notes dialog
            let dialog = MessageDialog::new(
                None::<&ApplicationWindow>,
                gtk4::DialogFlags::MODAL,
                MessageType::Info,
                ButtonsType::Ok,
                &format!("What's new in {}", config.current_version),
            );
            dialog.set_secondary_text(Some(&config.update_message));
            dialog.run_async(|dialog, _| dialog.close());

            settings_lock.last_launched_version = config.current_version.clone();
            settings_lock.auto_update_check_paused_until = None;
            let _ = settings_lock.save();
        }
    }

    // Create ListStore for the list view (region name, latency, stable, checked, is_divider, latency_color, tooltip)
    let list_store = ListStore::new(&[
        Type::STRING,
        Type::STRING,
        Type::BOOL,
        Type::BOOL,
        Type::BOOL,
        Type::STRING, // latency foreground color
        Type::STRING, // tooltip text
    ]);

    // Group regions by category
    let mut groups: HashMap<&'static str, Vec<(&String, &RegionInfo)>> = HashMap::new();
    for (region_name, region_info) in &regions {
        let group_name = get_group_name(region_name);
        groups
            .entry(group_name)
            .or_insert_with(Vec::new)
            .push((region_name, region_info));
    }

    // Define group order and names matching Windows version
    let group_order = vec![
        ("Europe", "Europe"),
        ("Americas", "The Americas"),
        ("Asia", "Asia (Excl. Cn)"),
        ("Oceania", "Oceania"),
        ("China", "Mainland China"),
    ];

    // Check merge_unstable setting to determine if we show warning symbols
    let merge_unstable = settings.lock().unwrap().merge_unstable;

    // Populate list store with dividers and regions
    for (group_key, group_label) in group_order.iter() {
        if let Some(group_regions) = groups.get(group_key) {
            // Add group divider (not clickable)
            let divider_iter = list_store.append();
            list_store.set(
                &divider_iter,
                &[
                    (0, &group_label.to_string()),
                    (1, &String::new()),
                    (2, &true),
                    (3, &false),
                    (4, &true), // is_divider flag
                    (5, &"black".to_string()), // default color for dividers (not displayed anyway)
                    (6, &String::new()), // no tooltip for dividers
                ],
            );

            // Add regions in this group
            for (region_name, region_info) in group_regions {
                // Only show warning symbol if merge_unstable is disabled and server is unstable
                let display_name = if !region_info.stable && !merge_unstable {
                    format!("{} ⚠︎", region_name)
                } else {
                    (*region_name).clone()
                };

                // Set tooltip for unstable servers when merge_unstable is disabled
                let tooltip = if !region_info.stable && !merge_unstable {
                    "Unstable: issues may occur.".to_string()
                } else {
                    String::new()
                };

                let iter = list_store.append();
                list_store.set(
                    &iter,
                    &[
                        (0, &display_name),
                        (1, &"…".to_string()),
                        (2, &region_info.stable),
                        (3, &false), // checked
                        (4, &false), // not a divider
                        (5, &"gray".to_string()), // initial color
                        (6, &tooltip), // tooltip text
                    ],
                );
            }
        }
    }

    // Create TreeView
    let tree_view = TreeView::with_model(&list_store);
    tree_view.set_headers_visible(true);
    tree_view.set_enable_search(false);
    tree_view.selection().set_mode(SelectionMode::None);
    tree_view.set_has_tooltip(true);

    // Set up tooltip handler
    tree_view.connect_query_tooltip(|tree_view, x, y, _keyboard_mode, tooltip| {
        if let Some((Some(path), _column, _cell_x, _cell_y)) = tree_view.path_at_pos(x, y) {
            if let Some(model) = tree_view.model() {
                if let Some(iter) = model.iter(&path) {
                    let tooltip_text = model.get::<String>(&iter, 6);
                    if !tooltip_text.is_empty() {
                        tooltip.set_text(Some(&tooltip_text));
                        return true;
                    }
                }
            }
        }
        false
    });

    // Add columns
    let col_server = TreeViewColumn::new();
    col_server.set_title("Server");
    col_server.set_min_width(220);
    let cell_toggle = gtk4::CellRendererToggle::new();
    cell_toggle.set_activatable(true);
    col_server.pack_start(&cell_toggle, false);
    col_server.add_attribute(&cell_toggle, "active", 3);

    // Hide checkbox for divider rows using cell data function
    col_server.set_cell_data_func(
        &cell_toggle,
        |_col: &TreeViewColumn,
         cell: &gtk4::CellRenderer,
         model: &gtk4::TreeModel,
         iter: &gtk4::TreeIter| {
            let is_divider = model.get::<bool>(iter, 4);
            let cell_toggle = cell.downcast_ref::<gtk4::CellRendererToggle>().unwrap();
            cell_toggle.set_visible(!is_divider);
        },
    );

    let cell_text = CellRendererText::new();
    col_server.pack_start(&cell_text, true);
    col_server.add_attribute(&cell_text, "text", 0);

    // Make divider text bold and styled using cell data function
    col_server.set_cell_data_func(
        &cell_text,
        |_col: &TreeViewColumn,
         cell: &gtk4::CellRenderer,
         model: &gtk4::TreeModel,
         iter: &gtk4::TreeIter| {
            let is_divider = model.get::<bool>(iter, 4);
            let cell_text = cell.downcast_ref::<CellRendererText>().unwrap();
            if is_divider {
                cell_text.set_weight(700); // Bold weight
            } else {
                cell_text.set_weight(400); // Normal weight
            }
        },
    );

    tree_view.append_column(&col_server);

    let col_latency = TreeViewColumn::new();
    col_latency.set_title("Latency");
    col_latency.set_min_width(115);
    let cell_latency = CellRendererText::new();
    cell_latency.set_property("style", pango::Style::Italic);
    col_latency.pack_start(&cell_latency, true);
    col_latency.add_attribute(&cell_latency, "text", 1);
    col_latency.add_attribute(&cell_latency, "foreground", 5); // Use color from column 5
    tree_view.append_column(&col_latency);

    // Create scrolled window for tree view
    let scrolled = ScrolledWindow::new();
    scrolled.set_policy(PolicyType::Automatic, PolicyType::Automatic);
    scrolled.set_child(Some(&tree_view));
    scrolled.set_vexpand(true);

    // Create window
    let window = ApplicationWindow::builder()
        .application(app)
        .title("Make Your Choice (DbD Server Selector)")
        .default_width(405)
        .default_height(585)
        .build();

    // Set window icon from embedded ICO file
    const ICON_DATA: &[u8] = include_bytes!("../icon.ico");
    const ICON_NAME: &str = "make-your-choice";

    // Install icon to user's local icon directory (only if not already there)
    if let Some(data_dir) = glib::user_data_dir().to_str() {
        let icon_path = std::path::PathBuf::from(data_dir)
            .join("icons/hicolor/256x256/apps")
            .join(format!("{}.png", ICON_NAME));

        if !icon_path.exists() {
            let loader = gtk4::gdk_pixbuf::PixbufLoader::new();
            if loader.write(ICON_DATA).is_ok() && loader.close().is_ok() {
                if let Some(pixbuf) = loader.pixbuf() {
                    if let Some(parent) = icon_path.parent() {
                        let _ = std::fs::create_dir_all(parent);
                    }
                    let _ = pixbuf.savev(&icon_path, "png", &[]);
                }
            }
        }
    }

    window.set_icon_name(Some(ICON_NAME));

    // Connected to display
    let connected_box = GtkBox::new(Orientation::Horizontal, 5);
    connected_box.set_margin_start(10);
    connected_box.set_margin_end(10);
    connected_box.set_margin_top(8);
    
    let connection_dot = Label::builder()
        .label("◉")
        .css_classes(["connection-dot", "waiting"])
        .build();
    // Default blue waiting color
    const CSS_DOT: &str = "label.connection-dot { color: #3498db; font-weight: bold; }";
    let provider = gtk4::CssProvider::new();
    provider.load_from_data(CSS_DOT);
    gtk4::style_context_add_provider_for_display(
        &gtk4::gdk::Display::default().expect("Could not connect to a display."),
        &provider,
        gtk4::STYLE_PROVIDER_PRIORITY_APPLICATION,
    );

    let connected_title = Label::builder()
        .label("Connected to: ")
        .css_classes(["bold-label"])
        .build();
        
    let connected_value = Label::builder()
        .label("Waiting for match...")
        .css_classes(["italic-label"])
        .build();
    
    // Additional CSS for styling
    const CSS_STYLES: &str = "
        label.bold-label { font-weight: bold; }
        label.italic-label { font-style: italic; }
    ";
    let style_provider = gtk4::CssProvider::new();
    style_provider.load_from_data(CSS_STYLES);
    gtk4::style_context_add_provider_for_display(
        &gtk4::gdk::Display::default().expect("Could not connect to a display."),
        &style_provider,
        gtk4::STYLE_PROVIDER_PRIORITY_APPLICATION,
    );
        
    connected_box.append(&connection_dot);
    connected_box.append(&connected_title);
    connected_box.append(&connected_value);

    // Tip label
    let tip_label = Label::new(Some("Tip: You can select multiple servers. The game will decide which one to use based on latency."));
    tip_label.set_wrap(true);
    tip_label.set_max_width_chars(50);
    tip_label.set_margin_start(10);
    tip_label.set_margin_end(10);
    tip_label.set_margin_top(5);
    tip_label.set_margin_bottom(5);

    // Buttons
    let button_box = GtkBox::new(Orientation::Horizontal, 10);
    button_box.set_halign(gtk4::Align::End);
    button_box.set_margin_start(10);
    button_box.set_margin_end(10);
    button_box.set_margin_top(10);
    button_box.set_margin_bottom(10);

    let btn_revert = Button::with_label("Revert to Default");
    let btn_apply = Button::with_label("Apply Selection");
    btn_apply.add_css_class("suggested-action");

    button_box.append(&btn_revert);
    button_box.append(&btn_apply);

    // Initialize AWS service
    let aws_service = Arc::new(AwsIpService::new());

    let (region_tx, region_rx) = std::sync::mpsc::channel::<(String, Option<String>)>();
    {
        let connected_label = connected_value.clone();
        let connection_dot = connection_dot.clone();
        let regions_map = regions.clone();
        let blocked_regions_map = blocked_regions.clone();
        let hosts_manager = hosts_manager.clone();
        let last_update = Rc::new(RefCell::new(None::<DateTime<Local>>));
        let last_update_clone = last_update.clone();

        glib::timeout_add_local(std::time::Duration::from_millis(100), move || {
            let blocked_hosts = hosts_manager.get_blocked_hostnames();
            while let Ok((ip_string, region_name_opt)) = region_rx.try_recv() {
                *last_update_clone.borrow_mut() = Some(Local::now());
                let (text, is_known, region_key_opt) = if let Some(name) = region_name_opt {
                    (name.clone(), true, Some(name))
                } else {
                    (format!("Unknown Region [{}]", ip_string), false, None)
                };

                connected_label.set_text(&text);

                // Determine dot color
                let mut color_class = "waiting"; // Blue (default)

                if is_known {
                    if let Some(key) = region_key_opt {
                        let is_blocked = is_region_blocked_by_hosts(
                            &key,
                            &regions_map,
                            &blocked_regions_map,
                            &blocked_hosts,
                        );

                        if !is_blocked {
                            color_class = "allowed"; // Green
                        } else {
                            color_class = "blocked"; // Red
                        }
                    }
                } else {
                    color_class = "unknown"; // Orange for unknown
                }

                // Apply CSS class to dot (remove existing classes first)
                let _ = connection_dot.remove_css_class("waiting");
                let _ = connection_dot.remove_css_class("good");
                let _ = connection_dot.remove_css_class("bad");
                let _ = connection_dot.remove_css_class("warning");
                let _ = connection_dot.remove_css_class("allowed");
                let _ = connection_dot.remove_css_class("blocked");
                let _ = connection_dot.remove_css_class("unknown");
                connection_dot.add_css_class(color_class);
            }

            let tooltip = if let Some(ts) = *last_update.borrow() {
                let elapsed = (Local::now() - ts).num_seconds().max(0);
                if elapsed >= 5 {
                    connected_label.set_text("Waiting for match...");
                    let _ = connection_dot.remove_css_class("waiting");
                    let _ = connection_dot.remove_css_class("allowed");
                    let _ = connection_dot.remove_css_class("blocked");
                    let _ = connection_dot.remove_css_class("unknown");
                    connection_dot.add_css_class("waiting");
                }
                format_update_tooltip(ts)
            } else {
                connected_label.set_text("Waiting for match...");
                let _ = connection_dot.remove_css_class("allowed");
                let _ = connection_dot.remove_css_class("blocked");
                let _ = connection_dot.remove_css_class("unknown");
                connection_dot.add_css_class("waiting");
                "Most recent connection: —\n\nThis is the region that Dead by Daylight chose\nwhen connecting you to their game.".to_string()
            };
            connected_label.set_tooltip_text(Some(&tooltip));

            glib::ControlFlow::Continue
        });
    }

    // Initialize Sniffer
    let aws_service_clone = aws_service.clone();
    let runtime_clone = tokio_runtime.clone();
    let region_tx_clone = region_tx.clone();
    let last_seen = Arc::new(Mutex::new(None::<(String, Option<String>)>));
    let last_seen_clone = last_seen.clone();

    let sniffer = Arc::new(TrafficSniffer::new(move |remote_ip, _port| {
        if let Ok(last) = last_seen_clone.lock() {
            if let Some((last_ip, last_region)) = &*last {
                if last_ip == &remote_ip {
                    let _ = region_tx_clone.send((remote_ip, last_region.clone()));
                    return;
                }
            }
        }
        let aws = aws_service_clone.clone();
        let runtime = runtime_clone.clone();
        let ip_string = remote_ip.clone();
        let region_tx = region_tx_clone.clone();
        let last_seen_update = last_seen_clone.clone();

        runtime.spawn(async move {
            let region_name_opt = aws.get_region(&ip_string).await;
            if let Ok(mut last) = last_seen_update.lock() {
                *last = Some((ip_string.clone(), region_name_opt.clone()));
            }
            let _ = region_tx.send((ip_string, region_name_opt));
        });
    }));
    
    // Add Dot Color Styles
    const DOT_COLORS: &str = "
        label.waiting { color: #3498db; }
        label.allowed { color: #2ecc71; }
        label.blocked { color: #e74c3c; }
        label.unknown { color: #f39c12; }
    ";
    let dot_provider = gtk4::CssProvider::new();
    dot_provider.load_from_data(DOT_COLORS);
    gtk4::style_context_add_provider_for_display(
        &gtk4::gdk::Display::default().expect("Could not connect to a display."),
        &dot_provider,
        gtk4::STYLE_PROVIDER_PRIORITY_APPLICATION,
    );

    // Create app state
    let app_state = Rc::new(AppState {
        config: config.clone(),
        regions: regions.clone(),
        blocked_regions: blocked_regions.clone(),
        settings: settings.clone(),
        hosts_manager,
        update_checker,
        selected_regions: RefCell::new(HashSet::new()),
        list_store: list_store.clone(),
        tokio_runtime,
        sniffer,
        aws_service,
        connected_to_label: connected_value,
        connection_dot: connection_dot, 
    });

    // Create menu bar
    let menu_box = GtkBox::new(Orientation::Horizontal, 5);
    menu_box.set_margin_start(5);
    menu_box.set_margin_end(5);
    menu_box.set_margin_top(5);
    menu_box.set_margin_bottom(5);

    // Version menu button
    let version_menu = create_version_menu(&window, &app_state);
    let version_btn = MenuButton::builder()
        .label(&config.current_version)
        .menu_model(&version_menu)
        .build();

    // Options menu button
    let options_menu = create_options_menu();
    let options_btn = MenuButton::builder()
        .label("Options")
        .menu_model(&options_menu)
        .build();

    // Help menu button
    let help_menu = create_help_menu(&app_state);
    let help_btn = MenuButton::builder()
        .label("Help")
        .menu_model(&help_menu)
        .build();

    // Set up menu actions
    setup_menu_actions(app, &window, &app_state);

    menu_box.append(&version_btn);
    menu_box.append(&options_btn);
    menu_box.append(&help_btn);

    // Main layout
    let main_box = GtkBox::new(Orientation::Vertical, 0);
    main_box.append(&menu_box);
    main_box.append(&Separator::new(Orientation::Horizontal));
    main_box.append(&connected_box);
    main_box.append(&tip_label);
    main_box.append(&scrolled);
    main_box.append(&button_box);

    window.set_child(Some(&main_box));

    // Handle checkbox toggles
    let app_state_clone = app_state.clone();
    cell_toggle.connect_toggled(move |_, path| {
        let list_store = &app_state_clone.list_store;
        if let Some(iter) = list_store.iter(&path) {
            // Check if this is a divider row (dividers shouldn't be toggleable)
            let is_divider = list_store.get::<bool>(&iter, 4);
            if is_divider {
                return; // Don't allow toggling dividers
            }

            let checked = list_store.get::<bool>(&iter, 3);
            list_store.set(&iter, &[(3, &!checked)]);

            // Update selected regions
            let region_name = list_store.get::<String>(&iter, 0);
            let clean_name = region_name.replace(" ⚠︎", "");
            let mut selected = app_state_clone.selected_regions.borrow_mut();
            if !checked {
                selected.insert(clean_name);
            } else {
                selected.remove(&clean_name);
            }
        }
    });

    // Connect button signals
    let app_state_clone = app_state.clone();
    let window_clone = window.clone();
    btn_apply.connect_clicked(move |_| {
        handle_apply_click(&app_state_clone, &window_clone);
    });

    let app_state_clone = app_state.clone();
    let window_clone = window.clone();
    btn_revert.connect_clicked(move |_| {
        handle_revert_click(&app_state_clone, &window_clone);
    });

    // Start ping timer
    start_ping_timer(app_state.clone());

    // Ensure helper sniffer exits when the window closes
    let app_state_clone = app_state.clone();
    window.connect_close_request(move |_| {
        app_state_clone.sniffer.stop();
        glib::Propagation::Proceed
    });

    // Check for updates silently on launch
    check_for_updates_silent(&app_state, &window);

    window.present();
}

fn create_version_menu(_window: &ApplicationWindow, _app_state: &Rc<AppState>) -> Menu {
    let menu = Menu::new();
    menu.append(Some("Check for updates"), Some("app.check-updates"));
    menu.append(Some("Repository (⭐)"), Some("app.repository"));
    menu.append(Some("About"), Some("app.about"));
    menu.append(Some("Open hosts file location"), Some("app.open-hosts"));
    menu.append(Some("Reset hosts file"), Some("app.reset-hosts"));
    menu
}

fn create_options_menu() -> Menu {
    let menu = Menu::new();
    menu.append(Some("Program settings"), Some("app.settings"));
    menu.append(Some("Custom splash art"), Some("app.custom-splash"));
    menu.append(
        Some("Auto-skip loading screen trailer"),
        Some("app.skip-trailer"),
    );
    menu
}

fn create_help_menu(_app_state: &Rc<AppState>) -> Menu {
    let menu = Menu::new();
    menu.append(Some("Discord (Get support)"), Some("app.discord"));
    menu
}

fn setup_menu_actions(app: &Application, window: &ApplicationWindow, app_state: &Rc<AppState>) {
    // Check for updates action
    let action = SimpleAction::new("check-updates", None);
    let app_state_clone = app_state.clone();
    let window_clone = window.clone();
    action.connect_activate(move |_, _| {
        check_for_updates_action(&app_state_clone, &window_clone);
    });
    app.add_action(&action);

    // Repository action
    let action = SimpleAction::new("repository", None);
    let repo_url = app_state.config.repo_url.clone();
    let window_clone = window.clone();
    action.connect_activate(move |_, _| {
        if let Some(url) = &repo_url {
            let dialog = MessageDialog::new(
                Some(&window_clone),
                gtk4::DialogFlags::MODAL,
                MessageType::Info,
                ButtonsType::OkCancel,
                "Repository",
            );
            dialog.set_secondary_text(Some(
                "Pressing \"Continue\" will open the project's public repository.\n\nPlease star the repository if you are able to do so as it increases awareness of the project! <3"
            ));

            // Change button labels
            if let Some(widget) = dialog.widget_for_response(ResponseType::Ok) {
                if let Some(button) = widget.downcast_ref::<Button>() {
                    button.set_label("Continue");
                }
            }

            let url_clone = url.clone();
            dialog.run_async(move |dialog, response| {
                if response == ResponseType::Ok {
                    open_url(&url_clone);
                }
                dialog.close();
            });
        } else {
            show_error_dialog(
                &window_clone,
                "Repository",
                "Unable to open repository.\n\nThe application was unable to fetch the git identity and therefore couldn't determine the repository URL.\n\nThis may be due to network issues or GitHub API issues.\nAn update to fix this issue has most likely been released, please check manually by joining the Discord server or doing a web search."
            );
        }
    });
    app.add_action(&action);

    // About action
    let action = SimpleAction::new("about", None);
    let app_state_clone = app_state.clone();
    let window_clone = window.clone();
    action.connect_activate(move |_, _| {
        show_about_dialog(&app_state_clone, &window_clone);
    });
    app.add_action(&action);

    // Open hosts location action
    let action = SimpleAction::new("open-hosts", None);
    action.connect_activate(move |_, _| {
        // Open /etc directory in file manager
        let _ = std::process::Command::new("xdg-open")
            .arg("/etc")
            .spawn();
    });
    app.add_action(&action);

    // Reset hosts action
    let action = SimpleAction::new("reset-hosts", None);
    let app_state_clone = app_state.clone();
    let window_clone = window.clone();
    action.connect_activate(move |_, _| {
        reset_hosts_action(&app_state_clone, &window_clone);
    });
    app.add_action(&action);

    // Program settings action
    let action = SimpleAction::new("settings", None);
    let app_state_clone = app_state.clone();
    let window_clone = window.clone();
    action.connect_activate(move |_, _| {
        show_settings_dialog(&app_state_clone, &window_clone);
    });
    app.add_action(&action);

    // Discord action
    let action = SimpleAction::new("discord", None);
    let discord_url = app_state.config.discord_url.clone();
    action.connect_activate(move |_, _| {
        open_url(&discord_url);
    });
    app.add_action(&action);

    // Custom splash art action
    let action = SimpleAction::new("custom-splash", None);
    let window_clone = window.clone();
    let app_state_clone = app_state.clone();
    action.connect_activate(move |_, _| {
        show_custom_splash_dialog(&app_state_clone, &window_clone);
    });
    app.add_action(&action);

    // Skip trailer action
    let action = SimpleAction::new("skip-trailer", None);
    let window_clone = window.clone();
    let app_state_clone = app_state.clone();
    action.connect_activate(move |_, _| {
        show_skip_trailer_dialog(&app_state_clone, &window_clone);
    });
    app.add_action(&action);
}

fn show_custom_splash_dialog(app_state: &Rc<AppState>, window: &ApplicationWindow) {
    let game_path = get_saved_game_path(app_state, window);
    if game_path.is_none() {
        return;
    }
    let game_path = game_path.unwrap();

    let dialog = Dialog::with_buttons(
        Some("Custom splash art"),
        Some(window),
        gtk4::DialogFlags::MODAL,
        &[
            ("Upload image…", ResponseType::Accept),
            ("Revert to default", ResponseType::Reject),
            ("Cancel", ResponseType::Cancel),
        ],
    );

    dialog.set_default_width(420);

    if let Some(action_area) = dialog.child().and_then(|c| c.last_child()) {
        action_area.set_margin_start(15);
        action_area.set_margin_end(15);
        action_area.set_margin_top(10);
        action_area.set_margin_bottom(15);
    }

    let content = dialog.content_area();
    content.set_margin_start(15);
    content.set_margin_end(15);
    content.set_margin_top(10);
    content.set_margin_bottom(10);

    let description = Label::new(Some(
        "This lets you use custom artwork for the EAC splash screen that pops up when you launch the game.",
    ));
    description.set_halign(gtk4::Align::Start);
    description.set_wrap(true);
    description.set_margin_top(5);
    description.set_margin_bottom(10);
    content.append(&description);
    let info = Label::new(Some(
        "Requirements:\n• PNG image\n• 800 x 450 pixels",
    ));
    info.set_halign(gtk4::Align::Start);
    info.set_wrap(true);
    info.set_margin_top(10);
    info.set_margin_bottom(5);
    content.append(&info);

    let window_clone = window.clone();
    dialog.connect_response(move |dialog, response| {
        dialog.close();

        match response {
            ResponseType::Accept => {
                let window_for_image = window_clone.clone();
                let window_for_result_inner = window_clone.clone();
                let game_path = game_path.clone();
                select_image_file(&window_for_image, move |image_path| {
                    if let Err(err) = apply_custom_splash(&game_path, &image_path) {
                        show_error_dialog(
                            &window_for_result_inner,
                            "Custom splash art",
                            &format!("Failed to apply custom splash art:\n{}", err),
                        );
                    } else {
                        show_info_dialog(
                            &window_for_result_inner,
                            "Custom splash art",
                            "Custom splash art applied.",
                        );
                    }
                });
            }
            ResponseType::Reject => {
                match revert_custom_splash(&game_path) {
                    Ok(true) => show_info_dialog(
                        &window_clone,
                        "Custom splash art",
                        "Reverted to default splash art.",
                    ),
                    Ok(false) => show_error_dialog(
                        &window_clone,
                        "Custom splash art",
                        "No backup found to restore.",
                    ),
                    Err(err) => show_error_dialog(
                        &window_clone,
                        "Custom splash art",
                        &format!("Failed to revert splash art:\n{}", err),
                    ),
                }
            }
            _ => {}
        }
    });

    dialog.show();
}

fn show_skip_trailer_dialog(app_state: &Rc<AppState>, window: &ApplicationWindow) {
    let game_path = get_saved_game_path(app_state, window);
    if game_path.is_none() {
        return;
    }
    let game_path = game_path.unwrap();

    let dialog = Dialog::with_buttons(
        Some("Auto-skip loading screen trailer"),
        Some(window),
        gtk4::DialogFlags::MODAL,
        &[
            ("Disable trailer", ResponseType::Accept),
            ("Revert to default", ResponseType::Reject),
            ("Cancel", ResponseType::Cancel),
        ],
    );

    if let Some(action_area) = dialog.child().and_then(|c| c.last_child()) {
        action_area.set_margin_start(15);
        action_area.set_margin_end(15);
        action_area.set_margin_top(10);
        action_area.set_margin_bottom(15);
    }

    let content = dialog.content_area();
    content.set_margin_start(15);
    content.set_margin_end(15);
    content.set_margin_top(10);
    content.set_margin_bottom(10);

    let description = Label::new(Some(
        "This will automatically skip the current DbD chapter's trailer video that plays everytime you launch the game.",
    ));
    description.set_halign(gtk4::Align::Start);
    description.set_wrap(true);
    description.set_margin_top(5);
    description.set_margin_bottom(10);
    content.append(&description);

    let window_clone = window.clone();
    dialog.connect_response(move |dialog, response| {
        dialog.close();

        match response {
            ResponseType::Accept => {
                if let Err(err) = apply_skip_trailer(&game_path) {
                    show_error_dialog(
                        &window_clone,
                        "Skip trailer",
                        &format!("Failed to disable trailer:\n{}", err),
                    );
                } else {
                    show_info_dialog(
                        &window_clone,
                        "Skip trailer",
                        "Trailer disabled.",
                    );
                }
            }
            ResponseType::Reject => {
                match revert_skip_trailer(&game_path) {
                    Ok(true) => show_info_dialog(
                        &window_clone,
                        "Skip trailer",
                        "Reverted to default trailer.",
                    ),
                    Ok(false) => show_error_dialog(
                        &window_clone,
                        "Skip trailer",
                        "No backup found to restore.",
                    ),
                    Err(err) => show_error_dialog(
                        &window_clone,
                        "Skip trailer",
                        &format!("Failed to revert trailer:\n{}", err),
                    ),
                }
            }
            _ => {}
        }
    });

    dialog.show();
}

fn select_game_path<F: FnOnce(std::path::PathBuf) + 'static>(
    window: &ApplicationWindow,
    on_selected: F,
) {
    let dialog = FileChooserNative::new(
        Some("Select game folder"),
        Some(window),
        FileChooserAction::SelectFolder,
        Some("Select"),
        Some("Cancel"),
    );

    let on_selected = Rc::new(RefCell::new(Some(on_selected)));
    dialog.run_async(move |dialog, response| {
        if response == ResponseType::Accept {
            if let Some(file) = dialog.file() {
                if let Some(path) = file.path() {
                    if let Some(callback) = on_selected.borrow_mut().take() {
                        callback(path);
                    }
                }
            }
        }
        dialog.destroy();
    });
}

fn select_image_file<F: FnOnce(std::path::PathBuf) + 'static>(
    window: &ApplicationWindow,
    on_selected: F,
) {
    let dialog = FileChooserNative::new(
        Some("Select splash image (800x450)"),
        Some(window),
        FileChooserAction::Open,
        Some("Open"),
        Some("Cancel"),
    );

    let filter = FileFilter::new();
    filter.add_mime_type("image/png");
    filter.add_mime_type("image/jpeg");
    filter.add_pattern("*.png");
    filter.add_pattern("*.jpg");
    filter.add_pattern("*.jpeg");
    dialog.add_filter(&filter);

    let on_selected = Rc::new(RefCell::new(Some(on_selected)));
    dialog.run_async(move |dialog, response| {
        if response == ResponseType::Accept {
            if let Some(file) = dialog.file() {
                if let Some(path) = file.path() {
                    if let Some(callback) = on_selected.borrow_mut().take() {
                        callback(path);
                    }
                }
            }
        }
        dialog.destroy();
    });
}

fn apply_custom_splash(game_path: &std::path::Path, image_path: &std::path::Path) -> anyhow::Result<()> {
    let pixbuf = gtk4::gdk_pixbuf::Pixbuf::from_file(image_path)?;
    if pixbuf.width() != 800 || pixbuf.height() != 450 {
        anyhow::bail!("Image must be exactly 800x450 pixels.");
    }

    let target_dir = game_path.join("EasyAntiCheat");
    let target_path = target_dir.join("SplashScreen.png");
    let backup_path = target_dir.join("SplashScreen.png.bak");

    std::fs::create_dir_all(&target_dir)?;
    if backup_path.exists() {
        let _ = std::fs::remove_file(&backup_path);
    }
    if target_path.exists() {
        std::fs::rename(&target_path, &backup_path)?;
    }
    std::fs::copy(image_path, &target_path)?;
    Ok(())
}

fn revert_custom_splash(game_path: &std::path::Path) -> anyhow::Result<bool> {
    let target_dir = game_path.join("EasyAntiCheat");
    let target_path = target_dir.join("SplashScreen.png");
    let backup_path = target_dir.join("SplashScreen.png.bak");

    if !backup_path.exists() {
        return Ok(false);
    }
    if target_path.exists() {
        let _ = std::fs::remove_file(&target_path);
    }
    std::fs::rename(&backup_path, &target_path)?;
    Ok(true)
}

fn apply_skip_trailer(game_path: &std::path::Path) -> anyhow::Result<()> {
    let target_path = game_path
        .join("DeadByDaylight")
        .join("Content")
        .join("Movies")
        .join("LoadingScreen.bk2");
    let backup_path = target_path.with_extension("bk2.bak");

    if !target_path.exists() {
        anyhow::bail!("LoadingScreen.bk2 not found.");
    }
    if backup_path.exists() {
        let _ = std::fs::remove_file(&backup_path);
    }
    std::fs::rename(&target_path, &backup_path)?;
    Ok(())
}

fn revert_skip_trailer(game_path: &std::path::Path) -> anyhow::Result<bool> {
    let target_path = game_path
        .join("DeadByDaylight")
        .join("Content")
        .join("Movies")
        .join("LoadingScreen.bk2");
    let backup_path = target_path.with_extension("bk2.bak");

    if !backup_path.exists() {
        return Ok(false);
    }
    if target_path.exists() {
        let _ = std::fs::remove_file(&target_path);
    }
    std::fs::rename(&backup_path, &target_path)?;
    Ok(true)
}

fn open_url(url: &str) {
    // Use the `open` crate for cross-platform URL opening
    let _ = open::that(url);
}

fn get_all_regions_map(
    selectable: &HashMap<String, RegionInfo>,
    blocked: &HashMap<String, RegionInfo>,
) -> HashMap<String, RegionInfo> {
    let mut all = selectable.clone();
    for (k, v) in blocked.iter() {
        all.insert(k.clone(), v.clone());
    }
    all
}

fn check_for_updates_action(app_state: &Rc<AppState>, window: &ApplicationWindow) {
    let window = window.clone();
    let update_checker = app_state.update_checker.clone();
    let current_version = app_state.config.current_version.clone();
    let runtime = app_state.tokio_runtime.clone();
    let repo_url = app_state.config.repo_url.clone();
    let settings = app_state.settings.clone();

    // Check if developer identity was fetched
    if repo_url.is_none() {
        show_error_dialog(
            &window,
            "Check For Updates",
            "Unable to check for updates.\n\nThe application was unable to fetch the git identity and therefore couldn't determine the repository URL.\n\nThis may be due to network issues or GitHub API issues.\nAn update to fix this issue has most likely been released, please check manually by joining the Discord server or doing a web search."
        );
        return;
    }

    let releases_url = update_checker.get_releases_url();

    glib::spawn_future_local(async move {
        let result = runtime
            .spawn(async move { update_checker.check_for_updates().await })
            .await
            .unwrap();

        match result {
            Ok(Some(new_version)) => {
                let dialog = MessageDialog::new(
                    Some(&window),
                    gtk4::DialogFlags::MODAL,
                    MessageType::Question,
                    ButtonsType::None,
                    "Update Available",
                );
                dialog.set_secondary_text(Some(&format!(
                    "A new version is available: {}.\nWould you like to visit the repository?\n\nYour version: {}\n\nOn Arch, it is recommended to use your package manager to update.",
                    new_version, current_version
                )));

                let combo = ComboBoxText::new();
                combo.append(Some("now"), "Update now");
                combo.append(Some("3days"), "Ask again in 3 days");
                combo.append(Some("14days"), "Ask again in 14 days");
                combo.append(Some("21days"), "Ask again in 21 days");
                combo.set_active_id(Some("now"));
                combo.set_margin_top(10);
                combo.set_margin_bottom(10);
                combo.set_margin_start(10);
                combo.set_margin_end(10);

                dialog.content_area().append(&combo);
                dialog.add_button("Not now", ResponseType::Close);
                dialog.add_button("Continue", ResponseType::Ok);

                dialog.run_async(move |dialog, response| {
                    if response == ResponseType::Ok {
                        let active = combo.active_id().map(|s| s.to_string()).unwrap_or_default();
                        if active == "now" {
                            open_url(&releases_url);
                        } else {
                            let days = match active.as_str() {
                                "3days" => 3,
                                "14days" => 14,
                                "21days" => 21,
                                _ => 0,
                            };
                            if days > 0 {
                                let mut settings = settings.lock().unwrap();
                                let date = chrono::Local::now() + chrono::Duration::days(days);
                                settings.auto_update_check_paused_until = Some(date.to_rfc3339());
                                let _ = settings.save();
                            }
                        }
                    }
                    dialog.close();
                });
            }
            Ok(None) => {
                show_info_dialog(
                    &window,
                    "Check For Updates",
                    "You're already using the latest release! :D",
                );
            }
            Err(e) => {
                show_error_dialog(
                    &window,
                    "Error",
                    &format!("Error while checking for updates:\n{}", e),
                );
            }
        }
    });
}

fn check_for_updates_silent(app_state: &Rc<AppState>, window: &ApplicationWindow) {
    {
        let settings = app_state.settings.lock().unwrap();
        if let Some(paused_until) = &settings.auto_update_check_paused_until {
            if let Ok(date) = chrono::DateTime::parse_from_rfc3339(paused_until) {
                if chrono::Local::now() < date {
                    return;
                }
            }
        }
    }

    // Don't check silently if developer identity wasn't fetched
    if app_state.config.repo_url.is_none() {
        show_error_dialog(
            window,
            "Check For Updates",
            "Unable to check for updates.\n\nThe application was unable to fetch the git identity and therefore couldn't determine the repository URL.\n\nThis may be due to network issues or GitHub API issues.\nAn update to fix this issue has most likely been released, please check manually by joining the Discord server or doing a web search."
        );
        return;
    }

    let window = window.clone();
    let update_checker = app_state.update_checker.clone();
    let current_version = app_state.config.current_version.clone();
    let runtime = app_state.tokio_runtime.clone();
    let releases_url = update_checker.get_releases_url();
    let settings = app_state.settings.clone();

    glib::spawn_future_local(async move {
        let result = runtime
            .spawn(async move { update_checker.check_for_updates().await })
            .await
            .unwrap();

        // Only show dialog if there's a new version available
        if let Ok(Some(new_version)) = result {
            let dialog = MessageDialog::new(
                Some(&window),
                gtk4::DialogFlags::MODAL,
                MessageType::Question,
                ButtonsType::None,
                "Update Available",
            );
            dialog.set_secondary_text(Some(&format!(
                "A new version is available: {}.\nWould you like to visit the repository?\n\nYour version: {}\n\nOn Arch, it is recommended to use your package manager to update.",
                new_version, current_version
            )));

            let combo = ComboBoxText::new();
            combo.append(Some("now"), "Update now");
            combo.append(Some("3days"), "Ask again in 3 days");
            combo.append(Some("14days"), "Ask again in 14 days");
            combo.append(Some("21days"), "Ask again in 21 days");
            combo.set_active_id(Some("now"));
            combo.set_margin_top(10);
            combo.set_margin_bottom(10);
            combo.set_margin_start(10);
            combo.set_margin_end(10);

            dialog.content_area().append(&combo);
            dialog.add_button("Not now", ResponseType::Close);
            dialog.add_button("Continue", ResponseType::Ok);

            dialog.run_async(move |dialog, response| {
                if response == ResponseType::Ok {
                    let active = combo.active_id().map(|s| s.to_string()).unwrap_or_default();
                    if active == "now" {
                        open_url(&releases_url);
                    } else {
                        let days = match active.as_str() {
                            "3days" => 3,
                            "14days" => 14,
                            "21days" => 21,
                            _ => 0,
                        };
                        if days > 0 {
                            let mut settings = settings.lock().unwrap();
                            let date = chrono::Local::now() + chrono::Duration::days(days);
                            settings.auto_update_check_paused_until = Some(date.to_rfc3339());
                            let _ = settings.save();
                        }
                    }
                }
                dialog.close();
            });
        }
        // If Ok(None) or Err, do nothing (silent)
    });
}

fn show_about_dialog(app_state: &Rc<AppState>, window: &ApplicationWindow) {
    let dialog = Dialog::with_buttons(
        Some("About Make Your Choice"),
        Some(window),
        gtk4::DialogFlags::MODAL,
        &[("Awesome!", ResponseType::Ok)],
    );
    dialog.set_default_width(480);

    // Add margin to the button area
    if let Some(action_area) = dialog.child().and_then(|c| c.last_child()) {
        action_area.set_margin_start(15);
        action_area.set_margin_end(15);
        action_area.set_margin_top(10);
        action_area.set_margin_bottom(15);
    }

    let content = dialog.content_area();
    let vbox = GtkBox::new(Orientation::Vertical, 10);
    vbox.set_margin_start(20);
    vbox.set_margin_end(20);
    vbox.set_margin_top(20);
    vbox.set_margin_bottom(20);

    let title = Label::new(Some("Make Your Choice (DbD Server Selector)"));
    title.add_css_class("title-2");

    // Developer label. This must always refer to the original developer. Changing this breaks license compliance.
    let developer_box = GtkBox::new(Orientation::Horizontal, 5);
    developer_box.set_halign(gtk4::Align::Start);
    let developer_label = Label::new(Some("Developer: "));
    developer_box.append(&developer_label);

    if let Some(dev) = &app_state.config.developer {
        let developer_link = gtk4::LinkButton::with_label(
            &format!("https://github.com/{}", dev),
            dev,
        );
        developer_link.set_halign(gtk4::Align::Start);
        developer_box.append(&developer_link);
    } else {
        let unknown_label = Label::new(Some("(unknown)"));
        unknown_label.set_halign(gtk4::Align::Start);
        developer_box.append(&unknown_label);
    }

    let version = Label::new(Some(&format!(
        "Version {}\nLinux (GTK4)",
        app_state.config.current_version
    )));
    version.set_halign(gtk4::Align::Start);

    // Copyright notice
    let copyright = Label::new(Some("Copyright © 2026"));
    copyright.set_halign(gtk4::Align::Start);

    // License information
    let license = Label::new(Some(
        "This program is free software licensed\n\
        under the terms of the GNU General Public License.\n\
        This program is distributed in the hope that it will be useful, but\n\
        without any warranty. See the GNU General Public License\n\
        for more details."
    ));
    license.set_halign(gtk4::Align::Start);
    license.set_wrap(true);
    license.set_max_width_chars(60);

    vbox.append(&title);
    vbox.append(&developer_box);
    vbox.append(&version);
    vbox.append(&Separator::new(Orientation::Horizontal));
    vbox.append(&copyright);
    vbox.append(&license);
    content.append(&vbox);

    dialog.run_async(|dialog, _| dialog.close());
    dialog.show();
}

fn reset_hosts_action(app_state: &Rc<AppState>, window: &ApplicationWindow) {
    let dialog = MessageDialog::new(
        Some(window),
        gtk4::DialogFlags::MODAL,
        MessageType::Warning,
        ButtonsType::YesNo,
        "Restore Linux default hosts file",
    );
    dialog.set_secondary_text(Some(
        "If you are having problems, or the program doesn't seem to work correctly, try resetting your hosts file.\n\n\
        This will overwrite your entire hosts file with the Linux default.\n\n\
        A backup will be saved as hosts.bak. Continue?"
    ));

    let app_state = app_state.clone();
    let window = window.clone();
    dialog.run_async(move |dialog, response| {
        if response == ResponseType::Yes {
            match app_state.hosts_manager.restore_default() {
                Ok(_) => {
                    show_info_dialog(
                        &window,
                        "Success",
                        "Hosts file restored to Linux default template.",
                    );
                }
                Err(e) => {
                    show_error_dialog(&window, "Error", &e.to_string());
                }
            }
        }
        dialog.close();
    });
}

fn show_conflict_dialog(
    window: &ApplicationWindow,
    app_state: &Rc<AppState>,
    selected: &HashSet<String>,
    settings: &std::sync::MutexGuard<UserSettings>,
) {
    let dialog = Dialog::with_buttons(
        Some("Conflicting Hosts Entries Detected"),
        Some(window),
        gtk4::DialogFlags::MODAL,
        &[
            ("Cancel", ResponseType::Cancel),
            ("Continue", ResponseType::Ok),
        ],
    );
    dialog.set_default_width(500);
    dialog.set_default_height(280);

    // Add margin to button area
    if let Some(action_area) = dialog.child().and_then(|c| c.last_child()) {
        action_area.set_margin_start(15);
        action_area.set_margin_end(15);
        action_area.set_margin_top(10);
        action_area.set_margin_bottom(15);
    }

    let content = dialog.content_area();
    let vbox = GtkBox::new(Orientation::Vertical, 15);
    vbox.set_margin_start(20);
    vbox.set_margin_end(20);
    vbox.set_margin_top(20);
    vbox.set_margin_bottom(20);

    let message = Label::new(Some(
        "It seems like there are conflicting entries in your hosts file.\n\n\
        This is usually caused by another program, or by manual changes.\n\n\
        It's best to resolve these issues first before applying a new configuration.\n\
        Would you like to clear out all conflicting entries?"
    ));
    message.set_wrap(true);
    message.set_max_width_chars(60);
    message.set_halign(gtk4::Align::Start);

    let rb_clear = gtk4::CheckButton::with_label("Clear out conflicts, and apply selection (recommended)");
    rb_clear.set_active(true);

    let rb_keep = gtk4::CheckButton::with_label("Apply selection without clearing out conflicts");
    rb_keep.set_group(Some(&rb_clear));

    vbox.append(&message);
    vbox.append(&rb_clear);
    vbox.append(&rb_keep);
    content.append(&vbox);

    let app_state_clone = app_state.clone();
    let window_clone = window.clone();
    let selected_clone = selected.clone();
    let apply_mode = settings.apply_mode;
    let block_mode = settings.block_mode;
    let merge_unstable = settings.merge_unstable;

    dialog.connect_response(move |dialog, response| {
        if response != ResponseType::Ok {
            dialog.close();
            return;
        }

        let clear_conflicts = rb_clear.is_active();

        if !clear_conflicts {
            // Show confirmation dialog
            let confirm_dialog = MessageDialog::new(
                Some(&window_clone),
                gtk4::DialogFlags::MODAL,
                MessageType::Warning,
                ButtonsType::YesNo,
                "Confirm",
            );
            confirm_dialog.set_secondary_text(Some(
                "Not clearing out conflicting entries will cause unexpected behavior.\n\n\
                Are you sure you want to continue?"
            ));

            let app_state_clone2 = app_state_clone.clone();
            let window_clone2 = window_clone.clone();
            let selected_clone2 = selected_clone.clone();

            confirm_dialog.run_async(move |confirm_dialog, confirm_response| {
                if confirm_response == ResponseType::Yes {
                    // User confirmed, proceed without clearing conflicts
                    apply_hosts_changes(&app_state_clone2, &window_clone2, &selected_clone2, apply_mode, block_mode, merge_unstable);
                }
                confirm_dialog.close();
            });

            dialog.close();
        } else {
            // Clear conflicts first, then apply
            match app_state_clone.hosts_manager.detect_conflicting_entries(
                &get_all_regions_map(&app_state_clone.regions, &app_state_clone.blocked_regions),
            ) {
                Ok(conflicts) => {
                    if let Err(e) = app_state_clone.hosts_manager.clear_conflicting_entries(&conflicts) {
                        show_error_dialog(&window_clone, "Error", &format!("Failed to clear conflicting entries:\n{}", e));
                        dialog.close();
                        return;
                    }
                }
                Err(e) => {
                    show_error_dialog(&window_clone, "Error", &format!("Failed to check for conflicts:\n{}", e));
                    dialog.close();
                    return;
                }
            }

            // Conflicts cleared, now apply
            apply_hosts_changes(&app_state_clone, &window_clone, &selected_clone, apply_mode, block_mode, merge_unstable);
            dialog.close();
        }
    });

    dialog.show();
}

fn apply_hosts_changes(
    app_state: &Rc<AppState>,
    window: &ApplicationWindow,
    selected: &HashSet<String>,
    apply_mode: ApplyMode,
    block_mode: BlockMode,
    merge_unstable: bool,
) {
    let result = match apply_mode {
        ApplyMode::Gatekeep => app_state.hosts_manager.apply_gatekeep(
            &app_state.regions,
            &app_state.blocked_regions,
            selected,
            block_mode,
            merge_unstable,
        ),
        ApplyMode::UniversalRedirect => {
            if selected.len() != 1 {
                show_error_dialog(
                    window,
                    "Universal Redirect",
                    "Please select only one server when using Universal Redirect mode.",
                );
                return;
            }
            let region = selected.iter().next().unwrap();
            app_state
                .hosts_manager
                .apply_universal_redirect(&app_state.regions, &app_state.blocked_regions, region)
        }
    };

    match result {
        Ok(_) => {
            show_info_dialog(
                window,
                "Success",
                &format!(
                    "The hosts file was updated successfully ({:?} mode).\n\nPlease restart the game for changes to take effect.",
                    apply_mode
                ),
            );
        }
        Err(e) => {
            show_error_dialog(window, "Error", &e.to_string());
        }
    }
}

fn handle_apply_click(app_state: &Rc<AppState>, window: &ApplicationWindow) {
    let selected = app_state.selected_regions.borrow().clone();
    let settings = app_state.settings.lock().unwrap();

    // Check for conflicting entries before proceeding
    match app_state.hosts_manager.detect_conflicting_entries(
        &get_all_regions_map(&app_state.regions, &app_state.blocked_regions),
    ) {
        Ok(conflicts) if !conflicts.is_empty() => {
            // Show conflict dialog and let it handle everything
            show_conflict_dialog(window, app_state, &selected, &settings);
            return;
        }
        Err(e) => {
            show_error_dialog(window, "Error", &format!("Failed to check for conflicts:\n{}", e));
            return;
        }
        _ => {} // No conflicts, continue
    }

    // No conflicts, apply directly
    let apply_mode = settings.apply_mode;
    let block_mode = settings.block_mode;
    let merge_unstable = settings.merge_unstable;
    drop(settings); // Release lock before applying

    apply_hosts_changes(app_state, window, &selected, apply_mode, block_mode, merge_unstable);
}

fn handle_revert_click(app_state: &Rc<AppState>, window: &ApplicationWindow) {
    match app_state.hosts_manager.revert() {
        Ok(_) => {
            show_info_dialog(
                window,
                "Reverted",
                "Cleared Make Your Choice entries. Your existing hosts lines were left untouched.",
            );
        }
        Err(e) => {
            show_error_dialog(window, "Error", &e.to_string());
        }
    }
}

fn show_settings_dialog(app_state: &Rc<AppState>, parent: &ApplicationWindow) {
    let dialog = Dialog::with_buttons(
        Some("Program Settings"),
        Some(parent),
        gtk4::DialogFlags::MODAL,
        &[
            ("Revert to Default", ResponseType::Other(1)),
            ("Apply", ResponseType::Ok),
        ],
    );
    dialog.set_default_width(350);

    // Add margin to the button area and style buttons
    if let Some(action_area) = dialog.child().and_then(|c| c.last_child()) {
        action_area.set_margin_start(15);
        action_area.set_margin_end(15);
        action_area.set_margin_top(10);
        action_area.set_margin_bottom(15);
    }

    let content = dialog.content_area();
    let settings_box = GtkBox::new(Orientation::Vertical, 10);
    settings_box.set_margin_start(15);
    settings_box.set_margin_end(15);
    settings_box.set_margin_top(15);
    settings_box.set_margin_bottom(15);

    // Apply mode
    let mode_label = Label::new(Some("Method:"));
    mode_label.set_halign(gtk4::Align::Start);
    let mode_combo = ComboBoxText::new();
    mode_combo.append_text("Gatekeep (default)");
    mode_combo.append_text("Universal Redirect (deprecated)");

    let mode_notice = Label::new(Some(
        "After changing this setting, reapply your selection to apply changes.",
    ));
    mode_notice.set_wrap(true);
    mode_notice.set_max_width_chars(40);
    mode_notice.set_halign(gtk4::Align::Start);

    let settings = app_state.settings.lock().unwrap();
    mode_combo.set_active(Some(match settings.apply_mode {
        ApplyMode::Gatekeep => 0,
        ApplyMode::UniversalRedirect => 1,
    }));

    // Block mode - using CheckButtons in radio mode
    let block_label = Label::new(Some("Gatekeep Options:"));
    block_label.set_halign(gtk4::Align::Start);
    let rb_both = CheckButton::with_label("Block both (default)");
    let rb_ping = CheckButton::with_label("Block UDP ping beacon endpoints");
    let rb_service = CheckButton::with_label("Block service endpoints");

    // Group the checkbuttons to act like radio buttons
    rb_ping.set_group(Some(&rb_both));
    rb_service.set_group(Some(&rb_both));

    match settings.block_mode {
        BlockMode::Both => rb_both.set_active(true),
        BlockMode::OnlyPing => rb_ping.set_active(true),
        BlockMode::OnlyService => rb_service.set_active(true),
    }

    // Merge unstable
    let merge_check = CheckButton::with_label("Merge unstable servers (recommended)");
    merge_check.set_active(settings.merge_unstable);

    settings_box.append(&mode_label);
    settings_box.append(&mode_combo);
    settings_box.append(&mode_notice);
    settings_box.append(&Separator::new(Orientation::Horizontal));
    settings_box.append(&block_label);
    settings_box.append(&rb_both);
    settings_box.append(&rb_ping);
    settings_box.append(&rb_service);
    settings_box.append(&merge_check);
    settings_box.append(&Separator::new(Orientation::Horizontal));

    // Game folder
    let game_path_label = Label::new(Some("Game folder:"));
    game_path_label.set_halign(gtk4::Align::Start);
    let game_path_entry = Entry::new();
    game_path_entry.set_hexpand(true);
    let browse_button = Button::with_label("Browse…");

    let game_path_row = GtkBox::new(Orientation::Horizontal, 6);
    game_path_row.append(&game_path_entry);
    game_path_row.append(&browse_button);

    let hint_label = Label::new(Some(
        "Tip: In Steam, right-click Dead by Daylight → Manage → Browse local files.\nThe folder that opens is the one you should select.\n\nThis setting is only required for some features like custom splash art and auto-skip trailer.",
    ));
    hint_label.set_wrap(true);
    hint_label.set_max_width_chars(40);
    hint_label.set_halign(gtk4::Align::Start);

    game_path_entry.set_text(&settings.game_path);
    drop(settings);

    settings_box.append(&game_path_label);
    settings_box.append(&game_path_row);
    settings_box.append(&hint_label);
    settings_box.append(&Separator::new(Orientation::Horizontal));

    // Tip label
    let tip_label = Label::new(Some(
        "The default options are recommended. You may not want to change these if you aren't sure of what you are doing. Your experience may vary by using settings other than the default."
    ));
    tip_label.set_wrap(true);
    tip_label.set_max_width_chars(40);
    tip_label.set_halign(gtk4::Align::Start);
    tip_label.set_margin_top(5);
    settings_box.append(&tip_label);

    content.append(&settings_box);

    let parent_clone = parent.clone();
    let game_path_entry_for_browse = game_path_entry.clone();
    browse_button.connect_clicked(move |_| {
        let entry_clone = game_path_entry_for_browse.clone();
        let parent_for_dialog = parent_clone.clone();
        let parent_for_error = parent_clone.clone();
        select_game_path(&parent_for_dialog, move |path| {
            if !is_valid_game_folder(&path) {
                show_error_dialog(
                    &parent_for_error,
                    "Invalid game folder",
                    "Please select the folder named \"Dead by Daylight\".",
                );
                return;
            }
            entry_clone.set_text(path.to_string_lossy().as_ref());
        });
    });

    let app_state_clone = app_state.clone();
    let parent_clone_for_save = parent.clone();
    dialog.connect_response(move |dialog, response| {
        if response == ResponseType::Ok {
            // Apply button clicked
            let mut settings = app_state_clone.settings.lock().unwrap();

            let game_path_text = game_path_entry.text().to_string();
            if !game_path_text.trim().is_empty()
                && !is_valid_game_folder(std::path::Path::new(game_path_text.trim()))
            {
                show_error_dialog(
                    &parent_clone_for_save,
                    "Invalid game folder",
                    "Please select the folder named \"Dead by Daylight\".",
                );
                return;
            }

            settings.apply_mode = match mode_combo.active() {
                Some(1) => ApplyMode::UniversalRedirect,
                _ => ApplyMode::Gatekeep,
            };

            settings.block_mode = if rb_both.is_active() {
                BlockMode::Both
            } else if rb_ping.is_active() {
                BlockMode::OnlyPing
            } else {
                BlockMode::OnlyService
            };

            settings.merge_unstable = merge_check.is_active();
            settings.game_path = game_path_text;

            let _ = settings.save();

            // Refresh the warning symbols in the list view
            refresh_warning_symbols(
                &app_state_clone.list_store,
                &app_state_clone.regions,
                settings.merge_unstable,
            );

            dialog.close();
        } else if response == ResponseType::Other(1) {
            // Revert to Default button clicked
            let mut settings = app_state_clone.settings.lock().unwrap();

            // Reset to default values
            settings.apply_mode = ApplyMode::Gatekeep;
            settings.block_mode = BlockMode::Both;
            settings.merge_unstable = true;
            settings.game_path.clear();

            let _ = settings.save();

            // Update UI controls to reflect defaults
            game_path_entry.set_text("");
            mode_combo.set_active(Some(0));
            rb_both.set_active(true);
            merge_check.set_active(true);

            // Refresh the warning symbols in the list view
            refresh_warning_symbols(
                &app_state_clone.list_store,
                &app_state_clone.regions,
                settings.merge_unstable,
            );

            // Don't close dialog - let user see the changes
        } else {
            // X button or other close action
            dialog.close();
        }
    });

    dialog.show();
}

fn get_saved_game_path(
    app_state: &Rc<AppState>,
    window: &ApplicationWindow,
) -> Option<std::path::PathBuf> {
    let settings = app_state.settings.lock().unwrap();
    let game_path = settings.game_path.trim();
    if game_path.is_empty() {
        show_info_dialog(
            window,
            "Game folder required",
            "Please set the game folder in Options → Program settings.\n\nTip: In Steam, right-click Dead by Daylight → Manage → Browse local files. The folder that opens is the one you should select.",
        );
        return None;
    }
    let path = std::path::PathBuf::from(game_path);
    if !is_valid_game_folder(&path) {
        show_error_dialog(
            window,
            "Invalid game folder",
            "Please select the folder named 'Dead by Daylight'.",
        );
        return None;
    }
    Some(path)
}

fn is_valid_game_folder(path: &std::path::Path) -> bool {
    path.file_name()
        .and_then(|name| name.to_str())
        .map(|name| name == "Dead by Daylight")
        .unwrap_or(false)
}

fn show_info_dialog(parent: &ApplicationWindow, title: &str, message: &str) {
    let dialog = MessageDialog::new(
        Some(parent),
        gtk4::DialogFlags::MODAL,
        MessageType::Info,
        ButtonsType::Ok,
        title,
    );
    dialog.set_secondary_text(Some(message));
    dialog.run_async(|dialog, _| dialog.close());
}

fn show_error_dialog(parent: &ApplicationWindow, title: &str, message: &str) {
    let dialog = MessageDialog::new(
        Some(parent),
        gtk4::DialogFlags::MODAL,
        MessageType::Error,
        ButtonsType::Ok,
        title,
    );
    dialog.set_secondary_text(Some(message));
    dialog.run_async(|dialog, _| dialog.close());
}

fn start_ping_timer(app_state: Rc<AppState>) {
    glib::timeout_add_seconds_local(5, move || {
        let regions = app_state.regions.clone();
        let regions_for_ping = regions.clone();
        let blocked_regions = app_state.blocked_regions.clone();
        let blocked_hosts = app_state.hosts_manager.get_blocked_hostnames();
        let runtime = app_state.tokio_runtime.clone();
        let list_store = app_state.list_store.clone();

        // Spawn work on tokio runtime in background thread
        glib::spawn_future_local(async move {
            let latency_results = runtime
                .spawn(async move {
                    let mut results = HashMap::new();

                    // Perform all pings
                    for (region_name, region_info) in regions_for_ping.iter() {
                        if let Some(host) = region_info.hosts.first() {
                            let latency = ping::ping_host(host).await;
                            results.insert(region_name.clone(), latency);
                        }
                    }

                    results
                })
                .await
                .unwrap();

            // Update the UI on the main thread
            if let Some(iter) = list_store.iter_first() {
                loop {
                    let is_divider = list_store.get::<bool>(&iter, 4);

                    // Skip dividers
                    if !is_divider {
                        let name = list_store.get::<String>(&iter, 0);
                        let clean_name = name.replace(" ⚠︎", "");

                        if is_region_blocked_by_hosts(&clean_name, &regions, &blocked_regions, &blocked_hosts) {
                            list_store.set(&iter, &[(1, &"disconnected".to_string()), (5, &"gray".to_string())]);
                        } else if let Some(&latency) = latency_results.get(&clean_name) {
                            let latency_text = if latency >= 0 {
                                format!("{} ms", latency)
                            } else {
                                "disconnected".to_string()
                            };
                            let color = get_color_for_latency(latency);
                            list_store.set(&iter, &[(1, &latency_text), (5, &color.to_string())]);
                        }
                    }

                    if !list_store.iter_next(&iter) {
                        break;
                    }
                }
            }
        });

        glib::ControlFlow::Continue
    });
}

fn is_region_blocked_by_hosts(
    region_key: &str,
    regions: &HashMap<String, RegionInfo>,
    blocked_regions: &HashMap<String, RegionInfo>,
    blocked_hosts: &HashSet<String>,
) -> bool {
    if blocked_hosts.is_empty() {
        return false;
    }

    if let Some(info) = regions.get(region_key) {
        return info.hosts.iter().any(|h| blocked_hosts.contains(&h.to_lowercase()));
    }

    if let Some(info) = blocked_regions.get(region_key) {
        return info.hosts.iter().any(|h| blocked_hosts.contains(&h.to_lowercase()));
    }

    false
}

fn format_update_tooltip(last_update: DateTime<Local>) -> String {
    let seconds = (Local::now() - last_update).num_seconds().max(0);
    let time = last_update.format("%-I:%M%p").to_string();
    let time = time.replace("AM", "ᴀᴍ").replace("PM", "ᴘᴍ");
    format!(
        "Most recent connection: {}s ago, at {}\n\nThis is the region that Dead by Daylight chose\nwhen connecting you to their game.",
        seconds, time
    )
}
