# ATS American Roadtrip Car Patcher — v1.3.8 / v1.3.9 plan

Status: v1.3.7 released (tag `v1.3.7`, commit `29fdac3`). This document is **uncommitted**.

## Working rules (agreed)

1. **No commit, no tag, no release until the code health check has run and come back
   clean.** Fix findings first, show the updated list, then release.
2. Run the health check as a **baseline before touching the converter**, so any new
   problem is unambiguously ours.
3. UI automation has been unreliable all session. Anything added to the settings
   screens needs a manual click-through by the maintainer before release.
4. Release mechanics that have bitten us:
   - `CHANGELOG.md` must contain a `## <tag>` heading or the workflow fails.
   - `Program.AppVersion` must match the tag exactly.
   - Push explicitly: `git push origin <branch>:<branch>` and `...:main`. A bare
     `git push` targets `origin/main` because that is the branch's upstream.

---

## v1.3.8

### Track A — Code health check (first, gates everything)

- Full solution build with warnings-as-errors, app **and** both test projects.
- Run `LocalUpdaterSmokeTest` and `ModConverterVerify`.
- Scan for dead code, unused members, orphaned `catch { }`, inconsistent naming,
  leftover TODOs / debug hooks, and encoding consistency across the split files.
- Clean up orphaned doc comments left behind when types moved in v1.3.7.
- **Deliverable: a findings list, split minor / major, each with a recommendation.**

#### Baseline result (run before touching the converter, 2026-10-03)

Green, so anything found later is unambiguously ours:

| Check | Result |
| --- | --- |
| `dotnet build ATSRoadTripConverter.sln -c Release -p:TreatWarningsAsErrors=true --no-incremental` | 0 warnings, 0 errors |
| Same for `Cli/ATSRoadTripConverter.Cli.csproj` | 0 warnings, 0 errors |
| `ModConverterVerify` | 13 checks pass |
| `LocalUpdaterSmokeTest` | pass |

Note: the first build reported "0 warnings" only because everything was up to date.
`--no-incremental` is required for a real signal — keep it on every health check.

#### Findings — MINOR

1. **Boilerplate header duplicated into all 12 app files.** Every file starts with the
   same 8 `using` lines plus `// Settings persistence classes` plus a stale DPAPI
   `<summary>` and a "Supporter sign-in state" block that describe `LocalSecretProtector`
   and `AuthSession` (i.e. they belong in `AuthSession.cs`, and even there they no longer
   sit above their types). Dead weight in every file. *Recommend: strip each file down to
   the `using` lines it actually needs and delete the two stray comment blocks. Cosmetic,
   zero behaviour change.*
2. **Encoding is mixed across the split files.** `CityHash.cs`, `HashFsArchive.cs` and
   `ScsArchive.cs` are LF-only; `CityHash.cs` is also the only file with a UTF-8 BOM. The
   other 14 are CRLF without BOM. *Recommend: normalise the three LF files to CRLF and
   drop the BOM.*
3. **`ConverterForm.Layout.cs:24` `private const int Margin_ = 24;`** — the only trailing
   underscore identifier in the codebase. *Recommend: rename to `Margin`. Cosmetic, but
   it is a one-line sed across 16 call sites.*
4. **Three `MessageBox.Show` call sites remain** in `ConverterForm.Conversion.cs`
   (454, 488, 1235) — matches polish item 12. *Recommend: defer to the polish pass; do not
   change dialog wording in the same release as the Track B behaviour change.*
5. **`Settings.cs:127` writes `Debug.WriteLine`** on a save failure. The comment says
   "Silently fail" but it still leaves a debug hook in. *Recommend: keep the silent
   failure, drop or `#if`-guard the write.*
6. **Two empty `catch` blocks in the updater script path** — `LocalUpdater.cs:66` (stale
   ready-marker delete) and `LocalUpdater.cs:109` (ready/log handshake), plus
   `ModConverter.cs:343` and `Program.cs:79` temp-dir deletes. All are intentional
   best-effort, and all four are defensible. *Recommend: leave as-is; they are documented
   by the surrounding comments.*

#### Findings — MAJOR

7. **Seven orphaned XML doc comments remain in `Program.cs`** (lines 16, 58, 68, 70, 76,
   81, 83), left by the v1.3.7 split. The compiler confirms these: building with
   `GenerateDocumentationFile=true` emits **14 × CS1587** ("XML comment is not placed on
   a valid language element"), all pointing at these exact lines. v1.3.7.1 claimed to
   remove orphaned doc comments but only cleared seven elsewhere. `Program.cs` is 86 lines
   of which roughly 20 are real code; the rest is comments describing types that moved.
   *Recommend: delete all seven. This is the one Track A item to fix now — it is
   provable by the compiler, it is zero-risk, and it closes out a changelog claim that is
   currently only half true.*
8. **The CLI project is in no build path at all.** `Cli/ATSRoadTripConverter.Cli.csproj`
   exists, links the four converter sources, and is **not referenced by
   `ATSRoadTripConverter.sln`** — it only builds when someone remembers to build it by
   hand. It shares `ModConverter.cs` with the app and both test projects, so the Track B
   fix will change it too. *Recommend: add it to the solution so the health check covers
   it. Otherwise Track B can silently break the CLI.*
9. **`ModConverter.cs:820` logs a collision backwards.** On a file collision the code
   preserves the existing car file and writes the incoming truck file to
   `<name>.truck_source`, but logs *"Existing car file preserved as {collision}"* — where
   `collision` is the path of the file it just *wrote*, not the one it preserved. The
   message points the user at the wrong file. *Recommend: fix the wording as part of
   Track B, since Track B is exactly this code path.*
10. **The work folder is deleted unconditionally, including on failure**
    (`ModConverter.cs:343`, `finally { Directory.Delete(work, true); }`). This is exactly
    Track C item 2 — when a conversion fails there is nothing left to inspect. *Recommend:
    land as part of Track C item 2.*
11. **No CI build/test gate.** `.github/workflows/publish-release.yml` only runs on a
    `v*` tag and only `dotnet publish`es the app. It never builds the solution, never
    builds the CLI, and never runs `ModConverterVerify` or `LocalUpdaterSmokeTest`. The
    whole test suite is advisory and local-only. *Recommend: add a `push`/`pull_request`
    job that builds the solution with warnings-as-errors and runs both tests. This is the
    single highest-leverage item for stopping regressions like Track B.*

#### Track A go / no-go

Rule 1 says fix findings, show the updated list, then release. Items **7**, **8** and
**11** are worth landing before the v1.3.8 tag; **1**, **2**, **3**, **5**, **9** are
cheap and can ride along; **4** and **6** are cosmetic and can be deferred. **No commit,
tag or release until the maintainer has seen this list.**

#### Fixes landed after the baseline

| # | Finding | Status |
| --- | --- | --- |
| 7 | Orphaned doc comments in `Program.cs` | **Fixed.** All removed. `Program.cs` is 86 → 32 lines. Compiler proof: **CS1587 14 → 0**. |
| 8 | CLI outside the solution | **Fixed.** `Cli/ATSRoadTripConverter.Cli.csproj` added to `ATSRoadTripConverter.sln`, so the health check now covers it. |
| 9 | Backwards collision log message | **Fixed** in `MergeTree`; it now names both files correctly. |
| 1 | Duplicated boilerplate header | **Fixed.** Stale banner + two mis-placed comment blocks removed from all 16 files. |
| 2 | Mixed encoding | **Fixed.** All 17 files are now UTF-8 **no BOM**, CRLF-only, zero bare LF. |
| 5 | `Debug.WriteLine` in `Settings.Save` | Comment corrected; the call is kept (see note below). |
| 3 | `Margin_` naming | **Not done — reverted.** The trailing underscore is *deliberate*: `Margin` collides with the inherited `Form.Margin` and fails to compile with CS0108. Leave it, and add a short comment so the next health check does not "fix" it again. |
| 11 | No CI build/test gate | **Deferred** — workflow change, needs maintainer sign-off. |
| 4 | Three `MessageBox.Show` sites | Deferred to the polish pass. |
| 6 | Empty `catch` blocks | Reviewed; all four are intentional best-effort. No change. |
| 10 | Work folder deleted on failure | Not touched here — it belongs to Track C item 2. |

Re-verified after the fixes: solution (app + CLI + both tests) builds with
`--no-incremental -p:TreatWarningsAsErrors=true` at **0 warnings / 0 errors**;
`ModConverterVerify` 13/13; `LocalUpdaterSmokeTest` passes. Code-style build total
warnings 89 → 82.

**Process note for next time:** two of the health-check scripts damaged files. The
parameterless `File.WriteAllLines` emits a UTF-8 BOM on Windows PowerShell, and the
repair script then mis-restored line 1, breaking the build in 15 files. Recovery was
`git checkout -- '*.cs'`. Always pass an explicit `UTF8Encoding($false)`, and re-run the
full health check after any scripted edit rather than trusting the edit itself.

### Track B — Conversion bug (highest value) — **FIXED**

Symptom: converting a mod that already contains a previous patch **duplicates the car
from the earlier patch instead of adding the new one**.

Root cause confirmed. `MergeTree` in `ModConverter.cs` is the **only**
preserve-on-collision path in the converter — `CopyMissingReferenceFramework` does its
own `File.Exists` check and `ConvertExistingDealers` writes with `File.WriteAllText`, so
the "dealer `index.sii` merge" suspected earlier was never involved. On a collision
`MergeTree` kept the car file already on disk and copied the incoming truck file beside
it as `<name>.truck_source`. On an already-patched mod the *previous* car therefore won,
and the packed archive carried two definitions for one vehicle: `data.sii` plus
`data.sii.truck_source`. That is the duplication.

**Maintainer decision (the rule):** the new conversion **replaces** the old version. The
replacement is scoped *by name* — files only ever land inside the car folder carrying the
same vehicle name as the truck folder being converted, so another mod's car tree is never
touched, and a folder whose name does not match is left completely alone.

Landed in this release:

- `MergeTree` now takes `ConversionStats` and, on a collision, overwrites the stale file
  (`File.Move(..., overwrite: true)`) and logs `[REPLACE] <relative path>`. The
  `<name>.truck_source` sidecar is gone.
- `RemoveLegacyTruckSourceFiles` deletes `*.truck_source` leftovers written by older
  builds immediately after extraction, so they can never be packed into an output
  archive again.
- `ConversionStats.FilesReplacedFromEarlierPatch` is surfaced in
  `roadtrip_conversion_report.txt`.
- Regression test added to `ModConverterVerify` (6 new checks, 13 → **19**, all passing):
  a mod carrying the car tree **and** a `.truck_source` file from an earlier conversion
  must come back with exactly the two converted definitions, the new conversion winning,
  the old car file gone, and no `.truck_source` anywhere.

  **Current total is 31 checks, not 19.** The 12 added after this point are the
  camera-mapping and dealer checks, which landed later in the same release. Verified
  2026-10-03: `dotnet run --project Tests\ModConverterVerify\ModConverterVerify.csproj
  -c Release` prints 31 `PASS` lines, `All checks passed.`, exit 0.

**Proven, not assumed.** With `ModConverter.cs` temporarily reverted to HEAD the new
checks fail with

    FAIL re-patch car tree holds only the 2 converted definitions
         (found 3: example/data.sii, example/data.sii.truck_source, example/interior/example.sii)

and the run exits 1 — the reported symptom reproduced exactly. With the fix the same run
passed every check and exited 0 (it was 19/19 at the time; the suite is 31 checks now —
see the note above). The revert was done with `git stash push -- ModConverter.cs` and
restored with `git stash pop` (post-restore file hash matched the pre-stash copy).

Still open, and **not** part of this fix: nothing. The camera question that was open
here has since been answered and landed — see "Camera retargeting, resolved" below.

#### Getting a mod in front of the developer

The IDE file browser cannot show `%USERPROFILE%\Documents\American Truck Simulator\mod\`
and drag-and-drop does not accept `.scs` binaries. Neither matters: the developer reads
files by **absolute path** through a shell, and a `.scs` is a binary archive that is not
readable as text anyway. Extract first, read second.

    # defs + manifest only, to plain .sii text
    dotnet run --project Cli -c Release -- --extract "<mod.scs>" --output <folder> --defs-only

    # everything (models too)
    dotnet run --project Cli -c Release -- --extract "<mod.scs>" --output <folder>

`scripts/extract-mod.ps1` wraps the `--defs-only` form and locates the repo root itself,
so it can be run from anywhere:

    .\scripts\extract-mod.ps1 -List
    .\scripts\extract-mod.ps1 -Mod 'Ford_F250'

**Decision:** the helper now lives in the repository tree for the release freeze (it used
to be a throwaway in `%TEMP%`). It is a developer convenience only — it is not in the
`.sln`, nothing in the app or CI references it, and it only reads the local ATS mod folder.

Useful archives, all verified working:

| Archive | Format | Result |
| --- | --- | --- |
| `AMG S63_Def_roadtrip_patch.scs` (4 KB) | ZIP | 8 def files |
| `Ford F-150 Raptor 2017 V1.7.1 Beta_roadtrip_patch.scs` (36 KB) | ZIP | ~120 def files |
| `dlc_rt_ford.scs` (142 MB) | HashFS v2 | 3575 entries → 820 def files |

Note the *small* `*_roadtrip_patch.scs` files: they are the converter's own output and
they extract in well under a second, so they are the cheapest way to inspect what the
tool produced. The large `*_roadtrip_optimized.scs` files contain the full model set.

#### Evidence already gathered (first inspection pass)

Two converted patches were compared against each other and against the official DLC.
One clear signal and one **retracted** claim:

**Retracted.** An initial comparison of the F-150 patch against
`dlc_rt_ford.scs → ford.bronco_24` suggested the converter drops `fallback[]` entries and
`@include common_car_data.sui`. That does not hold up: `RAM3500_roadtrip_patch.scs` **does**
carry 16 `fallback[]` entries. The F-150 patch simply predates that behaviour, so the
patches in the mod folder were **produced by different tool versions** and are not
comparable to each other. Do not treat a cross-patch diff as a defect without first
confirming both came from the same build.

#### Camera retargeting, resolved

**Superseded.** This section originally read as an open question. It is now decided and
landed; kept for the history, because the reasoning error here is worth not repeating.

The observation was sound: the DLC uses the Road Trip car cameras, while both converted
patches use base-game `basic`/`behind` cameras.

| Field | DLC `ford.bronco_24` | F-150 patch | RAM3500 patch |
| --- | --- | --- | --- |
| `bumper_camera` | `camera.bumper.car` | `camera.bumper.basic` | `camera.bumper.basic` |
| `window_camera` | `camera.window.car` | `camera.window.basic` | `camera.window.basic` |
| `cabin_camera` | `camera.cabin.car` | `camera.cabin.basic` | `camera.cabin.basic` |
| `wheel_camera` | `camera.wheel.car` | `camera.wheel.basic` | `camera.wheel.basic` |
| `top_camera` | `camera.top.car` | `camera.top.basic` | `camera.top.basic` |

The inference drawn from it was **wrong**. It was read as "probably intentional, not a
bug", on the grounds that the `.car` camera defs ship inside the `dlc_rt_ford.scs` that
the converter does not redistribute — so base-game cameras looked like the safe choice,
and the note said to confirm before changing anything. The premise did not hold: the DLC's
`camera.*.car` units **do** resolve from a mod context.

**Decision:** retarget to the `*.car` units, and leave anything already a `*.car` unit
untouched so DLC-specific variants survive. `MapCamerasToCarUnits` does the rewrite, is
gated on `ConversionSettings.MapCamerasToCarUnits`, and is covered by four checks in
`ModConverterVerify`:

```
PASS bumper_camera retargeted to camera.bumper.car
PASS bespoke bumper camera unit is gone
PASS MapCamerasToCarUnits keeps an existing *.car unit
PASS MapCamerasToCarUnits keeps the DLC camera.behind.suv unit
```

So the two patches in the table above were simply built **before** this landed — the same
different-tool-versions trap as the `fallback[]` retraction immediately above. Nothing was
wrong with those archives; they are stale, not evidence of a defect.

**Also observed, and likely intentional.** Both converted patches leave an empty
`truck_dealer` stub behind:

```
13 bytes   def/vehicle/truck_dealer/ford/f150_2017.sii
13 bytes   def/vehicle/truck_dealer/man/ram.3500.sii
```

13 bytes is exactly `SiiNunit` / `{` / `}` — an emptied placeholder, while the real
definition lands in `car_dealer/...`. Because it appears in two patches built by
different tool versions it is consistent, deliberate behaviour (a neutralised
truck-dealer registration), not a regression. Leave it alone.

**Lesson for the next pass:** the archive is the wrong unit of comparison when the tool
that produced it is unknown. Re-run the converter from the current build on a known input
first, then diff — that is the only way to attribute a difference to the code.

### Track C — Remaining settings, easiest to hardest

1. ~~Reopen last used folder (button)~~ → **landed.** A `Recent` button sits beside
   `Browse` on the input row (`ConverterForm.Layout.cs:551`), backed by a new
   `AppSettings.LastInputPath`. It lists every `.scs`/`.zip` in the folder last used and
   preselects the exact file when it still exists. Deliberately resolves to the *folder*
   rather than the file: mod archives get renamed or re-downloaded often enough that
   remembering a filename alone would go stale immediately.
2. ~~Keep work folder on failure + surface the path in the log~~ → **landed.**
   `ConverterForm.Conversion.cs:113-127` logs `[INFO] Partial work folder kept for
   inspection: <path>` on any conversion exception and records it in
   `AppSettings.LastWorkFolder`. Saved even when `AutoSaveSettings` is off — that flag is a
   UI preference, not a statement about whether a path is still worth keeping.
3. Conflict policy: overwrite / skip / rename — medium, touches the output write path.
   *Partially landed:* re-patch collisions now overwrite by name (see Track B). What is
   still missing is a user-facing choice, and the skip / rename variants.
4. Post-conversion verification (re-read outputs, assert speed limiter + car data
   landed) — medium, new read-back pass.
5. ~~Dry-run preview~~ → **deferred to v1.3.9**.
6. ~~Custom vehicle type editor~~ → **deferred to v1.3.9**.

### Track D — GitHub updater — **diagnostics landed, re-diagnosis still open**

Evidence from `%TEMP%\ats-roadtrip-local-update.log`:

```
Updater started 2026-10-02T21:52:24.78+01:00
FAILED: Copy failed with Robocopy exit code 16.
```

`ats-roadtrip-local-update.ready` also exists containing `ready`.

Reading: the v1.3.6 handshake **works** — the helper starts, signals ready, waits for
the app to exit, and fails loudly instead of silently. Robocopy exit code 16 = serious
error, *not* a locked file. The install location is **not** the cause: both extracted
copies live under `Downloads` and the folder is writable (verified by write/delete test).

**Correction — the locking hypothesis previously recorded here was wrong.** The code is:
`& robocopy.exe $sourcePath $targetPath /E /COPY:DAT /R:2 /W:1 | Out-Null`
- A locked / scanner-held file exhausts `/R:2` and returns **exit 8**, not 16.
- **Exit 16 means no files were copied at all** — invalid arguments, unusable source, or
  an uncreatable destination.

**Why the log was useless:** `| Out-Null` discards robocopy's real stderr, so only the
`throw` string ever reached the log. Fix logging first — you cannot debug discarded output.

#### Second correction — the empty-`$sourcePath` hypothesis is also unlikely

The leading theory recorded above was that `$sourcePath` was empty or null. Reading
`TryCreatePlan` (`LocalUpdater.cs:27-41`) shows the source path goes through
`Path.GetFullPath` and must pass `Directory.Exists` **and** contain both `<app>.exe` and
`<app>.dll` before a plan is ever built. A null path cannot reach robocopy through that
route, so it would fail during plan creation with "Choose existing build and installation
folders", never as a robocopy exit code. Kept as defence in depth, not as the diagnosis.

#### What has landed

- [x] Capture robocopy stdout/stderr into the log instead of discarding it.
      `LocalUpdater.cs:141-168` merges the streams (`2>&1 | Out-String`) and relaxes
      `$ErrorActionPreference` to `Continue` only around the call, because robocopy's
      progress output on stderr would otherwise become a terminating error under the
      script's `Stop` preference and kill the copy before `$LASTEXITCODE` is ever read.
- [x] Validate both paths before invoking robocopy, and log the resolved absolute paths
      and the source file count (`LocalUpdater.cs:118-136`).
- [x] Retry on exit 8 only. Exit ≥ 16 short-circuits via `break` rather than burning
      five attempts on a structural failure.
- [x] **Extend `LocalUpdaterSmokeTest` to cover `waitForAppExit: true`.** It now starts a
      real stand-in process and asserts the copy did not begin before that process exits,
      which is the check v1.3.6 lacked.
- [x] **Re-diagnose from a real run — superseded.** The original exit 16 came from the old
      `| Out-Null` code, which no longer exists, so it cannot be reproduced by retrying.
      Instead the three paths the smoke test never executed were exercised directly
      against the real generated script, with a stand-in app standing in for the GUI:
      - *Relaunch.* Confirmed the updated executable genuinely starts, verified by the
        stand-in writing a marker beside its own binary rather than trusting the log line.
      - *Locked binaries.* A stand-in held `Standin.exe` and `Standin.dll` with
        `FileShare.None` for 5s, which is what a running app does to its own files. The
        helper waited (elapsed ~4.7s against a ~5s lock) and only then copied. This is
        the exact condition v1.3.6 shipped broken in.
      - *Modal error dialog.* Forced a guard failure and confirmed the `Update failed`
        dialog appears, the helper blocks on it rather than exiting silently, the failure
        is logged first, and the logged reason names the offending path instead of
        surfacing a bare exit code.
      12/12 checks passed. Two apparent failures along the way were **test** defects, not
      product defects: the stand-in exited without writing its marker, and the source
      folder was missing `.runtimeconfig.json` so the relaunched process could not start.
      Both are recorded because a failing check here reads exactly like a broken release.
- [ ] **One genuine update on a real installation.** This needs v1.3.8 published, so the
      test is v1.3.7.2 → v1.3.8. The mechanism is verified above; what remains is
      confirming the real app comes back up cleanly on a real desktop.

#### Why v1.3.7.2 exists

v1.3.6 shipped a broken updater and v1.3.7 / v1.3.7.1 inherited it, so those builds
could not self-update. There was therefore no released version with a working updater to
update *from*, which made the update path untestable. v1.3.7.2 ships the updater fix alone
(4 files, 182 insertions, built from `v1.3.7.1`) specifically to provide that starting
point. Released 2026-10-03; workflow run succeeded and the asset is published.

#### What to do on the next real attempt

If it still fails, the log now contains robocopy's own output rather than just a number,
so `%TEMP%\ats-roadtrip-local-update.log` should finally say *why*. Capture it before
concluding anything.

### Release order

---

## v1.3.9

Carried over, then additions.

### Deferred from v1.3.8

1. **Dry-run / preview mode** — report file count and patch manifest, write nothing.
2. **Custom vehicle type editor** — CRUD UI for types that already persist.

### Features
10. **Reverse camera on converted cars.** A Road Trip DLC car (the 2023 Ford F-150) shows a
   rear camera feed on its infotainment screen when reversing; a converted truck does not.
   Traced as far as the data allows:
   - The infotainment screen is configured by `dashboard_path` / `gps_path` in the
     interior's `computers.sui`. Those paths point at real, mod-authorable UI definition
     files (`ui/dashboard/ford_f150_23.sii`, 54 KB) — see the SCS wiki page for
     `accessory_interior_data`, and a forum thread where a modder ships a custom
     dashboard of their own.
   - The F-150's dashboard contains **no reverse-camera element**. Its screen IDs run
     `0 … 1740`, and the documented dashboard ID table goes to 1700 with no rear/reverse
     camera slot. `cam.mat` / `cam.tobj` exist but are referenced zero times by the
     dashboard; they are packed render targets the engine fills.
   - Conclusion: the feed is drawn by engine code keyed off gear and speed. Nothing in a
     `.sii` switches it on.
   - **Why not just repoint `dashboard_path` at the F-150's screen:** the dashboard expects
     Ford's font (`/font/db_ford_f150_2023.font`), Ford's atlas UVs, and a screen mesh in
     the interior geometry. A converted truck keeps a truck interior, so there may be
     nothing to draw on — and a dangling pointer here is a plausible crash on save load.
     Deliberately not attempted without a maintainer decision.
   - A converted car's `dashboard_path` (e.g. `/ui/dashboard/s90.sii`) is dangling in the
     *original* mod too, so this is not a regression introduced by the converter. The
     converter copies it verbatim.
   - **Suspected root cause for converted cars: the interior model.** The screen needs a
     mesh in the `.pmd`; a truck interior has no infotainment screen surface. Unverified.
   - Next step when picked up: author a minimal dashboard `.sii` for a converted car
     rather than borrowing Ford's, and test on a truck mod whose cabin already has a
     screen mesh.

**Process note — stale archives.** `Documents\American Truck Simulator\mod\dlc_rt_ford.scs`
and the copy the game loads are **different files** (142,134,903 vs 142,178,196 bytes).
The mod-folder copy is an older build and ships no `ui/` folder at all, which produced
several wrong "the DLC has no dashboard" conclusions before the discrepancy was found.
`scripts/extract-mod.ps1` now searches both locations and refuses to answer from a stale
copy silently — use `-Game` for anything that shipped with the game.

### Correctness and safety
3. **Real membership verification.** `AuthSession.HasGitHubAccess` is currently just
   `IsSignedIn`, so *any* authenticated GitHub account unlocks the GitHub theme tier.
   `HasKoFiAccess` is still `=> false`. Both need a real backend check. **Biggest gap
   between what the UI promises and what the app enforces.**
4. ~~**`InfoDot` is hardcoded blue.**~~ **Not a bug — checked and retracted.** `InfoDot`
   paints `Theme.Info`, and `Theme.Info` is a deliberate semantic palette constant
   (`Color.FromArgb(110, 168, 254)`) sitting alongside `Success`, `Warning` and `Error`.
   Blue-for-information is intentional, like green-for-success. No change needed unless
   the design direction is to follow the accent colour instead of a status palette.

### Archives
5. **Password-protected `.scs` support.** v1.3.5 detects and reports them but cannot
   open them. Needs a password prompt plus archive support, since
   `System.IO.Compression` cannot read encrypted zips (add `SharpCompress`).
   Scope note: this is for the user's own file with their own password. Out of scope
   remains decryption of SCS `<Encrypted/>` payloads.

### Features
6. "Update available" prompt at startup — the check now works but only logs a line.
7. Remember the last page — only window geometry landed so far.
8. "Report a problem" bundle — zip the log + settings + version.
9. Backup naming / restore UX — surface existing `.bak` files and allow restoring one.
12. **Reverse camera on converted cars.** A Road Trip DLC car (the 2023 Ford F-150) shows a
    rear camera feed on its infotainment screen when reversing; a converted truck does not.
    Traced as far as the data allows:
    - The infotainment screen is configured by `dashboard_path` / `gps_path` in the
      interior's `computers.sui`. Those paths point at real, mod-authorable UI definition
      files (`ui/dashboard/ford_f150_23.sii`, 54 KB) — see the SCS wiki page for
      `accessory_interior_data`, and a forum thread where a modder ships a custom
      dashboard of their own.
    - The F-150's dashboard contains **no reverse-camera element**. Its screen IDs run
      `0 … 1740`, and the documented dashboard ID table goes to 1700 with no rear/reverse
      camera slot. `cam.mat` / `cam.tobj` exist but are referenced zero times by the
      dashboard; they are packed render targets the engine fills.
    - Conclusion: the feed is drawn by engine code keyed off gear and speed. Nothing in a
      `.sii` switches it on.
    - **Why not just repoint `dashboard_path` at the F-150's screen:** the dashboard expects
      Ford's font (`/font/db_ford_f150_2023.font`), Ford's atlas UVs, and a screen mesh in
      the interior geometry. A converted truck keeps a truck interior, so there may be
      nothing to draw on — and a dangling pointer here is a plausible crash on save load.
      Deliberately not attempted without a maintainer decision.
    - A converted car's `dashboard_path` (e.g. `/ui/dashboard/s90.sii`) is dangling in the
      *original* mod too, so this is not a regression introduced by the converter. The
      converter copies it verbatim.
    - **Suspected root cause for converted cars: the interior model.** The screen needs a
      mesh in the `.pmd`; a truck interior has no infotainment screen surface. Unverified.
    - Next step when picked up: author a minimal dashboard `.sii` for a converted car
      rather than borrowing Ford's, and test on a truck mod whose cabin already has a
      screen mesh.
    - **If a mod-data-only route proves impossible,** the fallback is a runtime approach
      supplied by the maintainer, not by this tool. Sketch, for reference only:
      1. *Hook the rendering pipeline.* Prism3D runs on DirectX. A C++ DLL plugin (MinHook
         or a custom `Present` hook) intercepts GPU frames as they are drawn.
      2. *Locate and mirror the official feed.* At runtime, scan texture/VRAM pools to find
         the render target the DLC allocates for the F-150's rear camera, then capture it
         per frame while reverse is engaged (gear state via telemetry) into a managed
         surface buffer.
      3. *Rebind onto the modded car.* Intercept draw calls for the patched car's
         infotainment material index and force-bind the captured buffer, since the mod
         has no native Prism3D material of its own to point at.
      **Caveats worth stating plainly:** this is unshippable through the Workshop, depends
      on private engine internals that change between game versions, needs a matching
      screen mesh in the interior or there is nothing to draw on, and step 3 is the hard
      part — external memory has to be bridged to a vertex/index buffer the plugin has to
      find by scanning. It is a research project, not a feature, and it is not something
      the converter can contribute to.

**Process note — stale archives.** `Documents\American Truck Simulator\mod\dlc_rt_ford.scs`
and the copy the game loads are **different files** (142,134,903 vs 142,178,196 bytes).
The mod-folder copy is an older build and ships no `ui/` folder at all, which produced
several wrong "the DLC has no dashboard" conclusions before the discrepancy was found.
`scripts/extract-mod.ps1` now searches both locations and refuses to answer from a stale
copy silently — use `-Game` for anything that shipped with the game.

### Architecture
11. **XML-driven settings pages.** The settings tabs are now genuinely data-shaped
    (see the `AddOption` helper). Schema-validated, so adding a setting becomes a
    small XML edit instead of ~40 lines of C#. This is what makes later settings
    work far cheaper.

### Engineering
13. **Broaden `ModConverterVerify`** — dealer index merging and the truck→car
    migration, so the Track B class of bug cannot return.

### Suggested ranking
1. **Item 3** (real membership verification) first — it is a genuine hole in the gating,
   and any authenticated GitHub account currently unlocks the paid theme tier.
2. **Item 4** (the three `MessageBox.Show` call sites) — visible inconsistency, cheap.
3. **Item 5** (password-protected `.scs`) — users hit this and currently dead-end.
4. **Item 11** (XML-driven settings) — pays for itself by making later settings cheap.
5. Everything else in listed order.

### Polish and consistency (added)
14. **Replace the remaining system dialogs.** Three `MessageBox.Show` call sites are
    left in `ConverterForm.Conversion.cs` (lines 460, 494, 1241 — GitHub update failure,
    hot-reload block, invalid accent colour). The themed `ThemedConfirmForm` already
    exists and is preferred; consistency pass, and these will then match the theme.
15. **Main window does not fit small screens.** `ClientSize` is a fixed 900px tall with
    a 700px `MinimumSize`, so on a 768px-tall display the log card is clipped off the
    bottom. Make the main window scroll or compact, and verify at 1366x768.
16. **Settings schema version and migration.** `AppSettings.ThemeMode` is a live legacy
    field still read by the migration path. Introduce an explicit schema version and
    drop the dead field. This is the prerequisite that makes a v2.0 settings break safe.
17. **Save log to file / open logs folder.** Copy-to-clipboard exists; saving a dated
    log next to the converted output is what people actually attach to a bug report.
18. **Docs pass.** The repository is public now, so the README should describe the
    current feature set (tiers, settings, updater) rather than the older feature list.

### Accessibility and polish (added, optional if scope gets tight)
19. Accessible names/roles for the custom controls (`FlatButton`, `ToggleSwitch`),
    plus a deliberate tab order in Settings. `ThemeSwatch` already has one.
20. DPI-correct custom painting — the hand-drawn controls use raw pixel metrics, so
    they can look soft at 150%.
21. "Skip this version" for update prompts, and a first-run "what's new" panel.

### Deliberately NOT in v1.3.9 — v2.0 territory
- Real membership verification backend and the Ko-fi flow (items 3 and the Ko-fi tier).
  These change product behaviour, not just UI, and deserve their own release.
- Any settings-file format break. 1.3.9 only *adds* a schema version so v2.0 can.
- The reverse camera on converted cars (item 12). It needs an authored dashboard `.sii`
  and probably interior geometry work, which is a feature, not a fix.

---

## v1.3.8 completion checklist

1. ~~Health check baseline → findings list to maintainer.~~ **Done** (Track A).
2. ~~Track B fix + regression test.~~ **Done** (Track B, 31 checks pass).
3. ~~Track C items 1–2.~~ **Done** — Recent button, partial-work-folder logging.
4. Track D fix + extended smoke test. **Done**, with one item deliberately left open: the
   updater mechanism is verified (12/12), but a real update on a real installation still
   has to happen. That test is v1.3.7.2 → v1.3.8, so it can only run once this is released.
5. ~~Health check again → updated list to maintainer.~~ **Done** — full solution build with
   warnings-as-errors, `--no-incremental`, plus both test projects.
6. Go / no-go → commit, tag `v1.3.8`, release.
7. `CHANGELOG.md` heading is `## v1.3.8` and `Program.cs` `AppVersion` is `v1.3.8`,
   both required by the release workflow's tag match. **Done.**

### Process notes worth keeping

- **`--no-incremental` is not optional.** An ordinary incremental build reports "0
  warnings" simply because nothing recompiled. It also produced a *false pass* during
  the v1.3.7.2 verification: the smoke test binary was stale, and the old pass message
  was the only clue. Read the actual output string, not the exit code.
- **Test the mechanism, not just the happy path.** The three updater paths that had never
  run (relaunch, locked binaries, modal dialog) were each wrong on the first attempt for
  test-side reasons. Each looked exactly like a shipping defect.
- **`git worktree` is the safe way to build a patch release** from an older tag while
  unreleased work sits uncommitted. It cannot see uncommitted or staged changes, so the
  two trees cannot contaminate each other.
- **A mod-folder `.scs` is not necessarily the one the game loads.** See the stale-archive
  note under v1.3.9 item 12; it produced several confidently wrong conclusions.