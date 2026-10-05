namespace TruckersToolKit;

/// <summary>
/// Rewrites a dealer logo texture so its background is transparent, which is what makes a
/// converted car's dealership badge look like the base game's own badges instead of sitting
/// inside a black letterbox.
/// <para>
/// The difference is entirely in the texture file, and it was measured rather than guessed:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A logo that shows a black box ships <b>DXT1</b> (BC1), a format with no alpha channel at
/// all, with the black background baked into the pixels. From the two mods in the bug report:
/// <c>volvo_cars.dds</c> is 128x64 DXT1 and 78.0% of its pixels are pure black;
/// <c>bmw.dds</c> is 256x64 DXT1 and 69.9% black. All four corners are (0,0,0), so the game
/// draws an opaque black plate and then the artwork on top of it.
/// </description></item>
/// <item><description>
/// A logo that renders correctly is <b>DXT5</b> (BC3), which carries a real alpha channel.
/// Verified against <c>Ford Focus Mk3 1.61_roadtrip.scs</c>, a working Road Trip car: its
/// <c>brand_logo/ford.dds</c> is 128x64 DXT5 and 73.9% of its pixels are fully transparent.
/// That single difference is the whole of the black box.
/// </description></item>
/// </list>
/// <para>
/// So the fix is to decode the texture, knock the background out to alpha 0, and re-encode as
/// DXT5. Nothing else has to move. <c>.mat</c> is plain text naming its <c>.tobj</c> by bare
/// file name, and the <c>.tobj</c> holds an absolute path to the <c>.dds</c> plus sampler state.
/// That was checked, not assumed: across 2205 real <c>.tobj</c>/<c>.dds</c> pairs in the mod
/// folder, DXT1 and DXT5 textures appear with identical <c>.tobj</c> field values, so the
/// compiled object does not encode the compression format and never needs rewriting here.
/// Overwriting the <c>.dds</c> in place leaves every link in the chain resolving.
/// </para>
/// <para>
/// Deliberately dependency-free, like <see cref="MainLayout"/> and <see cref="SettingsSchema"/>,
/// so the codec can be covered by the headless verify suite rather than only by looking at it.
/// </para>
/// </summary>
public static class BrandLogoAlpha
{
    /// <summary>Bytes in a DDS header. The pixel payload starts immediately after it.</summary>
    private const int HeaderSize = 128;

    /// <summary>
    /// Luminance at or below which a pixel counts as background for the flood fill. Low on
    /// purpose: the fill only reaches pixels connected to the image border, so a generous
    /// threshold still cannot eat into the artwork, and a tight one would leave a speckled
    /// fringe where the mod author's own anti-aliasing already darkened the background.
    /// </summary>
    private const int BackgroundLumaCutoff = 40;

    /// <summary>
    /// Luminance at which an edge pixel is considered fully opaque again. Pixels in the band
    /// between the two cutoffs fade, which is what removes the dark halo a hard key leaves
    /// around anti-aliased lettering.
    /// </summary>
    private const int EdgeLumaCutoff = 96;
    /// <summary>
    /// Rewrites <paramref name="ddsPath"/> in place as a DXT5 image with a transparent
    /// background.
    /// <para>
    /// Returns false, with the reason in <paramref name="detail"/>, for every case where the
    /// original should be left exactly as it is: an unreadable or already-transparent file, a
    /// compression this decoder does not handle, or a frame whose background is not a dark
    /// border-connected field. Being conservative matters more than being thorough here -
    /// a logo that already looks right must come through byte-identical.
    /// </para>
    /// </summary>
    public static bool TryMakeTransparent(string ddsPath, out string detail)
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

        if (!TryDecode(file, out var width, out var height, out var rgba, out var format))
        {
            detail = "uses " + format + ", which is not a format this build can re-encode; left unchanged";
            return false;
        }

        // A logo that already carries transparency is already doing this job, and re-encoding it
        // would mean resampling artwork that renders correctly today. DXT5 is the format the
        // base game's own badges use, so its presence is the signal, not the pixel count alone.
        if (format == "DXT5" || format == "DXT3")
        {
            detail = "already carries an alpha channel; left unchanged";
            return false;
        }

        KnockOutBackground(rgba, width, height, out var cleared);

        // A frame that is nearly all background, or that has almost none, means the flood fill
        // found something other than a background field. Guessing in either direction would
        // delete the artwork or achieve nothing while claiming success, so both bail out.
        var share = (double)cleared / (width * (long)height);
        if (share < 0.10)
        {
            detail = "has no border-connected background to remove; left unchanged";
            return false;
        }

        if (share > 0.985)
        {
            detail = "is almost entirely background, so there is no logo left to keep; left unchanged";
            return false;
        }

        try
        {
            File.WriteAllBytes(ddsPath, BuildDxt5WithAlpha(file, width, height, ReadMipCount(file), rgba));
        }
        catch (Exception ex)
        {
            detail = "could not be written back (" + ex.Message + ")";
            return false;
        }

        detail = (100.0 * share).ToString("F0") + "% of the frame was background and is now transparent";
        return true;
    }

    /// <summary>Reads a DDS header, returning its dimensions and FourCC as text.</summary>
    internal static bool TryReadDds(byte[] file, out int width, out int height, out string fourCc)
    {
        width = height = 0;
        fourCc = "not a DDS file";

        if (file.Length < HeaderSize ||
            file[0] != (byte)'D' || file[1] != (byte)'D' || file[2] != (byte)'S' || file[3] != (byte)' ')
        {
            return false;
        }

        height = BitConverter.ToInt32(file, 12);
        width = BitConverter.ToInt32(file, 16);
        fourCc = System.Text.Encoding.ASCII.GetString(file, 84, 4).TrimEnd('\0');

        return width > 0 && height > 0;
    }
    /// <summary>
    /// Decodes the top mip of a block-compressed DDS into straight (non-premultiplied) RGBA.
    /// <para>
    /// Only the base image is decoded: mip levels are regenerated from scratch on the way out,
    /// which is both simpler and more correct than resampling someone else's mip chain.
    /// </para>
    /// </summary>
    internal static bool TryDecode(byte[] file, out int width, out int height, out byte[] rgba, out string format)
    {
        rgba = Array.Empty<byte>();
        if (!TryReadDds(file, out width, out height, out format))
            return false;

        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        var blockBytes = format switch
        {
            "DXT1" => 8,
            "DXT3" or "DXT5" => 16,
            _ => 0
        };

        if (blockBytes == 0)
            return false;

        // A truncated texture is not something to guess at; the caller reports the format and
        // leaves the file alone.
        if (file.Length < HeaderSize + blocksWide * blocksHigh * blockBytes)
            return false;

        rgba = new byte[width * height * 4];
        var offset = HeaderSize;

        for (var by = 0; by < blocksHigh; by++)
        {
            for (var bx = 0; bx < blocksWide; bx++)
            {
                var block = offset + (by * blocksWide + bx) * blockBytes;

                // Colour is decoded before alpha on purpose. DecodeColorBlock writes 255 into the
                // alpha byte of every pixel it touches, because in a DXT1 block that byte is
                // implied rather than stored. Running it second would stamp over the alpha the
                // other call had just decoded, and every pixel would come back opaque.
                if (format == "DXT5")
                {
                    DecodeColorBlock(file, block + 8, rgba, bx, by, width, height);
                    DecodeAlphaBlock(file, block, rgba, bx, by, width, height, interpolated: true);
                }
                else if (format == "DXT3")
                {
                    DecodeColorBlock(file, block + 8, rgba, bx, by, width, height);
                    DecodeAlphaBlock(file, block, rgba, bx, by, width, height, interpolated: false);
                }
                else
                {
                    DecodeColorBlock(file, block, rgba, bx, by, width, height);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Decodes a BC3/BC4 alpha block, in either the interpolated (DXT5) or explicit (DXT3)
    /// form, into the alpha bytes of <paramref name="rgba"/>.
    /// </summary>
    private static void DecodeAlphaBlock(
        byte[] src, int offset, byte[] rgba, int bx, int by, int width, int height, bool interpolated)
    {
        Span<byte> palette = stackalloc byte[8];

        if (interpolated)
        {
            palette[0] = src[offset];
            palette[1] = src[offset + 1];

            if (palette[0] > palette[1])
            {
                for (var i = 1; i < 7; i++)
                    palette[i + 1] = (byte)((palette[0] * (7 - i) + palette[1] * i) / 7);
            }
            else
            {
                for (var i = 1; i < 5; i++)
                    palette[i + 1] = (byte)((palette[0] * (5 - i) + palette[1] * i) / 5);
                palette[6] = 0;
                palette[7] = 255;
            }
        }

        for (var py = 0; py < 4; py++)
        {
            for (var px = 0; px < 4; px++)
            {
                var x = bx * 4 + px;
                var y = by * 4 + py;
                if (x >= width || y >= height)
                    continue;

                byte alpha;
                if (interpolated)
                {
                    // 16 three-bit indices packed into 6 bytes, read little-endian across the
                    // byte boundary rather than per byte.
                    var bit = (py * 4 + px) * 3;
                    var index = offset + 2 + (bit >> 3);
                    var shift = bit & 7;
                    var packed = src[index] >> shift;
                    if (shift > 5)
                        packed |= src[index + 1] << (8 - shift);
                    alpha = palette[packed & 7];
                }
                else
                {
                    var nibble = src[offset + py * 4 + px];
                    alpha = (byte)((py * 4 + px) % 2 == 0 ? nibble & 0x0F : nibble >> 4);
                    alpha = (byte)(alpha * 17); // 4-bit -> 8-bit
                }

                rgba[(y * width + x) * 4 + 3] = alpha;
            }
        }
    }
    /// <summary>Decodes a BC1 colour block into the RGB bytes of <paramref name="rgba"/>.</summary>
    private static void DecodeColorBlock(byte[] src, int offset, byte[] rgba, int bx, int by, int width, int height)
    {
        Span<int> palette = stackalloc int[4];

        var c0 = src[offset] | (src[offset + 1] << 8);
        var c1 = src[offset + 2] | (src[offset + 3] << 8);
        palette[0] = c0;
        palette[1] = c1;

        if (c0 > c1)
        {
            palette[2] = Interpolate565(c0, c1, 2);
            palette[3] = Interpolate565(c0, c1, 1);
        }
        else
        {
            // The 3-colour mode: index 3 is fully transparent black, not a colour. Decoding it as a
            // colour is what turns a soft anti-aliased edge into coloured speckle.
            palette[2] = Interpolate565(c0, c1, 1);
            palette[3] = c0;
        }

        var bits = (uint)(src[offset + 4] | (src[offset + 5] << 8) |
                          (src[offset + 6] << 16) | (src[offset + 7] << 24));

        for (var py = 0; py < 4; py++)
        {
            for (var px = 0; px < 4; px++)
            {
                var x = bx * 4 + px;
                var y = by * 4 + py;
                if (x >= width || y >= height)
                    continue;

                var colour = palette[(int)((bits >> ((py * 4 + px) * 2)) & 3)];
                var p = (y * width + x) * 4;

                // The palette holds raw RGB565 values, so it has to be expanded rather than shifted
                // apart. Treating a 565 value as though it were 8-bits-per-channel reads almost
                // every colour as black, which silently turns a colourful logo into a silhouette.
                Unpack565(colour, out rgba[p], out rgba[p + 1], out rgba[p + 2]);
                rgba[p + 3] = 255;
            }
        }
    }

    /// <summary>
    /// Interpolates between two RGB565 endpoints <b>per channel</b>, after expanding both to 8 bits.
    /// <para>
    /// This must not be done on the packed integers. Adding two packed 5:6:5 values together lets a
    /// carry out of the green channel run into the red one, so the average of two identical greys can
    /// come back as a saturated colour. On the real BMW badge that turned the black outer ring into
    /// red, green and cyan speckle across 1371 of 16384 pixels, and the fault was invisible until the
    /// decoded pixels were actually rendered - every assertion about dimensions and block counts
    /// still passed, because the file was structurally perfect.
    /// </para>
    /// <para>
    /// <paramref name="weight"/> counts the first endpoint's contribution: 2 gives (2*c0 + c1)/3,
    /// which is the BC1 four-colour mode's third entry; 1 gives the midpoint used by the
    /// three-colour mode's third entry.
    /// </para>
    /// </summary>
    private static int Interpolate565(int c0, int c1, int weight)
    {
        Unpack565(c0, out var r0, out var g0, out var b0);
        Unpack565(c1, out var r1, out var g1, out var b1);

        var total = weight + 1;
        var r = (weight * r0 + r1) / total;
        var g = (weight * g0 + g1) / total;
        var b = (weight * b0 + b1) / total;

        return ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
    }

    /// <summary>
    /// Expands a 5:6:5 colour to 8 bits per channel.
    /// <para>
    /// The high bits are replicated into the low bits rather than shifted up and zero-filled. That
    /// is what the hardware does when it expands the format, so it keeps 0xFFFF white at 255
    /// instead of 248 and lets an encode/decode round trip land back on the value it started from.
    /// </para>
    /// </summary>
    private static void Unpack565(int colour, out byte r, out byte g, out byte b)
    {
        var r5 = (colour >> 11) & 0x1F;
        var g6 = (colour >> 5) & 0x3F;
        var b5 = colour & 0x1F;

        r = (byte)((r5 << 3) | (r5 >> 2));
        g = (byte)((g6 << 2) | (g6 >> 4));
        b = (byte)((b5 << 3) | (b5 >> 2));
    }

    /// <summary>
    /// Makes the border-connected dark background of a decoded image transparent, leaving the
    /// artwork itself untouched.
    /// <para>
    /// This is a flood fill from the edges rather than a global "make black transparent" key,
    /// and the distinction is the whole point. A logo legitimately contains black - the BMW
    /// roundel is black and white, the Volvo badge has a black interior - and a global key
    /// punches holes straight through the artwork. Filling only from the border reaches the
    /// background field and stops at the first pixel too light to be background, so interior
    /// blacks survive.
    /// </para>
    /// <para>
    /// Pixels that are neither clearly background nor clearly artwork get a partial alpha,
    /// which is what stops a hard-edged dark rim from appearing around anti-aliased lettering.
    /// </para>
    /// </summary>
    internal static void KnockOutBackground(byte[] rgba, int width, int height, out int cleared)
    {
        cleared = 0;
        var reached = new bool[width * height];
        var queue = new Queue<int>();

        void TryEnqueue(int index)
        {
            if (reached[index])
                return;

            var p = index * 4;
            if (Luma(rgba[p], rgba[p + 1], rgba[p + 2]) > BackgroundLumaCutoff)
                return;

            reached[index] = true;
            queue.Enqueue(index);
        }

        for (var x = 0; x < width; x++)
        {
            TryEnqueue(x);
            TryEnqueue((height - 1) * width + x);
        }

        for (var y = 0; y < height; y++)
        {
            TryEnqueue(y * width);
            TryEnqueue(y * width + width - 1);
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            cleared++;

            var x = index % width;
            var y = index / width;

            if (x > 0) TryEnqueue(index - 1);
            if (x < width - 1) TryEnqueue(index + 1);
            if (y > 0) TryEnqueue(index - width);
            if (y < height - 1) TryEnqueue(index + width);
        }

        // Reached pixels are the background and become fully transparent; everything else is
        // artwork and starts fully opaque. This pass also feathers the boundary: an unreached
        // pixel that touches cleared background but is too light to have been cleared itself is
        // an anti-aliased edge pixel, so its alpha is scaled by how dark it is instead of being
        // forced opaque, which is what stops a dark rim appearing around the lettering.
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var p = index * 4;

                if (reached[index])
                {
                    rgba[p + 3] = 0;
                    continue;
                }

                rgba[p + 3] = 255;

                var touchesBackground =
                    (x > 0 && reached[index - 1]) ||
                    (x < width - 1 && reached[index + 1]) ||
                    (y > 0 && reached[index - width]) ||
                    (y < height - 1 && reached[index + width]);

                if (!touchesBackground)
                    continue;

                var luma = Luma(rgba[p], rgba[p + 1], rgba[p + 2]);
                if (luma <= BackgroundLumaCutoff)
                    rgba[p + 3] = 0;
                else if (luma < EdgeLumaCutoff)
                    rgba[p + 3] = (byte)((luma - BackgroundLumaCutoff) * 255 / (EdgeLumaCutoff - BackgroundLumaCutoff));
            }
        }
    }

    private static int Luma(byte r, byte g, byte b) => (r * 299 + g * 587 + b * 114) / 1000;
    /// <summary>
    /// Builds a DXT5 DDS from a DXT1 source, adding an alpha channel and copying every colour
    /// block across byte for byte.
    /// <para>
    /// Re-encoding the colour from decoded pixels would be a serious downgrade, and measuring it
    /// showed why: a BC1 block holds only four palette entries, so a fresh bounding-box fit lands
    /// the artwork on entirely different colours from the ones the mod author chose - Volvo's red
    /// came back green, with a worst-case error of 251 per channel. The source blocks are already
    /// the mod author's own optimum, so they are copied verbatim instead. Only the alpha block in
    /// front of each one is synthesised, which makes the artwork provably identical to the input
    /// rather than approximately equal to it.
    /// </para>
    /// <para>
    /// The header is written field by field to match a known-good ATS logo
    /// (<c>Ford Focus Mk3</c>'s <c>ford.dds</c>) rather than to match a generic DDS writer:
    /// <c>dwPitchOrLinearSize</c> is left at 0 and <c>dwCaps</c> is 0x401008 (texture + mipmap
    /// + complex). Matching a file the game already loads is the safer reference.
    /// </para>
    /// </summary>
    /// <param name="source">
    /// The original DXT1 file. Its colour blocks and its mip count are both reused as-is.
    /// </param>
    /// <param name="mipCount">
    /// Levels to emit, taken from the source. A source is free to ship a shorter chain than the
    /// full one, and matching it is safer than inventing levels the mod author never had.
    /// </param>
    /// <param name="rgba">The decoded base image with its alpha channel already knocked out.</param>
    internal static byte[] BuildDxt5WithAlpha(byte[] source, int width, int height, int mipCount, byte[] rgba)
    {
        var output = new List<byte>(source.Length * 2 + HeaderSize);
        var header = new byte[HeaderSize];

        header[0] = (byte)'D';
        header[1] = (byte)'D';
        header[2] = (byte)'S';
        header[3] = (byte)' ';
        WriteInt(header, 4, 124);            // dwSize
        WriteInt(header, 8, 0xA1007);        // CAPS | HEIGHT | WIDTH | PIXELFORMAT | MIPMAPCOUNT | LINEARSIZE
        WriteInt(header, 12, height);
        WriteInt(header, 16, width);
        WriteInt(header, 20, 0);             // dwPitchOrLinearSize, as ATS ships it
        WriteInt(header, 24, 0);             // dwDepth
        WriteInt(header, 28, mipCount);
        WriteInt(header, 76, 32);            // ddspf.dwSize
        WriteInt(header, 80, 0x4);           // DDPF_FOURCC
        header[84] = (byte)'D';
        header[85] = (byte)'X';
        header[86] = (byte)'T';
        header[87] = (byte)'5';
        WriteInt(header, 108, 0x401008);     // DDSCAPS_COMPLEX | TEXTURE | MIPMAP

        output.AddRange(header);

        var alpha = rgba;
        var levelWidth = width;
        var levelHeight = height;
        var sourceOffset = HeaderSize;

        for (var mip = 0; mip < mipCount; mip++)
        {
            var blocksWide = (levelWidth + 3) / 4;
            var blocksHigh = (levelHeight + 3) / 4;
            var sourceBytes = blocksWide * blocksHigh * 8;

            // Stop if the source does not actually contain this level, rather than reading past
            // the end of the file and emitting whatever happened to be there.
            if (sourceOffset + sourceBytes > source.Length)
                break;

            var block = new byte[16];
            var index = 0;

            for (var by = 0; by < blocksHigh; by++)
            {
                for (var bx = 0; bx < blocksWide; bx++)
                {
                    // Alpha first, then the author's own colour block, unchanged.
                    EncodeAlphaBlock(alpha, bx, by, levelWidth, levelHeight, block);
                    Array.Copy(source, sourceOffset + index * 8, block, 8, 8);
                    output.AddRange(block);
                    index++;
                }
            }

            sourceOffset += sourceBytes;
            (alpha, levelWidth, levelHeight) = Downsample(alpha, levelWidth, levelHeight);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Re-encodes a cropped and re-padded badge as DXT5, copying the colour blocks out of the
    /// original rather than re-deriving them.
    /// <para>
    /// The whole reason this exists in this shape is that cropping must not cost artwork quality. A
    /// DXT colour block holds only four palette entries, and re-fitting them from decoded pixels was
    /// measured destroying real logos - up to 251 error per channel, with one mod's red coming back
    /// green. So the colour bytes are copied verbatim from whichever source block covers each
    /// destination block, and only the alpha block in front of each one is generated.
    /// </para>
    /// <para>
    /// <paramref name="rgba"/> is the already-cropped image (see
    /// <see cref="BrandLogoCrop.Extract"/>), so the alpha blocks are built from the padded pixels.
    /// A destination block with no corresponding source block - one that lands entirely inside the
    /// padding - has no artwork to protect, so it takes the source's edge colour rather than black,
    /// which keeps the transparent margin from darkening the logo if a mip ever samples across it.
    /// </para>
    /// <para>
    /// Only the base mip is cropped. The smaller levels are regenerated from it, exactly as
    /// <see cref="BuildDxt5WithAlpha"/> does, rather than being cropped independently: cropping a
    /// mip that was itself built for the old canvas would compound the error.
    /// </para>
    /// </summary>
    internal static byte[] BuildDxt5Cropped(
        byte[] source, int sourceWidth, int sourceHeight, int mipCount, byte[] rgba, BrandLogoCrop.Plan plan)
    {
        var output = new List<byte>(source.Length + 256);
        var header = new byte[HeaderSize];

        header[0] = (byte)'D';
        header[1] = (byte)'D';
        header[2] = (byte)'S';
        header[3] = (byte)' ';
        WriteInt(header, 4, 124);
        WriteInt(header, 8, 0xA1007);
        WriteInt(header, 12, plan.OutputHeight);
        WriteInt(header, 16, plan.OutputWidth);
        WriteInt(header, 20, 0);
        WriteInt(header, 24, 0);
        WriteInt(header, 28, mipCount);
        WriteInt(header, 76, 32);
        WriteInt(header, 80, 0x4);
        header[84] = (byte)'D';
        header[85] = (byte)'X';
        header[86] = (byte)'T';
        header[87] = (byte)'5';
        WriteInt(header, 108, 0x401008);

        output.AddRange(header);

        var sourceBlocksWide = (sourceWidth + 3) / 4;
        var sourceBlocksHigh = (sourceHeight + 3) / 4;

        // The source is usually already DXT5 by the time this runs - the transparency pass has just
        // rewritten it - and a DXT5 block is 16 bytes with the colour block in its second half, not
        // the 8 bytes of a DXT1 block. Assuming the DXT1 layout here reads the wrong eight bytes
        // entirely, which showed up as a max channel error of 255: correct shapes, wrong colours.
        if (!TryReadDds(source, out _, out _, out var sourceFormat))
            return source;

        var sourceBlockBytes = sourceFormat switch { "DXT1" => 8, "DXT3" or "DXT5" => 16, _ => 0 };
        if (sourceBlockBytes == 0)
            return source;

        var sourceColourOffset = sourceBlockBytes == 16 ? 8 : 0;

        var alpha = rgba;
        var levelWidth = plan.OutputWidth;
        var levelHeight = plan.OutputHeight;

        for (var mip = 0; mip < mipCount; mip++)
        {
            var blocksWide = (levelWidth + 3) / 4;
            var blocksHigh = (levelHeight + 3) / 4;
            var block = new byte[16];

            for (var by = 0; by < blocksHigh; by++)
            {
                for (var bx = 0; bx < blocksWide; bx++)
                {
                    EncodeAlphaBlock(alpha, bx, by, levelWidth, levelHeight, block);

                    // The source block under this destination block, once the crop offset and the
                    // padding are taken into account. Block-aligned, so this is exact.
                    var sourceBlockX = plan.SourceX / 4 + bx - plan.OffsetX / 4;
                    var sourceBlockY = plan.SourceY / 4 + by - plan.OffsetY / 4;

                    CopyColourBlock(source, sourceBlocksWide, sourceBlocksHigh,
                        sourceBlockBytes, sourceColourOffset,
                        sourceBlockX, sourceBlockY, block, mip);

                    output.AddRange(block);
                }
            }

            (alpha, levelWidth, levelHeight) = Downsample(alpha, levelWidth, levelHeight);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Copies one 8-byte BC1 colour block into the colour half of a DXT5 block.
    /// <para>
    /// Falls back to the source's edge colour when the destination block lies entirely in the
    /// padding, which keeps a transparent margin from being pure black.
    /// </para>
    /// </summary>
    private static void CopyColourBlock(
        byte[] source, int sourceBlocksWide, int sourceBlocksHigh,
        int sourceBlockBytes, int sourceColourOffset,
        int sourceBlockX, int sourceBlockY, byte[] block, int mip)
    {
        // A destination block with no artwork behind it — one landing entirely in the padding — has
        // nothing to protect, so it borrows the nearest real block's colour. Black would be simpler
        // and would also be wrong: a transparent margin of pure black bleeds into the artwork when
        // the mip chain averages across it.
        var outsideArtwork =
            mip > 0
            || sourceBlockX < 0 || sourceBlockY < 0
            || sourceBlockX >= sourceBlocksWide || sourceBlockY >= sourceBlocksHigh;

        var blockX = outsideArtwork ? Math.Clamp(sourceBlockX, 0, sourceBlocksWide - 1) : sourceBlockX;
        var blockY = outsideArtwork ? Math.Clamp(sourceBlockY, 0, sourceBlocksHigh - 1) : sourceBlockY;

        var offset = HeaderSize + (blockY * sourceBlocksWide + blockX) * sourceBlockBytes + sourceColourOffset;
        if (offset >= 0 && offset + 8 <= source.Length)
            Array.Copy(source, offset, block, 8, 8);
    }

    /// <summary>Reads the mip level count a DDS header declares.</summary>
    internal static int ReadMipCount(byte[] file)
    {
        var count = BitConverter.ToUInt32(file, 28);

        // A count of zero means the file has no mip chain at all. Some writers leave the field
        // unset, so fall back to a single level rather than emitting nothing.
        return count == 0 ? 1 : (int)Math.Min(count, 32);
    }

    /// <summary>
    /// Number of mip levels in a full chain, counting the base image as level 0.
    /// </summary>
    internal static int MipCount(int width, int height)
    {
        var count = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            count++;
        }

        return count;
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    /// <summary>
    /// Encodes the 16 alpha samples of one block into a BC4-style alpha block.
    /// <para>
    /// Extremes are taken as min/max alpha across the block and the remaining palette entries are
    /// interpolated between them, which is the standard BC4 construction. The six index bytes are
    /// packed little-endian across byte boundaries, matching how the decoder above reads them.
    /// </para>
    /// </summary>
    private static void EncodeAlphaBlock(byte[] rgba, int bx, int by, int width, int height, Span<byte> block)
    {
        Span<byte> alpha = stackalloc byte[16];
        var min = (byte)255;
        var max = (byte)0;

        for (var py = 0; py < 4; py++)
        {
            for (var px = 0; px < 4; px++)
            {
                var x = Math.Min(bx * 4 + px, width - 1);
                var y = Math.Min(by * 4 + py, height - 1);
                var value = rgba[(y * width + x) * 4 + 3];
                alpha[py * 4 + px] = value;

                if (value < min) min = value;
                if (value > max) max = value;
            }
        }

        Span<byte> palette = stackalloc byte[8];
        palette[0] = max;
        palette[1] = min;

        if (max > min)
        {
            for (var i = 1; i < 7; i++)
                palette[i + 1] = (byte)((max * (7 - i) + min * i) / 7);
        }
        else
        {
            for (var i = 1; i < 5; i++)
                palette[i + 1] = (byte)((max * (5 - i) + min * i) / 5);
            palette[6] = 0;
            palette[7] = 255;
        }

        // Clear the block, then store the two endpoints the indices refer to. Omitting these
        // writes is what makes every pixel decode as fully transparent: the decoder rebuilds
        // its palette from block[0] and block[1], so a block whose endpoints were never written
        // decodes against a palette of zeros no matter what the indices say.
        block.Slice(0, 8).Clear();
        block[0] = max;
        block[1] = min;

        for (var i = 0; i < 16; i++)
        {
            var best = 0;
            var bestDistance = int.MaxValue;

            for (var candidate = 0; candidate < 8; candidate++)
            {
                var distance = Math.Abs(palette[candidate] - alpha[i]);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = candidate;
            }

            var bit = i * 3;
            var index = 2 + (bit >> 3);
            var shift = bit & 7;
            var value3 = (uint)(best << shift);

            block[index] |= (byte)value3;
            if (shift > 5)
                block[index + 1] |= (byte)(value3 >> 8);
        }
    }

    /// <summary>
    /// Builds a DXT5 DDS from an already-resampled RGBA image, re-deriving every colour block.
    /// <para>
    /// <b>This is the one lossy encoder in the project.</b> It exists solely for
    /// <see cref="BrandLogoResample"/>, where the source blocks describe a different set of pixels and
    /// so cannot be copied. Every other badge path copies colour blocks verbatim and keeps the
    /// byte-for-byte guarantee.
    /// </para>
    /// <para>
    /// Endpoints come from a PRINCIPAL AXIS fit rather than a bounding box. A BC1 block holds only two
    /// endpoints plus two interpolations, so where those two colours sit dominates the error, and a
    /// bounding box puts them on the extremes of the block's colour cloud - which for a logo block
    /// spanning lettering and background lands the artwork on colours the mod author never chose. The
    /// principal axis runs through the block's actual colour variance instead, which approximates far
    /// better and is what the eye notices least.
    /// </para>
    /// </summary>
    internal static byte[] BuildDxt5Lossy(int width, int height, int mipCount, byte[] rgba)
    {
        var output = new List<byte>(((width + 3) / 4) * ((height + 3) / 4) * 16 * mipCount + HeaderSize);
        var header = new byte[HeaderSize];

        header[0] = (byte)'D';
        header[1] = (byte)'D';
        header[2] = (byte)'S';
        header[3] = (byte)' ';
        WriteInt(header, 4, 124);
        WriteInt(header, 8, 0xA1007);
        WriteInt(header, 12, height);
        WriteInt(header, 16, width);
        WriteInt(header, 20, 0);
        WriteInt(header, 24, 0);
        WriteInt(header, 28, mipCount);
        WriteInt(header, 76, 32);
        WriteInt(header, 80, 0x4);
        header[84] = (byte)'D';
        header[85] = (byte)'X';
        header[86] = (byte)'T';
        header[87] = (byte)'5';
        WriteInt(header, 108, 0x401008);

        output.AddRange(header);

        var pixels = rgba;
        var levelWidth = width;
        var levelHeight = height;

        for (var mip = 0; mip < mipCount; mip++)
        {
            var blocksWide = (levelWidth + 3) / 4;
            var blocksHigh = (levelHeight + 3) / 4;
            var block = new byte[16];

            for (var by = 0; by < blocksHigh; by++)
            {
                for (var bx = 0; bx < blocksWide; bx++)
                {
                    EncodeAlphaBlock(pixels, bx, by, levelWidth, levelHeight, block);
                    EncodeColourBlock(pixels, bx, by, levelWidth, levelHeight, block);
                    output.AddRange(block);
                }
            }

            (pixels, levelWidth, levelHeight) = Downsample(pixels, levelWidth, levelHeight);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Returns a copy of the image with every pixel's colour replaced by its own luminance.
    /// <para>
    /// Used by the opt-in greyscale pass, which exists because the base game's own badges are monochrome
    /// - Dodge, RAM and Ford all read as grey/white in the car shop - while a converted badge keeps its
    /// brand colours and therefore looks like a different kind of object.
    /// </para>
    /// <para>
    /// Alpha is copied through untouched, and that is what makes this safe to run at any point in the
    /// pipeline: it cannot disturb the silhouette or reintroduce an opaque background, because it never
    /// touches the channel that decides what is visible. It does change colour, so it forfeits the
    /// byte-for-byte colour guarantee in the same way resampling does.
    /// </para>
    /// </summary>
    internal static byte[] Greyscale(byte[] rgba)
    {
        var result = new byte[rgba.Length];

        for (var i = 0; i < rgba.Length; i += 4)
        {
            // Rec.601 luma, the same weighting the alpha pass already uses for its border fill, so the
            // two passes agree on what "brightness" means for this artwork.
            var luma = (byte)((rgba[i] * 299 + rgba[i + 1] * 587 + rgba[i + 2] * 114) / 1000);

            result[i] = luma;
            result[i + 1] = luma;
            result[i + 2] = luma;
            result[i + 3] = rgba[i + 3];
        }

        return result;
    }

    /// <summary>
    /// Byte offset of the colour half within a 16-byte DXT5 block. The first 8 bytes are alpha.
    /// </summary>
    private const int ColourOffset = 8;

    private static void WriteShort(Span<byte> buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }

    /// <summary>
    /// Fits the two BC1 colour endpoints of a block by principal axis, and writes them plus the
    /// per-pixel indices into the colour half of a DXT5 block.
    /// </summary>
    private static void EncodeColourBlock(byte[] rgba, int bx, int by, int width, int height, Span<byte> block)
    {
        Span<int> r = stackalloc int[16];
        Span<int> g = stackalloc int[16];
        Span<int> b = stackalloc int[16];
        Span<bool> solid = stackalloc bool[16];

        for (var py = 0; py < 4; py++)
        {
            for (var px = 0; px < 4; px++)
            {
                var x = Math.Min(bx * 4 + px, width - 1);
                var y = Math.Min(by * 4 + py, height - 1);
                var i = (y * width + x) * 4;
                var slot = py * 4 + px;

                r[slot] = rgba[i];
                g[slot] = rgba[i + 1];
                b[slot] = rgba[i + 2];

                // A fully transparent pixel carries whatever colour was behind it, which after the
                // knock-out is usually black. Including that in the fit drags both endpoints toward
                // black, so it is excluded from the statistics - the block still encodes an index for
                // it, it just does not drag the endpoints.
                solid[slot] = rgba[i + 3] > 8;
            }
        }

        Span<int> visible = stackalloc int[16];
        var count = 0;
        for (var i = 0; i < 16; i++)
            if (solid[i]) visible[count++] = i;

        // An entirely transparent block has no colour to fit; using every pixel keeps the endpoints valid
        // rather than uninitialised.
        if (count == 0)
            for (var i = 0; i < 16; i++) visible[count++] = i;

        double meanR = 0, meanG = 0, meanB = 0;
        for (var i = 0; i < count; i++)
        {
            meanR += r[visible[i]];
            meanG += g[visible[i]];
            meanB += b[visible[i]];
        }

        meanR /= count; meanG /= count; meanB /= count;

        double covRR = 0, covGG = 0, covBB = 0, covRG = 0, covRB = 0, covGB = 0;
        for (var i = 0; i < count; i++)
        {
            var k = visible[i];
            var dr = r[k] - meanR;
            var dg = g[k] - meanG;
            var db = b[k] - meanB;
            covRR += dr * dr; covGG += dg * dg; covBB += db * db;
            covRG += dr * dg; covRB += dr * db; covGB += dg * db;
        }

        // Dominant colour axis by power iteration. BC1 needs only one axis, and a handful of steps
        // converges well enough for a 4x4 block - far simpler than solving the 3x3 eigenproblem.
        var axisR = covRR >= covGG && covRR >= covBB ? 1.0 : 0.0;
        var axisG = axisR == 0.0 ? 1.0 : 0.0;
        var axisB = 0.0;
        var best = covRR * axisR * axisR + covGG * axisG * axisG;

        var vx = axisR; var vy = axisG; var vz = axisB;
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var nx = covRR * vx + covRG * vy + covRB * vz;
            var ny = covRG * vx + covGG * vy + covGB * vz;
            var nz = covRB * vx + covGB * vy + covBB * vz;

            var magnitude = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (magnitude < 1e-9) break;

            vx = nx / magnitude; vy = ny / magnitude; vz = nz / magnitude;

            var trace = covRR * vx * vx + covGG * vy * vy + covBB * vz * vz
                        + 2 * (covRG * vx * vy + covRB * vx * vz + covGB * vy * vz);
            if (trace <= best) break;

            best = trace;
            axisR = vx; axisG = vy; axisB = vz;
        }

        // The two endpoints are the extremes along that axis.
        var minT = double.MaxValue;
        var maxT = double.MinValue;
        for (var i = 0; i < count; i++)
        {
            var k = visible[i];
            var t = (r[k] - meanR) * axisR + (g[k] - meanG) * axisG + (b[k] - meanB) * axisB;
            if (t < minT) minT = t;
            if (t > maxT) maxT = t;
        }

        double end0R, end0G, end0B, end1R, end1G, end1B;
        if (maxT - minT < 1e-6)
        {
            // One flat colour: both endpoints coincide, which BC1 stores as a single solid entry.
            end0R = end1R = meanR; end0G = end1G = meanG; end0B = end1B = meanB;
        }
        else
        {
            end0R = meanR + axisR * minT; end0G = meanG + axisG * minT; end0B = meanB + axisB * minT;
            end1R = meanR + axisR * maxT; end1G = meanG + axisG * maxT; end1B = meanB + axisB * maxT;
        }

        var c0 = ToRgb565(end0R, end0G, end0B);
        var c1 = ToRgb565(end1R, end1G, end1B);

        // BC1 keeps all four palette entries only when c0 > c1. Swapping when it is not avoids falling
        // back to the three-entry mode, which drops an interpolation the artwork's gradient may rely on.
        if (c0 < c1) (c0, c1) = (c1, c0);

        // A DXT5 block is 16 bytes laid out as ALPHA first (8 bytes: two endpoints then six index bytes),
        // then COLOUR (8 bytes: two endpoints then four index bytes). Colour therefore starts at offset 8,
        // not 0 - writing it at 0 overwrites the alpha the encoder just produced and leaves the real colour
        // half as zeros, which decodes as solid black. This is the same offset
        // BuildDxt5Cropped already uses when copying blocks out of a source file.
        WriteShort(block, ColourOffset, (ushort)c0);
        WriteShort(block, ColourOffset + 2, (ushort)c1);
        WriteColourIndices(rgba, bx, by, width, height, solid, c0, c1, block);
    }

    /// <summary>
    /// Chooses a palette index per pixel for a block whose two colour endpoints are already written.
    /// </summary>
    private static void WriteColourIndices(
        byte[] rgba, int bx, int by, int width, int height, ReadOnlySpan<bool> solid,
        int c0, int c1, Span<byte> block)
    {
        Span<int> palette = stackalloc int[4];
        palette[0] = c0;
        palette[1] = c1;
        // Interpolated per channel, not on the packed values, for the same reason as the decoder: a
        // carry out of green would land in red and invent a colour the artwork never had.
        palette[2] = Interpolate565(c0, c1, 2);
        palette[3] = Interpolate565(c0, c1, 1);

        // Two bits per pixel in pixel order. Distances are measured in RGB565 because that is the space
        // the stored colours live in - comparing in 8-bit RGB would often pick an index the decoder
        // cannot actually reproduce, which is exactly the drift that gets blamed on the codec.
        ulong indices = 0;

        for (var i = 0; i < 16; i++)
        {
            var x = Math.Min(bx * 4 + i % 4, width - 1);
            var y = Math.Min(by * 4 + i / 4, height - 1);
            var p = (y * width + x) * 4;

            var tr = rgba[p];
            var tg = rgba[p + 1];
            var tb = rgba[p + 2];
            if (!solid[i]) tr = tg = tb = 0;

            var bestIndex = 0;
            var bestDistance = int.MaxValue;

            for (var candidate = 0; candidate < 4; candidate++)
            {
                var pr = (palette[candidate] >> 11) & 0x1F;
                var pg = (palette[candidate] >> 5) & 0x3F;
                var pb = palette[candidate] & 0x1F;

                var dr = ((pr << 3) | (pr >> 2)) - tr;
                var dg = ((pg << 2) | (pg >> 4)) - tg;
                var db = ((pb << 3) | (pb >> 2)) - tb;
                var distance = dr * dr + dg * dg + db * db;

                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestIndex = candidate;
            }

            indices |= (ulong)bestIndex << (i * 2);
        }

        for (var byteIndex = 0; byteIndex < 4; byteIndex++)
            block[ColourOffset + 4 + byteIndex] = (byte)((indices >> (byteIndex * 8)) & 0xFF);
    }

    private static int ToRgb565(double r, double g, double b)
    {
        static int Clamp(double value) => (int)Math.Clamp(Math.Round(value), 0, 255);

        return ((Clamp(r) >> 3) << 11) | ((Clamp(g) >> 2) << 5) | (Clamp(b) >> 3);
    }

    /// <summary>
    /// Halves an RGBA image for the next mip level.
    /// <para>
    /// Averaging is done on premultiplied colour, then unpremultiplied. Averaging straight RGBA
    /// would let the colour of a fully transparent pixel bleed into its opaque neighbours, which
    /// on this particular image means black bleeding out of the background into the lettering at
    /// every mip level - a dark halo that grows as the texture shrinks.
    /// </para>
    /// </summary>
    internal static (byte[] Pixels, int Width, int Height) Downsample(byte[] rgba, int width, int height)
    {
        var nextWidth = Math.Max(1, width / 2);
        var nextHeight = Math.Max(1, height / 2);
        var result = new byte[nextWidth * nextHeight * 4];

        for (var y = 0; y < nextHeight; y++)
        {
            for (var x = 0; x < nextWidth; x++)
            {
                var sumR = 0;
                var sumG = 0;
                var sumB = 0;
                var sumA = 0;
                var samples = 0;

                for (var dy = 0; dy < 2; dy++)
                {
                    for (var dx = 0; dx < 2; dx++)
                    {
                        var sx = Math.Min(x * 2 + dx, width - 1);
                        var sy = Math.Min(y * 2 + dy, height - 1);
                        var p = (sy * width + sx) * 4;
                        var a = rgba[p + 3];

                        sumR += rgba[p] * a;
                        sumG += rgba[p + 1] * a;
                        sumB += rgba[p + 2] * a;
                        sumA += a;
                        samples++;
                    }
                }

                var q = (y * nextWidth + x) * 4;
                var alpha = sumA / samples;

                if (alpha == 0)
                {
                    result[q + 3] = 0;
                    continue;
                }

                result[q] = (byte)Math.Min(255, sumR / sumA);
                result[q + 1] = (byte)Math.Min(255, sumG / sumA);
                result[q + 2] = (byte)Math.Min(255, sumB / sumA);
                result[q + 3] = (byte)alpha;
            }
        }

        return (result, nextWidth, nextHeight);
    }
}
