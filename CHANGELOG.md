# Changelog

## Unreleased — Installer updates

- Close the installed MWB automatically before updates, including when it is hidden in the notification area.
- Request a clean exit first, then terminate older or unresponsive instances from the same installation and Windows session.
- Apply the same shutdown when uninstalling and stop before changing files if MWB cannot be closed.

## 0.1.2 — Clear pairing and connection recovery

- Replace the hidden role dropdown with two visible role buttons and a direct action to enter the other PC's code, including while listening.
- Generate saved 8-character pairing codes, migrate existing local keys, accept lowercase/formatted input and retain connections to receivers with legacy keys.
- Add pairing-code and setup UI regression checks.
- Correct GitHub download URLs and include native runtime libraries in the single-file portable app and installer.
- Limit controller connection and pairing to five seconds, while preserving user cancellation.
- Report Windows mouse input failures and close the affected peer so the controller releases its cursor.
- Attempt every mouse button release during cleanup, logging failures without stopping the receiver.
- Add regression checks for stalled pairing, cancellation, rejected input and button cleanup.

## 0.1.1 — Windows installer and operational setup

- Assisted local Windows installation and uninstall PowerShell scripts.
- SHA-256 checksums for release assets, verified during assisted installation.
- GitHub Actions silent Setup.exe install/uninstall smoke test.
- Installation, rollback, firewall and two-PC validation documentation.
- Clear distinction between automated tests and unverified real-hardware behavior.

## 0.1.0 — Initial Windows preview

- Native Windows Forms UI with per-user saved role, IP, pairing key and layout preference.
- Mouse-only cross-PC movement, buttons and wheel over a paired LAN connection.
- Connection heartbeat, lost-connection recovery and reconnect support.
- System tray and optional Windows startup.
- Self-contained x64 build and per-user Inno Setup installer.
- Automated LAN authentication/packet round-trip smoke tests.
