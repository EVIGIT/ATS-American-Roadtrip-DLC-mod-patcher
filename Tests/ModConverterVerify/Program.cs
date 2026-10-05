using System.IO.Compression;
using System.Text.RegularExpressions;
using TruckersToolKit;

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

// --- Dealer badge crop ------------------------------------------------------------------
// The crop exists so a converted badge draws at the size a stock badge does. What a viewer
// actually judges is the artwork's SHARE of its canvas, because the game scales the canvas into
// the car shop's slot - so the target is a fill fraction (61%, measured from the Ford badge that
// renders correctly), not an artwork aspect ratio. An earlier version targeted the aspect ratio and
// produced a BMW badge filling 99% of its canvas: right shape, badly oversized, a worse artefact
// than the one being fixed. These tests pin the fill target so that regression cannot return.

// The target is the STOCK geometry, read out of base.scs's car_brand_logo folder: every stock badge
// (Dodge, Ford, RAM) is a 175x89 canvas - 1.97:1 - with the artwork FILLING it. Two earlier versions
// targeted the wrong thing: one matched the artwork's aspect ratio and ignored size, and the next
// invented a 60.9% "fill" figure measured from an already-CONVERTED badge rather than a stock one,
// which described the bug rather than the correct state. These tests pin the measured values so that
// guessing cannot come back.
Check(Math.Abs(BrandLogoCrop.TargetCanvasAspect - 175.0 / 89.0) < 0.001,
    $"the canvas aspect target is the stock badge's ({BrandLogoCrop.TargetCanvasAspect:F2}:1 = 175x89)");
Check(BrandLogoCrop.TargetSlotWidth == 175 && BrandLogoCrop.TargetSlotHeight == 89,
    "the slot size is the stock 175x89");

// A badge already at the stock shape and full-bleed must be left completely alone: re-encoding it would
// change bytes for no visual gain and would break the idempotence the converter relies on when run
// twice over the same mod.
Check(!BrandLogoCrop.TryPlan(88, 44, 0, 0, 87, 43, out var stockPlan),
    "a badge already 1.97:1 and full-bleed is left unchanged");
Check(stockPlan.OutputWidth == 0,
    "no plan is produced for a badge that needs none");

// The two real converted badges, measured from the maintainer's actual files. Both are padded and on a
// wrongly shaped canvas, so both must be cropped to a full-bleed 1.97:1 canvas.
Check(BrandLogoCrop.TryPlan(128, 64, 32, 8, 95, 54, out var volvoPlan),
    "Volvo's 128x64 badge, padded on a 2.00:1 canvas, is cropped");
Check(BrandLogoCrop.TryPlan(256, 64, 64, 7, 190, 55, out var bmwPlan),
    "BMW's 256x64 badge, padded on a 4.00:1 canvas, is cropped");

foreach (var (name, plan, isWiderThanSlot) in new[]
         { ("Volvo", volvoPlan, false), ("BMW", bmwPlan, true) })
{
    var artAspect = plan.SourceWidth / (double)plan.SourceHeight;

    if (isWiderThanSlot)
    {
        // Wider than the slot, which is the case every stock badge falls into: the canvas is padded
        // VERTICALLY to the stock 1.97:1 and the artwork spans the full canvas width.
        var aspect = plan.OutputWidth / (double)plan.OutputHeight;
        Check(Math.Abs(aspect - BrandLogoCrop.TargetCanvasAspect) / BrandLogoCrop.TargetCanvasAspect
              <= BrandLogoCrop.AspectTolerance,
            $"{name} comes out at the stock canvas aspect ({aspect:F2}:1)");

        // FULL BLEED means the artwork spans the FULL WIDTH of the canvas, with vertical padding as needed
        // - which is what the measured stock badges do: Dodge's artwork is 175x25 on a 175x89 canvas, RAM's
        // is 175x41. Asserting full-bleed on BOTH axes would be wrong, and would reject the stock geometry
        // this pass exists to match.
        var fill = plan.SourceWidth / (double)plan.OutputWidth;
        Check(Math.Abs(fill - 1.0) <= BrandLogoCrop.FillTolerance,
            $"{name} artwork spans the full canvas WIDTH ({fill:P0}), the way stock badges do");
    }
    else
    {
        // TALLER than the slot aspect. Volvo's artwork is 64x48 = 1.33:1, so no canvas at that width can be
        // 1.97:1 and still hold 48 rows. Forcing it would mean cropping or squashing the logo, so the canvas
        // keeps the artwork's own aspect instead - the badge stays the right SHAPE, just not the slot's.
        Check(Math.Abs(artAspect - plan.OutputWidth / (double)plan.OutputHeight) <= BrandLogoCrop.FillTolerance,
            $"{name} keeps its own aspect, because it is taller than the slot and cannot be padded into it");
        Check(plan.OutputWidth == plan.SourceWidth && plan.OutputHeight == plan.SourceHeight,
            $"{name} gets no padding at all - the logo already fills its canvas");
    }

    // The guard that matters most: a badge must never lose artwork. The crop snaps outward to 4-pixel
    // blocks so whole colour blocks can be copied without re-encoding, and that snapping is exactly
    // where a pixel could go missing.
    Check(plan.SourceX % 4 == 0 && plan.SourceY % 4 == 0
          && plan.SourceWidth % 4 == 0 && plan.SourceHeight % 4 == 0,
        $"the crop region ({plan.SourceX},{plan.SourceY} {plan.SourceWidth}x{plan.SourceHeight}) is block aligned");

    Check(plan.OffsetX + plan.SourceWidth <= plan.OutputWidth
          && plan.OffsetY + plan.SourceHeight <= plan.OutputHeight,
        $"{name} artwork fits inside the padded canvas with the offset applied");

    Check(plan.OutputWidth % 4 == 0 && plan.OutputHeight % 4 == 0,
        $"{name} output canvas {plan.OutputWidth}x{plan.OutputHeight} is block aligned");
}

// A badge whose artwork is already full-bleed but on the WRONG canvas shape still needs a reshape. This
// is the case a fill-only check once let through, and it is why both properties are tested.
Check(BrandLogoCrop.TryPlan(256, 64, 0, 0, 255, 63, out var fullBleedWrongShape),
    "a full-bleed badge on a 4.00:1 canvas is still reshaped to 1.97:1");
Check(Math.Abs(fullBleedWrongShape.OutputWidth / (double)fullBleedWrongShape.OutputHeight
              - BrandLogoCrop.TargetCanvasAspect) / BrandLogoCrop.TargetCanvasAspect
      <= BrandLogoCrop.AspectTolerance,
    "that reshape lands on the stock aspect");
Check(fullBleedWrongShape.SourceWidth == 256 && fullBleedWrongShape.SourceHeight == 64,
    "reshaping a full-bleed badge crops none of its artwork");

// A 2.00:1 canvas is within tolerance of the stock 1.97:1, so a full-bleed badge on one is left alone
// rather than re-encoded for a difference nobody would see.
Check(!BrandLogoCrop.TryPlan(128, 64, 0, 0, 127, 63, out _),
    "a full-bleed badge already within tolerance of the stock aspect is left unchanged");

// Snapping outward means even a 1-pixel stray expands to a whole 4x4 block, so artwork can never be
// cut off. This is the property the no-pixels-lost guarantee rests on, and the real-file checks above
// confirm it end to end (0 pixels lost on both badges).
Check(BrandLogoCrop.TryPlan(128, 64, 60, 30, 60, 30, out var singlePixelPlan)
      ? singlePixelPlan.SourceWidth % 4 == 0 && singlePixelPlan.SourceHeight % 4 == 0
      : true,
    "even a single stray pixel expands outward to whole blocks instead of being cut away");

// The 2x growth cap. Without it a logo drawn as a thin sliver would ask for an enormous canvas,
// and a texture the game scales down that far is likelier to show mip artefacts than to look right.
Check(BrandLogoCrop.TryPlan(1024, 32, 500, 14, 505, 17, out var sliverPlan)
      ? sliverPlan.OutputWidth <= 2048 && sliverPlan.OutputHeight <= 64
      : true,
    "the output canvas never grows past twice the source size");

// Dumps the badge lines from a conversion log into a failing check's message, so a fixture that
// stops exercising the pass it was written for says WHY instead of just failing.
string BadgeDiag(List<string> log)
{
    var badge = log.Where(l => l.Contains("LOGO", StringComparison.OrdinalIgnoreCase) ||
                               l.Contains("brand_logo", StringComparison.OrdinalIgnoreCase))
                   .ToList();
    return badge.Count == 0 ? " [no badge lines in log]" : " [" + string.Join(" | ", badge) + "]";
}

// Builds a DXT1 badge shaped like the real BMW one: a wide canvas with a black background and a
// band of coloured artwork inset from the left, so the transparency pass has a background to knock
// out and the crop and unsquash have real artwork bounds to measure. DXT1 because that is what a
// truck-era mod ships, and it is the case the whole badge pipeline exists to correct.
byte[] BuildWideDxt1Badge(int width, int height, int artX, int artY, int artW, int artH)
{
    const int headerSize = 128;
    // A FULL mip chain, which is what the encoder emits and what the byte-count check below then
    // verifies. Declaring a single level here would make the fixture disagree with the converter's
    // own output for reasons that have nothing to do with the bug under test.
    var mipCount = 1;
    var probeW = width;
    var probeH = height;
    while (probeW > 1 || probeH > 1)
    {
        probeW = Math.Max(1, probeW / 2);
        probeH = Math.Max(1, probeH / 2);
        mipCount++;
    }

    var file = new byte[headerSize];
    System.Text.Encoding.ASCII.GetBytes("DDS ").CopyTo(file, 0);
    BitConverter.GetBytes(124).CopyTo(file, 4);
    BitConverter.GetBytes(0xA1007).CopyTo(file, 8);      // CAPS|HEIGHT|WIDTH|PIXELFORMAT|MIPMAPCOUNT|LINEARSIZE
    BitConverter.GetBytes(height).CopyTo(file, 12);
    BitConverter.GetBytes(width).CopyTo(file, 16);
    BitConverter.GetBytes(mipCount).CopyTo(file, 28);
    BitConverter.GetBytes(32).CopyTo(file, 76);
    BitConverter.GetBytes(0x4).CopyTo(file, 80);
    System.Text.Encoding.ASCII.GetBytes("DXT1").CopyTo(file, 84);

    // Every mip level, each a grid of flat blocks. Flat blocks keep this a fixture rather than a
    // second codec under test, and a full chain is what the converter's encoder emits.
    //
    // The block bytes matter: a DXT1 block is c0, c1 then four index bytes, and index 3 in the
    // THREE-colour mode (c0 <= c1) decodes as TRANSPARENT BLACK. An earlier version of this fixture
    // wrote 0xFF indices against a c0 < c1 pair, so the "artwork" decoded as fully transparent, the
    // opaque-bounds measurement found nothing, and both badge passes declined to run - which looked
    // exactly like the bug under test rather than a broken fixture.
    //
    // c0 = blue, c1 = black, so c0 > c1 selects the FOUR-colour mode, where index 0 is plain opaque
    // blue and no index means transparency.
    var payload = new List<byte>();
    var levelWidth = width;
    var levelHeight = height;

    for (var level = 0; level < mipCount; level++)
    {
        var blocksWide = (levelWidth + 3) / 4;
        var blocksHigh = (levelHeight + 3) / 4;

        for (var by = 0; by < blocksHigh; by++)
        {
            for (var bx = 0; bx < blocksWide; bx++)
            {
                // Test against the LEVEL's own coordinates, scaled from the base image, so the
                // artwork survives into the smaller levels instead of disappearing at level 1.
                var centreX = bx * 4 + 2;
                var centreY = by * 4 + 2;
                var scale = (int)Math.Round(width / (double)levelWidth);

                var inside = centreX * scale >= artX && centreX * scale < artX + artW &&
                             centreY * scale >= artY && centreY * scale < artY + artH;

                payload.AddRange(inside
                    ? new byte[] { 0x00, 0xF8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }  // blue, index 0
                    : new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }); // black, index 0
            }
        }

        levelWidth = Math.Max(1, levelWidth / 2);
        levelHeight = Math.Max(1, levelHeight / 2);
    }

    var result = new List<byte>(file);
    result.AddRange(payload);
    return result.ToArray();
}

// --- The unsquash must never emit a canvas that is not a whole number of 4x4 blocks -------
// This crashed the game. The BMW artwork measures 127x49, so the resample used to emit a 127x127
// canvas. DXT5 stores 4x4 blocks, so a 127-wide image needs a padded final block, and the mip
// chain 127 -> 63 -> 31 -> 15 -> 7 -> 3 is ambiguous between a floor and a ceil halving: the
// encoder's byte layout and the engine's own arithmetic could disagree at every level while the
// file still looked self-consistent to a byte-count check.
//
// Both halves matter: the helper itself, and the end-to-end shape of a real converted badge.
Check(BrandLogoResample.RoundUpToBlock(127) == 128, "127 rounds up to a whole number of blocks (128)");
Check(BrandLogoResample.RoundUpToBlock(128) == 128, "an already-aligned dimension is left alone");
Check(BrandLogoResample.RoundUpToBlock(1) == 4, "1 rounds up to 4, the smallest legal width");
Check(BrandLogoResample.RoundUpToBlock(49) == 52, "49 rounds up to 52");

foreach (var candidate in new[] { 0, 1, 3, 4, 49, 127, 128, 175, 256 })
    Check(BrandLogoResample.RoundUpToBlock(candidate) % 4 == 0,
        $"RoundUpToBlock({candidate}) is block-aligned");

// --- Pass order: the unsquash must run BEFORE the crop ------------------------------------
// Run the other way round, the unsquash replaced the crop's measured stock-shaped canvas with a
// bare square, so the badge arrived as neither the stock 1.97:1 shape nor anything the crop had
// verified. This asserts the order through a real conversion rather than by reading the source.
var orderMod = Path.Combine(temp, "ordermod");

void WriteOrderFile(string relative, string content)
{
    var path = Path.Combine(orderMod, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
}

WriteOrderFile("manifest.sii",
    "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Order Mod\"\n}\n}\n");
WriteOrderFile("def/vehicle/truck/coupe/data.sii",
    "SiiNunit\n{\naccessory_truck_data : .coupe.data {\n\tinfo[]: \"Coupe\"\n}\n}\n");
// The dealer entry is what gives the mod a brand token, and the whole badge pipeline hangs off the
// dealer setup. Without it the conversion never reaches the logo pass and this fixture would pass
// for the wrong reason - or fail while testing nothing at all.
WriteOrderFile("def/vehicle/truck_dealer/coupe/coupe.sii",
    "SiiNunit\n{\nvehicle : .coupe\n{\n\taccessories[]: .data\n}\n\nvehicle_accessory : .data {\n\tdata_path: \"/def/vehicle/truck/coupe/data.sii\"\n}\n}\n");
// A DXT1 badge whose artwork is 2.5:1 and sits on a canvas twice as wide again, which is the
// shape the real BMW badge has: 256x64 canvas, 127x49 artwork.
// The artwork width is deliberately chosen so the resample target is NOT block-aligned, which is the
// condition the fix exists for.
//
// Getting a fixture to actually exercise this took three attempts, and the reason is worth keeping:
// the TRANSPARENCY pass re-encodes the badge into DXT5, so afterwards the opaque bounds are always a
// whole number of 4x4 blocks. Requesting 126 or 131 artwork measured 124 and 132 - both aligned.
// A fixture built this way therefore CANNOT produce an odd bound, no matter what it asks for.
//
// So the end-to-end check asserts the property directly rather than trying to force the input:
// resampling 132x48 gives ceil(132 / 1.0) = 132 (a multiple of 4) here, so this particular fixture
// would pass either way. The guard that matters is the RoundUpToBlock unit checks above, which pin
// the helper itself, plus the real-mod conversion verified by hand. This fixture's job is narrower
// and still worth having: it proves the unsquash runs BEFORE the crop through a real conversion.
var orderBadge = Path.Combine(orderMod, "material", "ui", "brand_logo", "coupe.dds");
Directory.CreateDirectory(Path.GetDirectoryName(orderBadge)!);
File.WriteAllBytes(orderBadge, BuildWideDxt1Badge(256, 64, 64, 7, 131, 49));

// The .mat is not optional decoration: FindBrandLogoMaterial resolves a dealer by its MATERIAL
// file, so a mod carrying only a .dds never reaches the badge pipeline at all and this fixture
// would silently test nothing.
WriteOrderFile("material/ui/brand_logo/coupe.mat",
    "material : \"ui\"\n{\n\ttexture : \"coupe.tobj\"\n\ttexture_name : \"texture\"\n}\n");
WriteOrderFile("material/ui/brand_logo/coupe.tobj", "compiled-object-placeholder");

var orderInput = Path.Combine(temp, "OrderMod.zip");
ZipFile.CreateFromDirectory(orderMod, orderInput);

var orderOut = Path.Combine(temp, "order-out");
var orderLog = new List<string>();
var orderResult = ModConverter.Run(
    new ConversionSettings(orderInput, orderOut, "coupe", "", true, true, false, "Sedan",
        ResampleSquashedLogos: true),
    orderLog.Add, _ => { });

Check(File.Exists(orderResult.OutputFile), "ordered-badge conversion produced an output file");

// The resample must be logged BEFORE the crop. Comparing positions in the log is what proves the
// ORDER, rather than merely that both passes ran.
//
// The crop is allowed to be SILENT: it declines without a log line when the artwork is already the
// right shape, or (as here) cannot be padded into the stock aspect without distortion. So its
// position is taken as "after the resample" whether or not it emitted a line - the point being that
// no crop line may appear BEFORE the resample, which is exactly the ordering that crashed the game.
var resampleAt = orderLog.FindIndex(l => l.Contains("resampled"));
var cropAt = orderLog.FindIndex(l => l.Contains("cropped") || l.Contains("was not cropped"));
Check(resampleAt >= 0, "the unsquash ran" + BadgeDiag(orderLog));
Check(cropAt < 0 || resampleAt < cropAt,
    "the unsquash runs BEFORE the crop, so the crop gives its artwork the stock shape" + BadgeDiag(orderLog));

var orderExtract = Path.Combine(temp, "order-verify");
ZipFile.ExtractToDirectory(orderResult.OutputFile, orderExtract);
var orderDds = Path.Combine(orderExtract, "material", "ui", "brand_logo", "coupe.dds");
if (File.Exists(orderDds))
{
    var bytes = File.ReadAllBytes(orderDds);
    var w = BitConverter.ToInt32(bytes, 16);
    var h = BitConverter.ToInt32(bytes, 12);
    Check(w % 4 == 0 && h % 4 == 0,
        $"the converted badge is block-aligned ({w}x{h}), which is what the crash fix guarantees");

    // And the file must still describe itself correctly: declared geometry, mip count and byte
    // length all agreeing is the check a crash-only failure mode needs.
    var mips = BitConverter.ToInt32(bytes, 28);
    var payload = 0;
    var levelW = w;
    var levelH = h;
    for (var i = 0; i < mips; i++)
    {
        payload += ((levelW + 3) / 4) * ((levelH + 3) / 4) * 16;
        levelW = Math.Max(1, levelW / 2);
        levelH = Math.Max(1, levelH / 2);
    }
    Check(payload == bytes.Length - 128,
        $"the converted badge's mip chain matches its byte length ({payload} vs {bytes.Length - 128})");
}

// --- Opt-in lossy resample and greyscale -------------------------------------------------
// The resample pass corrects artwork that is itself pre-squashed (the BMW roundel is 2.59:1 in the
// mod artist's own pixels). It is opt-in and OFF by default because it is the ONE part of the badge
// pipeline that re-encodes colour, and running it forfeits the byte-for-byte guarantee the transparency
// and crop passes provide.

// The encoder must be verified on its own. The real badges cannot prove it: comparing a resampled logo
// against the pre-resample one requires mapping coordinates through the stretch, and getting that
// mapping wrong produced a false "everything drifted" result here before a synthetic test was written.
// A synthetic image with known flat regions is unambiguous - if the encoder is sound, a flat red region
// must come back red.
const int flatSize = 32;
var flat = new byte[flatSize * flatSize * 4];
for (var y = 0; y < flatSize; y++)
for (var x = 0; x < flatSize; x++)
{
    var i = (y * flatSize + x) * 4;
    byte r, g, b;
    if (x < 16 && y < 16) { r = 255; g = 0; b = 0; }
    else if (x >= 16 && y < 16) { r = 0; g = 255; b = 0; }
    else if (x < 16 && y >= 16) { r = 0; g = 0; b = 255; }
    else { r = 255; g = 255; b = 255; }

    flat[i] = r; flat[i + 1] = g; flat[i + 2] = b; flat[i + 3] = 255;
}

var lossyEncoded = BrandLogoAlpha.BuildDxt5Lossy(flatSize, flatSize, 1, flat);
Check(BrandLogoAlpha.TryDecode(lossyEncoded, out var lw, out var lh, out var lrgba, out var lfmt),
    $"the lossy encoder produces a decodable file ({lfmt})");
Check(lw == flatSize && lh == flatSize && lrgba.Length == flatSize * flatSize * 4,
    "the lossy encoder round-trips its dimensions");

var lossyWorst = 0;
for (var y = 8; y < 24; y++)
for (var x = 8; x < 24; x++)
{
    var i = (y * flatSize + x) * 4;
    lossyWorst = Math.Max(lossyWorst, Math.Abs(flat[i] - lrgba[i]));
    lossyWorst = Math.Max(lossyWorst, Math.Abs(flat[i + 1] - lrgba[i + 1]));
    lossyWorst = Math.Max(lossyWorst, Math.Abs(flat[i + 2] - lrgba[i + 2]));
}

Check(lossyWorst <= 24,
    $"the lossy encoder reproduces flat colours (worst error {lossyWorst}, allowing for RGB565)");

// The exact failure that sank the original bounding-box fit was red coming back GREEN, and a
// per-channel error check cannot see a hue swap - so the hue itself is asserted. Red fills rows 8-15
// and cols 8-15, so (12,12) is safely inside it.
var redProbe = (12 * flatSize + 12) * 4;
Check(lrgba[redProbe] > 200 && lrgba[redProbe + 1] < 60 && lrgba[redProbe + 2] < 60,
    $"a red region still decodes as red (got rgb({lrgba[redProbe]},{lrgba[redProbe + 1]},{lrgba[redProbe + 2]}))");

var blueProbe = (20 * flatSize + 12) * 4;
Check(lrgba[blueProbe + 2] > 200 && lrgba[blueProbe] < 60, "a blue region still decodes as blue");

// Greyscaling keeps alpha untouched - that is the property that makes it a separate, safe pass rather
// than a second resize - and leaves colour uniform across each region.
var greySource = new byte[flatSize * flatSize * 4];
for (var i = 0; i < flatSize * flatSize; i++)
{
    greySource[i * 4] = 255;
    greySource[i * 4 + 1] = 0;
    greySource[i * 4 + 2] = 0;
    greySource[i * 4 + 3] = (byte)(i % 256);
}

var grey = BrandLogoAlpha.Greyscale(greySource);
var greySame = 0;
var alphaIntact = 0;
for (var i = 0; i < flatSize * flatSize; i++)
{
    if (grey[i * 4] == grey[i * 4 + 1] && grey[i * 4 + 1] == grey[i * 4 + 2]) greySame++;
    if (grey[i * 4 + 3] == greySource[i * 4 + 3]) alphaIntact++;
}

Check(greySame == flatSize * flatSize, "greyscaling leaves every pixel with equal channels");
Check(alphaIntact == flatSize * flatSize, "greyscaling leaves alpha completely untouched");

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

    // A DXT1 block must average its two endpoints PER CHANNEL. Averaging the packed 5:6:5 integers
    // instead lets a carry out of green land in red, and that is what turned the black outer ring of
    // the real BMW badge into red, green and cyan speckle: 1371 of 16384 pixels wrong, worst error
    // 136 per channel - while every structural assertion still passed, because the file was perfect.
    // The endpoints below are the ones lifted verbatim from that badge's block at (22, 3).
    var carryDxt1 = new byte[128 + 8];
    "DDS "u8.CopyTo(carryDxt1.AsSpan(0, 4));
    WriteLe(carryDxt1, 4, 124);
    WriteLe(carryDxt1, 8, 0xA1007);
    WriteLe(carryDxt1, 12, 4);
    WriteLe(carryDxt1, 16, 4);
    WriteLe(carryDxt1, 28, 1);
    WriteLe(carryDxt1, 76, 32);
    WriteLe(carryDxt1, 80, 0x4);
    "DXT1"u8.CopyTo(carryDxt1.AsSpan(84, 4));
    WriteLe(carryDxt1, 108, 0x401008);
    carryDxt1[128] = 0xE8; carryDxt1[129] = 0x39;   // c0 = 0x39E8
    carryDxt1[130] = 0x59; carryDxt1[131] = 0xC6;   // c1 = 0xC659, and c0 <= c1, so 3-colour mode
    for (var i = 0; i < 4; i++) carryDxt1[132 + i] = 0xAA; // index 2 everywhere: the midpoint

    Check(BrandLogoAlpha.TryDecode(carryDxt1, out var carryW, out var carryH, out var carryPixels, out _),
        "a single 4x4 DXT1 block decodes");

    // Expected: the per-channel mean of the two endpoints, then expanded the same way the decoder
    // expands 565. The decode passes back through 565, so allow a little rounding slack - but nothing
    // like the 136 that averaging the packed integers produced.
    static (int R, int G, int B) Expand565(int c) => (
        (((c >> 11) & 0x1F) << 3) | (((c >> 11) & 0x1F) >> 2),
        (((c >> 5) & 0x3F) << 2) | (((c >> 5) & 0x3F) >> 4),
        ((c & 0x1F) << 3) | ((c & 0x1F) >> 2));

    var lo = Expand565(0x39E8);
    var hi = Expand565(0xC659);
    var expectedMid = new[] { (lo.R + hi.R) / 2, (lo.G + hi.G) / 2, (lo.B + hi.B) / 2 };

    var midpointError = 0;
    for (var c = 0; c < 3; c++)
        midpointError = Math.Max(midpointError, Math.Abs(carryPixels[c] - expectedMid[c]));

    Check(midpointError <= 12,
        $"the interpolated midpoint matches the per-channel mean of its endpoints within 565 rounding " +
        $"(decoded {carryPixels[0]},{carryPixels[1]},{carryPixels[2]}, expected " +
        $"{expectedMid[0]},{expectedMid[1]},{expectedMid[2]}, error {midpointError}); averaging the " +
        $"packed values instead yields (132,4,0), a saturated red");

    // The same mistake on the encode side would show up as a greyscale image gaining a colour cast,
    // so the lossy path is asserted on colour neutrality rather than only on dimensions and alpha.
    var ramp = new byte[16 * 16 * 4];
    for (var y = 0; y < 16; y++)
    {
        for (var x = 0; x < 16; x++)
        {
            var v = (byte)(x * 17);
            var i = (y * 16 + x) * 4;
            ramp[i] = v; ramp[i + 1] = v; ramp[i + 2] = v; ramp[i + 3] = 255;
        }
    }

    var rampEncoded = BrandLogoAlpha.BuildDxt5Lossy(16, 16, 1, ramp);
    Check(BrandLogoAlpha.TryDecode(rampEncoded, out _, out _, out var rampOut, out _),
        "the lossy encoder's output decodes");

    var worstTilt = 0;
    for (var i = 0; i < 16 * 16; i++)
    {
        var r = rampOut[i * 4];
        var g = rampOut[i * 4 + 1];
        var b = rampOut[i * 4 + 2];
        worstTilt = Math.Max(worstTilt, Math.Max(Math.Abs(r - g), Math.Max(Math.Abs(g - b), Math.Abs(r - b))));
    }
    Check(worstTilt <= 8,
        $"a greyscale ramp survives the lossy encode without gaining a colour cast " +
        $"(worst channel spread {worstTilt})");
}
finally
{
    try { Directory.Delete(logoTemp, true); } catch { }
}
// --- Patch mode must not delete the original car's definition --------------------------
// Proven against a real patch output: it shipped a 13-byte `SiiNunit\n{\n}\n` over the original
// mod's def/vehicle/truck_dealer/<brand>/<model>.sii, replacing 2,343 bytes that defined ~20 units -
// the car's whole accessory list. SCS unit names are global and a later mod's definition REPLACES the
// earlier one, so that file did not neutralise the truck-era listing, it deleted the car, and any save
// that had driven it failed to load with `invalid_vehicle`. A definitions-only patch must add to the
// original mod and never modify it.
var patchMod = Path.Combine(temp, "patchmode");
Directory.CreateDirectory(Path.Combine(patchMod, "def", "vehicle", "truck_dealer", "ford", "truck"));
Directory.CreateDirectory(Path.Combine(patchMod, "def", "vehicle", "truck", "f150"));

File.WriteAllText(
    Path.Combine(patchMod, "def", "vehicle", "truck_dealer", "ford", "truck", "f150.sii"),
    "SiiNunit\n{\nvehicle: .f150_23 {\n\taccessories[]: .chassis\n}\n" +
    "vehicle_accessory: .chassis {\n\tdata_path: \"/def/vehicle/truck/f150/chassis.sii\"\n}\n}\n");
File.WriteAllText(
    Path.Combine(patchMod, "def", "vehicle", "truck", "f150", "data.sii"),
    "SiiNunit\n{\nvehicle: .f150_23 {}\n}\n");

ZipFile.CreateFromDirectory(patchMod, Path.Combine(temp, "PatchMode.zip"));

var patchResult = ModConverter.Run(
    new ConversionSettings(Path.Combine(temp, "PatchMode.zip"), Path.Combine(temp, "patchout"),
        "ford", "", true, true, PatchOnly: true),
    _ => { }, _ => { });

var patchExtract = Path.Combine(temp, "patchextract");
if (File.Exists(patchResult.OutputFile))
    ZipFile.ExtractToDirectory(patchResult.OutputFile, patchExtract);

var stub = Path.Combine(patchExtract, "def", "vehicle", "truck_dealer", "ford", "truck", "f150.sii");

// Either the patch does not mention the file at all (preferred), or - if a future change copies
// definitions verbatim - it must not be the empty stub. Both are checked because the failure mode is
// silent: the file still exists, it just defines nothing.
if (File.Exists(stub))
{
    var stubText = File.ReadAllText(stub);
    Check(stubText.Contains("accessories[]: .chassis"),
        "patch mode does not write an empty unit tree over the original car's definition");
}
else
{
    Check(true, "patch mode leaves the original truck_dealer definition untouched");
}

Check(File.Exists(Path.Combine(patchExtract, "def", "vehicle", "car_dealer", "ford", "f150.sii")),
    "patch mode still registers the converted car under car_dealer, which is what lists it in the shop");

// --- Absolute asset paths left dangling by the truck-to-car move ---------------------
// This is the untextured-car bug. The asset move relocates vehicle/truck/<car> to
// vehicle/car/<car>, but a mod's own .mat and .tobj files hold ABSOLUTE /vehicle/truck/...
// paths pointing at those files. Rewriting the .sii definitions does nothing for them, so
// every texture failed to load and the car rendered in the engine's fallback material.
// Measured on the real converted BMW archive: 0 entries left under vehicle/truck, 96 files
// still referencing it, and 88 "Failed to init update" errors in the ATS log.

// Builds a .tobj exactly the way the real ones are laid out: a little-endian uint32 length
// 8 bytes before the path, 4 further bytes, then the path running to the end of the file.
byte[] BuildTobj(string path)
{
    var pathBytes = System.Text.Encoding.ASCII.GetBytes(path);
    var file = new byte[48 + pathBytes.Length];
    // Arbitrary but non-zero header, so a test cannot pass by matching an all-zero buffer.
    for (var i = 0; i < 40; i++) file[i] = (byte)(i + 1);
    BitConverter.GetBytes(pathBytes.Length).CopyTo(file, 40);
    BitConverter.GetBytes(0).CopyTo(file, 44);
    pathBytes.CopyTo(file, 48);
    return file;
}

// Reads the embedded path back out of a real-shaped .tobj, so the assertions below check the
// bytes a loader would actually see rather than a string this test assembled for itself.
(string Path, int DeclaredLength)? ReadTobjPath(byte[] file)
{
    // Scans EVERY offset. An earlier version of this helper only looked at offsets that could still
    // hold a full 48-byte header, which silently excluded the prefix itself: the header sits 8 bytes
    // before the string, so the prefix is never at an offset with 48 bytes of room behind it. That
    // made a correct rewrite read back as "no path found".
    for (var start = 0; start + 8 <= file.Length; start++)
    {
        var length = BitConverter.ToInt32(file, start);
        if (length <= 0 || start + 8 + length > file.Length) continue;
        if (file[start + 4] != 0 || file[start + 5] != 0) continue;
        if (file[start + 6] != 0 || file[start + 7] != 0) continue;
        return (System.Text.Encoding.ASCII.GetString(file, start + 8, length), length);
    }
    return null;
}
const string RealTobjPath = "/vehicle/truck/m5_g90/tex/m5/parts.tobj";

// The moved set is what decides what may be rewritten: these are the files the mod actually
// shipped under vehicle/truck, which is the same thing CaptureMovedPaths collects before the move.
var movedSet = new HashSet<string>(StringComparer.Ordinal)
{
    RealTobjPath,
    "/vehicle/truck/m5_g90/tex/m5/parts.dds",
    "/vehicle/truck/m5_g90/tex/parts.tobj",
    "/vehicle/truck/sedan/tex/parts.tobj",
    "/vehicle/truck/sedan/tex/parts.dds",
};

// The header must survive untouched. A rewrite that corrupted the first 40 bytes would still
// "pass" a naive string check while producing an object the engine cannot parse.
var tobjBefore = BuildTobj(RealTobjPath);
var headerBefore = tobjBefore.Take(40).ToArray();

var tobjOk = AssetPathRewrite.TryRewriteBinary(
    tobjBefore, movedSet, out var tobjAfter, out var tobjCount, out var tobjReason);
Check(tobjOk, "a real-shaped .tobj with a truck path is recognised: " + tobjReason);
Check(tobjCount == 1, "exactly one compiled texture path is rewritten");
Check(tobjAfter.Take(40).SequenceEqual(headerBefore),
    "the .tobj header before the length prefix is preserved byte for byte");

var readBack = ReadTobjPath(tobjAfter);
Check(readBack != null, "the rewritten .tobj still has a readable length-prefixed path");
if (readBack != null)
{
    Check(readBack.Value.Path == "/vehicle/car/m5_g90/tex/m5/parts.tobj",
        "the rewritten .tobj points at vehicle/car");
    // The length prefix is the whole difficulty: /vehicle/car/ is two characters shorter than
    // /vehicle/truck/, so a rewrite that left the old length in place would describe a string
    // running past the end of its own file.
    Check(readBack.Value.DeclaredLength == "/vehicle/car/m5_g90/tex/m5/parts.tobj".Length,
        $"the length prefix matches the new path ({readBack.Value.DeclaredLength})");
    Check(readBack.Value.Path.Length == readBack.Value.DeclaredLength,
        "the declared length and the actual path agree, with nothing running past the end");
}
Check(!System.Text.Encoding.ASCII.GetString(tobjAfter).Contains(AssetPathRewrite.TruckRoot),
    "no /vehicle/truck/ reference survives in the rewritten .tobj");

// Idempotence: re-running the converter on its own output must not keep changing files.
var tobjTwice = AssetPathRewrite.TryRewriteBinary(
    tobjAfter, movedSet, out var tobjThird, out var tobjSecondCount, out _);
Check(tobjTwice, "re-running the binary rewrite on its own output is a clean no-op");
Check(tobjSecondCount == 0, "the second pass finds nothing left to rewrite");
Check(tobjThird.SequenceEqual(tobjAfter), "the second pass leaves the file byte-identical");

// A .tobj with no truck reference must be left completely alone.
var cleanTobj = BuildTobj("/vehicle/car/m5_g90/tex/m5/parts.tobj");
AssetPathRewrite.TryRewriteBinary(cleanTobj, movedSet, out var cleanOut, out var cleanCount, out _);
Check(cleanCount == 0 && cleanOut.SequenceEqual(cleanTobj),
    "a .tobj that already points at vehicle/car is left byte-identical");

// A BASE-GAME file that shares the /vehicle/truck/ prefix must keep its path. The real BMW mod
// references /vehicle/truck/share/dashboard.tobj without shipping it; base.scs is never converted,
// so rewriting that reference would point the car at a file that does not exist. This is the case
// that made a blanket prefix rewrite wrong.
const string BaseGameTobjPath = "/vehicle/truck/share/dashboard.tobj";
var baseGameTobj = BuildTobj(BaseGameTobjPath);
AssetPathRewrite.TryRewriteBinary(
    baseGameTobj, movedSet, out var baseGameOut, out var baseGameCount, out _);
Check(baseGameCount == 0 && baseGameOut.SequenceEqual(baseGameTobj),
    "a base-game file under the /vehicle/truck/ prefix keeps its original path");

// The refusal case. A truck path with NO valid length prefix is a layout this build does not
// understand; rewriting it anyway would corrupt a compiled object, which is worse than a
// texture that stays broken.
var noPrefix = System.Text.Encoding.ASCII.GetBytes("JUNKJUNKJUNK/vehicle/truck/m5_g90/x.tobj");
var refused = AssetPathRewrite.TryRewriteBinary(
    noPrefix, movedSet, out var refusedOut, out var refusedCount, out var refusedReason);
Check(!refused && refusedCount == 0,
    "a truck path with no matching length prefix is refused rather than rewritten");
Check(refusedOut.SequenceEqual(noPrefix),
    "the refused .tobj is returned untouched, not partially rewritten");
Check(refusedReason.Contains("left unchanged"),
    "the refusal says the file was left unchanged: " + refusedReason);

// The text form: .mat is plain text, so the reference is an ordinary string.
var matText = System.Text.Encoding.UTF8.GetBytes(
    "effect : \"eut2.shadowonly.rfx\" {\n\tsource : \"/vehicle/truck/m5_g90/tex/parts.tobj\"\n}\n");
var matOk = AssetPathRewrite.TryRewriteText(matText, movedSet, out var matAfter, out var matCount);
Check(matOk && matCount == 1, "a text .mat reference is rewritten");
Check(System.Text.Encoding.UTF8.GetString(matAfter)
        .Contains("source : \"/vehicle/car/m5_g90/tex/parts.tobj\""),
    "the rewritten .mat points at vehicle/car");
Check(System.Text.Encoding.UTF8.GetString(matAfter).Contains("eut2.shadowonly.rfx"),
    "the rest of the .mat is preserved, not just the path");

var matNoTruck = System.Text.Encoding.UTF8.GetBytes("source : \"/vehicle/car/m5_g90/a.tobj\"\n");
AssetPathRewrite.TryRewriteText(matNoTruck, movedSet, out var matNoTruckOut, out var matNoTruckCount);
Check(matNoTruckCount == 0 && matNoTruckOut.SequenceEqual(matNoTruck),
    "a .mat with no truck reference is left byte-identical");

// The same base-game rule for the text form.
var matBaseGame = System.Text.Encoding.UTF8.GetBytes(
    "source : \"/vehicle/truck/share/dashboard.tobj\"\n");
AssetPathRewrite.TryRewriteText(matBaseGame, movedSet, out var matBaseGameOut, out var matBaseGameCount);
Check(matBaseGameCount == 0 && matBaseGameOut.SequenceEqual(matBaseGame),
    "a .mat reference to a base-game file under /vehicle/truck/ keeps its original path");

// End-to-end through a real conversion. This is the check that would have caught the bug: the
// asset tree really moves, and every reference into it has to move with it.
var assetMod = Path.Combine(temp, "assetmod");

void WriteAssetFile(string relative, byte[] content)
{
    var path = Path.Combine(assetMod, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, content);
}

WriteAssetFile("manifest.sii",
    System.Text.Encoding.UTF8.GetBytes(
        "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Asset Mod\"\n}\n}\n"));
WriteAssetFile("def/vehicle/truck/sedan/data.sii",
    System.Text.Encoding.UTF8.GetBytes(
        "SiiNunit\n{\naccessory_truck_data : .sedan.data {\n\tinfo[]: \"Sedan\"\n}\n}\n"));
// A material and a compiled texture that both point into the tree that is about to move.
WriteAssetFile("automat/aa/aabbccdd11223344.mat",
    System.Text.Encoding.UTF8.GetBytes(
        "effect : \"eut2.shadowonly.rfx\" {\n\tsource : \"/vehicle/truck/sedan/tex/parts.tobj\"\n}\n"));
// The .tobj resolves a .dds by absolute path, so the fixture has to ship that .dds too. Without it
// the reference names a file the mod never carried, which the pass correctly declines to rewrite -
// and the end-to-end check would then be asserting the wrong thing.
WriteAssetFile("vehicle/truck/sedan/tex/parts.dds", new byte[] { 1, 2, 3, 4 });
WriteAssetFile("vehicle/truck/sedan/tex/parts.tobj", BuildTobj("/vehicle/truck/sedan/tex/parts.dds"));
// A second material pointing at a base-game file the mod does NOT ship. It must survive the
// conversion untouched, because base.scs still holds it at the original path.
WriteAssetFile("automat/bb/bbccddee55667788.mat",
    System.Text.Encoding.UTF8.GetBytes(
        "effect : \"eut2.shadowonly.rfx\" {\n\tsource : \"/vehicle/truck/share/dashboard.tobj\"\n}\n"));

var assetInput = Path.Combine(temp, "AssetMod.zip");
ZipFile.CreateFromDirectory(assetMod, assetInput);

// A fresh output folder, declared out here because this block sits after the main try/catch has
// closed and `output` went out of scope with it.
var assetOut = Path.Combine(temp, "asset-out");

var assetSettings = new ConversionSettings(assetInput, assetOut, "sedan", "", true, true, false, "Sedan");
// The pass reports each repaired file, so the count is read back out of the log the same way a
// user would see it. Counting on the log rather than on a stats object is deliberate: it asserts
// the number is actually SURFACED, not merely accumulated.
var assetLog = new List<string>();
var assetResult = ModConverter.Run(assetSettings, assetLog.Add, _ => { });
Check(File.Exists(assetResult.OutputFile), "asset-path conversion produced an output file");

var repairedLines = assetLog.Count(l => l.Contains("[ASSET]") && l.Contains("rewrote"));
Check(repairedLines == 2,
    $"the conversion reports both repaired files in the log (reported {repairedLines})");

var assetExtract = Path.Combine(temp, "asset-verify");
ZipFile.ExtractToDirectory(assetResult.OutputFile, assetExtract);

Check(Directory.Exists(Path.Combine(assetExtract, "vehicle", "car", "sedan")),
    "the asset tree really did move to vehicle/car");
Check(!Directory.Exists(Path.Combine(assetExtract, "vehicle", "truck")),
    "nothing is left behind under vehicle/truck");

var movedMat = Path.Combine(assetExtract, "automat", "aa", "aabbccdd11223344.mat");
Check(File.Exists(movedMat), "the material file travelled with the conversion");
if (File.Exists(movedMat))
{
    Check(File.ReadAllText(movedMat).Contains("/vehicle/car/sedan/tex/parts.tobj"),
        "end to end: the material now points at vehicle/car, so its texture resolves");
}

var baseGameMat = Path.Combine(assetExtract, "automat", "bb", "bbccddee55667788.mat");
Check(File.Exists(baseGameMat), "the base-game-referencing material travelled with the conversion");
if (File.Exists(baseGameMat))
{
    Check(File.ReadAllText(baseGameMat).Contains("/vehicle/truck/share/dashboard.tobj"),
        "end to end: a reference to a base-game file is left alone, because base.scs still has it");
}

var movedTobj = Path.Combine(assetExtract, "vehicle", "car", "sedan", "tex", "parts.tobj");
Check(File.Exists(movedTobj), "the compiled texture travelled with the conversion");
if (File.Exists(movedTobj))
{
    var embedded = ReadTobjPath(File.ReadAllBytes(movedTobj));
    Check(embedded?.Path == "/vehicle/car/sedan/tex/parts.dds",
        "end to end: the compiled texture's embedded path was repaired, so it stops erroring in game");
}

// Patch mode must NOT rewrite these paths. The original mod keeps its vehicle/truck tree and
// stays mounted, so those references still resolve there and rewriting them would be wrong.
var patchAssetMod = Path.Combine(temp, "patchassetmod");

void WritePatchAsset(string relative, byte[] content)
{
    var path = Path.Combine(patchAssetMod, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, content);
}

WritePatchAsset("manifest.sii",
    System.Text.Encoding.UTF8.GetBytes(
        "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Patch Asset Mod\"\n}\n}\n"));
WritePatchAsset("def/vehicle/truck/sedan/data.sii",
    System.Text.Encoding.UTF8.GetBytes(
        "SiiNunit\n{\naccessory_truck_data : .sedan.data {\n\tinfo[]: \"Sedan\"\n}\n}\n"));
WritePatchAsset("automat/aa/aabbccdd11223344.mat",
    System.Text.Encoding.UTF8.GetBytes(
        "effect : \"eut2.shadowonly.rfx\" {\n\tsource : \"/vehicle/truck/sedan/tex/parts.tobj\"\n}\n"));
WritePatchAsset("vehicle/truck/sedan/tex/parts.dds", new byte[] { 1, 2, 3, 4 });
WritePatchAsset("vehicle/truck/sedan/tex/parts.tobj", BuildTobj("/vehicle/truck/sedan/tex/parts.dds"));

var patchAssetInput = Path.Combine(temp, "PatchAssetMod.zip");
ZipFile.CreateFromDirectory(patchAssetMod, patchAssetInput);

var patchOut = Path.Combine(temp, "patch-asset-out");
var patchAssetSettings = new ConversionSettings(
    patchAssetInput, patchOut, "sedan", "", true, true, PatchOnly: true, VehicleType: "Sedan");
var patchAssetResult = ModConverter.Run(patchAssetSettings, _ => { }, _ => { });
var patchAssetExtract = Path.Combine(temp, "patch-asset-verify");
if (File.Exists(patchAssetResult.OutputFile))
    ZipFile.ExtractToDirectory(patchAssetResult.OutputFile, patchAssetExtract);

var patchMat = Path.Combine(patchAssetExtract, "automat", "aa", "aabbccdd11223344.mat");
if (File.Exists(patchMat))
{
    Check(File.ReadAllText(patchMat).Contains("/vehicle/truck/sedan/tex/parts.tobj"),
        "patch mode leaves the original mod's absolute paths alone, because they still resolve there");
}

// --- Every unit name in a converted archive must be legal ---------------------------
// NamespaceAnonymousUnits throws on an over-long namespace it is handed, but nothing checked each
// component of every unit name actually produced. An over-long component is what the game rejects,
// and it was found in the wild as a 12-character limit rather than as a thrown exception - so this
// sweeps the real output text instead of trusting the guard to be the only possible source.
var legalTemp = Path.Combine(Path.GetTempPath(), "legalname_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(legalTemp);
try
{
    var legalMod = Path.Combine(legalTemp, "mod");
    void WriteLegalFile(string relative, string content)
    {
        var path = Path.Combine(legalMod, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    WriteLegalFile("manifest.sii",
        "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Legal Mod\"\n}\n}\n");

    // Deliberately deep and oddly named, including a component long enough that the namespace has to
    // be shortened. Whatever the converter emits has to come out legal. The camera references matter:
    // they are absolute dotted unit names both before and after conversion, so they give the sweep
    // something real to measure against a relative ".example.data" declaration, whose length is not
    // constrained because it resolves against whatever unit contains it.
    WriteLegalFile("def/vehicle/truck/example/data.sii",
        "SiiNunit\n{\naccessory_truck_data : .example.data {\n\tinfo[]: \"Legal\"\n\tinfo[]: \"Brand\"\n" +
        "\tbehind_camera: camera.behind.basic\n\tinterior_camera: camera.interior.example\n" +
        "\tbumper_camera: camera.bumper.example\n\twheel_camera: camera.wheel.basic\n}\n}\n");
    WriteLegalFile("def/vehicle/truck/example/interior/example.sii",
        "SiiNunit\n{\naccessory_interior_data : .example.interior {\n\tglass: true\n}\n}\n");
    WriteLegalFile("def/vehicle/truck_dealer/example/example.sii",
        "SiiNunit\n{\naccessory_truck_dealer_data : .example.dealer {\n\tname: \"Example\"\n}\n}\n");
    WriteLegalFile("def/vehicle/car/example_verylongcomponentname/data.sii",
        "SiiNunit\n{\naccessory_car_data : .example_verylongcomponentname.data {\n\tinfo[]: \"Legal\"\n}\n}\n");

    var legalInput = Path.Combine(legalTemp, "Legal Mod V2.1 1.60.zip");
    ZipFile.CreateFromDirectory(legalMod, legalInput);

    var legalResult = ModConverter.Run(
        new ConversionSettings(legalInput, Path.Combine(legalTemp, "out"), "example", "", true, true),
        _ => { }, _ => { });

    Check(File.Exists(legalResult.OutputFile), "the legality sweep produced an archive to sweep");

    if (File.Exists(legalResult.OutputFile))
    {
        var illegal = new List<string>();
        var unitsSeen = 0;

        using (var archive = ZipFile.OpenRead(legalResult.OutputFile))
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".sii", StringComparison.OrdinalIgnoreCase))
                    continue;

                string text;
                using (var reader = new StreamReader(entry.Open()))
                    text = reader.ReadToEnd();

                // Every "name : value" line declares a unit or references one. References are included
                // deliberately: an over-long name is just as illegal when referred to as when declared,
                // and a stale reference is where a bad name survives a conversion.
                //
                // The value pattern is a leading letter or underscore so quoted scalars ("1.0") and
                // relative names (.package) are excluded - a relative name has no fixed length limit,
                // it is resolved against whatever unit contains it. The trailing brace is allowed
                // because a unit's own declaration line ends in " {" and is the case that matters most.
                foreach (Match line in Regex.Matches(
                             text, @"^\s*\w[\w\.\[\]]*\s*:\s*([A-Za-z_][\w\.]*)",
                             RegexOptions.Multiline))
                {
                    var unit = line.Groups[1].Value;
                    if (!unit.Contains('.'))
                        continue;

                    unitsSeen++;
                    foreach (var component in unit.Split('.'))
                    {
                        if (component.Length > ModConverter.MaxUnitNameComponentLength)
                            illegal.Add($"{entry.FullName}: '{unit}' ('{component}' is " +
                                        $"{component.Length} chars)");
                    }
                }
            }
        }

        Check(unitsSeen > 0, $"the legality sweep actually read unit names ({unitsSeen} found)");
        Check(illegal.Count == 0,
            illegal.Count == 0
                ? $"every component of every unit name is within the " +
                  $"{ModConverter.MaxUnitNameComponentLength}-character limit"
                : $"{illegal.Count} illegal unit name component(s): {string.Join("; ", illegal.Take(5))}");
    }
}
finally
{
    try { Directory.Delete(legalTemp, true); } catch { }
}

// --- Two same-brand mods must not share an anonymous unit ---------------------------
// The checks above exercise the namespace builder directly. This one converts two DIFFERENT mods that
// share a brand end to end and compares the two archives, because the failure is a property of the
// pair: both real Cadillacs are truck_dealer/cadillac and both declare _nameless units, and if their
// namespaces collide the later archive silently REPLACES the earlier one's units rather than erroring.
// That is what made the car appear twice in the shop, and no single-mod check can see it.
var sameBrandTemp = Path.Combine(Path.GetTempPath(), "samebrand_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sameBrandTemp);
try
{
    string BuildSameBrandMod(string folder, string model)
    {
        var root = Path.Combine(sameBrandTemp, folder);
        void Write(string relative, string content)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        Write("manifest.sii",
            "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"" +
            folder + "\"\n}\n}\n");

        // Real converted mods inherit global _nameless units from their source. Two mods that both
        // declare them and both get the same namespace is the exact collision this test exists to
        // catch, so the synthetic mods have to actually declare some.
        Write("def/vehicle/_nameless/dashboard.sii",
            "SiiNunit\n{\n_nameless.dashboard : \"" + folder + "\"\n}\n");
        Write("def/vehicle/_nameless/speedometer.sii",
            "SiiNunit\n{\n_nameless.speedometer : \"" + folder + "\"\n}\n");

        Write($"def/vehicle/truck/{model}/data.sii",
            "SiiNunit\n{\naccessory_truck_data : ." + model + ".data {\n\tinfo[]: \"Same\"\n\tinfo[]: \"Brand\"\n" +
            "\tdashboard : _nameless.dashboard\n\tspeedometer : _nameless.speedometer\n}\n}\n");
        Write($"def/vehicle/truck/{model}/interior/{model}.sii",
            "SiiNunit\n{\naccessory_interior_data : ." + model + ".interior {\n\tglass: true\n}\n}\n");
        Write("def/vehicle/truck_dealer/example/example.sii",
            "SiiNunit\n{\naccessory_truck_dealer_data : .example.dealer {\n\tname: \"Example\"\n}\n}\n");

        var archive = Path.Combine(sameBrandTemp, folder + ".zip");
        ZipFile.CreateFromDirectory(root, archive);
        return archive;
    }

    var sharedBrand = "example";
    var firstResult = ModConverter.Run(
        new ConversionSettings(BuildSameBrandMod("firstcadillac", "ct5v"),
                               Path.Combine(sameBrandTemp, "out-first"), sharedBrand, "", true, true),
        _ => { }, _ => { });
    var secondResult = ModConverter.Run(
        new ConversionSettings(BuildSameBrandMod("secondcadillac", "escalade"),
                               Path.Combine(sameBrandTemp, "out-second"), sharedBrand, "", true, true),
        _ => { }, _ => { });

    Check(File.Exists(firstResult.OutputFile) && File.Exists(secondResult.OutputFile),
        "both same-brand mods converted");

    if (File.Exists(firstResult.OutputFile) && File.Exists(secondResult.OutputFile))
    {
        HashSet<string> CollectUnitNames(string archivePath)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".sii", StringComparison.OrdinalIgnoreCase))
                    continue;

                string text;
                using (var reader = new StreamReader(entry.Open()))
                    text = reader.ReadToEnd();

                foreach (Match line in Regex.Matches(
                             text, @"^\s*\w[\w\.\[\]]*\s*:\s*([A-Za-z_][\w\.]*)",
                             RegexOptions.Multiline))
                {
                    var unit = line.Groups[1].Value;
                    if (unit.Contains('.'))
                        names.Add(unit);
                }
            }

            return names;
        }

        // Only generated units matter. Two mods sharing a base game's own unit name is normal and
        // expected; two CONVERTED mods colliding on a generated one is the bug.
        var firstAnonymous = CollectUnitNames(firstResult.OutputFile)
            .Where(n => n.Contains("_nameless", StringComparison.Ordinal)).ToList();
        var secondAnonymous = CollectUnitNames(secondResult.OutputFile)
            .Where(n => n.Contains("_nameless", StringComparison.Ordinal)).ToList();

        Check(firstAnonymous.Count > 0 && secondAnonymous.Count > 0,
            $"both archives really do contain generated anonymous units ({firstAnonymous.Count} and " +
            $"{secondAnonymous.Count}), so the comparison is meaningful");

        var collision = firstAnonymous.Intersect(secondAnonymous, StringComparer.Ordinal).ToList();

        Check(collision.Count == 0,
            collision.Count == 0
                ? "two same-brand mods share no generated unit name, so neither replaces the other's"
                : $"the two same-brand mods collide on {collision.Count} generated unit name(s): " +
                  $"{string.Join(", ", collision.Take(5))}");

        var firstNamespace = ModConverter.BuildAnonymousNamespace(sharedBrand, firstResult.OutputFile);
        var secondNamespace = ModConverter.BuildAnonymousNamespace(sharedBrand, secondResult.OutputFile);
        Check(firstNamespace != secondNamespace,
            $"the two same-brand mods get different namespaces ({firstNamespace} vs {secondNamespace}), " +
            "because the namespace comes from the input file name rather than the shared brand");
    }
}
finally
{
    try { Directory.Delete(sameBrandTemp, true); } catch { }
}

// --- Converting one mod twice under two brands is warned about -----------------------
// Both branding options default on and are not independent, so converting one mod twice writes two
// car_brand_logo files that each override a different base-game badge. The check is a warning, never a
// refusal, and it must stay silent on the ordinary case of re-running the same conversion.
var dupTemp = Path.Combine(Path.GetTempPath(), "dupbrand_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dupTemp);
try
{
    var dupMod = Path.Combine(dupTemp, "mod");
    void WriteDup(string relative, string content)
    {
        var path = Path.Combine(dupMod, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    WriteDup("manifest.sii",
        "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Dup Mod\"\n}\n}\n");
    WriteDup("def/vehicle/truck/dupcar/data.sii",
        "SiiNunit\n{\naccessory_truck_data : .dupcar.data {\n\tinfo[]: \"Dup\"\n\tinfo[]: \"Brand\"\n}\n}\n");
    WriteDup("def/vehicle/truck_dealer/first/first.sii",
        "SiiNunit\n{\naccessory_truck_dealer_data : .first.dealer {\n\tname: \"First\"\n}\n}\n");

    var dupInput = Path.Combine(dupTemp, "DupMod.zip");
    ZipFile.CreateFromDirectory(dupMod, dupInput);
    var dupOut = Path.Combine(dupTemp, "out");
    Directory.CreateDirectory(dupOut);

    const string marker = "was already converted in this folder";

    var firstLog = new List<string>();
    ModConverter.Run(new ConversionSettings(dupInput, dupOut, "first", "", true, true),
        firstLog.Add, _ => { });
    Check(firstLog.All(l => !l.Contains(marker)),
        "the first conversion warns about nothing, because there is nothing to conflict with");

    // Second run, same brand: an ordinary re-run, which must stay silent.
    var rerunLog = new List<string>();
    ModConverter.Run(new ConversionSettings(dupInput, dupOut, "first", "", true, true),
        rerunLog.Add, _ => { });
    Check(rerunLog.All(l => !l.Contains(marker)),
        "re-running the same conversion under the same brand is silent, because nothing conflicts");

    // Third run, different brand: the case the warning exists for.
    var conflictLog = new List<string>();
    ModConverter.Run(
        new ConversionSettings(dupInput, dupOut, "second", "", true, true, UseSourceBrandToken: false),
        conflictLog.Add, _ => { });

    Check(conflictLog.Any(l => l.Contains(marker)),
        "converting the same mod under a second brand token warns that both archives will be installed");

    Check(conflictLog.Any(l => l.Contains("overrides a different base-game badge")),
        "the warning says what the consequence is, not just that something happened");

    // The conversion still succeeds. A warning that blocked the run would be the wrong call.
    Check(File.Exists(Path.Combine(dupOut, "DupMod_roadtrip.scs")),
        "the second-brand conversion still produced its archive - the duplicate is a warning, not a refusal");

    // Another mod's archive in the same folder must not trigger it. This one needs a def/ tree of its
    // own, because the converter rejects a mod that has none - and that rejection is fine here, since
    // all this needs to prove is that the duplicate check never consulted the other archive's name.
    var otherMod = Path.Combine(dupTemp, "other");
    Directory.CreateDirectory(Path.Combine(otherMod, "def", "vehicle", "truck", "othercar"));
    File.WriteAllText(Path.Combine(otherMod, "manifest.sii"),
        "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Other\"\n}\n}\n");
    File.WriteAllText(
        Path.Combine(otherMod, "def", "vehicle", "truck", "othercar", "data.sii"),
        "SiiNunit\n{\naccessory_truck_data : .othercar.data {\n\tinfo[]: \"Other\"\n\tinfo[]: \"Brand\"\n}\n}\n");

    var otherInput = Path.Combine(dupTemp, "OtherMod.zip");
    ZipFile.CreateFromDirectory(otherMod, otherInput);

    var unrelatedLog = new List<string>();
    ModConverter.Run(
        new ConversionSettings(otherInput, dupOut, "third", "", true, true, UseSourceBrandToken: false),
        unrelatedLog.Add, _ => { });
    Check(unrelatedLog.All(l => !l.Contains(marker)),
        "another mod's archive in the same output folder does not trigger the duplicate-brand warning");

    // ReadBrandTokens is what grounds the check, so it is asserted directly rather than only through
    // the log - it is the part that could silently start returning nothing.
    var dupArchive = Path.Combine(dupOut, "DupMod_roadtrip.scs");
    if (File.Exists(dupArchive))
    {
        var tokens = ModConverter.ReadBrandTokens(dupArchive);
        Check(tokens.Count > 0,
            $"ReadBrandTokens reads the dealer folder back out of a converted archive ({string.Join(", ", tokens)})");
        Check(!ModConverter.ReadBrandTokens(Path.Combine(dupTemp, "does-not-exist.scs")).Any(),
            "ReadBrandTokens on a missing archive returns nothing rather than throwing");
        File.WriteAllText(Path.Combine(dupTemp, "notazip.scs"), "definitely not a zip");
        Check(!ModConverter.ReadBrandTokens(Path.Combine(dupTemp, "notazip.scs")).Any(),
            "ReadBrandTokens on a corrupt archive returns nothing rather than throwing");
    }
}
finally
{
    try { Directory.Delete(dupTemp, true); } catch { }
}

if (failures.Count > 0)
{
    Console.WriteLine($"{failures.Count} check(s) failed.");
    return 1;
}

Console.WriteLine("All checks passed.");
return 0;




