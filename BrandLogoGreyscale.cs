namespace ATSRoadTripConverter;

/// <summary>
/// Opt-in pass that desaturates a dealer badge, so a converted logo reads like the base game's own.
/// <para>
/// The stock badges are monochrome: measured straight out of <c>base.scs</c>, Dodge, Ford and RAM all
/// render as grey or white. A converted badge keeps its brand colours - BMW's blue and white, Volvo's
/// blue - which makes it read as a different kind of object rather than the same kind of thing in a
/// different colour. This is what closes that gap.
/// </para>
/// <para>
/// <b>It re-encodes colour and so forfeits the byte-for-byte guarantee</b>, which is why it is opt-in and
/// defaulted off. A mod author's brand colours are legitimate artwork, and some users will want to keep
/// them, so this is offered rather than applied.
/// </para>
/// <para>
/// Alpha is copied through untouched. That is what makes this pass safe to run at any point in the
/// pipeline: it cannot disturb the silhouette, cannot crop artwork and cannot reintroduce the opaque
/// background the transparency pass removed, because it never touches the channel that decides what is
/// visible. Only the three colour channels change, and they change to a value already present in the
/// pixel.
/// </para>
/// </summary>
public static class BrandLogoGreyscale
{
    /// <summary>
    /// How far a badge's colour must be from grey before it is worth re-encoding.
    /// <para>
    /// A badge that is already effectively grey - most stock logos, and any mod logo that shipped in
    /// greyscale - is left byte-identical rather than being re-encoded for no visible change. The threshold
    /// is on average saturation, because a logo with one small coloured accent still reads as coloured.
    /// </para>
    /// </summary>
    public const double SaturationTolerance = 0.02;

    /// <summary>
    /// Desaturates one badge texture in place, or leaves it alone.
    /// <para>
    /// Returns false with the reason in <paramref name="detail"/> for anything not worth touching: an
    /// unreadable file, too little artwork, artwork that is already grey, or a format this build cannot
    /// re-encode.
    /// </para>
    /// </summary>
    public static bool TryGreyscale(string ddsPath, out string detail)
    {
        detail = "";

        byte[] file;
        try
        {
            file = File.ReadAllBytes(ddsPath);
        }
        catch (Exception ex)
        {
            detail = "could not be read (" + ex.Message + ")";
            return false;
        }

        if (!BrandLogoAlpha.TryDecode(file, out var width, out var height, out var rgba, out var format))
        {
            detail = "uses " + format + ", which this build cannot re-encode; left unchanged";
            return false;
        }

        if (!BrandLogoCrop.TryFindOpaqueBounds(rgba, width, height, out _, out _, out _, out _))
        {
            detail = "has too little visible artwork to measure; left unchanged";
            return false;
        }

        // Measured over the artwork only. The transparent margin is excluded because its colour is
        // whatever was behind the logo before the knock-out, and averaging that in would make a fully
        // saturated badge look partly grey and a fully grey one look partly coloured.
        var saturationSum = 0.0;
        var counted = 0;

        for (var i = 0; i < width * height; i++)
        {
            if (rgba[i * 4 + 3] <= 8) continue;

            var max = Math.Max(rgba[i * 4], Math.Max(rgba[i * 4 + 1], rgba[i * 4 + 2]));
            var min = Math.Min(rgba[i * 4], Math.Min(rgba[i * 4 + 1], rgba[i * 4 + 2]));

            saturationSum += max == 0 ? 0 : (max - min) / (double)max;
            counted++;
        }

        if (counted == 0)
        {
            detail = "has no visible pixels; left unchanged";
            return false;
        }

        var saturation = saturationSum / counted;

        if (saturation <= SaturationTolerance)
        {
            detail = "artwork is already effectively greyscale (" +
                     (saturation * 100).ToString("F1") + "% saturation); left unchanged";
            return false;
        }

        var grey = BrandLogoAlpha.Greyscale(rgba);

        byte[] rebuilt;
        try
        {
            rebuilt = BrandLogoAlpha.BuildDxt5Lossy(
                width, height, BrandLogoAlpha.ReadMipCount(file), grey);
            File.WriteAllBytes(ddsPath, rebuilt);
        }
        catch (Exception ex)
        {
            detail = "could not be written back (" + ex.Message + ")";
            return false;
        }

        detail = $"artwork desaturated ({saturation * 100:F0}% saturation -> 0%), colour re-encoded, so " +
                 "this badge is no longer byte-identical to what the mod author shipped";
        return true;
    }
}