# ATS American Roadtrip Car Patcher

Current App Version: v1.2

## Unreleased

### Added
- Added authenticated GitHub changelog and release updates through GitHub CLI.
- Added a tag-triggered GitHub Actions workflow that builds and publishes the Windows update package.
- Added a VS Code watch-and-run task for faster development updates.
- Added a manual local-build updater to Advanced Settings.
- Added a Windows updater smoke test using temporary install/build folders.
- Added an in-window changelog page with navigation back to the converter.
- Added an in-window settings page with navigation back to the converter.

### Fixed
- Added a visible Back action to the Settings header.

### Changed
- Added more top spacing to changelog release sections.

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