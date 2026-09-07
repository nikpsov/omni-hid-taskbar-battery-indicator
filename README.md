# OmniHID Taskbar Battery Indicator

<div align="center">

**English** | [Русский](README.ru.md)

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6.svg?style=flat-square&logo=windows)](https://microsoft.com)
[![Runtime](https://img.shields.io/badge/.NET-Framework%204.8-512BD4.svg?style=flat-square&logo=dotnet)](#)
[![Dependencies](https://img.shields.io/badge/dependencies-Zero%20(Native%20Win32)-brightgreen.svg?style=flat-square)](https://github.com/)
[![Engine](https://img.shields.io/badge/engine-OmniHID.Core-2ea44f.svg?style=flat-square)](https://github.com/nikpsov/omni-hid)
[![License](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](LICENSE)

*Ultra-lightweight Windows taskbar widget and Fluent Flyout for wireless gaming gear, powered by the [OmniHID](https://github.com/nikpsov/omni-hid) telemetry engine (standalone UI — no separate engine installation required).*

<br/>

![Preview](preview.png)

</div>

---

## Overview

**OmniHID Taskbar Battery Indicator** is a standalone graphical frontend built on top of the [**OmniHID**](https://github.com/nikpsov/omni-hid) telemetry engine (`OmniHid.Core`). It docks beside the Windows system tray to show live battery percentages for your wireless peripherals (`🎧 85%  🖱️ 92%`).

> **Standalone UI**: The telemetry engine is embedded directly into the application. No separate installation of the OmniHID driver, CLI, or engine is required — simply download and run.

Clicking the widget opens a Windows 11-styled **Fluent Flyout** with charging status (`⚡`), estimated runtime, and battery voltage.

- **Ultra-lightweight:** Consumes ~15 MB RAM and ~0% CPU (replaces 500 MB+ bloatware like G HUB, Synapse, and iCUE).
- **Zero dependencies:** Pure C# (.NET 4.8) via native Win32 HID APIs — standalone executable with embedded OmniHID engine, no background services.
- **Gamer-friendly:** Automatically hides in fullscreen games and videos.
- **Smart Dual-Mode:** Detects cable charging without duplicate entries.
- **Low battery alerts:** Toast notification when battery drops to ≤ 20%.

---

## Supported Devices

Supports mice, keyboards, headsets, and gamepads across major brands and chipsets:  
**Logitech** (HID++ & Centurion), **Razer** (HyperSpeed), **Corsair**, **SteelSeries**, **HyperX**, **CompX / SinoWealth / Areson / YiChip MCU** (Lamzu, Pulsar, ARDOR, Akko, etc.), **Sony DualSense**, and **Xbox**.

> For the full list of supported devices and profiles, see the [OmniHID repository](https://github.com/nikpsov/omni-hid).

> [!NOTE]
> **Experimental Profiles & the `Unverified` Badge:**  
> Newly added or experimental profiles are marked with a gold **`Unverified`** badge in the Flyout (and tagged `(unverified)` in the context menu). This indicates the profile has been contributed or generated, but awaits testing on physical hardware.
> - **Device works correctly?** Please open a **[Device Verification Issue](https://github.com/nikpsov/omni-hid/issues/new?template=verify_device.yml)** on the [OmniHID repository](https://github.com/nikpsov/omni-hid) confirming that the battery level and charging/wired status are accurate so we can promote it to verified status!
> - **Device has bugs or incorrect readings?** Please file an issue on [OmniHID Issues](https://github.com/nikpsov/omni-hid/issues) with details or diagnostic dumps so we can fix the profile.

---

## Installation

- **Installer:** Download `omni-hid-taskbar-setup.exe` from [Releases](https://github.com/nikpsov/omni-hid-taskbar-battery-indicator/releases).
- **Portable:** Download the `.zip` archive from [Releases](https://github.com/nikpsov/omni-hid-taskbar-battery-indicator/releases) and run `OmniHidTaskbar.exe`.

### Build from Source

No SDK needed — compiles with the built-in Windows C# compiler (`csc.exe`):

```cmd
git clone --recursive https://github.com/nikpsov/omni-hid-taskbar-battery-indicator.git
cd omni-hid-taskbar-battery-indicator
build.bat
```

Output is saved to `bin\`. To update protocols from upstream: `git submodule update --remote --merge`.

---

## Configuration & Storage

Settings and device catalogs are stored in `%APPDATA%\OmniHid` (or locally in the application folder in portable mode):
- **Settings:** `%APPDATA%\OmniHid\settings.json`
- **Device Profiles:** `%APPDATA%\OmniHid\devices\` (`verified/` and `unverified/`)

Right-click the widget or edit `settings.json`:

| Setting | Default | Description |
|---|---|---|
| `DisplayStyle` | `0` | `0` = Icon + percent (`🎧 85%`), `1` = Battery icon only |
| `DisplayMode` | `0` | `0` = Taskbar overlay widget, `1` = System tray icon only |
| `HideWhenDisconnected` | `true` | Hide widget when all devices are sleeping or offline |
| `RunOnStartup` | `false` | Start automatically with Windows |
| `PollIntervalSeconds` | `15` | Polling interval in seconds on desktop |
| `BackgroundPollIntervalSeconds` | `300` | Throttled polling interval in seconds in fullscreen games/lock screen |

---

## Controls

- **Left-Click:** Open / close detailed Fluent Flyout (shows device battery status, voltage, and `Unverified` badges for experimental models).
- **Right-Click:** Open Fluent context menu:
  - Toggle display style and mode (Widget vs Tray icon).
  - Manage individual device visibility (`Device Visibility` submenu).
  - **Update Profiles from GitHub:** Over-The-Air (OTA) synchronization of the latest device profiles directly from GitHub without restarting or reinstalling.
  - Refresh device info and toggle run on startup.

---

## FAQ

<details>
<summary><b>Why doesn't the battery level change while my mouse is idle?</b></summary>
Wireless mice enter deep sleep after a few minutes of inactivity to conserve battery, turning off their RF radio. The widget displays the last known percentage until the mouse is moved.
</details>

<details>
<summary><b>How do I enable debug logging?</b></summary>
Run <code>bin\OmniHidTaskbarDebug.exe</code> or launch the app with <code>--debug</code>. Logs are printed to the console and saved to <code>debug.log</code> (auto-rotated at 1 MB).
</details>

<details>
<summary><b>Is it safe with anti-cheat software?</b></summary>
Yes. OmniHID uses standard user-mode Win32 HID APIs (<code>CreateFile</code>, <code>HidD_GetFeature</code>). It does not inject DLLs, hook game memory, or use kernel drivers.
</details>

---

## License

OmniHID Taskbar Battery Indicator is open-source software released under the [MIT License](LICENSE).

---

## Support & Donations

If you find this project useful and wish to support its development:

- **CloudTips:** [https://pay.cloudtips.ru/p/0886f5f7](https://pay.cloudtips.ru/p/0886f5f7)
- **USDT (TRC20 / TRON):** `TQpX2ogcFtb1pAnhKrnEioEc6wLZRCh7nv`
- **USDT (ERC20 / ETH):** `0x6cffDEA7b163850229610CA5Bbe9769E7F07F9e8`
- **GRAM (TON):** `UQDQpCjTeWYPDu0E8IcbojydBFeVHfRxzfOFFdxxw4gfYKq6`
