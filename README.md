# ATS American Roadtrip Car Patcher v1.3.2

This package is a complete .NET 8 WinForms project, rebuilt from the v2 project you supplied.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full history. The in-app changelog reads the embedded `CHANGELOG.md`, so add changes and fixes under **Unreleased** and rebuild to include them; to append to the correct unreleased section run `python scripts/changelog.py --added "Adds..." --fixed "Fixes..." --changed "Updates..."`.

## Development

Use **Terminal → Run Task → Hot Reload and run ATSRoadTripConverter** to start the app with `dotnet watch`. Supported code edits apply while it is running; changes that require a restart are rebuilt and relaunched. This only works for the app launched by the watcher; a regular Build task or an already-open standalone app cannot receive edits.

This repository is private. Install [GitHub CLI](https://cli.github.com/) and run `gh auth login` on each authorized computer. The app uses that sign-in to read the remote changelog and check/download releases; it does not store a GitHub token. If GitHub CLI is unavailable, the app uses its embedded changelog, while GitHub updates require signing in.

To publish a release, update `Program.AppVersion` and add a matching `## vX.Y` section in `CHANGELOG.md`, then push a tag with the same name (for example, `v1.3`). The GitHub Actions workflow builds the Windows app, packages `ATS-American-Roadtrip-Car-Patcher-win-x64.zip`, and publishes the section as release notes. The updater checks for a newer release and installs that package.

## Updater smoke test

On Windows, run `dotnet run --project Tests/LocalUpdaterSmokeTest/LocalUpdaterSmokeTest.csproj`. The test uses temporary folders and exercises the updater's folder validation and copy script without modifying an installed app.

## Converter test

Run `dotnet run --project Tests/ModConverterVerify/ModConverterVerify.csproj` to exercise the converter on a synthetic truck mod. It checks that the configured vehicle-type speed limits and `tags[]` entry are applied case-insensitively, that `def/vehicle/truck` is converted to `def/vehicle/car`, and that the truck dealer is translated to `car_dealer`.

## What is included

- `ATSRoadTripConverter.csproj` - the actual .NET project
- `ATSRoadTripConverter.sln` - Visual Studio solution
- `Program.cs` - complete WinForms interface
- `ModConverter.cs` - conversion logic
- `ScsArchive.cs` - SCS/ZIP-compatible archive reader for nonstandard `.scs` headers
- `.vscode/launch.json` - VS Code debugging profiles
- `.vscode/tasks.json` - VS Code build tasks
- `.vscode/settings.json` and `.vscode/extensions.json`
- `ATSRoadTripConverter.code-workspace` - recommended VS Code workspace

## Important: open the correct folder

After extracting this ZIP, open the folder containing:

    ATSRoadTripConverter.csproj
    ATSRoadTripConverter.sln
    Program.cs
    ModConverter.cs
    ScsArchive.cs
    .vscode

In VS Code, opening `ATSRoadTripConverter.code-workspace` is the safest option.

Do not open an individual source file first and do not open a parent folder that merely contains this project.

## Requirements

- Windows
- .NET 8 SDK
- Visual Studio 2022, or VS Code with C# Dev Kit

The project is Windows Forms, so it must be built on Windows with the Windows desktop targeting support.

## UI changes from the previous versions

The main window now shows:

1. The exact input `.scs/.zip` path.
2. The exact output folder.
3. The full output filename/path that will be created.
4. A dealer ID/showroom text box.
5. An optional Road Trip reference mod.
6. Options to move `vehicle/truck` assets and translate existing dealer definitions.
7. A live conversion log and progress bar.
8. A button to open the chosen output folder after conversion.

## Archive handling

The supplied Volvo S90 package uses nonstandard ZIP metadata: the local headers and central directory do not agree on some ZIP flags/method fields. Standard .NET `ZipFile.OpenRead()` can reject that package.

`ScsArchive.cs` reads the central directory directly and uses its actual compression method and sizes while ignoring the misleading encryption bit used by that SCS archive. Output files are repacked as a normal `.scs` archive.

## SCS HashFS archives

Official DLC files, `def.scs`/`base.scs`, and mods packed with the SCS packer use SCS's native HashFS format (`SCS#` header), not ZIP. `HashFsArchive.cs` reads HashFS v1 and v2 archives, so you can use an official Road Trip DLC `.scs` as the reference file, or convert a HashFS-packed mod.

Limitation: HashFS v2 stores textures (`.tobj`/`.dds`) in a packed GPU format. These are skipped and listed in the log. Definition files (`.sii`/`.sui`) and models are extracted normally.

## Command-line version (any OS)

`Cli/` contains a console build of the same converter, which also runs on Linux and macOS:

    dotnet run --project Cli -- --input "Volvo S90.scs" --output out --dealer volvo [--reference dlc_car.scs]
    dotnet run --project Cli -- --extract dlc_car.scs --output extracted

`--extract` unpacks any ZIP or HashFS `.scs` file. Add `--defs-only` to extract only manifest.sii, `def/vehicle/...`, and car/Road Trip definitions.

To extract definitions from game archives or mods, use the CLI commands above and pass each archive you want to inspect. The batch helper was removed; the CLI keeps the same extraction functionality without requiring a script file.

## Conversion behaviour

The converter:

- moves `def/vehicle/truck` to `def/vehicle/car`
- optionally moves `vehicle/truck` assets to `vehicle/car`
- changes definition references from `/def/vehicle/truck/...` to `/def/vehicle/car/...`
- changes definition references from `/vehicle/truck/...` to `/vehicle/car/...` when asset moving is enabled
- translates common legacy truck accessory unit names to car equivalents
- translates existing `def/vehicle/truck_dealer` definitions into `def/vehicle/car_dealer/<dealer-id>`
- preserves the source dealer accessory list rather than inventing one from hard-coded filenames
- optionally adds missing `car_dealer` framework files from a reference mod
- validates stale truck references and writes `roadtrip_conversion_report.txt`
- repacks the converted mod as `<original>_roadtrip.scs`

## Reference mod

A working Road Trip reference mod is useful for framework definitions because an arbitrary legacy passenger-car mod cannot reliably contain every Road Trip-specific definition introduced by the new system.

The converter therefore copies only missing files under `def/vehicle/car_dealer` from a supplied reference. It does not overwrite the converted vehicle's own definition files.

## Limitations

This is a structural/definition converter. It does not reauthor binary `.pmd`, `.pmg`, `.pmc`, animation, sound bank or material assets.

If the original mod itself is missing an asset or is incompatible with the current ATS car-mode requirements, the converter cannot manufacture that asset.

## Typical use

Choose:

    Input mod:
        C:\ATS Mods\Volvo S90 2020 V2.3 1.60.scs

    Output folder:
        C:\ATS Mods\Converted

    Dealer ID:
        volvo

Then the UI will show:

    C:\ATS Mods\Converted\Volvo S90 2020 V2.3 1.60_roadtrip.scs

Click `Convert to Road Trip`.

For the first test, keep `Move vehicle/truck assets to vehicle/car` enabled and supply a known-working Road Trip reference mod if you have one.

## Current Road Trip context

SCS released the Road Trip Ford DLC on September 29, 2026, and the official Road Trip purchase flow uses a dedicated Car Dealer tab. SCS has also described car configuration/customization as a separate car workflow rather than the older truck dealership flow.

Community testing around current light-vehicle conversions also shows `def/vehicle/car` as the relevant definition path for Road Trip-style car mods.

For exact current game definitions, a reference mod from the user's installed Road Trip-compatible setup remains the safest source.

## Definitions-only patch mode

`--patch-only` (CLI) or the "Definitions-only patch" checkbox (GUI) writes
`<mod>_roadtrip_patch.scs` containing the converted `def/` tree, copied and converted
vehicle assets (models, textures, sounds), and empty overrides for the original
`truck_dealer` entries. Load it **above** the untouched original mod.

In patch mode:
- All definition files are included (not skipped) to ensure compatibility when multiple mods are patched
- Vehicle assets from `vehicle/truck/` are copied to `vehicle/car/` with converted path references
- This ensures textures and models work correctly without requiring asset path changes in the original mod

Car interiors without `speed_limiter_value` get one based on the selected
vehicle type (sedan 190 km/h, hatchback 180, pickup 163, van 140).

Definition files that the mod author encrypted (`3nK`, `BSII`, `ScsC`) are not
decrypted. They are counted as validation issues, because the car cannot work in
car mode until plain-text versions are available (for example from the author).
