use crate::region::{ApplyMode, BlockMode};
use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UserSettings {
    pub apply_mode: ApplyMode,
    pub block_mode: BlockMode,
    pub merge_unstable: bool,
    pub last_launched_version: String,
    pub game_path: String,
    pub auto_update_check_paused_until: Option<String>,
    // Hard region lock: firewall-block the game-server data plane of unchosen regions on apply.
    #[serde(default)]
    pub use_hard_lock: bool,
    // Minimize (close button) hides to the system tray instead of quitting.
    #[serde(default = "default_true")]
    pub minimize_to_tray: bool,
    // Notify when the preferred server transitions offline -> online.
    #[serde(default)]
    pub notify_server_online: bool,
    // Last session's ticked regions, restored on launch.
    #[serde(default)]
    pub selected_regions: Vec<String>,
    // Start automatically at login (writes an XDG autostart .desktop entry).
    #[serde(default)]
    pub auto_start: bool,
    // How often (seconds) the GameLift beacon probe and the Dead by Queue poll run.
    #[serde(default = "default_poll_interval")]
    pub poll_interval_seconds: u64,
}

fn default_true() -> bool {
    true
}

fn default_poll_interval() -> u64 {
    60
}

impl Default for UserSettings {
    fn default() -> Self {
        Self {
            apply_mode: ApplyMode::Gatekeep,
            block_mode: BlockMode::Both,
            merge_unstable: true,
            last_launched_version: String::new(),
            game_path: String::new(),
            auto_update_check_paused_until: None,
            use_hard_lock: false,
            minimize_to_tray: true,
            notify_server_online: false,
            selected_regions: Vec::new(),
            auto_start: false,
            poll_interval_seconds: 60,
        }
    }
}

impl UserSettings {
    pub fn config_dir() -> PathBuf {
        dirs::config_dir()
            .unwrap_or_else(|| PathBuf::from("."))
            .join("make-your-choice")
    }

    pub fn config_file() -> PathBuf {
        Self::config_dir().join("config.yaml")
    }

    pub fn load() -> Result<Self> {
        let path = Self::config_file();
        if !path.exists() {
            return Ok(Self::default());
        }

        let content = fs::read_to_string(&path)
            .with_context(|| format!("Failed to read settings from {:?}", path))?;

        let settings: UserSettings = serde_yaml::from_str(&content)
            .with_context(|| "Failed to parse settings YAML")?;

        Ok(settings)
    }

    pub fn save(&self) -> Result<()> {
        let dir = Self::config_dir();
        if !dir.exists() {
            fs::create_dir_all(&dir)
                .with_context(|| format!("Failed to create config directory {:?}", dir))?;
        }

        let path = Self::config_file();
        let yaml = serde_yaml::to_string(self)
            .with_context(|| "Failed to serialize settings to YAML")?;

        fs::write(&path, yaml)
            .with_context(|| format!("Failed to write settings to {:?}", path))?;

        Ok(())
    }
}
