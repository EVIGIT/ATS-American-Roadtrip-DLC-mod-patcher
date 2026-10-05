using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace TruckersToolKit;
/// <summary>Summary of the encrypted content found inside a mod.</summary>
public sealed record EncryptedModReport(int FileCount, IReadOnlyList<string> Samples)
{
    public static readonly EncryptedModReport None = new(0, Array.Empty<string>());
    public bool HasEncryptedFiles => FileCount > 0;
}

/// <summary>
/// Detects encrypted definition files so a mod that cannot be converted explains why
/// up front, instead of failing later with a confusing parse or binary-file error.
/// </summary>
public static class EncryptedModScanner
{
    // SCS marks protected definition files with this tag; the payload behind it is not
    // readable, so the file has to be reported rather than converted.
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("<Encrypted/>");
    private const int SniffBytes = 8192;
    private const int MaxSamples = 5;

    public static EncryptedModReport Scan(string root)
    {
        if (!Directory.Exists(root))
            return EncryptedModReport.None;

        var count = 0;
        var samples = new List<string>();

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch
        {
            return EncryptedModReport.None;
        }

        foreach (var file in files)
        {
            if (!LooksEncrypted(file))
                continue;

            count++;
            if (samples.Count < MaxSamples)
            {
                try
                {
                    samples.Add(Path.GetRelativePath(root, file));
                }
                catch
                {
                    samples.Add(file);
                }
            }
        }

        return count == 0 ? EncryptedModReport.None : new EncryptedModReport(count, samples);
    }

    /// <summary>Checks a single archive entry, used by the pre-flight input check.</summary>
    public static bool EntryLooksEncrypted(System.IO.Compression.ZipArchiveEntry entry)
    {
        try
        {
            if (entry.Length <= 0 || entry.Length > 64 * 1024 * 1024)
                return false;

            using var stream = entry.Open();
            var length = (int)Math.Min(SniffBytes, Math.Max(stream.Length, 1));
            var buffer = new byte[length];
            var read = stream.Read(buffer, 0, length);
            return Contains(buffer.AsSpan(0, read), Marker);
        }
        catch
        {
            // A protected entry throws when opened, which is itself a strong hint, but it
            // is not reported as encrypted here so a permissions error is not mislabelled.
            return false;
        }
    }

    private static bool LooksEncrypted(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var length = (int)Math.Min(SniffBytes, Math.Max(stream.Length, 1));
            var buffer = new byte[length];
            var read = stream.Read(buffer, 0, length);
            return Contains(buffer.AsSpan(0, read), Marker);
        }
        catch
        {
            return false;
        }
    }

    private static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
            return false;

        for (var offset = 0; offset <= haystack.Length - needle.Length; offset++)
        {
            if (haystack.Slice(offset, needle.Length).SequenceEqual(needle))
                return true;
        }

        return false;
    }
}

public sealed record ConversionSettings(
    string InputFile,
    string OutputDirectory,
    string DealerId,
    string ReferenceFile,
    bool MoveVehicleAssets,
    bool TranslateDealerDefinitions,
    bool PatchOnly = false,
    string VehicleType = "pickup",
    bool MapCamerasToCarUnits = true,
    bool NamespaceAnonymousUnits = true,
    bool UseSourceBrandToken = true,
    bool RenameBrandLogo = true,
    bool ResampleSquashedLogos = false,
    bool GreyscaleLogos = false)
{
    public static readonly string[] VehicleTypes =
        { "sedan", "hatchback", "pickup", "van" };
}

public sealed record VehicleInfo(string Id, string Directory, string DataFile);

public sealed record ConversionResult(
    string OutputFile,
    bool Success,
    IReadOnlyList<string> Issues);

public sealed class ConversionStats
{
    public int TextFilesScanned;
    public int TextFilesEdited;
    public int TextFilesUnchanged;
    public int BinaryDefinitionsSkipped;
    public int UnreadableDefinitions;
    public int FilesMovedToCar;
    public int FilesReplacedFromEarlierPatch;
    public int DealerFilesConverted;
    public int DealerFilesCreatedFromReference;

    /// <summary>
    /// Count of car definitions moved into the dealer's brand namespace
    /// (<c>vehicle.&lt;dealer&gt;.&lt;model&gt;</c>), which is what decides the dealership a car
    /// is listed under.
    /// </summary>
    public int CarsBrandscoped;

    /// <summary>Count of <c>_nameless.*</c> occurrences rewritten to a unique namespace.</summary>
    public int AnonymousUnitsNamespaced;

    /// <summary>
    /// Count of badges whose artwork was resampled because it was itself pre-squashed.
    /// <para>
    /// Only ever non-zero when <see cref="ConversionSettings.ResampleSquashedLogos"/> is on, because
    /// this is the one badge pass that re-encodes colour and so gives up the byte-for-byte guarantee.
    /// </para>
    /// </summary>
    public int BrandLogosResampled;

    /// <summary>Count of badges desaturated to match the base game's monochrome badges.</summary>
    public int BrandLogosGreyscaled;

    /// <summary>
    /// Count of <c>brand_logo</c> .mat files that existed but were empty or zero-filled.
    /// These are unusable placeholders, so this is a count of things the user needs to be told
    /// about rather than something the converter can fix on its own.
    /// </summary>
    public int EmptyBrandLogos;

    /// <summary>
    /// Count of extra <c>brand_logo</c> material files written so a renamed dealer keeps
    /// the mod's own logo.
    /// </summary>
    public int BrandLogosAdded;

    /// <summary>
    /// Logo textures rewritten from DXT1 to DXT5 so their background is transparent.
    /// </summary>
    public int BrandLogosMadeTransparent;

    /// <summary>
    /// Badge textures whose empty margin was trimmed and re-padded to the car shop's proportions,
    /// so they draw at the same size as the base game's own badges.
    /// </summary>
    public int BrandLogosCropped;

    /// <summary>
    /// The brand the mod shipped with, as read from <c>def/vehicle/truck_dealer/&lt;brand&gt;</c>.
    /// Empty when the mod carried no truck dealer at all. This is the token the game's own
    /// <c>material/ui/brand_logo/&lt;brand&gt;.mat</c> is named after.
    /// </summary>
    public string SourceBrandToken { get; set; } = "";

    /// <summary>Distinct unit names that were namespaced, for the report.</summary>
    public HashSet<string> AnonymousUnitNames { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Files whose baked-in absolute asset path was repaired after the truck-to-car asset move.
    /// <para>
    /// Non-zero on any mod that ships its own materials or compiled textures, which is nearly all of
    /// them. It is the count that was silently zero while every texture failed to load in game.
    /// </para>
    /// </summary>
    public int AssetPathsRewritten;
}

public static class ModConverter
{
    public static ConversionResult Run(
        ConversionSettings settings,
        Action<string> log,
        Action<int> progress)
    {
        // The dealer ID cannot be settled until the mod has been read: keeping the mod's own
        // brand means reading that brand out of the mod, not out of the text box.
        var work = Path.Combine(
            Path.GetTempPath(),
            "ats_roadtrip_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(work);

        try
        {
            progress(2);
            log("-> Reading input archive...");
            ScsArchive.Extract(settings.InputFile, work, log);

            var root = FindModRoot(work);
            log($"[INFO] Mod root: {root}");

            // --- Which brand this conversion becomes -------------------------------
            // A mod is built around its own brand token, and the game looks the dealer's
            // logo up by that same token (material/ui/brand_logo/<brand>.mat). Keeping the
            // token is therefore the option that needs no logo work at all, which is why
            // it is the default; renaming it is opt-in and gets the logo copied.
            var sourceToken = DiscoverSourceBrandToken(root, log);

            var dealerId = SanitizeId(settings.DealerId);
            if (settings.UseSourceBrandToken && sourceToken != null)
            {
                if (!dealerId.Equals(sourceToken, StringComparison.OrdinalIgnoreCase))
                    log($"[INFO] Dealer ID '{dealerId}' ignored: keeping the mod's own brand '{sourceToken}' " +
                        "so its dealership logo keeps working.");
                dealerId = sourceToken;
            }
            else if (dealerId == "custom" &&
                     !string.Equals(settings.DealerId.Trim(), "custom", StringComparison.OrdinalIgnoreCase))
            {
                log("[WARNING] Dealer ID contained no usable letters/numbers; using 'custom'.");
            }

            // Both branding options default on and are not independent. Converting the same input
            // twice under two different brand tokens ships two car_brand_logo files that each
            // override a different base-game brand, which is almost never intended. The check is
            // deliberately a warning rather than a refusal: two conversions under two brands can be
            // exactly what someone wants, and stopping would be the wrong call.
            WarnIfAlreadyConvertedUnderAnotherBrand(settings, dealerId, log);

            RemoveLegacyTruckSourceFiles(root, log);

            // Encrypted content cannot be read or converted. Report it clearly here
            // instead of letting it surface as a confusing parse error much later.
            var encryptedFiles = EncryptedModScanner.Scan(root);
            if (encryptedFiles.HasEncryptedFiles)
            {
                log($"[WARNING] {encryptedFiles.FileCount} file(s) in this mod are encrypted and cannot be converted.");
                log("          Encrypted files (first few): " + string.Join(", ", encryptedFiles.Samples));
            }

            string? referenceRoot = null;
            if (!string.IsNullOrWhiteSpace(settings.ReferenceFile))
            {
                var referenceWork = Path.Combine(work, "__reference");
                Directory.CreateDirectory(referenceWork);

                log("-> Reading Road Trip reference archive...");
                ScsArchive.Extract(settings.ReferenceFile, referenceWork, log);
                referenceRoot = FindModRoot(referenceWork);
                log($"[INFO] Reference root: {referenceRoot}");
            }

            progress(12);

            var defVehicle = Path.Combine(root, "def", "vehicle");
            if (!Directory.Exists(defVehicle))
                throw new InvalidDataException("The selected mod has no def/vehicle directory.");

            var carDef = Path.Combine(defVehicle, "car");
            var truckDef = Path.Combine(defVehicle, "truck");

            var stats = new ConversionStats();
            var moveAssets = settings.MoveVehicleAssets && !settings.PatchOnly;
            var mapCameras = settings.MapCamerasToCarUnits;
            if (!mapCameras)
                log("[INFO] Camera retargeting is off; original camera unit names are kept.");
            if (settings.PatchOnly && settings.MoveVehicleAssets)
                log("[INFO] Patch mode: vehicle/truck assets will be copied to the patch, so asset paths are converted.");

            var truckDealerRoot = Path.Combine(defVehicle, "truck_dealer");
            var originalTruckDealerFiles = Directory.Exists(truckDealerRoot)
                ? Directory.EnumerateFiles(truckDealerRoot, "*.*", SearchOption.AllDirectories)
                    .Where(IsTextDefinition)
                    .Select(f => Path.GetRelativePath(root, f))
                    .ToList()
                : new List<string>();

            var textBeforeMove = ReadAllTextDefinitionPaths(root, stats);
            var migratedTruckFolders = new List<string>();
            if (Directory.Exists(truckDef))
            {
                log("-> Migrating def/vehicle/truck to def/vehicle/car...");
                migratedTruckFolders.AddRange(
                    Directory.GetDirectories(truckDef)
                             .Select(d => Path.GetFileName(d)!)
                             .Where(n => !string.IsNullOrEmpty(n)));
                MergeTree(truckDef, carDef, log, stats);
                Directory.Delete(truckDef, true);
            }
            else if (Directory.Exists(carDef))
            {
                log("[INFO] def/vehicle/car already exists; definitions will be converted in place.");
            }
            else
            {
                throw new InvalidDataException(
                    "No def/vehicle/truck or def/vehicle/car directory was found.");
            }

            progress(25);

            // Captured BEFORE the move: afterwards the files are under vehicle/car and there is no way
            // left to tell a reference to this mod's own file from one to a base-game file that merely
            // shares the /vehicle/truck/ prefix. Only the former may be rewritten.
            var movedAssetPaths = moveAssets
                ? AssetPathRewrite.CaptureMovedPaths(root)
                : new HashSet<string>(StringComparer.Ordinal);

            if (moveAssets)
            {
                MoveVehicleAssetsToCar(root, log, stats);
            }

            // The asset move relocates the files on disk, but a mod's own materials and compiled
            // textures hold ABSOLUTE /vehicle/truck/... paths pointing straight at them. Rewriting
            // the .sii definitions does nothing for those, so without this pass the car loads with
            // every texture missing and is drawn in the engine's fallback material. Proven against a
            // real conversion and the ATS log from the session that hit it: 0 entries left under
            // vehicle/truck, 96 files still referencing it, 88 "Failed to init update" errors in game.
            //
            // Runs only when the assets were actually moved. In patch mode the ORIGINAL mod keeps
            // its vehicle/truck tree and stays mounted, so those paths still resolve there and
            // rewriting them would be wrong.
            if (moveAssets)
            {
                stats.AssetPathsRewritten += AssetPathRewrite.RewriteTree(root, movedAssetPaths, log).Count;
            }

            progress(35);

            log("-> Converting SII/SUI definitions...");
            // Two converted mods can both inherit "_nameless." unit names from their source
            // mod. Those names are global in SCS, so the second mod silently replaces the
            // first. Each conversion therefore gets its own namespace unless the user
            // turns that off.
            var anonymousNamespace = settings.NamespaceAnonymousUnits
                ? BuildAnonymousNamespace(dealerId, settings.InputFile)
                : null;
            if (anonymousNamespace != null)
                log($"[INFO] Namespacing global _nameless units as '_nameless.{anonymousNamespace}.' to avoid clashes with other mods.");

            ConvertDefinitionFiles(root, stats, log, moveAssets, settings.VehicleType, settings.PatchOnly, mapCameras, anonymousNamespace);

            progress(55);

            var vehicles = DiscoverVehicles(carDef);
            if (vehicles.Count == 0)
            {
                // Some mods do not use data.sii naming, but still have vehicle subfolders.
                vehicles = DiscoverVehiclesBySii(carDef);
            }

            if (vehicles.Count == 0)
                throw new InvalidDataException(
                    "No vehicle definition folders were found beneath def/vehicle/car.");

            log($"[INFO] Discovered {vehicles.Count} car definition folder(s): " +
                string.Join(", ", vehicles.Select(v => v.Id)));

            // Convert existing dealer definitions rather than fabricating a new syntax.
            if (settings.TranslateDealerDefinitions)
            {
                ConvertExistingDealers(root, dealerId, moveAssets, stats, log, settings.PatchOnly);
            }

            // A car is listed under the dealership named by the middle component of its
            // accessory_truck_data unit (base game: vehicle.ford.f150_23 under
            // car_dealer/ford). Truck-era mods omit that component, so without this the car
            // has no dealership of its own and the game files it under another brand's.
            log("-> Assigning the converted car to the selected dealer...");
            AssignDealerBrand(root, dealerId, migratedTruckFolders, stats, log);

            CopyBrandLogoForDealer(root, sourceToken, dealerId, settings.RenameBrandLogo, stats, log,
                settings.ResampleSquashedLogos, settings.GreyscaleLogos);
            EnsureCarBrandLogo(root, sourceToken, dealerId, settings.RenameBrandLogo, stats, log,
                settings.ResampleSquashedLogos, settings.GreyscaleLogos);
            stats.SourceBrandToken = sourceToken ?? "";

            if (stats.CarsBrandscoped == 0)
                log("[INFO] No car definition needed renaming; it already carried a brand component.");

            // If a reference is provided, only add missing car_dealer framework files.
            if (!string.IsNullOrWhiteSpace(referenceRoot))
            {
                CopyMissingReferenceFramework(referenceRoot, root, log, stats);
            }

            progress(72);

            // Do not invent an undocumented dealer syntax. A missing dealer is reported
            // as a validation issue so the user knows a real Road Trip dealer definition
            // still needs to be supplied or copied from a reference mod.
            var dealerFiles = GetDealerFiles(root, dealerId);
            if (dealerFiles.Count == 0)
            {
                log("[WARNING] No usable car dealer definition was found for the selected dealer.");
            }

            progress(80);

            log("-> Validating converted structure...");
            var issues = Validate(root, carDef, dealerId, moveAssets, log, settings.PatchOnly);
            if (encryptedFiles.HasEncryptedFiles)
            {
                issues.Insert(0,
                    $"{encryptedFiles.FileCount} file(s) in this mod are encrypted (for example {encryptedFiles.Samples[0]}). " +
                    "Encrypted content cannot be read or converted, so those files were skipped.");
            }
            if (stats.BinaryDefinitionsSkipped > 0)
            {
                var issue = $"{stats.BinaryDefinitionsSkipped} definition file(s) are binary/encrypted by the mod author and could not be converted; the car will not work until plain-text versions are supplied.";
                log("[WARNING] " + issue);
                issues.Add(issue);
            }

            WriteReport(
                root,
                settings,
                dealerId,
                vehicles,
                stats,
                issues,
                textBeforeMove);

            progress(88);

            var outputFile = GetOutputFile(settings);

            // The output folder has to exist before the archive is written into it. Full conversion
            // happened to get away with this because the GUI always pre-creates it, but a patch run
            // into a fresh folder threw DirectoryNotFoundException from ZipFile.CreateFromDirectory -
            // i.e. the only way to reach patch mode at all was to already have the output folder.
            Directory.CreateDirectory(settings.OutputDirectory);

            if (File.Exists(outputFile))
                File.Delete(outputFile);

            var packRoot = root;
            if (settings.PatchOnly)
            {
                packRoot = Path.Combine(work, "__patch");
                BuildPatch(root, packRoot, Path.GetFileNameWithoutExtension(settings.InputFile), log);
            }

            log(settings.PatchOnly
                ? "-> Packing definition patch as .scs (load it ABOVE the original mod)..."
                : "-> Repacking converted mod as .scs...");
            ZipFile.CreateFromDirectory(
                packRoot,
                outputFile,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);

            progress(100);
            return new ConversionResult(outputFile, issues.Count == 0, issues);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    public static string GetOutputFile(ConversionSettings settings)
    {
        var baseName = Path.GetFileNameWithoutExtension(settings.InputFile);
        var suffix = settings.PatchOnly ? "_roadtrip_patch.scs" : "_roadtrip.scs";
        return Path.Combine(settings.OutputDirectory, baseName + suffix);
    }

    /// <summary>
    /// Warns when the same input has already been converted into the output folder under a
    /// <b>different</b> brand token.
    /// <para>
    /// The two dealer-branding options are both on by default and are not independent: "keep the
    /// mod's own brand" and the typed Dealer ID are alternatives, not a pair. Converting one mod
    /// twice under two tokens therefore produces two archives, each shipping its own
    /// <c>material/ui/car_brand_logo/&lt;token&gt;.*</c>. The game resolves a dealer badge by file
    /// name, so both load and each one silently replaces a different base game's badge - usually not
    /// the one that was meant.
    /// </para>
    /// <para>
    /// Detection is by reading the brand token back out of the <c>car_dealer</c> folder the previous
    /// archive declares, rather than by remembering conversions in settings. Settings are per-user and
    /// get cleared; the archive in the output folder is the artefact that actually proves a conversion
    /// happened, and it is what would really conflict.
    /// </para>
    /// <para>
    /// A warning and never a refusal. Two brands can be deliberate, and the cost of being wrong in the
    /// blocking direction - silently refusing a conversion someone wanted - is much higher than the
    /// cost of an extra warning line.
    /// </para>
    /// </summary>
    private static void WarnIfAlreadyConvertedUnderAnotherBrand(
        ConversionSettings settings,
        string dealerId,
        Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(settings.OutputDirectory) ||
            !Directory.Exists(settings.OutputDirectory))
            return;

        var baseName = Path.GetFileNameWithoutExtension(settings.InputFile);
        if (string.IsNullOrWhiteSpace(baseName))
            return;

        foreach (var existing in Directory.EnumerateFiles(settings.OutputDirectory, "*.scs"))
        {
            // Only this mod's own previous output, not somebody else's archive in the same folder.
            var name = Path.GetFileNameWithoutExtension(existing);
            if (!name.StartsWith(baseName, StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith("_roadtrip", StringComparison.OrdinalIgnoreCase))
                continue;

            var previous = ReadBrandTokens(existing);
            if (previous.Count == 0)
                continue;

            foreach (var token in previous)
            {
                // Same token: this is a re-run, which is the normal case and worth nothing.
                if (token.Equals(dealerId, StringComparison.OrdinalIgnoreCase))
                    continue;

                log($"[WARNING] '{baseName}' was already converted in this folder under brand " +
                    $"'{token}', and this run uses '{dealerId}'.");
                log($"          Both archives will be installed, and each one's " +
                    $"material/ui/car_brand_logo/{token}.* overrides a different base-game badge.");
                log("          If that was not intended, delete the older archive or convert into a " +
                    "different output folder.");
                return;
            }
        }
    }

    /// <summary>
    /// Reads the distinct brand tokens a converted archive declares, taken from its
    /// <c>def/vehicle/car_dealer/</c> folder names.
    /// <para>
    /// That folder is the ground truth for which dealer a car is listed under: the assignment is done
    /// by renaming the car's unit to <c>vehicle.&lt;brand&gt;.&lt;model&gt;</c> and writing the dealer
    /// definition beneath the matching <c>car_dealer/&lt;brand&gt;/</c>, so the folder name and the badge
    /// token are the same string by construction.
    /// </para>
    /// <para>
    /// Returns an empty list rather than throwing on anything unreadable. A warning that cannot be
    /// produced must not be the reason a conversion fails.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> ReadBrandTokens(string archivePath)
    {
        const string dealerPrefix = "def/vehicle/car_dealer/";
        var tokens = new List<string>();

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);

            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(dealerPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var rest = name[dealerPrefix.Length..];
                if (rest.Length == 0)
                    continue;

                var token = rest.Split('/')[0];
                if (token.Length == 0)
                    continue;

                if (!tokens.Contains(token, StringComparer.OrdinalIgnoreCase))
                    tokens.Add(token);
            }
        }
        catch
        {
            // Unreadable, locked or not an archive at all. Nothing to report.
        }

        return tokens;
    }

    /// <summary>
    /// Builds a definitions-only mod meant to be loaded above the untouched original.
    /// Models, textures and sounds are copied to the patch with converted paths, and the
    /// original truck dealer entries are overridden with empty units.
    /// </summary>
    private static void BuildPatch(
        string root,
        string patchRoot,
        string displayName,
        Action<string> log)
    {
        var defSource = Path.Combine(root, "def");
        var defTarget = Path.Combine(patchRoot, "def");
        Directory.CreateDirectory(defTarget);

        // Copy all converted definition files - don't skip unchanged ones
        // to ensure compatibility when multiple mods are patched
        foreach (var file in Directory.EnumerateFiles(defSource, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(defTarget, Path.GetRelativePath(defSource, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        log($"[PATCH] Copied all definition files to patch");

        // A definitions-only patch ships def/ and vehicle/ but nothing under material/, so a
        // dealer logo written by CopyBrandLogoForDealer would be silently dropped here and the
        // converted dealer would end up with no logo. The logo has to travel with the patch.
        CopyBrandLogoTree(root, patchRoot, log);

        // Copy vehicle assets from vehicle/truck to vehicle/car
        var vehicleSource = Path.Combine(root, "vehicle");
        var vehicleTarget = Path.Combine(patchRoot, "vehicle");
        var truckSource = Path.Combine(vehicleSource, "truck");
        var carTarget = Path.Combine(vehicleTarget, "car");

        if (Directory.Exists(truckSource))
        {
            Directory.CreateDirectory(carTarget);
            foreach (var sourceDir in Directory.GetDirectories(truckSource))
            {
                var dirName = Path.GetFileName(sourceDir);
                var targetDir = Path.Combine(carTarget, dirName);
                Directory.CreateDirectory(targetDir);

                foreach (var sourceFile in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(sourceDir, sourceFile);
                    var targetFile = Path.Combine(targetDir, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                    File.Copy(sourceFile, targetFile);
                }
                log($"[PATCH] Copied vehicle/truck/{dirName} to vehicle/car/{dirName}");
            }
        }

        // NOTE: this used to write a 13-byte `SiiNunit\n{\n}\n` stub over every one of the original
    // mod's def/vehicle/truck_dealer/<brand>/*.sii files. Proven against the real patch output: 13 bytes
    // replacing 2,343 bytes that defined ~20 units - the car's whole accessory list (chassis, cabin,
    // engine, transmission, wheels, paint, mirrors, steering_w).
    //
    // That is not "neutralising" a definition, it is deleting one. SCS unit names are global and a later
    // mod's definition of the same name REPLACES the earlier one, so the patch shipped an empty file at
    // the original's path, the car stopped existing as far as the engine was concerned, and every save
    // that had driven it failed to load with `invalid_vehicle`. The stub was intended to stop the car
    // appearing in the TRUCK dealer as well as the new CAR dealer, but deleting the definition achieves
    // that by breaking the car entirely.
    //
    // The correct behaviour for a definitions-only patch is to add to the original mod and never modify
    // it. The car is already re-registered under def/vehicle/car_dealer/<brand>/ by
    // ConvertExistingDealers, which is what puts it in the dealership; the truck_dealer entry is left
    // alone and simply remains the truck-era listing it always was.

    foreach (var name in new[] { "manifest.sii", "roadtrip_conversion_report.txt" })
        {
            var source = Path.Combine(root, name);
            if (File.Exists(source))
                File.Copy(source, Path.Combine(patchRoot, name));
        }

        var manifest = Path.Combine(patchRoot, "manifest.sii");
        if (!File.Exists(manifest))
        {
            File.WriteAllText(
                manifest,
                "SiiNunit\n{\nmod_package : .package_name\n{\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"" +
                displayName + " (Road Trip patch)\"\n}\n}\n",
                new UTF8Encoding(false));
        }
        else
        {
            var text = DecodeText(File.ReadAllBytes(manifest));
            text = Regex.Replace(
                text,
                @"(display_name\s*:\s*"")([^""]*)("")",
                "$1$2 (Road Trip patch)$3");
            text = Regex.Replace(text, @"(?m)^\s*icon\s*:.*$\r?\n?", "");
            text = CopyManifestDescription(text, root, patchRoot);
            File.WriteAllText(manifest, text, new UTF8Encoding(false));
        }
    }

    private static string CopyManifestDescription(string manifestText, string root, string patchRoot)
    {
        var match = Regex.Match(manifestText, @"(?m)^\s*description_file\s*:\s*""([^""]*)"".*$\r?\n?");
        if (!match.Success)
            return manifestText;

        var relative = match.Groups[1].Value.TrimStart('/', '\\');
        var source = Path.GetFullPath(Path.Combine(root, relative));
        var target = Path.GetFullPath(Path.Combine(patchRoot, relative));
        if (relative.Length > 0 &&
            source.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) &&
            target.StartsWith(Path.GetFullPath(patchRoot), StringComparison.OrdinalIgnoreCase) &&
            File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
            return manifestText;
        }

        return manifestText.Remove(match.Index, match.Length);
    }

    private static List<string> ReadAllTextDefinitionPaths(
        string root,
        ConversionStats stats)
    {
        var result = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(IsTextDefinition))
        {
            stats.TextFilesScanned++;

            try
            {
                var text = DecodeText(File.ReadAllBytes(file));
                result.Add(text);
            }
            catch
            {
                stats.UnreadableDefinitions++;
            }
        }

        return result;
    }

    private static void ConvertDefinitionFiles(
        string root,
        ConversionStats stats,
        Action<string> log,
        bool moveVehicleAssets,
        string vehicleType,
        bool isPatchMode = false,
        bool mapCamerasToCarUnits = true,
        string? anonymousNamespace = null)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(IsTextDefinition))
        {
            ConvertDefinitionFile(file, stats, log, moveVehicleAssets, vehicleType, isPatchMode, mapCamerasToCarUnits, anonymousNamespace);
        }
    }

    private static void ConvertDefinitionFile(
        string file,
        ConversionStats stats,
        Action<string> log,
        bool moveVehicleAssets,
        string vehicleType,
        bool isPatchMode = false,
        bool mapCamerasToCarUnits = true,
        string? anonymousNamespace = null)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(file);
        }
        catch
        {
            stats.UnreadableDefinitions++;
            return;
        }

        if (IsBinaryOrEncryptedSii(bytes))
        {
            stats.BinaryDefinitionsSkipped++;
            log($"[WARNING] Binary/encrypted definition left unchanged: {GetRelativePathForLog(file)}");
            return;
        }

        string text;
        try
        {
            text = DecodeText(bytes);
        }
        catch
        {
            stats.UnreadableDefinitions++;
            return;
        }

        var original = text;
        var isVehicleDefinitionFile =
            file.Contains(
                Path.Combine("def", "vehicle") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

        // Exact definition-path conversion is always required.
        text = Regex.Replace(
            text,
            @"(?i)(/?)def/vehicle/truck(?=/|\\|\s|""|$)",
            "$1def/vehicle/car");

        // Physical vehicle assets are moved in full conversion mode.
        // In patch mode, we still convert the paths because we copy assets to the patch.
        if (moveVehicleAssets || isPatchMode)
        {
            text = Regex.Replace(
                text,
                @"(?i)(/?)vehicle/truck(?=/|\\|\s|""|$)",
                "$1vehicle/car");
        }

        var normalizedVehicleType = (vehicleType ?? "pickup").Trim().ToLowerInvariant();

        // Road Trip ships generic camera.*.car units in the base game
        // (def/camera/units/*_car.sui), so truck-era mods can't reuse their own
        // bespoke names when loaded as a car: behind/wheel/top/bumper/cabin,
        // window/interior (+oculus) are remapped to those stable units.
        if (mapCamerasToCarUnits && isVehicleDefinitionFile && IsCarDataFile(file))
            text = MapCamerasToCarUnits(text);

        if (isVehicleDefinitionFile && IsCarDataFile(file))
            text = AddCarDataAttributes(text, normalizedVehicleType);

        if (isVehicleDefinitionFile && IsCarInteriorFile(file))
            text = AddSpeedLimiterValue(text, normalizedVehicleType);

        // Global anonymous units are namespaced last, so it also catches any name that a
        // definition was expected to introduce.
        if (anonymousNamespace != null)
        {
            var renamed = new List<string>();
            text = NamespaceAnonymousUnits(text, anonymousNamespace, renamed.Add);
            if (renamed.Count > 0)
            {
                stats.AnonymousUnitsNamespaced += renamed.Count;
                foreach (var name in renamed.Distinct())
                    stats.AnonymousUnitNames.Add(name);
            }
        }

        if (!string.Equals(text, original, StringComparison.Ordinal))
        {
            File.WriteAllText(file, text, new UTF8Encoding(false));
            stats.TextFilesEdited++;
            log($"[EDITED] {GetRelativePathForLog(file)}");
        }
        else
        {
            stats.TextFilesUnchanged++;
        }
    }

    /// <summary>
    /// Rewrites global anonymous units (<c>_nameless.x</c>) so they carry a per-mod namespace.
    /// <para>
    /// In SCS a <c>_nameless.</c> name is global: two mods defining <c>_nameless._.speed</c>
    /// define the same unit, so the second one replaces the first. Converted mods inherited
    /// those names verbatim from their source mod, which is why two different converted cars
    /// (a Cadillac CT5-V and a Cadillac Escalade, for example) shipped identical dashboard and
    /// camera unit names and one silently overwrote the other in game.
    /// </para>
    /// <para>
    /// Every occurrence is rewritten consistently, so references between the mod's own files
    /// still resolve. Only this mod's own definitions are touched; a file is rewritten in one
    /// pass, so a name is never namespaced twice.
    /// </para>
    /// </summary>
    private static string NamespaceAnonymousUnits(string text, string ns, Action<string>? onRename = null)
    {
        if (ns.Length == 0)
            return text;

        // Guard the 12-character component limit rather than trusting the caller. This is the
        // difference between a patch that converts cleanly and a patch that only fails in game
        // with "invalid_vehicle", so it is enforced where the names are actually written.
        if (ns.Length > MaxUnitNameComponentLength)
            throw new InvalidOperationException(
                $"Anonymous-unit namespace '{ns}' is {ns.Length} characters; SCS unit name " +
                $"components may not exceed {MaxUnitNameComponentLength}.");

        return Regex.Replace(
            text,
            @"(?<![A-Za-z0-9_])_nameless\.(?![A-Za-z0-9_]*_nameless\.)",
            match =>
            {
                onRename?.Invoke(match.Value);
                return "_nameless." + ns + ".";
            });
    }

    /// <summary>
    /// A namespace that is unique to this conversion, used to rewrite global anonymous units.
    /// <para>
    /// It must be derived from the <em>input file</em>, not from the dealer ID. Those used to be
    /// treated as interchangeable on the assumption that one dealer equals one vehicle, and that
    /// assumption is false in both directions:
    /// </para>
    /// <list type="bullet">
    /// <item>Two different mods share a brand. The two Cadillac mods in the common download set
    /// are both <c>truck_dealer/cadillac</c>, and the multi-brand Courier mod declares eight
    /// brands at once. When the dealer ID is taken from the mod's own brand, all of those
    /// collapse onto one namespace and their cars overwrite each other again - which is the
    /// exact bug this function exists to prevent.</item>
    /// <item>Conversely, a user can pick the same dealer ID for two mods by hand, or rely on the
    /// suggested ID, and reintroduce the collision that way.</item>
    /// </list>
    /// <para>
    /// The file name is therefore the only input that is guaranteed distinct per conversion, so
    /// it is the basis. It is trimmed of any version suffix a mod author appends
    /// (<c>Volvo S90 2020 V2.3 1.60.scs</c> and <c>... 1.61.scs</c> are the same vehicle), which
    /// keeps the namespace stable when a mod is updated to a new game version rather than
    /// changing on every patch.
    /// </para>
    /// <para>
    /// <b>The result is capped at 12 characters</b>, and that is not cosmetic. SCS unit names are
    /// dot-separated components of <em>at most 12 characters</em> each (the modding wiki is
    /// explicit: "correct unit name will be <c>vehicle.dummy.truck</c>", and the dashboard guide
    /// repeats "it must be in SCS name specification - 12 symbols length"). The file names of
    /// real mods blow straight past that: <c>cadillac_ct5_v_black_wing_2022</c> is 30 characters,
    /// <c>ford_f_150_raptor_2017_v1_7_1_beta</c> is 34. Emitting those produced units the engine
    /// could not resolve, and the game reported <c>invalid_vehicle</c> when loading a save that
    /// referenced one - the mod appeared to convert cleanly and only failed in game.
    /// </para>
    /// <para>
    /// So the readable part is truncated and a short hash of the <em>full</em> basis is appended.
    /// Truncation alone would collide (<c>cadillac_ct5_v_black_wing_2022</c> and
    /// <c>cadillac_escalade_2021</c> share their first 12 characters); the hash is what preserves
    /// uniqueness, and it is computed from the untruncated string so no information is lost that
    /// the collision check depends on.
    /// </para>
    /// </summary>
    internal static string BuildAnonymousNamespace(string dealerId, string inputFile)
    {
        var basis = StripVersionSuffix(SanitizeId(Path.GetFileNameWithoutExtension(inputFile)));

        // SanitizeId answers "custom" rather than an empty string, so a second pass is needed
        // to tell "the user named it custom" from "nothing usable was supplied at all".
        if (basis == "custom")
            basis = SanitizeId(dealerId);
        if (basis == "custom")
            basis = "patch";

        // Lower case, because SCS unit names are case-sensitive and this becomes part of
        // every anonymous unit name in the mod.
        return ShortenNamespace(basis.ToLowerInvariant());
    }

    /// <summary>Maximum length of one component of an SCS unit name.</summary>
    internal const int MaxUnitNameComponentLength = 12;

    /// <summary>
    /// Caps a namespace at <see cref="MaxUnitNameComponentLength"/> characters, keeping it
    /// readable where possible and appending a short hash when truncation happens.
    /// <para>
    /// A namespace short enough already is returned untouched, so <c>ford_f250</c> stays
    /// <c>ford_f250</c> and existing patches keep their unit names.
    /// </para>
    /// </summary>
    private static string ShortenNamespace(string basis)
    {
        if (basis.Length <= MaxUnitNameComponentLength)
            return basis;

        // 4 hash characters leaves 8 readable ones. The hash is over the full basis, not the
        // truncated prefix, so two names sharing a prefix still differ.
        const int hashLength = 4;
        var keep = MaxUnitNameComponentLength - hashLength - 1; // -1 for the '_' join
        var prefix = basis[..Math.Min(keep, basis.Length)].TrimEnd('_');

        // A prefix of all separators would leave a bare hash, so fall back to the hash alone.
        if (prefix.Length == 0)
            return ShortHash(basis, MaxUnitNameComponentLength);

        return prefix + "_" + ShortHash(basis, hashLength);
    }

    /// <summary>
    /// A short, stable, lowercase alphanumeric hash of <paramref name="value"/>.
    /// <para>
    /// Uses FNV-1a rather than <see cref="string.GetHashCode()"/>, which is deliberately
    /// randomised per process in .NET and would therefore produce a different namespace on every
    /// run - breaking every previously patched mod each time the tool was launched.
    /// </para>
    /// </summary>
    private static string ShortHash(string value, int length)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= prime;
        }

        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        var result = new char[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = alphabet[(int)(hash % (uint)alphabet.Length)];
            hash /= (uint)alphabet.Length;
        }

        return new string(result);
    }

    /// <summary>
    /// Drops the trailing "version" part mods append to their file names, so the same vehicle
    /// keeps one namespace across updates. "Volvo S90 2020 V2.3 1.60" becomes
    /// "Volvo S90 2020"; "Cadillac CT5-V Black Wing 2022 V2.2 1.60" becomes
    /// "Cadillac CT5-V Black Wing 2022".
    /// <para>
    /// The separator class includes <c>_</c> as well as <c>.</c> because this runs *after*
    /// <see cref="SanitizeId"/>, which has already collapsed every run of punctuation into a
    /// single underscore - by this point "V2.3 1.60" is "V2_3_1_60", and a dot-only pattern
    /// silently matches nothing and strips nothing.
    /// </para>
    /// <para>
    /// A trailing qualifier that is not a version ("... Beta") is deliberately left in place, so
    /// a beta and a full release of the same car get different namespaces. That is the safe
    /// direction: they are only ever installed together by mistake, and distinct names are what
    /// stops one silently replacing the other.
    /// </para>
    /// </summary>
    private static string StripVersionSuffix(string name)
    {
        // Match a trailing " V<digits>(.<digits>)+" plus an optional following version group.
        var trimmed = Regex.Replace(
            name,
            @"[ _]+v\d+(?:[._]\d+)+(?:[ _]+\d+(?:[._]\d+)*)?$",
            "",
            RegexOptions.IgnoreCase);

        return trimmed.Trim().TrimEnd('_', ' ', '-');
    }

    private static void ConvertExistingDealers(
        string root,
        string dealerId,
        bool moveAssets,
        ConversionStats stats,
        Action<string> log,
        bool isPatchMode = false)
    {
        var vehicleRoot = Path.Combine(root, "def", "vehicle");
        var oldDealerRoot = Path.Combine(vehicleRoot, "truck_dealer");
        var newDealerRoot = Path.Combine(vehicleRoot, "car_dealer");

        if (!Directory.Exists(oldDealerRoot))
        {
            // Nothing to translate.
            return;
        }

        Directory.CreateDirectory(newDealerRoot);
        var targetBrandRoot = Path.Combine(newDealerRoot, dealerId);
        Directory.CreateDirectory(targetBrandRoot);

        var sourceFiles = Directory.EnumerateFiles(
            oldDealerRoot, "*.*", SearchOption.AllDirectories)
            .Where(IsTextDefinition)
            .ToList();

        if (sourceFiles.Count == 0)
        {
            log("[WARNING] truck_dealer exists, but no .sii/.sui files were found.");
            return;
        }

        foreach (var source in sourceFiles)
        {
            var fileName = Path.GetFileName(source);
            var destination = Path.Combine(targetBrandRoot, fileName);

            // Make a translated copy rather than deleting the original until after
            // every dealer source file has been successfully read.
            var text = DecodeText(File.ReadAllBytes(source));

            text = Regex.Replace(
                text,
                @"(?i)(/?)def/vehicle/truck(?=/|\\|\s|""|$)",
                "$1def/vehicle/car");
            if (moveAssets || isPatchMode)
            {
                text = Regex.Replace(
                    text,
                    @"(?i)(/?)vehicle/truck(?=/|\\|\s|""|$)",
                    "$1vehicle/car");
            }


            File.WriteAllText(destination, text, new UTF8Encoding(false));
            stats.DealerFilesConverted++;
            log($"[DEALER] {dealerId}/{fileName}");
        }

        Directory.Delete(oldDealerRoot, true);
    }

    /// <summary>
    /// Rewrites a converted car's defining unit so ATS files it under the chosen dealer.
    /// <para>
    /// A base-game car declares its dealership in the unit name itself, not in a
    /// <c>brand</c> attribute. The Road Trip DLC's F-150 is
    /// <c>accessory_truck_data : vehicle.ford.f150_23</c>, lives in
    /// <c>def/vehicle/car/ford.f150_23/</c> and its dealer file is
    /// <c>car_dealer/ford/ford_f150_23.sii</c>: the middle component is the brand, and the
    /// dealer folder must match it.
    /// </para>
    /// <para>
    /// Truck-era mods name their unit <c>vehicle.vols90</c>, with no brand component at all,
    /// so the game cannot place the car and falls back to whichever dealership claims the
    /// unmatched definition. That is why a converted Volvo appeared under the BMW dealer.
    /// </para>
    /// </summary>
    internal static string ApplyDealerBrand(string text, string dealerId, string modelId)
    {
        var brand = SanitizeId(dealerId);
        var model = SanitizeId(modelId);
        if (brand.Length == 0 || model.Length == 0)
            return text;

        var replacement = "vehicle." + brand + "." + model;

        // "accessory_truck_data : vehicle.vols90" -> "accessory_truck_data : vehicle.volvo.vols90"
        var declared = Regex.Replace(
            text,
            @"(?im)^(?<indent>[ \t]*accessory_truck_data[ \t]*:[ \t]*)vehicle\.[A-Za-z0-9_.]+",
            match => match.Groups["indent"].Value + replacement);

        // Any other reference to the old unit elsewhere in the mod's own definitions.
        declared = Regex.Replace(
            declared,
            @"(?<![A-Za-z0-9_.])vehicle\." + Regex.Escape(model) + @"(?![A-Za-z0-9_])",
            replacement);

        return declared;
    }

/// <summary>
    /// Rewrites the converted car's defining unit so ATS files it under the chosen dealer.
    /// <para>
    /// A base-game car declares its dealership in the unit name itself, not in a
    /// <c>brand</c> attribute. The Road Trip DLC's F-150 is
    /// <c>accessory_truck_data : vehicle.ford.f150_23</c>, sold by <c>car_dealer/ford</c>:
    /// the middle component is the brand, and the dealer folder must match it.
    /// </para>
    /// <para>
    /// Truck-era mods name their unit <c>vehicle.vols90</c>, with no brand component at all,
    /// so the game has no dealership to match and lists the car under an unrelated brand -
    /// which is exactly why a converted Volvo S90 turned up in the BMW dealer.
    /// </para>
    /// <para>
    /// Only the unit name is rewritten, across every definition file in the mod so the
    /// declaration and all references to it stay in step. The definition folder is left
    /// alone on purpose: a mod's own files address it by path
    /// (<c>def/vehicle/car/vols90/...</c>), and renaming the folder without rewriting every
    /// one of those references leaves the mod pointing at files that are no longer there,
    /// which crashes the game when a save loads. The unit name is what carries the brand,
    /// so it is the only part that has to change.
    /// </para>
    /// </summary>
    private static void AssignDealerBrand(
        string root,
        string dealerId,
        IReadOnlyCollection<string> vehicleFolders,
        ConversionStats stats,
        Action<string> log)
    {
        var brand = SanitizeId(dealerId);
        var carRoot = Path.Combine(root, "def", "vehicle", "car");
        if (brand.Length == 0 || !Directory.Exists(carRoot))
            return;

        // Only the vehicles this conversion migrated. Another mod's car folder sharing the
        // tree is out of scope, exactly as it is for MergeTree.
        var targets = vehicleFolders.Count > 0
            ? vehicleFolders.ToList()
            : Directory.GetDirectories(carRoot)
                .Select(d => Path.GetFileName(d)!)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

        var models = new List<string>();
        foreach (var folder in targets)
        {
            if (!Directory.Exists(Path.Combine(carRoot, folder)))
                continue;

            // "m5.g90" keeps its model half; "vols90" has no brand half yet.
            var model = folder.Contains('.')
                ? folder[(folder.IndexOf('.') + 1)..]
                : folder;

            if (SanitizeId(model) is { Length: > 0 } sanitized && !models.Contains(sanitized))
                models.Add(sanitized);
        }

        if (models.Count == 0)
            return;

        // Already-prefixed units are skipped, which makes this idempotent: converting the
        // same mod twice does not stack dealer names.
        var pattern = new Regex(
            @"(?<![A-Za-z0-9_.])vehicle\.(?!" + Regex.Escape(brand) + @"\.)(?<model>" +
            string.Join("|", models.Select(Regex.Escape)) +
            @")(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase);

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(IsTextDefinition))
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); }
            catch { continue; }

            if (IsBinaryOrEncryptedSii(bytes))
                continue;

            string text;
            try { text = DecodeText(bytes); }
            catch { continue; }

            if (!pattern.IsMatch(text))
                continue;

            var updated = pattern.Replace(
                text,
                m => "vehicle." + brand + "." + m.Groups["model"].Value);

            File.WriteAllText(file, updated, new UTF8Encoding(false));
            log($"[EDITED] {GetRelativePathForLog(file)}");
        }

        stats.CarsBrandscoped += models.Count;

        log($"[BRAND] Assigned {models.Count} car(s) to dealer '{dealerId}': " +
            string.Join(", ", models.Select(m => "vehicle." + brand + "." + m)));
    }

    private static void CopyMissingReferenceFramework(
        string referenceRoot,
        string targetRoot,
        Action<string> log,
        ConversionStats stats)
    {
        var referenceCarDealer = Path.Combine(
            referenceRoot, "def", "vehicle", "car_dealer");

        if (!Directory.Exists(referenceCarDealer))
            return;

        var targetCarDealer = Path.Combine(
            targetRoot, "def", "vehicle", "car_dealer");

        foreach (var sourceFile in Directory.EnumerateFiles(
                     referenceCarDealer, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(referenceCarDealer, sourceFile);
            var targetFile = Path.Combine(targetCarDealer, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

            if (!File.Exists(targetFile))
            {
                File.Copy(sourceFile, targetFile);
                stats.DealerFilesCreatedFromReference++;
                log($"[REFERENCE] Added {Path.Combine("def", "vehicle", "car_dealer", relative)}");
            }
        }
    }

    private static List<string> GetDealerFiles(string root, string dealerId)
    {
        var folder = Path.Combine(
            root, "def", "vehicle", "car_dealer", dealerId);

        if (!Directory.Exists(folder))
            return new List<string>();

        return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(IsTextDefinition)
            .ToList();
    }

    /// <summary>
    /// Works out which brand the mod was built around, or <c>null</c> if it shipped no
    /// truck dealer.
    /// <para>
    /// The folder under <c>def/vehicle/truck_dealer/</c> is the only reliable source: ATS has no
    /// <c>brand</c> attribute to read, and a truck-era mod's brand never appears in its unit
    /// names. A mod that defines more than one truck brand would be ambiguous, so the one that
    /// also ships a matching <c>brand_logo</c> wins; failing that, the first by name, which keeps
    /// the choice deterministic rather than dependent on directory-listing order.
    /// </para>
    /// </summary>
    internal static string? DiscoverSourceBrandToken(string root, Action<string>? log = null)
    {
        var dealerRoot = Path.Combine(root, "def", "vehicle", "truck_dealer");
        if (!Directory.Exists(dealerRoot))
        {
            log?.Invoke("[INFO] No def/vehicle/truck_dealer folder; this mod has no brand of its own.");
            return null;
        }

        var brands = Directory.GetDirectories(dealerRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (brands.Count == 0)
            return null;

        var chosen = brands.FirstOrDefault(brand => HasBrandLogo(root, brand)) ?? brands[0];

        if (brands.Count > 1)
            log?.Invoke($"[INFO] Mod defines {brands.Count} truck brands ({string.Join(", ", brands)}); " +
                        $"using '{chosen}'.");

        return SanitizeId(chosen);
    }

    /// <summary>
    /// Whether the mod ships the dealer's logo material, named the way the game looks it up.
    /// </summary>
    internal static bool HasBrandLogo(string root, string brand) =>
    FindBrandLogoMaterial(root, brand) != null;

    /// <summary>
    /// Whether a file has real content, as opposed to existing but being empty or zero-filled.
    /// <para>
    /// This guards a real failure mode rather than a hypothetical one: a mod author can reserve
    /// <c>brand_logo/&lt;brand&gt;.mat</c> as a placeholder and never fill it in, and an empty
    /// file is indistinguishable from a working one by name alone. Copying an empty logo
    /// produces a correctly named, correctly placed badge that renders as nothing, which looks
    /// exactly like the dealer binding being wrong.
    /// </para>
    /// <para>
    /// Verified against the real Volvo S90 mod that its logo files are <em>not</em> empty
    /// (<c>volvo_cars.mat</c> 75 non-zero bytes of 75, <c>.tobj</c> 51 of 86, <c>.dds</c> 1777 of
    /// 5608). An earlier reading of that mod through <c>tar</c> showed all-zero files, but
    /// <c>tar</c> silently mis-decodes this archive's nonstandard ZIP metadata - the same
    /// problem <see cref="ScsArchive"/> exists to work around. Files must be read with the
    /// project extractor, never with a generic ZIP tool, or their contents cannot be trusted.
    /// </para>
    /// </summary>
    private static bool HasRealContent(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
                return false;

            // Read the first block only. A zero-filled file is all zeros from byte zero, and a
            // real material/tobj starts with printable text within the first few bytes, so this
            // is enough to tell them apart without pulling a whole texture into memory.
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(256, info.Length)];
            var read = stream.Read(buffer, 0, buffer.Length);
            return Array.Exists(buffer.Take(read).ToArray(), b => b != 0);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Full path of a <c>.mat</c> the car dealership could use for <paramref name="brand"/>, or
    /// <c>null</c> if the mod has no usable logo.
    /// <para>
    /// Two folders matter and they are not interchangeable. ATS has a <em>car</em> shop and a
    /// truck dealer, and each resolves its badge from its own folder — confirmed from the game's
    /// own log:
    /// <code>
    /// [car shop] Found logo of brand: dodge
    /// &lt;ERROR&gt; [car shop] No logo found for brand 'volvo_cars'!
    /// &lt;ERROR&gt; [resource_task] Can not open '/material/ui/car_brand_logo/volvo_cars.mat'
    /// </code>
    /// A truck-era mod ships its logo in <c>material/ui/brand_logo/</c>, which is correct while it
    /// stays a truck and useless once it is sold as a car. <see cref="EnsureCarBrandLogo"/> is what
    /// bridges the two.
    /// </para>
    /// </summary>
    private static string? FindBrandLogoMaterial(string root, string brand)
    {
        if (string.IsNullOrWhiteSpace(brand))
            return null;

        var name = SanitizeId(brand) + ".mat";

        // Prefer a car-shop logo if one is already present, so a re-run does not overwrite it
        // with the truck-era copy.
        foreach (var folder in new[] { CarBrandLogoFolder, TruckBrandLogoFolder })
        {
            var candidate = Path.Combine(root, folder, name);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Where the <em>car</em> shop looks for a dealer's badge. Confirmed from the game's log:
    /// <c>Can not open '/material/ui/car_brand_logo/volvo_cars.mat'</c>.
    /// </summary>
    private const string CarBrandLogoFolder = "material/ui/car_brand_logo";

    /// <summary>
    /// Where the <em>truck</em> dealer looks, and where truck-era mods put their logo. Not
    /// interchangeable with <see cref="CarBrandLogoFolder"/> — that difference is the whole bug.
    /// </summary>
    private const string TruckBrandLogoFolder = "material/ui/brand_logo";

    /// <summary>
    /// Copies <c>material/ui/brand_logo</c> from the converted mod into a patch archive.
    /// <para>
    /// Only that one folder is carried over. A full copy of the mod's material tree would drag
    /// every texture in it into a patch that is otherwise a few hundred kilobytes, which defeats
    /// the point of the patch mode. The logo files are small and are the only ones whose
    /// <em>name</em> the game resolves, so they are the ones that have to be present.
    /// </para>
    /// </summary>
    private static void CopyBrandLogoTree(string root, string patchRoot, Action<string> log)
    {
        // Both folders have to travel. car_brand_logo/ is what the car shop reads; brand_logo/ is
        // still needed because the copied .tobj holds an absolute path back into it.
        foreach (var folder in new[] { CarBrandLogoFolder, TruckBrandLogoFolder })
        {
            var source = Path.Combine(root, folder);
            if (!Directory.Exists(source))
                continue;

            var copied = 0;

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                // Empty or zero-filled logo files are not carried into the patch. A patch is loaded
                // above the original mod and wins, so shipping an empty brand_logo file would put a
                // blank badge over the original mod's working one - strictly worse than shipping
                // nothing and letting the original show through.
                if (!HasRealContent(file))
                    continue;

                var target = Path.Combine(patchRoot, folder, Path.GetRelativePath(source, file));

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
                copied++;
            }

            if (copied > 0)
                log($"[PATCH] Copied {copied} dealer logo file(s) to patch from {folder}.");
        }
    }

    /// <summary>
    /// Makes the dealer's logo reachable by the car shop, which reads a different folder from
    /// the truck dealer.
    /// <para>
    /// This runs for <em>every</em> conversion, including one that keeps the mod's own brand.
    /// That was the actual bug: keeping the brand was assumed to be enough because the logo file
    /// was already in the archive, but it was in <c>material/ui/brand_logo/</c> and the car shop
    /// only ever looks in <c>material/ui/car_brand_logo/</c>. The mod looked complete and correct;
    /// it was simply filed under the wrong shop.
    /// </para>
    /// <para>
    /// All three files are copied, not just the <c>.mat</c>. The <c>.mat</c> names its
    /// <c>.tobj</c> by bare file name and resolves it beside itself, so the <c>.tobj</c> has to
    /// travel too. The <c>.tobj</c> in turn holds an <em>absolute</em> path to the <c>.dds</c>,
    /// which is why the original <c>brand_logo/</c> copy is deliberately left in place rather than
    /// moved — that path still points back into it.
    /// </para>
    /// </summary>
    private static void EnsureCarBrandLogo(
        string root,
        string? sourceToken,
        string dealerId,
        bool enabled,
        ConversionStats stats,
        Action<string> log,
        bool resampleSquashedLogos = false,
        bool greyscaleLogos = false)
    {
        if (sourceToken == null)
            return;

        // Keeping the mod's own brand is not an opt-in nicety: that is the case the bug was in,
        // so it always runs. Renaming the dealer *is* opt-in, and the "add the logo" switch owns
        // it - otherwise turning the switch off would still leave a car-shop logo behind, which is
        // exactly what the switch promises it will not do.
        var isRename = !sourceToken.Equals(dealerId, StringComparison.OrdinalIgnoreCase);
        if (isRename && !enabled)
            return;

        var sourceMaterial = FindBrandLogoMaterial(root, sourceToken);
        if (sourceMaterial == null)
            return;

        if (!HasRealContent(sourceMaterial))
            return;

        var sourceFolder = Path.GetDirectoryName(sourceMaterial)!;
        var targetFolder = Path.Combine(root, CarBrandLogoFolder);
        Directory.CreateDirectory(targetFolder);

        // The truck-era file must survive: the .tobj points at it by absolute path.
        var needed = new[] { ".mat", ".tobj", ".dds" };
        var copied = 0;

        foreach (var extension in needed)
        {
            var from = Path.Combine(sourceFolder, dealerId + extension);

            // A logo renamed to a new dealer only has files under the source brand's name, so
            // fall back to those. The .mat still points at the original .tobj by bare name, and
            // that .tobj still points at the original .dds by absolute path, so all three resolve.
            if (!File.Exists(from))
                from = Path.Combine(sourceFolder, sourceToken + extension);
            if (!File.Exists(from))
                continue;

            var to = Path.Combine(targetFolder, dealerId + extension);

            // FindBrandLogoMaterial prefers car_brand_logo, so a mod that already ships one makes
            // sourceFolder and targetFolder the same directory and this becomes
            // File.Copy(x, x, overwrite: true), which throws IOException on Windows and fails the
            // whole conversion. The file is already exactly where it needs to be, so skip it.
            //
            // The comparison goes through GetFullPath because the folder constants are written with
            // forward slashes ("material/ui/car_brand_logo"). Path.Combine keeps them verbatim on
            // Windows, so the same file reached by two different routes is spelled two different
            // ways and a plain string compare says they differ.
            if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
                continue;

            File.Copy(from, to, overwrite: true);
            copied++;
        }

        if (copied == 0)
        {
            // Either there was nothing to copy, or every file was already in place because the mod
            // already shipped car_brand_logo. The transparency pass still has to run in that case:
            // a pre-existing car-shop logo is exactly the opaque DXT1 case it exists to fix.
            MakeLogoBackgroundsTransparent(root, stats, log, resampleSquashedLogos, greyscaleLogos);
            return;
        }

        stats.BrandLogosAdded += copied;
        log($"[LOGO] Wrote {copied} file(s) to material/ui/car_brand_logo/{dealerId}.* so the car dealership " +
            "shows this brand's badge.");

        // The badge now resolves, but a truck-era logo is usually an opaque DXT1 image with a
        // black background baked in, and the card shop draws that black plate as a letterbox
        // around the artwork. Both folders are fixed because both are reachable: the .tobj holds
        // an absolute path back into brand_logo/, so leaving the truck copy opaque would keep the
        // box even after the car-shop copy was corrected.
        MakeLogoBackgroundsTransparent(root, stats, log, resampleSquashedLogos, greyscaleLogos);
    }

    /// <summary>
    /// Rewrites every logo texture in both dealer folders so its background is transparent.
    /// <para>
    /// This is cosmetic, so it is deliberately last and deliberately forgiving: a texture it
    /// cannot improve is reported and left exactly as it was, and one that already has an alpha
    /// channel is never re-encoded at all. See <see cref="BrandLogoAlpha"/>.
    /// </para>
    /// </summary>
    private static void MakeLogoBackgroundsTransparent(
        string root, ConversionStats stats, Action<string> log,
        bool resampleSquashedLogos = false, bool greyscaleLogos = false)
    {
        foreach (var folder in new[] { TruckBrandLogoFolder, CarBrandLogoFolder })
        {
            var directory = Path.Combine(root, folder);
            if (!Directory.Exists(directory))
                continue;

            foreach (var texture in Directory.EnumerateFiles(directory, "*.dds"))
            {
                if (BrandLogoAlpha.TryMakeTransparent(texture, out var detail))
                {
                    stats.BrandLogosMadeTransparent++;
                    log($"[LOGO] {folder}/{Path.GetFileName(texture)}: {detail} (DXT1 -> DXT5), so the badge " +
                        "has no black box around it.");
                }
                else if (detail.StartsWith("already", StringComparison.Ordinal))
                {
                    log($"[LOGO] {folder}/{Path.GetFileName(texture)}: {detail}.");
                }
                else if (detail.Contains("not a format"))
                {
                    log($"[INFO] {folder}/{Path.GetFileName(texture)} {detail}.");
                }
                else
                {
                    log($"[INFO] Left {folder}/{Path.GetFileName(texture)} alone: it {detail}.");
                }

                // ORDER MATTERS, and getting it wrong is what crashed the game.
                //
                // The unsquash runs BEFORE the crop. It corrects the artwork's own proportions, and the
                // crop's job is then to give that corrected artwork the stock canvas shape. Run the other
                // way round, the unsquash discarded the crop's measured canvas and replaced it with a bare
                // square, so the badge reached the game as neither the stock 1.97:1 shape nor anything the
                // crop had verified - a 127x127 image that was not even a whole number of 4x4 blocks.
                //
                // Both still need the background to be transparent first, so the transparency pass stays
                // where it is.
                if (resampleSquashedLogos)
                {
                    var didResample = BrandLogoResample.TryUnsquash(texture, out var resampleDetail);

                    // The pass's own detail already says the badge is no longer byte-identical, so the caller's line adds
            // only what it is doing and why - not a second copy of the same warning.
                    if (didResample)
                    {
                        stats.BrandLogosResampled++;
                        log($"[LOGO] {folder}/{Path.GetFileName(texture)}: {resampleDetail}.");
                    }
                    else if (!resampleDetail.Contains("left unchanged", StringComparison.Ordinal)
                             && !resampleDetail.StartsWith("already", StringComparison.Ordinal)
                             && !resampleDetail.Contains("too little", StringComparison.Ordinal)
                             && !resampleDetail.Contains("not a format", StringComparison.Ordinal)
                             && !resampleDetail.Contains("no visible", StringComparison.Ordinal))
                    {
                        log($"[INFO] {folder}/{Path.GetFileName(texture)} was not resampled: {resampleDetail}.");
                    }
                }

                // Cropped after the transparency pass, because the crop measures the opaque bounding box:
                // with the black still baked in, the bounds would be the whole canvas and there would be
                // nothing to trim. And after the unsquash, so the corrected artwork is given the stock
                // canvas shape rather than replacing it.
                if (BrandLogoCrop.TryCrop(texture, out var cropDetail))
                {
                    stats.BrandLogosCropped++;
                    log($"[LOGO] {folder}/{Path.GetFileName(texture)}: {cropDetail}, so the badge draws at " +
                        "the same size as the base game's.");
                }
                else if (!cropDetail.StartsWith("already", StringComparison.Ordinal)
                         && !cropDetail.Contains("left unchanged", StringComparison.Ordinal))
                {
                    log($"[INFO] {folder}/{Path.GetFileName(texture)} was not cropped: {cropDetail}.");
                }

                // Greyscale runs last: it only rewrites the three colour channels, so it is indifferent
                // to canvas shape and safe at any point after the background is transparent.
                if (greyscaleLogos && BrandLogoGreyscale.TryGreyscale(texture, out var greyDetail))
                {
                    stats.BrandLogosGreyscaled++;
                    log($"[LOGO] {folder}/{Path.GetFileName(texture)}: {greyDetail}.");
                }
            }
        }
    }

    /// <summary>
    /// Gives a renamed dealer the mod's own logo.
    /// <para>
    /// The game resolves a dealer's logo as <c>material/ui/brand_logo/&lt;dealer&gt;.mat</c>, so
    /// renaming the dealer silently loses it. The fix is a single extra file: the source
    /// <c>.mat</c> copied verbatim under the new brand name.
    /// </para>
    /// <para>
    /// Copying it unchanged is deliberate, and the chain is two links with *different* rules —
    /// confirmed by dumping the bytes of a real <c>.tobj</c>:
    /// <list type="bullet">
    /// <item><c>.mat</c> → <c>.tobj</c> is a <b>bare file name</b>
    /// (<c>texture : "volvo_cars.tobj"</c>), resolved next to the <c>.mat</c>.</item>
    /// <item><c>.tobj</c> → <c>.dds</c> is an <b>absolute path baked into the compiled object</b>,
    /// e.g. <c>/material/ui/brand_logo/volvo_cars.dds</c> — a length-prefixed string at a fixed
    /// offset near the end of the file.</item>
    /// </list>
    /// <para>
    /// So a copy works precisely because the mod still ships the original <c>.tobj</c> and
    /// <c>.dds</c> under their own names, and the new <c>.mat</c> points at them. Renaming the
    /// <c>.tobj</c> or <c>.dds</c> would mean rewriting that embedded path, whose layout is not a
    /// documented format. The cost is that a renamed dealer always displays the <em>original</em>
    /// brand's artwork, which is the intent anyway.
    /// </para>
    /// </para>
    /// </summary>
    private static void CopyBrandLogoForDealer(
        string root,
        string? sourceToken,
        string dealerId,
        bool enabled,
        ConversionStats stats,
        Action<string> log,
        bool resampleSquashedLogos = false,
        bool greyscaleLogos = false)
    {
        if (sourceToken == null)
            return;

        if (sourceToken.Equals(dealerId, StringComparison.OrdinalIgnoreCase))
            return; // Dealer kept the mod's brand, so its logo is already found by name.

        if (!enabled)
        {
            log($"[INFO] Dealer renamed to '{dealerId}' without a logo; it will show whatever the game " +
                $"has for '{dealerId}', or none.");
            return;
        }

        var sourceMaterial = FindBrandLogoMaterial(root, sourceToken);
        if (sourceMaterial == null)
        {
            log($"[WARNING] This mod has no material/ui/brand_logo/{sourceToken}.mat, so the '{dealerId}' " +
                "dealer will have no logo unless the base game already ships one for it.");
            return;
        }

        // A file that is present but empty or zero-filled is a placeholder the mod author never
        // filled in, not a usable logo. Copying it would report success and still show nothing,
        // which looks exactly like the binding being wrong. Say what is actually wrong instead.
        if (!HasRealContent(sourceMaterial))
        {
            stats.EmptyBrandLogos++;
            log($"[WARNING] material/ui/brand_logo/{sourceToken}.mat is empty (zero bytes). The mod author " +
                $"shipped a placeholder, not a logo, so the '{dealerId}' dealer cannot show this mod's badge. " +
                "Supply a real brand_logo .mat/.tobj/.dds with the mod to fix this.");
            return;
        }

        var logoFolder = Path.Combine(root, "material", "ui", "brand_logo");
        Directory.CreateDirectory(logoFolder);

        var targetMaterial = Path.Combine(logoFolder, dealerId + ".mat");
        if (File.Exists(targetMaterial))
        {
            log($"[LOGO] material/ui/brand_logo/{dealerId}.mat already exists in this mod; left as it is.");
            return;
        }

        File.Copy(sourceMaterial, targetMaterial);
        stats.BrandLogosAdded++;
        log($"[LOGO] Added material/ui/brand_logo/{dealerId}.mat so the '{dealerId}' dealer shows the " +
            $"{sourceToken} logo.");
    }

    private static List<VehicleInfo> DiscoverVehicles(string carRoot)
    {
        var result = new List<VehicleInfo>();

        if (!Directory.Exists(carRoot))
            return result;

        foreach (var dir in Directory.GetDirectories(carRoot))
        {
            var data = Directory.EnumerateFiles(
                    dir,
                    "data.sii",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            if (data == null)
                continue;

            result.Add(new VehicleInfo(
                SanitizeId(Path.GetFileName(dir)),
                dir,
                data));
        }

        return result;
    }

    private static List<VehicleInfo> DiscoverVehiclesBySii(string carRoot)
    {
        var result = new List<VehicleInfo>();

        if (!Directory.Exists(carRoot))
            return result;

        foreach (var dir in Directory.GetDirectories(carRoot))
        {
            var sii = Directory.EnumerateFiles(
                    dir,
                    "*.sii",
                    SearchOption.AllDirectories)
                .FirstOrDefault();

            if (sii == null)
                continue;

            result.Add(new VehicleInfo(
                SanitizeId(Path.GetFileName(dir)),
                dir,
                sii));
        }

        return result
            .GroupBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static void MoveVehicleAssetsToCar(
        string root,
        Action<string> log,
        ConversionStats stats)
    {
        var vehicleRoot = Path.Combine(root, "vehicle");
        var truckRoot = Path.Combine(vehicleRoot, "truck");
        var carRoot = Path.Combine(vehicleRoot, "car");

        if (!Directory.Exists(truckRoot))
        {
            log("[INFO] No vehicle/truck asset tree found.");
            return;
        }

        Directory.CreateDirectory(carRoot);

        foreach (var sourceDirectory in Directory.GetDirectories(truckRoot))
        {
            var name = Path.GetFileName(sourceDirectory);
            var destination = Path.Combine(carRoot, name);

            if (Directory.Exists(destination))
            {
                MergeTree(sourceDirectory, destination, log, stats);
                Directory.Delete(sourceDirectory, true);
            }
            else
            {
                Directory.Move(sourceDirectory, destination);
            }

            stats.FilesMovedToCar++;
            log($"[ASSET] vehicle/truck/{name} -> vehicle/car/{name}");
        }

        if (!Directory.EnumerateFileSystemEntries(truckRoot).Any())
        {
            Directory.Delete(truckRoot, true);
        }
        else
        {
            log("[INFO] vehicle/truck still contains entries after migration; the folder was left in place.");
        }
    }

    /// <summary>
    /// Merges a legacy <c>vehicle/truck</c> tree into the Road Trip <c>vehicle/car</c> tree.
    /// <para>
    /// The merge is scoped by name: files only ever land inside the car folder that carries
    /// the same vehicle name as the truck folder being converted, so another mod's car tree
    /// is never touched. A colliding path therefore belongs to an earlier conversion of
    /// <b>this same mod</b>, and the freshly converted file replaces it.
    /// </para>
    /// <para>
    /// Older builds instead kept the previous car file and parked the incoming one beside it
    /// as <c>&lt;name&gt;.truck_source</c>. That left two definitions for one vehicle in the
    /// packed archive, so re-patching a mod duplicated the car from the earlier patch.
    /// </para>
    /// </summary>
    private static void MergeTree(
        string source,
        string destination,
        Action<string> log,
        ConversionStats stats)
    {
        Directory.CreateDirectory(destination);

        foreach (var dir in Directory.GetDirectories(
                     source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(
                     source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            if (File.Exists(target))
            {
                // Same vehicle folder and same file name: this is the same mod converted
                // again, so the new definition set wins and the stale one is dropped.
                File.Move(file, target, overwrite: true);
                stats.FilesReplacedFromEarlierPatch++;
                log($"[REPLACE] {relative.Replace('\\', '/')} updated over the copy left by an earlier patch");
            }
            else
            {
                File.Move(file, target);
            }
        }
    }

    /// <summary>
    /// Deletes <c>.truck_source</c> leftovers written by older converter builds. They were
    /// parked beside the real definition on a collision and then packed into the archive,
    /// where the game saw them as stray files next to the converted car.
    /// </summary>
    private static void RemoveLegacyTruckSourceFiles(string root, Action<string> log)
    {
        var leftovers = Directory
            .EnumerateFiles(root, "*.truck_source", SearchOption.AllDirectories)
            .ToList();

        foreach (var file in leftovers)
            File.Delete(file);

        if (leftovers.Count > 0)
        {
            log($"[INFO] Removed {leftovers.Count} legacy .truck_source leftover file(s) from an earlier conversion.");
        }
    }

    private static List<string> Validate(
        string root,
        string carDef,
        string dealerId,
        bool assetsMoved,
        Action<string> log,
        bool isPatchMode = false)
    {
        var issues = new List<string>();

        if (!Directory.Exists(carDef))
        {
            issues.Add("def/vehicle/car is missing.");
            log("[ERROR] def/vehicle/car is missing.");
        }

        var dealerFolder = Path.Combine(
            root, "def", "vehicle", "car_dealer", dealerId);

        if (!Directory.Exists(dealerFolder))
        {
            issues.Add($"car_dealer/{dealerId} is missing.");
            log($"[WARNING] car_dealer/{dealerId} is missing.");
        }

        // A car whose unit carries no brand component has no dealership of its own, and the
        // game files it under whichever brand happens to claim the unmatched definition.
        foreach (var dataFile in Directory.EnumerateFiles(carDef, "data.sii", SearchOption.AllDirectories))
        {
            string unitText;
            try { unitText = DecodeText(File.ReadAllBytes(dataFile)); }
            catch { continue; }

            var unit = Regex.Match(unitText, @"(?im)^\s*accessory_truck_data\s*:\s*(\S+)")
                .Groups[1].Value;

            if (unit.Length == 0)
                continue;

            var parts = unit.Split('.');
            if (parts.Length < 3)
            {
                var issue =
                    $"The car definition {GetRelativePathForLog(dataFile)} is named '{unit}', " +
                    "which carries no brand component, so ATS cannot match it to a dealership " +
                    $"and will list it under an unrelated brand. Expected 'vehicle.{dealerId}.<model>'.";
                issues.Add(issue);
                log("[WARNING] " + issue);
            }
        }

        var stalePathPattern = new Regex(assetsMoved || isPatchMode
            ? @"(?i)(/|\\)?def/vehicle/truck(/|\\)|(/|\\)vehicle/truck(/|\\)|(^|/|\\)truck_dealer(/|\\)"
            : @"(?i)(/|\\)?def/vehicle/truck(/|\\)|(^|/|\\)truck_dealer(/|\\)");


        foreach (var file in Directory.EnumerateFiles(
                     root, "*.*", SearchOption.AllDirectories)
                     .Where(IsTextDefinition))
        {
            string text;
            try
            {
                text = DecodeText(File.ReadAllBytes(file));
            }
            catch
            {
                continue;
            }

            if (stalePathPattern.IsMatch(text))
            {
                var issue = $"Stale truck path/dealer reference remains in {GetRelativePathForLog(file)}.";
                issues.Add(issue);
                log("[WARNING] " + issue);
            }

        }

        foreach (var vehicle in DiscoverVehicles(carDef))
        {
            foreach (var required in new[] { "chassis", "cabin", "interior", "engine", "transmission" })
            {
                if (!Directory.Exists(Path.Combine(vehicle.Directory, required)))
                {
                    var issue = $"{vehicle.Id}: missing {required}/ definition folder.";
                    issues.Add(issue);
                    log("[WARNING] " + issue);
                }
            }
        }

        return issues;
    }

    private static void WriteReport(
        string root,
        ConversionSettings settings,
        string dealerId,
        IReadOnlyList<VehicleInfo> vehicles,
        ConversionStats stats,
        IReadOnlyList<string> issues,
        IReadOnlyList<string> originalTexts)
    {
        var report = new StringBuilder();

        // No version here on purpose. This header used to hard-code "v12", which was already three
        // naming schemes out of date by the time it was written, and nothing ever noticed because a
        // version string in a report nobody asserts on cannot go red in a test. The report is written
        // into the converted mod's own folder, where the build that produced it is the only thing
        // that matters, and that is recorded by the file itself.
        report.AppendLine("Truckers Tool Kit");
        report.AppendLine("==================");
        report.AppendLine($"Input file: {settings.InputFile}");
        report.AppendLine($"Output directory: {settings.OutputDirectory}");
        report.AppendLine($"Dealer ID: {dealerId}");
        report.AppendLine($"Reference mod: {settings.ReferenceFile}");
        report.AppendLine();

        report.AppendLine($"Vehicles found: {vehicles.Count}");
        report.AppendLine($"Text files scanned: {stats.TextFilesScanned}");
        report.AppendLine($"Text files edited: {stats.TextFilesEdited}");
        report.AppendLine($"Text files unchanged: {stats.TextFilesUnchanged}");
        report.AppendLine($"Binary definitions skipped: {stats.BinaryDefinitionsSkipped}");
        report.AppendLine($"Unreadable definitions: {stats.UnreadableDefinitions}");
        report.AppendLine($"Vehicle asset directories moved: {stats.FilesMovedToCar}");
        report.AppendLine($"Files replaced from an earlier patch: {stats.FilesReplacedFromEarlierPatch}");
        report.AppendLine($"Dealer files converted: {stats.DealerFilesConverted}");
        report.AppendLine($"Reference dealer files added: {stats.DealerFilesCreatedFromReference}");
        report.AppendLine($"Cars assigned to dealer '{dealerId}': {stats.CarsBrandscoped}");
        report.AppendLine($"Mod's own brand token: {(stats.SourceBrandToken.Length == 0 ? "(none)" : stats.SourceBrandToken)}");
        report.AppendLine($"Dealer logo files added: {stats.BrandLogosAdded}");
        report.AppendLine($"Dealer logo textures made transparent: {stats.BrandLogosMadeTransparent}");
        report.AppendLine($"Dealer badge textures cropped to the stock proportions: {stats.BrandLogosCropped}");
        report.AppendLine($"Global _nameless units namespaced: {stats.AnonymousUnitsNamespaced}");
        report.AppendLine($"Validation issues: {issues.Count}");
        report.AppendLine();

        report.AppendLine("Vehicles:");
        foreach (var vehicle in vehicles)
            report.AppendLine($"- {vehicle.Id}");

        report.AppendLine();

        if (issues.Count == 0)
        {
            report.AppendLine("No structural validation issues were detected.");
        }
        else
        {
            report.AppendLine("Validation issues:");
            foreach (var issue in issues)
                report.AppendLine("- " + issue);
        }

        report.AppendLine();
        report.AppendLine("Notes:");
        report.AppendLine("- This tool converts text definitions and archive layout; it does not rebuild binary model files.");
        report.AppendLine("- The supplied Volvo S90 archive uses nonstandard ZIP header metadata; the custom extractor deliberately uses central-directory data so those SCS packages can still be read.");
        report.AppendLine("- A Road Trip reference mod can provide framework files whose exact structure cannot safely be inferred from a truck-era dealer file.");
        report.AppendLine("- '_nameless.' unit names are global in SCS. Two mods using the same one replace each other, so this conversion renames them to carry a unique prefix.");
        report.AppendLine("- A car's dealership comes from the middle component of its accessory_truck_data unit (base game: vehicle.ford.f150_23 is sold by car_dealer/ford). This conversion renames the unit to vehicle." + dealerId + ".<model> so the car appears under the dealer you chose.");
        report.AppendLine("- A dealer's logo is resolved by name, and the car shop and the truck dealer read DIFFERENT folders: material/ui/car_brand_logo/<brand>.mat and material/ui/brand_logo/<brand>.mat. A converted car is sold by the car shop, so its logo has to be in car_brand_logo. A truck-era mod only ships it under brand_logo, which is why the badge was missing.");
        report.AppendLine("- A truck-era logo is also usually an opaque DXT1 image with the black background baked into the pixels, which the dealership draws as a black box around the artwork. DXT1 has no alpha channel, so the box cannot be removed by the game. Textures like that are re-encoded as DXT5 with the border-connected background made transparent, which is the format the base game's own badges use. Interior dark pixels are left alone, so black-and-white artwork such as the BMW roundel keeps its black.");
        if (stats.BrandLogosAdded > 0)
        {
            report.AppendLine();
            report.AppendLine($"Dealer logo files added ({stats.BrandLogosAdded}):");
            report.AppendLine($"- material/ui/car_brand_logo/{dealerId}.mat (+ .tobj and .dds where present). The original truck-era logo is kept, because the .tobj holds an absolute path back to it.");
        }
        else if (stats.EmptyBrandLogos > 0)
        {
            report.AppendLine($"- The mod's own logo file exists but is empty (zero bytes). It is a placeholder " +
                "the mod author never filled in, so this vehicle cannot show a badge from it. This is a " +
                "problem with the mod's files, not with the conversion; no logo was copied.");
        }
        else if (stats.SourceBrandToken.Length > 0 &&
                 !stats.SourceBrandToken.Equals(dealerId, StringComparison.OrdinalIgnoreCase))
        {
            report.AppendLine($"- The dealer was renamed from '{stats.SourceBrandToken}' but no logo was added. If the '{dealerId}' dealer should show this mod's logo, re-run with dealer-logo copying enabled.");
        }
        else if (stats.SourceBrandToken.Length > 0)
        {
            report.AppendLine($"- The dealer kept the mod's own brand '{dealerId}', so the game finds its existing logo by name and no copy was needed.");
        }
        if (stats.AnonymousUnitNames.Count > 0)
        {
            report.AppendLine();
            report.AppendLine($"Namespaced unit names ({stats.AnonymousUnitNames.Count}):");
            foreach (var name in stats.AnonymousUnitNames.OrderBy(n => n, StringComparer.Ordinal))
                report.AppendLine($"- {name}");
        }

        // --- What was written -------------------------------------------------------
        // The duplication problem this guards against is two *separate* patches for the same
        // mod both being enabled, each defining the same unit paths. The converter writes
        // one archive and never sees the other, so it cannot detect that itself. Listing
        // every path here lets the user grep their mod folder for a collision.
        report.AppendLine();
        report.AppendLine($"Output archive: {GetOutputFile(settings)}");

        var writtenPaths = CollectWrittenDefinitionPaths(root);
        report.AppendLine();
        report.AppendLine($"Written definition paths ({writtenPaths.Count}):");
        foreach (var path in writtenPaths)
            report.AppendLine($"- {path}");

        report.AppendLine();
        report.AppendLine("IMPORTANT - duplicate patches:");
        report.AppendLine("- If another Road Trip patch for this same vehicle is also enabled in the mod");
        report.AppendLine("  folder, the game will mount two definitions of the same units and the car can");
        report.AppendLine("  appear twice or behave oddly. Enable only ONE patch per vehicle.");
        report.AppendLine("- To check, list the paths above and look for the same vehicle paths inside any");
        report.AppendLine("  other *_roadtrip*.scs in your mod folder.");

        File.WriteAllText(
            Path.Combine(root, "roadtrip_conversion_report.txt"),
            report.ToString(),
            new UTF8Encoding(false));
    }

    /// <summary>
    /// Every definition file written under <c>def/</c>, as archive-relative paths with forward
    /// slashes, sorted so two reports can be diffed or compared by eye.
    /// </summary>
    private static IReadOnlyList<string> CollectWrittenDefinitionPaths(string root)
    {
        var defRoot = Path.Combine(root, "def");
        if (!Directory.Exists(defRoot))
            return Array.Empty<string>();

        try
        {
            return Directory.EnumerateFiles(defRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            // Listing paths is a convenience. It must never fail a conversion that otherwise
            // succeeded, so a read error downgrades to no list rather than throwing.
            System.Diagnostics.Debug.WriteLine($"Could not list written paths: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static string FindModRoot(string extracted)
    {
        if (Directory.Exists(Path.Combine(extracted, "def")))
            return extracted;

        var immediate = Directory.GetDirectories(extracted);
        if (immediate.Length == 1 &&
            Directory.Exists(Path.Combine(immediate[0], "def")))
            return immediate[0];

        var def = Directory.EnumerateDirectories(
                extracted, "def", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (def != null)
            return Directory.GetParent(def)!.FullName;

        throw new InvalidDataException(
            "Could not find a mod root containing def/.");
    }

    private static bool IsTextDefinition(string file) =>
        file.EndsWith(".sii", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".sui", StringComparison.OrdinalIgnoreCase);

    private static bool IsCarDataFile(string file)
    {
        if (!string.Equals(Path.GetFileName(file), "data.sii", StringComparison.OrdinalIgnoreCase))
            return false;

        var vehicleFolder = Path.GetDirectoryName(file);
        var carFolder = vehicleFolder is null ? null : Path.GetDirectoryName(vehicleFolder);
        return carFolder is not null &&
               string.Equals(Path.GetFileName(carFolder), "car", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCarInteriorFile(string file)
    {
        if (!file.EndsWith(".sii", StringComparison.OrdinalIgnoreCase))
            return false;

        var interiorFolder = Path.GetDirectoryName(file);
        var vehicleFolder = interiorFolder is null ? null : Path.GetDirectoryName(interiorFolder);
        var carFolder = vehicleFolder is null ? null : Path.GetDirectoryName(vehicleFolder);
        return carFolder is not null &&
               string.Equals(Path.GetFileName(interiorFolder), "interior", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Path.GetFileName(carFolder), "car", StringComparison.OrdinalIgnoreCase);
    }

    // Values mirror the official Road Trip cars (km/h, 0 = no limiter).
    public static int DefaultSpeedLimit(string vehicleType) =>
        (vehicleType ?? "pickup").Trim().ToLowerInvariant() switch
        {
            "sedan" => 190,
            "hatchback" => 180,
            "van" => 140,
            _ => 163
        };

    // Cars without speed_limiter_value fall back to the truck speed limit.
    private static string AddSpeedLimiterValue(string text, string vehicleType)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var limit = DefaultSpeedLimit(vehicleType);
        var result = new StringBuilder();
        var position = 0;

        foreach (Match header in Regex.Matches(text, @"accessory_interior_data\s*:\s*[^\s{]+\s*\{"))
        {
            var bodyStart = header.Index + header.Length;
            if (bodyStart < position)
                continue;

            var bodyEnd = text.IndexOf('}', bodyStart);
            if (bodyEnd < 0)
                break;

            if (Regex.IsMatch(text[bodyStart..bodyEnd], @"(?m)^[ \t]*speed_limiter_value\s*:"))
                continue;

            result.Append(text, position, bodyStart - position)
                .Append(newline).Append("\tspeed_limiter_value: ").Append(limit);
            position = bodyStart;
        }

        return position == 0 ? text : result.Append(text, position, text.Length - position).ToString();
    }

    // Road Trip cars need a third info[] line (description) and a tags[] entry
    // matching a vehicle_types value in def/car_job_data.sii to receive jobs.
    private static string AddCarDataAttributes(string text, string vehicleType)
    {
        var header = Regex.Match(text, @"accessory_truck_data\s*:\s*[^\s{]+\s*\{");
        if (!header.Success)
            return text;

        var bodyStart = header.Index + header.Length;
        var bodyEnd = text.IndexOf('}', bodyStart);
        if (bodyEnd < 0)
            return text;

        var body = text[bodyStart..bodyEnd];
        var infos = Regex.Matches(body, @"(?m)^[ \t]*info\[\]\s*:\s*(""[^""\r\n]*"")[^\r\n]*");
        var insert = new StringBuilder();
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";

        var insertAt = infos.Count > 0
            ? bodyStart + infos[^1].Index + infos[^1].Length
            : bodyStart;

        for (var i = infos.Count; i < 3; i++)
        {
            var value = i == 2 && infos.Count >= 2 ? infos[1].Groups[1].Value : "\"\"";
            insert.Append(newline).Append("\tinfo[]: ").Append(value);
        }

        if (!Regex.IsMatch(body, @"(?m)^[ \t]*tags\[\]\s*:"))
            insert.Append(newline).Append("\ttags[]: \"").Append(vehicleType).Append('"');

        return insert.Length == 0 ? text : text.Insert(insertAt, insert.ToString());
    }

    // Maps the per-slot camera references in a car data.sii onto the generic
    // Road Trip car camera units shipped with the base game. Only the eight
    // known car views are rewritten; anything already pointing at *.car,
    // boutique dealer suffixes and non-vehicle cameras are left alone.
    //
    // accessory_truck_data documents fourteen camera fields in total. The six
    // not listed below are deliberately left unmapped, because they are not
    // per-vehicle views and have no *.car counterpart:
    //   top_fixed_camera, predefined_tv_camera, side_camera  - documented as "typically unused"
    //   tv_camera, wander_camera, debug_camera                 - global units
    //     (camera.tv.basic, camera.wander, camera.debug) that the official
    //     Road Trip cars also reference unchanged, so rewriting them would
    //     point at units that do not exist.
    // The reverse gear has no per-vehicle camera to configure. accessory_truck_data
    // documents no reverse_camera field, and the base game ships no reverse camera unit
    // (def/camera/units contains 18 units, none of them reverse-related). What looks
    // related are two unrelated cvars: s_reverse_enabled is the reverse-gear *beep*,
    // and g_cam_steering_reverse tunes interior camera sway while reversing. Neither
    // selects a camera, and neither is something a patch can or should set.
    public static string MapCamerasToCarUnits(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["behind_camera"] = "camera.behind.car",
            ["interior_camera"] = "camera.interior.car",
            ["interior_camera_oculus"] = "camera.interior.car.oculus",
            ["bumper_camera"] = "camera.bumper.car",
            ["window_camera"] = "camera.window.car",
            ["cabin_camera"] = "camera.cabin.car",
            ["wheel_camera"] = "camera.wheel.car",
            ["top_camera"] = "camera.top.car",
        };

        foreach (var slot in map)
        {
            text = Regex.Replace(
                text,
                @"(?m)^(?<indent>[ \t]*)(?<slot>" + slot.Key + @")(?<sep>\s*:\s*)(?<value>[^\r\n#]+)",
                match =>
                {
                    var value = match.Groups["value"].Value.Trim();

                    // Already a stable car unit (e.g. camera.bumper.car or a
                    // DLC-backed camera.behind.suv): never clobber it.
                    if (value.EndsWith(".car", StringComparison.OrdinalIgnoreCase) ||
                        value.EndsWith(".car.oculus", StringComparison.OrdinalIgnoreCase) ||
                        value.EndsWith(".suv", StringComparison.OrdinalIgnoreCase))
                        return match.Value;

                    return match.Groups["indent"].Value +
                           match.Groups["slot"].Value +
                           match.Groups["sep"].Value +
                           slot.Value;
                },
                RegexOptions.IgnoreCase);
        }

        return text;
    }

    private static bool IsBinaryOrEncryptedSii(byte[] bytes)
    {
        if (bytes.Length < 4)
            return false;

        var magic = Encoding.ASCII.GetString(bytes, 0, 4);
        return magic is "BSII" or "ScsC" || magic.StartsWith("3nK", StringComparison.Ordinal);
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes);

        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static string SanitizeId(string value)
    {
        var result = Regex.Replace(
            value.Trim().ToLowerInvariant(),
            @"[^a-z0-9_]+",
            "_");

        result = Regex.Replace(result, "_+", "_").Trim('_');
        return string.IsNullOrEmpty(result) ? "custom" : result;
    }

    private static string GetRelativePathForLog(string file)
    {
        try
        {
            return file.Replace('\\', '/');
        }
        catch
        {
            return file;
        }
    }
}
