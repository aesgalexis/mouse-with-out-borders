# Mouse, with out borders

**One mouse, two Windows computers.** A small app that makes the physical mouse plugged into one PC cross onto another PC on the same local network. Only mouse movement, buttons and wheel are shared.

- Switch by pushing through the **left or right edge** of the first PC's Windows desktop.
- Return by pushing across the opposite edge of the second PC.
- No keyboard, clipboard, files, remote desktop or account required.
- Per-user Windows installer and uninstaller; also available as a portable executable.
- Tray icon, option to launch with Windows, option to connect automatically, saved device pairing.
- Authenticated connection with heartbeat detection and automatic reconnection after an established connection drops.

**Project status: preview.** Windows compilation is automated; real two-PC interaction still needs hardware testing. This is not a production/stability guarantee.

## Install on both PCs

1. Download MouseWithOutBorders-0.1.1-Setup.exe from the [latest Release](https://github.com/aesgalexis/mouse-with-out-borders/releases) once a release is published, or download the MouseWithOutBorders-Windows-x64 artifact from [Actions](https://github.com/aesgalexis/mouse-with-out-borders/actions).
2. Run the setup on **both Windows 10/11 x64 PCs**. No administrator rights are needed to install for the current user.
3. On the PC you want to **receive** the mouse, select **Receive mouse**, then **Start receiving**. Copy the displayed LAN IP and pairing key.
4. On the PC with your physical mouse, choose **Control mouse**, enter the receiver IP and pairing key, and select which edge leads to the second PC. Click **Connect mouse**.
5. Push your physical mouse off that edge. The pointer now moves, clicks and scrolls on the second PC. Push through its opposite edge to come back.

For an assisted Windows installation or rollback, see [installation instructions](docs/INSTALLATION.md). For automated checks vs. pending two-PC tests, see [verification record](docs/TESTING.md). Changes are tracked in [CHANGELOG.md](CHANGELOG.md).

On the PC that will **receive the mouse**, you can run the checked-in `scripts/install.ps1 -Receiver` script after downloading it from the repo. On the controlling PC use `scripts/install.ps1` without the receiver switch. The script verifies the GitHub Release SHA-256 manifest and installs into the current Windows user's profile. It does not remotely control or install software on any other machine.

The pairing key is generated once per user and saved in the settings file under %LOCALAPPDATA%\MouseWithOutBorders. It is **not** reset on every launch. Never publish a pairing key, diagnostic log or settings file.

The app runs in the **notification area** by default when its window is closed. Use its tray menu's **Exit** to actually quit. You can turn off "Keep running..." in Settings. Check "Start with Windows" to launch the program when you sign in, and "Connect / listen automatically" to restore its previous role.

## Network and security

Both PCs must be on the same **trusted private network** with TCP port **42187** reachable on the receiver.

If Windows Firewall blocks the receiver, allow the application on **private** networks when prompted. To add a rule manually, run PowerShell as administrator on the receiving PC:

    New-NetFirewallRule -DisplayName "Mouse With Out Borders" -Direction Inbound -Action Allow -Profile Private -Protocol TCP -LocalPort 42187 -Program "$env:LOCALAPPDATA\Programs\MouseWithOutBorders\MouseWithOutBorders.exe"

The connection requires matching pairing keys, uses challenge-based HMAC authentication and verifies every packet. Mouse packets are **not encrypted**. Do **not** use this on public or hostile networks; never open or port-forward TCP 42187 to the internet. The listener binds to the local PC's network interfaces; use Windows Firewall to limit access.

Settings are stored in the signed-in user's LocalAppData folder; Windows account security protects access to those files. No cloud service is used.

## Supported / limits

- Exactly **two PCs**, Windows 10/11 x64, with interactive user sessions.
- Left/right screen arrangement; the outer edge is based on the Windows virtual desktop, so other monitors on a PC are taken into account. Complex non-rectangular display arrangements need additional testing.
- Raw mouse deltas, wheel, primary and X buttons are supported.
- Windows security boundaries (UAC prompts, locked/secure desktop, elevated applications) can restrict input injection.
- Some mice, DPI settings, remote sessions, exclusive-input games, network policies and antivirus software may behave differently. Physical two-PC testing is still necessary.
- The origin PC releases its cursor on disconnect or heartbeat timeout (normally within ~5 seconds).

## Troubleshooting

- **Cannot connect:** confirm both PCs are on the same LAN, the receiver IP is correct, and Windows Firewall allows TCP 42187 on a private network.
- **Pairing rejected:** copy the complete 32-character key from the receiving PC again.
- **Cursor does not cross:** make sure both apps report connected, and move through the *outer* edge selected on the controlling PC.
- **App disappeared:** check the Windows notification area (system tray); double-click its icon to reopen.
- **Diagnostics:** choose **Open diagnostic log**. Logs are under %LOCALAPPDATA%\MouseWithOutBorders and rotate at approximately 512 KB.

## Development

Requires **Windows**, .NET 8 SDK, and (to build the installer locally) [Inno Setup 6](https://jrsoftware.org/isinfo.php).

    dotnet build MouseWithOutBorders/MouseWithOutBorders.csproj -c Release
    dotnet publish MouseWithOutBorders/MouseWithOutBorders.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/app
    & ([IO.Path]::Combine([Environment]::GetFolderPath('ProgramFilesX86'), 'Inno Setup 6', 'ISCC.exe')) installer\MouseWithOutBorders.iss

The icon is generated automatically on Windows by the MSBuild project target. CI on windows-latest generates the icon, builds and publishes the portable ZIP and installable EXE as build artifacts. A Git tag such as v0.1.1 triggers publication of a GitHub Release.

The assisted installer prompts for Windows admin approval only if `-Receiver` is selected to add a private-network firewall rule; the application itself does not request elevation and installs in the current user's %LOCALAPPDATA%\Programs directory. Uninstall retains personal settings by design; these can be removed from LocalAppData manually.

Source code is intentionally small and only targets mouse sharing. Unrelated sharing features are out of scope.
