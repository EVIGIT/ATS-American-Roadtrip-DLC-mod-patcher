# Truckers Tool Kit — roadmap

Status: **v1.3.9.1 released** (tagged and published) on top of v1.3.9. It carries one real
fix — the option rows no longer clip at Font Size 11 and above — plus a correction to how the
duplication bug is recorded. **v1.4 is verified in game and ready to tag**; **v1.4.1, v1.4.2, v1.5 and
v1.6 are open**, and they are numbered in the order they will ship rather than the order they were
written — see the table below, because two of them have been renumbered since this line was first
written.

The maintainer's in-game check closes the last open question on v1.4: converted cars render with
their own textures, and the dealership badge is correct. The badge crop and both opt-in passes had
only ever been verified against the real badge files, and the texture repair against the archives
and the game's own error log rather than on screen.

| Number | Work | State |
| --- | --- | --- |
| **v1.4** | Badge crop, unsquash, greyscale, and the texture-path repair | **Code complete and verified in game — ready to tag** |
| **v1.4.1** | Enable patch mode by default; the fix itself shipped dormant in v1.4 | Code complete; waiting on a save-load test |
| v1.4.2 | Password-protected `.scs`, log-to-file + report bundle, backup/restore, accessibility | Not started |
| v1.5 | Save editor, ETS2 and ATS (was v1.4.1) | Not started |
| v1.6 | Settings XML schema, theme rewrite, DPI painting (was the v1.4 section) | Not started |

v1.3.9 shipped the authentication removal, launch page, About tab, layout fixes, dealer branding
and badges, the car-duplication fix, the `invalid_vehicle` fix, and the transparent-badge work.

**The badge rendering is now verified in game.** The maintainer's screenshot confirms the black
rectangle is gone from the Volvo and BMW badges, with the stock Ford, Dodge and RAM badges
unaffected — which is the last thing v1.3.9 left unconfirmed, and the reason the transparency
work is closed rather than merely tested. The same screenshot confirmed the two cosmetic defects
remain, and both are now **measured and explained** rather than guessed at; see *Badge rendering,
verified in game* at the bottom of this file. Items 8–9 carry the cosmetic remainder to v1.4.

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
   - **Do not invent a version number for unreleased work.** v1.3.9 is still in progress, so
     everything built this cycle belongs to v1.3.9's changelog section. Bumping `AppVersion`
     to a speculative future version (it was briefly set to `v1.4.2`) makes the launch page
     announce a release that does not exist, and it contradicts the release rules directly
     above. If a fix needs its own heading while its parent release is still open, put it in
     the parent's section instead and keep the planned future work in the deferred lists at
     the bottom of this file.

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
| `dotnet build TruckersToolKit.sln -c Release -p:TreatWarningsAsErrors=true --no-incremental` | 0 warnings, 0 errors |
| Same for `Cli/TruckersToolKit.Cli.csproj` | 0 warnings, 0 errors |
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
8. **The CLI project is in no build path at all.** `Cli/TruckersToolKit.Cli.csproj`
   exists, links the four converter sources, and is **not referenced by
   `TruckersToolKit.sln`** — it only builds when someone remembers to build it by
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
| 8 | CLI outside the solution | **Fixed.** `Cli/TruckersToolKit.Cli.csproj` added to `TruckersToolKit.sln`, so the health check now covers it. |
| 9 | Backwards collision log message | **Fixed** in `MergeTree`; it now names both files correctly. |
| 1 | Duplicated boilerplate header | **Fully fixed in v1.3.9.** The stale banner and the two mis-placed comment blocks were removed earlier, but the 8 duplicated `using` lines survived. All 10 app files now carry only the `using` lines they actually use — 80 lines down to 12, each proven necessary by the compiler. See Tranche 2. |
| 2 | Mixed encoding | **Fixed.** All 17 files are now UTF-8 **no BOM**, CRLF-only, zero bare LF. |
| 5 | `Debug.WriteLine` in `Settings.Save` | Comment corrected; the call is kept (see note below). |
| 3 | `Margin_` naming | **Not done — reverted.** The trailing underscore is *deliberate*: `Margin` collides with the inherited `Form.Margin` and fails to compile with CS0108. Leave it, and add a short comment so the next health check does not "fix" it again. |
| 11 | No CI build/test gate | **Deferred** — workflow change, needs maintainer sign-off. |
| 4 | Three `MessageBox.Show` sites | **Done in v1.3.9** via a `ShowNotice` helper. The fourth hit, in `LocalUpdater.cs`, is PowerShell script text and intentionally left. |
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

### Track D — GitHub updater — **complete**

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
- [x] **One genuine update on a real installation — done.** v1.3.7.2 → v1.3.8, verified
      from `%TEMP%\ats-roadtrip-local-update.log`:
      ```
      Updater started          16:25:20.278
      Copying 5 file(s) from  ...\ats-roadtrip-release-76c8c61e\extracted
                        to    C:\Users\weesc\Downloads\ATS-American-Roadtrip-Car-Patcher-win-x64\
      Robocopy attempt 1/5 exited with code 1
        Files : 5 copied, 0 skipped, 0 FAILED
        Bytes : 563.9 kB
      Files copied at          16:25:21.471
      App relaunched at        16:25:21.552
      ```
      Exit code 1 is success. The installed DLL contains `v1.3.8` and contains neither
      `v1.3.7.2` nor `v1.3.7.1`, so the update really landed rather than half-applying.

      Two details in that log that look wrong and are not:
      - *The copied files kept an older timestamp* (`15:23:02`, before the 16:25 run). That
        is `/COPY:DAT` preserving the source timestamps. A fresh write time would be the bug.
      - *The installed DLL is smaller than a local build* (318,976 vs 319,488 bytes). The
        release workflow publishes `--runtime win-x64`; a local `dotnet build` does not.
        Different RID output, not a truncated copy.

      Not confirmed: whether the relaunched window actually appeared on screen. The log
      records `App relaunched` because `Start-Process` returned, which is all it can know.

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

### Progress

Working rule 2 applied: the health check ran green **before** anything was touched, so
anything found later is unambiguously ours. Baseline (2026-10-03, at `a389c4d`):
solution build with `-p:TreatWarningsAsErrors=true --no-incremental` → 0 warnings / 0 errors;
`ModConverterVerify` → 31 checks; `LocalUpdaterSmokeTest` → pass.

#### Tranche 1 — landed, uncommitted

**Item 16, settings schema version and migration.** `SettingsSchema.cs` is new and
deliberately dependency-free, because the settings migration has to be verifiable headlessly
while `Settings.cs` itself is tied to WinForms through `Theme` and `AuthSession`. `ModConverterVerify`
and the CLI both link only dependency-free files, and this is what keeps it that way.

- `AppSettings.SchemaVersion` added; `CurrentVersion` is 1.
- The dead `ThemeMode` property is **removed**. Its value is read once from the *raw JSON* by
  `SettingsSchema.ReadLegacyThemeMode`, because the deserializer silently ignores properties it
  no longer knows — so a typed property was the only thing that could still see it.
- A v0 file is migrated on load and then stamped with version 1, so the migration runs once.
  A stored `ThemeName` always wins, which is what stops a v1 file being dragged back to a legacy
  preference on every launch.
- Per the roadmap, 1.3.9 only *adds* the version. No settings **format** break.

**Item 13, broadened `ModConverterVerify`** (31 → **49** checks). Covers the two preserve-on-
collision paths and the migration, so the Track B class of bug cannot come back:

- *Reference dealer merging.* `CopyMissingReferenceFramework` is the **second** such path — the
  original diagnosis wrongly suspected the dealer `index.sii` merge was involved. It is now
  covered, including a genuine same-folder/same-name collision where the mod's own translated
  definition must beat the reference copy.
- *Truck→car migration.* Both trees are removed afterwards, the dealer migrates with no
  `truck` paths left in it, and a car folder whose name does **not** match is left completely
  alone — the same vehicle-name scoping decided for the re-patch fix.

**Proven, not assumed.** The reference-merge check was mutation-tested by forcing
`CopyMissingReferenceFramework` to overwrite unconditionally: the suite aborts with
`IOException ... already exists` at `ModConverter.cs:687` and exits non-zero. A first attempt at
this proof *passed*, which exposed a flaw in the test rather than the code — the fixture used a
different dealer folder, so no collision existed and the check could not bite. The fixture now
collides for real.

**Correction to a test fixture, not the product.** While adding the migration checks I asserted
that a converted car becomes `accessory_car_data`. It does not, and it should not:
`accessory_car_data` is **not a real SCS unit type** (it is absent from the documented
`vehicle_accessory` list, and the wiki page 404s). Cars in ATS are defined with
`accessory_truck_data`, so the converter is already correct. The check now pins that behaviour
and asserts the invented unit type is never emitted; two pre-existing fixtures that used
`accessory_car_data` were corrected to match.

#### Tranche 2 — roadmap Track A finding 1 finished (boilerplate `using` block)

Finding 1 was recorded as **"Fixed"** after v1.3.8, but only its comment half was: the
status says "stale banner + two mis-placed comment blocks removed". The recommendation also
said *"strip each file down to the `using` lines it actually needs"*, and that never landed —
the same 8 `using` lines were still sitting on all 10 app files.

`Settings.cs:1`, the line quoted in review, was genuinely dead: `Debug.WriteLine` there is
called fully-qualified as `System.Diagnostics.Debug.WriteLine`, so the `using` was unused.

The 9 namespaces are not all boilerplate either. `ImplicitUsings=enable` already supplies
`System`, `System.Collections.Generic`, `System.Drawing`, `System.IO`, `System.Linq`,
`System.Net.Http`, `System.Threading`, `System.Threading.Tasks` and `System.Windows.Forms`
(verified in `obj/.../TruckersToolKit.GlobalUsings.g.cs`). That is why the whole block
could go: `Program.cs`, `SignInForm.cs` and `ThemedConfirmForm.cs` now need **zero** `using`
lines at all.

**80 boilerplate lines → 12 real ones**, and every one of the 12 was proven needed by removing
it individually and confirming the build fails:

| File | Kept |
| --- | --- |
| `Settings.cs` | `System.Text.Json`, `System.Text.Json.Serialization` |
| `AuthSession.cs` | `System.Runtime.InteropServices`, `System.Text.Json` |
| `GitHubSignIn.cs` | `System.Diagnostics`, `System.Text.Json` |
| `Controls.cs`, `Theme.cs` | `System.Drawing.Drawing2D` |
| `ConverterForm.Layout.cs` | `System.Drawing.Drawing2D`, `System.Runtime.InteropServices` |
| `ConverterForm.Conversion.cs` | `System.Diagnostics`, `System.Drawing.Text` |
| `Program.cs`, `SignInForm.cs`, `ThemedConfirmForm.cs` | *(none)* |

Verified: solution build `--no-incremental -p:TreatWarningsAsErrors=true` → 0 warnings / 0
errors; `ModConverterVerify` 49; `SettingsMigrationVerify` 25; `LocalUpdaterSmokeTest` pass.
`git diff` outside the `using` lines shows only the intended v1.3.9 work. All 11 touched files
remain UTF-8 **no BOM**, CRLF, zero bare LF.

**Process note — trust the compiler per-file, never across a set.** Stripping all 8 namespaces
from all 10 files at once reported **22** errors and none of them in `Settings.cs`, even though
`Settings.cs` was left with zero `using` lines and calls `JsonSerializer`. Building `Settings.cs`
in that stripped state *alone* reports 4 errors. So a bulk strip produced an incomplete picture,
and the required set had to be discovered one file at a time. Any future bulk-`using` cleanup
must confirm each file in isolation, exactly as done here.

**Process note — do not script file rewrites that build in a loop.** A throwaway script that
removed one `using` at a time and rebuilt per iteration was interrupted mid-run and left
`GitHubSignIn.cs` and `ConverterForm.Layout.cs` at **0 bytes**. They were recovered from the
pre-edit backups taken beforehand (which is the only reason this was cheap). The script was
deleted rather than kept. Take backups first, write with an explicit `UTF8Encoding($false)`, and
re-verify sizes and content afterwards — the v1.3.8 health check lost 15 files the same way.

**Status at the end of the build — replaces a stale "Not started" line.** The authoritative split
across the numbered items below:

- **Done:** 6 (update-available prompt), 7 (remember the last page), 13 (broadened
  `ModConverterVerify`, 31 → 49 in Tranche 1), 14, 15, 16, 18, and the "what's new" half of 21 —
  plus the dealer-branding, dealership-logo and unit-name-namespace work recorded in the notes below.
- **Moot, not deferred:** 3 (membership verification — resolved by deleting the gate, not by
  building a backend) and 4 (`InfoDot` — investigated and retracted as a non-bug).
- **Moved to v1.4:** 11 (XML-driven settings pages) and 20 (DPI-correct painting).
- **Deferred to v1.4:** 12 (reverse camera), recorded as item 3 in that section's
  `### Deferred from v1.3.9` list.
- **Still open in v1.3.9, not deferred:** **nothing.** Items 6 and 7 were the last two, and both
  landed. v1.3.9's code scope is closed; what remains is the verification list below and the
  commit/tag.

- **Deferred to v1.4.2 as the hardest remaining work:** 5, 8, 9, 17 and 19. The reasoning for each is
  inline at its item, and they are gathered in the v1.4.2 section.

Note for item 5: `SharpCompress` **0.37.2 — the version implied by the roadmap — restores but
carries a live advisory** (`GHSA-6c8g-7p36-r338`, path traversal in `WriteToDirectory`). The
patched line is **≥ 0.48.0**, which restores clean. Use that, not 0.37.x. Adding it needs the
maintainer's approval per working rule 3 — and item 5 is deferred partly because it needs a
dependency decision that should not have to be rushed.

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
3. ~~**Real membership verification.**~~ **Moot — resolved by removal, not by a backend.**
   This was "`AuthSession.HasGitHubAccess` is just `IsSignedIn`, so *any* authenticated GitHub
   account unlocks the theme tier". It was a real hole *while a paid tier existed*.
   Investigation of the three candidate checks showed only one was even viable:
   - **Stargazers** — public endpoint, works, but would have meant gating 9 of 12 themes on
     starring the repo, which contradicts "this tool is free".
   - **Collaborators** — the list endpoint requires push access to the repo, so it can only
     ever be true for maintainers. Worthless as a user-facing gate.
   - **GitHub Sponsors** — the sponsorships endpoint only returns *your own* sponsors. You
     cannot query whether an arbitrary user sponsors you. Not usable at all.
   Rather than build a backend to protect something free, the gate was deleted. All 12 themes
   are unlocked, `AuthSession.cs` / `GitHubSignIn.cs` / `SignInForm.cs` are deleted, and the
   `ThemeAccess` tier enum is gone from `Theme.cs`. The startup window that existed only for
   sign-in became the launch page (item 21).
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
   **Deferred to v1.4.2.** The largest open item: a new dependency, a new password-prompt UI,
   and a new archive path, with a live security advisory to handle correctly. Three separate
   pieces of work where one would do, and it cannot be built incrementally without the
   dependency first.

### Features
6. ~~**"Update available" prompt at startup**~~ **Done.** The check worked but only wrote a log
   line, which nobody reads at startup. It now raises a themed prompt offering to open Settings,
   where the existing GitHub installer already lives — deliberately a *notification*, not an action,
   because installing closes the app and a modal that silently kills the running converter would be
   a surprise. Dismissing it stores the release **tag** (`DismissedUpdateTag`) rather than a
   boolean, so the same version is not offered twice but a genuinely newer one still is.
7. ~~**Remember the last page**~~ **Done.** Now covers Settings *and* Changelog, plus which of the
   four Settings categories was open. New preference "Reopen the last page on launch", **off by
   default** — silently opening on Settings instead of the converter would surprise people, so it
   is opt-in rather than automatic.
   - `MainPage` is its own dependency-free file, not nested in `ConverterForm`. It is persisted in
     the settings file, and nesting it in the Form would make `Settings.cs` depend on the whole
     WinForms interface — the coupling `SettingsSchema`, `ReleaseNotes` and `MainLayout` exist to
     avoid. It is linked into SettingsMigrationVerify for that reason.
   - **`MainPageJsonConverter` is load-bearing, and for a non-obvious reason.** The first draft
     assumed `JsonSerializer` *throws* on an out-of-range enum number. Measured, it does not: `"99"`
     deserialises to an undefined member that `Load` will happily store. What *does* throw is a
     **string** in that field, and because the settings file is deserialised as one object, that
     throws away the user's theme, accent colour, vehicle types and everything else. The converter
     does both jobs: turn a throw into a fallback, and reject a number that is not a defined member.
     The test asserts the real behaviour of both halves, so the reasoning cannot rot.
   - `RecordLastPage` deliberately does **not** save to disk. The Settings page holds unsaved edits
     that its own Save button owns, and saving on a page switch would write a half-edited page
     behind the user's back.
8. **Deferred to v1.4.2.** "Report a problem" bundle — zip the log + settings + version. Deferred
   with item 17 because it depends on it: there is no log file to bundle until logs are written to
   one. Cheap once 17 exists, so they belong together in the same release rather than shipping a
   bundle button that can only ever attach an empty log.
9. **Deferred to v1.4.2.** Backup naming / restore UX — surface existing `.bak` files and allow
   restoring one. Deferred because restoring is the only genuinely **destructive** feature in this
   list: it overwrites a user's mod, so it needs an explicit file list, a real confirmation step,
   and a decision about what happens to the `.bak` afterwards. That is safety design, not a menu
   item, and it does not belong in a release that is otherwise bug-fix shaped.
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
11. ~~**XML-driven settings pages.**~~ **Moved to v1.4.** The scope is right but the risk is
    wrong for 1.3.9: it introduces an external settings *schema* to validate, and a broken
    one costs users their preferences. v1.3.9 ships the schema **version** that makes such a
    move safe; v1.4 does the move. See the v1.4 section.

### Engineering
13. **Broaden `ModConverterVerify`** — dealer index merging and the truck→car
    migration, so the Track B class of bug cannot return.

### Suggested ranking
1. ~~**Item 3** (real membership verification)~~ — **cancelled.** There is no tier left to verify
   after the Ko-fi removal and the sign-in removal; see item 3 for the full reasoning.
2. **Item 4** (the three `MessageBox.Show` call sites) — visible inconsistency, cheap.
4. **Item 5** (password-protected `.scs`) — users hit this and currently dead-end.
5. Everything else in listed order. (Item 11 was ranked here previously; it moved to v1.4.)

### Polish and consistency (added)
14. ~~**Replace the remaining system dialogs.**~~ **Done.** All three `MessageBox.Show` call
    sites in `ConverterForm.Conversion.cs` (GitHub update failure, hot-reload block, invalid
    accent colour) now go through a `ShowNotice` helper backed by `ThemedConfirmForm`, so every
    notice matches the palette and the dark title bar. The pre-existing inline `Ko-fi theme`
    notice was folded into the same helper so the file is internally consistent.
    - **One `MessageBox` deliberately remains**, at `LocalUpdater.cs:182`. It is not C# UI: it
      is inside the generated PowerShell script (the raw string literal ends at line 189) and
      runs in a separate `powershell.exe` **after the app has exited**. There is no WinForms
      app there to theme, and it is the exact "modal error dialog" path v1.3.8 Track D verified.
      Changing it would put a proven path at risk for no gain.
    - **Manually verified.** The maintainer triggered the invalid-accent path (`#GG0000` in the
      Accent Color field, then Save) and confirmed the themed dialog appears. That is the one
      changed site reachable on demand, and it exercises the shared `ShowNotice` helper the
      other two sites use. The GitHub-update and hot-reload notices are **not** separately
      click-through-verified — they differ only in owner and message.
15. ~~**Main window does not fit small screens.**~~ **Done.** The fixed 900px `ClientSize` with a
    700px `MinimumSize` is gone. What made this unavoidable rather than a tuning problem: the
    cards above the log need **816px** (`100` header + `222` files + `352` options + `86` action,
    plus margins), so on a 768px display there is no window size that fits — a 768px screen has
    roughly 728px of working area. Resizing alone could never have solved it.
    - The window now opens clamped to `Screen.PrimaryScreen.WorkingArea`, so it never starts
      taller than the screen.
    - `AutoScroll` is on, so overflow is **scrolled rather than clipped**. The log card holds a
      `MinimumLogCardHeight` floor instead of shrinking to nothing or going negative.
    - The log card and the log box are no longer bottom-anchored. Bottom anchoring fights the
      scroll extent; `ResizeMainContent()` (called from `OnResize`) owns the height instead.
    - `MinimumSize` dropped to `min(560, workingArea.Height)`, so the window can still open on a
      short display.
    - `RestoreWindowLayout` now clamps a **saved** height to the working area of the screen it is
      being restored onto. Without this, a window sized on a large monitor reopens too tall on a
      small one and the original bug returns for exactly those users.
    - Verified: build 0/0, all three suites pass, and the app launches and stays up with empty
      stderr, so the height arithmetic does not throw at runtime. **The 1366x768 appearance still
      needs a human look** — the arithmetic says ~258px of scroll on a 768px display, but only a
      real screen confirms the scrollbar behaves.

**Found visually, not by the health check — the "Copy log" button was invisible.** A screenshot of
the log card during the item 15 click-through showed only two buttons where there are three.
Arithmetic confirmed why:

| Button | x range (old) | |
| --- | --- | --- |
| Open output folder | 546–716 | added first, so painted **on top** |
| Copy log | 624–714 | **entirely inside** the row above — unreachable |
| Clear | 724–794 | |

`_openOutput` was positioned as `ContentWidth - 18 - 170 - 8 - 70`, arithmetic that never
accounted for the Copy log button: the `- 8 - 70` is missing `- 90 - 10`. It was added in
**v1.3.5** ("A Copy log button beside Clear") and the other button was never shifted left.
Because `_openOutput` is added to the card first it wins the z-order, so Copy log was not merely
overlapped but painted over. **Pre-existing and unrelated to item 15** — confirmed by
`git diff` showing no changed button coordinates.

Fixed by laying the row out right-to-left from the card's inner edge, deriving all three
positions from named width/gap constants instead of three independent expressions, so adding a
fourth button can no longer silently collide. Verified: 446–616 / 624–714 / 724–794, gaps
exactly 8 and 10, right edge at 794.
16. ~~**Settings schema version and migration.**~~ **Done** (see Tranche 1). `AppSettings.ThemeMode`
    was a live legacy field still read by the migration path; an explicit `SchemaVersion` now
    exists and the dead property is gone. This was the prerequisite that makes a v1.4 settings
    break safe.
17. **Deferred to v1.4.2.** Save log to file / open logs folder. Copy-to-clipboard exists; saving a
    dated log next to the converted output is what people actually attach to a bug report. Deferred
    with item 8, which consumes it. More work than it looks: the log is written from the UI thread
    while conversion runs on a worker, so the file writer needs its own locking and a decision about
    truncation and rollover before it is trustworthy.
    `ToggleSwitch`), plus a deliberate tab order in Settings. `ThemeSwatch` already has one.
    Deferred because these are hand-painted controls: `FlatButton` and `ToggleSwitch` draw
    themselves in `OnPaint` and do not derive from a themed WinForms base, so they expose no
    accessibility surface at all. Wiring that up means giving each an accessible name, role and
    state — which can only really be confirmed with a screen reader running, and this environment
    has none. Shipping it unverified would be worse than deferring it.
    `ToggleSwitch`), plus a deliberate tab order in Settings. `ThemeSwatch` already has one.
    Deferred because these are hand-painted controls: `FlatButton` and `ToggleSwitch` draw
    themselves in `OnPaint` and do not derive from a themed WinForms base, so they expose no
    accessibility surface at all. Wiring that up means giving each an accessible name, role and
    state — which can only really be confirmed with a screen reader running, and this environment
    has none. Shipping it unverified would be worse than deferring it.
18. ~~**Docs pass.**~~ **Done.** The README was four releases out of date and actively wrong:
    titled `v1.3.2`, describing `Program.cs` as the "complete WinForms interface" (32 lines since
    v1.3.7), and stating **"This repository is private"** when it has been public since v1.3.7.
    Rewritten around the current feature set: what the converter does, the two theme tiers, the
    three test suites with their build commands, a per-file project layout, and the release
    mechanics the workflow actually enforces. The stale `Reference mod`, `Limitations` and
    duplicated patch-mode sections were merged rather than left alongside their replacements.
    Verified: zero remaining references to `v1.3.2`, "repository is private", "complete WinForms
    interface" or the removed `Unreleased` changelog convention.

### Accessibility and polish (added, optional if scope gets tight)
19. **Deferred to v1.4.2.** Accessible names/roles for the custom controls (`FlatButton`,
    `ToggleSwitch`), plus a deliberate tab order in Settings. `ThemeSwatch` already has one.
    Deferred because these are hand-painted controls: both draw themselves in `OnPaint` and do not
    derive from a themed WinForms base, so they expose no accessibility surface at all. Wiring
    that up means giving each an accessible name, role and state — which can only really be
    confirmed with a screen reader running, and this environment has none. Shipping it unverified
    would be worse than deferring it.
20. ~~**DPI-correct custom painting**~~ **Moved to v1.4**, folded into the theme rewrite
    (v1.4 item 5). It belongs with the `OnPaint` changes rather than as standalone polish.
21. ~~"Skip this version" for update prompts, and a first-run "what's new" panel.~~ **Done
    (the "what's new" half).** The app auto-updates silently, so a user had no way to find out
    what changed. The startup window — previously the GitHub sign-in — is now the launch page:
    a four-step quick start on a first run, and on later runs the release notes for everything
    newer than the version last launched. `ReleaseNotes.cs` parses the embedded `CHANGELOG.md`
    and is dependency-free so it is covered headlessly by SettingsMigrationVerify (now 57
    checks). Suppressible via Settings → Advanced → "Show the launch page at startup".
    The "skip this version" half for *update prompts* is still open, see v1.4.

### Deliberately NOT in v1.3.9 — see v1.4 below

---

## v1.6 — settings and theme rewrite (was the v1.4 section)

> The badge and texture work took the v1.4 number, so this section is renumbered to **v1.6** and now
> sits *after* the save editor rather than before it. Everything below is unchanged and still
> sequenced XML-driven settings → theme rewrite → settings format break.
>
> **Sections are no longer in shipping order.** They were in planning order and renumbering made
> that obvious, but moving them wholesale is not worth the risk of relocating the historical
> checklists that sit between them. Use the table at the top of this file for shipping order.

The release the old roadmap called **v2.0**. Renamed because the version number was the least
interesting thing about it: the settings-file format break is a **minor** bump, and the work is
a rewrite of two subsystems rather than a new product. Everything here changes behaviour or
persisted data, not just UI.

Carried over, then additions.

### Deferred from v1.3.9

Items deferred **out of v1.3.9**, recorded so none is silently lost. Items 1–4 predate the
in-game testing; items 5–7 came out of it.

1. ~~**Real membership verification backend and the Ko-fi flow.**~~ **Cancelled — deleted in
   v1.3.9.** Both the Ko-fi tier and then the GitHub sign-in were removed, so there is no gated
   content left to verify and no backend to build. See item 3 above for why the API options were
   ruled out. Reinstating any gate later means starting from nothing.
2. **The settings-file format break.** Now unblocked: v1.3.9 added the schema version and a
   migration path, so the settings schema can move from version 1 to version 2 deliberately
   rather than by reinterpretation. This is the reason the version landed first. (Schema version
   2 is unrelated to the app version — this release is still v1.4.)
3. **The reverse camera on converted cars** (item 12). It needs an authored dashboard `.sii` and
   probably interior geometry work, which is a feature, not a fix.
4. **Launch page polish, deferred from v1.3.9.** The maintainer's first look at the finished page
   produced three requests, two of which shipped in v1.3.9 (content-sized layout so the quick start
   does not scroll, and Skip as the only way out). The third is deferred here:
   - **A "Get started" primary action.** The v1.3.9 page has a single Skip button. It was intended
     as a neutral, non-blocking greeting, so there is no affirmative "take me to the converter"
     path. Worth designing properly rather than bolting on: the question is whether the launch
     page should *navigate* to the converter or simply close into it, and whether a first-run user
     who closes the window without converting should see it again next launch.
   - Consider whether the launch page should still appear on *every* launch or only when the
     version actually changed. Today it shows whenever the last launched version differs from the
     current one, which means a user who reverts to an older build sees it too.

**Added during v1.3.9, from what in-game testing exposed:**

5. ~~**Warn when one mod is converted twice under two brand tokens.**~~ **Done in v1.4.** The two
   dealer-branding options are both on by default and are *not* independent: converting one mod twice
   under two different brand tokens writes two `car_brand_logo` files that override two different
   base-game brands. Detection reads the token back out of the previous archive's
   `def/vehicle/car_dealer/<token>/` folder rather than remembering conversions in settings — settings
   are per-user and get cleared, and the archive is both the proof and the actual conflict. It is a
   **warning and never a refusal**, because two brands can be deliberate and silently refusing a
   conversion someone wanted is the worse failure.
6. ~~**End-to-end same-brand coverage.**~~ **Done in v1.4.** Both real Cadillacs are
   `truck_dealer/cadillac` and both declare global `_nameless` units, so the collision is a property
   of the *pair* — no single-mod check can see it. Two same-brand mods are now converted end to end
   and their archives compared for shared generated units. Fixtures measure `firstca_ygu0` vs
   `secondc_mul7`, no overlap. It did not catch the 12-character violation retroactively, because that
   limit was already enforced in the namespace builder by this point; what it guards now is the
   regression.
7. ~~**A sweep asserting every unit name in a converted archive is legal.**~~ **Done in v1.4.**
   `NamespaceAnonymousUnits` throws on an over-long namespace, but nothing checked each component of
   *every* name actually produced. The sweep reads every `.sii` in the real output, checks every
   component of every unit name and reference, and asserts it found names before asserting anything
   about them — a first version silently matched zero because its pattern required a whole-line match
   and unit declarations end in ` {`.

8. **Normalise the dealer's badge geometry — MEASURED, and the cause is now known.** The
   in-game verification closed this question, so the guesswork this item carried is replaced with
   the actual numbers (full analysis at the bottom of this file under *Badge geometry, measured*).
   Neither badge is the wrong size because the game distorts it. Both are correct; they simply
   **waste half their canvas**:

   | Badge | Canvas | Opaque artwork | Occupies | Canvas aspect | Artwork aspect |
   |---|---|---|---|---|---|
   | Volvo | 128x64 | 64x47 at (32,9) | **50%** of width | 2.00:1 | 1.36:1 |
   | BMW | 256x64 | 127x49 at (64,7) | **50%** of width | 4.00:1 | 2.59:1 |

   Both are centred on a canvas twice as wide as the artwork, so the game scales the whole canvas
   into its slot and the logo ends up at half the size it could be. That is the "small" Volvo.

   **Whether the BMW "reads as stretched" in game is still an open observation.** It was assumed that a
   4:1 canvas holding a 2.59:1 roundel must be distorted, then assumed the opposite - that the mod
   artist had pre-stretched the artwork to cancel the squash, so the canvas should be left alone. Both
   were guesses. What is established is that the artwork is 2.59:1 **in the mod artist's own pixels**,
   so whatever the game does with the canvas, a roundel cannot read as circular without resampling it.
   The measurement below settles the canvas question; the artwork question needs the in-game observation.

   **THE TARGET IS NOW MEASURED FROM THE BASE GAME, not inferred.** `base.scs`'s textures are
   GDeflate-compressed with the DDS header stripped, so the C# reader refused every `.tobj` entry and it
   was concluded - wrongly, and twice - that the stock pixels were unreadable. They are not: decoding
   GDeflate and rebuilding the DDS header yields real textures, verified byte-for-byte against ~600
   base-game files by the `easy-scsmodmanager` project. Read that way, `/material/ui/car_brand_logo/`
   gives the ground truth:

   | Badge | Canvas | Aspect | Artwork | Fill |
   |---|---|---|---|---|
   | Dodge | 175x89 | 1.97:1 | 175x25 | 100% |
   | Ford | 175x89 | 1.97:1 | 175x53 | 100% |
   | RAM | 175x89 | 1.97:1 | 175x41 | 100% |

   Every stock badge is **full-bleed**: the artwork spans the full canvas WIDTH, with vertical padding as
   needed. The canvas is cut to the artwork, which is why there is no horizontal margin to remove - and
   removing it is the entire fix.

   **A logo taller than the slot aspect is a genuine exception.** Volvo's artwork is 64x48 = 1.33:1, so
   no canvas at that width is 1.97:1 while holding 48 rows; padding it into the slot would mean cropping or
   squashing the logo. The canvas therefore keeps the artwork's own aspect in that case. None of the three
   stock badges are affected - they are all far wider than the slot - so this only arises on converted mods.

   **Two earlier targets were wrong, and both looked plausible until measured.** Matching the artwork's
   aspect ratio ignored size and produced a BMW badge filling 99% of its canvas. A "60.9% fill" target
   then replaced it, measured from the **converted** `ford.dds` rather than a stock one - so it described
   the bug rather than the correct state, and had to be scrapped too. A third attempt concluded that the
   wide canvas was a deliberate compensation and preserved it exactly; that preserved the padding, and was
   wrong for the same underlying reason: the target was never read from the game.

   **The lesson worth keeping: the symptom is identical for every one of these.** A logo that looks the
   wrong size, or the wrong shape, looks the same whether the cause is aspect, fill or padding. Only
   reading the game's own data settles it, and each of these three was confidently wrong until that was
   done.

   **The crop is lossless and provably so** - colour blocks are copied byte for byte, measured at max error
   0 with 0 pixels lost on both real badges. Correcting artwork that is itself pre-squashed (the BMW
   roundel is 2.59:1 in the mod author's pixels) needs **resampling**, which cannot be lossless: a BC1/BCn
   block is self-contained, so a block can be relocated intact, but a resample puts pixels in different
   blocks whose endpoints must be re-derived from the new pixel set. That is an opt-in pass, defaulted off.
   The two passes are order-dependent: the crop reads the alpha channel to find the logo, so the transparency pass must run first; on a pristine mod the DDS has no alpha and every pixel reads as artwork. ModConverter runs transparency then crop, and the verification exercises that order on the untouched originals.

   **Dodge and RAM's slot geometry is still unmeasured** and remains an open gap. For the record, the
   truck badges in `base.scs` do declare a slot via `effect : "ui.sdf.rfx" { aux[0] : {175,89,2,0} }`,
   measured as 175x89 (**1.97:1**) for freightliner, international, kenworth, mack, peterbilt, volvo,
   westernstar and all; `modded` is 178x93. **The third value is `2.00000` in all 91 of 91**
   `ui.sdf.rfx` materials in `base.scs`, so it is a constant rather than a per-badge parameter.

   **None of that is evidence about the car shop.** Those `brand_logo/*.mat` files are *truck*
   badges; the working *car* badges use plain `material : "ui"` with **no `aux[0]` at all**. Copying
   `aux[0] : {175,89,2,0}` into a car-shop `.mat` would be incorrect, so it must not be done. It is
   recorded only so the value is not mistaken later for a car-shop measurement.
9. **Greyscale converted badges to match the stock treatment.** The base game's badges read as
   monochrome in the dealership; converted badges keep their brand colours (BMW blue-and-white,
   Volvo's blue), so they read as different objects. This is a separate change from item 8 and
   from the transparency work, and it **breaks the byte-for-byte colour guarantee** that the
   alpha pass is built on — a deliberate trade, because that guarantee exists to protect the
   artwork from *unintended* damage. It therefore belongs in its own toggle, defaulted on, with
   a note that it alters colour; a mod author's brand colours are legitimate and some users will
   want to keep them.

   **Why neither is in v1.3.9.** Both were seen in game after the black box was fixed and both
   are cosmetic, while the same release was carrying two correctness fixes that broke saves. The
   transparency pass is deliberately lossless and provably so (colour blocks copied verbatim,
   only alpha generated); item 9 would undo exactly that property, so shipping it silently
   alongside would have weakened the guarantee that makes the feature trustworthy. Deferring
   keeps the two concerns separable.

### Additions

4. **XML-driven settings pages** (moved from v1.3.9 item 11). The settings tabs are already
   genuinely data-shaped via the `AddOption` helper; the goal is that adding a setting becomes a
   small schema edit instead of ~40 lines of C#. This pays for itself by making all later
   settings work far cheaper, which is why it lands *before* the polish items that add settings.
   - Scope note: the schema has to be **validated**, not merely parsed. A malformed schema must
     fall back to defaults rather than half-applying, and it needs its own test coverage —
     `SettingsMigrationVerify` already exists to host that.
   - The schema version from item 16 is what lets this ship: a schema change is just another
     migration step rather than a silent reinterpretation of user data.
5. **Theme rewrite — palette, accent and remap layer.** Scoped to the theme engine only.
   Theme gating is explicitly **not** part of this — it no longer exists.
   - `Theme.Palettes` is 12 palettes written as positional `Color.FromArgb` literals
     (`Theme.cs:26-42`). Every colour is an unnamed 8th argument, so the palette record has no
     self-describing fields and no place to hang semantics. Promoting it to named slots is the
     enabling change.
   - **Status colours sit outside every palette.** `Success`, `Warning`, `Error` and `Info` are
     global constants (`Theme.cs:150-153`), so a palette cannot restyle them. Two light palettes
     (`Daylight`, `Sandstone`) therefore carry the same dark-palette status colours. Per the
     retracted item 4, `Info` blue is a *deliberate* semantic choice rather than a bug — so this
     is about giving status colours a home per palette, not about recolouring them.
   - **`RemapColor` matches by exact equality** (`Theme.cs:168-180`). A theme change walks the
     control tree and swaps a colour only if it is bit-identical to a known palette colour. Any
     derived, blended or alpha-composited colour silently fails to remap, which is a latent
     source of half-themed controls. Matching on palette slot rather than value is the fix.
   - **DPI-correct painting** (was v1.3.9 item 20). The hand-drawn controls use raw pixel
     metrics, so they look soft at 150%. Worth doing in the same release because the rewrite is
     already touching every `OnPaint`.

### Ko-fi tier removed, then GitHub sign-in removed — the tool is free

The maintainer decided against the Ko-fi supporter tier and asked for it to go, preferring to
keep the tool **100% free**. Removed rather than commented out, because the health check treats
commented-out code and unused members as findings in their own right.

The six themes reserved for it were **kept and promoted** to the GitHub tier rather than
deleted, so no work was lost: Midnight, Evergreen, Lagoon, Sandstone, Aurora and Vapor joined the
GitHub tier. That was an intermediate state only. The maintainer then went further and removed
the GitHub sign-in too, so **all 12 themes are unlocked and there is no account at all**.

| | Original | After Ko-fi | Now |
| --- | --- | --- | --- |
| Tiers | Free, GitHub, KoFi | Free, GitHub | **none** |
| Palettes | 3 / 3 / 6 | 3 free / 9 GitHub | **12 free** |
| Unlocks | two paid routes advertised | one | **no gate** |

**Round 1 — Ko-fi.** Removed `ThemeAccess.KoFi`, `Theme.KoFiPink`, `AuthSession.HasKoFiAccess`,
the inert "Sign in with Ko-fi" button on the sign-in form, the disabled one in Settings, and the
"Ko-fi themes" section in Settings. Verified 0 Ko-fi references remained in any source file.

**Round 2 — GitHub sign-in.** Removed the last tier entirely rather than build a verification
backend for content that is free. Deleted `AuthSession.cs`, `GitHubSignIn.cs` and `SignInForm.cs`;
dropped the `ThemeAccess` enum, `IsLocked`/`IsUnlocked`/`LockColor`/`SupporterGold`, the padlock
painting in `ThemeSwatch`, the sign-in/out buttons in Settings → Accounts, and the
`ApplyAccountChange`/`PromptSignIn` local functions.

Confirmed before deleting: `GitHubReleaseClient` uses only the `gh` CLI and never referenced
`AuthSession`, so the updater and remote changelog are unaffected by removing the app's own
OAuth flow. The Accounts tab now states plainly that no account is needed.

The startup window that existed only for sign-in was repurposed as the launch page (item 21)
rather than deleted, so the slot is not wasted.

### Log popup window — **built, then removed**

A separate `LogWindow` opening when a conversion finishes was implemented at the maintainer's
request, to give the finished log somewhere to live that is independent of the main window's
height. It worked, but the maintainer did not like how it looked and chose removal over a
redesign. **Reverted. No popup ships.** `LogWindow.cs`, the `OpenLogWindow` helper and the
`_logWindow` field are all gone; the log card stays on the main window as before.

Worth keeping from the exercise: the arithmetic showed that removing the log card from the main
window would leave content at `792 + 24 = 816px`, still above the ~728px working area of a
768px display. So a popup would have improved readability on a short screen without removing the
need for scrolling there. **Scrolling remains the small-screen answer.**

**Regression found and fixed during that work, and kept.** `DefaultWindowHeight` was still 900,
but content had become `792 + 170 (MinimumLogCardHeight) + 24 = 986px`, so a scrollbar appeared
on *normal* desktops where there previously was none. The `MinimumLogCardHeight` floor had been
chosen to protect short screens without checking it against the default height. Raised to
**1016** (`792 + 24 + 200`), leaving a usable log with no scrollbar wherever the screen allows.
Found by arithmetic prompted by the maintainer's screenshot, not by the health check.

**Manually verified by the maintainer:** no scrollbar at normal window size, scrollbar appears
when the window is dragged shorter, and the conversion itself runs normally.

1. Item 4 (XML-driven settings) first — it makes every later settings change cheaper.
2. Item 5 (theme rewrite) second — it is self-contained and needs no settings change to land.
3. Item 2 (settings format break) third, now that the schema exists to migrate.
4. ~~Item 1 (membership verification)~~ — **cancelled**, the gated content no longer exists.

---

## v1.5 — the save editor (was v1.4.1)

**Originally scoped as a patch release — no longer is.** This section was written as "defects
found after v1.4 ships, nothing structural". The save editor (item 6) changes that: it is a new
subsystem, in a new file format, on a new UI surface. Calling a release a *patch* while adding a
whole feature is exactly the kind of label drift that makes release notes untrustworthy, so the
framing is corrected here rather than quietly ignored.

**This choice is now made: it is v1.5.** The two honest options were a feature release (v1.5) or
keeping the patch number v1.4.1 while stating in the changelog that it was a feature. The first was
taken, because v1.4.1 turned out to be needed by the patcher fixes and because "v1.4.1" would have
disguised the scope anyway. The reasoning below is left as written, since it is what made the call
easy — v1.4 had not shipped, and shipping a *feature* between two unverified patch releases would
have been its own mess.

What has not changed: **no settings schema break.** Schema versioning landed in v1.3.9 precisely so
that a release adding features does not silently reinterpret user data. If the save editor needs to
persist anything (last used save path, recent folders), that is a *new* schema version migrated
deliberately — not a free-for-all.

### Scope boundaries

1. **The patcher stays ATS-only.** Converting a truck mod into a Road Trip car mod depends on ATS
   car-mode definitions that have no ETS2 equivalent. This is not a limitation to be fixed later;
   it is what the tool is.
2. **The save editor supports both ETS2 and ATS,** because the save format is comparable across the
   two games and this is where shared value actually is.
3. **No new dependencies without explicit approval.** Same rule as the SharpCompress decision.
4. **The save editor must never corrupt a save.** Any write goes through a verified
   read → validate → write round trip, and the user's original file is backed up before it is
   overwritten. See the safety rules below.

### Open items

6. **Save editor — both ETS2 and ATS.** Turns this from a single-purpose mod tool into a general
   ATS/ETS2 toolkit. Layout decision from the maintainer: a tab bar below the title bar and above
   the mod input, with **Patcher** (ATS only) and **Save editor** (ETS2 + ATS) as tabs. This is
   preferred over a separate window so both tools share one window, one theme and one settings
   store.

   **Scope confirmed by the maintainer — three groups, plus both interaction styles:**
   - **Money and XP**
   - **Unlocking cities and trucks**
   - **Skill points**

   Both are wanted, not either/or: **presets** ("1M and everything unlocked") *and* precise
   per-field entry. Presets come second because they are the harder half — a preset has to write a
   complete, self-consistent set of values rather than changing one field in place — but the
   maintainer wants both, so per-field entry is not treated as a fallback.

   Job and freight state was considered and **deliberately left out**: it is the most intricate
   part of a save and the easiest to corrupt, and it is not something the maintainer needs. Dropping
   it removes the single riskiest part of the editor, which is worth more than the feature.

   The three remaining groups still ship in stages rather than all at once. The ordering rule:
   read-only inspection first (load a save, show its values, change nothing), then single-field
   edits (money), then the bulk operations (unlock cities/trucks, skill points), and presets last
   because they depend on all three groups working.

   **Needed from the maintainer to start:** one save from **each** game (ETS2 and ATS), ideally an
   early and a late profile to see how the format differs. Because a save cannot be attached in
   chat, the agreed workaround is to run `scripts/inspect-save.ps1` on each and paste its
   **structure summary** back. That prints the key layout with values elided, which is what the
   parser needs to be written against and avoids putting a real profile's data into a conversation.

7. **Save-repair / diagnosis view.** Follow-on from item 6, and motivated directly by the
   `invalid_vehicle` experience: a read-only pass that lists unit references in a save which no
   longer resolve to a definition in any mounted mod, and names the mod or patch that used to
   satisfy them. It would have turned "the game says `invalid_vehicle` and I cannot tell why" into
   "this save references `vehicle.volvo_cars.vols90`, which no enabled mod defines". Cheaper than
   the full editor and useful on its own, so it is worth considering shipping **before** item 6 —
   it needs enough of the save parser to exist, but not the write path or the UI surface.
8. **Cross-patch collision report.** Two enabled patches for the same vehicle can define the same
   units, and the car then appears twice or behaves oddly. The conversion report already lists every
   written definition path so a user can grep manually; reading the sibling `.scs` files in the
   mod folder and reporting the overlap directly turns that manual step into a check. Cheap, and it
   covers a failure the converter structurally cannot detect on its own.

The maintainer asked for a companion mod that would detect whether the save is in truck mode or
car mode, hide all truck content when in car mode (keeping only the mode switch), and present
converted cars under the matching truck brand's dealer.

**Conclusion: the mode-detection half cannot be built.** Researched against the SCS modding wiki:

- Mods are mounted at game start from the profile's mod list plus **DLC ownership**
  (`dlc_dependencies`, and the `base` / `dlc_`-prefixed mount rule from 1.48). That is the complete
  set of conditions the engine evaluates.
- **Nothing is gated on career/mode state.** There is no directive, attribute or hook that reads
  the save's mode or reacts to the in-game Mode Switch.
- The Mode Switch changes career *inside a running session*, long after definitions are mounted.
  By the time the player switches, the mod's definitions are already loaded.

**The truck-hiding half is also redundant:** ATS already shows the car dealership, not the truck
dealership, when the player is in car mode. Truck definitions still load, but they are not shown or
buyable. A mod cannot change that, and does not need to.

**What survives is the valuable half, and it belongs in the converter rather than a mod.**
`ModConverter.TranslateDealerDefinitions` copies the `truck_dealer` `.sii` files into
`car_dealer/<dealer-id>/` and rewrites their paths — but it **never writes a `brand` attribute**.

### Duplication bug — root cause found: colliding global `_nameless` units

The maintainer described the symptom precisely: **the Volvo S90 appears twice in the car
dealership, and one copy sits in another patched car's dealer, replacing that car.** That ruled
out the earlier "two patches for one vehicle" theory.

Scanning every Road Trip patch in the mod folder for `_nameless.*` unit names found the real
cause. In SCS a `_nameless.` name is **global**. Two different converted mods shipped *identical*
global unit names:

    Cadillac CT5-V 2022  ui/dashboard/ct5.sii
                       _nameless._.speed, _nameless._.gear, _nameless._.sharedisplay, ...
    Cadillac Escalade 2021  ui/dashboard/escalade.sii
                       _nameless._.speed, _nameless._.gear, _nameless._.sharedisplay, ...

    Cadillac CT5-V 2022  def/camera/units/interior_ct5.sii
                       _nameless.interior.peterbilt.389.ar.*
    Cadillac Escalade 2021  def/camera/units/interior_escalade.21.sii
                       _nameless.interior.peterbilt.389.ar.*

The second mod mounted over the first, so one car's definitions replaced the other's. The names
come from the *source* mods (a Renault Megane and a Peterbilt 389), and `_nameless` appeared
**nowhere in `ModConverter.cs`** — the patcher copied them through verbatim.

**Fix shipped:** each conversion now namespaces its global anonymous units
(`_nameless.X` → `_nameless.<dealer-id>.X`), applied in one consistent pass so references between
the mod's own files still resolve. Verified end-to-end through a real CLI conversion:

    accessories[]: _nameless.volvo._.speed
    vehicle_accessory: _nameless.volvo._.speed {

New option **"Avoid clashes with other mods"** in the Options card, on by default. The report
lists every renamed unit name so the change is auditable.

### Cars still duplicating after the brand change — the namespace was derived from the dealer ID

Reported after the dealer/logo work: the S90 still duplicated over other patched mods. Real, and
**caused by the default added in this same release**, not a leftover.

`BuildAnonymousNamespace` derived the anonymous-unit namespace from the **dealer ID**, on the
assumption that one dealer equals one vehicle. Adding "keep the mod's own brand" as the default
broke that assumption, because a brand is not a vehicle. Measured across the real download set:

| Mod | `truck_dealer` brands |
|---|---|
| Cadillac CT5-V 2022 | `cadillac` |
| Cadillac Escalade 2021 | `cadillac` |
| Ford F-150 Raptor 2017 | `ford` |
| Ford_F250 | `ford` |
| Ford Fusion 2010 | `volvo` |
| FordTourneoCourier | `daf, iveco, kenworth, man, mercedes, peterbilt, renault, volvo` (8) |

Two Cadillacs share `cadillac`, two Fords share `ford`, and the Courier declares eight brands at
once. With the dealer ID now taken from the brand, both Cadillacs resolved to dealer `cadillac`
and therefore to the **same namespace**, so their `_nameless` units collided again — the exact
bug the namespacing was added to fix, reintroduced by the default that made the ID brand-derived.
A user typing the same dealer ID twice could cause the same collision by hand.

**Fix: the namespace comes from the input file name, not the dealer ID.** The file name is the
only input guaranteed distinct per conversion.

One subtlety cost a test failure and is worth keeping in mind: `StripVersionSuffix` runs *after*
`SanitizeId`, which has already collapsed punctuation to underscores, so by that point
`V2.3 1.60` is `V2_3_1_60`. The first version-stripping pattern matched only on dots, silently
matched nothing, and stripped nothing — and the test caught it, which is the argument for having
the test.

The trailing version *is* stripped, so one vehicle keeps one namespace across game-version
updates (`... 1.60.scs` and `... 1.61.scs` must not fight over unit names). A non-version
qualifier such as `Beta` is deliberately **kept**, so a beta and a release of the same car get
different namespaces: they are only ever installed together by mistake, and distinct names are
what stop one silently replacing the other.

Covered by checks for same-brand mods, same-dealer-ID-twice, version-strip stability, and the
fallback chain.

### Missing dealership logo — brand token and logo file name are one binding

Fixing the wrong dealership (`vehicle.<dealer>.<model>`) left a second, quieter problem: the
converted Volvo appeared in the right dealer **with no badge**.

The logo is resolved by file name — `material/ui/brand_logo/<dealer>.mat`. Renaming the dealer
from `volvo_cars` to `volvo` therefore leaves the game looking for a `volvo.mat` that no mod
ships. The base game does not have a `volvo` brand, so nothing was found and the dealer rendered
badge-less. The earlier diagnosis in this file was right about the unit name and wrong about the
dealer being the whole story: the dealer ID is bound to *two* names, not one.

Both halves are now handled, as two independent options:

| Option | Effect | Logo work needed |
|---|---|---|
| **Keep the mod's own brand** (default) | Dealer stays `volvo_cars` | None — the game finds the existing logo by name |
| **Rename the dealer** | Dealer becomes the typed ID | The mod's `.mat` is copied to the new name |

The brand is read from `def/vehicle/truck_dealer/<brand>`, the same place dealer identity comes
from. When a mod defines more than one truck brand the one that also ships a matching logo wins,
falling back to the first by name so the choice stays deterministic.

**The copy is byte-for-byte, and that is the whole trick.** A `.mat` names its texture by bare
file name (`texture : "volvo_cars.tobj"`) and the game resolves that next to the `.mat`, so a copy
under a new name keeps pointing at the texture the mod already ships. The obvious alternative —
renaming the `.tobj` too — would mean patching a compiled binary with a length-prefixed string
table that is not a documented format, for a cosmetic file. Not a risk worth taking.

**`BuildPatch` silently dropped it.** Patch mode copies `def/` and `vehicle/` but nothing under
`material/`, so the logo copy was being written and then thrown away — the fix would have worked
in full-conversion mode and done nothing in the *recommended* mode. `CopyBrandLogoTree` now copies
just that one folder, deliberately not the mod's whole material tree, which would drag every
texture in the mod into an otherwise tiny patch.

Verified against the real DLC in both modes (`tar -tf` on the output):

    keep:   material/ui/brand_logo/volvo_cars.{dds,mat,tobj}
            def/vehicle/car_dealer/volvo_cars/s90_2020.sii
    rename: material/ui/brand_logo/sweden.mat          <- added
            material/ui/brand_logo/volvo_cars.{dds,mat,tobj}
            def/vehicle/car_dealer/sweden/s90_2020.sii

The copied `sweden.mat` is identical to `volvo_cars.mat` and still references `volvo_cars.tobj`,
which ships in the same archive.

**Open, and worth knowing before converting the same mod twice:** both options are on by default,
but they are *not* independent. Converting one mod under two different brand tokens writes two
`brand_logo` files that override two different base-game brands. That is almost never what anyone
wants, and it is the case a future version should detect and warn about.

**Correction — a false lead, recorded because it nearly sent this the wrong way.** While
diagnosing "no logo shows", the Volvo S90 mod appeared to ship `volvo_cars.mat` (75 B),
`.tobj` (86 B) and `.dds` (5608 B) as *entirely zero bytes* — read with `tar`. That would have
been a clean root cause: the mod author reserved the logo and never filled it in.

It is wrong. Read with this project's own extractor (`--extract`, i.e. `ScsArchive`), the same
files are 75/75, 51/86 and 1777/5608 non-zero bytes. `tar` silently mis-decodes this archive's
nonstandard ZIP metadata — the exact problem `ScsArchive` exists to work around, which was
already documented in the README and had been forgotten the moment a generic ZIP tool was
reached for. **Never read these archives with a generic ZIP tool; their contents cannot be
trusted.** `Expand-Archive` was equally untrustworthy here (it produced an empty directory and
only printed the progress line).

`HasRealContent` was kept regardless: an empty placeholder `brand_logo` is a real failure mode,
it is simply not this mod's problem. It now reports the condition instead of copying a dead file,
and `CopyBrandLogoTree` skips empty files so a patch cannot put a blank badge over a working one.
Its doc comment records both the guard's purpose and the `tar` misreading so this is not repeated.

**The logo binding itself is still unconfirmed.** `material/ui/brand_logo/<dealer>.mat` is
inferred from where these mods put their files, not read out of ATS. The user reports that a
converted car shows **a small purple square** instead of a badge. Purple/pink is the engine's
missing-texture indicator, so *something* is being asked for and not found.

**SOLVED from the game's own log.** The user supplied a `game.log`, which names the missing file
exactly:

    [car shop] Found logo of brand: dodge
    [car shop] Found logo of brand: ford
    [car shop] Found logo of brand: ram
    <ERROR> [car shop] No logo found for brand 'volvo_cars'!
    <ERROR> [car shop] No logo found for brand 'bmw'!
    <ERROR> [car shop] No logo found for mod brand 'volvo_cars'!
    <ERROR> [resource_task] Can not open '/material/ui/car_brand_logo/volvo_cars.mat'
    <ERROR> [fs] Failed to open file '/material/ui/car_brand_logo/volvo_cars.mat'

**ATS has two separate logo folders, and the car shop does not read the truck one:**

| | Folder | Read by |
|---|---|---|
| Truck dealer | `material/ui/brand_logo/<brand>.mat` | the truck dealership |
| **Car shop** | `material/ui/car_brand_logo/<brand>.mat` | the car dealership |

A truck-era mod ships its logo under `brand_logo/`, which is correct while it stays a truck and
**useless the moment it is sold as a car**. Every part of this investigation was chasing the right
files in the wrong folder.

`EnsureCarBrandLogo` now writes the logo set into `car_brand_logo/` on **every** conversion —
including the "keep the mod's own brand" default, which is precisely the case that was broken.
Keeping the brand had been treated as sufficient because the file was already in the archive; it
was, and it was in the folder the car shop never reads.

**No logo needed downloading or recreating.** The artwork was always present and valid; it just
needed filing under the right shop. Verified on the two real mods — each converted archive now
carries both folders, and the car-shop copy is exactly the file the log was asking for:

    material/ui/car_brand_logo: volvo_cars.dds, volvo_cars.mat, volvo_cars.tobj
    material/ui/brand_logo:      volvo_cars.dds, volvo_cars.mat, volvo_cars.tobj
    material/ui/car_brand_logo: bmw.dds, bmw.mat, bmw.tobj

All three files travel together: the `.mat` resolves its `.tobj` by bare name beside itself, so the
`.tobj` must come too. The truck-era copy is deliberately **kept**, because the `.tobj` holds an
absolute path (`/material/ui/brand_logo/volvo_cars.dds`) straight back into it.

`CopyBrandLogoTree` carries both folders into a patch for the same reason.

**What the log also settled.** It confirms both cars were found and their brands read correctly —
`[car shop] Selected brand 'volvo_cars'` and `'bmw'` — so the dealer-assignment work is sound. The
purple square was purely the missing badge, and the log's own wording ("No logo found") is what a
missing-texture placeholder looks like from the inside.

**Note for anyone tempted by "just grab a logo from Google":** not needed, and it would have been
wrong. The mod ships real Volvo and BMW artwork; substituting a downloaded image would have
replaced a correct logo with an unofficial one, and added a licensing question to a tool that
currently has none.

### Correction: the `.tobj` embeds an ABSOLUTE path

Dumping the bytes shows the compiled texture object ends with a length-prefixed string:

    /material/ui/brand_logo/volvo_cars.dds

So the chain is **two links with different rules**, not the single bare-name link this file
previously claimed:

- `.mat` → `.tobj` — bare file name, resolved beside the `.mat`
- `.tobj` → `.dds` — **absolute path inside the binary**

The copy strategy still works (the mod ships the original `.tobj`/`.dds` and the copied `.mat`
points at them), and renaming the `.dds` really would require binary rewriting. But the stated
reasoning was wrong and has been corrected in `ModConverter.CopyBrandLogoForDealer`.

### Leading hypotheses, in order

1. **Wrong folder.** The mods ship `material/ui/brand_logo/`. If ATS resolves the *car* dealership
   badge from `material/brand_logo/` (no `ui/`), these logos never worked — original mod included.
   **This is the cheapest thing to test and the least invasive to try**: write the logo into both
   locations. An extra small file is harmless; a wrong guess costs nothing but a few bytes.
2. **The badge is not driven by `brand_logo` at all** — it may come from a brand definition the
   dealer references, or from the Road Trip DLC's own brand table.
3. **Hypothesis 1 is right and the original mod's logo also never worked.** Nothing in this repo
   can distinguish that; only loading the *unconverted* mod in game can. **This is the single most
   valuable observation still missing** and it costs the user one minute: if the stock Volvo S90
   shows a badge and the converted one does not, the converter is at fault; if neither does, the
   mod's logo was already broken and no converter change will fix it.

### What is needed to close this

**Closed.** The `game.log` supplied by the user named the missing resource outright. The two items
below are no longer needed for *this* problem:

- ~~`game.log`~~ — supplied, and it identified the file and the folder immediately.
- ~~`base.scs`~~ — the log answered the question the archive was going to be needed for.

Still worth having for **other** open questions, chiefly the patch-mode `invalid_vehicle` merge
semantics in the v1.4.3 notes, which genuinely needs the game's own definition files.

**The badge rendering in game is still unverified**, and that is the actual open item.

### Earlier theory, retracted

- **"Two patches for one vehicle"** — the S63 does ship two enabled archives with identical unit
  paths (`2021 Mercedes-Benz AMG S63_1.61x_roadtrip_patch` and `AMG S63_Def_roadtrip_patch`), but
  that is a separate concern and is not the reported symptom.
- **Missing `brand` attribute on `car_dealer`** — **wrong, and this was the wrong question.**
  All 15 `car_dealer` files across every local Road Trip patch lack `brand`, *including* a file
  derived from the base game (`car_dealer/ford/ford_f150_23.sii`). Branding genuinely is not
  carried by a `brand` attribute. What the earlier note could not answer without the base game is
  *where* the brand actually lives. Answered below.

### Unit-type inconsistency, still open

| Vehicle | Unit type |
| --- | --- |
| Volvo S90 | `vehicle : .s90_2020` |
| Ford F-150 (base-derived) | `vehicle : .car` |
| Mercedes S63 | `car : .tdealer.s63` |

The S63 is the odd one out and disagrees with genuine base-game files. Unresolved.

### Wrong-dealership bug — root cause found: the brand is the middle of the unit name

The maintainer reported a follow-on symptom after the first fix: **the Volvo S90 was not in the
Volvo dealer at all, but appeared in the BMW dealer of the BMW mod.** This was not a leftover of
the duplication bug; it was a separate defect in how the patcher named the car.

ATS is not installed on the development machine, but the maintainer's own Road Trip DLC
(`Documents/American Truck Simulator/mod/dlc_rt_ford.scs`) is a HashFS archive of the real
`dlc_rt_ford` content, and the repository's `ScsArchive` reader extracts it. That gave the
answer the previous note said it needed:

    def/vehicle/car/ford.f150_23/data.sii
        accessory_truck_data : vehicle.ford.f150_23     <-- brand is the middle component
    def/vehicle/car_dealer/ford/ford_f150_23.sii
        vehicle : .car { accessories[]: .data ... }      <-- no `brand` attribute anywhere

So the dealership a car belongs to is **not** a `brand` attribute. It is the middle namespace
component of the car's own `accessory_truck_data` unit, and it must match the `car_dealer/<brand>/`
folder. The definition folder mirrors the unit namespace, so the folder name is `<brand>.<model>`
too.

A truck-era mod has no brand component at all:

    def/vehicle/truck/vols90/data.sii
        accessory_truck_data : vehicle.vols90           <-- nothing for ATS to match

The patcher copied that unit through verbatim, so the converted car had **no dealership of its
own**, and the game listed it under whichever brand happened to claim the unmatched definition.
That is precisely why the S90 turned up in the BMW dealer, and it explains the "Volvo missing
from the Volvo dealer" half of the report too.

**Fix shipped.** Each conversion now rewrites the unit to `vehicle.<dealer-id>.<model>`, renames
the definition folder to match (the namespace mirrors the path, so the two cannot disagree),
moves the asset folder with it, and repoints the dealer's `data_path` entries. Only the vehicles
this conversion migrated are touched, so another mod's car folder in the same tree is left alone.

Verified end-to-end against the real Volvo S90 archive:

    def/vehicle/car/volvo.vols90/data.sii
        accessory_truck_data : vehicle.volvo.vols90
    def/vehicle/car_dealer/volvo/s90_2020.sii
        data_path: "/def/vehicle/car/volvo.vols90/data.sii"

which is structurally what the base game expects, while the definition folder keeps the mod's own
name. `Validate` also now reports any car definition whose unit still lacks a brand component, so
the class of bug surfaces instead of silently misplacing the car.

### The crash that fix caused, and the correction

The first implementation also renamed the definition folder, `def/vehicle/car/vols90` to
`def/vehicle/car/volvo.vols90`, to match the unit namespace. **That crashed the game when a save
was loaded.** The folder rename was correct as far as the unit went, but a mod addresses its own
definitions *by path*, and the patcher only rewrote the paths inside `car_dealer`. Every other
reference was left pointing at a folder that no longer existed. In the S90 that was, among others:

    def/vehicle/car/vols90/interior/black.sii
        defaults[]: "/def/vehicle/car/vols90/accessory/steering_w/default.sii"

A definition whose `defaults[]`/`fallback[]` cannot be resolved is fatal to the load, so the game
died before the dealership was ever reached.

**Correction shipped:** only the *unit name* is rewritten now, never the folder, and the rewrite
runs across every definition file in the mod in one consistent pass so the declaration and all
references to it move together. Verified on the real S90 archive:

    def/vehicle/car/vols90/data.sii
        accessory_truck_data : vehicle.volvo.vols90     <-- brand present
    def/vehicle/car_dealer/volvo/s90_2020.sii
        data_path: "/def/vehicle/car/vols90/data.sii"   <-- folder still exists

with **zero** `def/vehicle/car/...` references left dangling.

**The general lesson, worth stating because it is easy to get wrong again:** renaming a
definition folder is never safe on its own. Every reference to it has to be rewritten in the same
pass, and the safest default is not to rename it at all. `ModConverterVerify` now walks the
converted archive and fails if any `def/vehicle/car/<name>/` reference names a folder that is not
present, so this class of breakage is caught automatically instead of by a game crash.

Also observed: the two patches disagree on the unit type for the same concept — the newer S63
writes `car :` while the older Volvo writes `vehicle :`. Worth checking which one the game expects.

Next step before writing any fix: dump the base game's own `def/vehicle/car_dealer` to see the exact
attributes a branded dealership needs. `scripts/inspect-save.ps1` accepts a directory for this, so
the same paste-back route works:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\inspect-save.ps1 -Raw `
      -Path "<extracted base.scs>\def\vehicle\car_dealer"

`-Raw` is needed here because these are game files, not a private profile, so the actual values are
the whole point. The game is not installed on the development machine, so the maintainer must run
this on the machine that has ATS.

   **Why this needs care, stated up front rather than discovered later:**
   - Save format is **game-version dependent**. ATS and ETS2 saves differ, and both change between
     game updates. A parser that silently mis-reads a field is worse than one that refuses.
   - Saves contain **irreplaceable progress** — hours of driving, money, unlocks. Corruption here
     costs a user far more than any bug in the patcher.
   - Therefore: **read and validate first, write only on explicit user action, back up before
     overwrite**, and never round-trip a save through the editor unless the user asked for it.

   **Scope confirmed by the maintainer — three groups:**
   - **Money and XP**
   - **Unlocking cities and trucks**
   - **Skill points**

   Job and freight state was considered and **deliberately left out**: it is the most intricate
   part of a save and the easiest to corrupt, and it is not something the maintainer needs. Dropping
   it removes the single riskiest part of the editor, which is worth more than the feature.

   The three remaining groups still ship in stages rather than all at once. The ordering rule:
   read-only inspection first (load a save, show its values, change nothing), then single-field
   edits (money), then the bulk operations (unlock cities/trucks, skill points).

   **Needed from the maintainer to start:** one `game_data.sii`-style save from **each** game (ETS2
   and ATS), ideally from an early and a late profile to see how the format differs. Both are
   needed before any parsing is written, because guessing the format is how this goes wrong.
   Because a save cannot be attached in chat, the agreed workaround is to run
   `scripts/inspect-save.ps1` on each file and paste its **structure summary** back. That prints the
   key layout with values elided, which is what the parser needs to be written against and avoids
   putting a real profile's data into a conversation.

   **Still open even with the fields decided:** exactly *which* fields in each group are
   editable, and whether the editor should offer presets ("give me 1M and everything unlocked") or
   only precise per-field entry. Presets are far more useful but also far more dangerous, because
   a preset has to write a complete, self-consistent set of values rather than changing one field
   in place.

7. **Tab bar plumbing.** Independent of the save editor and worth landing first, because it is small
   and lets the patcher ship behind the new navigation before the editor exists:
   - A themed tab strip below the title bar, above the mod input.
   - Each tab owns its own layout and is shown/hidden rather than rebuilt, so switching is cheap and
     cannot lose entered state (dealer ID, chosen options, typed paths).
   - The tab bar participates in the existing window-height and small-screen clamping, so adding it
     must not reintroduce the scrollbar problem fixed in v1.3.9. **This is now mechanical rather
     than guesswork:** the vertical budget was extracted into `MainLayout.cs` in v1.3.9 and is
     asserted headlessly. Adding a tab strip means raising `ExtraChromeAboveLog` in
     `ConverterForm.Layout.cs` to the strip's height. `MainLayout.WindowHeightFor` and
     `MinimumHeightFor` already do the arithmetic, and the suite already checks that a 56px strip
     raises the window by exactly 56px and leaves the log at its preferred size. The one thing
     still needing eyes: the height is now derived rather than a round number, so re-confirm by
     hand that a normal desktop still opens with no scrollbar.
   - The current window geometry is saved and restored; the tab strip must not shift that.

8. **Deferred "Get started"** on the launch page — see v1.4 item 4.

### Rules for this release

1. **Tests green before tagging**: full solution build with warnings-as-errors and
   `--no-incremental`, plus all three suites.
2. **A new suite for the save format.** Save parsing is exactly the fiddly, silently-wrong work
   `ModConverterVerify` exists for. It needs a fixture-driven suite with saved-file samples
   committed as test data, and it must cover malformed and truncated saves.
3. **A regression found by the health check or manual testing is always in scope**, regardless of
   which release introduced it.
4. **No settings schema change without deliberately bumping `SettingsSchema.CurrentVersion`** and
   adding the migration step.

---

## v1.4.2 — deferred from v1.3.9

**Scope set by the maintainer: the hardest and longest-to-program items still open in v1.3.9.**
Items 6 and 7 landed afterwards, so **v1.3.9's code scope is now closed** — what remains for it is
the verification list under `### Before v1.3.9 can ship` and the commit/tag. Everything below was
deferred deliberately, not abandoned; each carries its reasoning inline at its item in the v1.3.9
section.

Ordered by how much work it actually is:

1. **Item 5 — password-protected `.scs` support.** The single largest item in the roadmap. Three
   separate pieces of work: a password-prompt UI, a new dependency (`SharpCompress` **≥ 0.48.0**,
   since 0.37.2 carries a path-traversal advisory), and a new archive path, because
   `System.IO.Compression` cannot read encrypted zips at all. It also cannot be built
   incrementally — the dependency decision gates everything else — and working rule 3 requires
   maintainer approval for a new dependency, which should not be rushed inside a bugfix release.
   Scope stays as written: the user's own file with their own password, never SCS `<Encrypted/>`
   payload decryption.
2. **Items 17 and 8, together — save log to file, and the "report a problem" bundle.** They ship as
   a pair because 8 consumes 17: a bundle button with no log file can only ever attach an empty
   log. Item 17 is more work than it looks, since the log is written from the UI thread while
   conversion runs on a worker — the file writer needs its own locking plus a decision on
   truncation and rollover before it is trustworthy. Item 8 is cheap once 17 exists.
3. **Item 9 — backup naming and restore UX.** Deferred on risk, not size. It is the only genuinely
   **destructive** feature in the v1.3.9 list: restoring overwrites a user's mod. That needs an
   explicit file list, a real confirmation step, and a decision about whether the `.bak` survives
   the restore. Safety design, and it does not belong in an otherwise bug-fix-shaped release.
4. **Item 19 — accessibility for the custom controls.** Deferred because it cannot be verified
   here. `FlatButton` and `ToggleSwitch` are hand-painted and derive from no themed base, so they
   expose no accessibility surface at all; each needs an accessible name, role and state added by
   hand. Confirming any of that needs a screen reader running, and this environment has none.
   Shipping it unverified would be worse than deferring it — an accessibility feature that is
   quietly broken is worse than an absent one, because it looks done.

### Order of work when v1.4.2 is picked up

Items 1 and 2 are independent and can go in parallel. Item 4 wants a machine with assistive
technology available, so it may need to be scheduled rather than started. Item 3 should not ship
without its confirmation UX designed first.

### What this leaves for v1.3.9

No open code items at all. v1.3.9 ships as a bug-fix release carrying the dealer branding and logo
work, the car-duplication fix, the `invalid_vehicle` unit-name fix, patch mode turned off by default,
and items 6 and 7 — with only the five verification steps under `### Before v1.3.9 can ship` and the
commit/tag outstanding.

---

## v1.3.8 completion checklist

1. ~~Health check baseline → findings list to maintainer.~~ **Done** (Track A).
2. ~~Track B fix + regression test.~~ **Done** (Track B, 31 checks pass).
3. ~~Track C items 1–2.~~ **Done** — Recent button, partial-work-folder logging.
4. ~~Track D fix + extended smoke test.~~ **Done.** Diagnostics, three previously untested
   paths (relaunch, locked binaries, modal dialog), and a real v1.3.7.2 → v1.3.8 update
   all verified. This closes the item v1.3.6 shipped broken.
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
- **Never inspect these archives with a generic ZIP tool.** `tar` and `Expand-Archive` both
  mis-decode the nonstandard ZIP metadata and will show you an empty or zero-filled file that is
  not empty. That produced a confidently wrong "the mod ships an empty logo" conclusion — see the
  correction above. Use `--extract` / `ScsArchive` for anything that has to be *true*.
- **The terminal is unreliable here.** Long foreground commands frequently report
  `Command completion could not be observed` / exit code 1 with no output, and PowerShell
  `try`/`catch` blocks swallow the result. Redirect to a file and read it back; do not treat
  "could not be observed" as failure. Script execution policy also blocks `.ps1`, so inline the
  command instead.
- **SCS unit name components are capped at 12 characters.** This is not documented in our code
  anywhere and it silently produced patches that converted cleanly and failed in game with
  `invalid_vehicle`. Any generated unit name must be checked against the limit. Sources: the
  modding wiki ("Unit names are divided into components which are 12-char tokens separated by
  dot") and the SCS dashboard guide ("it must be in SCS name specification - 12 symbols
  length"). Truncation to a limit is not a uniqueness strategy on its own — several real mods
  share a common prefix — so it has to be truncate + hash.
- **A save-load error is a different class of bug from a definition error.** `invalid_vehicle`
  meant a unit the save referenced no longer resolved, not that a definition was malformed.
  Nothing in the conversion report or the log would ever have shown it; the only signal was the
  game refusing the save. Checks that assert *what the game will resolve* are worth more than
  checks that assert the conversion ran.

### v1.4.2 — "invalid_vehicle" on save load: our own 12-character violation

Reported as a screenshot of the ATS `invalid_vehicle` error dialog after patching the Cadillac
CT5-V, then reproduced with a newly patched S90 and BMW M5 — the same message every time, which
immediately pointed at something the tool emits in *every* patch rather than something
mod-specific.

`invalid_vehicle` is a **save-load** failure: the save names a vehicle unit, the engine cannot
resolve it, and it refuses to load. That is a very different failure from a malformed definition,
and it is why the conversion looked completely clean. The v1.3.9 build reported success, wrote
zero validation issues, and produced an archive that only broke once ATS touched it.

Cause: the namespace change made in v1.3.9 (see the duplication entry above) took the
anonymous-unit namespace from the mod file name, and inserted it into *every* `_nameless` unit in
the patch. SCS unit names are dot-separated components of **at most 12 characters**. Real names
are much longer:

| Mod file name | v1.3.9 namespace | Length |
|---|---|---|
| `Ford_F250.scs` | `ford_f250` | 9 — legal |
| `Volvo S90 2020 V2.3 1.60.scs` | `volvo_s90_2020` | 14 — **illegal** |
| `Cadillac Escalade 2021 V2.3 1.61.scs` | `cadillac_escalade_2021` | 22 — **illegal** |
| `Cadillac CT5-V Black Wing 2022 V2.2 1.60.scs` | `cadillac_ct5_v_black_wing_2022` | 30 — **illegal** |
| `Ford F-150 Raptor 2017 V1.7.1 Beta.scs` | `ford_f_150_raptor_2017_v1_7_1_beta` | 34 — **illegal** |

Confirmed in the shipped CT5-V patch by extracting it and collecting every `_nameless.<component>`
in the result: one distinct value, `cadillac_ct5_v_black_wing_2022`, length 30.

This is also why the earlier dealer-derived namespaces were all fine: `cadillac` (8),
`volvo_cars` (10), `ford` (4) all happened to fit. Uniqueness and legality were in tension and
the previous scheme only avoided the problem by being short — which is exactly the trade the
brand default removed.

**Fix:** `ShortenNamespace` caps at 12 as 8 readable characters + `_` + a 4-character FNV-1a hash
of the *full* name. Truncation alone would collide — both Cadillacs start `cadillac` — so the
hash is what preserves uniqueness, and it is computed pre-truncation. Names already within the
limit are returned untouched, so `ford_f250` keeps its name and existing patches are unaffected.

FNV-1a rather than `string.GetHashCode()`, because .NET randomises the latter per process: the
namespace would change on every launch and silently break every patch already in a user's mod
folder.

Verified on the real CT5-V: the patch now emits `_nameless.cadilla_f0g5.*`, length 12. Covered by
checks over the real download-set file names asserting legality, distinctness and stability.

`NamespaceAnonymousUnits` now also throws if handed an over-long namespace, so this cannot
regress silently even if a future change reintroduces a long name.

**Lesson recorded above:** the v1.3.9 change fixed a real collision and introduced a worse bug,
and every check we had passed. The gap was checking *what the engine can resolve*, not *what our
own functions return*.

### Researched: "why not patch the whole mod instead of just the definitions?"

Suggested by the user as a possible v1.4.2 feature. **Not implemented, and the research says it
would not have fixed `invalid_vehicle` at all** — recorded so it is not re-proposed.

Full conversion already exists: it is the default, `--patch-only` is the opt-in "safe" mode, and
the GUI toggle is "Definitions-only patch (recommended)". So this was not new work, it was a
question of which mode should be the default.

**Why it would not have fixed the reported bug.** `invalid_vehicle` came from an illegal unit name
written into *every* `_nameless` unit. Full conversion runs the same rewrite pass over the same
definition files — the difference is only what gets *packed* around them. The illegal name would
be present either way. The user hit this on a patch build; switching modes would not have changed
the outcome.

**The trade-offs point the other way:**

| | Patch (current default) | Full conversion |
|---|---|---|
| Original mod | Untouched, removable at any time | Replaced — the source `.scs` no longer matches |
| Duplicate-unit risk | Patch and original both define the truck dealer; handled by emptying the originals | None — one archive |
| Reverting | Delete one file | Re-download the mod |
| Mod updates | Re-patch | Re-convert from the new version |

Full conversion's one genuine advantage is removing the double-definition situation entirely. But
that is already handled — the patch writes empty stubs over the original `truck_dealer` entries —
and the cost is losing the ability to undo without re-downloading.

**Recommendation: keep patch mode as the default.** If this is to change it should be an explicit,
tested switch with the revert cost spelled out in the UI, not a silent flip.

## v1.3.9 shipping checklist — closed, with three items carried forward

v1.3.9 is **tagged and published**. Of the five items that stood between the tree and a release,
three are closed; the option-row check in item 4 was also closed by v1.3.9.1, and the patcher fix
in item 1 is now scoped to v1.4.3.

1. **Patch-mode position — CLOSED.** The maintainer chose option (c): v1.3.9 shipped with patch
   mode **off by default** and labelled as having a known bug. The switch is off, it is renamed
   "Definitions-only patch (known bug)", selecting it raises a warning before anything is written,
   the README documents it, and five checks pin the default so it cannot quietly flip back. The
   remaining in-game confirmation is folded into the v1.3.9.1 scope below.
2. **Confirm the car-duplication fix in game — CLOSED, and the reproduction was cross-brand.**
   The maintainer's original bug report was **two cars of *different* brands**: the Volvo S90 and
   the BMW M5 duplicated each other. That is the case to remember, because it does **not** match
   the dealer-ID collision theory this file spent a page on. Two Cadillacs are both
   `truck_dealer/cadillac`, so a dealer-derived namespace genuinely would have collided for them —
   but `volvo` and `bmw` would never have collided with each other. A cross-brand duplication
   therefore points somewhere else entirely, most likely to the un-namespaced `_nameless.` units
   both mods emitted before v1.3.9, which is what the input-file-name namespace actually fixed.

   The user confirms it no longer reproduces. Re-tested by loading the S90 alongside the M5.

   Left standing as hardening, not as a known bug: an automated end-to-end sweep converting two
   real same-brand mods and asserting the archives declare no common `_nameless` unit. That is
   v1.4 item 6, and it would now catch the cross-brand case too rather than relying on the reason
   for the fix being right.
3. **Confirm the dealership logo binding from `base.scs` — ANSWERED IN GAME, and it moved.** The
   original worry was that `material/ui/brand_logo/<brand>.mat` might be the wrong path or that
   the bare-name `.tobj` reference might not resolve. In-game testing showed the opposite failure:
   the truck-era logo resolved fine, but the **car shop** reads a different folder,
   `material/ui/car_brand_logo/`, which a truck mod never ships. Both folders are now written on
   every conversion, and both must be transparent because the `.tobj` holds an absolute path back
   into `brand_logo/`. No further machinery is needed for the binding itself.
4. **Verify the 1080p layout by eye — DONE, and it found a real bug.** The options card was
   repacked into pairs to keep the default window at 1080px rather than 1150px, and the arithmetic
   was already asserted. Checking the *text* rather than the arithmetic turned up something the
   arithmetic could not see: the paired rows were a fixed 62px, while `Theme.UiFont` scales every
   font by the user's Font Size setting (8–16) and control sizes stay fixed. Measured against the
   real description strings at the real 322px text width, font size 11 needs 65px, 12 needs 68, and
   16 needs 108 — so at 11 and above **all six** option descriptions were silently ellipsised.
   `TextRenderer` truncates with "..." rather than throwing, which is why nothing ever looked
   broken and nothing was ever reported.

   Fixed by measuring: `ToggleSwitch.RequiredHeight` measures the wrapped text with the same flags
   `OnPaint` uses, `MainLayout` turns that into a row height and a card height, and the window's
   opening height follows. At the default font size the measured path reproduces 62 / 416 / 1080
   exactly, so **1080p is unchanged for anyone who has not touched the setting** — that equivalence
   is asserted, not assumed. Ten new checks in `SettingsMigrationVerify` pin the floor, the growth
   at 11 and 16, monotonicity, and that the last row still ends inside the card.

5. **Final repository review, commit and tag — CLOSED.** The whole v1.3.9 body of work is
   committed and tagged: authentication removal, launch page, About tab, layout fixes, dealer
   branding and badges, both namespace fixes, `MainLayout.cs`, `ReleaseNotes.cs`,
   `SettingsSchema.cs`, `BrandLogoAlpha.cs`, both test projects, and a full README overhaul.

## v1.4.3 — the patcher fix → **renumbered to v1.4.1**

**Renumbered a third time: v1.3.9.1 → v1.3.9.2 → v1.4.3 → v1.4.1.** The first two moves put the fix in
the same numbered family as v1.4 instead of claiming to be a patch release following the v1.3.9 line.
This one resolves a collision the earlier moves created.

**Why the collision, and how it resolves.** `v1.4.1` was already assigned to the save editor, which is
a *feature* and was itself flagged here as no longer deserving a patch number. The badge and texture
work is what is actually ready to ship, so it takes **v1.4** and the patcher fixes become **v1.4.1**.
The save editor moves to **v1.5**, which is what that section had already argued it was:

| Work | Was | Now |
| --- | --- | --- |
| Badge crop, unsquash, greyscale, texture repair | v1.4 | **v1.4** |
| Patcher fixes, `invalid_vehicle` | v1.4.3 | **v1.4.1** |
| Save editor (a feature, not a patch) | v1.4.1 | **v1.5** |
| Settings rewrite + theme engine | v1.4 | **v1.6** |

The settings and theme work moved too. It was written as the v1.4 section, but that section was
already describing the badge work, and mixing a settings-schema break into a release whose headline
is a texture and badge fix would repeat exactly the label drift this file keeps catching. It is now
v1.6, sequenced after the feature work rather than ahead of it.

**Why the patcher fix is not *announced* in v1.4.** The code ships in v1.4, dormant behind the
off-by-default setting, and this release is where it gets turned on. That ordering is deliberate:
v1.4 could ship on badge and texture verification alone precisely because the patch work could not
affect anyone who leaves the default alone, and a clean split of the code would have inverted the
risk — v1.4.1 would then have been "new patch code that has never been loaded from a save" instead
of "a default flip, verified".

The cost is that git history and the changelog do not line up perfectly, which is recorded in the
changelog rather than hidden. A surgical revert of 20 hunks across a 103KB file, immediately before
a tag, was not worth tidying history.

**Renumbered twice: v1.3.9.1 → v1.3.9.2 → v1.4.3.** The fix itself has moved once already —
v1.3.9.1 was released for the option-row clipping fix and never touched patch mode. It was then
renumbered to v1.3.9.2, and finally to v1.4.3 so it sits in the same numbered family as v1.4,
v1.4.1 and v1.4.2 instead of claiming to be a patch release that follows the v1.3.9 line.

**Scope decided by the maintainer: v1.3.9 shipped first**, with patch mode off by default and
labelled as buggy, and this is then the proper fix. Its scope is the research note at the
bottom of this file — in short:

1. Stop writing an empty unit tree over the original's `truck_dealer` entry; redefine that unit
   name with valid content instead.
2. Carry the shared asset roots (`automat/`, `material/ui/accessory/`) that full conversion
   currently drops, which is the missing-textures half.
3. Verify SCS mount-order merge semantics in game — patch above the original *and* below it —
   before trusting any redefinition approach.
4. Re-enable patch mode as the default only once all of the above is confirmed in game.

Deferred feature work is **not** duplicated here: it already has a home in the `## v1.6` and
`## v1.5` sections above. A second list is how the version numbering got muddled in the first
place.

## Research: why patch mode crashes but full conversion does not

The user ran three configurations in game. The results contradict where the fault was assumed to
be, so both were re-measured from the real archives rather than reasoned about.

| Configuration | Result |
|---|---|
| Full conversion, patch toggle **off** | Cars appear in their own dealers. **No textures.** |
| Fresh patches for both cars | `invalid_vehicle` |
| Full conversion **and** patches together | `invalid_vehicle` |

### The diagnosis was backwards

"Both cars load into their own dealer, but have no textures because the defs are not patched"
implicates patch mode as the broken path. The measurements say the opposite:

- **Full conversion is the mode that loses textures.** The patch ships `automat/` (100 files) and
  `material/ui/accessory/` (72 files) that full conversion does not.
- **Patch mode's dealer assignment is byte-identical to full conversion's.**
  `car_dealer/volvo_cars/s90_2020.sii` is character-for-character the same in both archives.
  Whatever makes the dealer correct is not the thing patch mode does differently.
- The crash is **only** reproducible with a patch in the folder.

So the two symptoms have **two independent causes**, and full conversion is not a working
workaround — it silently trades a crash for an untextured car.

### Why full conversion loses textures

`car.pmd` references its textures with **no `vehicle/` prefix at all**:

    /automat/ec/ec529005d7cfe966.mat
    /automat/95/951db6daaf80278e.mat   ... (28 of them)

and paint/metallic textures live in `material/ui/accessory/met_color3.dlc_metallics.*`. These are
the mod's *shared* asset roots. `automat/` is a top-level archive directory alongside `def/`,
`material/` and `vehicle/` — it is not under `vehicle/truck/`, so the "move truck assets to car"
pass never touches it, and `BuildPatch`'s copy list (`def/` and `vehicle/truck` → `vehicle/car`)
does not include it either.

Both modes ship `vehicle/car/volvo_s80/` with an identical 140 files (44 `.dds`, 44 `.tobj`,
20 `.pma`, 15 `.pmd`, 15 `.pmg`, 2 `.pmc`). The model is present in both; only the *root-level*
support trees differ. **The model is not what is missing — the shared support roots are.**

### Why patch mode crashes (`invalid_vehicle`)

Patch mode writes a **13-byte empty stub** over the original's dealer entry:

    def/vehicle/truck_dealer/volvo_cars/s90_2020.sii
    SiiNunit
    {
    }

That is an *empty unit tree* placed at the exact path where the original mod defines
`vehicle : .s90_2020` with 18 accessories. Patch mode is designed to load **above** the untouched
original, so this stub overrides the original's definition with nothing. The dealer entry the game
resolves for that vehicle then has no accessories at all, and a save's stored vehicle reference
cannot be satisfied — `invalid_vehicle` on load.

This also explains why combining a converted mod with a patch still fails: the patch's stub is
still mounted above both, still emptying the dealer entry. And it explains why the first
`invalid_vehicle` only ever appeared once patches were introduced.

### What the 12-character research got wrong

Everything above is consistent with `AssignDealerBrand` being correct — the dealer files match and
the car appears in the right dealer. The 12-character namespace bug was real and separately fixed,
but it is **not** what causes `invalid_vehicle` in the current build, because the current build
already emits legal 12-character namespaces and still crashes. Two distinct bugs were conflated,
and the second one was hidden behind the first.

### Fix direction for v1.4.3

1. **Do not blank the original dealer entry.** Instead of overwriting
   `truck_dealer/<brand>/*.sii` with an empty unit tree, redefine that *same unit name* with valid
   content pointing at the converted car paths. An empty file is a deletion, and SCS merges by
   unit definition, not by file deletion — the current stub deletes a definition the save needs.
2. **Verify merge semantics before coding.** Whether a redefinition wins depends on mount order and
   on both mods being mounted. Needs an in-game test with the patch above the original *and* below
   it.
3. **Carry `automat/` and `material/ui/accessory/` in patch mode**, and decide deliberately whether
   full conversion should carry them too — right now the two modes disagree about which shared
   roots belong to the mod, and one of them is already wrong.
4. **Re-test all four configurations** after the change: patch only; full only; both; and patch
   above vs below the original.

### Process lesson

"The defs are not being patched, so the def patcher is at fault" was reasonable and wrong, and
wrong in the direction that made full conversion look like the fix. Two symptoms that appear
together were assumed to have one cause; they have two. **Measure the configurations against each
other before believing a symptom's own explanation** — both archives were available locally the
whole time.

## Research: the black box around a converted dealer's badge

Reported after the purple-square fix landed: Volvo and BMW badges render inside a black rectangle
while the base game's Ford, Dodge and RAM badges do not. The badge *resolves* correctly — the log
shows `[car shop] Found logo of brand: volvo_cars` — so this is a separate, purely cosmetic defect
in the texture file.

### The cause, measured from the files

| Logo | Format | Black pixels | Result |
|---|---|---|---|
| Volvo `volvo_cars.dds` (128x64) | **DXT1** | 78.0%, all four corners (0,0,0) | black plate |
| BMW `bmw.dds` (256x64) | **DXT1** | 69.9%, all four corners (0,0,0) | black plate |
| `Ford Focus Mk3`'s `ford.dds` (128x64) | **DXT5** | 73.9% of pixels at **alpha 0** | renders correctly |

**DXT1 has no alpha channel.** The black background is not a default or a missing-file placeholder;
it is stored in the pixels, so the game draws it. The working reference is DXT5, which does carry
alpha. That single difference is the entire black box, and it can only be fixed in the file.

### What was checked before touching anything

- **The `.tobj` does not need rewriting.** Across **2205 real `.tobj`/`.dds` pairs** in the mod
  folder, DXT1 and DXT5 textures appear with byte-identical `.tobj` field values
  (`DXT1, F14=1, F1C=0x03` ×34 alongside `DXT5, F14=1, F1C=0x03` ×1247). The compiled object
  carries the absolute `.dds` path plus sampler state, not the compression format. So overwriting
  the `.dds` **in place, under its own name**, keeps the whole
  `.mat` → bare-name `.tobj` → absolute-path `.dds` chain resolving with no binary rewriting.
### A second, pre-existing crash found while testing this

`FindBrandLogoMaterial` **prefers** `car_brand_logo`, which is right — it stops a re-run clobbering a
good car-shop logo with the truck-era copy. But it means that for any mod which already ships
`car_brand_logo/`, `sourceFolder` and `targetFolder` in `EnsureCarBrandLogo` are the same directory,
so the copy becomes:

    File.Copy(x, x, overwrite: true)   ->  IOException: file is being used by another process

and that exception aborts the **whole conversion**, not just the logo step. It is reachable by
converting a mod that has already been converted, and by any mod released with both folders.

The first fix was wrong in an instructive way: `string.Equals(from, to, OrdinalIgnoreCase)` did
**not** match, because the folder constants are written with forward slashes
(`"material/ui/car_brand_logo"`) and `Path.Combine` keeps them verbatim on Windows. The same file
reached two different ways is spelled two different ways:

    C:\temp\x\material\ui\car_brand_logo\volvo_cars.mat
    C:\temp\x\material/ui/car_brand_logo\volvo_cars.mat
    string.Equals   : False
    GetFullPath eq  : True

So the comparison goes through `Path.GetFullPath`. Worth remembering: **path identity on Windows is
not string identity** once forward slashes are in play, and `File.Copy(x, x)` throws rather than
being a no-op.

This was found only because a test was written for it and then observed to crash the entire suite.
It had nothing to do with transparency, and would otherwise have shipped as an intermittent
"conversion failed" on any second run.

- **Both logo folders have to be converted.** The car-shop copy's `.tobj` points back at
  `/material/ui/brand_logo/<brand>.dds`, so correcting only `car_brand_logo/` would leave the box.

### The encoder mistake worth remembering

The first implementation decoded the DXT1 to RGBA, knocked out the background, and re-encoded the
whole thing as DXT5 with a fresh palette fit per block. It passed every alpha assertion — the
silhouette was correct, no black on the border, most of the frame transparent.

It was **wrong**, and only a pixel-level comparison caught it:

    worst pixel: src=(123,4,33) -> out=(156,255,181)      worst error 251/channel
    341 pixels off by >64 out of 981

Volvo's red was coming back green. A BC1 block holds only **four palette entries**, so re-fitting
them from decoded pixels lands the artwork on different colours than the author chose. The
bounding-box fit is not the author's optimum and cannot be.

**The fix was to stop re-encoding colour at all.** DXT5's colour block is the same BC1 block DXT1
uses, so every source colour block is copied across **byte for byte** and only the 8-byte alpha
block in front of it is synthesised. The result is provably identical artwork rather than
approximately equal — measured at `worst error 0` across all 5429 artwork pixels of the real Volvo
and BMW logos. The lesson generalises: **when adding a channel to a block-compressed texture, copy
the blocks that already exist rather than regenerating them.**

### Alpha: fill from the border, never key by colour

A global "make black transparent" pass destroys these logos — the BMW roundel is black and white
and the Volvo badge has a black interior. The knock-out is a flood fill seeded from the image
border, so it reaches the background field and stops at the first pixel too light to be background;
interior black survives. Edge pixels caught between the two thresholds get a scaled alpha instead of
being forced opaque, which is what prevents a dark rim around anti-aliased lettering.

Both guards matter: a frame that is under 10% background has nothing to remove, and one over 98.5%
has no logo left, so both bail out and leave the original byte-identical rather than guessing.

### Process lesson

The alpha assertions all passed while the colour was badly wrong, because alpha was the thing being
tested and colour was assumed rather than measured. Comparing **against the source image** — not
against an expectation — is what exposed it. A test that checks the property you just implemented
will not notice the property you broke.

---

## Badge rendering, verified in game

The last outstanding item from v1.3.9 was that every badge check so far had been byte-level, and
byte-level tests cannot confirm what the car shop actually draws. **The maintainer has now
screenshot in game, and the black-box fix is confirmed working.** No black rectangle behind
either the Volvo or the BMW badge, alongside the stock Ford, Dodge and RAM badges, which render
exactly as they always did. That was the whole point of the DXT1 → DXT5 work and it holds.

The same screenshot shows the two cosmetic defects are still there, so this section records what
they actually are rather than what they looked like.

### What the screenshot shows

| Badge | Symptom | Verdict |
|---|---|---|
| Volvo | small compared with the stock badges | Confirmed, and now explained below |
| BMW | looks stretched / squashed horizontally | Confirmed, and now explained below |
| Ford, Dodge, RAM | correct, unchanged | Unaffected, as intended |
| Colour | BMW and Volvo keep their brand colours; the stock badges read monochrome | Still an open item (v1.4 item 9) |

### The cause, measured from the converted archives

Measured with the converter's own `BrandLogoAlpha.TryDecode`, on the `car_brand_logo/*.dds` inside
the two `_roadtrip.scs` the maintainer produced:

| Badge | Canvas | Opaque bounding box | Origin | Occupies | Canvas aspect | Art aspect | Opaque pixels |
|---|---|---|---|---|---|---|---|
| Volvo `volvo_cars.dds` | 128x64 | 64x47 | (32,9) | **50%** wide, 73% tall | 2.00:1 | 1.36:1 | 13.3% of canvas |
| BMW `bmw.dds` | 256x64 | 127x49 | (64,7) | **50%** wide, 77% tall | 4.00:1 | 2.59:1 | 27.8% of canvas |

Both are correct DXT5 with 6 mip levels, and both carry the artwork centred on a canvas **twice
as wide as the artwork needs**. So neither badge is distorted by the game and neither is the wrong
resolution. The game scales the whole canvas into its slot, and the logo ends up at half the size
it could have been. That is exactly the "small Volvo".

BMW reads as *stretched* for a second, compounding reason: its canvas is 4:1 while the roundel
inside it is 2.59:1. The game stretches the canvas, and the roundel is squashed horizontally
along with it. The roundel should be round; the file makes it an ellipse before the game touches
it.

### What this changes about the fix

The v1.4 item was written as "normalise the geometry", which invited a resample. **Do not
resample.** Both problems are solved by **cropping to the opaque bounding box** and padding to the
reference aspect, because:

- Cropping **copies existing blocks**. It re-encodes nothing, so the byte-for-byte colour
  guarantee the alpha pass is built on survives intact — which is precisely what a resample would
  destroy, and what caused the earlier "Volvo's red came back green" failure.
- Both bounds are already block-aligned in the vertical direction (Volvo y 9–55, BMW y 7–55), so
  the horizontal crop has to snap to 4-pixel block boundaries or it cannot be done as a copy. That
  is a detail worth pinning in a test rather than discovering.
- The target aspect is **1.97:1**, read from `aux[0]` in the base game's own `brand_logo/*.mat`:
  175x89 for eight of the nine badges, 178x93 for `modded`. See the roadmap item above for why the
  textures themselves cannot be read.

### Process note

This took three releases to close, and the reason is worth keeping: the badge work was verified
exhaustively at the byte level and every check passed while two visible defects remained. What was
missing was a measurement of the thing actually being judged — **how much of the canvas the
artwork occupies**. Every test asked "is the alpha correct?" and alpha was correct. Nobody asked
"is the logo the right size?", because the obvious place to look was unreadable: the pixel
dimensions live in a `.dds` that is packed inside a `.tobj` and cannot be extracted from a HashFS
v2 archive.

Don't ask what the 2 means — check that it never varies. It is 2.00000 in all nine dealer badges
and in 91 of 91 `ui.sdf.rfx` materials in `base.scs`, so copying it verbatim is safe. Had it
differed anywhere, guessing would have been the whole risk of writing our own `.mat`.

The measurement that answered it was in a different file. The badge slot is not a property of the
texture at all — it is declared as `aux[0]` in the `.mat`, which is plain text and extracts
cleanly. Two earlier turns of this work assumed the answer lived in the texture because that is
where the pixels are.

The general lesson: **a property that was never measured should not be reported as working**, and
when a measurement turns out to be unreadable, the answer is usually in a different file rather
than in a more elaborate way of reading the same one.
