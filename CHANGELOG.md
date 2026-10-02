# ATS American Roadtrip Car Patcher

Current App Version: v1.3.6

## Unreleased

### Added

### Fixed

### Changed

## v1.3.6

### Fixed
- "Install latest GitHub release" no longer closes the app and does nothing. The app now waits for the updater helper to confirm it is running before it closes, and stays open with an explanation if the helper never starts.
- The updater helper now waits on the exact process id instead of the process name, so it cannot mistake another build of the same app for the running one.
- The file copy retries up to five times, because the first attempt could still fail against a scanner holding a file the app had just released.
- The updater now writes a log at every step (started, copied, relaunched, or failed) instead of only on failure, so a silent no-op can be diagnosed. Details: `%TEMP%\ats-roadtrip-local-update.log`.

## v1.3.5

### Added
- General: "Remember window size and position", "Check the selected mod before converting", and "Check for updates automatically" (which records when it last ran and reports a newer release in the log).
- Advanced: "Open the output folder when a conversion finishes", a "Maximum log lines" cap, and a "Reset all settings" button behind a confirmation.
- A "Copy log" button beside "Clear" in the conversion log card.
- The pre-flight check reports a password-protected or corrupted archive, an input that is not an ATS mod, and how many files inside are encrypted.

### Fixed
- The conversion log is now capped at the configured maximum so a very verbose run cannot grow the control without limit.

### Changed
- "Reset all settings" only resets preferences; sign-in state lives in a separate file and is never touched, so resetting cannot change what is unlocked.

## v1.3.4

### Added
- Conversion now detects encrypted mod files up front and reports how many were found (with examples) instead of failing later with a confusing parse error. Encrypted content cannot be read or converted and is skipped.

### Fixed
- "Install latest GitHub release" no longer fails with a "release not found" error. The latest tag is now read from the GitHub REST API because `gh release view latest` reports the wrong result on some setups, even when a published release exists.
- The in-app Changelog now reads the newest published release instead of the repository's default branch, so it no longer hides the latest entry when a release is tagged from another branch.

### Changed
- The window, taskbar and header logo artwork is now tinted to the active theme, so the branding follows the selected palette.

## v1.3.3

### Added
- New "Accounts" settings tab holding GitHub sign-in, sign-out, a greyed-out Ko-fi sign-in placeholder, and a breakdown of what each account level unlocks; it used to sit inside Customization.
- Three theme tiers: Roadtrip, Daylight and Steel are free; Ember, Obsidian and Crimson unlock with a GitHub sign-in; Midnight, Evergreen, Lagoon, Sandstone, Aurora and Vapor are reserved for Ko-fi support once that sign-in exists.
- A "Sign in with Ko-fi" button on the startup sign-in window. It is inert for now and stays Ko-fi pink under every theme.
- Locked theme swatches are outlined in the colour of the tier that unlocks them: gold for the GitHub themes and Ko-fi's pink for the Ko-fi themes, with the padlock tinted to match.
- Locked themes now explain themselves in a themed in-app dialog instead of a system message box: GitHub themes offer the sign-in, Ko-fi themes explain that Ko-fi sign-in is coming soon.

### Fixed
- Applied the configured vehicle-type speed limits (sedan, hatchback, van) during conversion; a capitalized vehicle-type selection from the main window fell through to the pickup limit.
- Fixed the Vehicle Type dropdown going blank in the main window after saving settings.
- Removed the horizontal scrollbar on the Customization tab; the theme description can no longer widen the scrollable page.
- Fixed the locked-theme dialog message running past the right edge of the window.
- Windows title bar now follows the active theme instead of staying dark on the light (Daylight) theme.

### Changed
- Moved account sign-in out of Customization into its own Accounts tab, which also lists the free, GitHub and Ko-fi themes separately.
- Grouped the theme grid by access tier: the three free themes first, then the GitHub-only themes, then the Ko-fi themes.
- Removed an unused definition-file hashing pass that ran on every conversion.
- Made the Advanced settings functional: "Show detailed conversion logs" now filters per-file log lines, "Create a backup of the original mod" copies the input to `<input>.bak`, and "Automatically save settings after conversion" persists the last used dealer, vehicle type, and output folder.

## v1.3.2

### Fixed
- Applied the configured hatchback and van speed limits during conversion.

### Changed
- Installed validated GitHub releases automatically after download without another prompt.

## v1.3.1

### Fixed
- Hid the Unreleased section from the in-app release history.

## v1.3

### Added
- Added authenticated GitHub changelog and release checks through GitHub CLI.
- Added a tag-triggered GitHub Actions workflow that builds and publishes the Windows updater package.
- Added five coordinated app themes with live preview and saved selection.
- Added a VS Code watch-and-run task for faster local development updates.
- Added a Windows updater smoke test using temporary install/build folders.
- Added an in-window changelog page with navigation back to the converter.
- Added an in-window settings page with navigation back to the converter.

### Fixed
- Fixed the Settings header Back action being hidden.
- Fixed custom button corners rendering black on transparent surfaces.
- Fixed the main form's vehicle type selection text not appearing.
- Fixed the Changelog and Settings header labels being truncated.
- Fixed button slivers that disappeared only after hovering.
- Fixed settings content overlapping the category sidebar.
- Fixed custom accent color changes not previewing across the app.

### Changed
- Replaced the Light mode toggle with a saved theme preference.
- Increased spacing between changelog release sections for easier reading.
- Polished conversion option switches and wrapped their descriptions to prevent truncation.
- Kept settings and customization updates aligned with the app-wide theme and surface styling.

## v1.2

### Added
- Added General, Customization, and Advanced settings pages.
- Added live light/dark mode switching with saved theme preference.
- Added accent color selection and live preview.
- Added installed-font selection with live preview and app-wide application.
- Added local changelog display embedded in the application.

### Changed
- Saved font family and size when leaving Settings.
- Kept Save and Cancel controls in a persistent settings footer.
- Changed changelog history to use the local `CHANGELOG.md` instead of Git history.

### Fixed
- Fixed settings content overlapping the category sidebar.
- Fixed custom button corners appearing black on transparent panels.

## v12.1

### Fixed
- Fixed texture breaking when adding multiple mods through the patcher.
- Fixed definition files not working in patch mode.
- Fixed asset path references not being converted in patch mode.
- Fixed vehicle assets not being included in patches.

### Changed
- Included all definition files in patches instead of hash-based skipping.
- Copied vehicle assets from `vehicle/truck/` to `vehicle/car/` in patches.
- Converted asset path references in patch mode.
- Updated UI descriptions for patch behavior.

### Added
- Added support for patching multiple mods together without conflicts.
- Added the Changelog button and version history view.
- Added improved asset path conversion and copying for models, textures, and sounds.
