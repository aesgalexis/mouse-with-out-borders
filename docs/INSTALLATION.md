# Windows installation (two PCs)

This is a **mouse-only** Windows desktop application. The same executable runs on both PCs; each PC chooses its own role.

The current release is **0.1.4**. It adds two-way input: every mouse and touchpad on either PC can cross after pairing once. **Both PCs must be updated**; the 0.1.2 protocol is incompatible.

**Status:** Windows x64 preview. Installation and transport tests run in GitHub Actions. Real pointer switching must still be checked on two physical machines. The current release is unsigned and Windows SmartScreen may warn before installation.

## Option A — easiest: Setup.exe

1. Open the [v0.1.4 release](https://github.com/aesgalexis/mouse-without-borders/releases/tag/v0.1.4).
2. Download [MouseWithOutBorders-0.1.4-Setup.exe](https://github.com/aesgalexis/mouse-without-borders/releases/download/v0.1.4/MouseWithOutBorders-0.1.4-Setup.exe) and use it on **both PCs**.
3. Run it on each PC. Installation is for the current Windows user, without an administrator password. You can also use the portable ZIP without installing anything.
4. On either PC: open Mouse, with out borders; click **Allow the connection**; click **Start receiving**.
5. Note this PC's local IPv4 address and 8-character pairing code.
6. On the other PC: click **Connect to other PC**, enter the first PC's IP and code, and select **Right edge** or **Left edge** according to the physical layout. Click **Connect mouse**. The **Have the other PC's code? Enter it here** button also opens these fields, even if this PC is already listening.
7. Either PC's physical mice and touchpad can now cross the shared edge. The listening PC uses the opposite edge automatically. Return across the shared edge, or use a local device to reclaim its PC. No second pairing is needed.

Keep both PCs powered on and connected to the same trusted private LAN.

This installer automatically closes the installed app before updating, even
when its window is hidden in the notification area. It requests a clean exit,
then closes an older or unresponsive instance if necessary. Settings are retained.
The same shutdown applies when uninstalling.

## Manual updates from the app (0.1.3 and later)

Click **Check & install updates** at the bottom of the app window. It checks this
project's GitHub Releases, including previews, and installs only a higher version.
Download progress and errors appear below the button. The SHA-256 manifest must
match before Setup starts. The app then exits, Setup shows installation progress,
and the updated app opens automatically. Pairing settings are retained; Windows
is not restarted. Failed checks/downloads leave the app running and allow retry.
If Setup itself fails, it displays the error; the existing app can be reopened.

Updates require internet access to GitHub and are checked only on demand. A
portable copy uses the same per-user Setup installation; its original file remains
in place. Install 0.1.3 or later manually once to gain the button on an older app.

Upgrading an existing receiver replaces its old 32-character key with a saved 8-character code. Enter the new code on the mouse PC once. Connections to older receivers still accept their 32-character keys.

## Option B — assisted install (Windows PowerShell)

This script defaults to the published **0.1.4** release.

Download scripts/install.ps1 from the repository, inspect it, then run from PowerShell in the folder where you saved it:

    .\install.ps1

This downloads release 0.1.4, verifies the SHA-256 manifest shipped with the release, installs without admin rights, then opens the application.

On the **receiving PC**, use:

    .\install.ps1 -Receiver

This requests administrator authorization **only** to add a TCP 42187 inbound Windows Firewall rule scoped to private networks, which may otherwise block the receiver. If that prompt is declined, the application still installs; you may need to allow it manually.

If your Windows policy blocks running scripts, download Setup.exe directly and run it instead. You do **not** need to disable Windows security.

To run without opening the UI after the installation:

    .\install.ps1 -NoLaunch

## Network details

- Receiver TCP port: **42187**.
- Authentication: challenge/response with pairing key, then HMAC on every event.
- **Events are not encrypted.** Use trusted LAN only, never open or port-forward the port to the internet.
- Windows Security/UAC, locked or elevated desktops are not remotely controllable by this app.
- If there are several IPv4 addresses (VPNs, virtual adapters), choose the address on the network shared by both PCs.

## Startup / tray

- **Start with Windows** adds a per-user startup entry for the current executable.
- **Connect / listen automatically when opened** restores the saved connection/receiver.
- Closing the window minimizes it to the **notification area** by default.
- Right-click the tray icon and choose **Exit** to actually stop the application.

## Uninstall and rollback

Use Windows Settings > Installed apps > Mouse, with out borders > Uninstall, or run:

    .\scripts\uninstall.ps1

The assisted uninstall removes the app, saved Windows startup entry and (with administrator confirmation) the optional firewall rule. Settings and diagnostic logs stay in %LOCALAPPDATA%\MouseWithOutBorders by default.

To also delete pairing details and diagnostics:

    .\scripts\uninstall.ps1 -PurgeSettings

Windows' standard uninstaller removes the program and startup entry but does not automatically remove a separately elevated manual firewall rule; use the assisted uninstall or Windows Defender Firewall to clean that rule.

## Limitations and troubleshooting

- This preview is limited to two Windows 10/11 x64 computers.
- Mouse-only: movement, clicks and wheel. No keyboard sharing, clipboard, files or remote desktop.
- No Windows Service and no elevated desktop input injection.
- For connectivity, ensure private network profile, correct LAN IP, receiver running and matching 8-character code (or the legacy key for an older receiver).
- For a lost connection, the controller's cursor is released after its network heartbeat timeout, normally about 5 seconds.
- Diagnostic logs: %LOCALAPPDATA%\MouseWithOutBorders\diagnostics.log. Do not publish logs containing personal information.
- These scripts change only the **local PC** on which they are run. They cannot install the application on another machine remotely.
