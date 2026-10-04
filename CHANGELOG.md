# ATS American Roadtrip Car Patcher

Current App Version: v1.3.9.1

## v1.3.9.1

### Fixed
- **Option descriptions were cut off at larger font sizes.** The options rows were a fixed 62px, but the Font Size setting scales every font from 8 to 16 while control sizes stay put. Measured against the real description strings at the real 322px text width, font size 11 needs 65px, 12 needs 68, and 16 needs 108 — so at 11 and above all six option descriptions were truncated. It was silent because `TextRenderer` ellipsises with "..." instead of throwing, so nothing looked broken. The row height is now measured from the wrapped text and the options card grows to match, with the window following.
- The default is unchanged: at the default font size the measured path reproduces the previous 62px rows, 416px card and 1080px window exactly, so 1080p is unaffected for anyone who has not changed the setting.

### Changed
- SettingsMigrationVerify grew from 88 to 100 checks, covering the measured row height: that it reproduces the hand-verified card and window heights at the default font size, that it grows rather than clips at font sizes 11 and 16, that it never shrinks as the text grows, and that the last row still ends inside the card.

### Known issues in this release
- **Dealer badges are transparent, but not re-laid-out.** Verified in game: the black rectangle is gone from both the Volvo and the BMW badge. What remains is cosmetic — a wide logo can still look stretched (the BMW roundel) and a narrow one small (the Volvo badge) beside the base game's badges, and a coloured badge keeps its own colours rather than being greyscaled to match. Both were measured: the logos are correct DXT5 files that place the artwork on a canvas twice as wide as it needs, so the game scales the canvas and the logo lands at half the available size. Cropping the empty margin and greyscaling are scheduled for v1.4.
- **Definitions-only patch mode can still produce `invalid_vehicle`.** It is off by default and full conversion is unaffected; the proper fix is scheduled for v1.3.9.2.
- **Full conversion still omits the shared `automat/` and `material/ui/accessory/` asset roots**, which is the cause of the occasional unconverted texture. Scheduled for v1.3.9.2.
- The 1080p layout is no longer outstanding. The outstanding item from v1.3.9 was to look at the option switches at half width, and that is what surfaced this release's fix.

## v1.3.9

### Fixed
- **Fixed "invalid_vehicle" when loading a save after patching.** SCS unit names are dot-separated components of at most 12 characters each, but this release's anonymous-unit namespace (added below, to stop cars replacing each other) was taken from the mod's file name, and real names are far longer — `cadillac_ct5_v_black_wing_2022` is 30 characters, `ford_f_150_raptor_2017_v1_7_1_beta` is 34. Every anonymous unit in the patch was written with an illegal name, so the conversion looked completely clean and then failed in game the moment a save referenced one of those vehicles. Namespaces are now truncated to 8 readable characters plus a 4-character hash of the full name, which keeps them unique *and* legal: the two Cadillacs share the prefix "cadillac", so the hash is what keeps them apart. Verified on the real mod, whose patch now emits `_nameless.cadilla_f0g5.*` instead of a 30-character name.
- Mod names that are already within the limit are left untouched, so existing patches keep their unit names.
- The 12-character limit is now also enforced where names are written, so an illegal namespace fails the conversion loudly instead of shipping a patch that only breaks in game.
- **Fixed converted cars duplicating over other patched mods.** The anonymous-unit namespace was derived from the dealer ID, assuming one dealer equals one vehicle. Two Cadillac mods in circulation are both `truck_dealer/cadillac`, two Fords are both `ford`, and one mod declares eight brands at once, so they collapsed onto one namespace and overwrote each other's units — the very bug the namespacing exists to prevent. The namespace now comes from the input file name, which is guaranteed distinct per mod. The trailing game version is stripped so one vehicle keeps one namespace across updates.
- A logo file that exists but is empty is now reported instead of copied. An empty placeholder produces a correctly named badge that renders as nothing, which looks identical to the dealer binding being wrong. Empty logo files are also left out of patches, so a patch can no longer put a blank badge over a working one.

- **Definitions-only patch mode is now OFF by default, and is labelled as having a known bug.** In game testing it was found to write an empty definition over the original mod's `truck_dealer` entry, which overrides that definition with nothing and makes American Truck Simulator refuse to load any save that has driven the car (`invalid_vehicle`). Full conversion is now the default. The switch still exists — it is the only mode that leaves the original mod untouched — and choosing it now shows a warning explaining the problem before anything is written. `--patch-only` on the command line still works exactly as before.
- The convert button and status line no longer say "PATCHING..." regardless of mode; they say "CONVERTING...".
- **An update is now announced instead of only logged.** Previously a newer release produced a single line in the conversion log, which nobody reads at startup. It now raises a themed prompt offering to open Settings, where the existing installer lives. Dismissing it remembers that specific release tag, so the same version is not offered twice but a genuinely newer one still is.
- **The app can reopen the page you left.** New "Reopen the last page on launch" option in Settings, off by default because silently opening on Settings instead of the converter would be a surprise. It remembers the Settings page *and* which of its four categories you were on.
- An unrecognised value in that remembered page no longer risks your settings. A hand-edited or newer-build value previously threw while the file was being read, which discards the whole settings file and resets every preference; it now falls back to the main view.

### Added
- **Fixed the dealership showing a purple square instead of a badge.** ATS has two separate logo folders: the truck dealer reads `material/ui/brand_logo/<brand>.mat` and the car shop reads `material/ui/car_brand_logo/<brand>.mat`. A truck mod ships its logo under the truck folder, which is correct while it stays a truck and useless once it is sold as a car — so every converted car had no badge. The logo is now also written to `car_brand_logo/`, on every conversion including the "keep the mod's own brand" default, which is exactly the case that was broken. The `.mat`, `.tobj` and `.dds` travel together, and the original truck-era logo is kept because the `.tobj` refers to it by absolute path.
- Dealer logos now work. A dealer's badge is resolved by file name, so renaming the dealer silently lost the logo. Two options are now offered, both on by default:
  - "Keep the mod's own brand" (the default) uses the brand the mod already has, read from its `def/vehicle/truck_dealer/<brand>` folder, so its existing logo is found by name and nothing has to be copied. Turning this on ignores whatever is typed in the Dealer ID box, and the log says so.
  - Turning that off renames the dealer as before, and the mod's logo file is copied to the new dealer's name so the badge is kept. The copy is byte-for-byte the original: the `.mat` names its texture by bare file name, so the copy keeps pointing at the texture the mod already ships and no compiled `.tobj` is ever rewritten.
- **Fixed the dealership badge showing a black box around the artwork.** The badge resolved correctly after the fix below, but a truck-era logo is an opaque DXT1 image with the black background baked into the pixels, and the car shop draws that as a black plate behind the badge. DXT1 has no alpha channel at all, so the game has no way to drop it and the box can only be removed in the file. Logo textures are now re-encoded as DXT5 — the format the base game's own badges use — with the background made transparent. Verified against the two mods in the report: Volvo's 128x64 DXT1 logo is 78% pure black and BMW's 256x64 DXT1 logo is 70% black, while the working reference (`Ford Focus Mk3`'s `ford.dds`) is DXT5 with 73.9% of its pixels fully transparent. That single difference was the whole of the black box.
- The artwork itself is copied, not re-compressed. A BC1 block holds only four palette entries, and re-fitting them from decoded pixels was measured doing real damage to the actual logos — up to 251 per channel, with Volvo's red coming back green. The original colour blocks are copied across byte for byte and only the alpha block in front of each one is built, so the badge is provably identical to what the mod author shipped. Pinned by a test that compares the surviving pixels and the raw blocks against the source.
- Only the background is made transparent, and it is found by filling inwards from the image border rather than by a blanket "make black transparent" pass. That distinction matters: the BMW roundel is black and white and the Volvo badge has a black interior, so a global key would punch straight through the artwork. Interior black is kept, and anti-aliased edges are faded rather than cut so no dark rim appears around the lettering.
- A logo that already carries an alpha channel, or one this build cannot decode, is left byte-identical. Both logo folders are fixed, because the `.tobj` holds an absolute path back into `brand_logo/` and leaving that copy opaque would keep the box even after the car-shop copy was corrected.
- **Fixed a conversion crash on a mod that already ships `material/ui/car_brand_logo/`.** The logo lookup prefers that folder, which made the source and destination the same file, and copying a file onto itself throws `IOException` on Windows — so the entire conversion failed rather than the one file being skipped. Reachable by converting a mod that has been converted before, and by any mod released with both folders. The copy is now skipped when source and destination are the same path; the logo is already in the right place, and the transparency pass still runs so a pre-existing car-shop logo is fixed too.
- A definitions-only patch now carries `material/ui/brand_logo` with it. Patch mode previously shipped `def/` and `vehicle/` but nothing under `material/`, so the logo would have been dropped on the floor and the fix above would have done nothing in the recommended mode.
- The conversion report records the mod's own brand token and any logo file that was added, and warns when a dealer was renamed without one.
- The settings file now records a schema version. Existing settings are migrated once on first launch and stamped with the new version, so a future settings format change can be handled deliberately instead of by guesswork. This release only adds the version - it does not change the settings format.
- A new SettingsMigrationVerify suite covers the settings migration, including that a damaged or hand-edited settings file is treated as older rather than trusted as current.
- A launch page now appears at startup. On a first run it walks through the four steps of a conversion; on every later run it lists the release notes for anything newer than the version you last ran, so a silent update no longer changes things without telling you. Turn it off permanently in Settings → Advanced → "Show the launch page at startup". This replaces the old GitHub sign-in window, which existed only to gate themes.
- The launch page is now sized to its content instead of using a fixed height, so the quick start no longer scrolls. Text is measured with the real font metrics rather than an estimate.
- The launch page content no longer sits inside a sunken panel that looked like a disabled text box, and each step is now numbered so the quick start reads as an ordered sequence.
- The launch page can no longer be closed with the title-bar X, Escape or Alt+F4. Skip is the only way out, so it cannot be dismissed by accident.
- Fixed the Skip button on the launch page being clipped, and the message text overlapping it, when there were no newer release notes to show. The window now keeps a minimum height that fits its own footer.
- Fixed one of the option switches disappearing when the main window was widened horizontally. The two side-by-side switches both grew from their left edge, so past a certain width they overlapped and one painted over the other.
- The Settings "Accounts" tab is now "About". It carries the app name and version, a note that the tool is free with no account, a disclaimer that this is an unofficial SCS community tool, and a button to open the project on GitHub.
- The conversion report now lists the output archive and every definition path it wrote, plus a note explaining that two enabled Road Trip patches for the same vehicle cause the car to load twice. This is the check for a duplication problem the tool cannot detect by itself, because the colliding archive is a separate file it never sees.
- **Fixed cars replacing each other in the car dealership.** In-game, `_nameless.` unit names are global, and two converted mods could ship identical ones, so the second mod silently overwrote the first and its car took the other's place. Every conversion now gives its global anonymous units a unique prefix, so two converted cars can no longer collide. The prefix comes from the mod's file name rather than the dealer ID, because several mods share a brand and two of them would then collide again. New "Avoid clashes with other mods" option, on by default; the report lists every renamed unit.
- **Fixed a converted car showing up in the wrong dealership.** ATS does not use a `brand` attribute to decide which dealer sells a car. It reads the middle part of the car's own unit name: the base game's Ford F-150 is `accessory_truck_data : vehicle.ford.f150_23` and lives under `car_dealer/ford`. Truck-era mods have no brand part at all (`vehicle.vols90`), so the game had no dealer to match and listed the car under an unrelated brand - a converted Volvo S90 was appearing in the BMW dealer. Every conversion now renames the unit to `vehicle.<dealer-id>.<model>`. The report lists each car that was assigned.
- The README now describes the current app instead of v1.3.2. It had been claiming the repository was private, and describing `Program.cs` as the "complete WinForms interface" after the code had been split into separate files.

### Changed
- The internal "ThemeMode" setting is gone. It had been read only by the migration that maps it to "ThemeName"; that mapping now happens during migration instead. Your chosen theme is unaffected.
- **There is no longer any account or sign-in.** All 12 themes are unlocked for everyone, and the "Sign in with GitHub" window, the token storage and the theme tiers are all gone. The tool is free with nothing to buy and nothing to log into.
- ModConverterVerify grew from 31 to 49 checks, now covering the reference dealer merge and the truck-to-car migration so that class of bug cannot return unnoticed.
- ModConverterVerify grew again to 90 checks, covering dealer brand discovery and the dealership logo files in all three combinations (keep the brand, rename with a logo copy, rename without one), then to 112 checks covering the 12-character unit-name limit using the real mod names from the download set — the specific failure that shipped in this release. The main window's options card was repacked onto its existing rows to fit the two new switches, which keeps the default window at 1080px rather than pushing it to 1150px and scrolling on a standard display.
- SettingsMigrationVerify grew from 25 to 88 checks, now also covering the launch page's changelog parsing, the window's vertical budget arithmetic and the tolerant JSON converter for the remembered page.
- Internal cleanup only, no behaviour change: every source file now declares only the namespaces it actually uses. The duplicated block of eight `using` lines that was copied across every file has been removed, and three files no longer need any at all.
- Warning and information pop-ups now match the rest of the app instead of appearing as plain system dialogs. This covers a failed GitHub update, trying to update while a Hot Reload session is open, and an invalid accent colour. The wording is unchanged.
- The window now fits smaller screens. On a display too short to show everything at once, the page scrolls instead of cutting the conversion log off the bottom, and the window no longer opens taller than the screen. It also works on a smaller display after being sized on a larger one.
- Fixed: the "Copy log" button in the log card was hidden underneath the "Open output folder" button and could not be clicked. It has been misplaced since v1.3.5.
- The main window no longer opens with a scrollbar on a normally sized desktop.
- Removed the Ko-fi supporter tier entirely. The tool is now free with no paid unlock path. All 12 themes are unlocked for everyone. The "Sign in with Ko-fi" buttons and the Ko-fi section in Settings are gone.

### Known issues in this release
- **Dealer badges are made transparent, but not re-laid-out.** A converted badge no longer shows a black rectangle, however the game's badge slot still draws it at a fixed size and aspect. A wide logo therefore still looks stretched (the BMW roundel), and a narrow one looks small (the Volvo badge) beside the base game's Ford, Dodge and RAM badges. Separately, a coloured badge keeps its colour: BMW and Volvo are still in their own blue and black-and-white schemes rather than the greyscale treatment the stock badges use. Normalising all three — aspect ratio, drawn size and greyscale — is deferred to v1.4. It is deliberately not done with a blanket colour or aspect filter here, because the transparency pass is required to be provably lossless to the artwork (see above) and any of the three would break that guarantee.
- **Definitions-only patch mode can still produce `invalid_vehicle`**, as described above. Off by default, full conversion is unaffected.
- **Full conversion still omits the shared `automat/` and `material/ui/accessory/` asset roots**, which is the cause of the occasional unconverted texture. Carried over from earlier releases; scheduled for v1.3.9.1.
- **The 1080p layout has not been looked at by eye.** The arithmetic is asserted by SettingsMigrationVerify, but the two new option switches have not been confirmed side by side at half width.

## v1.3.8

### Added
- Camera retargeting: converted `def/vehicle/car/*/data.sii` files now point the eight known camera slots (`behind`, `interior` + oculus, `bumper`, `window`, `cabin`, `wheel`, `top`) at the generic Road Trip units shipped in the base game (`camera.*.car`, e.g. `camera.bumper.car`). New GUI toggle "Use Road Trip car cameras" (on by default); CLI opt-out with `--keep-cameras`. Already-stable `*.car` / `*.suv` (DLC) units are never overwritten.
- A "Recent" button beside "Browse" on the input row reopens the folder used for the previous conversion and lists the `.scs`/`.zip` archives in it, preselecting the last one when it still exists. The folder is remembered rather than the filename, because mod archives get renamed or re-downloaded often enough that a remembered path would go stale immediately.
- A failed conversion now logs `[INFO] Partial work folder kept for inspection: <path>` so the output folder holding partial work can be found without guessing. The path is remembered for the next run even when "auto-save settings" is off.

### Fixed
- Re-patching a mod that already contains a previous conversion no longer duplicates the car. The freshly converted definitions now replace the copies the earlier patch left behind, instead of being parked beside them as .truck_source files - which left two definitions for one vehicle inside the packed archive.
- Legacy .truck_source leftovers written by older builds are deleted during conversion instead of being packed into the output archive.

## v1.3.7.2

### Fixed
- The local updater reports *why* a copy failed instead of only "Robocopy exit code 16". The helper script ran under `$ErrorActionPreference = 'Stop'` while piping robocopy's output to `Out-Null`; robocopy writes failures on stderr, so those errors could abort the copy, and the discarded output removed the only diagnostic. Robocopy output is now merged into the updater log, `$ErrorActionPreference` is relaxed around the robocopy call so its native exit code survives, and both the build and install folders are validated before robocopy runs.

### Changed
- The updater helper log now records the file count being copied, both folder paths, and the exit code of every retry attempt.

## v1.3.7.1

### Fixed
- Sign-in wording no longer promises that signing in unlocks "the supporter themes". Signing in with GitHub unlocks the GitHub tier, so the window title and the signed-in status line now say what actually happens.
- The GitHub tier prompt is titled "GitHub theme" rather than the ambiguous "Supporter theme".

### Changed
- Removed seven orphaned XML doc comments left behind in the v1.3.7 file split. Comment-only; no behaviour change.

## v1.3.7

### Changed
- `Program.cs` is no longer a single ~4,000 line file. Code is split by responsibility with no behaviour change: `Program.cs` (entry point only), `Theme.cs`, `Controls.cs`, `Settings.cs`, `AuthSession.cs`, `GitHubSignIn.cs`, `ThemedIcon.cs`, `SignInForm.cs`, `ThemedConfirmForm.cs`, and `ConverterForm` split into `ConverterForm.Layout.cs` (fields and layout) and `ConverterForm.Conversion.cs` (conversion, logging and settings).

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
