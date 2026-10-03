using System.IO.Compression;
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
    WriteRePatchFile("def/vehicle/car/example/data.sii",
        "SiiNunit\n{\naccessory_car_data : .example.OLD {\n\tinfo[]: \"STALE_PATCH_MARKER\"\n}\n}\n");
    WriteRePatchFile("def/vehicle/car/example/data.sii.truck_source",
        "SiiNunit\n{\naccessory_car_data : .example.leftover {\n\tinfo[]: \"LEGACY_LEFTOVER_MARKER\"\n}\n}\n");

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

Console.WriteLine("All checks passed.");
return 0;
