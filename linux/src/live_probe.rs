// Active liveness probing of real DBD game servers — the Linux port of LiveProbe.cs.
//
// DBD's dedicated servers run on Unreal Engine and answer a connectionless "stateless connect
// handshake" with a Challenge before any auth. Replaying a captured InitialConnect therefore tells
// us, in real time, whether a region's fleet is up: a reply carrying the build magic (c9 1e f8 11)
// is zero-false-positive proof a live DBD server is there. A connected UDP socket also surfaces
// ICMP port-unreachable (ECONNREFUSED) so we can tell "host up, no process" from "nothing there".

use std::collections::HashMap;
use std::io::ErrorKind;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::Arc;
use std::sync::Mutex;
use std::sync::OnceLock;
use std::time::{Duration, Instant};
use tokio::net::UdpSocket;
use tokio::sync::Semaphore;
use tokio::time::{sleep, timeout};

// A captured UE InitialConnect packet (Ohio capture, frame 415). Replayed verbatim; the stale
// trailing client nonce does not stop the server issuing a fresh challenge.
const UE_HANDSHAKE_HEX: &str =
    "b801028000c91ef81100000000000000000000000000000000000000000000000000000000000001e05886665b064cc46901";
// Bytes 5..9 of any handshake packet — the DBD build magic.
const MAGIC: [u8; 4] = [0xc9, 0x1e, 0xf8, 0x11];

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Outcome {
    Replied,         // got data back
    PortUnreachable, // ICMP port unreachable -> host up, no process on that port
    NoResponse,      // timeout
    Error,
}

#[derive(Debug, Clone)]
pub struct ProbeReport {
    pub ip: String,
    pub port: u16,
    pub outcome: Outcome,
    pub magic: bool, // reply carried the UE handshake magic -> confirmed DBD server
}

impl ProbeReport {
    pub fn is_live_server(&self) -> bool {
        self.outcome == Outcome::Replied && self.magic
    }
}

#[derive(Debug, Default, Clone)]
pub struct SweepSummary {
    pub total: usize,
    pub replied: usize,
    pub port_unreach: usize,
    pub timeout: usize,
    pub errored: usize,
    pub any_live: bool,
    pub first_live: Option<(String, u16)>,
}

fn bootstrap_handshake() -> Vec<u8> {
    (0..UE_HANDSHAKE_HEX.len() / 2)
        .map(|i| u8::from_str_radix(&UE_HANDSHAKE_HEX[i * 2..i * 2 + 2], 16).unwrap_or(0))
        .collect()
}

// The handshake learned live from the game's own traffic (survives DBD patches). None until seen.
fn learned_handshake() -> &'static Mutex<Option<Vec<u8>>> {
    static HS: OnceLock<Mutex<Option<Vec<u8>>>> = OnceLock::new();
    HS.get_or_init(|| Mutex::new(load_learned()))
}

fn handshake_path() -> std::path::PathBuf {
    dirs::config_dir()
        .unwrap_or_else(|| std::path::PathBuf::from("."))
        .join("make-your-choice")
        .join("handshake.hex")
}

fn load_learned() -> Option<Vec<u8>> {
    let hex = std::fs::read_to_string(handshake_path()).ok()?;
    let hex = hex.trim();
    if hex.len() < 18 || hex.len() % 2 != 0 {
        return None;
    }
    (0..hex.len() / 2)
        .map(|i| u8::from_str_radix(&hex[i * 2..i * 2 + 2], 16).ok())
        .collect()
}

// The handshake we currently probe with: learned-from-live if we have one, else bootstrap.
fn active_handshake() -> Vec<u8> {
    if let Ok(g) = learned_handshake().lock() {
        if let Some(ref hs) = *g {
            return hs.clone();
        }
    }
    bootstrap_handshake()
}

// Current build magic = bytes 5..8 of the active handshake; tracks the live handshake across updates.
fn active_magic() -> [u8; 4] {
    let hs = active_handshake();
    if hs.len() >= 9 {
        [hs[5], hs[6], hs[7], hs[8]]
    } else {
        MAGIC
    }
}

/// Build magic as hex (for the diagnostic log) — a stale bootstrap magic after a DBD netcode patch
/// is the likely cause of probes that all time out even when the region is up.
pub fn active_magic_hex() -> String {
    active_magic().iter().map(|b| format!("{:02x}", b)).collect()
}

/// True if probing with a handshake learned from live game traffic (vs the shipped bootstrap).
pub fn using_learned_handshake() -> bool {
    learned_handshake().lock().map(|g| g.is_some()).unwrap_or(false)
}

/// Adopt a UE InitialConnect handshake captured live from the game's own traffic as the probe
/// template. If the magic differs from what we had, that's a netcode patch — log it and auto-update
/// (no manual recapture needed). Persisted so it survives restarts.
pub fn set_learned_handshake(payload: &[u8]) {
    if payload.len() < 18 || payload[0] != 0xB8 {
        return;
    }
    if let Ok(mut g) = learned_handshake().lock() {
        let current = g.clone().unwrap_or_else(bootstrap_handshake);
        let same_magic = current.len() >= 9
            && current[5] == payload[5]
            && current[6] == payload[6]
            && current[7] == payload[7]
            && current[8] == payload[8];
        if g.is_some() && same_magic {
            return; // already current -> no churn
        }
        *g = Some(payload.to_vec());
        let p = handshake_path();
        if let Some(dir) = p.parent() {
            let _ = std::fs::create_dir_all(dir);
        }
        let hex: String = payload.iter().map(|b| format!("{:02x}", b)).collect();
        let _ = std::fs::write(&p, &hex);
        let old_m: String = current.iter().skip(5).take(4).map(|b| format!("{:02x}", b)).collect();
        let new_m: String = payload.iter().skip(5).take(4).map(|b| format!("{:02x}", b)).collect();
        if same_magic {
            eprintln!("[beacon] handshake learned from live traffic (magic {})", new_m);
        } else {
            eprintln!(
                "[beacon] handshake magic changed {} -> {} — DBD likely patched; probe template auto-updated",
                old_m, new_m
            );
        }
    }
}

// Local UDP ports used by our own probe sockets (with an expiry), so the traffic sniffer can ignore
// the beacon's own packets and never show a phantom "Connected to" or self-feed the pool.
fn beacon_ports() -> &'static Mutex<HashMap<u16, Instant>> {
    static PORTS: OnceLock<Mutex<HashMap<u16, Instant>>> = OnceLock::new();
    PORTS.get_or_init(|| Mutex::new(HashMap::new()))
}

fn mark_beacon_port(port: u16) {
    if port == 0 {
        return;
    }
    if let Ok(mut m) = beacon_ports().lock() {
        m.insert(port, Instant::now() + Duration::from_secs(8));
    }
}

pub fn is_beacon_local_port(port: u16) -> bool {
    if let Ok(mut m) = beacon_ports().lock() {
        if let Some(&exp) = m.get(&port) {
            if exp > Instant::now() {
                return true;
            }
            m.remove(&port);
        }
    }
    false
}

/// Send the UE handshake to ip:port on a connected UDP socket and classify the result.
pub async fn probe_handshake(ip: &str, port: u16, timeout_ms: u64) -> ProbeReport {
    let hs = active_handshake();
    let magic = active_magic();
    let mut report = ProbeReport { ip: ip.to_string(), port, outcome: Outcome::Error, magic: false };

    let sock = match UdpSocket::bind("0.0.0.0:0").await {
        Ok(s) => s,
        Err(_) => return report,
    };
    if let Ok(local) = sock.local_addr() {
        mark_beacon_port(local.port());
    }
    if sock.connect((ip, port)).await.is_err() {
        return report;
    }
    if sock.send(&hs).await.is_err() {
        return report;
    }

    let mut buf = [0u8; 2048];
    match timeout(Duration::from_millis(timeout_ms), sock.recv(&mut buf)).await {
        Ok(Ok(n)) => {
            report.outcome = Outcome::Replied;
            report.magic = n >= 9 && buf[5..9] == magic;
        }
        Ok(Err(e)) if e.kind() == ErrorKind::ConnectionRefused => {
            report.outcome = Outcome::PortUnreachable;
        }
        Ok(Err(_)) => report.outcome = Outcome::Error,
        Err(_) => report.outcome = Outcome::NoResponse, // timed out
    }
    report
}

/// Probe many endpoints with bounded concurrency; returns a summary (any confirmed DBD server?).
pub async fn probe_batch(targets: Vec<(String, u16)>, timeout_ms: u64, concurrency: usize) -> SweepSummary {
    let mut summary = SweepSummary { total: targets.len(), ..Default::default() };
    if targets.is_empty() {
        return summary;
    }
    let sem = std::sync::Arc::new(Semaphore::new(concurrency.max(1)));
    let mut handles = Vec::with_capacity(targets.len());
    for (ip, port) in targets {
        let sem = sem.clone();
        handles.push(tokio::spawn(async move {
            let _permit = sem.acquire().await;
            probe_handshake(&ip, port, timeout_ms).await
        }));
    }
    for h in handles {
        if let Ok(r) = h.await {
            match r.outcome {
                Outcome::Replied => summary.replied += 1,
                Outcome::PortUnreachable => summary.port_unreach += 1,
                Outcome::NoResponse => summary.timeout += 1,
                Outcome::Error => summary.errored += 1,
            }
            if r.is_live_server() && !summary.any_live {
                summary.any_live = true;
                summary.first_live = Some((r.ip.clone(), r.port));
            }
        }
    }
    summary
}

pub struct LiveResult {
    pub live: Vec<(String, u16)>,
    pub probed: usize,
    pub replied: usize,
    pub port_unreach: usize,
    pub timeout: usize,
}

fn cheap_hash(s: &str) -> u64 {
    let mut h: u64 = 1469598103934665603; // FNV-1a
    for b in s.bytes() {
        h ^= b as u64;
        h = h.wrapping_mul(1099511628211);
    }
    h
}

/// Probe instances (one (ip,port) each) with per-probe jitter and bounded concurrency, stopping the
/// moment `needed` distinct DBD challenges are confirmed. Returns live endpoints + outcome counts.
pub async fn probe_for_live(
    targets: Vec<(String, u16)>,
    needed: usize,
    timeout_ms: u64,
    concurrency: usize,
    jitter_ms: u64,
) -> LiveResult {
    let mut res = LiveResult { live: Vec::new(), probed: 0, replied: 0, port_unreach: 0, timeout: 0 };
    if targets.is_empty() {
        return res;
    }
    let sem = Arc::new(Semaphore::new(concurrency.max(1)));
    let done = Arc::new(AtomicBool::new(false));
    let live_count = Arc::new(AtomicUsize::new(0));
    let mut handles = Vec::with_capacity(targets.len());
    for (ip, port) in targets {
        let sem = sem.clone();
        let done = done.clone();
        let live_count = live_count.clone();
        handles.push(tokio::spawn(async move {
            let _permit = sem.acquire().await;
            if done.load(Ordering::Relaxed) {
                return (None, Outcome::Error, true); // skipped after `needed` reached
            }
            if jitter_ms > 0 {
                sleep(Duration::from_millis((cheap_hash(&ip) + port as u64) % jitter_ms)).await;
            }
            let r = probe_handshake(&ip, port, timeout_ms).await;
            let live = r.is_live_server();
            if live {
                let n = live_count.fetch_add(1, Ordering::Relaxed) + 1;
                if n >= needed {
                    done.store(true, Ordering::Relaxed);
                }
            }
            (if live { Some((ip, port)) } else { None }, r.outcome, false)
        }));
    }
    for h in handles {
        if let Ok((live_ep, outcome, skipped)) = h.await {
            if skipped {
                continue;
            }
            res.probed += 1;
            match outcome {
                Outcome::Replied => res.replied += 1,
                Outcome::PortUnreachable => res.port_unreach += 1,
                Outcome::NoResponse => res.timeout += 1,
                _ => {}
            }
            if let Some(ep) = live_ep {
                res.live.push(ep);
            }
        }
    }
    res
}

/// Steam A2S_INFO probe (exploration): if DBD answers it, the reply distinguishes a busy in-match
/// server from an idle/ready one. Returns Replied (with magic=true on a valid info reply).
pub async fn probe_a2s(ip: &str, port: u16, timeout_ms: u64) -> Outcome {
    let mut payload: Vec<u8> = vec![0xFF, 0xFF, 0xFF, 0xFF, 0x54];
    payload.extend_from_slice(b"Source Engine Query\0");
    let sock = match UdpSocket::bind("0.0.0.0:0").await {
        Ok(s) => s,
        Err(_) => return Outcome::Error,
    };
    if let Ok(local) = sock.local_addr() {
        mark_beacon_port(local.port());
    }
    if sock.connect((ip, port)).await.is_err() {
        return Outcome::Error;
    }
    if sock.send(&payload).await.is_err() {
        return Outcome::Error;
    }
    let mut buf = [0u8; 2048];
    match timeout(Duration::from_millis(timeout_ms), sock.recv(&mut buf)).await {
        Ok(Ok(_)) => Outcome::Replied,
        Ok(Err(e)) if e.kind() == ErrorKind::ConnectionRefused => Outcome::PortUnreachable,
        Ok(Err(_)) => Outcome::Error,
        Err(_) => Outcome::NoResponse,
    }
}

/// Handshake every port in 7777..=7820 on one IP; return the ports that answer as a live DBD server.
pub async fn harvest_live_ports(ip: &str) -> Vec<u16> {
    let targets: Vec<(String, u16)> = (7777u16..=7820).map(|p| (ip.to_string(), p)).collect();
    let sem = std::sync::Arc::new(Semaphore::new(32));
    let mut handles = Vec::new();
    for (ip, port) in targets {
        let sem = sem.clone();
        handles.push(tokio::spawn(async move {
            let _permit = sem.acquire().await;
            probe_handshake(&ip, port, 800).await
        }));
    }
    let mut ports = Vec::new();
    for h in handles {
        if let Ok(r) = h.await {
            if r.is_live_server() {
                ports.push(r.port);
            }
        }
    }
    ports.sort_unstable();
    ports
}
