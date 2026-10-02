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
        "SiiNunit\n{\naccessory_truck_data : .example.data {\n\tinfo[]: \"Verify\"\n\tinfo[]: \"Brand\"\n}\n}\n");
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
    }

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
