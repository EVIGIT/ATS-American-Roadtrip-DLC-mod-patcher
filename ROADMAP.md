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

### Track B — Conversion bug (highest value)

Symptom: converting a mod that already contains a previous patch **duplicates the car
from the earlier patch instead of adding the new one**.

Known relevant code: `MergeTree` in `ModConverter.cs` (~line 795). On a collision it
preserves the existing car file and writes the incoming truck file beside it as
`<name>.truck_source` — so on an already-patched mod the *previous* car legitimately
wins. The dealer `index.sii` merge is the other likely suspect.

- [ ] Reproduce with a real mod (patched, then patched again).
- [ ] Decide the correct rule: new car **replaces** the old, or both coexist under
      distinct names. Needs a maintainer decision.
- [ ] Fix, and add a regression test to `ModConverterVerify`.
- **BLOCKED ON:** a mod that reproduces it, ideally with an unpatched copy for A/B.

### Track C — Remaining settings, easiest to hardest

1. Reopen last used folder (button) — trivial, no converter change.
2. Keep the work folder on failure + surface the path in the log — low risk.
3. Conflict policy: overwrite / skip / rename — medium, touches the output write path.
4. Post-conversion verification (re-read outputs, assert speed limiter + car data
   landed) — medium, new read-back pass.
5. ~~Dry-run preview~~ → **deferred to v1.3.9**.
6. ~~Custom vehicle type editor~~ → **deferred to v1.3.9**.

### Track D — GitHub updater (evidence already gathered)

Evidence from `%TEMP%\ats-roadtrip-local-update.log`:

```
Updater started 2026-10-02T21:52:24.78+01:00
FAILED: Copy failed with Robocopy exit code 16.
```

`ats-roadtrip-local-update.ready` also exists containing `ready`.

Reading: the v1.3.6 handshake **works** — the helper starts, signals ready, waits for
the app to exit, and fails loudly instead of silently. Robocopy exit code 16 = serious
error. The install location is **not** the cause: both extracted copies live under
`Downloads` and the folder is writable (verified by a write/delete test).

Remaining hypotheses:
- Files still locked after the PID disappears (Windows holds the section handle
  longer than our 800 ms grace), and a single locked file aborts the whole run.
- Retries re-run the whole tree instead of isolating the failure.

Plan:
- [ ] Reproduce the exact robocopy against the extracted copy, app running vs closed.
- [ ] Fix: longer/targeted backoff and per-file resilience so one locked file cannot
      abort the update.
- [ ] **Extend `LocalUpdaterSmokeTest` to cover `waitForAppExit: true`.** It currently
      skips that path entirely, which is why v1.3.6 shipped without catching this.
- [ ] Verify with a real end-to-end update.
- If it proves stubborn, move to v1.3.9.

### Release order

---

## v1.3.9

Carried over, then additions.

### Deferred from v1.3.8
1. **Dry-run / preview mode** — report file count and patch manifest, write nothing.
2. **Custom vehicle type editor** — CRUD UI for types that already persist.

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

### Architecture
10. **XML-driven settings pages.** The settings tabs are now genuinely data-shaped
    (see the `AddOption` helper). Schema-validated, so adding a setting becomes a
    small XML edit instead of ~40 lines of C#. This is what makes later settings
    work far cheaper.

### Engineering
11. **Broaden `ModConverterVerify`** — dealer index merging and the truck→car
    migration, so the Track B class of bug cannot return.

### Suggested ranking
3 and 4 first (one is a real hole in the gating, one is visible), then 5 (users hit
password-protected mods and currently dead-end), then 10, then the rest.
1. Health check baseline → findings list to maintainer.
2. Track B fix + regression test.
3. Track C items 1–2.
4. Track D fix + extended smoke test.
5. Health check again → updated list to maintainer.
### Polish and consistency (added)
12. **Replace the remaining system dialogs.** Three `MessageBox.Show` call sites are
    left in `ConverterForm.Conversion.cs` (lines 460, 494, 1241 — GitHub update failure,
    hot-reload block, invalid accent colour). The themed `ThemedConfirmForm` already
    exists and is preferred; consistency pass, and these will then match the theme.
13. **Main window does not fit small screens.** `ClientSize` is a fixed 900px tall with
    a 700px `MinimumSize`, so on a 768px-tall display the log card is clipped off the
    bottom. Make the main window scroll or compact, and verify at 1366x768.
14. **Settings schema version and migration.** `AppSettings.ThemeMode` is a live legacy
    field still read by the migration path. Introduce an explicit schema version and
    drop the dead field. This is the prerequisite that makes a v2.0 settings break safe.
15. **Save log to file / open logs folder.** Copy-to-clipboard exists; saving a dated
    log next to the converted output is what people actually attach to a bug report.
16. **Docs pass.** The repository is public now, so the README should describe the
    current feature set (tiers, settings, updater) rather than the older feature list.

### Accessibility and polish (added, optional if scope gets tight)
17. Accessible names/roles for the custom controls (`FlatButton`, `ToggleSwitch`),
    plus a deliberate tab order in Settings. `ThemeSwatch` already has one.
18. DPI-correct custom painting — the hand-drawn controls use raw pixel metrics, so
    they can look soft at 150%.
19. "Skip this version" for update prompts, and a first-run "what's new" panel.

### Deliberately NOT in v1.3.9 — v2.0 territory
- Real membership verification backend and the Ko-fi flow (items 3 and the Ko-fi tier).
  These change product behaviour, not just UI, and deserve their own release.
- Any settings-file format break. 1.3.9 only *adds* a schema version so v2.0 can.
6. Go / no-go → commit, tag `v1.3.8`, release.