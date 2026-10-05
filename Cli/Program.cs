using ATSRoadTripConverter;

var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];
    if (!arg.StartsWith("--", StringComparison.Ordinal))
        return Usage($"Unexpected argument '{arg}'.");

    var name = arg[2..];
    if (name is "no-move-assets" or "no-dealer" or "defs-only" or "patch-only" or "keep-cameras"
        or "custom-dealer" or "no-logo" or "resample-logos" or "greyscale-logos" or "help")
    {
        flags.Add(name);
        continue;
    }

    if (i + 1 >= args.Length)
        return Usage($"Missing value for {arg}.");

    options[name] = args[++i];
}

if (flags.Contains("help"))
    return Usage(null);

if (options.TryGetValue("extract", out var extractArchive))
{
    if (!options.TryGetValue("output", out var extractTo))
        return Usage("--extract requires --output.");

    Func<string, bool>? include = flags.Contains("defs-only") ? IsDefinitionPath : null;
    ScsArchive.Extract(extractArchive, extractTo, Console.WriteLine, include);
    return 0;
}

if (!options.TryGetValue("input", out var input) || !options.TryGetValue("output", out var output))
    return Usage("--input and --output are required.");

var vehicleType = options.GetValueOrDefault("vehicle-type", "pickup").ToLowerInvariant();
if (!ConversionSettings.VehicleTypes.Contains(vehicleType))
    return Usage($"--vehicle-type must be one of: {string.Join(", ", ConversionSettings.VehicleTypes)}.");

Directory.CreateDirectory(output);

var settings = new ConversionSettings(
    input,
    output,
    options.GetValueOrDefault("dealer", "custom"),
    options.GetValueOrDefault("reference", ""),
    !flags.Contains("no-move-assets"),
    !flags.Contains("no-dealer"),
    flags.Contains("patch-only"),
    vehicleType,
    !flags.Contains("keep-cameras"),
    NamespaceAnonymousUnits: true,
    UseSourceBrandToken: !flags.Contains("custom-dealer"),
    RenameBrandLogo: !flags.Contains("no-logo"),
    // Opt-in and off unless asked for, because both re-encode the artwork's colour and so give up the
    // byte-for-byte guarantee the rest of the badge pipeline keeps.
    ResampleSquashedLogos: flags.Contains("resample-logos"),
    GreyscaleLogos: flags.Contains("greyscale-logos"));

ConversionResult result;
try
{
    result = ModConverter.Run(settings, Console.WriteLine, _ => { });
}
catch (Exception ex)
{
    Console.Error.WriteLine("[EXCEPTION] " + ex);
    return 3;
}

Console.WriteLine(result.Success
    ? $"[SUCCESS] Converted file: {result.OutputFile}"
    : $"[COMPLETE] {result.OutputFile} created with {result.Issues.Count} validation issue(s).");
return result.Success ? 0 : 2;

static bool IsDefinitionPath(string path)
{
    path = path.ToLowerInvariant();
    if (path == "manifest.sii" || path.StartsWith("def/vehicle/", StringComparison.Ordinal))
        return true;

    return path.StartsWith("def/", StringComparison.Ordinal) &&
           System.Text.RegularExpressions.Regex.IsMatch(path, @"(^|[/_.])cars?([/_.]|$)|road_?trip|courier|dispatch|parcel");
}

static int Usage(string? error)
{
    if (error != null)
        Console.Error.WriteLine(error);

    Console.WriteLine("""
        Usage:
          ats-roadtrip-convert --input <mod.scs> --output <folder> [--dealer <id>] [--reference <mod.scs>]
          
          Dealer branding (default, recommended): the mod keeps its own brand and therefore
          its own dealership logo. --custom-dealer instead renames the dealer to --dealer <id>;
          the mod's logo is then copied to that name too, unless --no-logo is given.

          Optional badge repairs, both OFF by default because each re-encodes the artwork's colour and
          so gives up the byte-for-byte guarantee the rest of the pipeline keeps:
            --resample-logos  un-squashes artwork that is itself flattened (the BMW roundel is 2.59:1 in
                              the artist's own pixels, which no amount of canvas trimming can fix).
            --greyscale-logos desaturates the badge to match the base game's monochrome badges.

          ats-roadtrip-convert --extract <archive.scs> --output <folder> [--defs-only]
        """);
    return error == null ? 0 : 1;
}
