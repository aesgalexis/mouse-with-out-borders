# Verification record and manual acceptance checklist

This file distinguishes automated checks from hardware testing. Do not claim a feature works on two physical PCs until observed.

## Automated (GitHub Actions / Windows x64)

- [x] .NET Windows Forms restore, build, and self-contained publish (prior build on 2026-10-20)
- [x] Inno Setup compiler produces Setup.exe (prior build on 2026-10-20)
- [x] LAN protocol smoke tests: successful authenticated TCP exchange and rejection of wrong key (prior build on 2026-10-20)
- [ ] **v0.1.1** complete CI including silent install/uninstall verification; see Actions run for current result
- [ ] SHA-256 manifest attached to v0.1.1 release, after publication

CI checks only what it runs. A successful build does not prove smooth mouse interaction.

## Version 0.1.6 release verification (2026-10-24)

The Windows x64 Release build passed locally with zero warnings and errors.
Self-contained single-file publication and all transport/input, updater, keyboard
and pairing UI suites passed, including the new automatic-startup/retry and
administrator-restart checks. The pairing and remapping screens were captured;
the main window's administrator action and help text were visually inspected.
The installer and portable ZIP are generated with SHA-256 checksums. GitHub
Actions also runs the install/upgrade/uninstall suite in its disposable account
before publishing v0.1.6. Physical two-PC use, actual UAC and sign-out/reboot
remain manual acceptance checks.

## Administrator windows

The pairing UI suite covers the explicit administrator restart, cancellation of
UAC without stopping the listener, launch/settings-save failures, successful exit
from tray mode, elevated status and resuming a listener with AutoConnect disabled.
Elevation is replaced with a callback: automated tests do not request UAC.
Run just these checks with `dotnet run --project tests/Pairing.UiTests/Pairing.UiTests.csproj -c Release -- --elevation-only`.
Local verification on 2026-10-24: Release compilation and these focused UI checks
passed, as did the transport/input smoke suite. The administrator action and help
text were also rendered and inspected. This does not confirm physical-PC behavior.

Manual two-PC check: on the receiving desktop, open Task Manager and check hover,
Close and Minimize using the remote mouse. If blocked in standard mode, use
**Restart as administrator...** on that PC and approve UAC with its local device.
Verify reconnection, hover, Close and Minimize, then local takeover and reverse
handoff. Also cancel UAC once and confirm sharing stays connected. UAC/locked
screens require a local device. These physical checks remain pending.

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

Startup UI checks cover the enabled defaults, compatibility with older settings,
persisted opt-outs, startup-registration failures and checkbox recovery, hidden
automatic listening at sign-in, and incomplete saved pairing. A loopback test
starts the receiver after the first connection failure and verifies that the
hidden connecting app retries automatically. Authentication is left pending so
this check does not install desktop mouse hooks. Registry changes are replaced
with callbacks; the tests do not change the user's Windows startup entry.
These checks passed locally on 2026-10-24, alongside the Release build and the
pairing, updater and keyboard UI checks. Actual sign-out/reboot and physical
two-PC interaction remain manual checks.

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

## Installer upgrade regression checks

Run `dotnet run --project tests/Updater.SmokeTests/Updater.SmokeTests.csproj -c Release`
to check numeric version selection, preview inclusion, pagination, missing assets,
GitHub errors/rate limits, URL validation, SHA-256 verification, download progress,
cancellation and failed-download cleanup. Installer launch is replaced with a
callback so these tests never execute a downloaded program. Add `-- --live` for
a read-only check against the real GitHub release list; add `--download` to also
download and verify the latest installer, with process launch suppressed and
temporary files removed afterward.

The pairing UI suite also checks manual-only updates, displayed version, duplicate
request prevention, retry after failure, preserving the listener when an update
fails, exiting after successful handoff even in tray mode, and cancellation on exit.

The next installer is checked with MWB open as a window, hidden in the
notification area, and running the original v0.1.2 executable. Current builds
must exit cleanly; v0.1.2 exercises the fallback for an older app that does not
understand the installer's exit request. Uninstall is also checked with MWB in
the tray. `tests/Installer.SmokeTests.ps1` performs these checks in the disposable
GitHub Actions account; it refuses to overwrite a different local installation.

The UI tests also send the installer exit message to a hidden window and verify
that the app exits instead of remaining in the notification area.
The installer smoke suite also passes `/MWBUPDATE=1` and verifies that the old
tray process exits and the updated installed executable opens automatically.

## Two-way input regression checks

The transport suite now simulates paired desktops on both left/right layouts.
It checks crossings and returns from both PCs, physical-mouse takeover,
simultaneous crossings, generation-stamped late packets, local held buttons,
remote held-button cleanup, and repeated edge crossings without any injected
button event. Empty cleanup and unmatched right-button releases produce no input.
The TCP checks preserve handoff IDs and origin fields in both directions and
reject an older protocol with an update-both-PCs message.

Windows input emitted by MWB is tagged and filtered so it cannot feed back to the
other PC. Other Windows pointer events remain available to mice and touchpads.
An optional native check can be run on an interactive Windows desktop:
`dotnet run --project tests/Pairing.UiTests/Pairing.UiTests.csproj -c Release -- --native-pointer`.
It sends three tagged test movements of two pixels through a real hook, verifies
the pointer deltas and suppression, restores the original cursor position and
does not press any buttons. This native check passed locally on 2026-10-23.
Physical USB/Bluetooth/touchpad use across two updated PCs still needs acceptance
testing; the simulator and native check do not replace that hardware test.

Local verification on 2026-10-23: Inno Setup 6.7.3 compiled the candidate;
updates from v0.1.2 in the tray and from the candidate both visible and hidden
passed. The older build exercised forced termination; the candidate exited
cleanly in both cases. The published v0.1.2 executable and original settings were
restored afterward, with the pairing code unchanged.

## Physical two-PC acceptance — not yet performed

- [ ] Setup.exe installs on PC A and PC B
- [ ] App appears in Start menu and tray; uninstaller removes it
- [ ] IP/key pairing connects only with correct key
- [ ] Both PCs initially show a code; the mouse PC's enter-code action opens the IP/code fields
- [ ] After upgrading an existing receiver, its new 8-character code pairs and persists after restart
- [ ] Moving right across PC A outer edge reaches PC B
- [ ] Moving left across PC B edge returns to PC A
- [ ] Crossings and returns also work with PC B's own mouse, without a second connection
- [ ] Built-in laptop touchpads cross, click, scroll and return on either PC
- [ ] Repeated crossings with no buttons pressed do not open desktop context menus
- [ ] A local mouse takes control from remote input, including simultaneous movements/crossings
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

## Version 0.1.4 visual and cursor checks

Session tests verify pointer visibility in both crossing directions, on returning,
local takeover, simultaneous crossings and disposal. The optional interactive
`--native-pointer` UI check now also verifies the actual transparent Windows cursor,
foreground preservation, and cursor restoration after disposing the parking window.
The native check passed locally on 2026-10-23. No system cursor scheme is modified.
UI captures cover both pairing modes and the dark tray menu. The installer smoke
suite creates a custom-named legacy desktop shortcut and checks its icon is migrated
to the installed versioned ICO during every upgrade case.
## Version 0.1.5 local keyboard remapping

Keyboard.SmokeTests covers selection validation, JSON persistence, Enter/Backspace,
repeat, swaps, shared destinations, real target-key collisions, disabling and
cleanup, including a failed release. UI tests configure both keys using only
selectors/buttons, reject duplicate inputs, edit/remove, save/cancel, restart and
verify independence from the mouse connection. The optional `--native-keyboard`
UI test exercises the real global keyboard hook and Windows SendInput; a separate
test hook intercepts all generated Enter/Backspace events before user windows.
This native check passed locally on 2026-10-23. No typed text is logged.
