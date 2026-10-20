# Verification record and manual acceptance checklist

This file distinguishes automated checks from hardware testing. Do not claim a feature works on two physical PCs until observed.

## Automated (GitHub Actions / Windows x64)

- [x] .NET Windows Forms restore, build, and self-contained publish (prior build on 2026-10-20)
- [x] Inno Setup compiler produces Setup.exe (prior build on 2026-10-20)
- [x] LAN protocol smoke tests: successful authenticated TCP exchange and rejection of wrong key (prior build on 2026-10-20)
- [ ] **v0.1.1** complete CI including silent install/uninstall verification; see Actions run for current result
- [ ] SHA-256 manifest attached to v0.1.1 release, after publication

CI checks only what it runs. A successful build does not prove smooth mouse interaction.

## Physical two-PC acceptance — not yet performed

- [ ] Setup.exe installs on PC A and PC B
- [ ] App appears in Start menu and tray; uninstaller removes it
- [ ] IP/key pairing connects only with correct key
- [ ] Moving right across PC A outer edge reaches PC B
- [ ] Moving left across PC B edge returns to PC A
- [ ] Primary, secondary, middle and X button clicks reach PC B
- [ ] Vertical and horizontal wheel reach PC B
- [ ] With inverted layout, left crossing from PC A reaches PC B and returns through right
- [ ] Cursor does not get stuck on PC A after receiver power-off / Wi-Fi loss
- [ ] Auto-connect succeeds when PC A starts before PC B
- [ ] Closing window keeps app in notification area; Exit ends process and releases cursor
- [ ] Start with Windows and autoconnect persist across sign-out/reboot
- [ ] Two monitors on one PC, different resolutions, and DPI scaling behave acceptably
- [ ] Windows Firewall rule only allows private LAN; uninstall removes optional rule
- [ ] No unintended keyboard, clipboard or file-sharing functionality
- [ ] UAC/secure desktop limitations communicated accurately

## Bug reports

File an issue at https://github.com/aesgalexis/mouse-with-out-borders/issues containing:

1. App version on both PCs.
2. Windows version on both PCs, physical screen arrangement and monitor resolution.
3. Whether mouse clicks, movement or wheel failed (and in which direction).
4. Private LAN network details **without revealing public IPs, pairing keys or other secrets**.
5. Steps to reproduce and whether disconnect/reconnect recovered.

Attach sanitized diagnostic logs only when necessary. Do not commit personal pairing details, IP addresses from your home network, or log files.
