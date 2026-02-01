use serde::{Deserialize, Serialize};
use std::collections::HashMap;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RegionInfo {
    pub hosts: Vec<String>,
    pub stable: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ApplyMode {
    Gatekeep,
    UniversalRedirect,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum BlockMode {
    Both,
    OnlyPing,
    OnlyService,
}

pub fn get_selectable_regions() -> HashMap<String, RegionInfo> {
    let mut regions = HashMap::new();

    // Europe
    regions.insert(
        "Europe (London)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.eu-west-2.amazonaws.com".to_string(),
                "gamelift-ping.eu-west-2.api.aws".to_string(),
            ],
            stable: false,
        },
    );
    regions.insert(
        "Europe (Ireland)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.eu-west-1.amazonaws.com".to_string(),
                "gamelift-ping.eu-west-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Europe (Frankfurt am Main)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.eu-central-1.amazonaws.com".to_string(),
                "gamelift-ping.eu-central-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );

    // The Americas
    regions.insert(
        "US East (N. Virginia)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.us-east-1.amazonaws.com".to_string(),
                "gamelift-ping.us-east-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "US East (Ohio)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.us-east-2.amazonaws.com".to_string(),
                "gamelift-ping.us-east-2.api.aws".to_string(),
            ],
            stable: false,
        },
    );
    regions.insert(
        "US West (N. California)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.us-west-1.amazonaws.com".to_string(),
                "gamelift-ping.us-west-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "US West (Oregon)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.us-west-2.amazonaws.com".to_string(),
                "gamelift-ping.us-west-2.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Canada (Central)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.ca-central-1.amazonaws.com".to_string(),
                "gamelift-ping.ca-central-1.api.aws".to_string(),
            ],
            stable: false,
        },
    );
    regions.insert(
        "South America (São Paulo)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.sa-east-1.amazonaws.com".to_string(),
                "gamelift-ping.sa-east-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );

    // Asia (excluding Mainland China)
    regions.insert(
        "Asia Pacific (Tokyo)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.ap-northeast-1.amazonaws.com".to_string(),
                "gamelift-ping.ap-northeast-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Asia Pacific (Seoul)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.ap-northeast-2.amazonaws.com".to_string(),
                "gamelift-ping.ap-northeast-2.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Asia Pacific (Mumbai)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.ap-south-1.amazonaws.com".to_string(),
                "gamelift-ping.ap-south-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Asia Pacific (Singapore)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.ap-southeast-1.amazonaws.com".to_string(),
                "gamelift-ping.ap-southeast-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Asia Pacific (Hong Kong)".to_string(),
        RegionInfo {
            hosts: vec![
                "ec2.ap-east-1.amazonaws.com".to_string(),
                "gamelift-ping.ap-east-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );

    // Oceania
    regions.insert(
        "Asia Pacific (Sydney)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.ap-southeast-2.amazonaws.com".to_string(),
                "gamelift-ping.ap-southeast-2.api.aws".to_string(),
            ],
            stable: true,
        },
    );

    regions
}

// These regions are always blocked regardless of user choice. DbD doesn't use them so they're not shown in the UI. They are just blocked for stability purposes.
pub fn get_blocked_regions() -> HashMap<String, RegionInfo> {
    let mut regions = HashMap::new();

    regions.insert(
        "Africa (Cape Town)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.af-south-1.amazonaws.com".to_string(),
                "gamelift-ping.af-south-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Asia Pacific (Osaka)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.ap-northeast-3.amazonaws.com".to_string(),
                "gamelift-ping.ap-northeast-3.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Europe (Stockholm)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.eu-north-1.amazonaws.com".to_string(),
                "gamelift-ping.eu-north-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Europe (Paris)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.eu-west-3.amazonaws.com".to_string(),
                "gamelift-ping.eu-west-3.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Europe (Milan)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.eu-south-1.amazonaws.com".to_string(),
                "gamelift-ping.eu-south-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Middle East (Bahrain)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.me-south-1.amazonaws.com".to_string(),
                "gamelift-ping.me-south-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Asia Pacific (Malaysia)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.ap-southeast-5.amazonaws.com".to_string(),
                "gamelift-ping.ap-southeast-5.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "Asia Pacific (Thailand)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.ap-southeast-7.amazonaws.com".to_string(),
                "gamelift-ping.ap-southeast-7.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "China (Beijing)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.cn-north-1.amazonaws.com.cn".to_string(),
                "gamelift-ping.cn-north-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );
    regions.insert(
        "China (Ningxia)".to_string(),
        RegionInfo {
            hosts: vec![
                "gamelift.cn-northwest-1.amazonaws.com.cn".to_string(),
                "gamelift-ping.cn-northwest-1.api.aws".to_string(),
            ],
            stable: true,
        },
    );

    regions
}

pub fn get_group_name(region: &str) -> &'static str {
    if region.starts_with("Europe") {
        "Europe"
    } else if region.starts_with("US") || region.starts_with("Canada") || region.starts_with("South America") {
        "Americas"
    } else if region.contains("Sydney") {
        "Oceania"
    } else if region.contains("China") {
        "China"
    } else {
        "Asia"
    }
}
