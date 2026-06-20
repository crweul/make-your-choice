> [!NOTE]
> This repository was previously hosted on Codeberg and has been moved to GitHub on December 17, 2025.  
> You are currently on the new repository, no action is required.  
> ↳ [Visit the old repository](https://codeberg.org/ky/make-your-choice).

[![GitHub Downloads](https://img.shields.io/github/downloads/crweul/make-your-choice/total?style=for-the-badge&color=6e5494&logo=github&logoColor=f5f5f5&label=downloads+(precompiled))](https://github.com/crweul/make-your-choice/releases)
[![Codeberg Downloads](https://img.shields.io/badge/dynamic/json?style=for-the-badge&logo=codeberg&logoColor=f5f5f5&label=downloads+(1.0.0+RC)&query=$.assets[0].download_count&url=https://codeberg.org/api/v1/repos/ky/make-your-choice/releases/tags/1.0.0-RC)](https://codeberg.org/ky/make-your-choice/releases/tag/1.0.0-RC)
[![Build Status](https://img.shields.io/github/actions/workflow/status/crweul/make-your-choice/build.yml?style=for-the-badge&logo=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAD20lEQVRYR7WXaYiOURTHvfliGJNCmpAlNdb4YAljkiyNpUHJ+oFIItuIL8QYDI1dGSkU2Uuyjd1EBllTCmMb0hAzaYw1H16//3TvdOf1vu/zPK+ZU6fnLv9zzv+5z3PPPTfUwKeEw+FGQMeg49HOaAuj8lBu9CnPE+i5UCj0y4/rkBeIwK3ArEWnokleeDP/g+cBdCVEPseziUuA4LkYL0X19omIiKyHhF4gqkQlQODmoM+g/SOsntA/hV5F36MfzHwqzzboMDQL7RphV0R/HEQqI1n8Q4DgXQCdR9s54Ae0F+Dglp9lwMdgcDvQHg7+Be2h+Hjn+qhFAEO9xR20tQOah1GBn8CRGPwtZmxLBIl++Ptix2oIAE5h8KbDWss7BrDePmHB7yCMT6L6rJLb6BC7S1wCO5mYa0C/eQ4A9DDhyI4hJDLoXkMbmuHV+M5Ru5oAgI48Xjk20wHsr4vg1gcxltDeZPrfeHYgRrklcJiByWaymIn0ugzukFCiUhKTbCVOdghmTel8dQKmM1EciwD4bOaOgikLShJbZVFlSkkFPlqIwCQ6R8zgYwZ7xXMMPsz8dzQP3Qj+j18imGrFP6FK45IMEThEY4oZyMXhKh8ELOQ1DeWHcwFI7AE70+DzReAend5mYKBXsjErEBnvAgPKFyIUV7CfAOC4ARWKQCkdm/Xa4+RtgBVwofoU21BtMX2iqEI8feJHZvKJCAjc2AwkeR2jMVbADabTb2yslcS+GfM2E1aKgHaAdoIkOR57AXwS0METdSdhn4ybKhOvSgR0SHQyA2kYlvzHJ9iObY7HJ1AeUD6QlIjADRrK15LhGF9OgMBFbOZjq5eJK8TLBFBoQEUisJvObDOgfb0sAAEdrQp82iuwnSeejun5pl8gAqrzrIMSnKX5IGAT0WbwOrh8C/GUQVXASDJFQOVWBWp3go5KVTBRBbzO+GMJpuJR2J41jqvwkWIPo30MzjAT95no4/uVAgAhr5KumzHZRZy5loASUanjaxaTewP49oQSfBGgrQ4wlRgf3YJEWWyhAdR3QZJH8OWK5RJQglBJ1tOQqK+S7C7+B0PgZy0C6rBM+jsFUHFqZQ5gbdXAgj/dKfIdwze0++JPN6lqiVaWK1NdQts6hvdp69hVQekpBNb9QNVwdwf8nHYmPkSiRmJdTFqC0BkfuRt0imkbXUHLcPaSYDpHtHIiPAIdido/3Qa6TiMLvPfFxGWH8zX0V3i+cmyAis91BN4QC+LncqpLihxMC0BEmfKgyLvfO5q9JwFrxGo0oT0anYiqjNdFQ7WdakR7PX9GWwWr77PhL8wGiLa1JMgJAAAAEGRlQkdDNzZFNENGNTAxMTcwQTFCfZSh2AAAAABJRU5ErkJggg==)](https://github.com/crweul/make-your-choice/actions)
[![Discord](https://img.shields.io/discord/1173896039401521245?style=for-the-badge&color=5865f2&logo=discord&logoColor=f5f5f5&label=discord)](https://discord.gg/mH7vgCEFWq)
[![Ko-fi](https://img.shields.io/badge/support_me_through_ko--fi-F16061?style=for-the-badge&logo=kofi&logoColor=f5f5f5)](https://ko-fi.com/kylo)

# Make Your Choice
Make Your Choice is a server region changer for Dead by Daylight. It allows you to play on any server of choice.

<img src="https://i.imgur.com/oJetRV7.png" alt="Main">

# Installation: Windows
## Installation
Download the latest `.exe` file from the [Releases](https://github.com/crweul/make-your-choice/releases/latest) page and run it as administrator.

## Supported Windows Versions
- Windows 10
- Windows 11

## UAC Popup & SmartScreen Alert
The application needs to be run with [administrator permissions](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/) to ensure the hosts file can be edited. Since I don't want to pay Microsoft a fee for getting this free application signed, you will be met with a prompt to trust the unknown developer.  
<img src="https://i.imgur.com/zpMPDzM.png" alt="Main" height="350"> <img src="https://i.imgur.com/bu62CXd.png" alt="Main" height="350">

## Hard Region Lock (Windows)
Selecting a region edits your hosts file so the **client** stops measuring latency to the other regions, which makes DBD prefer the one you left. That's enough to *prefer* a region, but it can't stop DBD's **server-side fallback to N. Virginia** (`us-east-1`): you can't hosts-block Virginia (EAC/matchmaking/startup live there), and a match connects to a raw server IP over **UDP**, not a hostname.

The **Use hard region lock (firewall) to force exclude unchosen servers** option in **Program settings** closes that gap. When it's on, applying your selection also creates Windows Firewall rules that block **outbound UDP 7770–7820** (the GameLift ping beacon + game-server ports) to every region you did **not** choose — using AWS's published IP ranges:

- EAC / matchmaking / startup use **TCP 443**, which is never touched — the game still launches.
- If DBD tries to drop you onto an unchosen region the match **can't connect**, so it fails/re-queues instead of putting you there.

It's two-way: pick your region (e.g. Ohio) and click **Apply Selection** with the toggle on to lock; turning the toggle off (or **Reset hosts file**) removes the rules. With **Merge unstable servers** also on, the merged similar (stable) servers stay allowed too — everything else is still blocked, so an unstable pick falls back only to its similar server, never to the rest. Requires running as administrator.

## Live server status & system tray (Windows)
Real online/offline status for the unstable servers comes from the public [Dead by Queue](https://www.deadbyqueue.com) API (`/regions`), shown as ✓ (online) / ⚠ (offline) next to those servers in the list.

A single **system-tray icon** (the app icon with a coloured status bubble in its corner) tracks your preferred server: **green** = online, **red** = offline, **grey** = unknown. Hover it for the server status and the current killer/survivor queue time (from Dead by Queue).

By default, minimizing the window sends it to the **system tray** instead of the taskbar — toggle this with **Minimize to system tray** in Program settings. Double-click the tray icon (or right-click → *Show*) to restore. A **Start with Windows** toggle (Program settings) launches the app automatically at logon (via a scheduled task, so it starts elevated without a UAC prompt). When auto-started it goes straight to the system tray (or minimizes to the taskbar if *Minimize to system tray* is off).

> The standalone/portable build is the single-file `MakeYourChoice.exe` in the **`publish\`** folder — it runs from anywhere. (The loose exe under `win-x64\` needs its sibling files and won't run if copied alone.)

A **Notify when preferred server comes online** toggle (Program settings) shows a notification the moment your preferred server flips from offline to online.

### Linux notes
All of the above is also available on Linux (GTK build): the hard region lock uses **nftables** (`nft`, via a one-time `pkexec` prompt) instead of Windows Firewall; live status and the offline→online notification use the same Dead by Queue API and native desktop notifications; and the tray uses **StatusNotifierItem** (native on KDE Plasma; on GNOME it needs the AppIndicator/KStatusNotifier extension). On Linux the close button hides to the tray (rather than a separate minimize action). A **Start with PC** toggle (Program settings) adds an XDG autostart entry (`~/.config/autostart`) to launch at login; when auto-started it goes straight to the tray (or minimized if the tray is off/unavailable).

# Installation: Linux / SteamOS
> [!NOTE]
> **For SteamOS users**: There are two ways to use Make Your Choice:  
> 1. Download the binary and simply run it. For this, follow the steps for "Precompiled Binary" at the bottom. (Easiest)
> 2. Disable system immutability, and follow the steps for Arch Linux. This will give you a nice desktop entry which makes it easier to use the program. (Advanced, only recommended for nerds)

## Method 1: Package Manager
Currently, only Arch Linux is supported for this method.  
*If you would like to contribute: feel free to distribute Make Your Choice for other package managers, and give me a headsup so I can provide official steps for other people to follow.*
### Arch
Simply install the program from the AUR using your AUR helper of choice:
```bash
# Using yay
yay -S make-your-choice

# Using paru
paru -S make-your-choice

# Using pikaur
pikaur -S make-your-choice
```

## Method 2: Build & Install From Source (Makefile)
This method can be used to build and install the program straight from source using Makefile. This is the best choice for most distros that aren't SteamOS or Arch.
### Prerequisites
Install the prerequisite packages in order to build, install and run the program. If your distro isn't listed below, find out the correct package names for your distro's package manager.
#### Arch
```bash
sudo pacman -S rust gtk4 polkit base-devel git dbus nftables
```
#### Debian / Ubuntu / ZorinOS
```bash
sudo apt install cargo rustc make gcc pkg-config libgtk-4-dev libdbus-1-dev git policykit-1 nftables
```
#### Fedora
```bash
sudo dnf install cargo rust make gcc pkg-config gtk4-devel dbus-devel git polkit nftables
```
#### openSUSE
```bash
sudo zypper install cargo rust make gcc pkg-config gtk4-devel dbus-1-devel git polkit nftables
```
> `dbus`/`libdbus-1-dev` is required to build the system-tray support; `nftables` is required at runtime for the hard region lock.

### Build & Install
Clone and install using Makefile:
```bash
cd ~/ && git clone https://github.com/crweul/make-your-choice.git
cd make-your-choice/linux/makefile && make install
cd ~/ && rm -rf ~/make-your-choice
```
After installation, the clone will be removed.

## Method 3: Precompiled Binary
This option won't provide desktop entries to easily access the app. Use this only if you have no other options available. No prerequisites are required. Simply download the binary from the [Releases](https://github.com/crweul/make-your-choice/releases/latest) page and run it.

This option is recommended if you have a SteamOS device.


# Screenshots
## Windows
<img src="https://i.imgur.com/36jV4su.png" alt="Main" height="450"> <img src="https://i.imgur.com/45Gesfc.png" alt="Main" height="450">  
*Screenshots taken on Windows 11 Pro 25H2*
## Linux
<img src="https://i.imgur.com/VlHsxtc.png" alt="Main" height="450"> <img src="https://i.imgur.com/BXZuWkL.png" alt="Main" height="450"> <img src="https://i.imgur.com/xtvswcf.png" alt="Main" height="450">  
*Screenshots taken on Arch Linux with the KDE Plasma Desktop Environment.*
