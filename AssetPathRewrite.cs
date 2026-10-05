using System.Text;

namespace TruckersToolKit;

/// <summary>
/// Repairs the absolute asset paths baked into a mod's own materials and compiled textures after
/// <c>vehicle/truck/&lt;car&gt;</c> has been moved to <c>vehicle/car/&lt;car&gt;</c>.
/// <para>
/// <b>This was the cause of the untextured car, and it was invisible to every check that existed.</b>
/// The converter rewrites definition paths in <c>.sii</c> text, and it moves the asset tree on
/// disk. It did <em>not</em> touch the absolute paths stored inside the mod's own binary assets, and
/// those are the ones the renderer actually follows. Measured on the real converted BMW archive,
/// after conversion <b>0</b> entries remained under <c>vehicle/truck/</c> while <b>96 files still
/// referenced it</b>: 52 <c>.mat</c> and 44 <c>.tobj</c>.
/// </para>
/// <para>
/// The ATS log from the session that produced the screenshot names every one of them:
/// </para>
/// <code>
///&lt;ERROR&gt; [mem server] Failed to init update for object '/vehicle/truck/m5_g90/tex/ao/ao_paint.tobj'.
/// ... 88 further failures, every one under /vehicle/truck ...
/// </code>
/// <para>
/// A surface whose material cannot be loaded is drawn with the engine's missing-material fallback,
/// which is what the pink/magenta car in the dealership screenshot is. Nothing about the conversion
/// <em>looked</em> wrong: the definitions were valid, the model was present, the dealer was right, and
/// the archive had grown to 406 files. The failure was only ever visible in game.
/// </para>

/// <para>
/// Two formats need fixing and they are genuinely different, which is why this is one pass with two
/// strategies rather than one regex over everything:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b><c>.mat</c> is plain text</b> (<c>source : "/vehicle/truck/m5_g90/tex/...tobj"</c>), UTF-8,
/// no BOM. Verified byte-identical on an encode/decode round trip, so a byte-level replacement of
/// the ASCII path is safe and leaves every other byte alone.
/// </description></item>
/// <item><description>
/// <b><c>.tobj</c> is binary</b> and carries the absolute path as a length-prefixed string at the very
/// end of the file. Measured across all 45 affected files: a little-endian <c>uint32</c> <b>8 bytes
/// before</b> the path holds its length, 4 further bytes sit between them, and the string runs to
/// the end of the file with no trailing NUL. Zero mismatches. Because <c>/vehicle/truck/</c> and
/// <c>/vehicle/car/</c> are different lengths, this cannot be an in-place patch: the length prefix
/// has to be rewritten and the file re-spun.
/// </description></item>
/// </list>
/// <para>
/// Every file whose layout does not match those measurements exactly is <b>left untouched</b> and
/// reported, in keeping with the rest of the converter: a wrong guess here produces a corrupt
/// compiled object, which is a worse outcome than a texture that stays broken.
/// </para>
/// <para>
/// Deliberately dependency-free, like <see cref="BrandLogoAlpha"/> and <see cref="MainLayout"/>, so
/// the layout assumptions can be asserted headlessly instead of only being inspected once.
/// </para>
/// </summary>
public static class AssetPathRewrite
{
    /// <summary>The path prefix the asset move invalidates.</summary>
    public const string TruckRoot = "/vehicle/truck/";

    /// <summary>What that prefix becomes once the assets are under <c>vehicle/car</c>.</summary>
    public const string CarRoot = "/vehicle/car/";

    /// <summary>Size of the little-endian length prefix in a compiled object.</summary>
    private const int LengthPrefixBytes = 4;

    /// <summary>Bytes from the start of the path back to the prefix that measures it.</summary>
    private const int PrefixLeadBytes = 8;

    /// <summary>Bytes between the end of that prefix and the start of the path.</summary>
    private const int PrefixToPathBytes = 4;



    /// <summary>
    /// Rewrites every affected material and texture under <paramref name="root"/>, in place.
    /// <para>
    /// Returns the archive-relative paths that were changed, for the report. Safe to call on a tree
    /// that holds none of them: it is a no-op rather than an error.
    /// </para>
    /// <para>
    /// Only references that point at a file <b>this mod actually shipped</b> are rewritten. A mod's
    /// materials also reference base-game files that happen to sit under the same
    /// <c>/vehicle/truck/</c> prefix - the real BMW mod points at
    /// <c>/vehicle/truck/share/dashboard.tobj</c>, <c>gps.tobj</c> and <c>glass_ex.tobj</c>, none of
    /// which it ships. Those files are still in <c>base.scs</c> at their original path, and the base
    /// game is never converted, so rewriting them would point the car at nothing. The known-working
    /// <c>Ford Focus Mk3_roadtrip.scs</c> proves the point: it references
    /// <c>/vehicle/truck/share/dashboard.tobj</c> and ships no such file, and it renders correctly.
    /// </para>
    /// <para>
    /// The moved set is therefore captured <em>before</em> the tree is moved, while the files are
    /// still on disk to be found.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> RewriteTree(
        string root, IReadOnlyCollection<string> movedPaths, Action<string> log)
    {
        var rewritten = new List<string>();
        if (!Directory.Exists(root) || movedPaths.Count == 0)
            return rewritten;

        var moved = new HashSet<string>(movedPaths, StringComparer.Ordinal);

        // Both extensions, and nothing else. Measured across a real converted archive, the only
        // files carrying a /vehicle/truck/ reference are .mat (text) and .tobj (binary); the
        // .pmd/.pma/.pmg/.pmc model files reference /automat/ materials instead and never did.
        foreach (var path in SafeEnumerate(root))
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".mat" && extension != ".tobj")
                continue;

            var detail = "";
            var changed = extension == ".mat"
                ? TryRewriteMaterial(path, moved, out detail)
                : TryRewriteTexture(path, moved, out detail);

            if (!changed)
                continue;

            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            rewritten.Add(relative);
            log($"[ASSET] {relative}: {detail}");
        }

        return rewritten;
    }

    /// <summary>
    /// Every file under <c>vehicle/truck/</c>, as archive-relative paths with forward slashes.
    /// <para>
    /// Captured before the asset move, because afterwards the files are under <c>vehicle/car</c> and
    /// there is no way left to tell which references were pointing at <em>this mod's</em> files
    /// rather than at the base game's.
    /// </para>
    /// </summary>
    public static HashSet<string> CaptureMovedPaths(string root)
    {
        var moved = new HashSet<string>(StringComparer.Ordinal);
        var vehicleTruck = Path.Combine(root, "vehicle", "truck");

        if (!Directory.Exists(vehicleTruck))
            return moved;

        foreach (var file in SafeEnumerate(vehicleTruck))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            moved.Add("/" + relative);
        }

        return moved;
    }

    /// <summary>
    /// Enumerates files without letting one unreadable directory abort the whole pass.
    /// <para>
    /// The alternative is that a single locked file silently costs the user every other repair in
    /// the archive, which is the exact class of bug this pass exists to fix.
    /// </para>
    /// </summary>
    private static List<string> SafeEnumerate(string root)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] subdirectories;
            string[] entries;

            try
            {
                subdirectories = Directory.GetDirectories(directory);
                entries = Directory.GetFiles(directory);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Skipped {directory}: {ex.Message}");
                continue;
            }

            files.AddRange(entries);
            foreach (var subdirectory in subdirectories)
                pending.Push(subdirectory);
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// Rewrites the truck path inside a text <c>.mat</c>, or leaves it alone.
    /// <para>
    /// Byte-level rather than decode/re-encode: the path is ASCII, so the bytes are identical either
    /// way, and going through the codec would risk re-encoding a file the mod author shipped. A file
    /// carrying NUL bytes is not text and is refused, because that means the extension lied.
    /// </para>
    /// </summary>
    public static bool TryRewriteMaterial(string path, IReadOnlySet<string> moved, out string detail)
    {
        detail = "";

        byte[] file;
        try
        {
            file = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            detail = "could not be read (" + ex.Message + ")";
            return false;
        }

        if (file.Length == 0)
        {
            detail = "is empty";
            return false;
        }

        foreach (var b in file)
        {
            if (b != 0)
                continue;

            detail = "contains NUL bytes, so it is not the plain-text format this repair expects; left unchanged";
            return false;
        }

        if (!TryRewriteText(file, moved, out var rewritten, out var replacements) || replacements == 0)
        {
            detail = "no " + TruckRoot + " reference";
            return false;
        }

        try
        {
            File.WriteAllBytes(path, rewritten);
        }
        catch (Exception ex)
        {
            detail = "could not be written back (" + ex.Message + ")";
            return false;
        }

        detail = $"rewrote {replacements} material reference(s) from {TruckRoot} to {CarRoot}";
        return true;
    }

    /// <summary>
    /// Replaces every <see cref="TruckRoot"/> occurrence in a text file's bytes.
    /// <para>
    /// Length-changing, which is fine here precisely because text has no length prefix. Kept separate
    /// from the binary path because the two must not be confused: applying this to a compiled object
    /// would leave its length prefix describing the old string.
    /// </para>
    /// </summary>
    internal static bool TryRewriteText(
        byte[] file, IReadOnlySet<string> moved, out byte[] rewritten, out int replacements)
    {
        rewritten = file;
        replacements = 0;

        var needle = Encoding.ASCII.GetBytes(TruckRoot);
        var replacement = Encoding.ASCII.GetBytes(CarRoot);

        var index = IndexOf(file, needle, 0);
        if (index < 0)
            return false;

        var buffer = new List<byte>(file.Length);
        var copied = 0;

        while (index >= 0)
        {
            // Only the reference that resolves into this mod's own moved tree. A base-game file
            // under the same prefix keeps its original path, because base.scs is never converted.
            if (ReferencesMovedFile(file, index, moved))
            {
                buffer.AddRange(file.Skip(copied).Take(index - copied));
                buffer.AddRange(replacement);
                copied = index + needle.Length;
                replacements++;
            }

            index = IndexOf(file, needle, index + needle.Length);
        }

        if (replacements == 0)
            return false;

        buffer.AddRange(file.Skip(copied));
        rewritten = buffer.ToArray();
        return true;
    }

    /// <summary>
    /// True when the reference starting at <paramref name="index"/> names a file the mod shipped.
    /// <para>
    /// The surrounding quotes and any trailing quote are excluded, so the candidate path is exactly
    /// the resource the loader would open.
    /// </para>
    /// </summary>
    private static bool ReferencesMovedFile(byte[] file, int index, IReadOnlySet<string> moved)
    {
        if (!TryReadReference(file, index, out var reference))
            return false;

        return moved.Contains(reference);
    }

    /// <summary>
    /// Reads the full quoted/ delimited path that begins at <paramref name="index"/>.
    /// <para>
    /// Handles the two shapes this pass actually rewrites: a quoted string in text
    /// (<c>"/vehicle/truck/..."</c>) and a bare length-prefixed string in a compiled object. Anything
    /// else is reported as unreadable rather than guessed at.
    /// </para>
    /// </summary>
    internal static bool TryReadReference(byte[] file, int index, out string reference)
    {
        reference = "";

        // A quote immediately before the path means this is a text reference; the closing quote
        // bounds it. A length prefix 8 bytes back means it is the compiled form, bounded by the
        // prefix's own length.
        if (index > 0 && file[index - 1] == (byte)'"')
        {
            var end = index;
            while (end < file.Length && file[end] != (byte)'"')
                end++;

            if (end >= file.Length)
                return false;

            reference = Encoding.ASCII.GetString(file, index, end - index);
            return true;
        }

        if (index >= PrefixLeadBytes)
        {
            var length = BitConverter.ToInt32(file, index - PrefixLeadBytes);
            if (length > 0 && index + length <= file.Length)
            {
                reference = Encoding.ASCII.GetString(file, index, length);
                return reference.StartsWith(TruckRoot, StringComparison.Ordinal);
            }
        }

        return false;
    }

    /// <summary>
    /// Rewrites the length-prefixed absolute path inside a binary <c>.tobj</c>, or leaves it alone.
    /// <para>
    /// Refuses anything whose layout is not exactly the measured one. A wrong rewrite here yields a
    /// compiled object the engine cannot parse, so a texture that stays broken is the better failure.
    /// </para>
    /// </summary>
    public static bool TryRewriteTexture(string path, IReadOnlySet<string> moved, out string detail)
    {
        detail = "";

        byte[] file;
        try
        {
            file = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            detail = "could not be read (" + ex.Message + ")";
            return false;
        }

        if (file.Length == 0)
        {
            detail = "is empty";
            return false;
        }

        if (!TryRewriteBinary(file, moved, out var rewritten, out var replacements, out var reason))
        {
            detail = reason;
            return false;
        }

        if (replacements == 0)
        {
            detail = "no " + TruckRoot + " reference";
            return false;
        }

        try
        {
            File.WriteAllBytes(path, rewritten);
        }
        catch (Exception ex)
        {
            detail = "could not be written back (" + ex.Message + ")";
            return false;
        }

        detail = $"rewrote {replacements} compiled texture path(s) from {TruckRoot} to {CarRoot}, " +
                 "length prefix and all";
        return true;
    }

    /// <summary>
    /// Rewrites the length-prefixed path in a compiled object.
    /// <para>
    /// For each occurrence the string boundaries are recovered from the bytes themselves, then the
    /// declared length prefix 8 bytes back must agree with the recovered length. Only then is the
    /// file rebuilt: everything before the prefix, the corrected prefix, the untouched bytes between
    /// prefix and string, the new path, and any trailing NUL padding.
    /// </para>
    /// </summary>
    internal static bool TryRewriteBinary(
        byte[] file, IReadOnlySet<string> moved,
        out byte[] rewritten, out int replacements, out string reason)
    {
        rewritten = file;
        replacements = 0;
        reason = "";

        var needle = Encoding.ASCII.GetBytes(TruckRoot);
        var result = file;
        var search = 0;

        while (true)
        {
            var at = IndexOf(result, needle, search);
            if (at < 0)
                break;

            var start = at;
            while (start > 0 && IsPathByte(result[start - 1]))
                start--;

            var end = at;
            while (end < result.Length && IsPathByte(result[end]))
                end++;

            var trailing = 0;
            while (end + trailing < result.Length && result[end + trailing] == 0)
                trailing++;

            var length = end - start;

            // The layout check runs first, so an unrecognised structure is reported even when the
            // reference turns out to be a base-game file this pass would have left alone anyway.
            if (start < PrefixLeadBytes ||
                BitConverter.ToInt32(result, start - PrefixLeadBytes) != length)
            {
                // Not the layout this pass understands. Say exactly what was found so the log points
                // at the file rather than at this method.
                rewritten = file;
                replacements = 0;
                reason = "holds " + TruckRoot + " but its embedded path is not in the expected " +
                         "length-prefixed layout (string at " + start + ", length " + length +
                         ", no matching prefix " + PrefixLeadBytes + " bytes earlier); left unchanged";
                return false;
            }

            var oldPath = Encoding.ASCII.GetString(result, start, length);

            // A base-game file under the same prefix keeps its path: base.scs is never converted,
            // so rewriting it would point the car at a file that does not exist.
            if (!moved.Contains(oldPath))
            {
                search = end;
                continue;
            }
            var newPath = oldPath.Replace(TruckRoot, CarRoot, StringComparison.Ordinal);
            var newBytes = Encoding.ASCII.GetBytes(newPath);

            var headerLength = start - PrefixLeadBytes;
            var buffer = new byte[headerLength + LengthPrefixBytes + PrefixToPathBytes +
                                 newBytes.Length + trailing];

            Array.Copy(result, 0, buffer, 0, headerLength);
            Array.Copy(BitConverter.GetBytes(newBytes.Length), 0, buffer, headerLength, LengthPrefixBytes);
            Array.Copy(result, start - PrefixToPathBytes, buffer,
                       headerLength + LengthPrefixBytes, PrefixToPathBytes);
            Array.Copy(newBytes, 0, buffer, start, newBytes.Length);
            // Trailing bytes are the NUL padding discovered above and are already zero.

            result = buffer;
            replacements++;

            // Resume at the start of the path just rewritten: it no longer matches, so the next
            // search moves on to any later occurrence without rescanning this one.
            search = start;
        }

        rewritten = result;
        return true;
    }

    /// <summary>Characters a resource path is built from, used to recover its boundaries.</summary>
    private static bool IsPathByte(byte b) =>
        (b >= (byte)'a' && b <= (byte)'z') ||
        (b >= (byte)'A' && b <= (byte)'Z') ||
        (b >= (byte)'0' && b <= (byte)'9') ||
        b == (byte)'_' || b == (byte)'.' || b == (byte)'/' || b == (byte)'-';

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        for (var i = Math.Max(0, start); i <= haystack.Length - needle.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j])
                    continue;

                matched = false;
                break;
            }

            if (matched)
                return i;
        }

        return -1;
    }
}
