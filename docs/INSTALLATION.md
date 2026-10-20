# Windows installation (two PCs)

This is a **mouse-only** Windows desktop application. The same executable runs on both PCs; each PC chooses its own role.

**Status:** Windows x64 preview. Installation and transport tests run in GitHub Actions. Real pointer switching must still be checked on two physical machines. The current release is unsigned and Windows SmartScreen may warn before installation.

## Option A — easiest: Setup.exe

1. Go to https://github.com/aesgalexis/mouse-with-out-borders/releases/tag/v0.1.1 .
2. Download MouseWithOutBorders-0.1.1-Setup.exe on **both PCs**.
3. Run it on each PC. Installation is for the current Windows user, without an administrator password. You can also use the portable ZIP without installing anything.
4. On the PC **without the shared physical mouse**: open Mouse, with out borders; select **Receive mouse**; click **Start receiving**.
5. Copy this PC's local IPv4 address and pairing key.
6. On the PC **with the mouse**: select **Control mouse**, paste the other PC's IP and pairing key, and select **Right edge** or **Left edge** according to the physical layout. Click **Connect mouse**.
7. Move beyond the selected edge. The pointer should now move on the receiving PC. Move across the opposite receiving edge to return.

Keep both PCs powered on and connected to the same trusted private LAN.

## Option B — assisted install (Windows PowerShell)

Download scripts/install.ps1 from the repository, inspect it, then run from PowerShell in the folder where you saved it:

    .\install.ps1

This downloads release 0.1.1, verifies the SHA-256 manifest shipped with the release, installs without admin rights, then opens the application.

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
- For connectivity, ensure private network profile, correct LAN IP, receiver running and matching 32-character key.
- For a lost connection, the controller's cursor is released after its network heartbeat timeout, normally about 5 seconds.
- Diagnostic logs: %LOCALAPPDATA%\MouseWithOutBorders\diagnostics.log. Do not publish logs containing personal information.
- These scripts change only the **local PC** on which they are run. They cannot install the application on another machine remotely.
