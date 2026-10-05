namespace TruckersToolKit;

/// <summary>
/// Opt-in correction for artwork that is itself pre-squashed, so that a roundel drawn at 2.59:1 stops
/// reading as a flat ellipse.
/// <para>
/// <b>This pass is lossy and says so.</b> It is the only part of the badge pipeline that re-encodes
/// colour, and running it forfeits the byte-for-byte guarantee the transparency and crop passes
/// provide. That is why it is opt-in and defaulted off rather than part of the normal conversion.
/// </para>
/// <para>
/// WHY resampling cannot be lossless, which is why it is separate rather than folded into
/// <see cref="BrandLogoCrop"/>: a BC1/BCn colour block is self-contained - two endpoint colours plus
/// two-bit indices, all local to one 4x4 block. That is exactly why a crop can relocate whole blocks
/// and preserve the artwork byte for byte. A resample is not a relocation: it places source pixels into
/// <em>different</em> blocks, so the destination block's endpoints must be re-derived from whatever
/// pixels now land in it, and the author's endpoints described a different set of pixels and cannot be
/// reused. Measured on the real BMW artwork, 97 of 98 output rows read a different source row and 12
/// span more than one source block row, so there is no subset of blocks that can simply be copied.
/// </para>
/// <para>
/// The damage is not hypothetical either: re-fitting BC1 endpoints from decoded pixels was already
/// measured on these same logos at up to 251 error per channel, with one mod's red returning as green.
/// A resample has no way around that path.
/// </para>
/// </summary>
public static class BrandLogoResample
{
    /// <summary>
    /// How far the artwork may sit from the target shape before a resample is attempted, as a fraction.
    /// <para>
    /// Wide on purpose. Artwork already close to the target is left byte-identical, because resampling
    /// something that does not need it spends the guarantee for no visible gain.
    /// </para>
    /// </summary>
    public const double AspectTolerance = 0.15;

    /// <summary>
    /// The shape a clearly-squashed logo is resampled to.
    /// <para>
    /// Near enough square that a roundel reads as a roundel again, with the tolerance above absorbing
    /// the fact that no logo is perfectly square.
    /// </para>
    /// </summary>
    public const double TargetArtworkAspect = 1.0;
    /// <summary>
    /// Resamples one badge's artwork in place toward <see cref="TargetArtworkAspect"/>, or leaves it be.
    /// <para>
    /// Returns false with the reason in <paramref name="detail"/> for anything not worth touching: an
    /// unreadable file, too little artwork, artwork already close enough to the target, or a format this
    /// build cannot re-encode.
    /// </para>
    /// </summary>
    public static bool TryUnsquash(string ddsPath, out string detail)
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

        if (!BrandLogoCrop.TryFindOpaqueBounds(rgba, width, height,
                out var minX, out var minY, out var maxX, out var maxY))
        {
            detail = "has too little visible artwork to measure; left unchanged";
            return false;
        }

        var artWidth = maxX - minX + 1;
        var artHeight = maxY - minY + 1;
        var current = artWidth / (double)artHeight;

        if (Math.Abs(current - TargetArtworkAspect) / TargetArtworkAspect <= AspectTolerance)
        {
            detail = "artwork is already about " + current.ToString("F2") +
                     ":1, close enough to a roundel; left unchanged";
            return false;
        }

        // Grow the shorter axis to reach the target aspect, keeping the artwork full-bleed on the longer
        // one. Growing rather than shrinking avoids discarding artwork the mod author drew.
        //
        // ROUNDED UP TO A WHOLE NUMBER OF 4x4 BLOCKS. This is not cosmetic. The BMW artwork measures
        // 127x49, and 127 is not a multiple of 4, so the resample emitted a 127x127 canvas: the
        // encoder has to pad the final partial block, every mip level is then computed from a width
        // that is not the width actually stored, and the chain 127 -> 63 -> 31 -> 15 -> 7 -> 3 is
        // ambiguous between a floor and a ceil halving. That produced a DDS whose declared geometry
        // and byte layout were only self-consistent by coincidence, and loading it is what crashed the
        // game. Rounding up keeps the engine's own arithmetic valid at every level.
        var newWidth = RoundUpToBlock(artWidth);
        var newHeight = RoundUpToBlock(artHeight);
        if (current > TargetArtworkAspect)
            newHeight = RoundUpToBlock((int)Math.Ceiling(artWidth / TargetArtworkAspect));
        else
            newWidth = RoundUpToBlock((int)Math.Ceiling(artHeight * TargetArtworkAspect));

        var resampled = Resample(rgba, width, minX, minY, artWidth, artHeight, newWidth, newHeight);

        byte[] rebuilt;
        try
        {
            rebuilt = BrandLogoAlpha.BuildDxt5Lossy(
                newWidth, newHeight, BrandLogoAlpha.ReadMipCount(file), resampled);
            File.WriteAllBytes(ddsPath, rebuilt);
        }
        catch (Exception ex)
        {
            detail = "could not be written back (" + ex.Message + ")";
            return false;
        }

        detail = $"artwork resampled from {artWidth}x{artHeight} ({current:F2}:1) to " +
                 $"{newWidth}x{newHeight} ({newWidth / (double)newHeight:F2}:1); colour re-encoded, " +
                 "so this badge is no longer byte-identical to what the mod author shipped";
        return true;
    }

    /// <summary>
    /// Rounds a dimension up to a whole number of 4x4 blocks, which is the unit DXT5 stores in.
    /// <para>
    /// A dimension that is already aligned is returned unchanged, so this is a no-op for the common
    /// case and only ever grows the canvas by at most 3 pixels.
    /// </para>
    /// </summary>
    internal static int RoundUpToBlock(int value) => (value + 3) / 4 * 4;

    /// <summary>
    /// Nearest-neighbour resample of the artwork region into a new RGBA buffer.
    /// <para>
    /// Nearest neighbour rather than a smoothing filter because smoothing would blur the lettering and
    /// the roundel edge, and because a resample has already forfeited exactness - the least-blurred of
    /// two lossy options is the kinder trade. Alpha is carried the same way as colour so the silhouette
    /// does not gain a halo of half-transparent pixels at the edge.
    /// </para>
    /// </summary>
    internal static byte[] Resample(
        byte[] rgba, int stride, int originX, int originY,
        int artWidth, int artHeight, int newWidth, int newHeight)
    {
        var result = new byte[newWidth * newHeight * 4];

        for (var y = 0; y < newHeight; y++)
        {
            // Centre-sampled, so the artwork stays centred rather than drifting toward one edge.
            var sourceY = Math.Clamp(
                originY + (int)(((y + 0.5) * artHeight / newHeight) - 0.5),
                originY, originY + artHeight - 1);

            for (var x = 0; x < newWidth; x++)
            {
                var sourceX = Math.Clamp(
                    originX + (int)(((x + 0.5) * artWidth / newWidth) - 0.5),
                    originX, originX + artWidth - 1);

                var source = (sourceY * stride + sourceX) * 4;
                var destination = (y * newWidth + x) * 4;

                result[destination] = rgba[source];
                result[destination + 1] = rgba[source + 1];
                result[destination + 2] = rgba[source + 2];
                result[destination + 3] = rgba[source + 3];
            }
        }

        return result;
    }
}