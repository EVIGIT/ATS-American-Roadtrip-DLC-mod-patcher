using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ATSRoadTripConverter;

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
    string VehicleType = "pickup")
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
    public int DealerFilesConverted;
    public int DealerFilesCreatedFromReference;
}

public static class ModConverter
{
    public static ConversionResult Run(
        ConversionSettings settings,
        Action<string> log,
        Action<int> progress)
    {
        var dealerId = SanitizeId(settings.DealerId);
        if (dealerId == "custom" && !string.Equals(settings.DealerId.Trim(), "custom", StringComparison.OrdinalIgnoreCase))
            log("[WARNING] Dealer ID contained no usable letters/numbers; using 'custom'.");

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
            if (Directory.Exists(truckDef))
            {
                log("-> Migrating def/vehicle/truck to def/vehicle/car...");
                MergeTree(truckDef, carDef, log);
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

            if (moveAssets)
            {
                MoveVehicleAssetsToCar(root, log, stats);
            }

            progress(35);

            log("-> Converting SII/SUI definitions...");
            ConvertDefinitionFiles(root, stats, log, moveAssets, settings.VehicleType, settings.PatchOnly);

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

            if (File.Exists(outputFile))
                File.Delete(outputFile);

            var packRoot = root;
            if (settings.PatchOnly)
            {
                packRoot = Path.Combine(work, "__patch");
                BuildPatch(root, packRoot, originalTruckDealerFiles, Path.GetFileNameWithoutExtension(settings.InputFile), log);
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
    /// Builds a definitions-only mod meant to be loaded above the untouched original.
    /// Models, textures and sounds are copied to the patch with converted paths, and the
    /// original truck dealer entries are overridden with empty units.
    /// </summary>
    private static void BuildPatch(
        string root,
        string patchRoot,
        IReadOnlyList<string> originalTruckDealerFiles,
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

        foreach (var relative in originalTruckDealerFiles)
        {
            var target = Path.Combine(patchRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "SiiNunit\n{\n}\n", new UTF8Encoding(false));
            log($"[PATCH] Emptied original truck dealer entry {relative.Replace('\\', '/')}");
        }

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
        bool isPatchMode = false)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(IsTextDefinition))
        {
            ConvertDefinitionFile(file, stats, log, moveVehicleAssets, vehicleType, isPatchMode);
        }
    }

    private static void ConvertDefinitionFile(
        string file,
        ConversionStats stats,
        Action<string> log,
        bool moveVehicleAssets,
        string vehicleType,
        bool isPatchMode = false)
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

        if (isVehicleDefinitionFile && IsCarDataFile(file))
            text = AddCarDataAttributes(text, normalizedVehicleType);

        if (isVehicleDefinitionFile && IsCarInteriorFile(file))
            text = AddSpeedLimiterValue(text, normalizedVehicleType);

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
                MergeTree(sourceDirectory, destination, log);
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
            log("[INFO] Some vehicle/truck asset folders were left untouched because of collisions.");
        }
    }

    private static void MergeTree(
        string source,
        string destination,
        Action<string> log)
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
                var collision = target + ".truck_source";
                File.Copy(file, collision, true);
                log($"[COLLISION] Existing car file preserved as {collision}");
            }
            else
            {
                File.Move(file, target);
            }
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

        report.AppendLine("ATS American Roadtrip Car Patcher v12");
        report.AppendLine("==========================");
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
        report.AppendLine($"Dealer files converted: {stats.DealerFilesConverted}");
        report.AppendLine($"Reference dealer files added: {stats.DealerFilesCreatedFromReference}");
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

        File.WriteAllText(
            Path.Combine(root, "roadtrip_conversion_report.txt"),
            report.ToString(),
            new UTF8Encoding(false));
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
