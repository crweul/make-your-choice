// Probes an AWS GameLift ping beacon directly (the same mechanism Dead by Queue uses), so we can
// detect whether a region's fleet is live or ramped down in real time instead of waiting on Dead
// by Queue's slow (20-60 min) all-region rotation.
//
// Protocol: send a 12-byte UDP packet to gamelift-ping.<region>.api.aws on port 443 — the "GLPL"
// magic (4 bytes) followed by an 8-byte big-endian Unix-ms timestamp. A live fleet echoes back 12
// bytes starting with "GLPL"; a ramped-down fleet stays silent.
//
// This is UDP 443, not the 7770-7820 game-server range the hard lock blocks, and we only probe the
// user's selected region (which is never blocked), so it works with the lock on.
use std::time::Duration;
use tokio::net::UdpSocket;
use tokio::time::timeout;

/// Some(true)  = beacon echoed (fleet online)
/// Some(false) = resolved but no valid response before timeout (fleet ramped down / offline)
/// None        = could not probe (DNS/socket failure) -> caller should fall back to another source
pub async fn is_fleet_online(ping_host: &str, timeout_ms: u64) -> Option<bool> {
    if ping_host.trim().is_empty() {
        return None;
    }

    // Resolve via the OS resolver (which honors /etc/hosts); skip a host blocked to 0.0.0.0.
    let addr = match lookup_host(ping_host).await {
        Some(a) => a,
        None => return None,
    };

    let bind = if addr.is_ipv6() { "[::]:0" } else { "0.0.0.0:0" };
    let sock = match UdpSocket::bind(bind).await {
        Ok(s) => s,
        Err(_) => return None,
    };
    if sock.connect(addr).await.is_err() {
        return None;
    }

    let ts = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0);
    let mut packet = [0u8; 12];
    packet[0..4].copy_from_slice(b"GLPL");
    packet[4..12].copy_from_slice(&ts.to_be_bytes());

    if sock.send(&packet).await.is_err() {
        return None;
    }

    let mut buf = [0u8; 64];
    match timeout(Duration::from_millis(timeout_ms), sock.recv(&mut buf)).await {
        Ok(Ok(n)) => Some(n >= 4 && &buf[0..4] == b"GLPL"),
        Ok(Err(_)) => None,     // socket error -> inconclusive
        Err(_) => Some(false),  // timeout -> resolved, sent, no echo -> ramped down
    }
}

async fn lookup_host(host: &str) -> Option<std::net::SocketAddr> {
    match tokio::net::lookup_host((host, 443u16)).await {
        Ok(it) => it.into_iter().find(|a| !a.ip().is_unspecified()),
        Err(_) => None,
    }
}
