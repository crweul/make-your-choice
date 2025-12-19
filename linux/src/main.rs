mod hosts;
mod ping;
mod region;
mod settings;
mod update;

use gio::{Menu, SimpleAction};
use glib::Type;
use gtk4::prelude::*;
use gtk4::{
    gio, glib, pango, Application, ApplicationWindow, Box as GtkBox, Button, ButtonsType,
    CellRendererText, CheckButton, ComboBoxText, Dialog, Label, ListStore, MenuButton,
    MessageDialog, MessageType, Orientation, PolicyType, ResponseType, ScrolledWindow,
    SelectionMode, Separator, TreeView, TreeViewColumn,
};
use std::cell::RefCell;
use std::collections::{HashMap, HashSet};
use std::rc::Rc;
use std::sync::{Arc, Mutex};
use tokio::runtime::Runtime;

use hosts::HostsManager;
use region::*;
use settings::UserSettings;
use update::UpdateChecker;

const APP_ID: &str = "dev.lawliet.makeyourchoice";

#[derive(Clone)]
struct AppConfig {
    repo_url: String,
    current_version: String,
    developer: String,
    repo: String,
    update_message: String,
    discord_url: String,
}

struct AppState {
    config: AppConfig,
    regions: HashMap<String, RegionInfo>,
    settings: Arc<Mutex<UserSettings>>,
    hosts_manager: HostsManager,
    update_checker: UpdateChecker,
    selected_regions: RefCell<HashSet<String>>,
    list_store: ListStore,
    tokio_runtime: Arc<Runtime>,
}

fn main() -> glib::ExitCode {
    let app = Application::builder().application_id(APP_ID).build();
    app.connect_activate(build_ui);
    app.run()
}

fn build_ui(app: &Application) {
    // Create tokio runtime for async operations
    let tokio_runtime = Arc::new(Runtime::new().expect("Failed to create tokio runtime"));

    // Load configuration
    let config = AppConfig {
        repo_url: "https://github.com/laewliet/make-your-choice".to_string(),
        current_version: "2.0.0-RC".to_string(), // Must match git tag for updates, and Cargo.toml version
        developer: "laewliet".to_string(), // GitHub username, DO NOT CHANGE, as changing this breaks the license compliance
        repo: "make-your-choice".to_string(), // Repository name
        update_message: "Welcome back! Here are the new features and changes in this version:\n\n- Introduced Linux / Steam Deck version.\nThank you for your support!".to_string(),
        discord_url: "https://discord.gg/gnvtATeVc4".to_string(),
    };

    let regions = get_regions();
    let settings = Arc::new(Mutex::new(UserSettings::load().unwrap_or_default()));
    let hosts_manager = HostsManager::new(config.discord_url.clone());
    let update_checker = UpdateChecker::new(
        config.developer.clone(),
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
            let _ = settings_lock.save();
        }
    }

    // Create ListStore for the list view (region name, latency, stable, checked, is_divider)
    let list_store = ListStore::new(&[
        Type::STRING,
        Type::STRING,
        Type::BOOL,
        Type::BOOL,
        Type::BOOL,
    ]);

    // Group regions by category
    let mut groups: HashMap<&str, Vec<(&String, &RegionInfo)>> = HashMap::new();
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
                ],
            );

            // Add regions in this group
            for (region_name, region_info) in group_regions {
                let display_name = if !region_info.stable {
                    format!("{} ⚠︎", region_name)
                } else {
                    (*region_name).clone()
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
    tree_view.append_column(&col_latency);

    // Create scrolled window for tree view
    let scrolled = ScrolledWindow::new();
    scrolled.set_policy(PolicyType::Automatic, PolicyType::Automatic);
    scrolled.set_child(Some(&tree_view));
    scrolled.set_vexpand(true);

    // Create app state
    let app_state = Rc::new(AppState {
        config: config.clone(),
        regions: regions.clone(),
        settings: settings.clone(),
        hosts_manager,
        update_checker,
        selected_regions: RefCell::new(HashSet::new()),
        list_store: list_store.clone(),
        tokio_runtime,
    });

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

    // Create window
    let window = ApplicationWindow::builder()
        .application(app)
        .title("Make Your Choice (DbD Server Selector)")
        .default_width(405)
        .default_height(585)
        .build();

    // Set window icon (embedded in binary from ICO file)
    // GTK4 Note: Window icons should be set via icon theme
    // Convert the embedded ICO to PNG and install it to the icon theme directory
    const ICON_DATA: &[u8] = include_bytes!("../icon.ico");
    const ICON_NAME: &str = "make-your-choice";

    // Install icon to runtime icon directory
    if let Some(data_dir) = glib::user_data_dir().to_str() {
        let icon_dir = std::path::PathBuf::from(data_dir).join("icons/hicolor/256x256/apps");

        if let Ok(_) = std::fs::create_dir_all(&icon_dir) {
            let icon_path = icon_dir.join(format!("{}.png", ICON_NAME));

            // Convert ICO to PNG and save (only if needed)
            if !icon_path.exists() {
                // Load ICO using gdk-pixbuf and save as PNG
                let loader = gtk4::gdk_pixbuf::PixbufLoader::new();
                if loader.write(ICON_DATA).is_ok() && loader.close().is_ok() {
                    if let Some(pixbuf) = loader.pixbuf() {
                        // Save as PNG
                        let _ = pixbuf.savev(&icon_path, "png", &[]);
                    }
                }
            }
        }
    }

    // Set the icon name on the window
    window.set_icon_name(Some(ICON_NAME));

    // Create menu bar
    let menu_box = GtkBox::new(Orientation::Horizontal, 5);
    menu_box.set_margin_start(5);
    menu_box.set_margin_end(5);
    menu_box.set_margin_top(5);
    menu_box.set_margin_bottom(5);

    // Version menu button
    let version_menu = create_version_menu(&window, &app_state);
    let version_btn = MenuButton::builder()
        .label(&format!("v{}", config.current_version))
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

    // Main layout
    let main_box = GtkBox::new(Orientation::Vertical, 0);
    main_box.append(&menu_box);
    main_box.append(&Separator::new(Orientation::Horizontal));
    main_box.append(&tip_label);
    main_box.append(&scrolled);
    main_box.append(&button_box);

    window.set_child(Some(&main_box));

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

    window.present();
}

fn create_version_menu(_window: &ApplicationWindow, _app_state: &Rc<AppState>) -> Menu {
    let menu = Menu::new();
    menu.append(Some("Check for updates"), Some("app.check-updates"));
    menu.append(Some("Repository"), Some("app.repository"));
    menu.append(Some("About"), Some("app.about"));
    menu.append(Some("Open hosts file location"), Some("app.open-hosts"));
    menu.append(Some("Reset hosts file"), Some("app.reset-hosts"));
    menu
}

fn create_options_menu() -> Menu {
    let menu = Menu::new();
    menu.append(Some("Program settings"), Some("app.settings"));
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
    action.connect_activate(move |_, _| {
        open_url(&repo_url);
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

    // Open hosts location action (disabled for future implementation)
    let action = SimpleAction::new("open-hosts", None);
    action.set_enabled(false);
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
    let _discord_url = app_state.config.discord_url.clone(); // TODO: Implement opening Discord link
    action.set_enabled(false);
    app.add_action(&action);
}

fn open_url(url: &str) {
    // Use the `open` crate for cross-platform URL opening
    let _ = open::that(url);
}

fn check_for_updates_action(app_state: &Rc<AppState>, window: &ApplicationWindow) {
    let window = window.clone();
    let update_checker = app_state.update_checker.clone();
    let current_version = app_state.config.current_version.clone();
    let runtime = app_state.tokio_runtime.clone();
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
                    ButtonsType::YesNo,
                    "Update Available",
                );
                dialog.set_secondary_text(Some(&format!(
                    "A new version is available: {}.\nWould you like to update?\n\nYour version: {}",
                    new_version, current_version
                )));

                dialog.run_async(move |dialog, response| {
                    if response == ResponseType::Yes {
                        open_url(&releases_url);
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

fn show_about_dialog(app_state: &Rc<AppState>, window: &ApplicationWindow) {
    let dialog = Dialog::with_buttons(
        Some("About Make Your Choice"),
        Some(window),
        gtk4::DialogFlags::MODAL,
        &[("Awesome!", ResponseType::Ok)],
    );
    dialog.set_default_width(480);

    let content = dialog.content_area();
    let vbox = GtkBox::new(Orientation::Vertical, 10);
    vbox.set_margin_start(10);
    vbox.set_margin_end(10);
    vbox.set_margin_top(10);
    vbox.set_margin_bottom(10);

    let title = Label::new(Some("Make Your Choice (DbD Server Selector)"));
    title.add_css_class("title-2");

    let developer = Label::new(Some(&format!("Developer: {}", app_state.config.developer)));
    developer.set_halign(gtk4::Align::Start);

    let version = Label::new(Some(&format!(
        "Version {}\nLinux (GTK4)",
        app_state.config.current_version
    )));
    version.set_halign(gtk4::Align::Start);

    vbox.append(&title);
    vbox.append(&developer);
    vbox.append(&version);
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

fn handle_apply_click(app_state: &Rc<AppState>, window: &ApplicationWindow) {
    let selected = app_state.selected_regions.borrow().clone();
    let settings = app_state.settings.lock().unwrap();

    let result = match settings.apply_mode {
        ApplyMode::Gatekeep => app_state.hosts_manager.apply_gatekeep(
            &app_state.regions,
            &selected,
            settings.block_mode,
            settings.merge_unstable,
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
                .apply_universal_redirect(&app_state.regions, region)
        }
    };

    match result {
        Ok(_) => {
            show_info_dialog(
                window,
                "Success",
                &format!(
                    "The hosts file was updated successfully ({:?} mode).\n\nPlease restart the game for changes to take effect.",
                    settings.apply_mode
                ),
            );
        }
        Err(e) => {
            show_error_dialog(window, "Error", &e.to_string());
        }
    }
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
            ("Apply Changes", ResponseType::Ok),
            ("Cancel", ResponseType::Cancel),
        ],
    );
    dialog.set_default_width(350);

    let content = dialog.content_area();
    let settings_box = GtkBox::new(Orientation::Vertical, 10);
    settings_box.set_margin_start(10);
    settings_box.set_margin_end(10);
    settings_box.set_margin_top(10);
    settings_box.set_margin_bottom(10);

    // Apply mode
    let mode_label = Label::new(Some("Method:"));
    mode_label.set_halign(gtk4::Align::Start);
    let mode_combo = ComboBoxText::new();
    mode_combo.append_text("Gatekeep (default)");
    mode_combo.append_text("Universal Redirect");

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

    drop(settings);

    settings_box.append(&mode_label);
    settings_box.append(&mode_combo);
    settings_box.append(&Separator::new(Orientation::Horizontal));
    settings_box.append(&block_label);
    settings_box.append(&rb_both);
    settings_box.append(&rb_ping);
    settings_box.append(&rb_service);
    settings_box.append(&Separator::new(Orientation::Horizontal));
    settings_box.append(&merge_check);

    content.append(&settings_box);

    let app_state_clone = app_state.clone();
    dialog.connect_response(move |dialog, response| {
        if response == ResponseType::Ok {
            let mut settings = app_state_clone.settings.lock().unwrap();

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

            let _ = settings.save();
        }
        dialog.close();
    });

    dialog.show();
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
        let runtime = app_state.tokio_runtime.clone();
        let list_store = app_state.list_store.clone();

        // Spawn work on tokio runtime in background thread
        glib::spawn_future_local(async move {
            let latency_results = runtime
                .spawn(async move {
                    let mut results = HashMap::new();

                    // Perform all pings
                    for (region_name, region_info) in regions.iter() {
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

                        if let Some(&latency) = latency_results.get(&clean_name) {
                            let latency_text = if latency >= 0 {
                                format!("{} ms", latency)
                            } else {
                                "disconnected".to_string()
                            };
                            list_store.set(&iter, &[(1, &latency_text)]);
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
