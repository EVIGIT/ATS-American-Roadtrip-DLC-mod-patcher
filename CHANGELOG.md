# Truckers Tool Kit

Current App Version: v1.4.1

## v1.4

### Added
- **Converting one mod twice under two brand tokens now warns.** The two dealer-branding options are both on by default and are *not* independent — "keep the mod's own brand" and the typed Dealer ID are alternatives, not a pair. Converting one mod under two tokens ships two archives, each with its own `material/ui/car_brand_logo/<token>.*`; the game resolves a badge by file name, so both load and each silently replaces a **different** base-game brand. Nothing said so before.
  - Detection reads the brand token back out of the previous archive's `def/vehicle/car_dealer/<token>/` folder rather than remembering conversions in settings. Settings are per-user and get cleared; the archive in the output folder is the artefact that actually proves a conversion happened, and it is what would really conflict.
  - **A warning, never a refusal.** Two brands can be exactly what someone wants, and silently refusing a conversion they did intend is a worse failure than an extra line in the log. It stays silent on a first conversion and on an ordinary re-run under the same brand, and does not fire for an unrelated mod's archive in the same folder.
- **Dealer badges are now cropped to the geometry the base game's own badges use.** A converted badge sat on a canvas with empty margin either side of the artwork, so the game scaled the whole canvas and the logo drew at roughly half the size it could. Measured from `base.scs`'s `/material/ui/car_brand_logo/` — whose GDeflate-packed textures had to be decoded to be read at all — every stock badge is a **175×89 canvas (1.97:1) with the artwork spanning the full canvas width**: Dodge's artwork is 175×25, Ford's 175×53, RAM's 175×41. Converted badges are cropped to their own opaque bounds and given a canvas of that shape. On the real files: Volvo 128×64 → 64×48, BMW 256×64 → 128×64 with the artwork spanning 99% of the width.
  - A logo **taller** than the slot aspect cannot be padded into it — Volvo's artwork is 64×48 (1.33:1), and no canvas at that width is 1.97:1 while holding 48 rows. Forcing it would mean cropping or squashing the logo, so in that case the canvas keeps the artwork's own aspect. None of the three stock badges fall into this case; it only arises on converted mods.
- **The crop is lossless and provably so.** Colour blocks are copied byte for byte rather than re-encoded, so on the real Volvo and BMW badges the artwork comes back with a maximum per-channel error of **0**, **0 opaque pixels lost**, still DXT5, and re-running the converter changes nothing.
- **Two new opt-in passes, both OFF by default**, because both change colour and therefore forfeit that guarantee:
  - **Unsquash pre-squashed artwork** (`BrandLogoResample`). The BMW roundel is 2.59:1 in the mod artist's own pixels, so no amount of canvas work makes it read as a roundel. This resamples the artwork toward 1:1.
  - **Greyscale** to match the stock treatment. The base game's badges are monochrome — Dodge, RAM and Ford all read grey/white — while a converted badge keeps its brand colours and looks like a different kind of object. Alpha is copied through untouched, so it cannot disturb the silhouette or reintroduce a background.

### Changed
- ModConverterVerify grew from 224 to **242** checks. Three new sweeps close gaps that no existing check could see, all three found by reasoning about what was *not* being asserted rather than by a failure:
  - **Every unit name in a converted archive is swept for legality.** `NamespaceAnonymousUnits` throws on an over-long namespace it is handed, but nothing checked each component of every name actually *produced*, and the 12-character limit was originally found in the wild rather than as a thrown exception. The sweep reads every `.sii` in the real output and checks every component. It deliberately includes unit *references* as well as declarations, because an over-long name is equally illegal when referred to, and a stale reference is where a bad name survives a conversion. Relative names (`.package`) are excluded — they have no fixed limit, being resolved against their containing unit.
  - **Two same-brand mods are converted end to end and compared archive to archive.** The old checks exercised the namespace builder in isolation; this one asserts the property that actually failed. Both real Cadillacs are `truck_dealer/cadillac` and both declare global `_nameless` units, and if their namespaces collide the later archive silently *replaces* the earlier one's units rather than erroring — which is what made the car appear twice in the shop. No single-mod check can see a collision, because it is a property of the pair. Measured on the fixtures: `firstca_ygu0` vs `secondc_mul7`, no overlap.
  - **The duplicate-brand warning's own helper is asserted directly.** `ReadBrandTokens` is what grounds the check, so it is tested on a real converted archive and on a missing and a corrupt one — it is the part that could silently start returning nothing, which would turn the warning off without any visible failure.
  - Two fixture bugs surfaced while writing these and are worth recording, because both produced *false passes*: the unit-name pattern first matched only whole lines, so a declaration ending in ` {` was invisible and the sweep reported zero names; and the same-brand fixtures originally declared no `_nameless` units at all, so the collision check passed by comparing two empty sets. Both now assert that the sweep actually found names before asserting anything about them.
- ModConverterVerify grew from 142 to 224 checks, covering the crop at the measured stock geometry, both opt-in passes, the badge-pass ordering that crashed the game, and the asset-path repair (binary `.tobj` layout, length-prefix rewriting, idempotence, refusal on an unrecognised layout, and base-game references left alone). The lossy encoder is verified on a **synthetic image with known flat regions** rather than a real logo: comparing a resampled badge against the pre-resample one requires mapping coordinates through the stretch, and a mistake in that mapping produced a false "everything drifted" reading before this test existed.
- **The window is now 1142px tall by default** (was 1080px), because the options card grew a row. The 1080p target was given up deliberately: the fifth row was already being placed and only being clipped, and a control the user cannot see or click is worse than a 62px taller window.

### Fixed
- **The dealership badge was being decoded into coloured speckle, and the speckle was invisible to every existing test.** This is what made the BMW roundel read as a mess of red, green and cyan pixels rather than a roundel. The artwork was never damaged: `bmw.dds` ships a clean, genuine roundel, drawn squashed to 2.59:1 by the mod artist.
  - **Cause:** a BC1/BC3 colour block stores its two endpoints as packed 5:6:5 integers, and all three interpolated palette entries were computed by adding and dividing those *packed* values. A carry out of the 6-bit green channel then runs straight into red, so the midpoint of two near-identical greys can come back saturated. On the real badge's block (22, 3), endpoints `0x39E8` and `0xC659` average to `(127, 131, 136)` per channel and to `(132, 4, 0)` — a saturated red — as packed integers. **1371 of 16384 pixels were wrong, worst error 136 per channel.**
  - **Why nothing caught it:** the file is structurally perfect. Dimensions, block counts, format and mip chain all validated, and the pass was *lossless* for the crop, so the byte-for-byte guarantee the crop is tested on still held — the corruption happened while *reading*, before any of that. The only thing that exposed it was decoding the badge to pixels and rendering it.
  - **Fix:** interpolation now expands both endpoints to 8 bits, averages per channel, and re-quantises once (`Interpolate565`). Applied to both the decoder's palette and the lossy encoder's index search, which had the identical defect. Worst per-channel error on the real badge drops from **136 to 5**, which is ordinary 5:6:5 rounding. The artwork bounds also measure correctly as a result — **120×49 rather than 127×49**, because the phantom margin was corrupted pixels being read as opaque.
  - Regression-tested both ways: the decoder against the real endpoints lifted from that file, and the encoder by asserting a greyscale ramp survives the lossy path without gaining a colour cast.
- **The dealership no longer crashes the game when "Un-squash squashed badges" is on.** This was the resample pass, and it had two defects that only showed up in game:
  - **The canvas was not a whole number of 4x4 blocks.** The BMW artwork measures 127×49, so the resample emitted a **127×127** canvas. DXT5 stores 4×4 blocks, so a 127-wide image needs a padded final block, and the mip chain `127 → 63 → 31 → 15 → 7 → 3` is ambiguous between a floor and a ceil halving. The encoder's byte layout and the engine's own arithmetic could disagree at every level while the file still looked self-consistent to a byte-count check — which is exactly why every offline test passed. Dimensions are now rounded up to a whole number of blocks: **127×127 → 128×128**.
  - **The passes ran in the wrong order.** The unsquash ran *after* the crop, so it discarded the crop's measured stock-shaped canvas and replaced it with a bare square. The badge reached the game as neither the stock 1.97:1 shape nor anything the crop had verified. The order is now **transparency → unsquash → crop → greyscale**, so the crop gives the corrected artwork the stock canvas shape.
  - Both are covered by regression tests: `RoundUpToBlock` is pinned directly, and the ordering is asserted through a real conversion by comparing where each pass appears in the log. Verified to actually fail when the ordering is inverted.
- **Converted cars now render with their textures instead of the engine's pink fallback material.** This was the cause of the untextured car, and it was invisible to every check that existed — the conversion *looked* perfect. The asset pass moves `vehicle/truck/<car>` to `vehicle/car/<car>` and rewrites the `.sii` definitions, but a mod's own **materials and compiled textures hold absolute `/vehicle/truck/...` paths pointing straight at those files**, and nothing rewrote them. Measured on the real converted BMW archive: **0 entries left under `vehicle/truck/` while 96 files still referenced it** (52 `.mat`, 44 `.tobj`). The game's own log from that session names every one: **88 × `<ERROR> [mem server] Failed to init update for object '/vehicle/truck/m5_g90/tex/...'`**. A surface whose material cannot load is drawn in the fallback material, which is the pink car.
  - `.mat` is plain text (`source : "/vehicle/truck/..."`), so it is a byte-level replacement that leaves every other byte alone. `.tobj` is **binary** and carries the path as a length-prefixed string at the end of the file; `/vehicle/car/` is two characters shorter than `/vehicle/truck/`, so the length prefix is rewritten too and the file re-spun. The layout was measured across all 45 affected `.tobj` files — prefix 8 bytes before the string, string running to EOF — with zero mismatches, and any file that does not match is **left untouched and reported** rather than guessed at, because a corrupt compiled object is worse than a texture that stays broken.
  - **Only references into the mod's own moved files are rewritten.** A mod also references base-game files that share the prefix: the BMW mod points at `/vehicle/truck/share/dashboard.tobj`, `gps.tobj` and `glass_ex.tobj` without shipping them. `base.scs` still holds those at their original path and is never converted, so rewriting them would point the car at nothing. The moved set is therefore captured *before* the move. On the real BMW conversion: **90 of 93 references now resolve inside the archive, and the 3 exceptions are left at their original base-game paths** — confirmed correct against `Ford Focus Mk3_roadtrip.scs`, a Road Trip car that renders properly and references `/vehicle/truck/share/dashboard.tobj` itself.
  - Patch mode deliberately does **not** run this pass: the original mod keeps its `vehicle/truck` tree and stays mounted, so those paths still resolve there.
- **The two badge toggles ("Un-squash squashed badges" and "Greyscale badges") were unreadable and unclickable.** `MainLayout.ToggleRowCount` was **4 while the layout code placed 5 rows**, so the options card was sized for four and the fifth row was drawn *past the card's bottom edge* and clipped by it. The row existed and was clickable in the model, just not on screen. The count and the card height are now derived from one number, so they cannot disagree again. The card grows 416 → 478px and the default window 1080 → 1142px; `AutoScroll` still covers anything shorter.
- **The lossy colour encoder wrote colour into the wrong half of each DXT5 block.** A DXT5 block is 16 bytes laid out as alpha first then colour; the encoder wrote colour at offset 0, overwriting the alpha it had just produced and leaving the real colour half as zeros. Every resampled badge decoded as solid black. Now written at offset 8, verified by flat red and blue regions round-tripping exactly.

### Known issues in this release
- **The resample pass changes colour, by necessity.** A BC1/BCn colour block is self-contained — two endpoints plus 2-bit indices, all local to one 4×4 block — which is why the crop can relocate whole blocks and stay byte-identical. A resample is not a relocation: pixels land in different blocks, so the endpoints must be re-derived from the new pixel set, and the author's described a different set of pixels. Measured on the real BMW artwork, 97 of 98 output rows read a different source row. There is no lossless route, which is why it is opt-in and off by default. Its endpoint fit is by principal axis rather than bounding box, which keeps the damage well below the 251-per-channel error the original bounding-box fit produced on these same logos.
- **The stock badges' own artwork is monochrome, but a converted badge's is not** unless greyscale is switched on. Both Cadillacs sharing one dealer is correct — they are both `truck_dealer/cadillac` — not a symptom.

### Verified in game
- **The badge work and the texture repair are both confirmed by the maintainer**, after a full ATS restart. Converted cars render with their own textures, and the dealership badge is correct. That closes the last thing this release was waiting on: the crop and both opt-in passes had only ever been verified against the real badge files offline, and the texture fix against the archives and the game's own error log rather than on screen.

## v1.4.1

> **This release enables patch mode; it does not contain the fix.** The `invalid_vehicle` fix below is
> already in **v1.4**, shipped dormant behind a setting that is off by default. Nothing here can affect
> anyone who leaves the default alone, which is what let v1.4 go out on badge and texture verification
> alone rather than waiting on the patcher.
>
> So this section is not a list of changes to the code — it is the verification that has to happen
> before the default flips, plus the flip itself. **Patch mode stays off by default until a converted
> car has been loaded from a save in game**, because that is the only test that distinguishes "the
> patch writes correct bytes" from "the patch loads without breaking the save". Everything so far
> proves the former.

### Added
- **A new mark for the app.** The old shield-and-sunset was not wrong, but it described a scenic
  drive rather than a truck. The new one is a side-profile lorry on amber over the app's dark surface.
  - Designed against the two brands a player already has on their desktop. **TruckersMP** publishes
    exact brand values (Red `#B92025`, Black `#040608`, White `#FEFEFE`), uses a lorry silhouette as
    its motif, and ships a deliberately simplified badge for small sizes because its wordmark dies
    below ~32px. **SCS's** own ATS identity is a dark UI with a warm amber accent. The new mark takes
    the shape language from the first and the palette temperature from the second.
  - **Side profile, not front-on.** A front view is genuinely ambiguous between a car, a van and a
    bus — the first attempt did read as a car, which is the entire reason this one exists. Everything
    is proportioned around the three wheels, because wheel size is what separates a truck from a car
    at a glance and it is the first thing to survive at 16px.
  - Amber `#FF8A3D` is the app's own accent from `Theme.cs`, so the icon and the running UI agree.
    Changing one without the other is how an icon stops matching the software it belongs to.
  - Still generated, not hand-drawn: `Assets/make_logo.py` is committed and reproducible, and
    `Assets/_sizes_preview.png` is a contact sheet of every icon size so legibility can be judged
    rather than assumed. Everything is drawn at 1024 and downsampled with LANCZOS — nothing is
    authored at icon size.

> **The app is now Truckers Tool Kit.** It was "ATS American Roadtrip Car Patcher". The rename is in
> the tree after the v1.4 tag, so v1.4 itself keeps its original name — a tagged release is not
> rewritten after the fact. Three things were deliberately *not* renamed, because renaming them would
> have broken working installs rather than branding anything:
>
> - **The executable and assembly name** (`ATS American Roadtrip Car Patcher.exe`). The updater
>   downloads a release and looks for the *running* build's executable inside it. Rename the exe and
>   every update from a pre-rename build fails on "the release package did not contain ...".
> - **The output filenames** `<mod>_roadtrip.scs` and `<mod>_roadtrip_patch.scs`. These are what
>   users actually install, and `roadtrip` here is ATS's own car-mode name, not the product's.
> - **"Road Trip" throughout the UI** ("Patch for Road Trip", the launch-page step, the
>   `(Road Trip patch)` entry in generated `.sii` files). That is the game's mode, not this tool.

### Changed
- **Renamed to Truckers Tool Kit** across the app name, window titles, About tab, conversion report
  header, CLI, repository slug and description, README, and the C# namespace (`ATSRoadTripConverter` →
  `TruckersToolKit`, including the solution, project and workspace files).
- **Three silent-reset traps the rename would otherwise have sprung**, each fixed rather than shipped:
  - **Settings folder.** `%AppData%\ATSRoadTripConverter` → `%AppData%\TruckersToolKit`. Renaming
    without care loses every user's theme, font size and dealer preferences, and the app starts up
    looking reset with nothing in the log to explain it. The old file is now adopted once on first run.
  - **Default theme name.** The dark palette "Roadtrip" is now "Truckers". A settings file still
    holding the old name resolves through an alias table instead of falling through to "no match",
    which would have quietly changed the user's colours. Case-insensitive, whitespace-tolerant, and
    asserted.
  - **Release asset name.** `ATS-American-Roadtrip-Car-Patcher-win-x64.zip` →
    `TruckersToolKit-win-x64.zip`. This is the one that would have looked like a network fault: a
    user on a pre-rename build downloads the *new* release with *their* older code, which asks only
    for the old filename. Old names stay in `LegacyWindowsReleaseAssets` and every known name is
    tried, so the first update after a rename still works.
- The conversion report header no longer hard-codes a version. It read `v12`, which was three naming
  schemes out of date by the time it was written and nothing noticed, because a version string in a
  report nobody asserts on cannot go red in a test.
- **Patch mode becomes the default**, once a converted car has been loaded from a save in game. This
  is the only other code change; the fix it depends on shipped dormant in v1.4.

### Fixed
*(Shipped dormant in v1.4, behind the off-by-default setting. Listed here so the history is in one place.)*
- **Definitions-only patch mode no longer deletes the car.** This was the `invalid_vehicle` bug, and it was not a label on a known problem — it was actively destroying data. Proven against a real patch output: it shipped a **13-byte `SiiNunit\n{\n}\n`** over the original mod's `def/vehicle/truck_dealer/<brand>/<model>.sii`, replacing **2,343 bytes that defined ~20 units** — the car's entire accessory list (`chassis`, `cabin`, `engine`, `transm`, wheels, `paint`, `mirror`, `steering_w`). SCS unit names are global and a later mod's definition of the same name **replaces** the earlier one, so the patch did not neutralise the truck-era listing: it deleted the car, and any save that had driven it failed to load. The stub is gone, and patch mode now only ever adds to the original mod. The converted car is still registered under `def/vehicle/car_dealer/<brand>/` by the existing dealer translation, which is what puts it in the shop — so the dealership listing is unaffected.
- **Patch mode can now write to a folder that does not exist yet.** `ZipFile.CreateFromDirectory` threw `DirectoryNotFoundException` when the output folder was new. Full conversion masked this because the GUI always pre-creates the folder, so the only way to reach patch mode was to already have one. Caught by the new regression test.

### Known issues in this release
- **`invalid_vehicle` is fixed but patch mode is still not in-game verified.** The fix is proven against the patch's own bytes and by regression test, not yet by loading a save in ATS, so the default stays off. This is the one thing standing between v1.4.1 and being a one-line release.
- Cross-brand Volvo/BMW logo duplication is closed for conversion; the patcher-side half is still open.

## v1.3.9.1

### Fixed
- **Option descriptions were cut off at larger font sizes.** The options rows were a fixed 62px, but the Font Size setting scales every font from 8 to 16 while control sizes stay put. Measured against the real description strings at the real 322px text width, font size 11 needs 65px, 12 needs 68, and 16 needs 108 — so at 11 and above all six option descriptions were truncated. It was silent because `TextRenderer` ellipsises with "..." instead of throwing, so nothing looked broken. The row height is now measured from the wrapped text and the options card grows to match, with the window following.
- The default is unchanged: at the default font size the measured path reproduces the previous 62px rows, 416px card and 1080px window exactly, so 1080p is unaffected for anyone who has not changed the setting.

### Changed
- SettingsMigrationVerify grew from 88 to 100 checks, covering the measured row height: that it reproduces the hand-verified card and window heights at the default font size, that it grows rather than clips at font sizes 11 and 16, that it never shrinks as the text grows, and that the last row still ends inside the card.

### Known issues in this release
- **Dealer badges are transparent, but not re-laid-out.** Verified in game: the black rectangle is gone from both the Volvo and the BMW badge. What remains is cosmetic — a wide logo can still look stretched (the BMW roundel) and a narrow one small (the Volvo badge) beside the base game's badges, and a coloured badge keeps its own colours rather than being greyscaled to match. Both were measured: the logos are correct DXT5 files that place the artwork on a canvas twice as wide as it needs, so the game scales the canvas and the logo lands at half the available size. Cropping the empty margin and greyscaling are scheduled for v1.4.
- **Definitions-only patch mode can still produce `invalid_vehicle`.** It is off by default and full conversion is unaffected; the proper fix is scheduled for v1.4.3.
- **Full conversion still omits the shared `automat/` and `material/ui/accessory/` asset roots**, which is the cause of the occasional unconverted texture. Scheduled for v1.4.3.
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
