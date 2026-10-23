# Verification record and manual acceptance checklist

This file distinguishes automated checks from hardware testing. Do not claim a feature works on two physical PCs until observed.

## Automated (GitHub Actions / Windows x64)

- [x] .NET Windows Forms restore, build, and self-contained publish (prior build on 2026-10-20)
- [x] Inno Setup compiler produces Setup.exe (prior build on 2026-10-20)
- [x] LAN protocol smoke tests: successful authenticated TCP exchange and rejection of wrong key (prior build on 2026-10-20)
- [ ] **v0.1.1** complete CI including silent install/uninstall verification; see Actions run for current result
- [ ] SHA-256 manifest attached to v0.1.1 release, after publication

CI checks only what it runs. A successful build does not prove smooth mouse interaction.

## Connection and input regression checks

Run `dotnet run --project tests/MousePeer.SmokeTests/MousePeer.SmokeTests.csproj -c Release`.
The suite also checks a receiver that never sends its challenge, a receiver that
stops after receiving the pairing proof, cancellation and socket cleanup. Input
tests use a fake native sender to verify event layout, rejected input with and
without a Windows error code, and continued cleanup after a failed button release.
These checks do not inject mouse events into the desktop.

Pairing checks cover generated 8-character codes, case/spacing normalization,
invalid input, migration of existing local keys without changing connection
preferences, legacy 32-character keys, and authenticated round trips with both
key formats.

Run `dotnet run --project tests/Pairing.UiTests/Pairing.UiTests.csproj -c Release`
on Windows to check that both roles are visible, the receiver offers a direct
enter-code action, the connecting role exposes editable IP/code fields, entered
details survive role changes, and the action stops an active listener before
switching roles. Settings writes are replaced with an in-memory callback, and
the test listener uses an available loopback port so an installed app can stay open.

Local verification on 2026-10-23: Windows Release build, self-contained x64
publish, all transport/input smoke tests (including short and legacy keys),
and the pairing UI checks passed. Both setup screens were rendered and inspected.
This does not replace the physical two-PC checks below.

## Physical two-PC acceptance — not yet performed

- [ ] Setup.exe installs on PC A and PC B
- [ ] App appears in Start menu and tray; uninstaller removes it
- [ ] IP/key pairing connects only with correct key
- [ ] Both PCs initially show a code; the mouse PC's enter-code action opens the IP/code fields
- [ ] After upgrading an existing receiver, its new 8-character code pairs and persists after restart
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
- [ ] A Windows input rejection appears in receiver status/logs and returns the controller's cursor

## Bug reports

File an issue at https://github.com/aesgalexis/mouse-without-borders/issues containing:

1. App version on both PCs.
2. Windows version on both PCs, physical screen arrangement and monitor resolution.
3. Whether mouse clicks, movement or wheel failed (and in which direction).
4. Private LAN network details **without revealing public IPs, pairing keys or other secrets**.
5. Steps to reproduce and whether disconnect/reconnect recovered.

Attach sanitized diagnostic logs only when necessary. Do not commit personal pairing details, IP addresses from your home network, or log files.
