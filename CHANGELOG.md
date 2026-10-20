# Changelog

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
