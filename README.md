<div align="center">

<img src="Assets/logo.png" alt="ATS American Roadtrip Car Patcher" width="220">

# ATS American Roadtrip Car Patcher

**Turn an American Truck Simulator truck mod into a Road Trip car — drivable, and sold through
the in-game car dealership.**

[![Release](https://img.shields.io/github/v/release/EVIGIT/ATS-American-Roadtrip-DLC-mod-patcher?display_name=tag&sort=semver&color=ff8a3d)](https://github.com/EVIGIT/ATS-American-Roadtrip-DLC-mod-patcher/releases/latest)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Windows](https://img.shields.io/badge/Windows-0078D4?logo=windows&logoColor=white)](https://learn.microsoft.com/windows/win32/winforms/)
[![License](https://img.shields.io/badge/license-GPL--3.0-8B5A2B)](LICENSE)
[![Build](https://img.shields.io/badge/tests-142%20%2B%2088%20%2B%20smoke-4CAF50)](Tests)

</div>

---

Converts an American Truck Simulator **truck** mod into a **Road Trip car** mod, so the vehicle
can be driven and sold through the Road Trip car dealership instead of the truck one.

**The tool is free.** There is no paid tier, no account and nothing to buy.

> **Community tool.** Not affiliated with, endorsed by, or supported by SCS Software.

---

## Contents

- [What it does](#what-it-does)
- [Quick start](#quick-start)
- [Options](#options)
- [Dealer badges](#dealer-badges)
- [Definitions-only patch mode](#definitions-only-patch-mode)
- [Themes](#themes)
- [Requirements](#requirements)
- [Downloading a release](#downloading-a-release)
- [Building and testing](#building-and-testing)
- [Project layout](#project-layout)
- [How it works](#how-it-works)
- [Development notes](#development-notes)
- [Command-line version (any OS)](#command-line-version-any-os)
- [Reporting problems](#reporting-problems)
- [License](#license)

---

## What it does

Take an existing truck mod and produce a `.scs` that loads as a Road Trip car:

- Moves `def/vehicle/truck` to `def/vehicle/car`, rewriting every definition path that pointed
  at the old one.
- Rewrites the truck dealer into `def/vehicle/car_dealer/<dealer-id>`.
- **Assigns the car to that dealer**, by renaming its unit to `vehicle.<dealer-id>.<model>`.
  This is the step that decides everything, because ATS does not use a `brand` attribute at all:
  the dealership comes from the middle component of the car's own unit name. The base game's
  F-150 is `accessory_truck_data : vehicle.ford.f150_23` under `car_dealer/ford`, while a
  truck-era mod has no brand component in that name at all — which is why an unconverted Volvo
  S90 turns up under an unrelated brand. Only the unit name changes; the definition folder keeps
  the mod's own name, because the mod refers to it by path and renaming it without rewriting
  every reference crashes the game.
- **Namespaces the anonymous units**, so two converted mods cannot overwrite each other.
  In-game `_nameless.` units are global, so identical names mean the second mod silently replaces
  the first. Namespaces come from the input file name (guaranteed distinct per mod) and are
  truncated to a legal 8 characters plus a 4-character hash — SCS allows at most 12 characters
  per dot-separated component, and real mod names run to 30 or more.
- Keeps the dealership badge working, and **makes its background transparent** instead of
  leaving a black plate behind it. See [Dealer badges](#dealer-badges).
- Adds the extra `info[]` line and `tags[]` entry a car definition needs.
- Gives car interiors a `speed_limiter_value` from the chosen vehicle type
  (sedan 190 km/h, hatchback 180, pickup 163, van 140).
- Retargets the eight known camera slots (`behind`, `interior` + oculus, `bumper`, `window`,
  `cabin`, `wheel`, `top`) onto the generic `camera.*.car` units the base game ships, so a
  converted truck's bespoke camera names still resolve in car mode.
- Optionally moves the physical assets from `vehicle/truck` to `vehicle/car` (full conversion).
- Replaces the definitions an earlier conversion left behind, so re-converting a mod produces one
  definition set per vehicle rather than a duplicate car.
- Writes `roadtrip_conversion_report.txt` next to the output, listing every change, every
  definition path it wrote, and any stale truck references it found.

Each of those decisions is driven by what ATS actually does, measured from the game's own files
rather than assumed — see [How it works](#how-it-works).

---

## Quick start

1. **Download and run.** Grab the latest `ATS-American-Roadtrip-Car-Patcher-win-x64.zip` from
   [Releases](https://github.com/EVIGIT/ATS-American-Roadtrip-DLC-mod-patcher/releases/latest)
   and extract it anywhere. There is no installer; run the `.exe`.
2. **Pick your mod.** On the input row, browse to a truck mod `.scs` (or `.zip`). *Recent*
   reopens the folder you used last time.
3. **Choose an output folder** and a **dealer ID**, e.g. `volvo`. The window shows the exact
   file that will be written before you commit to anything.
4. **Leave the defaults alone.** Full conversion, keep the mod's own brand, copy the logo — all
   on, because that is the combination that works.
5. **Click *Patch for Road Trip*** and watch the log.
6. **In ATS**, disable the original truck mod and enable the converted `<mod>_roadtrip.scs` in
   the same slot. Then confirm the car appears under your dealer in the car dealership.

For a first conversion, keep *Move vehicle assets* enabled. If you hit a texture problem the
report file names it — that is the main reason it is written.

---

## Options

| Option | Default | What it does |
| --- | --- | --- |
| **Definitions-only patch** | **Off** | Writes a small patch instead of a full conversion. **Known bug, see [below](#definitions-only-patch-mode).** |
| **Move vehicle assets** | On | Moves `vehicle/truck` assets to `vehicle/car` and rewrites the paths. Turning it off risks missing textures. |
| **Use Road Trip car cameras** | On | Retargets the eight camera slots onto the base game's `camera.*.car` units. |
| **Avoid clashes with other mods** | On | Namespaces the global `_nameless.` units so two converted mods cannot overwrite each other. |
| **Keep the mod's own brand** | On | Uses the brand already in the mod, so the existing badge resolves by name. Turn off to rename the dealer to the Dealer ID instead. |
| **Add the logo to a renamed dealer** | On | When the dealer *is* renamed, copies the logo file to the new name. Only matters if the option above is off. |

The last two interact: both on is the normal case, and they only conflict if you convert the same
mod twice under two different brand tokens, which overrides two base-game logos at once.

CLI equivalents: `--patch-only`, `--no-move-assets`, `--keep-cameras`, `--custom-dealer`,
`--no-logo`.

---

## Dealer badges

A dealership badge is resolved **by file name**, and ATS has *two* separate logo folders — the
truck dealer reads `material/ui/brand_logo/<brand>.mat`, while the car shop reads
`material/ui/car_brand_logo/<brand>.mat`. Both have to be right, and getting it wrong produces
three distinct symptoms:

| Symptom | Cause | What the tool does |
| --- | --- | --- |
| Purple or blank square | The mod only ships the truck-era logo, so the car shop finds nothing | The logo is written to **both** folders on every conversion |
| Nothing renders at all | The dealer was renamed, so the badge file no longer matches its name | Keep the mod's own brand, or copy the logo to the new name |
| Black rectangle behind the badge | Truck-era logos are **DXT1** with black baked into the pixels, and DXT1 has no alpha channel for the game to drop | Re-encoded as **DXT5** with a transparent background |

That last case is handled by `BrandLogoAlpha.cs`, and the approach matters:

- **The background is found by filling inwards from the image border**, not by keying every dark
  pixel. The BMW roundel is black and white and the Volvo badge has a black interior, so a global
  "make black transparent" pass would punch straight through the artwork. Enclosed black is
  preserved, and anti-aliased edges are faded rather than cut so no dark rim appears.
- **The artwork is copied, not re-compressed.** A DXT5 colour block is the same BC1 block DXT1
  uses, so the original colour blocks are copied across byte for byte and only the alpha block in
  front of each one is generated. Re-fitting the four palette entries from decoded pixels was
  measured doing real damage — up to 251 per channel, with Volvo's red coming back green.
- A logo that **already has an alpha channel**, or one this build cannot decode, is left
  byte-identical.

> **Known cosmetic gap.** Badges are converted, not re-laid-out: a wide logo can still look
> stretched or small next to the base-game badges, and a coloured logo keeps its colour rather than
> being greyscaled to match. Normalising aspect ratio, size and colour to match the stock badges is
> scheduled for v1.4.

---

## Definitions-only patch mode

**Known bug — this mode is off by default.** `--patch-only` (CLI) or the "Definitions-only patch"
switch (GUI) writes a small patch to load above the original mod. It currently writes an **empty
definition over the original mod's `truck_dealer` entry**, overriding that definition with nothing.
ATS then refuses to load any save that has driven the car, reporting `invalid_vehicle`. Confirmed
in game across several mods; the proper fix is scheduled for v1.3.9.1.

Until then, **use full conversion** (the default). It has a separate and lesser problem — some
textures may not convert, and full conversion still omits the shared `automat/` and
`material/ui/accessory/` roots — but it leaves you with a drivable, correctly-dealt car rather
than a car that cannot be loaded at all.

Patch mode remains available for anyone who specifically wants the original mod left untouched.
Selecting it in the GUI now shows a warning before anything is written. It produces
`<mod>_roadtrip_patch.scs` containing the converted definitions, copied and converted models,
textures and sounds, and empty overrides for the original `truck_dealer` entries.

**If you do use it, load the patch above the untouched original mod.**

## Themes

There are 12 themes — Roadtrip, Daylight, Steel, Ember, Obsidian, Crimson, Midnight, Evergreen,
Lagoon, Sandstone, Aurora and Vapor — and **every one of them is unlocked**.

There is no account, no sign-in and no paid tier. Pick any of them in Settings → Customization.

---

## Requirements

**To run it:** Windows, and nothing else. The release zip is framework-dependent, so it needs
the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) installed. The app
can install its own updates from inside Settings once it is running.

**To build it:** Windows and the .NET 8 SDK. This is a Windows Forms project, so it must be
built on Windows.

The command-line converter under `Cli/` has no Windows dependency and also runs on Linux and
macOS.

---

## Downloading a release

Grab `ATS-American-Roadtrip-Car-Patcher-win-x64.zip` from the
[releases page](https://github.com/EVIGIT/ATS-American-Roadtrip-DLC-mod-patcher/releases/latest),
extract it, and run the `.exe`. No installer, no registry entries.

The app can also update itself in place from **Settings → Advanced → Install latest GitHub
release**. It uses your existing `gh auth login` session and never stores a token of its own.

---

## Building and testing

    dotnet build ATSRoadTripConverter.sln -c Release -p:TreatWarningsAsErrors=true --no-incremental

`--no-incremental` matters: an ordinary incremental build reports "0 warnings" simply because
nothing recompiled. Use it whenever you want a real signal.

Three headless suites, none of which need a display:

| Suite | Checks | Covers |
| --- | --- | --- |
| `Tests/ModConverterVerify` | 142 | end-to-end conversion, dealership binding, badge transparency, camera retargeting, re-patching, reference merging, truck→car migration |
| `Tests/SettingsMigrationVerify` | 88 | settings schema versioning and migration, window layout arithmetic, launch-page changelog parsing |
| `Tests/LocalUpdaterSmokeTest` | smoke | the updater's folder validation and copy script, using temp folders only |

    dotnet run --project Tests/ModConverterVerify/ModConverterVerify.csproj -c Release
    dotnet run --project Tests/SettingsMigrationVerify/SettingsMigrationVerify.csproj -c Release
    dotnet run --project Tests/LocalUpdaterSmokeTest/LocalUpdaterSmokeTest.csproj -c Release

Helper scripts live in `scripts/`: `extract-mod.ps1` inspects a mod, `inspect-save.ps1` prints the
structure of a save or game archive without exposing profile values, and `changelog.py` appends
changelog entries.

## Project layout

The UI was split out of `Program.cs` in v1.3.7. `Program.cs` is now just the entry point.

| File | Role |
| --- | --- |
| `Program.cs` | entry point |
| `ConverterForm.Layout.cs` | main window construction and layout |
| `ConverterForm.Conversion.cs` | conversion, logging, settings UI |
| `MainLayout.cs` | the window's vertical budget, as testable arithmetic |
| `LaunchForm.cs` | the startup quick start / "what's new" window |
| `ReleaseNotes.cs` | parses `CHANGELOG.md` for the launch page |
| `MainPage.cs` | the restorable page enum and its tolerant JSON converter |
| `Theme.cs`, `Controls.cs` | palette and the hand-drawn themed controls |
| `Settings.cs`, `SettingsSchema.cs` | persistence, schema version and migration |
| `GitHubReleaseClient.cs` | changelog and release lookup via GitHub CLI |
| `ThemedConfirmForm.cs`, `ThemedIcon.cs` | dialogs and chrome |
| `ModConverter.cs` | conversion logic |
| `BrandLogoAlpha.cs` | makes a dealer logo's background transparent (DXT1 → DXT5) |
| `ScsArchive.cs`, `HashFsArchive.cs`, `CityHash.cs` | ZIP and HashFS archive reading |
| `LocalUpdater.cs` | self-update helper script |
| `Cli/` | the `ats-roadtrip-convert` command line tool |
| `Tests/` | the three headless verification suites |

`SettingsSchema.cs`, `ReleaseNotes.cs`, `MainLayout.cs`, `MainPage.cs` and `BrandLogoAlpha.cs`
are deliberately **dependency-free**. That is what lets a headless suite cover the fiddly parts —
version ordering, window arithmetic, a malformed settings file, a BC1 codec — without dragging in
WinForms, and it is why they are linked into the test projects rather than referenced.

---

## How it works

Two archive formats, because ATS uses two:

- **ZIP.** `ScsArchive.cs` reads the central directory directly and trusts its actual compression
  method and sizes, rather than the local headers. Some real mods (including the supplied Volvo
  S90) have local headers and central directory that disagree on flags and method fields, and
  plain `ZipFile.OpenRead()` rejects those. Output is always repacked as a normal archive.
- **HashFS.** Official DLC files, `base.scs`, and mods packed with the SCS packer use SCS's native
  `SCS#` format, not ZIP. `HashFsArchive.cs` reads v1 and v2, so an official Road Trip
  `dlc_car.scs` works as a reference file. *Limitation:* HashFS v2 stores textures in a packed GPU
  format, so `.tobj`/`.dds` entries are skipped and listed in the log; definitions and models
  extract normally.

What the converter does to the mod:

- moves `def/vehicle/truck` → `def/vehicle/car`, and optionally `vehicle/truck` → `vehicle/car`
- rewrites `/def/vehicle/truck/...` → `/def/vehicle/car/...`, and the asset paths to match
- translates common legacy truck accessory unit names to their car equivalents
- retargets the eight camera slots to the generic Road Trip `camera.*.car` units
- translates `def/vehicle/truck_dealer` into `def/vehicle/car_dealer/<dealer-id>`
- renames the car unit to `vehicle.<dealer-id>.<model>` so the game files it under that dealer
- namespaces global anonymous units, truncated to the legal 12 characters
- discovers the mod's own brand and, by default, keeps it so the existing badge resolves
- when the dealer *is* renamed, copies `material/ui/brand_logo/<brand>.mat` to the new name, and
  makes the background transparent in both logo folders
- preserves the source dealer accessory list instead of inventing one from hard-coded filenames
- optionally merges missing `car_dealer` framework files from a reference mod
- validates stale truck references and writes `roadtrip_conversion_report.txt`
- replaces definitions a previous conversion left behind, then repacks as `<original>_roadtrip.scs`

**Current Road Trip context.** SCS released the Road Trip Ford DLC on 29 September 2026, and the
official purchase flow uses a dedicated Car Dealer tab, with car configuration described as a
separate workflow from the truck dealership. Community testing shows `def/vehicle/car` as the
relevant definition path. For exact current definitions, a reference mod from your own installed
setup remains the safest source — which is why the tool accepts one.

---

## Development notes

- Open `ATSRoadTripConverter.code-workspace` in VS Code. Do not open a single source file, or a
  parent folder that merely contains this project.
- **Terminal → Run Task → Hot Reload and run ATSRoadTripConverter** starts the app under
  `dotnet watch`. Only the app launched by the watcher can receive hot edits.
- The app shells out to your local `gh auth login` session to read the remote changelog and to
  check and download releases. It never stores a GitHub token. Without GitHub CLI installed it
  falls back to the embedded `CHANGELOG.md`, and GitHub updates are unavailable.
- Any new logic that can be expressed without WinForms belongs in a dependency-free file, so a
  headless suite can cover it. See the note under [Project layout](#project-layout).

### Releasing

1. Set `Program.AppVersion` to the tag, exactly.
2. Add a `## <tag>` section to `CHANGELOG.md`.
3. Commit, then push the branch and `main` **explicitly** — a bare `git push` targets
   `origin/main` because that is the branch's upstream.
4. Push the tag with the same name.

`.github/workflows/publish-release.yml` builds the Windows app, packages
`ATS-American-Roadtrip-Car-Patcher-win-x64.zip`, and publishes the `## <tag>` section as the
release notes. The workflow fails if `AppVersion` and the tag disagree, or if the changelog
section is missing.

### Main window

1. The exact input `.scs/.zip` path, with Browse and a Recent button for the previous folder.
2. The exact output folder and the full filename that will be created.
3. A dealer ID text box.
4. An optional Road Trip reference mod.
5. The [options](#options) listed above.
6. A live conversion log and progress bar, with Copy log, Clear and Open output folder.
7. A themed settings screen reachable from the header, with General, Customization, About and
   Advanced pages.

The app also opens with a short **launch page**: a four-step quick start on a first run, and
"what's new" pulled from `CHANGELOG.md` on later ones.

---

## Command-line version (any OS)

`Cli/` is a console build of the same converter with no Windows dependency, so it also runs on
Linux and macOS:

    dotnet run --project Cli -- --input "Volvo S90.scs" --output out --dealer volvo [--reference dlc_car.scs]
    dotnet run --project Cli -- --extract dlc_car.scs --output extracted

| Flag | Effect |
| --- | --- |
| `--input`, `--output` | required |
| `--dealer <id>` | dealer ID; ignored unless `--custom-dealer` |
| `--reference <file>` | Road Trip reference mod to merge framework files from |
| `--vehicle-type <t>` | `sedan`, `hatchback`, `pickup` (default) or `van` — sets the speed limiter |
| `--patch-only` | definitions-only patch mode (**known bug**, see above) |
| `--custom-dealer` | rename the dealer to `--dealer` instead of keeping the mod's brand |
| `--no-logo` | do not copy the logo to a renamed dealer |
| `--no-move-assets` | leave `vehicle/truck` assets in place |
| `--no-dealer` | skip dealer translation entirely |
| `--keep-cameras` | do not retarget the camera slots |
| `--extract` + `--defs-only` | unpack any ZIP or HashFS `.scs`; `--defs-only` takes just manifest.sii and `def/vehicle/...` |

Dealer branding defaults to keeping the mod's own brand, which needs no `--dealer` at all.

Exit codes: `0` success, `2` completed with validation issues, `3` unhandled exception.

`scripts/extract-mod.ps1` is a thin helper for inspecting mods from any directory, reading the
archive straight off disk and writing readable `.sii`/`.sui` text to a scratch folder:

    .\scripts\extract-mod.ps1 -List                   # every .scs in the ATS mod folder
    .\scripts\extract-mod.ps1 -Mod 'Ford_F250'        # substring match, definitions only
    .\scripts\extract-mod.ps1 -Mod 'Ford_F250' -Full  # include models/textures

---

## Reporting problems

Include the **`roadtrip_conversion_report.txt`** written next to the output — it lists what was
changed, every definition path written, and every stale reference found, which is normally enough
on its own. If the problem only appears in game, attach the relevant part of
`Documents\American Truck Simulator\game.log.txt` (search for the vehicle's unit name).

Please state which ATS version and which mod file, and whether the problem happens on a full
conversion or a patch.

---

## License

[GPL-3.0](LICENSE). Free to use, modify and redistribute under those terms.

This project is an unofficial community tool. It is not affiliated with, endorsed by, or
supported by **SCS Software**. *American Truck Simulator* and the Road Trip DLC are the property
of SCS Software; this repository contains no game assets.

## Further reading

- [CHANGELOG.md](CHANGELOG.md) — the full release history.
- [ROADMAP.md](ROADMAP.md) — what is planned for v1.3.9.1 and v1.4, including the known issues
  this release leaves open and why they were left.
- [Releases](https://github.com/EVIGIT/ATS-American-Roadtrip-DLC-mod-patcher/releases) — downloads.
