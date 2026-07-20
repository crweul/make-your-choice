> [!NOTE]
> This repository was previously hosted on Codeberg and has been moved to GitHub on December 17, 2025.  
> You are currently on the new repository, no action is required.  
> ↳ [Visit the old repository](https://codeberg.org/ky/make-your-choice).

> [!IMPORTANT]
> Make Your Choice does <u>**NOT**</u> work reliably with an active VPN connection, or any other tools / services that may be altering the network.

[![GitHub Downloads](https://img.shields.io/github/downloads/crweul/make-your-choice/total?style=for-the-badge&color=6e5494&logo=github&logoColor=f5f5f5&label=downloads+(precompiled))](https://github.com/crweul/make-your-choice/releases)
[![Codeberg Downloads](https://img.shields.io/badge/dynamic/json?style=for-the-badge&logo=codeberg&logoColor=f5f5f5&label=downloads+(1.0.0+RC)&query=$.assets[0].download_count&url=https://codeberg.org/api/v1/repos/ky/make-your-choice/releases/tags/1.0.0-RC)](https://codeberg.org/ky/make-your-choice/releases/tag/1.0.0-RC)
[![Build Status](https://img.shields.io/github/actions/workflow/status/crweul/make-your-choice/build.yml?style=for-the-badge&logo=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAD20lEQVRYR7WXaYiOURTHvfliGJNCmpAlNdb4YAljkiyNpUHJ+oFIItuIL8QYDI1dGSkU2Uuyjd1EBllTCmMb0hAzaYw1H16//3TvdOf1vu/zPK+ZU6fnLv9zzv+5z3PPPTfUwKeEw+FGQMeg49HOaAuj8lBu9CnPE+i5UCj0y4/rkBeIwK3ArEWnokleeDP/g+cBdCVEPseziUuA4LkYL0X19omIiKyHhF4gqkQlQODmoM+g/SOsntA/hV5F36MfzHwqzzboMDQL7RphV0R/HEQqI1n8Q4DgXQCdR9s54Ae0F+Dglp9lwMdgcDvQHg7+Be2h+Hjn+qhFAEO9xR20tQOah1GBn8CRGPwtZmxLBIl++Ptix2oIAE5h8KbDWss7BrDePmHB7yCMT6L6rJLb6BC7S1wCO5mYa0C/eQ4A9DDhyI4hJDLoXkMbmuHV+M5Ru5oAgI48Xjk20wHsr4vg1gcxltDeZPrfeHYgRrklcJiByWaymIn0ugzukFCiUhKTbCVOdghmTel8dQKmM1EciwD4bOaOgikLShJbZVFlSkkFPlqIwCQ6R8zgYwZ7xXMMPsz8dzQP3Qj+j18imGrFP6FK45IMEThEY4oZyMXhKh8ELOQ1DeWHcwFI7AE70+DzReAend5mYKBXsjErEBnvAgPKFyIUV7CfAOC4ARWKQCkdm/Xa4+RtgBVwofoU21BtMX2iqEI8feJHZvKJCAjc2AwkeR2jMVbADabTb2yslcS+GfM2E1aKgHaAdoIkOR57AXwS0METdSdhn4ybKhOvSgR0SHQyA2kYlvzHJ9iObY7HJ1AeUD6QlIjADRrK15LhGF9OgMBFbOZjq5eJK8TLBFBoQEUisJvObDOgfb0sAAEdrQp82iuwnSeejun5pl8gAqrzrIMSnKX5IGAT0WbwOrh8C/GUQVXASDJFQOVWBWp3go5KVTBRBbzO+GMJpuJR2J41jqvwkWIPo30MzjAT95no4/uVAgAhr5KumzHZRZy5loASUanjaxaTewP49oQSfBGgrQ4wlRgf3YJEWWyhAdR3QZJH8OWK5RJQglBJ1tOQqK+S7C7+B0PgZy0C6rBM+jsFUHFqZQ5gbdXAgj/dKfIdwze0++JPN6lqiVaWK1NdQts6hvdp69hVQekpBNb9QNVwdwf8nHYmPkSiRmJdTFqC0BkfuRt0imkbXUHLcPaSYDpHtHIiPAIdido/3Qa6TiMLvPfFxGWH8zX0V3i+cmyAis91BN4QC+LncqpLihxMC0BEmfKgyLvfO5q9JwFrxGo0oT0anYiqjNdFQ7WdakR7PX9GWwWr77PhL8wGiLa1JMgJAAAAEGRlQkdDNzZFNENGNTAxMTcwQTFCfZSh2AAAAABJRU5ErkJggg==)](https://github.com/crweul/make-your-choice/actions)
[![Discord](https://img.shields.io/discord/1173896039401521245?style=for-the-badge&color=5865f2&logo=discord&logoColor=f5f5f5&label=discord)](https://discord.gg/mH7vgCEFWq)
[![Ko-fi](https://img.shields.io/badge/support_me_through_ko--fi-F16061?style=for-the-badge&logo=kofi&logoColor=f5f5f5)](https://ko-fi.com/kylo)

# Make Your Choice
Make Your Choice is a server region changer for Dead by Daylight. It allows you to play on any server of choice.

<img src="https://i.imgur.com/oJetRV7.png" alt="Main">

# Table of Contents
- [Make Your Choice](#make-your-choice)
- [Installation](#installation)
  - [Supported Windows Versions](#supported-windows-versions)
  - [UAC Popup & SmartScreen Alert](#uac-popup--smartscreen-alert)
- [Screenshots](#screenshots)
- [Frequently Asked Questions](#faq)

# Installation
Download the latest `.exe` file from the [Releases](https://github.com/crweul/make-your-choice/releases/latest) page and run it as administrator.

## Supported Windows Versions
- Windows 10
- Windows 11

## UAC Popup & SmartScreen Alert
The application needs to be run with [administrator permissions](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/) to ensure the hosts file can be edited. Since I don't want to pay Microsoft a fee for getting this free application signed, you will be met with a prompt to trust the unknown developer.  
<img src="https://i.imgur.com/zpMPDzM.png" alt="Main" height="350"> <img src="https://i.imgur.com/bu62CXd.png" alt="Main" height="350">

# Screenshots
<img src="https://i.imgur.com/36jV4su.png" alt="Main" height="450"> <img src="https://i.imgur.com/45Gesfc.png" alt="Main" height="450">  
*Screenshots taken on Windows 11 Pro 25H2*

# FAQ
<details>
  <summary><b>Does Make Your Choice work on Steam Deck?</b></summary>
  No. The Linux build has been discontinued; Make Your Choice is currently Windows-only.
  <br>
</details>
<details>
  <summary><b>Does Make Your Choice need to remain open in the background for it to work?</b></summary>
  No. Once your selections are applied, MYC will remain functional even if you close the application.
  <br>
</details>
<details>
  <summary><b>Does Make Your Choice work in my region?</b></summary>
  MYC is mostly incompatible with VPNs. If you live in a country with restricted internet access and rely on a VPN to get online, the program may not function correctly.
  <br>
</details>
