namespace TruckersToolKit;

/// <summary>One changelog section: a version heading with the notes under it.</summary>
public sealed record ReleaseSection(string Version, string Heading, IReadOnlyList<string> Bullets);

/// <summary>
/// Parses the embedded <c>CHANGELOG.md</c> so the launch page can show what changed since the
/// version the user last ran.
/// <para>
/// Deliberately free of any dependency, for the same reason as <see cref="SettingsSchema"/>:
/// this is pure text handling, and keeping it dependency-free lets a headless test cover the
/// fiddly parts (version ordering, missing headings, malformed lines) without WinForms.
/// </para>
/// </summary>
public static class ReleaseNotes
{
    /// <summary>Headings that are structural rather than a released version.</summary>
    private static readonly string[] NonReleaseHeadings = { "unreleased" };

    /// <summary>
    /// Returns the released sections newer than <paramref name="sinceVersion"/>, newest first.
    /// <para>
    /// An empty or unreadable <paramref name="sinceVersion"/> returns nothing, so a first run
    /// shows the quick-start instead of the entire history. A version that cannot be parsed is
    /// treated as "no previous version" for the same reason: showing one release is a far
    /// smaller problem than hiding every change from a user who upgrades across several.
    /// </para>
    /// <para>
    /// <paramref name="currentVersion"/> caps the result. The changelog contains at least one
    /// historical heading that is mis-numbered ("v12.1"), which parses as a version far newer
    /// than anything real. Without a cap, that section would be reported as newer than the
    /// running build and shown on every single launch. A "what's new" page can never honestly
    /// contain a release newer than the app displaying it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ReleaseSection> SectionsNewerThan(
        string? markdown, string? sinceVersion, string? currentVersion = null)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return Array.Empty<ReleaseSection>();

        // Without a comparable baseline we cannot say what is new. This is the first-run case.
        if (!TryParseVersion(sinceVersion, out var baseline))
            return Array.Empty<ReleaseSection>();

        // An unparseable cap is ignored rather than treated as "hide everything".
        var hasCap = TryParseVersion(currentVersion, out var cap);

        return ParseSections(markdown)
            .Where(section => TryParseVersion(section.Version, out var version) && version > baseline)
            .Where(section => !hasCap
                || (TryParseVersion(section.Version, out var version) && version <= cap))
            .OrderByDescending(section => ParseVersionOrZero(section.Version))
            .ToList();
    }

    /// <summary>
    /// Every released section in the document, in document order. Unreleased and any other
    /// non-version heading is skipped, as are sections whose heading is not a version.
    /// </summary>
    public static IReadOnlyList<ReleaseSection> ParseSections(string markdown)
    {
        var sections = new List<ReleaseSection>();
        if (string.IsNullOrWhiteSpace(markdown))
            return sections;

        ReleaseSection? current = null;
        var bullets = new List<string>();

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (current != null)
                {
                    current = current with { Bullets = bullets.ToList() };
                    sections.Add(current);
                    bullets = new List<string>();
                }

                var heading = line[3..].Trim();
                // A heading that is not a version (Unreleased, or prose) is not a release.
                current = TryParseVersion(heading, out _)
                    ? new ReleaseSection(heading, heading, bullets)
                    : null;
                continue;
            }

            if (current == null)
                continue;

            // A "### Added"-style subheading is structural only. It must not reset the bullet
            // list, or the notes under the first subheading are overwritten by the next one.
            if (line.StartsWith("### ", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var text = line[2..].Trim();
                if (text.Length > 0)
                    bullets.Add(text);
            }
        }

        if (current != null)
            sections.Add(current with { Bullets = bullets.ToList() });

        return sections;
    }

    /// <summary>True when the heading names a version rather than a structural heading.</summary>
    public static bool IsReleaseHeading(string? heading)
    {
        if (string.IsNullOrWhiteSpace(heading))
            return false;
        return !NonReleaseHeadings.Contains(heading.Trim(), StringComparer.OrdinalIgnoreCase)
            && TryParseVersion(heading, out _);
    }

    /// <summary>
    /// Parses a version heading such as "v1.3.9" or "1.3.9". A fourth numeric component is
    /// kept so a two-part patch like "v1.3.7.2" sorts above "v1.3.7" rather than tying with it,
    /// which is how the changelog has actually numbered releases.
    /// </summary>
    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[1..];

        // Tolerate a trailing "-suffix" without letting it defeat the parse.
        var suffix = trimmed.IndexOfAny(new[] { '-', '+', ' ' });
        if (suffix >= 0)
            trimmed = trimmed[..suffix];

        return Version.TryParse(trimmed, out version!);
    }

    private static Version ParseVersionOrZero(string value) =>
        TryParseVersion(value, out var version) ? version : new Version(0, 0);
}