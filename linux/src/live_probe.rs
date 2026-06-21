// Active liveness probing of real DBD game servers — the Linux port of LiveProbe.cs.
//
// DBD's dedicated servers run on Unreal Engine and answer a connectionless "stateless connect
// handshake" with a Challenge before any auth. Replaying a captured InitialConnect therefore tells
// us, in real time, whether a region's fleet is up: a reply carrying the build magic (c9 1e f8 11)
// is zero-false-positive proof a live DBD server is there. A connected UDP socket also surfaces
// ICMP port-unreachable (ECONNREFUSED) so we can tell "host up, no process" from "nothing there".

use std::collections::HashMap;
use std::io::ErrorKind;
use std::sync::Mutex;
use std::sync::OnceLock;
use std::time::{Duration, Instant};
use tokio::net::UdpSocket;
use tokio::sync::Semaphore;
use tokio::time::timeout;

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

fn handshake() -> Vec<u8> {
    (0..UE_HANDSHAKE_HEX.len() / 2)
        .map(|i| u8::from_str_radix(&UE_HANDSHAKE_HEX[i * 2..i * 2 + 2], 16).unwrap_or(0))
        .collect()
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
    let hs = handshake();
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
            report.magic = n >= 9 && buf[5..9] == MAGIC;
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
