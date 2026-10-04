namespace ATSRoadTripConverter;

/// <summary>
/// The main window's vertical budget, in one place.
/// <para>
/// The main window stacks a fixed-height header, two fixed-height cards, a fixed-height action
/// area, and then a log card that absorbs whatever height is left over. The arithmetic that ties
/// those together was previously spread across magic numbers in the layout code and a comment,
/// which is how the default height and the minimum log height drifted apart and produced a
/// scrollbar on a normally sized desktop.
/// </para>
/// <para>
/// Deliberately dependency-free, like <see cref="SettingsSchema"/> and <see cref="ReleaseNotes"/>,
/// so the budget can be asserted in a headless test instead of being re-derived by eye.
/// </para>
/// </summary>
public static class MainLayout
{
    /// <summary>Gap between stacked cards.</summary>
    public const int CardGap = 16;

    public const int HeaderHeight = 100;
    public const int FilesCardHeight = 222;
    public const int OptionsCardHeight = 416;
    public const int ActionAreaHeight = 86;

    /// <summary>The log may shrink to this, but never past it.</summary>
    public const int MinimumLogCardHeight = 170;

    /// <summary>The log height the default window size is chosen to give it.</summary>
    public const int PreferredLogCardHeight = 200;

    /// <summary>
    /// Distance from the top of the client area to the top of the log card: everything above it.
    /// </summary>
    public const int ContentAboveLog =
        HeaderHeight + FilesCardHeight + CardGap + OptionsCardHeight + CardGap + ActionAreaHeight;

    /// <summary>
    /// Smallest client height that still shows the log at its minimum. Below this the page has to
    /// scroll, which is the correct answer on a genuinely short screen.
    /// </summary>
    public const int MinimumContentHeight = ContentAboveLog + MainLayoutMargin + MinimumLogCardHeight;

    /// <summary>
    /// The height the window opens at. Chosen as the minimum plus the difference between the
    /// preferred and minimum log heights, so a normal desktop gets a comfortable log with no
    /// scrollbar rather than a window sized exactly to its own floor.
    /// </summary>
    public const int DefaultWindowHeight =
        ContentAboveLog + MainLayoutMargin + PreferredLogCardHeight;

    /// <summary>
    /// Additional chrome, in pixels, that the content above the log has to make room for. A tab
    /// strip is the reason this exists: adding one must increase the window budget by exactly the
    /// same amount, or the log is silently squeezed and the scrollbar comes back.
    /// </summary>
    public const int MainLayoutMargin = 24;

    /// <summary>
    /// Height the log card should take for a given client height, honouring the minimum.
    /// A negative result means the window is shorter than the content above the log, in which
    /// case the caller keeps the minimum and lets the form scroll.
    /// </summary>
    public static int LogCardHeight(int clientHeight, int logCardTop, int extraChrome = 0)
    {
        var available = clientHeight - logCardTop - extraChrome - MainLayoutMargin;
        return Math.Max(MinimumLogCardHeight, available);
    }

    /// <summary>
    /// The height a window needs so that <paramref name="extraChrome"/> of additional chrome
    /// (a tab strip, for example) still leaves the log at its preferred size.
    /// </summary>
    public static int WindowHeightFor(int extraChrome) =>
        DefaultWindowHeight + extraChrome;

    /// <summary>Smallest client height that avoids scrolling, given the same extra chrome.</summary>
    public static int MinimumHeightFor(int extraChrome) =>
        MinimumContentHeight + extraChrome;
}