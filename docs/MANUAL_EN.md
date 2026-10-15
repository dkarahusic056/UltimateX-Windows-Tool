# ULTIMATE X Windows Control – User Manual

**Version 1.3.1** · for Windows 10 (1809 or later) and Windows 11, 64-bit
*Your Complete Windows Control Center*

This manual describes every page of the application in navigation order and walks you through the most important tasks step by step. Page and button names are given as they appear in the English user interface. The German version is [HANDBUCH.md](HANDBUCH.md).

---

## Contents

1. [Introduction](#1-introduction)
2. [Getting around](#2-getting-around)
3. [The pages of the navigation](#3-the-pages-of-the-navigation)
4. [Backing up and restoring your PC](#4-backing-up-and-restoring-your-pc)
5. [Games: Can My PC Run It and online requirements](#5-games-can-my-pc-run-it-and-online-requirements)
6. [In-game overlay](#6-in-game-overlay)
7. [Benchmarks and Performance Score](#7-benchmarks-and-performance-score)
8. [Scheduled shutdown](#8-scheduled-shutdown)
9. [Troubleshooting your PC](#9-troubleshooting-your-pc)
10. [FAQ and problem solving](#10-faq-and-problem-solving)
11. [Appendix](#11-appendix)

---

# 1 Introduction

## 1.1 What is ULTIMATE X?

ULTIMATE X Windows Control is a Windows application that brings hardware information, monitoring, diagnostics, repair, networking, security, backup and gaming tools together in one place. It shows what Windows and your hardware actually report, explains it in plain language and offers safe tools to fix problems.

Three principles apply throughout the application:

| Principle | What it means for you |
|---|---|
| **No made-up values** | Anything that cannot be measured is shown as **N/A**. Nothing is guessed. |
| **Estimates are labelled** | Values based on databases or typical figures (e.g. FPS estimates, performance classes, expected values) are marked **ESTIMATED**. |
| **Least privilege** | The app runs without administrator rights. For actions that need admin rights, Windows asks you via User Account Control (**UAC**). ULTIMATE X never bypasses UAC. |

You additionally confirm sensitive actions (e.g. installing drivers, revealing sensitive data, turning off the report's privacy mode) with **Windows Hello** – or with an explicit confirmation if Hello is not set up.

## 1.2 System requirements

| Requirement | Value |
|---|---|
| Operating system | Windows 10 64-bit version 1809 (build 17763) or later, or Windows 11 64-bit |
| .NET | not required – the application ships its own runtime (self-contained) |
| Rights | a standard user account is enough; admin rights only for individual actions via UAC |
| Optional: Microsoft Edge WebView2 Runtime | only for the embedded speedtest.ch (normally preinstalled on Windows 11) |
| Optional: HWiNFO or LibreHardwareMonitor | for real CPU core temperatures, CPU/GPU clocks and power draw |
| Optional: Debugging Tools for Windows (Windows SDK) | for cause analysis of crash dumps |
| Optional: Geekbench 5 or 6 | only if you also want to compare with Geekbench |

## 1.3 Installation (MSI)

1. Double-click `UltimateX-WindowsControl-1.3.1-x64.msi`.
2. Read and accept the End User License Agreement (see [Appendix C](#c-license)).
3. In the setup wizard you can change the installation folder and select or deselect the **desktop shortcut**. Default folder: `C:\Program Files\ULTIMATE X Windows Control\`.
4. Confirm the Windows prompt (UAC) – installing into *Program Files* requires admin rights.
5. After installation you will find **ULTIMATE X Windows Control** in the Start menu.

**Notes**
- The Start menu shortcut is always created. It is also required for Windows notifications (toasts) to appear.
- An older version is replaced automatically during installation (upgrade). A **downgrade** is blocked: the MSI then reports that a newer version is already installed.
- The MSI checks for 64-bit Windows build 17763 or later and otherwise stops with a message.

## 1.4 Uninstalling

1. Windows Settings → **Apps** → **Installed apps** (Windows 10: *Apps & features*).
2. **ULTIMATE X Windows Control** → **Uninstall**, confirm UAC.

Program files, shortcuts and the folder `%ProgramData%\UltimateX` are removed. **Your personal data is kept:** settings, histories and logs under `%LOCALAPPDATA%\UltimateX`, PDF reports and game sessions under `Documents\ULTIMATE X`, and your backups on external drives. Delete these folders yourself if you no longer need them (see [Appendix B](#b-file-locations)).

## 1.5 First start

On the first start the window **Welcome to ULTIMATE X** appears. It briefly explains what the application is now detecting (system, hardware, network adapters, installed software, startup entries, security and Windows status), that it runs without admin rights, that sensitive data is masked and that there is no telemetry. Confirm with **OK** – the Dashboard loads the first values in the background.

At every start ULTIMATE X also checks its own digital signature. If program files have been modified (invalid signature), the app does not start for security reasons and asks you to reinstall it.

## 1.6 Language and appearance

Everything is under **Settings → Appearance**:

| Setting | Options |
|---|---|
| **Color scheme** | System (follows Windows), Dark, Light |
| **Layout** | Modern Control Center (default), Gaming Mode, Technician Mode, Visual PC Map, Simple Mode, Admin / Service Mode |
| **Language** | Automatic (Windows display language: German Windows → German, otherwise English), German, English |

The language is applied after the app restarts – use the **Restart app** button. A few rare messages may remain in German in the English interface.

## 1.7 Privacy

**ULTIMATE X sends no telemetry.** There are no usage statistics, no account and no cloud. All data (settings, logs, histories, backups) stays on your PC.

Internet access **only happens when you trigger a function that needs it** – with one exception that you can turn off: the setting *Check for updates in the background at startup (Windows Update, winget, ULTIMATE X)* is on by default and searches for updates about 45 seconds after start.

### All internet access at a glance

| Function (page) | Destination | What is sent |
|---|---|---|
| Update check at startup (Settings, can be turned off) and the **Updates** page | Windows Update (Microsoft), winget source (Microsoft), publisher's update manifest (only if an update source is entered) | standard Windows/winget requests; manifest via HTTPS |
| **Query public IP** (Network) | `api.ipify.org`, `api6.ipify.org` | nothing except the request itself |
| **Analyze network** (Network) | `www.microsoft.com` (reachability, DNS test), ping to `1.1.1.1` and `8.8.8.8` | – |
| **Start speed test** (Speedtest, Dashboard, network benchmark) | `speed.cloudflare.com` | test data, no user data |
| **speedtest.ch (embedded)** (Speedtest) | `www.speedtest.ch` – only after you switch to this area | whatever the web page itself requests |
| Component tests "Internet", "DNS", "System time" | `1.1.1.1`, `8.8.8.8`, `www.microsoft.com`, `www.cloudflare.com`, `time.windows.com`, `pool.ntp.org` | – (the time is only read) |
| **Load vendor list (IEEE)** (Network scanner) | `standards-oui.ieee.org` | – |
| **Trace route**, **Check ports** (Network Tools) | only the target you enter | ping or connection attempt, no data |
| Overlay line "Ping" with target *Internet (1.1.1.1, Cloudflare)* | `1.1.1.1` (the default is the router – stays in your home network) | ping every 3 s, only while the overlay is visible |
| **Search online**, **Load missing requirements online** (Can My PC Run It) | `store.steampowered.com` | only the game name or Steam AppID |
| **Search requirements on the web**, **FIND GAMES/SOFTWARE FOR MY PC** | opens `www.google.com` in your default browser | search term built from hardware names – ULTIMATE X fetches nothing itself |
| **Open official download page** (Benchmarks → Geekbench) | opens `www.geekbench.com/download/` in your browser | – |
| **Download WebView2 Runtime from Microsoft** (Speedtest) | opens `developer.microsoft.com` in your browser | – |
| New PC Setup | winget source/Microsoft Store, official vendor domains, `officecdn.microsoft.com` (Office Deployment Tool) | package ID; download directly from the vendor |
| Open Source Security | `api.github.com` (ClamAV project check), `database.clamav.net` (signatures) | anonymous requests |
| **Find cause** (Crash analysis, with Microsoft debugger) | Microsoft symbol server `msdl.microsoft.com` | the debugger's symbol requests; the crash dump never leaves your PC |

**Local network only (no internet):** Network scanner (ping/ARP within your own private subnet), Wake-on-LAN (broadcast into your own subnet), the UPnP query of the Security Check (SSDP multicast to the router), overlay ping to the router.

**Not done by ULTIMATE X, but good to know:** the free Geekbench version uploads its results to the internet by itself. The notification-area monitoring never accesses the internet.

---

# 2 Getting around

## 2.1 Main window

- **Navigation (left):** all pages, arranged in groups (see chapter 3).
- **Header (HUD):** live values for CPU, GPU, RAM, temperatures and network, the time and the Windows version; plus the user and whether the app runs as a standard user or as *Administrator*.
- **Search box:** opens the first page whose name or keyword matches (e.g. "smart", "driver", "fps"). **Ctrl+K** jumps straight into it, **Enter** searches.
- **F5** reloads the current page.
- **LOCK:** immediately ends the security session; sensitive data is masked again and sensitive actions require a new confirmation.

## 2.2 Status colors and labels

| Display | Meaning |
|---|---|
| Green / OK / PASS / GOOD | all right |
| Yellow / WARNING | note, take a closer look |
| Red / FAIL / CRITICAL / problem | action required |
| **N/A** / NOT AVAILABLE | no measurement available – nothing is estimated |
| **ESTIMATED** | estimate from a local database or a typical value |
| **UAC** on a button | Windows will ask for administrator rights |

## 2.3 Administrator rights (UAC)

All actions with admin rights run through the signed helper `UltimateX.AdminHelper.exe` with a fixed list of allowed commands. Windows shows the UAC prompt for them. If you decline, the action is **not** carried out and the page tells you so. There is no free command input.

---

# 3 The pages of the navigation

The navigation is divided into groups by separator lines. The table lists all pages in order; the group names only help you find your way in this manual and do not appear in the app.

| Group | Pages |
|---|---|
| Start | Dashboard |
| Hardware | Hardware · BIOS / UEFI · Live Monitor · Sensor sources · Benchmarks · Memory Test · Storage · PC Lifetime · Component test |
| Network | Network · Speedtest · Network scanner · Network Tools |
| Gaming | Gaming Check · Can My PC Run It · Upgrade Advisor · Overlay |
| Software & maintenance | New PC Setup · Updates · Drivers · Programs · Startup · Scheduled Tasks · Processes · Clean up storage · Disk Space Analysis · Desktop Organizer |
| Diagnostics | Windows Health · Crash analysis · Timeline · Windows Logs · Services · System Snapshot |
| Backup | Backup Center |
| Security | Security Center · Security Check · Privacy · Open Source Security |
| Tools | Technician Mode · Tools & Power · Logs · PC report |
| Settings | Settings |
| Layouts | Gaming Mode · Visual PC Map · Admin / Service Mode |

*New in version 1.3:* PC Lifetime, Network Tools, Overlay, Scheduled Tasks, Disk Space Analysis, Timeline, Backup Center, Security Check, Privacy.

---

## 3.1 Dashboard

**Purpose:** The start page shows the state of your PC at a glance and offers the most important actions.

**Main functions**
- **SYSTEM HEALTH** with **HEALTH SCORE**, PC status, operating system, uptime, last check and rating.
- Ring gauges for CPU, GPU, RAM, VRAM, storage and network, **TEMPERATURES**, **LIVE PERFORMANCE · 2 MIN**, cards for security, Windows health, network (last speed test) and updates.
- Actions: **RUN PC CHECK** (complete system check, read-only), **TEST COMPONENTS**, **NETWORK TEST**, **SECURITY CHECK**, **EXPORT REPORT**, **GAME MODE**.
- **HEALTH SCORE · DETAILS:** starts at 100, every deduction is shown with its reason; values that cannot be measured are not included.
- **QUICK ACCESS:** Check Windows, Cleanup, Organize desktop, Empty recycle bin, CMD, PowerShell, Lock PC, Restart, Sleep, Shut down.
- **Raw data (TXT/JSON) …** exports the collected data; sensitive data only after confirmation.

**Step by step – check the whole PC**
1. Open the Dashboard.
2. Click **RUN PC CHECK**. The check steps appear under **COMPLETE PC CHECK** with their status.
3. Read the result; clicking a card takes you to the matching detail page.
4. Optionally use **EXPORT REPORT** to create a PDF report.

**Notes:** The health score is guidance, not a guarantee. The SFC/DISM check within the PC check requires admin rights (UAC).

## 3.2 Hardware

**Purpose:** Complete hardware overview – only components that Windows actually reports.

**Main functions:** Windows (edition, version, build), CPU, GPU (with exact VRAM), RAM modules, mainboard/BIOS/UEFI/TPM/Secure Boot, **Monitors** (resolution, Hz, HDR, scaling, primary monitor, GPU), **Drives**, battery health, **System information**, **Hardware topology**.

**Steps**
1. Open the page – detection runs automatically. **Detect again** repeats it.
2. For temperature, power-on hours, lifetime and errors of the drives click **Read SMART details (admin rights)** and confirm UAC.

**Notes:** Serial numbers, MAC and IP addresses are masked; revealing them requires confirmation. **Limits:** Used/free PCIe and M.2 slots cannot be determined reliably via Windows and are not shown.

## 3.3 BIOS / UEFI

**Purpose:** Show and explain firmware and security features – **read-only**.

**Functions:** UEFI/Legacy, Secure Boot, TPM, virtualization (VT-x/AMD-V), memory integrity/VBS, Hyper-V, Resizable BAR, BIOS age – each with an explanation.

**Notes:** ULTIMATE X never changes the BIOS/UEFI. Only make changes following the instructions of your mainboard or PC manufacturer. **Limits:** Resizable BAR cannot be read reliably without vendor tools (N/A).

## 3.4 Live Monitor

**Purpose:** Follow utilization live.

**Functions:** CPU, GPU, RAM, VRAM, disks, download/upload as history; period **60 seconds**, **5 minutes** or **15 minutes**; **Sensors** list.

**Steps:** Open the page and choose the **Period** at the top. **Real CPU temperatures?** explains how to obtain CPU core temperatures via HWiNFO or LibreHardwareMonitor.

**Limits:** Without HWiNFO/LibreHardwareMonitor there are no CPU core temperatures (ULTIMATE X deliberately loads no kernel driver). GPU temperature requires a current graphics driver (WDDM 2.4+).

## 3.5 Sensor sources

**Purpose:** Shows where sensor values come from and automatically picks the best source.

**Functions:** status of the sources **HWiNFO** (shared memory), **LibreHardwareMonitor** (WMI), **Windows** (graphics kernel, performance counters) and **ACPI** with automatic failover; **UNIFIED SENSOR MODEL** with current/min/avg/max/source/as of; **SENSOR DETAILS** for the selected sensor; **Reset min/avg/max**; **Setup** (help for HWiNFO/LibreHardwareMonitor).

**Steps – set up real CPU temperatures**
1. Start HWiNFO and enable **Shared Memory Support** in its settings (or start LibreHardwareMonitor as administrator).
2. In ULTIMATE X open **Sensor sources** – the source shows as active and the CPU values carry their source.

**Notes:** Statistics (min/avg/max) run since the program started. ACPI thermal zones are shown as "mainboard", never as CPU temperature.

## 3.6 Benchmarks

**Purpose:** Measure CPU, GPU, RAM, drive and network performance and compare it with earlier runs; **ULTIMATE X PERFORMANCE SCORE**; record Geekbench results.

Described in detail in [chapter 7](#7-benchmarks-and-performance-score).

## 3.7 Memory Test

**Purpose:** Check your RAM for errors.

**Functions**
- **RAM STABILITY TEST** in three levels:

| Mode | Scope | Duration (approx.) |
|---|---|---|
| Quick Memory Test | 25 % of free memory · 3 patterns · 1 pass | 1–3 minutes |
| Standard Memory Test | 50 % of free memory · 8 patterns · 2 passes | 5–15 minutes |
| Extended Memory Test | 70 % of free memory · 8 patterns · 4 passes | 15–45 minutes |

- Error list **ERRORS (ERROR ADDRESS)** with address, pattern, pass, expected/read value and number of flipped bits; result PASS/WARNING/FAIL.
- **BOOT MEMORY TEST:** schedules Windows Memory Diagnostic for the next restart (tests almost all RAM outside Windows) and shows its results afterwards.
- **RAM CONFIGURATION:** type, channels, current clock compared with rated clock, XMP/EXPO hint.

**Steps**
1. Choose a mode and click **Start test**. **Cancel** stops it at any time.
2. For the thorough test click **Schedule boot test …**, confirm and restart the PC. The result appears after the restart under **BOOT MEMORY TEST – RESULTS**.

**Limits:** The in-app test only checks free memory inside running Windows. The addresses shown are virtual addresses; to find a faulty module, test modules one at a time.

## 3.8 Storage

**Purpose:** Drive health (SMART) and usage.

**Functions:** condition **GOOD / WARNING / CRITICAL / N/A** per drive with reasons (wear, temperature, uncorrectable errors, power-on hours, power cycles), usage per volume (**VOLUMES**).

**Steps:** Open the page; for complete SMART values click **Read SMART (UAC)**. The values are cached and also used by *PC Lifetime* and the notification-area monitoring.

## 3.9 PC Lifetime

**Purpose:** How long and how often your PC and its drives have been running – measured values only.

**Steps**
1. Open **PC Lifetime**. The values load automatically (evaluating the event log may take a few seconds).
2. If drive values are missing, click **Read SMART (UAC)**. These are the same cached values as on the **Storage** page.

**What is shown**
- **Per drive:** power-on hours, power cycles, wear (remaining life), condition.
- **PC / Windows:** Windows installation date, uptime since the last boot, number of starts and total runtime according to the system event log ("at least …, since <date>"), unexpected shutdowns.
- **Own runtime log:** ULTIMATE X records a timestamp at every start and exit and every 5 minutes (local only).
- **CPU, GPU, RAM:** "N/A – no hour counter" – these components have no readable operating-hours counter.

**Notes and limits**
- Data written/read (TBW) is not provided by Windows across vendors (N/A) – use your drive manufacturer's tool.
- Windows resets the installation date with major feature updates.
- The event log has a limited size, hence "at least".
- Starts from fast startup or hibernation do not count as a start; sleep periods do not count as runtime. After a crash or power failure only the time up to the last sign of life is counted.
- The own log only counts times when ULTIMATE X was running.

## 3.10 Component test

**Purpose:** *System Component Test Center* – more than 40 safe, non-destructive checks (CPU, RAM, GPU, VRAM, drives, mainboard, BIOS/UEFI, TPM, Secure Boot, network, Bluetooth, USB, audio, camera, monitors, input devices, battery, sensors, services, Windows Update, Defender, firewall, event log, WMI, printers, system time, WinRE and more).

**Functions:** **TEST ALL SYSTEM COMPONENTS**, single tests, status PASS / WARNING / FAIL / NOT AVAILABLE / USER ACTION, **INTERACTIVE TESTS** (PLAY TEST SOUND, MICROPHONE LEVEL START / STOP, CHECK CAMERA, DISPLAY COLOR TEST, KEYBOARD & MOUSE TEST, PRINT WINDOWS TEST PAGE, WINDOWS MEMORY DIAGNOSTIC), optional **WRITE TEST**, **TEST HISTORY**, **Export report (TXT/JSON)**.

**Steps**
1. Click **TEST ALL SYSTEM COMPONENTS**; **Cancel** is possible.
2. View findings with WARNING/FAIL via **DETAILS**.
3. Start interactive tests individually only when needed.

**Notes:** By default only read-only checks run. Interactive tests record and store nothing. Camera and microphone require the Windows privacy permission for desktop apps. **Limits:** no GPU render/stress test; the WinRE status may be NOT AVAILABLE without admin rights.

## 3.11 Network

**Purpose:** Check network adapters and the connection.

**Functions:** **Network adapters** (IPv4/IPv6, gateway, DNS, DHCP, MAC – masked, **Copy MAC**), **WLAN** (SSID, signal, band, channel, Wi-Fi standard, rates, security), **Network diagnostics**, **Ping** (gateway, 1.1.1.1, 8.8.8.8; Ping (10×) with minimum, maximum, average, packet loss), **Test DNS**, **Query public IP**.

**Step by step – narrow down a connection problem**
1. Click **Analyze network**. The diagnosis checks adapter, gateway, DNS and internet step by step and shows where the problem is.
2. If needed, **Test DNS** (A query www.microsoft.com).
3. Continue with the [Network Tools](#314-network-tools) (flush DNS cache, reset network).

**Notes:** The public IP is only queried on request via ipify.org (HTTPS). **Limits:** From Windows 11 24H2, WLAN details require the "Location access for apps" permission.

## 3.12 Speedtest

**Purpose:** Measure internet speed and compare the history.

**Functions**
- **Own speed test (Cloudflare):** download, upload, ping, jitter, packet loss, speedometer; **Last result**, **Comparison** of the last 7 and 30 days, **History** with source.
- **speedtest.ch (embedded)** (new in 1.3): run the speedtest.ch test right inside ULTIMATE X and add the result to the history.

**Steps – own speed test**
1. Click **Start speed test** (can be cancelled).
2. Read the result and the comparison.

**Steps – speedtest.ch**
1. Select **speedtest.ch (embedded)** at the top. Only now is www.speedtest.ch loaded.
2. Start the test on the web page.
3. On the right under **Apply result**, type in download and upload (Mbit/s), ping (ms) and optionally jitter (ms) → **Apply**.
4. The entry appears in the **History** with the source "speedtest.ch (manual)" and is included in the 7/30-day comparison.

**Notes**
- Only speedtest.ch pages are allowed in the embedded window. Links to other sites (e.g. ads), pop-ups and downloads are blocked and shown as a notice.
- ULTIMATE X does **not** read the web page – you transfer the values yourself.
- Saved passwords, form autofill and location/camera/microphone access are disabled. Browser data (e.g. speedtest.ch cookies) is stored separately from your Edge profile under `%LOCALAPPDATA%\UltimateX\WebView2`.
- If no jitter is entered, the history shows "N/A".

**Limits:** Requires the Microsoft Edge WebView2 Runtime. If it is missing, a notice appears with the button **Download WebView2 Runtime from Microsoft**. Content and data usage of the test are determined by speedtest.ch; its terms apply.

## 3.13 Network scanner

**Purpose:** Which devices are in my home network?

**Functions:** **Scan network** (IP, MAC, manufacturer, name, device type), new devices are marked **NEW**, **Custom label**, per device **PING** (4 pings), **LIVE PING** (continuous ping with live chart, STOP ends it), **DETAILS**, **SERVICES** (check common services, on click only), **COPY IP**, **COPY MAC**, **Web interface**; **QUICK PING** for an IP, host name or local name (e.g. `fritz.box`, `printer.local`); **Load vendor list (IEEE)**; **Forget known devices**.

**Steps**
1. Click **Scan network** (confirm the notice the first time).
2. Select a device and use **PING**, **LIVE PING** or **SERVICES**.
3. Optionally give it a custom label and **Save**.

**Notes:** Only your own private network is scanned (e.g. 192.168.x.x), at most 1024 addresses. The port check is not an attack tool. Many devices do not answer ping – "not reachable" does not mean "off". The live ping ends when you leave the page. Devices found here are available for **Wake-on-LAN**.

## 3.14 Network Tools

**Purpose:** Small tools for typical network problems in one place. All output appears in the **RESULT** area and can be copied with **Copy**.

### Traceroute (trace route)
1. Enter a host name or IP address (e.g. `example.com` or `1.1.1.1`).
2. Optionally turn off **Resolve router host names** to make it faster.
3. Click **Trace route**. Each line shows one station (hop) with three response times.

"*" means a router does not answer – often intentional and not an error. At most 30 hops; after 5 silent hops in a row the trace stops. Measurement uses ping (ICMP), which some networks block.

### Port check
1. Enter the target (host name or IP).
2. Enter ports separated by commas (e.g. `80, 443, 3389`) or choose **Insert common services**.
3. Click **Check ports**. Result per port: **open**, **closed** or **no reply (filtered)**.

Only a connection attempt is made; no data is sent. Deliberately **not a port scanner**: one target, at most 20 individual ports, no ranges. Only check your own devices or servers you are authorized to test.

### Flush DNS cache / Renew IP address
- **Flush DNS cache** helps when a website still resolves to its old address after a change. No admin rights needed.
- **Renew IP address** requests new addresses from the router; the connection is briefly interrupted.

### Change DNS servers
1. Choose the **Adapter** (shown: current DNS servers, manual or automatic).
2. **New DNS server**: Cloudflare (1.1.1.1), Quad9 (9.9.9.9), Google (8.8.8.8) or **Automatic (DHCP)**.
3. Optionally **Also change IPv6**.
4. Click **Apply DNS servers (UAC)** and confirm.

Windows asks for admin rights for each address separately (2 to 4 prompts). If you decline one, the setting may only be partly changed – then choose a provider or **Automatic (DHCP)** again. There is only the fixed provider list, no free input.

### Reset network
Resets the Winsock catalog and TCP/IP settings to Windows defaults – helpful after uninstalling VPN, proxy or security software.
1. Click **Reset network …** and confirm the warning (admin rights).
2. **Restart** the PC afterwards.

**Caution:** Manually entered IP addresses are lost; VPN/proxy software may need to be set up again.

### Wake-on-LAN
1. Choose a **Known device (from the network scanner)** or type the **MAC address** (`AA:BB:CC:DD:EE:FF`).
2. Click **Wake up**.

The "magic packet" is only broadcast into your own home network. The target device must have Wake-on-LAN enabled in BIOS/UEFI and in the network driver and is usually connected by cable. No waking over the internet.

## 3.15 Gaming Check

**Purpose:** Is this PC optimally set up for gaming?

**Functions:** **GAMING SCORE** and findings on hardware, graphics driver age, Game Mode, HAGS, Resizable BAR, power plan, temperatures, background processes, ping, Secure Boot/TPM (for anti-cheat). Buttons open the matching Windows settings (**Game Mode**, **Graphics settings (HAGS)**, **Power settings**) or **Can My PC Run It**; **Check again** repeats the check.

**Notes:** CPU/GPU classes are estimates (ESTIMATED). ULTIMATE X does not change any settings automatically.

## 3.16 Can My PC Run It

**Purpose:** Compare game requirements with this PC, estimate FPS, rate installed games; since 1.3 load requirements online from the Steam store.

Described in detail in [chapter 5](#5-games-can-my-pc-run-it-and-online-requirements).

## 3.17 Upgrade Advisor

**Purpose:** Is an upgrade worthwhile – and which one?

**Functions:** **RECOMMENDED UPGRADE** (only if a measurement or finding supports it), **NO PURCHASE NEEDED – CHECK FIRST** (free measures such as XMP, RAM channels, power plan, drivers, PCIe slot), **ANALYSIS**.

**Steps:** First run a benchmark (**Open benchmarks**), then open the page again. Without a benchmark there is no upgrade recommendation.

## 3.18 Overlay

**Purpose:** In-game overlay with FPS and performance values, recording of game sessions and background monitoring (notification area).

Described in detail in [chapter 6](#6-in-game-overlay).

## 3.19 New PC Setup

**Purpose:** Set up a freshly installed PC safely – from official sources only.

**Functions**
- **BEFORE INSTALLATION – CHECKS:** internet, disk space, pending restart, Windows Update status, Defender, winget.
- **PROFILE & SOFTWARE:** profiles BASIC / OFFICE / GAMING / DEVELOPER / CUSTOM, **RECOMMENDED FOR THIS PC**, **Select recommended**, **Clear selection**; programs that are already installed are detected.
- **GRAPHICS DRIVER:** detects the graphics card. For **NVIDIA**, ULTIMATE X automatically looks up the latest matching Game Ready driver (WHQL) at NVIDIA and shows the installed and latest version. If it is newer, it is pre-selected; use **Install NVIDIA driver now** or install it together with **Install selected** / **SET UP THIS PC**. Downloads only from `download.nvidia.com` (HTTPS); the “NVIDIA Corporation” signature is checked before the NVIDIA installer opens (UAC, choose “Express”). **AMD:** **AMD driver download** button (AMD auto-detect). **Intel:** “Intel Driver & Support Assistant” from the list.
- **MICROSOFT OFFICE** (Microsoft 365 Apps, 64-bit, via the Office Deployment Tool) directly above the program list, with a choice of Word, Excel, PowerPoint, Outlook, OneNote, Access.
- **Programs** include browsers (Firefox, Chrome, Brave), OneDrive, WinRAR, 7-Zip, VLC, Adobe Reader, LibreOffice, hardware tools (HWiNFO, CPU-Z, GPU-Z, CrystalDiskInfo/-Mark), game platforms (Steam, Epic, GOG, EA app, Ubisoft Connect, Battle.net), peripherals (Logitech G HUB, Corsair iCUE), tools (Notepad++, Everything, ShareX, PowerToys), media (Spotify, GIMP, Paint.NET, Audacity, HandBrake, OBS), security (Bitwarden, KeePassXC, Malwarebytes), TeamViewer and developer tools.
- **Install selected**, **Firefox as default browser …**, **SET UP THIS PC** (sequence from hardware scan to report; optionally ClamAV and Complete Benchmark), **INSTALLATION LOG**, **FIND SOFTWARE FOR MY PC**.

**Steps**
1. Wait for the checks or click **Check again**.
2. Choose a profile and **Apply profile**, or tick items individually.
3. **Install selected**; each installation appears in the log with source, version, SHA-256 and result.

**Notes:** Nothing is installed without your selection. Before every installation, HTTPS, vendor domain, publisher, version and SHA-256 are checked; portals, mirrors, file hosters, cracks and activators are blocked. Office is activated with your Microsoft account or valid license – no license circumvention. **Limits:** requires winget ("App Installer" from the Microsoft Store).

## 3.20 Updates

**Purpose:** Driver, Windows and software updates as well as updates of ULTIMATE X itself.

**Functions:** **Driver updates (Windows Update)**, **Windows updates**, **Software updates (winget)** with **Update all**, the **ULTIMATE X** area (**Check for app update**, **Install update**), **Check everything again**, **Open Windows Update**.

**Step by step – install a driver update**
1. Click **Search** in the driver updates area.
2. Click **Install** on the desired update, confirm with Windows Hello and allow UAC. A restore point is requested first.

**Notes:** Only drivers signed by Microsoft and released for this PC's hardware ID. Rollback: Device Manager → Properties → Driver → "Roll Back Driver". App updates are only downloaded via HTTPS and installed after SHA-256 and signature checks (same certificate); without an update source there is no app update check, and unsigned builds never update themselves.

## 3.21 Drivers

**Purpose:** All drivers with version, date, provider and signature – plus the **DRIVER HEALTH CENTER** (devices with error codes, unknown devices, unsigned and very old drivers, graphics driver age).

**Functions:** filter by device/manufacturer/class, **Important device classes only**, **Search for driver updates** (column **New version** from Windows Update), **Windows Update**, **Device Manager**.

**Notes:** Driver updates come exclusively via Windows Update. For the newest graphics drivers use the official NVIDIA, AMD or Intel websites.

## 3.22 Programs

**Purpose:** Installed programs with publisher, version, size, installation date and scope.

**Functions:** filter, **Open install folder**, **Uninstall …** (opens Windows "Apps & features").

**Note:** For security reasons ULTIMATE X never runs uninstall commands of other programs itself.

## 3.23 Startup

**Purpose:** Enable or disable programs that start with Windows – just like in Task Manager.

**Functions:** columns Active, Name, system-relevant, Publisher, Signature, Source, Path; **Restore last backup**, **Open backups**.

**Steps:** Tick or untick the **Active** column. Entries for all users require UAC.

**Notes:** Entries are never deleted, only enabled/disabled. A backup is created before every change.

## 3.24 Scheduled Tasks

**Purpose:** Shows all tasks of the Windows Task Scheduler with path, status, trigger, last/next run, result, program and its digital signature. This lets you spot hidden autostarts and suspicious programs.

**Markers**

| Marker | Meaning |
|---|---|
| Microsoft-signed / Known publisher | the program is validly signed |
| Windows folder (catalog signature not checked) | Windows system file without an embedded signature (normal) |
| Unsigned | no or invalid signature – take a closer look |
| Program missing | the task points to a file that no longer exists (often leftovers of uninstalled software) |
| Path in Temp/AppData | the program is in a user-writable folder – typical for malware, but also for some updaters |
| No program (e.g. COM handler) | the task does not start a file |

**Steps**
1. Open the page; the list loads (a few seconds).
2. Narrow it down with the **filter**, **Hide Microsoft tasks** and **Noticeable only**.
3. Select a task and click **Disable …** or **Enable …**; confirm the prompt (admin rights).

**Notes:** Nothing is ever deleted. For tasks under `\Microsoft\Windows\` an additional warning appears, because disabling them can affect maintenance, updates or security features. **Limits:** Tasks of other users or protected folders may be invisible without admin rights ("Unreadable folders"). Very long task paths (over about 200 characters) cannot be changed for security reasons.

## 3.25 Processes

**Purpose:** Running processes with PID, CPU, RAM, publisher, signature and path.

**Functions:** filter by name, path or publisher, **Verify signatures**, open folder.

**Note:** Display only. A missing signature does not mean a process is malicious – use Windows Security for an assessment.

## 3.26 Clean up storage

**Purpose:** Free up space in fixed locations that Windows defines as temporary.

**Categories:** Temporary files (user), Temporary files (Windows), Thumbnail cache, Windows error reports, DirectX shader cache, Recycle bin.

**Steps:** Tick categories, check the preview (file count, size, paths), **Clean selected**. **Analyze again** refreshes, **Windows Storage Sense** opens the Windows setting.

**Notes:** Documents, Desktop, Downloads and Pictures are never affected. Locked files are skipped. Emptying the recycle bin requires confirmation.

## 3.27 Disk Space Analysis

**Purpose:** Find out what is using your disk space: largest folders, largest files, duplicate files and file types.

**Steps**
1. Open **Disk Space Analysis**.
2. Choose a **drive** or pick a folder with **Choose folder …**.
3. **Start scan**. Progress (files, folders, size, current folder) is shown; **Cancel** is possible at any time (the result so far stays visible and is marked incomplete).
4. Results in four tabs:
   - **Folders:** subfolders by size with bars; click a name to go into it; **↑ Up** goes back.
   - **Largest files (top 100)**.
   - **Duplicates:** **Check duplicates (SHA-256)** compares files of 1 MB or more with the same size first by their first 64 KB and then completely via SHA-256. Only files with truly identical content are grouped.
   - **File types:** usage per file extension.
5. **Explorer** opens the folder or selects the file in Windows Explorer.
6. To clean up, **tick** files and choose **Move selected files to Recycle Bin …**.

**Safety**
- The scan **only reads**; nothing is ever deleted automatically.
- Only what you tick is deleted – always **to the Recycle Bin**.
- Deleting is blocked in **Windows**, **Program Files** and **ProgramData** as well as for system files and drive roots (box greyed out, reason as tooltip).
- At least one copy of duplicate files always remains.

**Limits:** Links (junctions/symlinks) are not followed; unreadable folders are counted and skipped. Online-only OneDrive files are not counted (they would otherwise be downloaded). Hard links (e.g. in `C:\Windows\WinSxS`) are counted per path – totals can exceed the space actually used. Space is only freed once the Recycle Bin is emptied.

## 3.28 Desktop Organizer

**Purpose:** Group and arrange desktop icons by type – only positions change.

**Functions:** sort order A – Z / Z – A, screen side (start on the left/right), gap between groups, own **rules** (categories with file extensions, order = arrangement), **Refresh preview**, **Organize desktop now**, **Restore last desktop layout**.

**Notes:** No file is moved, renamed or deleted; the layout is saved before every change. **Requirement:** "Auto arrange icons" must be turned off (right-click the desktop → View).

## 3.29 Windows Health

**Purpose:** Check and repair Windows, **WINDOWS HEALTH SCORE**.

Described in detail in [chapter 9.3](#93-checking-and-repairing-windows-health).

## 3.30 Crash analysis

**Purpose:** Blue screens, hardware errors and program crashes explained in plain language.

Described in detail in [chapter 9.2](#92-crash-analysis).

## 3.31 Timeline

**Purpose:** "What happened?" – changes and problems of the last 7, 30 or 90 days on a timeline.

Described in detail in [chapter 9.1](#91-timeline--what-happened).

## 3.32 Windows Logs

**Purpose:** Events from the Windows logs **System**, **Application** and **Setup**.

**Functions:** choose the log, **incl. warnings**, **Refresh**; columns time, level, source, event ID, message. See also [chapter 9.4](#94-logs).

## 3.33 Services

**Purpose:** Show Windows services with status, start type, account and path.

**Functions:** **Filter by name**, **Running only**, **Refresh**.

**Notes:** Display only. Orange = start type "Automatic" but not running (normal for delayed or trigger start). Make changes in the Windows Services console.

## 3.34 System Snapshot

**Purpose:** Save the system state before changes and compare it afterwards.

**Steps**
1. Enter a label (e.g. "Before driver update") → **Create snapshot**.
2. Make the change (e.g. install a driver).
3. Create a second snapshot, select them under **BEFORE** and **AFTER** → **Compare**. The table shows area, change and entry.

**Notes:** Captures hardware, drivers, programs, Windows version, services, startup, network, events and updates – without IP/MAC addresses, serial numbers or user data. Stored locally (**Open folder**).

## 3.35 Backup Center

**Purpose:** System image, file backup with versions, restore, driver backup, rescue USB stick.

Described in detail in [chapter 4](#4-backing-up-and-restoring-your-pc).

## 3.36 Security Center

**Purpose:** Read the security status: Defender or other antivirus, firewall, Secure Boot, TPM, BitLocker, UAC, app integrity.

**Functions:** **Open Windows Security**, **Firewall settings**, **Check file (signature & hash)** with **Select file …** (SHA-256, optionally **Also MD5 (legacy, not secure)**).

**Notes:** ULTIMATE X only reads these values. Defender, firewall, Secure Boot, TPM and BitLocker are never changed automatically; BitLocker recovery keys are never read. The checked file is only read, never executed. Windows system files are often catalog-signed and show "No embedded signature".

## 3.37 Security Check

**Purpose:** A quick IT audit of the PC with a **SECURITY SCORE** (0–100). The check only reads; something is changed only if you confirm an offered fix.

| Check | Meaning |
|---|---|
| **SMB1 protocol** | Outdated file-sharing protocol (entry point e.g. for WannaCry). Should not be installed. Without admin rights possibly "N/A without admin rights". |
| **Remote Desktop** | Can the PC be reached via Remote Desktop? Without Network Level Authentication (NLA) = FAIL. |
| **Firewall profiles** | Domain, Private and Public should be on. |
| **Local administrators** | Warning with more than two accounts or an active built-in Administrator. |
| **Accounts without password requirement** | Active local accounts that are allowed to have no password. |
| **Guest account** | Should be disabled. |
| **UPnP on the router** | If the router answers a UPnP search, "Router reports UPnP active" appears. No answer does not prove that UPnP is off. |
| **Autorun / AutoPlay** | Whether Windows asks when you insert media. |
| **PowerShell 2.0** | Outdated PowerShell version that bypasses modern protection features. |
| **Windows version** | Does your Windows version still receive security updates? (support table as of October 2026) |

**Steps**
1. Open the page – the check starts automatically (**Check again** repeats it).
2. Read findings marked **WARNING** or **FAIL**; a hint is shown below each.
3. If offered, choose a button under **FIX (AFTER CONFIRMATION, UAC)** – **Disable SMB1 …**, **Turn off Remote Desktop …**, **Turn on firewall everywhere …** –, read the prompt and confirm. Windows asks for admin rights. The output appears under **OUTPUT OF THE LAST FIX**.

**Notes:** After disabling SMB1 a restart is required; very old NAS devices/printers that only support SMB1 may no longer be reachable. If you yourself are connected via Remote Desktop, turning it off prevents future connections.

**Limits:** Microsoft Defender, User Account Control (UAC) and Windows Update are never changed. PowerShell 2.0, guest account, accounts and UPnP are only displayed – change them in Windows or on the router. The app cannot detect Extended Security Updates (ESU) for Windows 10.

## 3.38 Privacy

**Purpose:** View and toggle important Windows privacy settings individually – undoable at any time.

| Setting | Effect of "Enable privacy" | Applies to |
|---|---|---|
| **Advertising ID** | apps may not use an advertising ID | this account |
| **Tips and suggestions (Start & lock screen)** | no suggested content/app suggestions | this account |
| **Tailored experiences** | diagnostic data not used for personal tips/offers | this account |
| **Web search in the Start menu** | Start menu search without Bing web search | this account |
| **Activity history** | Windows does not collect an activity history | all users (admin rights) |
| **Diagnostic data "Required" only** | only the required diagnostic data is sent to Microsoft | all users (admin rights) |

**Steps**
1. Check the status of each setting: **Privacy enabled**, **Partial** or **Windows default**.
2. Click **Enable privacy**. For settings that apply to all users, first confirm the prompt, then the Windows admin prompt.
3. To revert, click **Undo** – the value before the first change is restored (without a backup: Windows default).

**Notes:** Before the first change ULTIMATE X remembers the original value in `%LOCALAPPDATA%\UltimateX\privacy-backup.json`. Some changes only take effect after signing in again or restarting. On company PCs group policies may override settings. **Open Windows privacy settings** takes you to the Windows settings.

**Limits:** No Windows components or apps are removed, no services are turned off and the hosts file is not changed. Diagnostic data can only be limited to "Required", not switched off completely.

## 3.39 Open Source Security

**Purpose:** ClamAV as an optional additional on-demand scanner – Microsoft Defender remains the primary real-time protection.

**Functions:** **Check project online** (actively maintained, not archived), **Install ClamAV**, **Definitions Update**, scans **Quick Scan**, **Full Scan**, **Custom Scan …**, **USB Scan**, **Download Folder Scan**, optionally **Move detections to quarantine**, **Open quarantine**, **SCAN HISTORY**.

**Steps:** **Check project online** → **Install ClamAV** → **Definitions Update** → choose a scan.

**Limits:** ClamAV has no real-time protection; a full scan can take hours. ULTIMATE X never disables or changes Defender.

## 3.40 Technician Mode

**Purpose:** Technical view for professionals: a tree with system, mainboard, CPU, memory, GPU and further device classes; per device status, class, manufacturer, device ID, hardware IDs, driver version/date/provider and signature; plus **Recent admin commands (exit codes & original output)**.

**Note:** IDs and serial numbers are masked as everywhere until you reveal them after confirmation.

## 3.41 Tools & Power

**Purpose:** Windows tools and power actions.

**Functions**
- **Power:** Shut down, Restart, Sign out (each with confirmation), Sleep (standby), Lock (lock screen), **Cancel scheduled shutdown**; countdown before restart/shutdown (adjustable in *Settings*).
- **Scheduled** (new in 1.3): shut down, restart, lock, sign out or sleep at a set time – see [chapter 8](#8-scheduled-shutdown).
- **Windows tools:** Command Prompt, CMD (administrator), PowerShell, PowerShell (administrator), Windows Terminal, Task Manager, Device Manager, Disk Management, System information, Windows Update, Network adapters, Firewall settings, Windows Security, Apps & Features, Storage settings, System Restore, Location privacy, Reliability history.

**Note:** Tools marked "UAC" require confirmation (Windows Hello if set up) and then administrator approval by Windows.

## 3.42 Logs

**Purpose:** The action and security log of ULTIMATE X itself.

**Functions:** columns time, level, area, action, message, details; **Security events only**, **Export (TXT/JSON) …**, **Open folder**. See [chapter 9.4](#94-logs).

## 3.43 PC report

**Purpose:** PDF export and printing with preview – privacy mode on by default.

**Functions:** **EXPORT PC REPORT (PDF)**, **PRINT PC REPORT** (Windows print dialog, where "Microsoft Print to PDF" is also available), **Collect data again**, **SECTIONS** (Hardware, Sensors, Network, Security, Drivers, Windows Health, Component tests, Benchmarks, Crash analysis, Rating & recommendations), **Run component test now**, **Privacy mode (recommended)**.

**Steps**
1. Select or deselect sections; the preview shows what the report contains.
2. **EXPORT PC REPORT (PDF)** – the file is saved as `Documents\ULTIMATE X\UltimateX-PC-Bericht-<date>.pdf`.

**Notes:** Privacy mode masks IP, MAC, user and computer names, serial numbers, gateway/DNS. Turning it off requires confirmation via Windows Hello. If a section is missing, it is deselected or there is no data yet.

## 3.44 Settings

**Purpose:** Appearance, security, startup and updates.

| Area | Settings |
|---|---|
| **Appearance** | Color scheme (System/Dark/Light), Layout (six layouts), Language (Automatic/German/English) + **Restart app** |
| **Security** | **Mask sensitive data (MAC, IP, serial numbers)** – turning it off requires Windows Hello; **Security session after successful confirmation**: Always ask again, 1, 2 or 5 minutes |
| **Start & Power** | Start with Windows, Start minimized, Countdown before restart/shutdown (10/30/60 s) |
| **Notifications & updates** | Windows notifications, restore point before driver updates and system repairs, check for updates in the background at startup, **Update source for ULTIMATE X** (publisher's HTTPS manifest; empty = no app update check) |
| **About** | version, **Open data folder** |

**Notes:** The security session ends immediately when the PC is locked, the user is switched or **LOCK** is pressed. ULTIMATE X never asks for passwords or PINs itself.

## 3.45 Gaming Mode

**Purpose:** Layout with live performance at a glance: CPU, GPU, RAM, VRAM, GPU and CPU temperature and **PERFORMANCE HISTORY · 2 MIN**.

**Note:** GPU load, VRAM and GPU temperature come from the Windows graphics kernel (as in Task Manager). CPU temperatures come from ACPI or HWiNFO/LibreHardwareMonitor. For values directly in the game use the [overlay](#6-in-game-overlay).

## 3.46 Visual PC Map

**Purpose:** Graphical overview of the components – clicking a component shows details (including RAM channels/clock, firmware status, performance score). **Open Technician Mode** jumps to the technical view.

## 3.47 Admin / Service Mode

**Purpose:** All important areas as tabs for service work: Hardware, Drivers, Processes, Services, Startup, Network, Storage, Windows Logs, Diagnostics, Security and **Export**.

**Export:** **Export system report (TXT/JSON) …** (Windows, CPU, GPU, RAM, mainboard, storage, network, drivers, updates, startup, health; sensitive data hidden by default, **Include sensitive data** requires confirmation) and **Export action and security log (TXT/JSON) …**.

*Note:* The **Simple Mode** layout has no navigation entry of its own. It shows large tiles (Check PC, Test internet, Search for updates, Clean up storage, Tidy up desktop, Manage startup, Check Windows, Restart, Sleep) and **Switch to advanced view**.

---

# 4 Backing up and restoring your PC

The **Backup Center** brings together everything you need to back up and restore – using built-in Windows tools and no additional software. You find it in the navigation under **Backup Center** (search terms e.g. "backup", "image", "driver").

| Area | Purpose |
|---|---|
| Overview | last backups, warnings, history, recommendations |
| File backup | create, edit and run backup profiles with versions |
| Restore | bring back files/folders from an earlier version |
| System image | back up all of Windows as an image; browse an image; instructions for restoring |
| Back up drivers | export all installed drivers to a folder |
| Rescue USB stick | create a Windows recovery drive |

**How you are protected**
1. **System image:** restore the complete Windows installation (after a disk failure, malware, a failed update).
2. **File backup with versions:** bring back individual files and folders from an earlier day.
3. **Rescue USB stick:** starts a PC that no longer boots – to restore the image.

**Recommendation – the 3-2-1 rule:** three copies of your data, on two different media, one of them off-site. Unplug the backup drive after the backup – so ransomware cannot reach it.

**Recommendation – BitLocker To Go:** encrypt the backup drive (Explorer → right-click the drive → "Turn on BitLocker", in Windows Pro/Enterprise). Keep the recovery key separate from the drive.

## 4.1 Overview

Shows, per profile, the last successful backup, the schedule and whether the target drive is connected, as well as the last system image and the last driver backup. Red **warnings** appear if the last successful backup of a profile is older than the warning threshold (default 7 days). The same warning is also shown by the [notification-area monitoring](#64-background-monitoring-notification-area) (rule *Backup overdue*). The **History** lists the latest backups of all kinds with result, target and duration.

## 4.2 File backup with versions

**Purpose:** Back up your personal files regularly to an external drive – with several versions, so you can also bring back last week's state. Runs **without** administrator rights.

**How it works:** Every backup is a complete folder that you can open in Explorer even without ULTIMATE X:

```
E:\UltimateX-Backup\<Profile>\2026-10-14_190000\C\Users\<Name>\Documents\…
```

Unchanged files are not copied again but taken over from the previous version as *hard links*. Each version is therefore complete but only needs space for the changes. Requirement: the target drive is formatted with **NTFS**. On exFAT/FAT32 each version is copied in full (works, but needs more space).

### Create a profile – step by step
1. Open the **File backup** area and choose **+ New profile**.
2. Enter a **Name** (e.g. "My files").
3. Choose the **Target** (**Choose …**): ideally the root of an external drive (e.g. `E:\`).
4. Tick **Templates**: Desktop, Documents, Pictures, Videos, Music, Downloads, OneDrive (if set up), Browser bookmarks (only the bookmark files of Edge/Chrome and Firefox), Source code (`%USERPROFILE%\source\repos`, if present). Templates are resolved again on every run – a new browser profile, for example, is included automatically.
5. Add more folders with **Add folder …** or by **drag & drop** from Explorer.
6. Check the **Exclusions** – one pattern per line:
   - `node_modules`, `bin` – excludes every folder/file with this name (including its content),
   - `*.tmp`, `~$*` – wildcards `*` (anything) and `?` (one character),
   - `Cache\*` – patterns containing `\` are relative to the source folder; `**\logs\*.log` matches `logs` at any depth,
   - **+ “Programming” template** adds `bin, obj, node_modules, .vs, packages, *.tmp, Thumbs.db, ~$*`. The `.git` folder is deliberately kept.
7. Choose a **Schedule**: **Manual**, **Daily** (time), **Weekly** (day + time) or **When the backup disk is connected**.
8. Set **Retention (versions)** (default 10).
9. Optionally **Also detect changes by hash (slower)** – reads every file but also detects changes without a new modification date.
10. **Save profile**, then **Back up now**.

**During the backup** you see phase, progress and file count; **Cancel** is possible at any time. A cancelled version stays marked as "incomplete" and is never used as a base for hard links.

**After the backup** ULTIMATE X automatically checks that all files are present with the correct size and compares a sample of SHA-256 checksums. Skipped files are listed with their reason under **SKIPPED FILES**.

### Notes
- **Open files** (e.g. Outlook PST, databases of a running browser) cannot be read without administrator rights – shadow copies (VSS) require admin rights. These files are skipped and listed. Tip: close Outlook and your browser before the backup.
- **Online-only OneDrive files** are not downloaded and not backed up. If you want to back up everything, choose "Always keep on this device" in OneDrive.
- Links (junctions/symbolic links) are not followed; empty folders are not backed up.
- The target drive is recognized by the file `UltimateX-Backup\target.json` – even under a different drive letter. Please do not delete this file. If a *different* backup drive is plugged in under the usual letter, ULTIMATE X does not back up there for safety reasons.
- If source and target are on the same drive, ULTIMATE X warns you: that does not protect against a disk failure.

### Retention
After every *successful and verified* backup, the oldest versions beyond the configured number are deleted **permanently** (not to the Recycle Bin – otherwise no space would be freed). Only version folders of this profile below `UltimateX-Backup` that contain an ULTIMATE X manifest are deleted. Your original files and other folders are never touched. Thanks to the hard links, no data that newer versions still need is lost.

> **Important – do not change anything inside version folders:** unchanged files are shared between versions via hard links. If you edit a file *inside* a version folder, you change it in all versions at once. To work with files, always copy them out first or use **Restore**.

### Schedules – to be honest
Schedules only run while ULTIMATE X is open (minimized or in the notification area is enough). If the app is closed or the PC is off, a missed daily/weekly backup is caught up at the next start as soon as the target drive is connected. "When the backup disk is connected" checks every minute whether the target drive has been connected and backs up at most once every 20 hours. ULTIMATE X does **not** create a Windows task.

### Export/import profiles
Profiles are stored in `%LOCALAPPDATA%\UltimateX\backup-profiles.json`. **Export …** saves them to a file, **Import …** loads them (e.g. on a new PC). Imported profiles start without history; the target drive is reassigned on the first run. **Remove profile** only removes the profile, never the backups.

**Settings** (File backup area): **Store SHA-256 per file in the manifest** (on by default) and **Warn if the last backup is older than (days)**.

## 4.3 Restoring files and folders

1. Open the **Restore** area and choose the profile and **Version** (date).
2. Without a search term you see the backed-up sources (e.g. Documents). With a search term you find files and folders whose name contains it.
3. Select a file or folder in the list.
4. Choose **Restore to**:
   - **Original location** – back to where it was,
   - **Other folder** – the entry is placed with its name in the chosen folder (recommended if you want to compare first).
5. **If the file already exists at the target:**
   - *Keep existing files (skip)* – default, nothing is overwritten,
   - *Keep both (backup with a new name)* – the backed-up file is saved as "Name (wiederhergestellt date time).ext",
   - *Replace (existing file to the Recycle Bin)* – the existing file goes to the Recycle Bin, then the backup is written back.
6. **Restore …** – ULTIMATE X first shows how many files are affected and how many already exist. Nothing is written until you confirm.

Alternatively: **Open version in Explorer** and simply copy files out.

**Limits:** Nothing is ever restored into protected system folders (e.g. `C:\Windows`, `C:\Program Files`). If the original drive is missing, choose "Other folder".

## 4.4 Creating a system image

**Purpose:** A complete copy of Windows including programs, settings and files on the system drive plus the boot partitions (EFI, recovery). It lets you fully reset the PC after a disk failure, malware or a failed update. Technology: Windows Backup (`wbadmin`), requires administrator rights (UAC).

1. Connect the external drive, open the **System image** area, **Refresh** if needed.
2. Choose the target drive. ULTIMATE X checks in advance: not the system drive, locally attached, **NTFS**, free space at least the used space of the system drive + 10 %. If something does not fit, the reason is shown right below.
3. **Create image …**, confirm and allow the UAC prompt.
4. Progress and wbadmin output are shown (progress per volume: EFI, system, recovery). Duration: 20 minutes to several hours.

**Notes**
- **Stop display** only stops the display – wbadmin keeps running with administrator rights. You can see the result later under **Show images (wbadmin)**.
- Windows usually only keeps the **newest** image per PC reliably on a drive. For several states, rotate several drives.
- The image is stored in `<drive>:\WindowsImageBackup\<PC name>\`. Do not rename or move the folder, otherwise Windows recovery cannot find it.

### Browsing an image (getting individual files)
1. **Search VHDX files** – lists the image files. If ULTIMATE X reports "administrators only", first open **Image folder in Explorer** and confirm Windows' access prompt, then search again.
2. **Browse …** – Windows mounts the VHDX as a drive.
   **Caution:** Windows mounts the file *writable*. Only copy files out, do not change or delete anything – otherwise the image may become unusable.
3. Afterwards in Explorer: right-click the mounted drive → **Eject**.

## 4.5 Restoring a system image – step by step

> **Caution:** The system drive is completely overwritten with the state of the image. Back up files created after the image with the file backup first and restore them afterwards.

1. Connect the external drive containing the image.
2. **Windows still starts:** in ULTIMATE X, in the System image area, click **Advanced startup …** (opens *Settings → System → Recovery*) and choose **Restart now** next to "Advanced startup".
   **Windows no longer starts:** boot from the **rescue USB stick** (see 4.7) or from Windows installation media and choose "Repair your computer".
3. In the blue menu: **Troubleshoot → Advanced options → System Image Recovery** (if necessary first "See more recovery options").
4. Choose the account and enter its password.
5. Windows suggests the newest image; choose an older one via "Select a system image".
6. Finish the wizard and wait until the restore is complete.
7. Windows starts in the backed-up state. Then run Windows Update and, if needed, restore newer files from the file backup (see 4.3).

## 4.6 Backing up drivers

**Purpose:** Copy all installed driver packages (driver store) to a folder – useful after reinstalling Windows or for older devices without a vendor download.

1. Open the **Back up drivers** area and choose a drive (at least 1 GB free).
2. **Back up drivers …** and confirm the UAC prompt.
3. The drivers are stored in `<drive>:\UltimateX-Backup\Treiber\<YYYYMMDD-HHMMSS>\`, one subfolder per package. The list **BACKED-UP DRIVERS** shows package and INF; earlier backups are listed under **EXISTING BACKUPS ON THIS DRIVE** and can be opened with **Open**.

**Restoring** – ULTIMATE X deliberately never does this automatically:
1. Start menu → open "Terminal (Admin)".
2. Enter (adjust the folder):
   `pnputil /add-driver "E:\UltimateX-Backup\Treiber\20261014-153000\*.inf" /subdirs /install`
3. Restart. For individual devices you can also use Device Manager: device → Update driver → "Browse my computer for drivers" → choose the folder, "Include subfolders".

**Note:** Only restore drivers that match this PC. Graphics drivers are better installed fresh from the vendor.

## 4.7 Rescue USB stick

**Purpose:** A recovery drive starts a PC that no longer boots into Windows recovery – from there you can restore the system image.

1. Have a USB stick with at least 16 GB ready (with the option "Back up system files": 32 GB). **All data on the stick will be deleted.**
2. **Start Windows wizard …** and confirm the UAC prompt. The Windows "Recovery Drive" wizard guides you through the creation.
3. Label the stick and keep it safe. Recreate it after major Windows feature updates.

**Booting from the stick:** press the boot menu key when switching on (often F12, F8, F11 or Esc) and choose the stick.

## 4.8 Limits at a glance

- File backup without shadow copies: open files are skipped (listed in the report).
- Schedules only while ULTIMATE X is running.
- Network shares (`\\Server\…`) are not supported as source or target.
- System image via Windows Backup (wbadmin): reliably only the newest image per drive and PC; the target must be NTFS.
- A system image that has been started cannot be cancelled from within ULTIMATE X (only the display).
- No cloud backup, no encryption by ULTIMATE X itself – use BitLocker To Go for that.

---

# 5 Games: Can My PC Run It and online requirements

## 5.1 Overview

The **Can My PC Run It** page compares a game's published system requirements with your PC (columns **MY PC**, **MINIMUM**, **RECOMMENDED**, **Status**), gives a rating from **EXCELLENT** via VERY GOOD, GOOD, PLAYABLE, LIMITED to **NOT RECOMMENDED** and estimates the frame rate (**FPS ESTIMATION (ESTIMATED)**) per resolution, quality level (Low/Medium/High/Ultra), ray tracing and upscaler.

The built-in database contains the requirements of 42 well-known games. Since version 1.3, ULTIMATE X can load the requirements (minimum and recommended) for all other games from the public **Steam store** – without a Steam account and without signing in. The loaded data is saved locally and is then also available offline.

## 5.2 Checking a game

1. Open **Can My PC Run It**. **MY PC** at the top shows your hardware.
2. Type the game name into the search box (e.g. "Hogwarts Legacy").
3. If the game is already known (built in or saved earlier), it is selected and rated immediately – nothing is fetched.
4. Read the rating, comparison table and FPS estimate.

## 5.3 Searching a game online (Steam)

1. Enter the game name.
2. Click **Search online** or press **Enter**.
3. ULTIMATE X searches the Steam store, picks the best match, loads the requirements, saves them and rates your PC immediately.
4. Below the game name the **source** is shown: "built-in database …" or "Steam (AppID …, retrieved on DD.MM.YYYY)".

## 5.4 Loading missing requirements for installed games

Under **INSTALLED GAMES** ULTIMATE X lists games from Steam, Epic, Xbox, GOG, EA and Ubisoft with launcher, size, **PC Suitability**, **Expected performance**, **Possible bottleneck** and **Recommendation**.

1. Click **Load missing requirements online**.
2. Confirm the prompt. Only games **without** data are queried – Steam games by their Steam AppID, games from other launchers by their name.
3. Progress is shown ("Loading 3/12: …"). **Cancel** stops the process at any time.
4. The list is then re-rated automatically.

Only local library files (names, install location) are read – no account data or passwords.

## 5.5 Removing a saved game

Select a game saved from online data and click **Remove saved game** (with confirmation). Built-in games cannot be removed. The data can be loaded again at any time.

## 5.6 Further functions

- **Search requirements on the web** opens a web search in your default browser.
- **FIND GAMES FOR MY PC** offers search queries built from your hardware; a click opens the search in your default browser. ULTIMATE X itself fetches nothing.

## 5.7 Notes

- **Privacy:** Only the game name (search) or the Steam AppID is sent to `store.steampowered.com` – no hardware, account or device data. Online requests only happen at the push of a button.
- **Storage location:** `%LOCALAPPDATA%\UltimateX\games-user.json`. A damaged file is not loaded; a backup copy (`games-user.json.broken`) is created.
- **Nothing guessed:** Only explicitly stated values are taken over (CPU, GPU, RAM, VRAM, storage, DirectX, SSD). Unknown details appear as **N/A**. Generic statements like "quad-core processor" or "DirectX 11 graphics card" cannot be compared – the rating may then be N/A.
- **SSD:** For the minimum, an SSD only counts as mandatory if Steam explicitly requires it ("SSD required"); for the recommendation a mention ("SSD preferred") is enough.
- **Ray tracing/upscaling:** Steam provides no information on this. For games loaded online, the FPS estimate with ray tracing or DLSS/FSR/XeSS therefore shows "N/A – no information on ray tracing/upscaling in the online data".
- **All FPS values are estimates (ESTIMATED)** based on published requirements and estimated performance classes – real values depend on the game scene, patch and driver. Unknown or mobile graphics cards result in N/A.

## 5.8 Limits

- Only games listed in the Steam store (e.g. Valorant or Fortnite are not available there).
- Steam limits the number of requests. With several games ULTIMATE X waits 1.5 seconds between requests; if Steam reports "too many requests" (HTTP 429), the process stops – please try again a few minutes later.
- If the game name is ambiguous, no game is guessed; the names found are shown so you can refine the search.
- Vendor requirements can change with updates; the data therefore carries the retrieval date.

---

# 6 In-game overlay

## 6.1 Setting up the overlay

**Purpose:** Performance values directly on top of the game – FPS, frametime, 1 % low, CPU/GPU utilization, temperatures, clocks, GPU power, RAM, VRAM, ping and time. The overlay is **click-through**: mouse and keyboard stay with the game; it captures no input.

1. Open the **Overlay** page. The top shows whether the overlay is shown or hidden; **Overlay on/off** toggles it.
2. Under **DISPLAYED VALUES** tick the lines you want.
3. Under **APPEARANCE & POSITION** choose:
   - **Monitor**,
   - **Corner** (Top left, Top right, Bottom left, Bottom right),
   - **Font size**, **Opacity**, **Background** (transparency),
   - **Ping target**: *Router (default gateway, local)* or *Internet (1.1.1.1, Cloudflare)*,
   - **Hotkey** (default **Ctrl+Shift+O**),
   - optionally **Show overlay when the program starts**.
4. Click **Apply**. The **PREVIEW (LIVE)** shows what the overlay looks like.
5. In the game, show and hide the overlay with the hotkey.

**Profiles:** Under **PROFILES** you save the current appearance under a name (**Save current appearance as** → **Save**), e.g. "Minimal" with FPS only or "Diagnostics" with all values. **Load profile** restores it, **Delete profile** removes it. A profile contains values, corner, font size and transparency.

**Notes**
- Only values with a measurement source are shown. If a source is missing, the line is omitted. CPU/GPU clocks and GPU power are only provided by sensor sources such as HWiNFO or LibreHardwareMonitor (see *Sensor sources*).
- **Ping:** pinging only happens while the overlay is visible and the "Ping" line is active (every 3 seconds).
- **Hotkey:** at least **Ctrl**, **Alt** or **Win** plus one key (A–Z, 0–9, F1–F24, Insert, Delete, Home, End, Page Up, Page Down, Pause). Examples: Ctrl+Shift+O, Alt+F10, Ctrl+Alt+P. If the combination is already taken, a notice appears.
- The preview on the page does not measure FPS of ULTIMATE X itself – FPS always refer to the program in the foreground.
- Settings: `%LOCALAPPDATA%\UltimateX\overlay.json`.

**Limits**
- Games in **exclusive full-screen mode** do not show any other window on top. Set "Borderless window" or "Full screen (windowed)" in the game.
- Some games with anti-cheat or their own overlay can cover windows of other programs.

## 6.2 FPS measurement (without touching the game)

ULTIMATE X measures FPS, frametime and 1 % low of the program in the foreground **without loading any code into the game** (anti-cheat neutral). To do this it reads Windows' frame presentation events (Event Tracing for Windows/ETW, providers "Microsoft-Windows-DXGI" and "Microsoft-Windows-D3D9") – the same method as Intel's PresentMon.

| Value | Meaning |
|---|---|
| **FPS** | frames in the last second |
| **Frametime** | average time between two frames in the last second |
| **1 % low** | frame rate of the slowest 1 % of all frames in the last 10 seconds (reveals stutter) |

**Permission:** Windows only allows this measurement for administrators or members of the group **"Performance Log Users"** (German Windows: *Leistungsprotokollbenutzer*). Without the permission, the overlay shows **FPS: N/A** and the *Overlay* page shows a notice with the button **Show instructions**. ULTIMATE X does not change your account itself.

How to add your account to the group (once, administrator required):
1. Open `lusrmgr.msc` (Windows Pro/Enterprise) → *Groups* → *Performance Log Users* → *Add* → enter your user name. Alternatively, in a Command Prompt run as administrator:
   `net localgroup "Performance Log Users" YourUserName /add` (German Windows: `"Leistungsprotokollbenutzer"`).
2. Sign out and sign in again.
3. Restart ULTIMATE X.

**Limits**
- The program in the foreground is always measured. If you switch windows, the statistics start over.
- OpenGL and some Vulkan games report no Direct3D frame presentation – the FPS line then stays hidden.
- Measurement only runs while it is needed (overlay with FPS line visible, recording active or *Overlay* page open).

## 6.3 Recording a game session

1. **Overlay** page → **Start recording**.
2. Play.
3. **Stop recording**. The new session appears in the list (**Refresh list**); a click shows the FPS history, temperature history (CPU/GPU) and a summary (duration, avg FPS, minimum, avg 1 % low, maximum temperatures, game).

**Notes:** Every second, FPS, frametime, 1 % low, utilization and temperatures are written – as a CSV file under `Documents\ULTIMATE X\Sessions` (columns separated by ";", numbers with a dot; can be opened e.g. in Excel). Empty fields mean: no measurement. Session files are never deleted automatically.

## 6.4 Background monitoring (notification area)

**Purpose:** ULTIMATE X keeps an eye on your PC even when the window is minimized or closed and reports problems via Windows notifications.

| Rule | Default |
|---|---|
| CPU temperature above limit | 90 °C, continuously for more than 30 seconds |
| GPU temperature above limit | 85 °C, continuously for more than 30 seconds |
| Drive usage | warning from 90 %, critical from 95 % |
| SMART/drive warning | from the last SMART query and the Windows drive status |
| New blue screen | since the last check |
| Antivirus turned off | Defender real-time protection off and no other active antivirus |
| Backup overdue | a file backup profile is older than the Backup Center's warning threshold |

**Steps**
1. **Overlay** page → section **BACKGROUND MONITORING (NOTIFICATION AREA)**.
2. Turn on **Notification area icon and monitoring active** (on by default).
3. Enable/disable rules, set limits, **Check interval** (default 5 minutes) and **Repeat lock** (default 30 minutes per rule) → **Apply**. **Check now** checks immediately.
4. Optionally **Minimize to notification area on close** – ULTIMATE X then keeps running in the notification area after you close the window.

**Icon menu** (right-click the ULTIMATE X icon): **Open** · **Overlay on/off** · **Pause monitoring** · **Exit**. Double-click opens the window.

**Notes**
- Monitoring only reads – it changes nothing and does not access the internet.
- If "notifications" are turned off in Settings, no notifications appear; the **RECENT ALERTS** list on the page still shows them.
- The same alert repeats at the earliest after the repeat lock. Windows notifications of the same kind appear at most once per hour; further ones appear as a balloon at the icon.
- Settings: `%LOCALAPPDATA%\UltimateX\background-monitor.json`.

**Limits**
- Temperature rules need a temperature source (see *Sensor sources*). Without a source nothing is reported.
- The SMART rule uses the last saved SMART query; for current values run the query again on the *Storage* page.

---

# 7 Benchmarks and Performance Score

## 7.1 Running benchmarks

**Purpose:** Measure, rate and compare the performance of processor, graphics card, memory, drive and network with earlier runs. All own tests run locally, without additional software and without internet (exception: optional speed test). Results are stored in the **History** (local database).

| Card | What is measured | Options |
|---|---|---|
| **Test CPU** | integer, floating point, crypto, compression, decompression, AES, rendering – on one core and on all cores | **Sustained load (2 min, throttling detection)** |
| **Test GPU** | Direct3D 11 compute: computing power (GFLOPS), memory bandwidth | **Stability test (60 s)** |
| **Test RAM** | read, write, copy, latency | – |
| **Test drive** | sequential and 4K bypassing the Windows cache, IOPS, access time | Quick (256 MB), Normal (1 GB), Extended (4 GB – only if needed) |
| **Test network** | ping, jitter, packet loss, DNS, gateway, connection, Wi-Fi signal | **incl. speed test (download/upload)** |

**Steps**
1. Close other programs if possible.
2. Either **START COMPLETE BENCHMARK** (everything in sequence: CPU → GPU → RAM → SSD/HDD → Network → Cooling → Stability; approx. 5–10 minutes; optionally **incl. internet speed test**) or start individual tests via the cards. **Cancel** is possible.
3. Read the result: points per card, comparison with the previous run, **ULTIMATE X PERFORMANCE SCORE** at the top, **BOTTLENECK ANALYZER** (CPU/GPU/RAM/VRAM/storage/PCIe limit, throttling, power plan, background load).

## 7.2 ULTIMATE X PERFORMANCE SCORE – how to read the scale

- 0–100 points per component (CPU, GPU, RAM, SSD, network, cooling) and a weighted total:

| Component | CPU | GPU | RAM | SSD | Network | Cooling |
|---|---|---|---|---|---|---|
| Weight | 25 % | 30 % | 15 % | 15 % | 5 % | 10 % |

- **100 points ≈ the current top class for consumers (as of 2026).** The scale is logarithmic: doubling performance always adds the same number of points. Anything faster than the reference stays at 100.
- **Tiers:**

| Tier | Total score |
|---|---|
| ENTRY LEVEL | below 35 |
| STANDARD | 35–54 |
| GAMING | 55–71 |
| HIGH-END GAMING | 72–86 |
| ENTHUSIAST | 87 and above |

  A tier is only given if both CPU **and** GPU were measured. If a part fails, it is not included in the score.
- **Examples (estimated):** Core i9-13900K (top class 2022) about 84, RTX 4080 about 81, DDR5-6000 with 106 ns latency about 67, NVMe SSD (PCIe 4.0, approx. 7,000 MB/s) about 90. An RTX 5090 is at 100.
- The reference values are estimates (**ESTIMATED**) for the measuring methods of ULTIMATE X – they do not apply to other benchmarks.
- **Recalibrated in version 1.3:** In 1.2 the CPU was always at 100/100 due to wrong reference values. The stored measurements of older runs remain valid – only their rating changes. Your total may therefore be lower after the update without your PC having become slower.

## 7.3 Drives: which drive counts?

- For the SSD score in the Performance Score and for the bottleneck analysis, **the Windows system drive always counts** (usually `C:\`) – not the drive tested last.
- You can still test any local drive (e.g. `D:\`). The drive card then shows the last test and, clearly below it, which run of the system drive is used in the score. The percentage comparison only refers to earlier tests of the same drive.
- The Complete Benchmark always tests the system drive.
- If the system drive has never been tested, the SSD score is "N/A".

## 7.4 Expected vs Actual Performance

The area **EXPECTED VS ACTUAL PERFORMANCE** compares measured values (*Measured*) with typical values (*Expected*):

| Component | Expected value |
|---|---|
| GPU | vendor specification (TFLOPS) × typical utilization – only for graphics cards in the local database |
| RAM | theoretical bandwidth from channels and clock, of which about 75 % is considered achievable |
| Drive (system drive) | typical value of the interface: NVMe PCIe 3.0 ≈ 3,000 MB/s, PCIe 4.0 ≈ 6,500 MB/s, PCIe 5.0 ≈ 10,000 MB/s, SATA SSD ≈ 500 MB/s, hard disk ≈ 150 MB/s. For NVMe only if the PCIe generation can be determined unambiguously (e.g. a single NVMe SSD), otherwise "N/A" |
| CPU | no verified reference values – only comparison with your own earlier runs |

All expected values are estimates (**ESTIMATED**).

If a value is more than 10 % below expectation, ULTIMATE X names **possible causes – matched to the component**:
- **GPU:** temperature, power limit, PCIe configuration, driver, background programs, CPU limit, power plan
- **RAM:** XMP/EXPO off, single channel, background programs, power plan
- **Drive:** wrong drive tested, SATA instead of NVMe, drive almost full, drive temperature, background programs
- **CPU:** temperature, power limit, power plan, background programs

If nothing specific is detected, the general checklist for the component is shown.

## 7.5 Notes and limits

- Values that no sensor provides (e.g. power draw) are not shown or shown as N/A – nothing is estimated. Without HWiNFO/LibreHardwareMonitor the cooling score and throttling detection are limited.
- A 3D game test (FPS, 1 % low) is not included. For real in-game FPS use the [overlay](#6-in-game-overlay).
- The drive test's test file is always deleted. "Extended" mode writes 4 GB – only use it on SSDs when needed.

## 7.6 Geekbench (Primate Labs)

**Purpose:** If you also want to compare with the well-known Geekbench, you can launch an already installed Geekbench from ULTIMATE X and record the results in the benchmark history.

**Important**
- Geekbench is a program by Primate Labs. **ULTIMATE X does not ship Geekbench, downloads nothing and uploads nothing.**
- The free Geekbench version uploads its results to the Geekbench Browser (internet) **by itself** – that is Geekbench, not ULTIMATE X.
- Geekbench scores have their own scale and are **not** included in the ULTIMATE X PERFORMANCE SCORE.

**Steps**
1. **Benchmarks** page → **GEEKBENCH** card.
2. If Geekbench is installed, the path found is shown there. **Launch Geekbench** opens the program (without administrator rights, in its own window). If it is not installed, **Open official download page** opens www.geekbench.com/download/ in your browser – you download and install it yourself.
3. Run the test in Geekbench.
4. In ULTIMATE X under **Enter result**: **Version** (Geekbench 6 or 5), **Single-core** and **Multi-core** score, optionally **GPU (optional)** with API (OpenCL or Vulkan).
5. **Save result.** The card shows the last multi-core score, date, all entered values and the change from the previous result.

**Notes and limits**
- Only `C:\Program Files\Geekbench 6\` and `C:\Program Files\Geekbench 5\` are searched (highest version first). Other install locations are deliberately not supported.
- Comparisons are only made with results of the same Geekbench major version (5 and 6 have different scales).
- You enter the values yourself; they are not read automatically. Thousands separators are allowed (e.g. "22,456", "22.456" or "22 456"). If the multi-core score is lower than the single-core score, the entry is rejected as a typo. A GPU score is only accepted together with the API (OpenCL or Vulkan).

---

# 8 Scheduled shutdown

**Purpose:** Shut down, restart, lock, sign out or put the PC to sleep at a specific time – e.g. after a long download or a nightly backup.

## 8.1 Scheduling an action

1. Open the **Tools & Power** page, **Scheduled** area.
2. Choose the **Action**: Shut down, Restart, Lock, Sign out or Sleep.
3. Set the time:
   - **In …** – amount and unit (minutes or hours), e.g. "90 minutes",
   - **At …** – time in the format `HH:mm` (e.g. `22:30`) and **today** or **tomorrow**.
4. Click **Schedule** and confirm the prompt.
5. At the top, "… scheduled for 22:30 (today)" appears with the remaining time.

## 8.2 Cancelling or postponing

- In the **Scheduled** area: **Cancel**, **+15 min** or **+1 h**.
- **Cancel scheduled shutdown** (at the top of the Power area) also cancels a scheduled shutdown.
- **Warnings:** 5 minutes and 1 minute before the time, ULTIMATE X brings its window to the front and asks whether to cancel the action or postpone it by 15 minutes. Save your open work at the latest then.

## 8.3 Who carries out the action?

| Action | Carried out by | If ULTIMATE X is closed |
|---|---|---|
| Shut down, Restart | Windows (scheduled with `shutdown /t`) | the schedule continues; at the time Windows **forcibly** closes all programs – unsaved work is lost |
| Lock, Sign out, Sleep | ULTIMATE X itself | only works while ULTIMATE X is running (minimized is enough); if the app was closed at the time, the action is **not caught up** – a notice appears at the next start |

The schedule is saved (`%LOCALAPPDATA%\UltimateX\power-schedule.json`) and shown again after ULTIMATE X restarts.

## 8.4 Limits

- At most **7 days** in advance; only **one** action can be scheduled at a time (a new schedule replaces the old one).
- Daylight saving time: a time that does not exist (e.g. 02:30 when summer time begins) is moved to the first valid time after it (03:00); a time that occurs twice (end of summer time) applies at its first occurrence.
- A time "today" that has already passed is rejected (not shifted).
- If a shutdown was cancelled outside ULTIMATE X (e.g. with `shutdown /a`), ULTIMATE X keeps showing the schedule until its time.
- If another program has already scheduled a shutdown, Windows reports an error – cancel that one first.

---

# 9 Troubleshooting your PC

If your PC crashes, becomes slow or behaves strangely, it is best to proceed in this order:

1. **Timeline** – What has changed since the problem started?
2. **Crash analysis** – What do blue screens, hardware errors and program crashes say?
3. **Windows Health** – Are system files, drives and memory in order?
4. **Logs** – Windows Logs and the ULTIMATE X log for details.

## 9.1 Timeline – What happened?

**Purpose:** "The PC has been crashing since yesterday – what changed?" The timeline shows changes and problems of the last 7, 30 or 90 days, grouped by day.

**Events captured**
- Windows updates (installed / failed)
- Program installations and uninstallations (Windows Installer and installation date from the program list – there only the date, time "––:––")
- Driver installations
- Blue screens and unexpected restarts
- Program crashes
- Windows startup and shutdown
- System snapshots as well as startup and desktop layout backups of ULTIMATE X

**Steps**
1. Open the **Timeline** page and choose the **Period** (default: 30 days).
2. Filter by **type** with the check boxes; **Problems & changes only** shows only crashes, failures, drivers and updates; **All types** resets the filter.
3. Search for program, driver or update names in the **search box**.
4. Click an event – the **DETAILS** (time, source, error codes) appear on the right.

**Possible cause:** If the first crash occurs within **48 hours** after a driver or update installation (and there was no similar crash in the 7 days before), it is marked with ⚑ and named at the top under **POSSIBLE CAUSE**. This is a **possible connection, not proof** – check, for example, whether rolling back the driver or uninstalling the update helps.

**Limits**
- Windows event logs are limited in size; older events may be missing.
- Starts from fast startup/hibernation do not appear as "startup".
- Programs installed without Windows Installer only appear with a date (according to the program list), if the vendor provides it.
- Backup Center runs do not appear in the timeline – they are listed in the Backup Center history.
- Everything is read locally, without admin rights and without uploading anything.

## 9.2 Crash analysis

**Purpose:** Blue screens, hardware errors and program crashes explained in plain language.

**Functions**
- Choose the **Period (days)**.
- **Blue screens** with a plain-language explanation, parameters, source and recommendation.
- **Crash dumps (minidumps):** **Read crash dumps (admin)** reads the error code from the dumps; with the Microsoft debugger installed (*Debugging Tools for Windows*), **Find cause** additionally identifies the responsible driver (**Original debugger output** can be shown).
- **Hardware errors (WHEA)**, graphics driver resets.
- **Program crashes and hanging programs** with program, type, count, last occurrence, module and code.
- Buttons **Reliability history** (Windows) and **Test RAM**.

**Steps**
1. Open the page and choose the period.
2. Read the blue screens and recommendations.
3. For details, **Read crash dumps (admin)** (UAC) and, if available, **Find cause**.
4. If the recommendation points to memory errors, **Test RAM** (see Memory Test).

**Notes:** Crash dumps contain memory contents. They never leave your PC and are not copied. The debugger loads symbols exclusively from the Microsoft symbol server (HTTPS). **Limits:** Cause analysis requires the Debugging Tools for Windows (Windows SDK).

## 9.3 Checking and repairing Windows Health

**Functions**
- **WINDOWS HEALTH SCORE** (100 minus traceable deductions).
- **System files and component store:** **Check system files (SFC)**, **Quick Check**, **Deep Scan**, **Windows Repair** – **Repair workflow (DISM → SFC)**.
- **Disk (CHKDSK):** choose a drive, **Check (read-only)** or **Repair …**.
- **Memory:** **Windows Memory Diagnostic …**.
- **Updates & restart:** **Open Windows Update**.
- **Recovery:** **Create restore point**, **Open System Restore**.
- **Stability (30 days)**, **Important Windows services**, **Most frequent problems (event logs, 7 days)**.

**Step by step – repair Windows**
1. First run **Quick Check** or **Check system files (SFC)**.
2. If errors are reported, start **Windows Repair** (DISM checks and repairs the component store, then SFC repairs the system files). Confirm UAC.
3. Follow the live output; **Show technical output** shows the original output. At the end there is an easy-to-understand summary.
4. For drive problems use **Check (read-only)**; if errors are found, **Repair …**. The system drive can only be repaired at the next start – Windows then checks and repairs it automatically before sign-in.

**Notes**
- All tools run through a fixed command list and the signed admin helper; Windows asks via UAC each time.
- **Stop display** only stops the display, not the running command.
- Before *Windows Repair*, drive repair and driver updates a restore point is requested automatically (can be turned off in Settings). Windows creates at most one restore point per 24 hours.
- ULTIMATE X never disables Windows Update.

## 9.4 Logs

| Log | Where | Content |
|---|---|---|
| **Windows Logs** (page) | Windows event logs System, Application, Setup | errors (and optionally warnings) with source and event ID |
| **Logs** (page) | `%LOCALAPPDATA%\UltimateX\logs\app-*.jsonl` and `security-*.jsonl` | actions and security events of ULTIMATE X, kept for 30 days |
| **Recent admin commands** (Technician Mode) | current session | exit codes and original output of admin commands |
| Windows CBS log | `%windir%\Logs\CBS\CBS.log` | details on SFC/DISM |

On the **Logs** page, **Security events only** shows security-relevant entries; **Export (TXT/JSON) …** saves the log, e.g. for a bug report. In **Admin / Service Mode → Export** you export the system report and logs together.

---

# 10 FAQ and problem solving

## 10.1 Installation and start

| Problem | Solution |
|---|---|
| MSI reports that a newer version is already installed | Downgrades are blocked. Uninstall the newer version first via *Settings → Apps*. |
| App does not start, "integrity check" message | Program files were modified (invalid signature). Reinstall the MSI. |
| "Admin helper not found" | Reinstall; `UltimateX.AdminHelper.exe` must be next to `UltimateX.exe`. |
| "Secure connection to the admin helper could not be established" | Other security software blocks named pipes, or the app was started from an unusual folder. Start the app from the installation folder. |
| "Output of the admin helper discarded for security reasons" | The check of the peer or protocol failed (see `security-*.jsonl`). The command itself was executed; the result can be checked in Windows (e.g. `%windir%\Logs\CBS\CBS.log`). |
| Reset settings | Delete `%LOCALAPPDATA%\UltimateX\settings.json`. |
| Windows notifications do not appear | Notifications only appear with the Start menu shortcut from the MSI and if "Windows notifications" is on in Settings; otherwise an in-app notice is shown. |
| Windows Hello does not appear | Set up Hello in Windows Settings; without Hello you confirm explicitly. |

## 10.2 Sensors and temperatures

| Problem | Solution |
|---|---|
| "HWiNFO Sensor Source: NOT AVAILABLE" | HWiNFO is not running – optional. For real CPU core temperatures, start HWiNFO. |
| HWiNFO status "INACTIVE" | HWiNFO is running but *Shared Memory Support* is off: HWiNFO → Settings → enable "Shared Memory Support", keep the sensor window open (the free HWiNFO version limits the runtime). |
| LibreHardwareMonitor "INACTIVE" | Start LibreHardwareMonitor as administrator, otherwise its WMI interface is empty. |
| No GPU temperature | Install a current graphics driver (WDDM 2.4+); some iGPUs report no temperature. |
| CPU temperature missing without extra software | Intended: Windows only provides CPU core temperatures via kernel drivers; ULTIMATE X loads none. ACPI zones appear as "mainboard", not as CPU. |
| Min/avg/max look wrong | Statistics run since program start; **Reset min/avg/max** on the *Sensor sources* page. |

## 10.3 Network and ping

| Problem | Solution |
|---|---|
| Device "NOT REACHABLE" although it is running | Many devices/firewalls do not answer ping. Use **SERVICES** to check whether services are reachable. |
| Host name is not resolved | Local names (`*.local`, `fritz.box`) need the router as DNS or mDNS/LLMNR. Use the IP address. |
| Live ping stops when changing pages | Intended – continuous ping only runs while the page is open. |
| WLAN shows no SSID | Settings → Privacy → Location → "Let apps access your location". |
| Traceroute shows many "*" | Routers often deliberately do not answer ping – not an error. After 5 silent hops the trace stops. |
| DNS change only partly applied | One of the UAC prompts was declined. Choose the provider again or **Automatic (DHCP)**. |

## 10.4 Component tests

| Problem | Solution |
|---|---|
| Camera "access blocked" | Settings → Privacy → Camera → "Let desktop apps access your camera". |
| Microphone "no signal" | Check mute, default recording device and the microphone privacy permission. |
| System time "NOT AVAILABLE" | UDP port 123 (NTP) is blocked or there is no internet. |
| WinRE "NOT AVAILABLE" | `ReAgent.xml` is not readable without admin rights – check the status with `reagentc /info` in an admin console. |
| SMART "not read yet" | *Hardware* page → **Read SMART details (admin rights)** or *Storage* → **Read SMART (UAC)**. |
| Write test "not possible" | No write permission on the drive (e.g. removable media) or too little space. |
| Memory diagnostic result missing | The result appears in the event log after the restart and then in the Memory Test. |

## 10.5 PC report

| Problem | Solution |
|---|---|
| Privacy mode cannot be turned off | Turning it off requires Windows Hello or the confirmation; cancelled = stays on. |
| Section missing | The section is deselected or there is no data yet (e.g. no component test). |
| PDF instead of printer | Choose "Microsoft Print to PDF" in the print dialog or use **EXPORT PC REPORT (PDF)** directly. |

## 10.6 Repair and updates

| Problem | Solution |
|---|---|
| SFC "could not perform the requested operation" | Restart (pending updates), then try again. |
| DISM RestoreHealth error 0x800f081f | Check the internet connection/Windows Update. |
| winget missing | Install "App Installer" from the Microsoft Store. |
| Desktop Organizer reports "auto arrange" | Right-click the desktop → View → turn off "Auto arrange icons". |

## 10.7 New in version 1.3

| Question / problem | Answer / solution |
|---|---|
| **speedtest.ch says the WebView2 Runtime is not installed** | The Microsoft Edge WebView2 Runtime is missing (possible on Windows 10). **Download WebView2 Runtime from Microsoft** opens the official page; install the runtime and restart ULTIMATE X. The own speed test (Cloudflare) also works without WebView2. |
| **Overlay shows "FPS: N/A"** | Your account lacks the permission for FPS measurement (ETW). Add the account to the group "Performance Log Users", sign out and in again, restart ULTIMATE X – instructions in [6.2](#62-fps-measurement-without-touching-the-game) or via **Show instructions**. |
| **The FPS line is missing entirely** | The game uses OpenGL or Vulkan without Direct3D frame presentation – there is no measurement for it. |
| **Overlay is not visible in the game** | The game runs in exclusive full-screen mode. Choose "Borderless window" or "Full screen (windowed)" in the game. Also check that the overlay is shown (hotkey, default Ctrl+Shift+O). |
| **Hotkey does nothing** | The combination is used by another program (notice on the *Overlay* page). Choose another combination and **Apply**. |
| **A scheduled backup did not run** | Schedules only run while ULTIMATE X is open (minimized or in the notification area is enough). Tip: enable **Minimize to notification area on close** and, in Settings, **Start with Windows** + **Start minimized**. Missed daily/weekly backups are caught up at the next start as soon as the target drive is connected. |
| **Backup Center does not back up to the usual drive** | A different backup drive is plugged in under that drive letter (recognized via `UltimateX-Backup\target.json`). Connect the correct drive. |
| **System image: wbadmin reports an error** | Check the target: external, locally attached **NTFS** drive, not the system drive, free space ≥ used space of the system drive + 10 %. UAC must be confirmed. The original wbadmin output is shown in the System image area. |
| **System image: older images are missing** | Windows usually only keeps the newest image per drive and PC reliably. Rotate several drives. |
| **"Search VHDX files" reports "administrators only"** | Open **Image folder in Explorer**, confirm Windows' access prompt, then search again. |
| **A backed-up file changed in all versions** | Files in version folders are shared via hard links. Never work directly in version folders – copy files out first or use **Restore**. |
| **Open files were skipped** | No shadow copies without admin rights. Close Outlook/browser before the backup. |
| **Steam search reports "too many requests"** | Steam limits requests (HTTP 429). Wait a few minutes and try again. |
| **Game not found online** | Only games in the Steam store can be retrieved; refine ambiguous names. |
| **Performance score dropped after the update** | The scale was recalibrated in 1.3 (in 1.2 the CPU was always at 100). Your PC has not become slower. |
| **SSD score "N/A"** | The system drive has never been tested. Run **Test drive** on the system drive or **START COMPLETE BENCHMARK**. |
| **Scheduled lock/sleep was not carried out** | ULTIMATE X carries out these actions itself and only while the app is running. They are not caught up. |
| **Scheduling "Shut down" fails** | Another program has already scheduled a shutdown. First use **Cancel scheduled shutdown**. |
| **Notification area reports no temperature warning** | Without a temperature source (HWiNFO/LibreHardwareMonitor or graphics driver) there is no temperature rule. |
| **SMB1 disabled – NAS no longer reachable** | Very old NAS devices/printers only support SMB1. Update the device firmware; only re-enable SMB1 deliberately and temporarily (in Windows Features). |
| **Space is not freed after cleaning up** | Deleted files are in the Recycle Bin. Empty the Recycle Bin. |

---

# 11 Appendix

## A Keyboard shortcuts

| Shortcut | Effect | Where |
|---|---|---|
| **Ctrl+K** | activate the search box (global search) | main window |
| **Enter** (in the search box) | find and open a page | main window |
| **F5** | reload the current page | main window |
| **Ctrl+Shift+O** (default, can be changed) | show/hide the in-game overlay | system-wide while ULTIMATE X is running |
| **Enter** (in the game search box) | search the game online | Can My PC Run It |
| Double-click on the notification-area icon | open the window | taskbar |

## B File locations

**Under `%LOCALAPPDATA%\UltimateX\`** (your user account only, no admin rights needed):

| File / folder | Content |
|---|---|
| `settings.json` | settings (if damaged: backup copy + defaults) |
| `logs\app-*.jsonl`, `logs\security-*.jsonl` | action and security log (30 days) |
| `history.db` | histories (speed test, health score, benchmarks, network devices) |
| `backups\` | backups before changes (e.g. startup, desktop layout) |
| `desktop-rules.json` | Desktop Organizer rules |
| `smart-cache.json` | last SMART values read |
| `ComponentTests\history.json` | component test history (max. 20 runs) |
| `snapshots\` | system snapshots |
| `install-log.jsonl` | New PC Setup installation log |
| `setup\office\` | Office Deployment Tool |
| `clamav\` | ClamAV configuration, signatures, quarantine, scan history |
| `oui.csv` | vendor list (IEEE) of the network scanner |
| `updates\` | downloaded app update (only run after verification) |
| `games-user.json` | game requirements loaded online (v1.3) |
| `backup-profiles.json`, `backup-history.json` | backup profiles and history (v1.3) |
| `privacy-backup.json` | original privacy values for "Undo" (v1.3) |
| `power-schedule.json` | scheduled power action (v1.3) |
| `uptime-log.json` | own runtime log for PC Lifetime (v1.3) |
| `overlay.json` | overlay settings and profiles (v1.3) |
| `background-monitor.json` | notification-area monitoring settings (v1.3) |
| `WebView2\` | browser data of the embedded speedtest.ch (v1.3) |
| `bench\` | working folder for drive test files (always deleted) |

**Other locations**

| Location | Content |
|---|---|
| `Documents\ULTIMATE X\` | exported PDF reports |
| `Documents\ULTIMATE X\Sessions\` | recorded game sessions (CSV, v1.3) |
| `<target>\UltimateX-Backup\<profile>\<date_time>\` | file backup versions (v1.3) |
| `<target>\UltimateX-Backup\target.json` | backup drive identifier (do not delete) |
| `<drive>:\UltimateX-Backup\Treiber\<timestamp>\` | driver backups (v1.3) |
| `<drive>:\WindowsImageBackup\<PC name>\` | system image (created by Windows) |
| `C:\Program Files\ULTIMATE X Windows Control\` | program files |

Open the data folder via **Settings → About → Open data folder**.

## C License

- **Source code:** MIT License – Copyright (c) 2026 Dennis Karahusic. See the `LICENSE` file.
- **Installation and use:** The installer shows the End User License Agreement with disclaimer and limitation of liability (Switzerland): [EULA.md](EULA.md). ULTIMATE X is free, non-commercial open-source software. Rights to the source code are governed by the MIT license.
- **Third-party software** that you install on request (e.g. ClamAV, Microsoft 365 Apps, Firefox, Geekbench) is subject to the licenses of the respective vendors. Libraries used: [DEPENDENCIES.md](DEPENDENCIES.md).
