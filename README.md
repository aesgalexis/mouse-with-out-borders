# Mouse, with out borders

**All your mice and touchpads, two Windows computers.** Pair the PCs once and use the pointing devices attached to either PC across the shared screen edge. Only mouse movement, buttons and wheel are shared.

- Cross the **left or right edge** using a mouse or touchpad on either PC.
- Return across the shared edge, or move a local pointing device to reclaim that PC.
- Hide the inactive parked pointer, restoring it when returning or using a local device.
- No keyboard, clipboard, files, remote desktop or account required.
- Per-user Windows installer and uninstaller; also available as a portable executable.
- Tray icon, option to launch with Windows, option to connect automatically, saved device pairing.
- **Check & install updates** button with GitHub release downloads, SHA-256 verification and automatic app restart (from 0.1.3).
- Authenticated connection with heartbeat detection and automatic reconnection after an established connection drops.

**Project status: preview.** Windows compilation is automated; real two-PC interaction still needs hardware testing. This is not a production/stability guarantee.

## Install on both PCs

The current release is **0.1.4**, with hidden inactive cursors, consistent app/desktop icons and a readable dark interface. Install it on both PCs for all visual improvements. It shares the two-way protocol with 0.1.3; 0.1.2 uses the incompatible older, one-way protocol.

1. Download [MouseWithOutBorders-0.1.4-Setup.exe](https://github.com/aesgalexis/mouse-without-borders/releases/download/v0.1.4/MouseWithOutBorders-0.1.4-Setup.exe) from the [v0.1.4 release](https://github.com/aesgalexis/mouse-without-borders/releases/tag/v0.1.4). A portable ZIP and SHA-256 checksums are also available there.
2. Run the setup on **both Windows 10/11 x64 PCs**. No administrator rights are needed to install for the current user.
3. On either PC, click **Allow the connection**, then **Start receiving**. Note the displayed local IP and **8-character pairing code**.
4. On the other PC, click **Connect to other PC**, enter that IP and code, and select which edge leads to the first PC. Click **Connect mouse**. If both PCs show a code, **Have the other PC's code? Enter it here** opens the editable connection fields.
5. Use **any mouse or touchpad on either PC** to cross the shared edge. The opposite edge is configured automatically on the listening PC. Move back across the shared edge to return. Moving or clicking a local device takes that PC back from a remote mouse.

For an assisted Windows installation or rollback, see [installation instructions](docs/INSTALLATION.md). For automated checks vs. pending two-PC tests, see [verification record](docs/TESTING.md). Changes are tracked in [CHANGELOG.md](CHANGELOG.md).

The assisted `scripts/install.ps1` script defaults to 0.1.4. It verifies release SHA-256 checksums and installs only on the current PC; `-Receiver` adds the optional private-network firewall rule on the PC accepting the connection.

The pairing code has **8 letters and digits**, without ambiguous characters such as `0`, `O`, `1` or `I`. Lowercase input and pasted spaces or hyphens are accepted. It is saved per user under %LOCALAPPDATA%\MouseWithOutBorders and stays the same between launches. Existing 8-character codes are retained during this update. Upgrading from 32-character local keys generates a short local code; the parser also accepts 32-character keys, but **both apps must support the new two-way protocol**. Never publish a pairing code, diagnostic log or settings file.

The app runs in the **notification area** by default when its window is closed. Use its tray menu's **Exit** to actually quit. You can turn off "Keep running..." in Settings. Check "Start with Windows" to launch the program when you sign in, and "Connect / listen automatically" to restore its previous role.

From version **0.1.3**, click **Check & install updates** at the bottom of the window
to download and install the newest higher version from this repository's GitHub
Releases, including published previews. The app shows download progress, verifies
the release's SHA-256 manifest, then starts Setup with installation progress and
exits. Setup reopens the app after a successful update and retains pairing and
preferences. It never restarts Windows. Updates run only when you click the button.
If the check, download or verification fails, the app stays open and you can retry.
When run from a portable copy, this action installs the per-user application;
it does not replace the portable executable. Older versions need one manual
Setup installation to get the button.

## Network and security

Both PCs must be on the same **trusted private network** with TCP port **42187** reachable on the receiver.

If Windows Firewall blocks the receiver, allow the application on **private** networks when prompted. To add a rule manually, run PowerShell as administrator on the receiving PC:

    New-NetFirewallRule -DisplayName "Mouse With Out Borders" -Direction Inbound -Action Allow -Profile Private -Protocol TCP -LocalPort 42187 -Program "$env:LOCALAPPDATA\Programs\MouseWithOutBorders\MouseWithOutBorders.exe"

The connection requires matching pairing keys, uses challenge-based HMAC authentication and verifies every packet. Mouse packets are **not encrypted**. Do **not** use this on public or hostile networks; never open or port-forward TCP 42187 to the internet. The listener binds to the local PC's network interfaces; use Windows Firewall to limit access.

Settings are stored in the signed-in user's LocalAppData folder; Windows account security protects access to those files. No cloud service is used.

## Supported / limits

- Exactly **two PCs**, Windows 10/11 x64, with interactive user sessions.
- Left/right screen arrangement; the outer edge is based on the Windows virtual desktop, so other monitors on a PC are taken into account. Complex non-rectangular display arrangements need additional testing.
- Windows mouse/touchpad pointer movement, vertical/horizontal wheel, primary and X buttons are supported on both PCs.
- A handoff waits for locally held buttons to be released so a local click/drag is not split between desktops.
- Windows security boundaries (UAC prompts, locked/secure desktop, elevated applications) can restrict input injection.
- Some mice, DPI settings, remote sessions, exclusive-input games, network policies and antivirus software may behave differently. Physical two-PC testing is still necessary.
- The origin PC releases its cursor on disconnect or heartbeat timeout (normally within ~5 seconds).

## Troubleshooting

- **Cannot connect:** confirm both PCs are on the same LAN, the receiver IP is correct, and Windows Firewall allows TCP 42187 on a private network.
- **Both PCs show a code:** on either PC, click **Connect to other PC** or **Have the other PC's code? Enter it here**. Enter the listening PC's local IP and code, and click **Connect mouse**.
- **Update both PCs:** two-way input requires the new version on both computers; 0.1.2 cannot use this protocol.
- **Pairing rejected:** enter the receiving PC's current 8-character code again, especially after an upgrade.
- **Cursor does not cross:** make sure both apps report connected, and move through the *outer* edge selected on the controlling PC.
- **App disappeared:** check the Windows notification area (system tray); double-click its icon to reopen.
- **Diagnostics:** choose **Open diagnostic log**. Logs are under %LOCALAPPDATA%\MouseWithOutBorders and rotate at approximately 512 KB.

## Development

Requires **Windows**, .NET 8 SDK, and (to build the installer locally) [Inno Setup 6](https://jrsoftware.org/isinfo.php).

    dotnet build MouseWithOutBorders/MouseWithOutBorders.csproj -c Release
    dotnet publish MouseWithOutBorders/MouseWithOutBorders.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/app
    & ([IO.Path]::Combine([Environment]::GetFolderPath('ProgramFilesX86'), 'Inno Setup 6', 'ISCC.exe')) installer\MouseWithOutBorders.iss

The icon is generated automatically on Windows by the MSBuild project target. CI on windows-latest generates the icon, builds and publishes the portable ZIP and installable EXE as build artifacts. After checks pass on main, a new project version publishes a preview release. Version tags also build and publish a GitHub Release.

The assisted installer prompts for Windows admin approval only if `-Receiver` is selected to add a private-network firewall rule; the application itself does not request elevation and installs in the current user's %LOCALAPPDATA%\Programs directory. Uninstall retains personal settings by design; these can be removed from LocalAppData manually.

Source code is intentionally small and only targets mouse sharing. Unrelated sharing features are out of scope.
