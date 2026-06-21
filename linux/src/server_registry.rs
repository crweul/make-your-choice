// Remembers real DBD game-server endpoints per AWS region (Linux port of ServerRegistry.cs).
// Fed by the traffic sniffer; persisted across launches; ships with a shared address book so the
// beacon can probe known servers without the user connecting first. Thread-safe so the sniffer's
// async tasks and the beacon timer can both use it.

use std::collections::HashMap;
use std::fs;
use std::path::PathBuf;
use std::sync::Mutex;

// Shared address book compiled into the binary (curated from real connections).
const SEED: &str = include_str!("../servers-seed.txt");
const MAX_PER_REGION: usize = 256;

#[derive(Clone)]
pub struct Entry {
    pub ip: String,
    pub port: u16,
    pub last_seen: u64,
}

pub struct ServerRegistry {
    // region -> ("ip:port" -> entry)
    by_region: Mutex<HashMap<String, HashMap<String, Entry>>>,
    path: PathBuf,
}

fn now_unix() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

impl ServerRegistry {
    pub fn new() -> Self {
        let dir = dirs::config_dir()
            .unwrap_or_else(|| PathBuf::from("."))
            .join("make-your-choice");
        let _ = fs::create_dir_all(&dir);
        let reg = ServerRegistry {
            by_region: Mutex::new(HashMap::new()),
            path: dir.join("known-servers.txt"),
        };
        reg.merge_text(SEED); // shipped seed
        if let Ok(text) = fs::read_to_string(&reg.path) {
            reg.merge_text(&text); // user's learned servers
        }
        reg
    }

    fn merge_text(&self, text: &str) {
        let mut map = self.by_region.lock().unwrap();
        for raw in text.lines() {
            let line = raw.trim();
            if line.is_empty() || line.starts_with('#') {
                continue;
            }
            let parts: Vec<&str> = line.split('|').collect();
            if parts.len() < 4 {
                continue;
            }
            let region = parts[0].trim();
            let ip = parts[1].trim();
            let port: u16 = match parts[2].trim().parse() {
                Ok(p) => p,
                Err(_) => continue,
            };
            let seen: u64 = parts[3].trim().parse().unwrap_or(0);
            if region.is_empty() || ip.is_empty() {
                continue;
            }
            let entry = Entry { ip: ip.to_string(), port, last_seen: seen };
            let inner = map.entry(region.to_string()).or_default();
            let key = format!("{}:{}", ip, port);
            match inner.get(&key) {
                Some(e) if e.last_seen >= seen => {}
                _ => {
                    inner.insert(key, entry);
                }
            }
        }
    }

    pub fn record(&self, region_code: &str, ip: &str, port: u16) {
        if region_code.is_empty() || ip.is_empty() {
            return;
        }
        {
            let mut map = self.by_region.lock().unwrap();
            let inner = map.entry(region_code.to_string()).or_default();
            inner.insert(
                format!("{}:{}", ip, port),
                Entry { ip: ip.to_string(), port, last_seen: now_unix() },
            );
            if inner.len() > MAX_PER_REGION {
                let mut items: Vec<(String, u64)> =
                    inner.iter().map(|(k, e)| (k.clone(), e.last_seen)).collect();
                items.sort_by_key(|(_, t)| *t);
                for (k, _) in items.into_iter().take(inner.len() - MAX_PER_REGION) {
                    inner.remove(&k);
                }
            }
        }
        self.save();
    }

    /// Most-recently-seen endpoints for a region (newest first).
    pub fn candidates(&self, region_code: &str, max: usize) -> Vec<(String, u16)> {
        let map = self.by_region.lock().unwrap();
        match map.get(region_code) {
            None => Vec::new(),
            Some(inner) => {
                let mut v: Vec<&Entry> = inner.values().collect();
                v.sort_by(|a, b| b.last_seen.cmp(&a.last_seen));
                v.into_iter().take(max).map(|e| (e.ip.clone(), e.port)).collect()
            }
        }
    }

    pub fn count_for(&self, region_code: &str) -> usize {
        self.by_region.lock().unwrap().get(region_code).map(|m| m.len()).unwrap_or(0)
    }

    pub fn total(&self) -> usize {
        self.by_region.lock().unwrap().values().map(|m| m.len()).sum()
    }

    fn save(&self) {
        let map = self.by_region.lock().unwrap();
        let mut out = String::from("# DBD known servers — regionCode|ip|port|lastSeenUnix\n");
        for (region, inner) in map.iter() {
            for e in inner.values() {
                out.push_str(&format!("{}|{}|{}|{}\n", region, e.ip, e.port, e.last_seen));
            }
        }
        let _ = fs::write(&self.path, out);
    }
}
