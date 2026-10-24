# Changelog

## 0.1.6 — Automatic startup, reconnection and administrator restart

- Enable **Start with Windows** and automatic connection/listening by default. Save the Windows startup preference, restore saved pairing at sign-in, and preserve explicit opt-outs.
- Skip automatic connection for incomplete pairing details and show offline-PC retry status instead of a misleading mouse-input error.
- Add an explicit **Restart as administrator...** action in the window and tray
  for Task Manager and other elevated windows that reject remote mouse input.
  Preserve pairing and resume active sharing; cancelling UAC keeps sharing running.
- Explain the Windows input privilege restriction and require a local device for
  UAC prompts and locked screens.

## 0.1.5 — Local keyboard remapping

- Add mouse-operated input/output key selectors, add/edit/remove controls, a pause switch, and an optional F11 → Backspace / F12 → Enter preset.
- Save per-user mappings and restore them when MWB starts, independently of the mouse connection.
- Handle key repeat, swaps, shared destinations and physical target keys; release injected keys when changing mappings, pausing or exiting.
- Run keyboard capture on a separate message thread, filter generated input and report Windows injection failures without logging typed keys.
- Display layout-specific OEM key names and distinguish Enter from Numpad Enter. Keyboard input remains local and is never sent to the other PC.

## 0.1.4 — Cursor visibility, consistent icons and dark interface

- Hide the parked cursor on the PC just left, on both crossing and returning. Restore it for local input, stopping and application exit.
- Use a small non-activating cursor window rather than changing the Windows cursor scheme or display counter.
- Load dedicated embedded icons for the application window, tray and header instead of relying on Explorer's cached associated icon.
- Install a versioned icon file for shortcuts and refresh existing desktop shortcuts pointing to this installation, including custom shortcut names.
- Apply explicit dark rendering to tray menus, selectors, checkboxes, role buttons and disabled/hover states; brighten secondary text and support scrolling on shorter screens.

## 0.1.3 — Two-way mouse sharing and updates

- Replace the app icon with a flat blue cursor crossing an open screen edge, generated from a transparent SVG at Windows icon sizes from 16 to 256 pixels.
- Add a manual **Check & install updates** button with the current version, download progress and retryable errors.
- Find the highest newer GitHub Release version, including published previews, and verify the installer's SHA-256 manifest before launching Setup.
- Close the app only after the verified installer starts; reopen it after successful installation without restarting Windows or losing pairing settings.
- Close the installed MWB automatically before updates, including when it is hidden in the notification area.
- Request a clean exit first, then terminate older or unresponsive instances from the same installation and Windows session.
- Apply the same shutdown when uninstalling and stop before changing files if MWB cannot be closed.
- Fix the desktop context menu appearing when returning across the shared edge: release only buttons actually pressed by MWB, and ignore unmatched releases.
- Capture mouse movement, clicks and scrolling on both PCs after one pairing; connecting/listening no longer determines which PC can share its mice.
- Include Windows pointer input from built-in touchpads and other mouse devices, while filtering MWB's own input to prevent feedback.
- Synchronize the shared screen edge, let local input reclaim control, and resolve simultaneous crossings and late packets.
- Require both PCs to run the new two-way protocol, with an explicit update message when connecting to an older version.

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
