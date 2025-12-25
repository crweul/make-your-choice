> [!NOTE]
> This repository was previously hosted on Codeberg and has been moved to GitHub on December 17, 2025.  
> You are currently on the new repository, no action is required.  
> ↳ [Visit the old repository](https://codeberg.org/ky/make-your-choice).

# Make Your Choice
[![GitHub Downloads](https://img.shields.io/github/downloads/laewliet/make-your-choice/total?style=for-the-badge&logo=github&logoColor=f5f5f5&label=downloads+(windows))](https://github.com/laewliet/make-your-choice/releases)
[![Codeberg Downloads](https://img.shields.io/badge/dynamic/json?style=for-the-badge&logo=codeberg&logoColor=f5f5f5&label=downloads+(1.0.0+RC)&query=$.assets[0].download_count&url=https://codeberg.org/api/v1/repos/ky/make-your-choice/releases/tags/1.0.0-RC)](https://codeberg.org/ky/make-your-choice/releases/tag/1.0.0-RC)
[![Discord](https://img.shields.io/discord/1173896039401521245?style=for-the-badge&logo=discord&logoColor=f5f5f5&label=discord)](https://discord.gg/mH7vgCEFWq)
[![Ko-fi](https://img.shields.io/badge/support_me_through_ko--fi-F16061?style=for-the-badge&logo=kofi&logoColor=f5f5f5)](https://ko-fi.com/kylo)

Make Your Choice is a server region changer for Dead by Daylight. It allows you to play on any server of choice.

<img src="https://i.imgur.com/oJetRV7.png" alt="Main">

# Download & Installation

## Windows
### Installation
Download the latest `.exe` file from the [Releases](https://github.com/laewliet/make-your-choice/releases/latest) page and run it as administrator.

### Supported Windows Versions
- Windows 7
- Windows 8 & 8.1
- Windows 10
- Windows 11

### UAC Popup & SmartScreen Alert
The application needs to be run with [administrator permissions](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/) to ensure the hosts file can be edited. Since I don't want to pay Microsoft a fee for getting this free application signed, you will be met with a prompt to trust the unknown developer.

The SmartScreen Alert only appears on Windows 8 and newer.  

## Linux / SteamOS
> [!WARNING]
> **For SteamOS users**: The current version (v2.0.0) will only work on SteamOS if you disable system immutability.
> Proper SteamOS support will be added in the next update. Hang in there! ( ゝ◡╹ )ノ♡
>
> *If you know what you are doing, you could `git clone`, compile to binary, and run that in the mean time.*

### Prerequisites
Install the prerequisite packages in order to build, install and run the program. If your distro isn't listed below, find out the correct package names for your distro's package manager.
#### Arch / SteamOS
```bash
sudo pacman -S rust gtk4 polkit base-devel git
```
#### Debian / Ubuntu / ZorinOS
```bash
sudo apt install cargo rustc make gcc pkg-config libgtk-4-dev git policykit-1
```
#### Fedora
```bash
sudo dnf install cargo rust make gcc pkg-config gtk4-devel git polkit
```
#### openSUSE
```bash
sudo zypper install cargo rust make gcc pkg-config gtk4-devel git polkit
```
### Installation
#### Arch (AUR)

For Arch Linux users, install from the AUR using your preferred AUR helper:
```bash
# Using yay
yay -S make-your-choice

# Using paru
paru -S make-your-choice

# Using pikaur
pikaur -S make-your-choice
```

#### Other Linux Distros (Build and Install from Source)

Clone and install using Makefile:
```bash
cd ~/ && git clone https://github.com/laewliet/make-your-choice.git
cd make-your-choice/linux && make install
cd ~/ && rm -rf ~/make-your-choice
```
After installation, the clone will be removed.


# Screenshots
## Windows
<img src="https://i.imgur.com/wyNJ7HO.png" alt="Main" height="400"> <img src="https://i.imgur.com/J2pI1sy.png" alt="Main" height="400">  
*Screenshots taken on Windows 10 with a Windows 7 skin.*
## Linux
<img src="https://i.imgur.com/VlHsxtc.png" alt="Main" height="400"> <img src="https://i.imgur.com/BXZuWkL.png" alt="Main" height="400"> <img src="https://i.imgur.com/xtvswcf.png" alt="Main" height="400">  
*Screenshots taken on Arch Linux with the KDE Plasma Desktop Environment.*
