use std::time::{Duration, Instant};
use std::net::ToSocketAddrs;

pub async fn ping_host(hostname: &str) -> i64 {
    // Run ping in a blocking task since the ping crate is not async
    let hostname = hostname.to_string();

    match tokio::task::spawn_blocking(move || {
        // Resolve hostname to IP address
        let ip = format!("{}:0", hostname)
            .to_socket_addrs()
            .ok()?
            .next()?
            .ip();

        // Measure time manually
        let start = Instant::now();

        // Ping with 2 second timeout
        let result = ping::ping(
            ip,
            Some(Duration::from_secs(2)), // timeout
            None, // ttl
            None, // ident
            None, // seq_cnt
            None, // payload
        );

        match result {
            Ok(_) => Some(start.elapsed().as_millis() as i64),
            Err(_) => None,
        }
    }).await {
        Ok(Some(latency)) => latency,
        _ => -1,
    }
}
