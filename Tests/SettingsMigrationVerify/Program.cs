using System.Text.Json;
using ATSRoadTripConverter;

var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
    Console.WriteLine((condition ? "PASS " : "FAIL ") + message);
}

// Used to assert that something *does* throw, so a "this cannot fail" claim is proved rather
// than assumed. Several checks here depend on System.Text.Json's real behaviour, and asserting
// the behaviour is what stops the assertion being quietly inverted later.
bool Throws(Action action)
{
    try
    {
        action();
        return false;
    }
    catch
    {
        return true;
    }
}

// --- Version reading -----------------------------------------------------------

// A file written before versioning existed has no SchemaVersion and must read as 0, so the
// migration runs. This is the exact shape a real pre-v1.3.9 settings.json has.
Check(
    SettingsSchema.ReadVersion("{\"DefaultDealerId\":\"volvo\",\"ThemeMode\":\"Light\"}") == 0,
    "a pre-versioning settings file reads as schema version 0");

Check(
    SettingsSchema.ReadVersion("{\"SchemaVersion\":1}") == 1,
    "a current settings file reads its own schema version");

// Case-insensitive: the serializer writes PascalCase, but a hand-edited file may not.
Check(
    SettingsSchema.ReadVersion("{\"schemaversion\":2}") == 2,
    "SchemaVersion is matched case-insensitively");

// A damaged file must not be trusted as current, or a broken file would skip migration.
Check(SettingsSchema.ReadVersion(null) == 0, "a null settings file reads as version 0");
Check(SettingsSchema.ReadVersion("") == 0, "an empty settings file reads as version 0");
Check(SettingsSchema.ReadVersion("not json at all") == 0, "a malformed settings file reads as version 0");
Check(SettingsSchema.ReadVersion("[1,2,3]") == 0, "a non-object settings file reads as version 0");
Check(SettingsSchema.ReadVersion("{\"SchemaVersion\":\"one\"}") == 0, "a non-numeric schema version reads as version 0");
Check(SettingsSchema.ReadVersion("{\"SchemaVersion\":-3}") == 0, "a negative schema version reads as version 0");

// --- Legacy ThemeMode reading --------------------------------------------------

Check(
    SettingsSchema.ReadLegacyThemeMode("{\"ThemeMode\":\"Light\"}") == "Light",
    "the legacy ThemeMode is still readable from raw JSON");
Check(
    SettingsSchema.ReadLegacyThemeMode("{\"thememode\":\"Light\"}") == "Light",
    "the legacy ThemeMode is matched case-insensitively");
Check(
    SettingsSchema.ReadLegacyThemeMode("{\"ThemeName\":\"Daylight\"}") == null,
    "a file without ThemeMode reports no legacy theme");

// This is the property that has been removed from AppSettings. Reading it only works because
// it comes from the raw JSON rather than the deserialized object.
Check(
    SettingsSchema.ReadLegacyThemeMode("{\"ThemeMode\":\"Light\"}") is not null,
    "the removed ThemeMode field survives on disk for the migration to read");

Check(SettingsSchema.ReadLegacyThemeMode(null) == null, "a null file reports no legacy theme");
Check(SettingsSchema.ReadLegacyThemeMode("{oops") == null, "a malformed file reports no legacy theme");

// --- Theme resolution ----------------------------------------------------------

Check(
    SettingsSchema.ResolveThemeName("", "Light") == "Daylight",
    "a legacy Light preference migrates to Daylight");
Check(
    SettingsSchema.ResolveThemeName("", "Dark") == "Roadtrip",
    "a legacy Dark preference migrates to Roadtrip");
Check(
    SettingsSchema.ResolveThemeName(null, null) == "Roadtrip",
    "an absent legacy preference defaults to Roadtrip");
Check(
    SettingsSchema.ResolveThemeName("   ", "Light") == "Daylight",
    "a whitespace-only theme name is treated as unset");

// A stored ThemeName must win. Without this, a v1 file could be dragged back to a legacy
// preference on every load, and the migration would never settle.
Check(
    SettingsSchema.ResolveThemeName("Midnight", "Light") == "Midnight",
    "an explicit ThemeName wins over the legacy ThemeMode");
Check(
    SettingsSchema.ResolveThemeName("Daylight", "Dark") == "Daylight",
    "an explicit ThemeName is not overwritten by a legacy Dark preference");

// --- Round trip through a real serialized document -----------------------------

// Mirrors what SettingsManager.Save writes, to prove a migrated file comes back as current
// version and therefore does not migrate again.
var migrated = JsonSerializer.Serialize(new Dictionary<string, object>
{
    ["SchemaVersion"] = SettingsSchema.CurrentVersion,
    ["ThemeName"] = "Daylight"
});

Check(
    SettingsSchema.ReadVersion(migrated) == SettingsSchema.CurrentVersion,
    "a re-serialized migrated file reads as the current version");
Check(
    SettingsSchema.ReadLegacyThemeMode(migrated) == null,
    "a migrated file no longer carries the legacy theme field");
Check(
    SettingsSchema.ResolveThemeName(
        SettingsSchema.ReadVersion(migrated) >= SettingsSchema.CurrentVersion ? "Daylight" : null,
        SettingsSchema.ReadLegacyThemeMode(migrated)) == "Daylight",
    "a migrated file resolves to the same theme on a second load");

// The version must be exactly 1 for now; this check is what forces a deliberate decision
// to bump CurrentVersion rather than editing the constant silently in a release.
Check(SettingsSchema.CurrentVersion == 1, "the current settings schema version is 1");

// --- Release notes parsing ------------------------------------------------------
// The launch page shows the changelog entries newer than the version last launched, so the
// version comparison has to be right or a user silently misses their changes.

var changelog = """
    # ATS American Roadtrip Car Patcher

    Current App Version: v1.3.9

    ## v1.3.9

    ### Added
    - Added the settings schema version.

    ### Changed
    - Changed the theme list.

    ## v1.3.8

    ### Added
    - Added camera retargeting.

    ## v1.3.7.2

    ### Fixed
    - Fixed the updater log.

    ## Unreleased

    ### Added
    - Added something not released yet.
    """;

var sections = ReleaseNotes.ParseSections(changelog);
Check(sections.Count == 3, "only the three released sections are parsed");
Check(
    sections.All(section => !section.Heading.Equals("Unreleased", StringComparison.OrdinalIgnoreCase)),
    "the Unreleased section is never shown as a release");
Check(
    sections.Any(section => section.Bullets.Contains("Added something not released yet.")) == false,
    "Unreleased bullets never leak into a released section");
Check(
    sections.Single(section => section.Heading == "v1.3.9").Bullets.Count == 2,
    "bullets under separate ### subheadings stay with their version");
Check(
    sections.All(section => !section.Bullets.Any(b => b.Contains("Current App Version"))),
    "the preamble before the first heading is not treated as a bullet");

Check(
    ReleaseNotes.SectionsNewerThan(changelog, "v1.3.8").Select(s => s.Heading).SequenceEqual(new[] { "v1.3.9" }),
    "only releases newer than the last launched version are shown");
// v1.3.7.2 really is newer than v1.3.7, so all three come back. This is the check that
// stopped a four-part patch release from being hidden behind the release it patched.
Check(
    ReleaseNotes.SectionsNewerThan(changelog, "v1.3.7").Select(s => s.Heading)
        .SequenceEqual(new[] { "v1.3.9", "v1.3.8", "v1.3.7.2" }),
    "several missed releases are all shown, newest first, including a four-part patch release");
Check(
    ReleaseNotes.SectionsNewerThan(changelog, "v1.3.9").Count == 0,
    "an already-current user is shown nothing");

// The launch page must never dump the entire history on a first run, where there is no
// previous version to compare against.
Check(
    ReleaseNotes.SectionsNewerThan(changelog, "").Count == 0,
    "a first run with no previous version is shown no release notes");
Check(
    ReleaseNotes.SectionsNewerThan(changelog, null).Count == 0,
    "a null previous version is shown no release notes");

// A hand-edited or unparseable baseline must not hide the user's changes.
Check(
    ReleaseNotes.SectionsNewerThan(changelog, "not-a-version").Count == 0,
    "an unparseable previous version yields no notes rather than throwing");
Check(
    ReleaseNotes.SectionsNewerThan(changelog, "v9.9.9").Count == 0,
    "a future previous version yields no notes");

// A version ahead of the build must not crash the comparison.
Check(ReleaseNotes.TryParseVersion("v1.3.9", out _), "a v-prefixed version parses");
Check(ReleaseNotes.TryParseVersion("1.3.9", out _), "a version without the v prefix parses");
Check(ReleaseNotes.TryParseVersion("v1.3.7.2", out _), "a four-part version parses");
Check(ReleaseNotes.TryParseVersion("v1.3.9-beta", out _), "a pre-release suffix is tolerated");
Check(!ReleaseNotes.TryParseVersion("", out _), "an empty version does not parse");
Check(!ReleaseNotes.TryParseVersion(null, out _), "a null version does not parse");
Check(!ReleaseNotes.TryParseVersion("Unreleased", out _), "a non-version heading does not parse");

Check(
    ReleaseNotes.SectionsNewerThan(changelog, "v1.3.7").First().Heading == "v1.3.9",
    "v1.3.7.2 sorts above v1.3.7 rather than tying with it");
// A four-part baseline excludes the patch release it names, but keeps everything above it.
Check(
    ReleaseNotes.SectionsNewerThan(changelog, "v1.3.7.2").Select(s => s.Heading)
        .SequenceEqual(new[] { "v1.3.9", "v1.3.8" }),
    "a four-part baseline excludes itself but keeps later releases");

Check(ReleaseNotes.IsReleaseHeading("v1.3.9"), "a version heading is a release heading");
Check(!ReleaseNotes.IsReleaseHeading("Unreleased"), "Unreleased is not a release heading");
Check(!ReleaseNotes.IsReleaseHeading(null), "a null heading is not a release heading");

Check(ReleaseNotes.ParseSections("").Count == 0, "an empty changelog parses to nothing");
Check(
    ReleaseNotes.SectionsNewerThan(null, "v1.3.8").Count == 0,
    "a missing changelog yields no notes rather than throwing");
Check(ReleaseNotes.ParseSections("no headings here").Count == 0, "a headingless document parses to nothing");

// --- Regression: a mis-numbered historical heading ---------------------------------
// The real CHANGELOG.md contains a "## v12.1" heading, a typo for a much older release.
// Version parsing reads it as 12.1, which is numerically far newer than v1.3.9, so an
// uncapped query reported it as brand-new and the launch page would have shown it on
// every single launch for an already-current user.

var misnumbered = "## v12.1\n\n### Fixed\n- Fixed an ancient thing.\n\n## v1.3.9\n\n### Added\n- Added something recent.\n";

Check(
    ReleaseNotes.SectionsNewerThan(misnumbered, "v1.3.8").Select(s => s.Heading).SequenceEqual(new[] { "v12.1", "v1.3.9" }),
    "without a cap, a mis-numbered heading still sorts as newer (documents the bug)");
Check(
    ReleaseNotes.SectionsNewerThan(misnumbered, "v1.3.8", "v1.3.9").Select(s => s.Heading)
        .SequenceEqual(new[] { "v1.3.9" }),
    "a mis-numbered heading newer than the running build is hidden by the cap");
Check(
    ReleaseNotes.SectionsNewerThan(misnumbered, "v1.3.9", "v1.3.9").Count == 0,
    "an already-current user is shown nothing even with a mis-numbered heading present");
Check(
    ReleaseNotes.SectionsNewerThan(misnumbered, "v1.3.8", "v99.0.0").Count == 2,
    "a cap above the mis-numbered heading still shows it, so the cap is a bound and not a blacklist");

// A cap that cannot be parsed must not silently hide everything.
Check(
    ReleaseNotes.SectionsNewerThan(misnumbered, "v1.3.8", "not-a-version").Count == 2,
    "an unparseable cap is ignored rather than hiding every release");

// --- Main window vertical budget -------------------------------------------------
// The main window stacks a fixed header, two fixed cards and a fixed action area, then a log
// card that absorbs the rest. This budget used to be scattered magic numbers plus a comment,
// and the default height and the log floor drifted apart, which put a scrollbar on a normally
// sized desktop. It is asserted here so the two cannot disagree again.

Check(
    MainLayout.ContentAboveLog
        == MainLayout.HeaderHeight + MainLayout.FilesCardHeight + MainLayout.CardGap
        + MainLayout.OptionsCardHeight + MainLayout.CardGap + MainLayout.ActionAreaHeight,
    "the content above the log is the header, both cards with their gaps, and the action area");

// These are the numbers the maintainer verified by hand. They are pinned deliberately: changing
// one is a layout decision, not an accident, and this check is what forces that decision.
// 792 -> 856 when the options card grew by a row for the clash-avoidance switch.
Check(MainLayout.ContentAboveLog == 856, "content above the log is 856px");
Check(MainLayout.MinimumContentHeight == 1050, "the minimum content height is 1050px");
Check(MainLayout.DefaultWindowHeight == 1080, "the default window height is 1080px");

// The default height must leave the log MORE than its minimum, or the window opens already
// scrolling. This is the exact regression that was fixed by raising the default height.
Check(
    MainLayout.DefaultWindowHeight > MainLayout.MinimumContentHeight,
    "the default window is taller than the no-scroll minimum");
Check(
    MainLayout.DefaultWindowHeight - MainLayout.MinimumContentHeight == 30,
    "the default window leaves 30px more than the no-scroll minimum");
Check(
    MainLayout.LogCardHeight(MainLayout.DefaultWindowHeight, MainLayout.ContentAboveLog)
        == MainLayout.PreferredLogCardHeight,
    "at the default height the log gets its preferred size");

// A short window must never squeeze the log below its floor.
Check(
    MainLayout.LogCardHeight(700, MainLayout.ContentAboveLog) == MainLayout.MinimumLogCardHeight,
    "a window too short for the log holds it at the minimum instead of squashing it");
Check(
    MainLayout.LogCardHeight(400, MainLayout.ContentAboveLog) == MainLayout.MinimumLogCardHeight,
    "a very short window still holds the log at the minimum");
Check(
    MainLayout.LogCardHeight(MainLayout.MinimumContentHeight, MainLayout.ContentAboveLog)
        == MainLayout.MinimumLogCardHeight,
    "at exactly the minimum content height the log is exactly at its floor");

// The tab bar: adding chrome above the log must raise the budget by the same amount, or the
// log is silently squeezed and the scrollbar the v1.3.9 fix removed comes straight back.
Check(
    MainLayout.WindowHeightFor(56) == MainLayout.DefaultWindowHeight + 56,
    "adding a tab strip raises the default window height by exactly its height");
Check(
    MainLayout.MinimumHeightFor(56) == MainLayout.MinimumContentHeight + 56,
    "adding a tab strip raises the no-scroll minimum by exactly its height");
Check(
    MainLayout.LogCardHeight(MainLayout.WindowHeightFor(56), MainLayout.ContentAboveLog, 56)
        == MainLayout.PreferredLogCardHeight,
    "the log keeps its preferred size once a tab strip is accounted for");

// --- Remembered page (roadmap item 7) -----------------------------------------------
// The enum is persisted in the settings file, so what matters is that a value the current build
// does not recognise degrades to the main view instead of throwing. SettingsManager.Load
// deserialises the whole file as one object, so an unhandled throw here would discard every
// preference the user has, over one remembered page.
var pageOptions = new System.Text.Json.JsonSerializerOptions
{
    Converters = { new MainPageJsonConverter() }
};

MainPage Read(string json) =>
    System.Text.Json.JsonSerializer.Deserialize<MainPage>(json, pageOptions);

Check(Enum.IsDefined(typeof(MainPage), MainPage.Converter), "MainPage.Converter is defined (the safe default)");
Check((int)MainPage.Converter == 0,
    "MainPage.Converter is 0, so an unrecognised persisted value falls back to the main view");
Check(Enum.GetNames<MainPage>().Length == 3,
    "MainPage has exactly the three pages that can be restored");

Check(Read("0") == MainPage.Converter, "a persisted 0 restores the converter");
Check(Read("1") == MainPage.Settings, "a persisted 1 restores Settings");
Check(Read("2") == MainPage.Changelog, "a persisted 2 restores the changelog");

// The default deserialiser's real behaviour, which is what motivates the converter. Both halves
// matter and they fail differently, so both are asserted rather than assumed:
//   - an out-of-range NUMBER is silently accepted and yields an undefined enum member
//   - a STRING throws, which would discard the whole settings file
// No options are passed, deliberately: routing through pageOptions would use the converter and
// prove nothing.
MainPage Raw(string json) =>
    System.Text.Json.JsonSerializer.Deserialize<MainPage>(json, (System.Text.Json.JsonSerializerOptions?)null);

Check(
    !Enum.IsDefined(typeof(MainPage), Raw("99")),
    "the default deserialiser silently accepts an out-of-range enum number (not a throw)");
Check(
    Throws(() => System.Text.Json.JsonSerializer.Deserialize<MainPage>("\"settings\"", (System.Text.Json.JsonSerializerOptions?)null)),
    "the default deserialiser throws on a string where an enum belongs");

Check(Read("99") == MainPage.Converter,
    "the converter rejects an out-of-range page number instead of storing an undefined member");
Check(Read("\"settings\"") == MainPage.Settings, "a hand-edited name restores that page");
Check(Read("\"nonsense\"") == MainPage.Converter, "an unrecognised page name falls back to the converter");
Check(Read("null") == MainPage.Converter, "a null page falls back to the converter");
Check(Read("{}") == MainPage.Converter, "an object where a page belongs falls back to the converter");
Check(Read("-1") == MainPage.Converter, "a negative page number falls back to the converter");
Check(Read("true") == MainPage.Converter, "a boolean where a page belongs falls back to the converter");
// An empty document is NOT a page the converter can rescue, and this is asserted rather than
// skipped because it is easy to assume the opposite. System.Text.Json rejects input that contains
// no tokens before any JsonConverter is invoked, so MainPageJsonConverter.Read never runs here —
// an out-of-range *number* is a value it must survive, whereas "" is not a value at all.
//
// Nothing is lost by leaving it to throw: SettingsManager.Load wraps the whole deserialise in a
// catch that falls back to defaults, which is the correct outcome for a settings file containing
// no preferences at all. Asserting the throw pins that reasoning, so nobody later "fixes" this by
// widening the converter and implies it handles more than it can.
Check(Throws(() => Read("")),
    "an empty document throws before the converter is reached; recovery is the loader's fallback");

Check(System.Text.Json.JsonSerializer.Serialize(MainPage.Settings, pageOptions) == "1",
    "the page serialises as the number it is restored from");

// The real risk: one bad field must not cost the user every other preference.
var mixed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
    "{\"LastPage\":99,\"AccentColor\":\"#FF8A3D\"}");
Check(mixed != null && mixed.ContainsKey("AccentColor"),
    "an unrelated setting alongside a bad page still parses (the file is not discarded wholesale)");

if (failures.Count > 0)
{
    Console.WriteLine($"{failures.Count} check(s) failed.");
    return 1;
}

Console.WriteLine("All checks passed.");
return 0;