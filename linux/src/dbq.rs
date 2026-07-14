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

// Two DBQ mirrors. One sometimes glitches and reports EVERY region offline (even always-on stable
// ones) — never real — while the other is correct. We query both, discard an all-offline response,
// and use the sane/fresher one. api2 has been the more reliable mirror, so it's first.
const REGIONS_URLS: [&str; 2] = [
    "https://api2.deadbyqueue.com/regions",
    "https://api.deadbyqueue.com/regions",
];
const QUEUE_URLS: [&str; 2] = [
    "https://api2.deadbyqueue.com/queuetime?region=",
    "https://api.deadbyqueue.com/queuetime?region=",
];
const UA: &str = "make-your-choice";

async fn fetch_regions(url: &str) -> (HashMap<String, bool>, Option<i64>) {
    let mut out = HashMap::new();
    let client = reqwest::Client::new();
    let resp = match client.get(url).header("User-Agent", UA).send().await {
        Ok(r) => r,
        Err(_) => return (out, None),
    };
    let json: Value = match resp.json().await {
        Ok(j) => j,
        Err(_) => return (out, None),
    };
    let data_unix = json.get("lastupdated2").and_then(|v| v.as_i64());
    if let Some(regions) = json.get("regions").and_then(|r| r.as_object()) {
        for (code, v) in regions {
            if let Some(b) = v.as_bool() {
                out.insert(code.clone(), b);
            }
        }
    }
    (out, data_unix)
}

/// AWS region code -> online(true)/offline(false), plus DBQ's data-refresh time (unix). Queries both
/// mirrors, ignores an all-offline (glitched) response, returns the sane/fresher one. Empty map if
/// neither is sane, so the caller keeps its prior good values.
pub async fn get_region_status() -> (HashMap<String, bool>, Option<i64>) {
    let mut best: (HashMap<String, bool>, Option<i64>) = (HashMap::new(), None);
    let mut have_sane = false;
    for url in REGIONS_URLS.iter() {
        let r = fetch_regions(url).await;
        if r.0.is_empty() || !r.0.values().any(|&v| v) {
            continue; // failed, or all-offline glitch
        }
        if !have_sane || r.1.unwrap_or(0) > best.1.unwrap_or(0) {
            best = r;
        }
        have_sane = true;
    }
    best
}

/// Returns (raw text, killer queue minutes). minutes is 0 when under a minute, -1 if unknown.
/// Tries both mirrors; returns the first that yields a real queue time.
pub async fn get_queue(region_code: &str) -> (String, i64) {
    let client = reqwest::Client::new();
    for base in QUEUE_URLS.iter() {
        let url = format!("{}{}", base, region_code);
        if let Ok(resp) = client.get(&url).header("User-Agent", UA).send().await {
            if let Ok(text) = resp.text().await {
                let t = text.trim().to_string();
                if !t.is_empty() && !t.starts_with("HTTP ") && t.contains("Killer") {
                    let minutes = parse_killer_minutes(&t);
                    return (t, minutes);
                }
            }
        }
    }
    (String::new(), -1) // no mirror had a queue (e.g. region down) -> caller shows nothing
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
