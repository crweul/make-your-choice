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
    pub last_live: u64, // last time a probe confirmed this endpoint live
}

impl Entry {
    fn recency(&self) -> u64 {
        self.last_seen.max(self.last_live)
    }
}

// One game-server instance (IP) with its best representative port — the beacon dedups to this.
#[derive(Clone)]
pub struct Instance {
    pub ip: String,
    pub port: u16,
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
            let live: u64 = parts.get(4).and_then(|s| s.trim().parse().ok()).unwrap_or(0);
            if region.is_empty() || ip.is_empty() {
                continue;
            }
            let entry = Entry { ip: ip.to_string(), port, last_seen: seen, last_live: live };
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
            let key = format!("{}:{}", ip, port);
            match inner.get_mut(&key) {
                Some(e) => e.last_seen = now_unix(), // preserve last_live
                None => {
                    inner.insert(key, Entry { ip: ip.to_string(), port, last_seen: now_unix(), last_live: 0 });
                }
            }
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

    /// Mark an endpoint confirmed-live (a probe got a DBD challenge). Powers reliability ranking and
    /// self-pruning. Not persisted on every call — call flush() after a batch.
    pub fn mark_live(&self, region_code: &str, ip: &str, port: u16) {
        if region_code.is_empty() || ip.is_empty() {
            return;
        }
        let mut map = self.by_region.lock().unwrap();
        let inner = map.entry(region_code.to_string()).or_default();
        let key = format!("{}:{}", ip, port);
        let now = now_unix();
        match inner.get_mut(&key) {
            Some(e) => e.last_live = now,
            None => {
                inner.insert(key, Entry { ip: ip.to_string(), port, last_seen: now, last_live: now });
            }
        }
    }

    /// Distinct instances (one per IP) for a region, newest-first by recency, each IP's best
    /// (most recent) port as representative — the beacon probes one port per IP.
    pub fn instances_ranked(&self, region_code: &str, max: usize) -> Vec<Instance> {
        let map = self.by_region.lock().unwrap();
        let inner = match map.get(region_code) {
            Some(m) => m,
            None => return Vec::new(),
        };
        let mut by_ip: HashMap<&str, &Entry> = HashMap::new();
        for e in inner.values() {
            match by_ip.get(e.ip.as_str()) {
                Some(best) if best.recency() >= e.recency() => {}
                _ => {
                    by_ip.insert(e.ip.as_str(), e);
                }
            }
        }
        let mut v: Vec<&Entry> = by_ip.values().copied().collect();
        v.sort_by(|a, b| b.recency().cmp(&a.recency()));
        v.into_iter().take(max).map(|e| Instance { ip: e.ip.clone(), port: e.port }).collect()
    }

    /// Drop endpoints not seen/live within max_age_days that were never probed live — keeps the pool
    /// lean so the beacon probes fewer, higher-quality targets.
    pub fn prune(&self, max_age_days: u64) {
        let cutoff = now_unix().saturating_sub(max_age_days * 86400);
        let mut changed = false;
        {
            let mut map = self.by_region.lock().unwrap();
            for inner in map.values_mut() {
                let dead: Vec<String> = inner
                    .iter()
                    .filter(|(_, e)| e.recency() < cutoff && e.last_live == 0)
                    .map(|(k, _)| k.clone())
                    .collect();
                for k in dead {
                    inner.remove(&k);
                    changed = true;
                }
            }
        }
        if changed {
            self.save();
        }
    }

    /// Persist current state (call after a batch of mark_live updates).
    pub fn flush(&self) {
        self.save();
    }

    pub fn count_for(&self, region_code: &str) -> usize {
        self.by_region.lock().unwrap().get(region_code).map(|m| m.len()).unwrap_or(0)
    }

    pub fn total(&self) -> usize {
        self.by_region.lock().unwrap().values().map(|m| m.len()).sum()
    }

    fn save(&self) {
        let map = self.by_region.lock().unwrap();
        let mut out = String::from("# DBD known servers — regionCode|ip|port|lastSeenUnix|lastLiveUnix\n");
        for (region, inner) in map.iter() {
            for e in inner.values() {
                out.push_str(&format!("{}|{}|{}|{}|{}\n", region, e.ip, e.port, e.last_seen, e.last_live));
            }
        }
        let _ = fs::write(&self.path, out);
    }
}
