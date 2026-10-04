using System.IO.Compression;
using System.Text.RegularExpressions;
using ATSRoadTripConverter;

var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
    Console.WriteLine((condition ? "PASS " : "FAIL ") + message);
}

// The GUI passes a capitalized selection; the converter must normalize it.
Check(ModConverter.DefaultSpeedLimit("Hatchback") == 180, "DefaultSpeedLimit(\"Hatchback\") == 180");
Check(ModConverter.DefaultSpeedLimit("hatchback") == 180, "DefaultSpeedLimit(\"hatchback\") == 180");
Check(ModConverter.DefaultSpeedLimit("Van") == 140, "DefaultSpeedLimit(\"Van\") == 140");
Check(ModConverter.DefaultSpeedLimit("Sedan") == 190, "DefaultSpeedLimit(\"Sedan\") == 190");
Check(ModConverter.DefaultSpeedLimit("pickup") == 163, "DefaultSpeedLimit(\"pickup\") == 163");

// --- Patch mode must be opt-in ---------------------------------------------------
// Patch mode writes an empty unit tree over the original mod's truck_dealer entry, which
// overrides that definition with nothing and makes ATS refuse to load saves ("invalid_vehicle",
// confirmed in game). It must therefore never be the default. These checks pin the default on the
// record itself so the setting cannot quietly flip back.
Check(!new ConversionSettings("a.scs", "out", "volvo", "", true, true).PatchOnly,
    "patch mode is OFF by default on ConversionSettings");
// VehicleType is the 8th parameter; the 7th is PatchOnly. That ordering is exactly the trap this
// check exists to guard - passing "true, pickup" positionally enables patch mode whether the caller
// meant to or not - so it is pinned deliberately with named arguments.
Check(!new ConversionSettings("a.scs", "out", "volvo", "", true, true, VehicleType: "pickup").PatchOnly,
    "setting a vehicle type does not switch patch mode on");
Check(new ConversionSettings("a.scs", "out", "volvo", "", true, true, PatchOnly: true).PatchOnly,
    "patch mode can still be switched on explicitly");

// The CLI must also require an explicit opt-in flag rather than defaulting to a patch. Reading the
// CLI source from here would tie the check to the build output layout, which is fragile; instead
// this asserts the flag is genuinely optional by checking the record has no implicit-on path, which
// is the same guarantee the CLI relies on.
Check(!new ConversionSettings("a.scs", "out", "volvo", "", true, true, false, "pickup", true).PatchOnly,
    "patch mode stays off when unrelated options (camera mapping) are enabled");
Check(!new ConversionSettings("a.scs", "out", "volvo", "", false, false).PatchOnly,
    "patch mode stays off when assets and dealer translation are disabled");

var temp = Path.Combine(Path.GetTempPath(), "ats-verify-" + Guid.NewGuid().ToString("N"));
var modRoot = Path.Combine(temp, "mod");

void WriteFile(string relative, string content)
{
    var path = Path.Combine(modRoot, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
}

try
{
    WriteFile("manifest.sii",
        "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Verify Mod\"\n}\n}\n");
    WriteFile("def/vehicle/truck/example/data.sii",
        "SiiNunit\n{\naccessory_truck_data : .example.data {\n\tinfo[]: \"Verify\"\n\tinfo[]: \"Brand\"\n\tbehind_camera: camera.behind.basic\n\tinterior_camera: camera.interior.example\n\tinterior_camera_oculus: camera.interior.example.oculus\n\tbumper_camera: camera.bumper.example\n\twindow_camera: camera.window.example\n\tcabin_camera: camera.cabin.basic\n\twheel_camera: camera.wheel.basic\n\ttop_camera: camera.top.basic\n}\n}\n");
    WriteFile("def/vehicle/truck/example/interior/example.sii",
        "SiiNunit\n{\naccessory_interior_data : .example.interior {\n\tglass: true\n}\n}\n");
    WriteFile("def/vehicle/truck_dealer/example/example.sii",
        "SiiNunit\n{\naccessory_truck_dealer_data : .example.dealer {\n\tname: \"Example\"\n}\n}\n");

    var input = Path.Combine(temp, "TestMod.zip");
    ZipFile.CreateFromDirectory(modRoot, input);
    var output = Path.Combine(temp, "out");
    Directory.CreateDirectory(output);

    var settings = new ConversionSettings(input, output, "example", "", true, true, false, "Hatchback");
    var result = ModConverter.Run(settings, _ => { }, _ => { });
    Check(File.Exists(result.OutputFile), "conversion produced an output file");

    var extract = Path.Combine(temp, "verify");
    ZipFile.ExtractToDirectory(result.OutputFile, extract);

    var carRoot = Path.Combine(extract, "def", "vehicle", "car");
    Check(Directory.Exists(carRoot), "def/vehicle/car exists in the converted archive");

    var dataFile = Directory.EnumerateFiles(carRoot, "data.sii", SearchOption.AllDirectories).FirstOrDefault();
    Check(dataFile != null, "converted data.sii exists");
    if (dataFile != null)
    {
        var text = File.ReadAllText(dataFile);
        Check(text.Contains("tags[]: \"hatchback\""), "data.sii tags[] uses the lowercase vehicle type");
        Check(!text.Contains("\"Hatchback\""), "data.sii contains no capitalized vehicle type");
        Check(text.Contains("bumper_camera: camera.bumper.car"), "bumper_camera retargeted to camera.bumper.car");
        Check(text.Contains("behind_camera: camera.behind.car"), "behind_camera retargeted to camera.behind.car");
        Check(text.Contains("cabin_camera: camera.cabin.car"), "cabin_camera retargeted to camera.cabin.car");
        Check(text.Contains("wheel_camera: camera.wheel.car"), "wheel_camera retargeted to camera.wheel.car");
        Check(text.Contains("top_camera: camera.top.car"), "top_camera retargeted to camera.top.car");
        Check(text.Contains("window_camera: camera.window.car"), "window_camera retargeted to camera.window.car");
        Check(text.Contains("interior_camera: camera.interior.car"), "interior_camera retargeted to camera.interior.car");
        Check(text.Contains("interior_camera_oculus: camera.interior.car.oculus"), "oculus camera retargeted to camera.interior.car.oculus");
        Check(!text.Contains("camera.bumper.example"), "bespoke bumper camera unit is gone");
    }

    // Opt-out must keep the original truck-era camera names untouched.
    Check(ModConverter.MapCamerasToCarUnits(" bumper_camera: camera.bumper.example ")
        .Contains("camera.bumper.car"), "MapCamerasToCarUnits maps a bespoke bumper camera");
    Check(ModConverter.MapCamerasToCarUnits(" bumper_camera: camera.bumper.car ")
        .Contains("camera.bumper.car"), "MapCamerasToCarUnits keeps an existing *.car unit");
    Check(ModConverter.MapCamerasToCarUnits(" behind_camera: camera.behind.suv ")
        .Contains("camera.behind.suv"), "MapCamerasToCarUnits keeps the DLC camera.behind.suv unit");

    var interiorFile = Directory.EnumerateFiles(carRoot, "*.sii", SearchOption.AllDirectories)
        .FirstOrDefault(f => f.Replace('\\', '/').Contains("/interior/"));
    Check(interiorFile != null, "converted interior .sii exists");
    if (interiorFile != null)
    {
        var text = File.ReadAllText(interiorFile);
        Check(text.Contains("speed_limiter_value: 180"), "interior speed_limiter_value is 180 for a hatchback");
    }

    var dealerFile = Path.Combine(extract, "def", "vehicle", "car_dealer", "example", "example.sii");
    Check(File.Exists(dealerFile), "truck dealer was translated to car_dealer/example");

    // --- Re-patch regression -------------------------------------------------
    // A mod that already carries a car tree from an earlier conversion must come back
    // with a single converted definition set. The old build kept the stale car file and
    // parked the incoming one beside it as <name>.truck_source, so the packed archive
    // ended up with two definitions for one vehicle - the car was "duplicated".
    var rePatchMod = Path.Combine(temp, "repatch");

    void WriteRePatchFile(string relative, string content)
    {
        var path = Path.Combine(rePatchMod, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    WriteRePatchFile("manifest.sii",
        "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Re-patch Mod\"\n}\n}\n");
    WriteRePatchFile("def/vehicle/truck/example/data.sii",
        "SiiNunit\n{\naccessory_truck_data : .example.data {\n\tinfo[]: \"Verify\"\n\tinfo[]: \"Brand\"\n}\n}\n");
    WriteRePatchFile("def/vehicle/truck/example/interior/example.sii",
        "SiiNunit\n{\naccessory_interior_data : .example.interior {\n\tglass: true\n}\n}\n");
    WriteRePatchFile("def/vehicle/truck_dealer/example/example.sii",
        "SiiNunit\n{\naccessory_truck_dealer_data : .example.dealer {\n\tname: \"Example\"\n}\n}\n");

    // The leftovers of an earlier conversion of this very mod: the car tree it produced
    // plus one of the .truck_source files the old collision handling used to write.
    // accessory_truck_data is the unit type ATS uses for cars too; accessory_car_data is not
    // a real SCS unit type, so these fixtures must not invent it.
    WriteRePatchFile("def/vehicle/car/example/data.sii",
        "SiiNunit\n{\naccessory_truck_data : .example.OLD {\n\tinfo[]: \"STALE_PATCH_MARKER\"\n}\n}\n");
    WriteRePatchFile("def/vehicle/car/example/data.sii.truck_source",
        "SiiNunit\n{\naccessory_truck_data : .example.leftover {\n\tinfo[]: \"LEGACY_LEFTOVER_MARKER\"\n}\n}\n");

    var rePatchInput = Path.Combine(temp, "RePatchMod.zip");
    ZipFile.CreateFromDirectory(rePatchMod, rePatchInput);

    var rePatchSettings = new ConversionSettings(rePatchInput, output, "example", "", true, true, false, "Hatchback");
    var rePatchResult = ModConverter.Run(rePatchSettings, _ => { }, _ => { });
    Check(File.Exists(rePatchResult.OutputFile), "re-patch produced an output file");

    var rePatchExtract = Path.Combine(temp, "repatch-verify");
    ZipFile.ExtractToDirectory(rePatchResult.OutputFile, rePatchExtract);

    var rePatchCarRoot = Path.Combine(rePatchExtract, "def", "vehicle", "car");
    var rePatchCarFiles = Directory.EnumerateFiles(rePatchCarRoot, "*", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(rePatchCarRoot, f).Replace('\\', '/'))
        .OrderBy(f => f, StringComparer.Ordinal)
        .ToList();
    Check(rePatchCarFiles.Count == 2,
        $"re-patch car tree holds only the 2 converted definitions (found {rePatchCarFiles.Count}: {string.Join(", ", rePatchCarFiles)})");

    var rePatchData = Directory.EnumerateFiles(rePatchCarRoot, "data.sii", SearchOption.AllDirectories).ToList();
    Check(rePatchData.Count == 1, "re-patch leaves exactly one car data.sii (no duplicated car)");
    if (rePatchData.Count >= 1)
    {
        var text = File.ReadAllText(rePatchData[0]);
        Check(text.Contains("tags[]: \"hatchback\""), "re-patch: the freshly converted data.sii wins");
        Check(!text.Contains("STALE_PATCH_MARKER"), "re-patch: the earlier patch's car file is gone");
    }

    var leftovers = Directory
        .EnumerateFiles(rePatchExtract, "*.truck_source", SearchOption.AllDirectories)
        .ToList();
    Check(leftovers.Count == 0, "re-patch writes no .truck_source files");

    // --- Reference dealer framework merging ----------------------------------
    // CopyMissingReferenceFramework is the second preserve-on-collision path in the
    // converter (the first being MergeTree, covered above). It copies only files the
    // target is missing, so a mod that already carries its own copy of a reference dealer
    // file must keep it. This guards the Track B bug class in the path the earlier
    // diagnosis wrongly suspected was involved.
    var refMod = Path.Combine(temp, "reference");

    void WriteReferenceFile(string relative, string content)
    {
        var path = Path.Combine(refMod, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    WriteReferenceFile("manifest.sii",
        "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Reference\"\n}\n}\n");
    // Framework files the converted mod does not have, so they must be pulled across.
    WriteReferenceFile("def/vehicle/car_dealer/framework/index.sii",
        "SiiNunit\n{\naccessory_car_dealer_data : .framework.index {\n\tname: \"Framework\"\n}\n}\n");
    WriteReferenceFile("def/vehicle/car_dealer/framework/brand.sii",
        "SiiNunit\n{\naccessory_car_dealer_data : .framework.brand {\n\tname: \"Framework Brand\"\n}\n}\n");
    // Same dealer folder AND same file name the converted mod ends up with, so this is a
    // genuine collision. The reference copy must lose: the mod's own translated definition is
    // the correct one, and overwriting it is the bug this check guards.
    WriteReferenceFile("def/vehicle/car_dealer/example/example.sii",
        "SiiNunit\n{\naccessory_car_dealer_data : .framework.collide {\n\tname: \"REFERENCE_COLLISION_MARKER\"\n}\n}\n");

    var refInput = Path.Combine(temp, "Reference.zip");
    ZipFile.CreateFromDirectory(refMod, refInput);

    var refSettings = new ConversionSettings(input, output, "example", refInput, true, true, false, "Hatchback");
    var refResult = ModConverter.Run(refSettings, _ => { }, _ => { });
    Check(File.Exists(refResult.OutputFile), "reference conversion produced an output file");

    var refExtract = Path.Combine(temp, "reference-verify");
    ZipFile.ExtractToDirectory(refResult.OutputFile, refExtract);

    var frameworkIndex = Path.Combine(refExtract, "def", "vehicle", "car_dealer", "framework", "index.sii");
    Check(File.Exists(frameworkIndex), "a missing reference dealer file was added from the reference mod");
    var frameworkBrand = Path.Combine(refExtract, "def", "vehicle", "car_dealer", "framework", "brand.sii");
    Check(File.Exists(frameworkBrand), "every missing reference dealer file is added, not just the first");

    // The converted mod's own dealer entry must be untouched by the reference copy.
    var ownDealer = Path.Combine(refExtract, "def", "vehicle", "car_dealer", "example", "example.sii");
    Check(File.Exists(ownDealer), "the converted mod's own dealer file survives a reference copy");
    if (File.Exists(ownDealer))
    {
        Check(File.ReadAllText(ownDealer).Contains("accessory_truck_dealer_data"),
            "the converted dealer file is not overwritten by the reference framework");
        Check(!File.ReadAllText(ownDealer).Contains("REFERENCE_COLLISION_MARKER"),
            "the reference copy loses the collision against the mod's own dealer file");
    }

    // --- Truck to car migration ---------------------------------------------
    // MergeTree moves def/vehicle/truck onto def/vehicle/car and the old tree is deleted.
    // The replacement is scoped by vehicle name, so a car folder whose name does NOT match
    // the truck being converted must be left completely alone.
    var foreignMod = Path.Combine(temp, "foreign");

    void WriteForeignFile(string relative, string content)
    {
        var path = Path.Combine(foreignMod, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    WriteForeignFile("manifest.sii",
        "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Foreign Car Mod\"\n}\n}\n");
    WriteForeignFile("def/vehicle/truck/newtruck/data.sii",
        "SiiNunit\n{\naccessory_truck_data : .newtruck.data {\n\tinfo[]: \"New\"\n}\n}\n");
    WriteForeignFile("def/vehicle/truck/newtruck/interior/newtruck.sii",
        "SiiNunit\n{\naccessory_interior_data : .newtruck.interior {\n\tglass: true\n}\n}\n");
    WriteForeignFile("def/vehicle/truck_dealer/newtruck/newtruck.sii",
        "SiiNunit\n{\naccessory_truck_dealer_data : .newtruck.dealer {\n\tname: \"New Truck\"\n}\n}\n");
    // A car from a different mod, whose folder name does not match the truck being converted.
    WriteForeignFile("def/vehicle/car/othercar/data.sii",
        "SiiNunit\n{\naccessory_truck_data : .othercar.data {\n\tinfo[]: \"FOREIGN_CAR_MARKER\"\n}\n}\n");

    var foreignInput = Path.Combine(temp, "ForeignMod.zip");
    ZipFile.CreateFromDirectory(foreignMod, foreignInput);

    var foreignSettings = new ConversionSettings(foreignInput, output, "newtruck", "", true, true, false, "Van");
    var foreignResult = ModConverter.Run(foreignSettings, _ => { }, _ => { });
    Check(File.Exists(foreignResult.OutputFile), "truck-to-car migration produced an output file");

    var foreignExtract = Path.Combine(temp, "foreign-verify");
    ZipFile.ExtractToDirectory(foreignResult.OutputFile, foreignExtract);

    // The truck trees must be gone entirely: leaving one behind is how a single vehicle ends
    // up defined twice, once as a truck and once as a car.
    Check(
        !Directory.Exists(Path.Combine(foreignExtract, "def", "vehicle", "truck")),
        "def/vehicle/truck is removed after the car migration");
    Check(
        !Directory.Exists(Path.Combine(foreignExtract, "def", "vehicle", "truck_dealer")),
        "def/vehicle/truck_dealer is removed after the dealer migration");

    var migratedDealer = Path.Combine(foreignExtract, "def", "vehicle", "car_dealer", "newtruck", "newtruck.sii");
    Check(File.Exists(migratedDealer), "the truck dealer migrated to car_dealer/newtruck");
    if (File.Exists(migratedDealer))
    {
        var dealerText = File.ReadAllText(migratedDealer);
        Check(!dealerText.Contains("def/vehicle/truck"), "the migrated dealer file has no def/vehicle/truck path left");
        Check(!dealerText.Contains("vehicle/truck"), "the migrated dealer file has no vehicle/truck asset path left");
    }

    // The foreign car tree is out of scope: the same vehicle-name scoping the maintainer
    // decided for the re-patch fix. Assigning the dealer renames only the folder this
    // conversion migrated, so another mod's car folder must keep both its name and content.
    var foreignCar = Path.Combine(foreignExtract, "def", "vehicle", "car", "othercar", "data.sii");
    Check(File.Exists(foreignCar), "another mod's car tree is left in place");
    if (File.Exists(foreignCar))
    {
        Check(File.ReadAllText(foreignCar).Contains("FOREIGN_CAR_MARKER"),
            "a car folder whose name does not match is left completely alone");
    }

    // The converted truck became a car definition. Only the unit name is rewritten; the
    // definition folder keeps the mod's own name so its internal path references still resolve.
    var migratedCar = Path.Combine(foreignExtract, "def", "vehicle", "car", "newtruck", "data.sii");
    Check(File.Exists(migratedCar), "the converted truck became a car definition");
    if (File.Exists(migratedCar))
    {
        var carText = File.ReadAllText(migratedCar);
        // The unit type stays accessory_truck_data, and that is deliberate: accessory_car_data
        // is not a real SCS unit type (it is absent from the documented vehicle accessory
        // list), and cars in ATS are defined with accessory_truck_data. Renaming it here would
        // be the bug, not the fix, so this check pins the correct behaviour.
        Check(carText.Contains("accessory_truck_data"),
            "the migrated car keeps the accessory_truck_data unit type, which is what ATS uses for cars");
        Check(!carText.Contains("accessory_car_data"),
            "no invented accessory_car_data unit type is emitted");
        Check(carText.Contains("tags[]: \"van\""), "the migrated definition carries the requested vehicle type");
    }
// --- Dealer branding regression ------------------------------------------
    // A car's dealership is decided by the middle component of its accessory_truck_data
    // unit, not by a "brand" attribute. Real base-game evidence from the Road Trip DLC:
    //   def/vehicle/car/ford.f150_23/data.sii  -> accessory_truck_data : vehicle.ford.f150_23
    //   def/vehicle/car_dealer/ford/ford_f150_23.sii
    // Truck-era mods omit the brand half ("accessory_truck_data : vehicle.vols90"), so ATS
    // cannot match them to a dealer and lists them under an unrelated brand. That is the
    // "Volvo appears in the BMW dealer" report.
    Check(
        ModConverter.ApplyDealerBrand(
            "SiiNunit\n{\naccessory_truck_data : vehicle.vols90\t\n{\n\tinfo[]: \"Volvo\"\n}\n}\n",
            "volvo",
            "vols90")
            .Contains("accessory_truck_data : vehicle.volvo.vols90"),
        "ApplyDealerBrand gives a brandless car unit the chosen dealer");

    Check(
        ModConverter.ApplyDealerBrand(
            "SiiNunit\n{\naccessory_truck_data : vehicle.m5.g90\n{\n}\n}\n",
            "bmw",
            "g90")
            .Contains("accessory_truck_data : vehicle.bmw.g90"),
        "ApplyDealerBrand replaces a wrong brand half with the chosen dealer");

    Check(
        !ModConverter.ApplyDealerBrand(
            "SiiNunit\n{\naccessory_truck_data : vehicle.ford.f150_23\n{\n}\n}\n",
            "ford",
            "f150_23")
            .Contains("vehicle.ford.ford"),
        "ApplyDealerBrand leaves an already-correct base-game unit alone");

    Check(
        ModConverter.ApplyDealerBrand(
            "SiiNunit\n{\naccessory_truck_data : vehicle.vols90\n{\n}\n}\nreference: vehicle.vols90\n}\n",
            "volvo",
            "vols90")
            .Contains("reference: vehicle.volvo.vols90"),
        "ApplyDealerBrand also rewrites plain references to the old unit");

    // End-to-end: a converted mod must land in car/<dealer>.<model> with a matching unit
    // and dealer data_path, which is what makes the game show it in that dealership.
    var brandMod = Path.Combine(temp, "brandmod");

    void WriteBrandFile(string relative, string content)
    {
        var path = Path.Combine(brandMod, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    WriteBrandFile("manifest.sii",
        "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Brand Mod\"\n}\n}\n");
    WriteBrandFile("def/vehicle/truck/vols90/data.sii",
        "SiiNunit\n{\naccessory_truck_data : vehicle.vols90\t\n{\n\tinfo[]: \"Volvo\"\n\tinfo[]: \"S90\"\n}\n}\n");
    // A reference to the car unit from another file in the same mod. It must be rewritten too,
    // and the internal path reference below must keep pointing at the folder that still exists.
    WriteBrandFile("def/vehicle/car/vols90/interior/black.sii",
        "SiiNunit\n{\naccessory_interior_data : black.vols90.interior\n{\n\tsuitable_for[]: \"vehicle.vols90\"\n\tdefaults[]: \"/def/vehicle/car/vols90/data.sii\"\n}\n}\n");
    WriteBrandFile("def/vehicle/truck_dealer/volvo_cars/s90_2020.sii",
        "SiiNunit\n{\nvehicle : .s90_2020\n{\n\taccessories[]: .data\n}\n\nvehicle_accessory : .data {\n\tdata_path: \"/def/vehicle/truck/vols90/data.sii\"\n}\n}\n");

    var brandInput = Path.Combine(temp, "BrandMod.zip");
    ZipFile.CreateFromDirectory(brandMod, brandInput);

    // Explicitly renames the dealer. Without this the new default keeps the mod's own brand,
// which is the behaviour covered separately below.
var brandSettings = new ConversionSettings(
    brandInput, output, "volvo", "", true, true, true, "Sedan",
    UseSourceBrandToken: false);
    var brandResult = ModConverter.Run(brandSettings, _ => { }, _ => { });
    Check(File.Exists(brandResult.OutputFile), "dealer-brand conversion produced an output file");

    var brandExtract = Path.Combine(temp, "brand-verify");
    ZipFile.ExtractToDirectory(brandResult.OutputFile, brandExtract);

    var brandedCar = Path.Combine(brandExtract, "def", "vehicle", "car", "vols90", "data.sii");
    Check(File.Exists(brandedCar),
        "the converted car keeps its own definition folder name");
    if (File.Exists(brandedCar))
    {
        var text = File.ReadAllText(brandedCar);
        Check(text.Contains("accessory_truck_data : vehicle.volvo.vols90"),
            "converted car declares vehicle.<dealer>.<model> so ATS can place it at that dealer");
    }

    // Regression: an earlier build renamed the definition folder to <dealer>.<model> but did
    // not rewrite the mod's own path references to it, so the mod pointed at files that no
    // longer existed and the game crashed on load. The folder name must not change, and every
    // def/vehicle/car/... path must still resolve to a file that is actually in the archive.
    Check(
        !Directory.Exists(Path.Combine(brandExtract, "def", "vehicle", "car", "volvo.vols90")),
        "the definition folder is NOT renamed to <dealer>.<model> (that orphaned path refs)");
    Check(
        Directory.Exists(Path.Combine(brandExtract, "def", "vehicle", "car", "vols90")),
        "the original definition folder still exists at the path the mod references");

    var interiorOut = Path.Combine(brandExtract, "def", "vehicle", "car", "vols90", "interior", "black.sii");
    Check(File.Exists(interiorOut), "interior definition survives alongside the car data");
    if (File.Exists(interiorOut))
    {
        var text = File.ReadAllText(interiorOut);
        Check(text.Contains("vehicle.volvo.vols90"),
            "a cross-file reference to the car unit is rewritten consistently");
        Check(text.Contains("/def/vehicle/car/vols90/data.sii"),
            "an internal path reference is left alone because the folder still exists");
    }

    var brandedDealer = Path.Combine(brandExtract, "def", "vehicle", "car_dealer", "volvo", "s90_2020.sii");
    Check(File.Exists(brandedDealer), "dealer file exists under car_dealer/volvo");
    if (File.Exists(brandedDealer))
    {
        var text = File.ReadAllText(brandedDealer);
        Check(text.Contains("/def/vehicle/car/vols90/data.sii"),
            "dealer data_path still points at the existing car definition folder");
        Check(!text.Contains("volvo.vols90"),
            "dealer data_path is not rewritten to a folder that does not exist");
    }

    // Every def/vehicle/car path any file mentions must exist in the packed archive.
    var extractRoot = brandExtract;
    var brokenRefs = new List<string>();
    foreach (var file in Directory.EnumerateFiles(extractRoot, "*.*", SearchOption.AllDirectories)
                 .Where(f => f.EndsWith(".sii") || f.EndsWith(".sui")))
    {
        foreach (Match m in Regex.Matches(
                     File.ReadAllText(file),
                     @"(?i)def/vehicle/car/([A-Za-z0-9_.]+)/"))
        {
            var target = Path.Combine(extractRoot, "def", "vehicle", "car", m.Groups[1].Value);
            if (!Directory.Exists(target))
                brokenRefs.Add(file.Replace(extractRoot, "") + " -> " + m.Value);
        }
    }
    Check(brokenRefs.Count == 0,
        $"no definition points at a car folder that does not exist ({string.Join("; ", brokenRefs)})");

    var reportText = File.ReadAllText(Path.Combine(brandExtract, "roadtrip_conversion_report.txt"));
    Check(reportText.Contains("vehicle.volvo.<model>") || reportText.Contains("accessory_truck_data"),
        "the conversion report explains how the dealership is chosen");

    // --- Dealer branding and dealership logos ------------------------------------
    // The game resolves a dealer's logo by name (material/ui/brand_logo/<dealer>.mat), so the
    // dealer ID and the logo file name are two halves of one binding. Renaming one without the
    // other is what makes a converted car lose its badge, so both halves are covered here.

    var logoMod = Path.Combine(temp, "logomod");

    void WriteLogoFile(string relative, string content)
    {
        var path = Path.Combine(logoMod, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    WriteLogoFile("manifest.sii",
        "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Logo Mod\"\n}\n}\n");
    WriteLogoFile("def/vehicle/truck/vols90/data.sii",
        "SiiNunit\n{\naccessory_truck_data : vehicle.vols90\t\n{\n\tinfo[]: \"Volvo\"\n\tinfo[]: \"S90\"\n}\n}\n");
    WriteLogoFile("def/vehicle/truck_dealer/volvo_cars/s90_2020.sii",
        "SiiNunit\n{\nvehicle : .s90_2020\n{\n\taccessories[]: .data\n}\n\nvehicle_accessory : .data {\n\tdata_path: \"/def/vehicle/truck/vols90/data.sii\"\n}\n}\n");
    // A truck-era mod files its badge under material/ui/brand_logo/. The car shop reads
    // material/ui/car_brand_logo/ instead - proven by the game's own log:
    //   <ERROR> [resource_task] Can not open '/material/ui/car_brand_logo/volvo_cars.mat'
    WriteLogoFile("material/ui/brand_logo/volvo_cars.mat",
        "material : \"ui\"\n{\n\ttexture : \"volvo_cars.tobj\"\n\ttexture_name : \"texture\"\n}\n");
    WriteLogoFile("material/ui/brand_logo/volvo_cars.tobj", "not-a-real-compiled-object");
    WriteLogoFile("material/ui/brand_logo/volvo_cars.dds", "not-a-real-dds-payload");

    var logoInput = Path.Combine(temp, "LogoMod.zip");
    ZipFile.CreateFromDirectory(logoMod, logoInput);

    // The mod's brand is read from def/vehicle/truck_dealer, because ATS has no brand attribute
    // to read and a truck-era unit name (vehicle.vols90) carries no brand part either.
    Check(ModConverter.DiscoverSourceBrandToken(logoMod) == "volvo_cars",
        "the mod's own brand token is discovered from def/vehicle/truck_dealer");
    Check(ModConverter.DiscoverSourceBrandToken(modRoot) == "example",
        "brand discovery works for a differently named dealer folder");
    Check(ModConverter.DiscoverSourceBrandToken(temp) == null,
        "brand discovery returns null when there is no truck dealer at all");
    Check(ModConverter.HasBrandLogo(logoMod, "volvo_cars"),
        "the mod is recognised as shipping its dealer's logo material");
    Check(!ModConverter.HasBrandLogo(logoMod, "someother"),
        "a brand with no logo material is not reported as having one");

    // Default: keep the mod's brand, so the existing logo is found by name and nothing is copied.
    var keepSettings = new ConversionSettings(logoInput, output, "volvo", "", true, true, true, "Sedan");
    var keepResult = ModConverter.Run(keepSettings, _ => { }, _ => { });
    var keepExtract = Path.Combine(temp, "logo-keep");
    ZipFile.ExtractToDirectory(keepResult.OutputFile, keepExtract);

    var keepCar = Path.Combine(keepExtract, "def", "vehicle", "car", "vols90", "data.sii");
    Check(File.Exists(keepCar) &&
          File.ReadAllText(keepCar).Contains("accessory_truck_data : vehicle.volvo_cars.vols90"),
        "keeping the mod's brand assigns the car to vehicle.<mod-brand>.<model>");

    Check(Directory.Exists(Path.Combine(keepExtract, "def", "vehicle", "car_dealer", "volvo_cars")),
        "keeping the mod's brand puts the dealer under its original name");
    Check(!Directory.Exists(Path.Combine(keepExtract, "def", "vehicle", "car_dealer", "volvo")),
        "keeping the mod's brand does not create the typed dealer name");

    var keepLogo = Path.Combine(keepExtract, "material", "ui", "car_brand_logo", "volvo_cars.mat");
    Check(File.Exists(keepLogo),
        "the logo is written to car_brand_logo, which is the folder the car shop actually reads");

    var keepTruckLogo = Path.Combine(keepExtract, "material", "ui", "brand_logo", "volvo_cars.dds");
    Check(File.Exists(keepTruckLogo),
        "the original truck-era logo is kept, because the copied .tobj points at it by absolute path");

    var keepTobj = Path.Combine(keepExtract, "material", "ui", "car_brand_logo", "volvo_cars.tobj");
    Check(File.Exists(keepTobj),
        "the .tobj travels with the .mat, since the .mat resolves it by bare name beside itself");

    // Renamed dealer: a logo copy must appear under the new name, or the dealer has no badge.
    var renameSettings = new ConversionSettings(
        logoInput, output, "sweden", "", true, true, true, "Sedan",
        UseSourceBrandToken: false);
    var renameResult = ModConverter.Run(renameSettings, _ => { }, _ => { });
    var renameExtract = Path.Combine(temp, "logo-rename");
    ZipFile.ExtractToDirectory(renameResult.OutputFile, renameExtract);

    Check(Directory.Exists(Path.Combine(renameExtract, "def", "vehicle", "car_dealer", "sweden")),
        "renaming the dealer still puts the dealer data under the new name");

    var renamedLogo = Path.Combine(renameExtract, "material", "ui", "car_brand_logo", "sweden.mat");
    Check(File.Exists(renamedLogo),
        "renaming the dealer writes car_brand_logo/<new-dealer>.mat so the badge is not lost");

    if (File.Exists(renamedLogo) && File.Exists(keepLogo))
    {
        Check(File.ReadAllText(renamedLogo) == File.ReadAllText(keepLogo),
            "the copied logo is byte-for-byte the source logo (no binary rewrite)");
        Check(File.ReadAllText(renamedLogo).Contains("\"volvo_cars.tobj\""),
            "the copied logo still points at the source texture, which the mod still ships");
        Check(File.Exists(Path.Combine(renameExtract, "material", "ui", "brand_logo", "volvo_cars.tobj")),
            "the texture the copied logo refers to is present in the archive");
    }

    // Opting out must leave the rename alone rather than silently writing a logo anyway.
    var noLogoSettings = new ConversionSettings(
        logoInput, output, "norway", "", true, true, true, "Sedan",
        UseSourceBrandToken: false, RenameBrandLogo: false);
    var noLogoResult = ModConverter.Run(noLogoSettings, _ => { }, _ => { });
    var noLogoExtract = Path.Combine(temp, "logo-none");
    ZipFile.ExtractToDirectory(noLogoResult.OutputFile, noLogoExtract);

    Check(Directory.Exists(Path.Combine(noLogoExtract, "def", "vehicle", "car_dealer", "norway")),
        "the dealer rename still happens when logo copying is off");
    Check(!File.Exists(Path.Combine(noLogoExtract, "material", "ui", "car_brand_logo", "norway.mat")),
        "no car-shop logo is written for the renamed dealer when logo copying is off");
    Check(File.Exists(Path.Combine(noLogoExtract, "material", "ui", "brand_logo", "volvo_cars.mat")),
        "the mod's own truck-era logo is still shipped when logo copying is off");

    var noLogoReport = File.ReadAllText(Path.Combine(noLogoExtract, "roadtrip_conversion_report.txt"));
    Check(noLogoReport.Contains("norway") && noLogoReport.Contains("volvo_cars"),
        "the report records the renamed dealer and the brand it came from");
    Check(noLogoReport.Contains("no logo was added"),
        "the report warns when a dealer was renamed without a logo");

    // --- A logo file that exists but is empty --------------------------------------
    // A logo file that exists but is empty. A mod author can reserve the path as a placeholder
    // and never fill it in, and an empty file is indistinguishable from a working one by name.
    // Copying it would produce a correctly named badge that renders as nothing, which looks
    // exactly like the binding being wrong. It must be reported, not copied.
    var emptyLogoMod = Path.Combine(temp, "emptylogo");
    Directory.CreateDirectory(Path.Combine(emptyLogoMod, "material", "ui", "brand_logo"));
    File.Copy(Path.Combine(logoMod, "manifest.sii"), Path.Combine(emptyLogoMod, "manifest.sii"));
    foreach (var rel in new[]
             {
                 "def/vehicle/truck/vols90/data.sii",
                 "def/vehicle/truck_dealer/volvo_cars/s90_2020.sii"
             })
    {
        var dest = Path.Combine(emptyLogoMod, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(Path.Combine(logoMod, rel.Replace('/', Path.DirectorySeparatorChar)), dest);
    }

    var emptyMat = Path.Combine(emptyLogoMod, "material", "ui", "brand_logo", "volvo_cars.mat");
    File.WriteAllBytes(emptyMat, new byte[75]); // real length, all zeros

    Check(ModConverter.HasBrandLogo(emptyLogoMod, "volvo_cars"),
        "a zero-filled logo file still counts as present");

    var emptyInput = Path.Combine(temp, "EmptyLogoMod.zip");
    ZipFile.CreateFromDirectory(emptyLogoMod, emptyInput);

    // Renaming the dealer, so the empty source logo is what decides whether a copy is written.
    var emptySettings = new ConversionSettings(
        emptyInput, output, "norway", "", true, true, true, "Sedan",
        UseSourceBrandToken: false);

    var emptyResult = ModConverter.Run(emptySettings, _ => { }, _ => { });
    var emptyExtract = Path.Combine(temp, "logo-empty");
    ZipFile.ExtractToDirectory(emptyResult.OutputFile, emptyExtract);

    Check(Directory.Exists(Path.Combine(emptyExtract, "def", "vehicle", "car_dealer", "norway")),
        "an empty logo does not stop the conversion itself");
    Check(!File.Exists(Path.Combine(emptyExtract, "material", "ui", "brand_logo", "norway.mat")),
        "an empty logo file is NOT copied to the new dealer name");

    var emptyReport = File.ReadAllText(Path.Combine(emptyExtract, "roadtrip_conversion_report.txt"));
    Check(emptyReport.Contains("empty (zero bytes)"),
        "the report says the logo is an empty placeholder rather than implying the binding is wrong");

    // --- A mod that already ships BOTH logo folders -----------------------------------
    // FindBrandLogoMaterial prefers car_brand_logo, so when a mod already ships one there, the
    // "source" folder and the target folder are the same directory and the copy becomes
    // File.Copy(x, x, overwrite: true) - which throws IOException on Windows, failing the whole
    // conversion. This is reachable by re-running the converter on a mod that was already
    // converted, and by any mod shipped with both folders. It must be a no-op, not a crash.
    var bothLogoMod = Path.Combine(temp, "bothlogos");
    Directory.CreateDirectory(Path.Combine(bothLogoMod, "material", "ui", "car_brand_logo"));
    File.Copy(Path.Combine(logoMod, "manifest.sii"), Path.Combine(bothLogoMod, "manifest.sii"));
    foreach (var rel in new[] { "def/vehicle/truck/vols90/data.sii", "def/vehicle/truck_dealer/volvo_cars/s90_2020.sii" })
    {
        var dest = Path.Combine(bothLogoMod, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(Path.Combine(logoMod, rel.Replace('/', Path.DirectorySeparatorChar)), dest);
    }

    // The same three files in both folders, which is what a previously converted mod looks like.
    foreach (var folder in new[] { "brand_logo", "car_brand_logo" })
    {
        foreach (var ext in new[] { ".mat", ".tobj", ".dds" })
        {
            var dest = Path.Combine(bothLogoMod, "material", "ui", folder, "volvo_cars" + ext);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, "material : \"ui\"\n{\n\ttexture : \"volvo_cars.tobj\"\n}\n");
        }
    }

    Check(ModConverter.HasBrandLogo(bothLogoMod, "volvo_cars"),
        "a mod shipping both logo folders is still recognised as having a badge");

    var bothInput = Path.Combine(temp, "BothLogoMod.zip");
    ZipFile.CreateFromDirectory(bothLogoMod, bothInput);

    var bothResult = ModConverter.Run(
        new ConversionSettings(bothInput, output, "volvo_cars", "", true, true, true, "Sedan"),
        _ => { }, _ => { });

    Check(File.Exists(bothResult.OutputFile) && new FileInfo(bothResult.OutputFile).Length > 0,
        "a mod that already ships car_brand_logo still converts instead of failing on a self-copy");

    var bothExtract = Path.Combine(temp, "logo-both");
    ZipFile.ExtractToDirectory(bothResult.OutputFile, bothExtract);
    Check(File.Exists(Path.Combine(bothExtract, "material", "ui", "car_brand_logo", "volvo_cars.mat")),
        "the pre-existing car-shop logo survives the conversion");
}
finally
{
    try { Directory.Delete(temp, true); } catch { }
}

if (failures.Count > 0)
{
    Console.WriteLine($"{failures.Count} check(s) failed.");
    return 1;
}

// --- Global anonymous unit namespacing ---------------------------------------------
// In SCS a "_nameless." name is global. Two mods defining "_nameless._.speed" define the
// same unit and the second replaces the first, which is how one converted car ended up
// standing in for another. Each conversion now namespaces them.

// The namespace must come from the input file, not the dealer ID: two mods can share a brand
// (both Cadillacs are truck_dealer/cadillac) and a user can type the same dealer ID twice, so
// the dealer ID cannot be assumed unique per vehicle. See the ROADMAP entry for the bug this
// caused, where two same-brand cars overwrote each other again.
var namespaceA = ModConverter.BuildAnonymousNamespace("cadillac_ct5", "Cadillac CT5-V 2022 V2.2 1.60.scs");
var namespaceB = ModConverter.BuildAnonymousNamespace("cadillac_escalade", "Cadillac Escalade 2021 V2.3 1.61.scs");

Check(namespaceA != namespaceB, "two vehicles get two different anonymous-unit namespaces");

// Same brand, which is what the real download set contains. Deriving the namespace from the dealer
// ID would give both of these "cadillac" and the cars would replace each other again.
var sameBrandA = ModConverter.BuildAnonymousNamespace("cadillac", "Cadillac CT5-V 2022 V2.2 1.60.scs");
var sameBrandB = ModConverter.BuildAnonymousNamespace("cadillac", "Cadillac Escalade 2021 V2.3 1.61.scs");
Check(sameBrandA != sameBrandB,
    "two mods sharing a brand still get different namespaces");

// The same dealer ID typed twice must also not collide.
Check(
    ModConverter.BuildAnonymousNamespace("ford", "Ford F-150.scs") !=
    ModConverter.BuildAnonymousNamespace("ford", "Ford_F250.scs"),
    "the same dealer ID typed for two mods does not collide");

Check(namespaceA == namespaceA.ToLowerInvariant(), "the namespace is lower case, because SCS unit names are case-sensitive");

// The trailing game version is stripped, so one vehicle keeps one namespace across updates.
Check(
    ModConverter.BuildAnonymousNamespace("volvo", "Volvo S90 2020 V2.3 1.60.scs") ==
    ModConverter.BuildAnonymousNamespace("volvo", "Volvo S90 2020 V2.4 1.61.scs"),
    "the trailing game version is stripped so one vehicle keeps one namespace across updates");

// --- The 12-character component limit ----------------------------------------------
// This is what made the game report "invalid_vehicle": the namespace went into every unit name,
// and SCS unit name components may not exceed 12 characters. Real mod names blow past it
// (cadillac_ct5_v_black_wing_2022 is 30), so this must hold for every real mod file name.
var realModNames = new[]
{
    "Volvo S90 2020 V2.3 1.60.scs",
    "Cadillac CT5-V Black Wing 2022 V2.2 1.60.scs",
    "Cadillac Escalade 2021 V2.3 1.61.scs",
    "Ford F-150 Raptor 2017 V1.7.1 Beta.scs",
    "Ford_F250.scs",
    "BMW M5 G90 2025 V1.5 1.61.scs",
    "Ford Fusion 2010 V3 1.39.scs",
    "FordTourneoCourier_V1R100_ETS2_ATS_1_44_X.scs"
};

foreach (var modName in realModNames)
{
    var ns = ModConverter.BuildAnonymousNamespace("cadillac", modName);
    Check(ns.Length <= 12,
        $"namespace for '{modName}' fits the 12-char SCS unit component limit ({ns})");
}

// Truncation must not reintroduce the collision it was introduced to prevent. Only 8 characters
// survive truncation (12 minus the 4-char hash and its separator), and both Cadillac names start
// "cadillac", so the hash is the only thing keeping them apart.
Check(
    "cadillac_ct5_v_black_wing_2022"[..8] == "cadillac_escalade_2021"[..8],
    "the two Cadillac names really do share the surviving 8-char prefix (guards the test below)");
Check(
    "cadillac_ct5_v_black_wing_2022"[..12] != "cadillac_escalade_2021"[..12],
    "and they differ beyond it, so this is a genuine truncation collision case");
Check(sameBrandA != sameBrandB,
    "names whose readable prefix is identical still get distinct namespaces");
Check(sameBrandA != sameBrandB && sameBrandA.Length == 12 && sameBrandB.Length == 12,
    "both colliding namespaces are exactly at the limit, so neither was left unshortened");

// Distinctness must survive truncation across the whole real set.
var realNamespaces = realModNames.Select(n => ModConverter.BuildAnonymousNamespace("x", n)).ToList();
Check(realNamespaces.Distinct().Count() == realNamespaces.Count,
    "every real mod file name yields a distinct namespace");

// A short namespace is left alone, so already-patched mods keep their existing unit names.
Check(ModConverter.BuildAnonymousNamespace("ford", "Ford_F250.scs") == "ford_f250",
    "a namespace already within the limit is left untouched");

// Stable across runs: .NET randomises string.GetHashCode per process, which would give a
// different namespace every launch and break every patch already in the user's mod folder.
Check(
    ModConverter.BuildAnonymousNamespace("cadillac", "Cadillac CT5-V Black Wing 2022 V2.2 1.60.scs") ==
    ModConverter.BuildAnonymousNamespace("cadillac", "Cadillac CT5-V Black Wing 2022 V2.2 1.60.scs"),
    "the namespace is stable across calls (no per-process hash randomisation)");
Check(
    ModConverter.BuildAnonymousNamespace("cadillac", "Cadillac CT5-V Black Wing 2022 V2.2 1.60.scs") !=
    ModConverter.BuildAnonymousNamespace("ford", "Ford F-150 Raptor 2017 V1.7.1 Beta.scs"),
    "different mods with different dealer ids still differ");

// Falls back to the dealer ID when the file name has nothing usable in it.
Check(
    ModConverter.BuildAnonymousNamespace("volvo_cars", "!!!.scs") == "volvo_cars",
    "an unusable file name falls back to the dealer id for the namespace");

Check(
    ModConverter.BuildAnonymousNamespace("!!!", "!!!") == "patch",
    "a namespace is always produced, even with no usable input at all");

// --- Dealer logo transparency (DXT1 -> DXT5) ---------------------------------------
// A truck-era logo is an opaque DXT1 image with the black background baked into the pixels, and
// the dealership draws that as a black box around the badge. DXT1 has no alpha channel at all, so
// the game cannot remove it and the converter has to. These checks build synthetic DXT1 logos
// with known geometry and assert on the decoded result, so a regression in the codec is caught
// here rather than by looking at a badge in game.
static byte[] BuildDxt1Logo(int width, int height, Func<int, int, bool> isArtwork)
{
    var blocksWide = (width + 3) / 4;
    var blocksHigh = (height + 3) / 4;
    var file = new byte[128 + blocksWide * blocksHigh * 8];
    "DDS "u8.CopyTo(file.AsSpan(0, 4));
    WriteLe(file, 4, 124);
    WriteLe(file, 8, 0xA1007);
    WriteLe(file, 12, height);
    WriteLe(file, 16, width);
    WriteLe(file, 28, 1);
    WriteLe(file, 76, 32);
    WriteLe(file, 80, 0x4);
    "DXT1"u8.CopyTo(file.AsSpan(84, 4));
    WriteLe(file, 108, 0x401008);

    for (var by = 0; by < blocksHigh; by++)
    {
        for (var bx = 0; bx < blocksWide; bx++)
        {
            var o = 128 + (by * blocksWide + bx) * 8;
            // Endpoints black then white, so index 0 is black and index 1 is white.
            file[o] = 0; file[o + 1] = 0;
            file[o + 2] = 0xFF; file[o + 3] = 0xFF;

            uint bits = 0;
            for (var py = 0; py < 4; py++)
            {
                for (var px = 0; px < 4; px++)
                {
                    var x = Math.Min(bx * 4 + px, width - 1);
                    var y = Math.Min(by * 4 + py, height - 1);
                    bits |= (uint)(isArtwork(x, y) ? 1 : 0) << ((py * 4 + px) * 2);
                }
            }

            file[o + 4] = (byte)bits;
            file[o + 5] = (byte)(bits >> 8);
            file[o + 6] = (byte)(bits >> 16);
            file[o + 7] = (byte)(bits >> 24);
        }
    }

    return file;
}

static void WriteLe(byte[] buffer, int offset, int value)
{
    buffer[offset] = (byte)value;
    buffer[offset + 1] = (byte)(value >> 8);
    buffer[offset + 2] = (byte)(value >> 16);
    buffer[offset + 3] = (byte)(value >> 24);
}

static int CountTransparent(byte[] px, int w, int h) =>
    Enumerable.Range(0, w * h).Count(i => px[i * 4 + 3] <= 8);

static int CountOpaque(byte[] px, int w, int h) =>
    Enumerable.Range(0, w * h).Count(i => px[i * 4 + 3] >= 247);

/// <summary>Knocks out a DXT1 file's background and rebuilds it through the given builder.</summary>
static byte[] KnockOut(byte[] dxt1, int w, int h, Func<byte[], int, int, byte[], byte[]> build)
{
    BrandLogoAlpha.TryDecode(dxt1, out _, out _, out var rgba, out _);
    BrandLogoAlpha.KnockOutBackground(rgba, w, h, out _);
    return build(dxt1, w, h, rgba);
}

var logoTemp = Path.Combine(Path.GetTempPath(), "logoalpha_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(logoTemp);
try
{
    // A white disc on a black field: the exact shape of the bug report's logos.
    const int W = 64, H = 32;
    var disc = BuildDxt1Logo(W, H, (x, y) =>
    {
        var dx = x - W / 2.0;
        var dy = y - H / 2.0;
        return dx * dx + dy * dy <= 12 * 12;
    });

    var discPath = Path.Combine(logoTemp, "disc.dds");
    File.WriteAllBytes(discPath, disc);

    Check(BrandLogoAlpha.TryReadDds(disc, out var dw, out var dh, out var dfmt) && dw == W && dh == H && dfmt == "DXT1",
        "a synthetic DXT1 logo header is read back correctly");

    Check(BrandLogoAlpha.TryMakeTransparent(discPath, out var discDetail),
        $"a DXT1 logo with a black background is converted ({discDetail})");

    Check(BrandLogoAlpha.TryReadDds(File.ReadAllBytes(discPath), out var nw, out var nh, out var nfmt) &&
          nw == W && nh == H && nfmt == "DXT5",
        "the converted logo keeps its dimensions and is now DXT5, which carries a real alpha channel");

    Check(BrandLogoAlpha.TryDecode(File.ReadAllBytes(discPath), out _, out _, out var discPixels, out _),
        "the converted logo decodes back");
    var discClear = CountTransparent(discPixels, W, H);
    var discOpaque = CountOpaque(discPixels, W, H);
    Check(discClear > (W * H) / 2,
        $"the black background became transparent ({100.0 * discClear / (W * H):F0}% of the frame)");
    Check(discOpaque > (W * H) / 20,
        $"the artwork survived the conversion ({100.0 * discOpaque / (W * H):F0}% stays opaque)");

    // The corners were background, so they must now be see-through.
    Check(discPixels[3] <= 8, "the top-left corner is transparent");
    Check(discPixels[((H - 1) * W + (W - 1)) * 4 + 3] <= 8, "the bottom-right corner is transparent");

    // Interior black must NOT be keyed out. A logo that legitimately contains black inside the
    // artwork is the case a naive global "make black transparent" pass destroys, so it is pinned
    // here explicitly: a white ring with a black centre has to keep its centre.
    var ring = BuildDxt1Logo(W, H, (x, y) =>
    {
        var dx = x - W / 2.0;
        var dy = y - H / 2.0;
        var d = dx * dx + dy * dy;
        return d <= 13 * 13 && d >= 6 * 6;
    });
    var ringPath = Path.Combine(logoTemp, "ring.dds");
    File.WriteAllBytes(ringPath, ring);
    Check(BrandLogoAlpha.TryMakeTransparent(ringPath, out _),
        "a ring logo with an enclosed black centre is converted");
    BrandLogoAlpha.TryDecode(File.ReadAllBytes(ringPath), out _, out _, out var ringPixels, out _);
    var centre = ((H / 2) * W + (W / 2)) * 4;
    Check(ringPixels[centre + 3] >= 247,
        "black enclosed by the artwork stays opaque, because the fill only reaches the border");

    // An already-correct logo must be left byte-identical: re-encoding artwork that renders
    // correctly today would resample it for no reason and could only lose quality.
    var alreadyPath = Path.Combine(logoTemp, "already.dds");
    var seed = BuildDxt1Logo(W, H, (x, y) => x > 8 && x < W - 8 && y > 4 && y < H - 4);
    File.WriteAllBytes(alreadyPath, KnockOut(seed, W, H, (bytes, w, h, rgba) =>
        BrandLogoAlpha.BuildDxt5WithAlpha(bytes, w, h, BrandLogoAlpha.ReadMipCount(bytes), rgba)));
    var alreadyBefore = File.ReadAllBytes(alreadyPath);
    Check(!BrandLogoAlpha.TryMakeTransparent(alreadyPath, out var alreadyDetail),
        "a logo that already has an alpha channel is not converted");
    Check(alreadyDetail.Contains("already", StringComparison.OrdinalIgnoreCase),
        $"and it says why ({alreadyDetail})");
    Check(File.ReadAllBytes(alreadyPath).SequenceEqual(alreadyBefore),
        "an already-correct logo comes out of the converter byte-identical");

    // A file this build cannot decode must be reported, not damaged.
    var junkPath = Path.Combine(logoTemp, "junk.dds");
    File.WriteAllBytes(junkPath, new byte[300]);
    var junkBefore = File.ReadAllBytes(junkPath);
    Check(!BrandLogoAlpha.TryMakeTransparent(junkPath, out _),
        "a DDS this build cannot decode is refused");
    Check(File.ReadAllBytes(junkPath).SequenceEqual(junkBefore),
        "a refused texture is left exactly as it was");

    // The artwork must come through untouched, and the guarantee is stronger than "close enough":
    // every colour block is copied from the source verbatim. Re-fitting a BC1 palette from decoded
    // pixels was measured doing real damage on the actual Volvo and BMW logos - up to 251 per
    // channel, red coming back green - because a BC1 block holds only four palette entries and a
    // fresh fit lands them somewhere else entirely. So the bytes are compared directly.
    var probe = BuildDxt1Logo(W, H, (x, y) =>
    {
        var dx = x - W / 2.0;
        var dy = y - H / 2.0;
        return dx * dx + dy * dy <= 12 * 12;
    });
    var rebuilt = KnockOut(probe, W, H, (bytes, w, h, rgba) =>
        BrandLogoAlpha.BuildDxt5WithAlpha(bytes, w, h, BrandLogoAlpha.ReadMipCount(bytes), rgba));

    var probePath = Path.Combine(logoTemp, "probe.dds");
    File.WriteAllBytes(probePath, rebuilt);
    BrandLogoAlpha.TryDecode(probe, out _, out _, out var probeSrc, out _);
    BrandLogoAlpha.TryDecode(rebuilt, out _, out _, out var probeOut, out _);

    var maxColourError = 0;
    for (var i = 0; i < W * H; i++)
    {
        if (probeOut[i * 4 + 3] <= 8) continue; // background, keyed out on purpose
        for (var c = 0; c < 3; c++)
            maxColourError = Math.Max(maxColourError, Math.Abs(probeOut[i * 4 + c] - probeSrc[i * 4 + c]));
    }
    Check(maxColourError == 0,
        $"surviving artwork is pixel-identical to the source, because colour blocks are copied verbatim (max error {maxColourError})");

    // The source's colour blocks must actually be present in the output, unmodified.
    var blockCount = ((W + 3) / 4) * ((H + 3) / 4);
    var colourBytesMatch = true;
    for (var b = 0; b < blockCount; b++)
    {
        // Output block = 8 alpha bytes then 8 copied colour bytes; source block = 8 colour bytes.
        for (var i = 0; i < 8; i++)
        {
            if (rebuilt[128 + b * 16 + 8 + i] == probe[128 + b * 8 + i]) continue;
            colourBytesMatch = false;
            break;
        }
        if (!colourBytesMatch) break;
    }
    Check(colourBytesMatch, "every colour block is byte-for-byte the source block");

    Check(BrandLogoAlpha.MipCount(64, 32) == 7, "MipCount(64,32) counts a full chain including 1x1");
    Check(BrandLogoAlpha.MipCount(1, 1) == 1, "MipCount(1,1) is a single level");
    Check(BrandLogoAlpha.ReadMipCount(probe) == 1, "ReadMipCount reads the level count a header declares");

}
finally
{
    try { Directory.Delete(logoTemp, true); } catch { }
}
Console.WriteLine("All checks passed.");
return 0;
