// Hard region lock for Linux — blocks the game-server DATA PLANE (UDP) to unwanted AWS regions
// using nftables, while leaving the TCP control plane (EAC, matchmaking, startup) untouched.
//
// The hosts file can only block DNS for the GameLift beacons/service endpoint; it can't stop DBD
// connecting to a game server it's already been placed on (a raw EC2 IP over UDP ~7777-7820), nor
// DBD's server-side fallback to N. Virginia. So we drop outbound UDP 7770-7820 (ping beacon +
// game-server range) to the chosen regions' IP ranges. The binary only has cap_net_raw/
// cap_dac_override (not cap_net_admin), so nft is run via pkexec.
//
// nftables is used (not thousands of iptables rules) because an interval set blocks all the CIDRs
// efficiently in a single ruleset. Requires `nft` (nftables) to be installed.
use crate::aws_ranges::AwsIpService;
use std::collections::HashSet;

const TABLE: &str = "myc_regionlock";

fn build_ruleset(cidrs: &[String]) -> String {
    let elements = cidrs.join(", ");
    format!(
        "table inet {table} {{\n\
         \tset blocked {{\n\
         \t\ttype ipv4_addr\n\
         \t\tflags interval\n\
         \t\telements = {{ {elements} }}\n\
         \t}}\n\
         \tchain output {{\n\
         \t\ttype filter hook output priority 0; policy accept;\n\
         \t\tudp dport 7770-7820 ip daddr @blocked drop\n\
         \t}}\n\
         }}\n",
        table = TABLE,
        elements = elements
    )
}

/// Block the game-server data plane of the given AWS region codes. One pkexec prompt.
pub async fn apply_lock(aws: &AwsIpService, block_codes: &HashSet<String>) -> Result<String, String> {
    if block_codes.is_empty() {
        remove_lock();
        return Ok("No regions to block; any existing lock was removed.".to_string());
    }

    let cidrs = aws.get_cidrs_for_regions(block_codes).await;
    if cidrs.is_empty() {
        return Err("Could not fetch AWS IP ranges (no internet?). No changes were made.".to_string());
    }

    let ruleset = build_ruleset(&cidrs);
    let tmp = std::env::temp_dir().join("myc_regionlock.nft");
    if let Err(e) = std::fs::write(&tmp, ruleset) {
        return Err(format!("Failed to write nft ruleset: {}", e));
    }

    // Replace any existing table, then load the new ruleset — one privileged invocation.
    let script = format!(
        "nft delete table inet {table} 2>/dev/null; nft -f '{path}'",
        table = TABLE,
        path = tmp.display()
    );
    let status = std::process::Command::new("pkexec")
        .arg("sh")
        .arg("-c")
        .arg(&script)
        .status();
    let _ = std::fs::remove_file(&tmp);

    match status {
        Ok(s) if s.success() => Ok(format!(
            "blocked UDP 7770-7820 to {} IP ranges across {} region(s)",
            cidrs.len(),
            block_codes.len()
        )),
        Ok(_) => Err("Failed to apply firewall rules (cancelled, or nftables not installed?).".to_string()),
        Err(e) => Err(format!("Failed to run pkexec/nft: {}", e)),
    }
}

/// Remove the hard-lock nftables table. One pkexec prompt; ignores "not found".
pub fn remove_lock() {
    let _ = std::process::Command::new("pkexec")
        .arg("sh")
        .arg("-c")
        .arg(format!("nft delete table inet {} 2>/dev/null; true", TABLE))
        .status();
}
