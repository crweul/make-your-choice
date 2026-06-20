// Client for the public Dead by Queue API (https://www.deadbyqueue.com), used to show real
// server online/offline status and killer queue times.
//
//   GET /regions                 -> {"regions":{"us-east-2":false,"eu-west-2":true, ...}}
//                                   true = region online, false = offline / ramped down
//   GET /queuetime?region=<code> -> "Killer: 10m24s | Survivor: 12s"  (plain text)
//
// NOTE: the queue endpoint returns "HTTP 500" for browser-like ("Mozilla/...") User-Agents,
// so we send a simple product token instead.
use serde_json::Value;
use std::collections::HashMap;

const REGIONS_URL: &str = "https://api.deadbyqueue.com/regions";
const QUEUE_URL: &str = "https://api.deadbyqueue.com/queuetime?region=";
const UA: &str = "make-your-choice";

/// AWS region code -> online(true)/offline(false). Empty map on failure.
pub async fn get_region_status() -> HashMap<String, bool> {
    let mut out = HashMap::new();
    let client = reqwest::Client::new();
    let resp = match client.get(REGIONS_URL).header("User-Agent", UA).send().await {
        Ok(r) => r,
        Err(_) => return out,
    };
    let json: Value = match resp.json().await {
        Ok(j) => j,
        Err(_) => return out,
    };
    if let Some(regions) = json.get("regions").and_then(|r| r.as_object()) {
        for (code, v) in regions {
            if let Some(b) = v.as_bool() {
                out.insert(code.clone(), b);
            }
        }
    }
    out
}

/// Returns (raw text, killer queue minutes). minutes is 0 when under a minute, -1 if unknown.
pub async fn get_queue(region_code: &str) -> (String, i64) {
    let client = reqwest::Client::new();
    let url = format!("{}{}", QUEUE_URL, region_code);
    match client.get(&url).header("User-Agent", UA).send().await {
        Ok(resp) => match resp.text().await {
            Ok(text) => {
                let t = text.trim().to_string();
                if t.is_empty() || t.starts_with("HTTP ") || !t.contains("Killer") {
                    return (if t.is_empty() { "No data".to_string() } else { t }, -1);
                }
                let minutes = parse_killer_minutes(&t);
                (t, minutes)
            }
            Err(e) => (format!("Queue unavailable: {}", e), -1),
        },
        Err(e) => (format!("Queue unavailable: {}", e), -1),
    }
}

// Parse the minutes out of "Killer: 10m24s | ...". Returns 0 if "Killer" is present but no
// minutes component (e.g. only seconds), -1 if not found at all.
fn parse_killer_minutes(text: &str) -> i64 {
    let after = match text.find("Killer") {
        Some(idx) => &text[idx..],
        None => return -1,
    };
    let bytes = after.as_bytes();
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i].is_ascii_digit() {
            let start = i;
            while i < bytes.len() && bytes[i].is_ascii_digit() {
                i += 1;
            }
            if i < bytes.len() && bytes[i] == b'm' {
                return after[start..i].parse::<i64>().unwrap_or(0);
            }
        } else {
            i += 1;
        }
    }
    0
}
