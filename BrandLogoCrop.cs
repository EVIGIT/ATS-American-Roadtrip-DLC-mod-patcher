namespace ATSRoadTripConverter;

/// <summary>
/// Trims the empty margin off a dealer badge and re-pads it to the shape the car shop expects, so a
/// converted logo draws at the same size and proportions as the base game's own badges.
/// <para>
/// The target is measured, not guessed. Read out of <c>base.scs</c>'s
/// <c>/material/ui/car_brand_logo/</c>, whose textures are GDeflate-packed with the DDS header stripped
/// and had to be decoded before they could be read at all:
/// <code>
/// | Badge | Canvas | Aspect | Artwork | Fill |
/// | Dodge | 175x89 | 1.97:1 | 175x25 | 100% |
/// | Ford  | 175x89 | 1.97:1 | 175x53 | 100% |
/// | RAM   | 175x89 | 1.97:1 | 175x41 | 100% |
/// </code>
/// Every stock badge is FULL BLEED - the artwork fills the canvas edge to edge, because the canvas is
/// cut to the artwork. A converted badge is therefore cropped to its own opaque bounds and given a
/// canvas of the stock 1.97:1 shape.
/// </para>
/// <para>
/// Two earlier versions targeted the wrong thing and had to be undone: one matched the artwork's aspect
/// ratio and ignored size, the next invented a 60.9% "fill" figure measured from an already-CONVERTED
/// badge rather than a stock one - which described the bug instead of the correct state. Both are worth
/// remembering, because the symptom is identical either way and only reading the stock geometry settles
/// which is which.
/// </para>
/// <para>
/// This pass is <b>lossless</b>: it copies existing colour blocks rather than re-encoding them, so the
/// artwork stays provably byte-identical to what the mod author shipped. It changes the canvas, never
/// the artwork's own proportions. Correcting artwork that is itself pre-squashed - such as the BMW
/// roundel at 2.59:1 - would need <b>resampling</b>, which re-encodes colour and so forfeits that
/// guarantee; that belongs in a separate opt-in pass rather than being done silently here.
/// </para>
/// <para>
/// Deliberately dependency-free, like <see cref="BrandLogoAlpha"/> and <see cref="MainLayout"/>,
/// so the geometry can be asserted headlessly. Only the DDS rewrite goes through the codec.
/// </para>
/// <para>
/// The <c>.mat</c> is <b>not</b> touched. <c>base.scs</c>'s truck dealer badges declare
/// <c>aux[0] : { 175, 89, 2, 0 }</c> under a <c>ui.sdf.rfx</c> SDF effect, and it was tempting to
/// mirror that. It would be wrong: the car shop's own badges declare no <c>aux[0]</c> at all, and
/// writing an SDF effect onto a plain textured quad would very likely render nothing.
/// </para>
/// </summary>
public static class BrandLogoCrop
{
    /// <summary>
    /// The car shop's slot, measured from the base game's own badges.
    /// <para>
    /// Every stock badge in <c>base.scs</c>'s <c>/material/ui/car_brand_logo/</c> is 175x89 - Dodge,
    /// Ford and RAM alike - which is 1.97:1. This was read out of the archive's GDeflate-packed
    /// textures, not inferred, so unlike the earlier guesses this number is ground truth.
    /// </para>
    /// </summary>
    public const double TargetCanvasAspect = 175.0 / 89.0;

    /// <summary>
    /// The slot size itself. Stock badges are authored at exactly this resolution.
    /// <para>
    /// A converted badge is not resized UP to 175x89 - it is only as large as its own artwork needs,
    /// because the game scales the texture to the slot regardless of its pixel size. Matching the
    /// resolution would inflate a small logo without making it any more legible, and would cost
    /// texture memory for nothing. Only the <em>aspect</em> is borrowed from here.
    /// </para>
    /// </summary>
    public const int TargetSlotWidth = 175;

    public const int TargetSlotHeight = 89;

    /// <summary>
    /// How far a badge may already sit from the target before it is left alone, as a fraction.
    /// <para>
    /// Generous because the value is quantised: the crop snaps to 4-pixel blocks, so a badge already
    /// within a couple of percent of 1.97:1 cannot be reproduced exactly and re-encoding it would
    /// change bytes for no visible gain - and would cost the lossless guarantee for nothing.
    /// </para>
    /// </summary>
    public const double AspectTolerance = 0.04;

    /// <summary>
    /// How far the artwork may already fill its canvas before it is left alone, as a fraction of the
    /// width. Stock badges are full-bleed, so this is close to zero in practice; the small band exists
    /// only to absorb block snapping.
    /// </summary>
    public const double FillTolerance = 0.04;

    /// <summary>
    /// Minimum opaque pixels required before a crop is attempted.
    /// <para>
    /// Guards the degenerate case. If decoding produced a near-empty alpha channel the bounds would
    /// collapse to a point or a sliver, and padding that out to the target fill would manufacture a
    /// large image out of nothing. A real logo clears this comfortably.
    /// </para>
    /// </summary>
    public const int MinimumOpaquePixels = 64;

    /// <summary>
    /// The opaque bounding box of a decoded badge, or false when there is too little to work with.
    /// <para>
    /// Alpha is read with a low threshold so a barely-visible fringe still counts as artwork. The
    /// point is to find where the logo <em>ends</em>, and trimming a row of near-transparent pixels
    /// off the edge is harmless, whereas trimming a row of real artwork is the failure this whole
    /// pass exists to avoid.
    /// </para>
    /// </summary>
    public static bool TryFindOpaqueBounds(
        byte[] rgba, int width, int height, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = width; minY = height; maxX = -1; maxY = -1;
        var opaque = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (rgba[(y * width + x) * 4 + 3] <= 8)
                    continue;

                opaque++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (opaque < MinimumOpaquePixels || maxX < minX || maxY < minY)
            return false;

        return true;
    }

    /// <summary>
    /// Where the artwork sits, and where it should end up. All values in pixels.
    /// </summary>
    public readonly record struct Plan(
        int SourceX, int SourceY,
        int SourceWidth, int SourceHeight,
        int OutputWidth, int OutputHeight,
        int OffsetX, int OffsetY);

    /// <summary>
    /// Works out the crop and pad for one badge, or returns false when it should be left alone.
    /// <para>
    /// The target is the stock geometry measured from <c>base.scs</c>: a canvas of
    /// <see cref="TargetCanvasAspect"/> with the artwork filling it. Both properties must already hold
    /// for a badge to be left alone - checking only one previously let a badge through with a badly
    /// wrong canvas shape.
    /// </para>
    /// <para>
    /// The canvas is sized to the artwork, not inflated to the slot's 175x89: the game scales whatever
    /// texture it is given into the slot, so matching the SHAPE is what matters and matching the
    /// resolution would only inflate the file.
    /// </para>
    /// <para>
    /// Everything is snapped to a 4-pixel block boundary in both directions. That is not cosmetic:
    /// the colour blocks are copied rather than re-encoded, which is what keeps the artwork
    /// provably identical, and a block can only be copied whole. Snapping outward guarantees the
    /// copy keeps every opaque pixel.
    /// </para>
    /// </summary>
    public static bool TryPlan(
        int width, int height, int minX, int minY, int maxX, int maxY, out Plan plan)
    {
        plan = default;

        var left = Math.Max(0, SnapDown(minX));
        var top = Math.Max(0, SnapDown(minY));
        var right = Math.Min(width, SnapUp(maxX + 1));
        var bottom = Math.Min(height, SnapUp(maxY + 1));

        var cropWidth = right - left;
        var cropHeight = bottom - top;
        if (cropWidth < 4 || cropHeight < 4)
            return false;

        var currentFill = (double)cropWidth / width;
        var currentAspect = (double)width / height;
        var fillOk = Math.Abs(currentFill - 1.0) <= FillTolerance;
        var aspectOk = Math.Abs(currentAspect - TargetCanvasAspect) / TargetCanvasAspect <= AspectTolerance;

        // Already correct on both counts: leave the bytes alone rather than re-encoding them for no
        // visible gain, which would also cost the lossless guarantee for nothing.
        if (fillOk && aspectOk)
            return false;

        // FULL BLEED at the stock aspect. The measured stock badges are all 175x89 with the artwork
        // filling the canvas edge to edge - Dodge's artwork is 175x25 on a 175x89 canvas, RAM's is
        // 175x41. There is no empty margin to preserve, because there is no margin in the real thing.
        // <para>
        // The canvas is as large as the artwork needs and no larger, so a converted badge is not
        // inflated to 175x89: the game scales whatever texture it is given into the slot, so only the
        // SHAPE has to match, not the resolution.
        // </para>
        // FULL BLEED at the stock aspect. Measured stock badges are all 175x89 with the artwork spanning the
        // FULL WIDTH: Dodge's artwork is 175x25 on a 175x89 canvas, RAM's is 175x41. So for a logo WIDER
        // than the slot aspect - which all three stock badges are, at 7.00:1, 4.27:1 and 3.30:1 - the
        // canvas is cut to the artwork's width and padded vertically to reach 1.97:1. The horizontal margin
        // is exactly what made converted logos draw small.
        // <para>
        // A logo TALLER than the slot aspect cannot be padded into it: a 64x48 Volvo badge would need a
        // 64x32 canvas to hit 1.97:1, which cannot hold 48 rows of artwork. The alternatives are all worse
        // - cropping the logo, or squashing it - and artwork is never distorted here, so in that case the
        // canvas simply takes the artwork's own aspect and the badge keeps its shape. None of the stock
        // badges are in this case, which is why it only shows up on converted mods.
        // </para>
        var outputWidth = SnapUp(cropWidth);
        var artAspect = cropWidth / (double)cropHeight;

        if (artAspect >= TargetCanvasAspect)
        {
            // Wider than the slot: pad vertically to the target aspect. Both dimensions must be multiples
            // of 4 because a colour block can only be copied whole, and 1.97:1 does not generally survive
            // that - a 128-wide canvas wants a height of 65.1. Search outward from that height for the
            // multiple of 4 closest to the target.
            var idealHeight = (int)Math.Round(outputWidth / TargetCanvasAspect);
            var outputHeight = 0;
            var bestError = double.MaxValue;

            for (var delta = 0; delta <= 8; delta++)
            {
                foreach (var candidate in delta == 0 ? new[] { idealHeight } : new[] { idealHeight - delta, idealHeight + delta })
                {
                    if (candidate < 4) continue;

                    var h = SnapUp(candidate);
                    var error = Math.Abs(outputWidth / (double)h - TargetCanvasAspect) / TargetCanvasAspect;

                    // Prefer the smaller canvas on a tie: a larger one costs texture memory and gains
                    // nothing visible, because the game scales the texture to the slot regardless.
                    if (error < bestError - 1e-9
                        || (Math.Abs(error - bestError) <= 1e-9 && outputHeight != 0 && h < outputHeight))
                    {
                        bestError = error;
                        outputHeight = h;
                    }
                }

                // A height this close cannot be meaningfully improved on by a wider delta.
                if (bestError <= 0.005) break;
            }

            if (outputHeight == 0) return false;
            return Complete(ref plan, left, top, cropWidth, cropHeight, outputWidth, outputHeight,
                width, height, width * 2, height * 2);
        }

        // Taller than the slot: the canvas takes the artwork's own aspect, padded to whole blocks. No
        // padding is added at all - the logo already fills it - which is what makes this case lossless and
        // also why the aspect cannot be forced here without distorting the artwork.
        var tallHeight = SnapUp(cropHeight);
        return Complete(ref plan, left, top, cropWidth, cropHeight, outputWidth, tallHeight,
            width, height, width * 2, height * 2);
    }

    /// <summary>
    /// Validates a candidate canvas and fills in the plan, or reports that the badge should be left alone.
    /// <para>
    /// Split out of <see cref="TryPlan"/> because the two branches above reach the same guards by
    /// different routes, and duplicating those guards is how the two paths silently drifted apart once
    /// already.
    /// </para>
    /// </summary>
    private static bool Complete(
        ref Plan plan,
        int left, int top, int cropWidth, int cropHeight,
        int outputWidth, int outputHeight,
        int width, int height, int maxWidth, int maxHeight)
    {
        // Never grow past twice the source. A badge drawn as a thin sliver would otherwise ask for an
        // enormous canvas, and a texture the game must scale down that far is likelier to show mip
        // artefacts than to look right.
        outputWidth = Math.Min(outputWidth, Math.Max(cropWidth, maxWidth));
        outputHeight = Math.Min(outputHeight, Math.Max(cropHeight, maxHeight));

        // The artwork must fit. If the cap clipped the canvas below the logo, there is nothing safe to do.
        if (outputWidth < cropWidth || outputHeight < cropHeight)
            return false;

        // Nothing to do when the crop already is the whole image at the right shape.
        if (left == 0 && top == 0 && cropWidth == width && cropHeight == height
            && outputWidth == width && outputHeight == height)
            return false;

        plan = new Plan(
            left,
            top,
            cropWidth,
            cropHeight,
            outputWidth,
            outputHeight,
            SnapDown((outputWidth - cropWidth) / 2),
            SnapDown((outputHeight - cropHeight) / 2));

        return true;
    }

    private static int SnapDown(int value) => value - value % 4;

    private static int SnapUp(int value) => ((value + 3) / 4) * 4;
/// <summary>
    /// Copies the planned region into a new RGBA image of the planned output size, leaving the
    /// padding fully transparent.
    /// </summary>
    internal static byte[] Extract(Plan plan, byte[] rgba, int width, int height)
    {
        var result = new byte[plan.OutputWidth * plan.OutputHeight * 4];

        for (var y = 0; y < plan.SourceHeight; y++)
        {
            for (var x = 0; x < plan.SourceWidth; x++)
            {
                var source = ((plan.SourceY + y) * width + (plan.SourceX + x)) * 4;
                var destination = ((plan.OffsetY + y) * plan.OutputWidth + (plan.OffsetX + x)) * 4;

                result[destination] = rgba[source];
                result[destination + 1] = rgba[source + 1];
                result[destination + 2] = rgba[source + 2];
                result[destination + 3] = rgba[source + 3];
            }
        }

        return result;
    }

    /// <summary>
    /// Crops and re-pads one badge texture in place, or leaves it untouched.
    /// <para>
    /// Returns false with the reason in <paramref name="detail"/> for anything that should not be
    /// rewritten: an undecodable file, too little artwork, or artwork already at the right ratio.
    /// </para>
    /// </summary>
    public static bool TryCrop(string ddsPath, out string detail)
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
            detail = "uses " + format + ", which is not a format this build can re-encode; left unchanged";
            return false;
        }

        if (!TryFindOpaqueBounds(rgba, width, height, out var minX, out var minY, out var maxX, out var maxY))
        {
            detail = "has too little visible artwork to measure a crop from; left unchanged";
            return false;
        }

        if (!TryPlan(width, height, minX, minY, maxX, maxY, out var plan))
        {
            // Deliberately measured from the bounds rather than from `plan`: TryPlan returns false
            // with `plan` left at its default, so reading plan here once reported a fill of 0% for
            // every untouched badge, including Ford's.
            var measuredFill = 100.0 * (maxX - minX + 1) / width;
            detail = "artwork already fills " + measuredFill.ToString("F0") +
                     "% of a " + ((double)width / height).ToString("F2") +
                     ":1 canvas, both already matching the stock " +
                     TargetCanvasAspect.ToString("F2") + ":1 full-bleed badge; left unchanged";
            return false;
        }

        var cropped = Extract(plan, rgba, width, height);

        try
        {
            File.WriteAllBytes(ddsPath, BrandLogoAlpha.BuildDxt5Cropped(
                file, width, height, BrandLogoAlpha.ReadMipCount(file), cropped, plan));
        }
        catch (Exception ex)
        {
            detail = "could not be written back (" + ex.Message + ")";
            return false;
        }

        detail = $"artwork cropped from {width}x{height} to {plan.SourceWidth}x{plan.SourceHeight} and " +
                 $"re-padded to {plan.OutputWidth}x{plan.OutputHeight}, a full-bleed " +
                 ((double)plan.OutputWidth / plan.OutputHeight).ToString("F2") +
                 ":1 canvas - the shape every stock badge uses";
        return true;
    }
}