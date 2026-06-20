use std::time::{Duration, Instant};
use tokio::net::TcpStream;
use tokio::time::timeout;

pub async fn ping_host(hostname: &str) -> i64 {
    let ports = [443, 80];

    for port in ports {
        let address = format!("{}:{}", hostname, port);
        let start = Instant::now();

        // Try to establish TCP connection with 2 second timeout
        match timeout(Duration::from_secs(2), TcpStream::connect(&address)).await {
            Ok(Ok(_)) => {
                // Connection successful, return latency
                return start.elapsed().as_millis() as i64;
            }
            Ok(Err(_)) => {
                // Connection failed, try next port
                continue;
            }
            Err(_) => {
                // Timeout, try next port
                continue;
            }
        }
    }

    // All connection attempts failed
    -1
}
