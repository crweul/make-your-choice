> [!NOTE]
> This repository was previously hosted on Codeberg and has been moved to GitHub on December 17, 2025.  
> You are currently on the new repository, no action is required.  
> ↳ [Visit the old repository](https://codeberg.org/ky/make-your-choice).

# Make Your Choice
[![GitHub Downloads](https://img.shields.io/github/downloads/crweul/make-your-choice/total?style=for-the-badge&logo=github&logoColor=f5f5f5&label=downloads+(precompiled))](https://github.com/crweul/make-your-choice/releases)
[![Codeberg Downloads](https://img.shields.io/badge/dynamic/json?style=for-the-badge&logo=codeberg&logoColor=f5f5f5&label=downloads+(1.0.0+RC)&query=$.assets[0].download_count&url=https://codeberg.org/api/v1/repos/ky/make-your-choice/releases/tags/1.0.0-RC)](https://codeberg.org/ky/make-your-choice/releases/tag/1.0.0-RC)
[![Discord](https://img.shields.io/discord/1173896039401521245?style=for-the-badge&logo=discord&logoColor=f5f5f5&label=discord)](https://discord.gg/mH7vgCEFWq)
[![Ko-fi](https://img.shields.io/badge/support_me_through_ko--fi-F16061?style=for-the-badge&logo=kofi&logoColor=f5f5f5)](https://ko-fi.com/kylo)

Make Your Choice is a server region changer for Dead by Daylight. It allows you to play on any server of choice.

<img src="https://i.imgur.com/oJetRV7.png" alt="Main">

# Installation: Windows
## Installation
Download the latest `.exe` file from the [Releases](https://github.com/laewliet/make-your-choice/releases/latest) page and run it as administrator.

## Supported Windows Versions
- Windows 10
- Windows 11

## UAC Popup & SmartScreen Alert
The application needs to be run with [administrator permissions](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/) to ensure the hosts file can be edited. Since I don't want to pay Microsoft a fee for getting this free application signed, you will be met with a prompt to trust the unknown developer.  
<img src="https://i.imgur.com/zpMPDzM.png" alt="Main" height="350"> <img src="https://i.imgur.com/bu62CXd.png" alt="Main" height="350">

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

### Build & Install
Clone and install using Makefile:
```bash
cd ~/ && git clone https://github.com/laewliet/make-your-choice.git
cd make-your-choice/linux/makefile && make install
cd ~/ && rm -rf ~/make-your-choice
```
After installation, the clone will be removed.

## Method 3: Precompiled Binary
This option won't provide desktop entries to easily access the app. Use this only if you have no other options available. No prerequisites are required. Simply download the binary from the [Releases](https://github.com/laewliet/make-your-choice/releases/latest) page and run it.

This option is recommended if you have a SteamOS device.


# Screenshots
## Windows
<img src="https://i.imgur.com/36jV4su.png" alt="Main" height="450"> <img src="https://i.imgur.com/45Gesfc.png" alt="Main" height="450">  
*Screenshots taken on Windows 11 Pro 25H2*
## Linux
<img src="https://i.imgur.com/VlHsxtc.png" alt="Main" height="450"> <img src="https://i.imgur.com/BXZuWkL.png" alt="Main" height="450"> <img src="https://i.imgur.com/xtvswcf.png" alt="Main" height="450">  
*Screenshots taken on Arch Linux with the KDE Plasma Desktop Environment.*
