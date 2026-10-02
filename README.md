# ATS-American-Roadtrip-DLC-mod-patcher

A Windows desktop application that converts ATS truck-slot vehicle mods for Road Trip.

## Current version

`v1.2`

## Build and run

Requirements: Windows and the .NET 8 SDK.

```powershell
dotnet build ATSRoadTripConverter.csproj -c Release
```

Run `dotnet run --project ATSRoadTripConverter.csproj --configuration Debug` to launch the app. In VS Code, use the **Hot Reload and run ATSRoadTripConverter** task for `dotnet watch`.

## Changelog and releases

The in-app changelog reads `CHANGELOG.md` from this repository and falls back to the embedded copy when offline.

For an authorized updater, install GitHub CLI and sign in with `gh auth login`. The updater downloads the latest Windows release package from this public repository; it does not store a GitHub token.

To publish a release, update `Program.AppVersion` and add a matching `## vX.Y` section to `CHANGELOG.md`. Commit and push those changes, then create and push a matching tag. For example:

```powershell
git tag v1.3
git push origin v1.3
```

The tag-triggered GitHub Actions workflow checks the version and changelog section, publishes the app, packages `ATS-American-Roadtrip-Car-Patcher-win-x64.zip`, and attaches it to the GitHub Release. The app's **Settings → Advanced → Check GitHub for updates...** action checks for a newer release.

## Updater smoke test

On Windows, run:

```powershell
dotnet run --project Tests/LocalUpdaterSmokeTest/LocalUpdaterSmokeTest.csproj
```

The test uses temporary build and installation folders and does not modify an installed app.