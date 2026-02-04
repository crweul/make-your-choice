use anyhow::{Context, Result};
use serde::Deserialize;

#[derive(Debug, Deserialize)]
struct Release {
    tag_name: String,
}

#[derive(Clone)]
pub struct UpdateChecker {
    developer: String,
    repo: String,
    current_version: String,
}

impl UpdateChecker {
    pub fn new(developer: String, repo: String, current_version: String) -> Self {
        Self {
            developer,
            repo,
            current_version,
        }
    }

    pub async fn check_for_updates(&self) -> Result<Option<String>> {
        let url = format!(
            "https://api.github.com/repos/{}/{}/releases",
            self.developer, self.repo
        );

        let client = reqwest::Client::new();
        let releases: Vec<Release> = client
            .get(&url)
            .header("User-Agent", "make-your-choice")
            .send()
            .await
            .context("Failed to fetch releases")?
            .json()
            .await
            .context("Failed to parse release JSON")?;

        if let Some(latest) = releases.first() {
            if latest.tag_name.to_lowercase() != self.current_version.to_lowercase() {
                return Ok(Some(latest.tag_name.clone()));
            }
        }

        Ok(None)
    }

    pub fn get_releases_url(&self) -> String {
        format!("https://github.com/{}/{}/releases/latest", self.developer, self.repo)
    }
}
