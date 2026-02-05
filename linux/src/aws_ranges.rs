use reqwest;
use serde_json::Value;
use std::net::{IpAddr, Ipv4Addr};
use std::sync::{Arc, Mutex};
use tokio::sync::Mutex as AsyncMutex;

#[derive(Debug, Clone)]
pub struct AwsCidr {
    network: u32,
    mask: u32,
    prefix_len: u8,
    region: String,
}

#[derive(Clone)]
pub struct AwsIpService {
    cidrs: Arc<Mutex<Vec<AwsCidr>>>,
    fetch_lock: Arc<AsyncMutex<()>>,
}

impl AwsIpService {
    pub fn new() -> Self {
        Self {
            cidrs: Arc::new(Mutex::new(Vec::new())),
            fetch_lock: Arc::new(AsyncMutex::new(())),
        }
    }

    async fn refresh(&self) -> Result<(), Box<dyn std::error::Error>> {
        let _guard = self.fetch_lock.lock().await;
        {
            let cidrs = self.cidrs.lock().unwrap();
            if !cidrs.is_empty() {
                return Ok(());
            }
        }
        let url = "https://ip-ranges.amazonaws.com/ip-ranges.json";
        let client = reqwest::Client::new();
        let resp = client
            .get(url)
            .header("User-Agent", "make-your-choice")
            .send()
            .await?
            .json::<Value>()
            .await?;

        let mut list = Vec::new();
        if let Some(prefixes) = resp.get("prefixes").and_then(|p| p.as_array()) {
            for p in prefixes {
                let ip_prefix = match p.get("ip_prefix").and_then(|v| v.as_str()) {
                    Some(v) if !v.is_empty() => v,
                    _ => continue,
                };

                let region = p.get("region").and_then(|v| v.as_str()).unwrap_or("");

                if let Some((network, mask, prefix_len)) = parse_ipv4_cidr(ip_prefix) {
                    list.push(AwsCidr {
                        network,
                        mask,
                        prefix_len,
                        region: region.to_string(),
                    });
                }
            }
        }

        let mut cidrs = self.cidrs.lock().unwrap();
        *cidrs = list;
        Ok(())
    }

    pub async fn get_region(&self, ip_str: &str) -> Option<String> {
        self.refresh().await.ok()?;

        let ip: IpAddr = ip_str.parse().ok()?;
        let ip_v4 = match ip {
            IpAddr::V4(v4) => v4,
            IpAddr::V6(_) => return None,
        };

        let ip_val = u32::from(ip_v4);
        let cidrs = self.cidrs.lock().unwrap();

        let mut best: Option<&AwsCidr> = None;
        for cidr in cidrs.iter() {
            if (ip_val & cidr.mask) == cidr.network {
                if best.map_or(true, |b| cidr.prefix_len > b.prefix_len) {
                    best = Some(cidr);
                }
            }
        }

        best.map(|c| Self::get_pretty_region_name(&c.region))
    }

    pub fn get_pretty_region_name(region_code: &str) -> String {
        match region_code {
            "us-east-1" => "US East (N. Virginia)",
            "us-east-2" => "US East (Ohio)",
            "us-west-1" => "US West (N. California)",
            "us-west-2" => "US West (Oregon)",
            "ca-central-1" => "Canada (Central)",
            "sa-east-1" => "South America (São Paulo)",
            "eu-west-1" => "Europe (Ireland)",
            "eu-west-2" => "Europe (London)",
            "eu-central-1" => "Europe (Frankfurt am Main)",
            "eu-north-1" => "Europe (Stockholm)",
            "eu-west-3" => "Europe (Paris)",
            "eu-south-1" => "Europe (Milan)",
            "ap-northeast-1" => "Asia Pacific (Tokyo)",
            "ap-northeast-2" => "Asia Pacific (Seoul)",
            "ap-south-1" => "Asia Pacific (Mumbai)",
            "ap-southeast-1" => "Asia Pacific (Singapore)",
            "ap-southeast-2" => "Asia Pacific (Sydney)",
            "ap-east-1" => "Asia Pacific (Hong Kong)",
            "af-south-1" => "Africa (Cape Town)",
            "me-south-1" => "Middle East (Bahrain)",
            "ap-northeast-3" => "Asia Pacific (Osaka)",
            _ => region_code,
        }.to_string()
    }
}

fn parse_ipv4_cidr(cidr: &str) -> Option<(u32, u32, u8)> {
    let mut parts = cidr.split('/');
    let ip_str = parts.next()?;
    let prefix_str = parts.next()?;
    if parts.next().is_some() {
        return None;
    }

    let ip: Ipv4Addr = ip_str.parse().ok()?;
    let prefix_len: u8 = prefix_str.parse().ok()?;
    if prefix_len > 32 {
        return None;
    }

    let ip_val = u32::from(ip);
    let mask = if prefix_len == 0 { 0 } else { u32::MAX << (32 - prefix_len) };
    let network = ip_val & mask;
    Some((network, mask, prefix_len))
}
