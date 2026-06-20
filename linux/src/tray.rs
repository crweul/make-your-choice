// System tray via ksni (StatusNotifierItem) — works natively on KDE Plasma and on GNOME with the
// AppIndicator extension. Shows the preferred server's online status as a themed status icon, with
// the server status + queue time in the tooltip. Menu actions are sent back to the GTK main thread
// over an std mpsc channel (the tray runs on its own thread, so it must not touch GTK directly).
use std::sync::mpsc::{Receiver, Sender};
use std::sync::{Arc, Mutex};

#[derive(Debug, Clone, Copy)]
pub enum TrayMsg {
    Show,
    Exit,
}

pub struct TrayState {
    pub online: Option<bool>, // None = unknown
    pub tooltip: String,
}

struct MycTray {
    state: Arc<Mutex<TrayState>>,
    tx: Sender<TrayMsg>,
}

impl ksni::Tray for MycTray {
    fn id(&self) -> String {
        "make-your-choice".to_string()
    }

    fn title(&self) -> String {
        "Make Your Choice".to_string()
    }

    fn icon_name(&self) -> String {
        match self.state.lock().unwrap().online {
            Some(true) => "network-transmit-receive".to_string(),
            Some(false) => "network-offline".to_string(),
            None => "network-idle".to_string(),
        }
    }

    fn tool_tip(&self) -> ksni::ToolTip {
        ksni::ToolTip {
            title: "Make Your Choice".to_string(),
            description: self.state.lock().unwrap().tooltip.clone(),
            icon_name: String::new(),
            icon_pixmap: Vec::new(),
        }
    }

    fn menu(&self) -> Vec<ksni::MenuItem<Self>> {
        use ksni::menu::{MenuItem, StandardItem};
        vec![
            StandardItem {
                label: "Show Make Your Choice".to_string(),
                activate: Box::new(|t: &mut Self| {
                    let _ = t.tx.send(TrayMsg::Show);
                }),
                ..Default::default()
            }
            .into(),
            MenuItem::Separator,
            StandardItem {
                label: "Exit".to_string(),
                activate: Box::new(|t: &mut Self| {
                    let _ = t.tx.send(TrayMsg::Exit);
                }),
                ..Default::default()
            }
            .into(),
        ]
    }
}

pub struct TrayController {
    state: Arc<Mutex<TrayState>>,
    handle: ksni::Handle<MycTray>,
}

impl TrayController {
    /// Update the icon/tooltip. Safe to call from the GTK main thread.
    pub fn update(&self, online: Option<bool>, tooltip: String) {
        {
            let mut s = self.state.lock().unwrap();
            s.online = online;
            s.tooltip = tooltip;
        }
        // Trigger a refresh; the trait methods re-read `state`.
        self.handle.update(|_t: &mut MycTray| {});
    }
}

/// Start the tray. Returns the controller and a receiver for menu actions (poll it on the GTK
/// main loop). Returns None if the tray service could not be spawned.
pub fn start_tray() -> Option<(TrayController, Receiver<TrayMsg>)> {
    let (tx, rx) = std::sync::mpsc::channel();
    let state = Arc::new(Mutex::new(TrayState {
        online: None,
        tooltip: "Starting…".to_string(),
    }));
    let tray = MycTray {
        state: state.clone(),
        tx,
    };
    let service = ksni::TrayService::new(tray);
    let handle = service.handle();
    service.spawn();
    Some((TrayController { state, handle }, rx))
}
