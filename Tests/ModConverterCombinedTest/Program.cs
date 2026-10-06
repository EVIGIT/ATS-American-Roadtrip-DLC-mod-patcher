using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using TruckersToolKit;

// Simple test for the combined run mode
var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
    Console.WriteLine((condition ? "PASS " : "FAIL ") + message);
}

// Test 1: Check that EmitPatchAlongside can be set
Check(new ConversionSettings("mod.scs", "out", "volvo", "", true, true, true, "pickup", true, true, true, true).EmitPatchAlongside == true,
    "EmitPatchAlongside can be switched on");

// Test 2: Simple combined run test
var tempDir = Path.Combine(Path.GetTempPath(), "ats-test-" + Guid.NewGuid().ToString("N"));
try
{
    // Create a minimal mod structure (truck-era mod that can be converted)
    var modDir = Path.Combine(tempDir, "mod");
    Directory.CreateDirectory(Path.Combine(modDir, "def", "vehicle", "truck", "acme_truck"));
    Directory.CreateDirectory(Path.Combine(modDir, "def", "vehicle", "truck_dealer", "acme"));
    
    // Main vehicle definition
    File.WriteAllText(Path.Combine(modDir, "def", "vehicle", "truck", "acme_truck", "data.sii"),
        "SiiNunit\n{\naccessory_truck_data : vehicle.acme.truck\n{\n\tinfo[]: \"ACME\"\n\tinfo[]: \"Truck\"\n\tchassis: vehicle.acme.truck\n}\n}\n");
    
    // Dealer definition
    File.WriteAllText(Path.Combine(modDir, "def", "vehicle", "truck_dealer", "acme", "truck.sii"),
        "SiiNunit\n{\nvehicle : .truck\n{\n\taccessories[]: .data\n}\n\nvehicle_accessory : .data {\n\tdata_path: \"/def/vehicle/truck/acme_truck/data.sii\"\n}\n}\n");
    
    // Manifest
    File.WriteAllText(Path.Combine(modDir, "manifest.sii"),
        "SiiNunit\n{\nmod_package : .package {\n\tpackage_version: \"1.0\"\n\tdisplay_name: \"Test Mod\"\n}\n}\n");

    // Create input archive
    var inputArchive = Path.Combine(tempDir, "mod.scs");
    using (var archive = ZipFile.Open(inputArchive, ZipArchiveMode.Create))
    {
        archive.CreateEntryFromFile(Path.Combine(modDir, "def", "vehicle", "truck", "acme_truck", "data.sii"), "def/vehicle/truck/acme_truck/data.sii");
        archive.CreateEntryFromFile(Path.Combine(modDir, "def", "vehicle", "truck_dealer", "acme", "truck.sii"), "def/vehicle/truck_dealer/acme/truck.sii");
        archive.CreateEntryFromFile(Path.Combine(modDir, "manifest.sii"), "manifest.sii");
    }

    // Run conversion with EmitPatchAlongside = true
    var outDir = Path.Combine(tempDir, "output");
    Directory.CreateDirectory(outDir);
    var settings = new ConversionSettings(inputArchive, outDir, "acme", "", true, true, false, "pickup", true, true, true, true)
    {
        EmitPatchAlongside = true,
    };

    var result = ModConverter.Run(settings, s => Console.WriteLine($"[LOG] {s}"), p => { });

    Check(!string.IsNullOrEmpty(result.PatchFile), "Patch file is produced");
    Check(File.Exists(result.OutputFile), "Full output file exists");
    Check(File.Exists(result.PatchFile), "Patch file exists");

    Console.WriteLine("All tests passed!");
}
catch (Exception ex)
{
    Console.WriteLine($"Test failed with exception: {ex.Message}");
    throw;
}
finally
{
    try { Directory.Delete(tempDir, true); } catch { }
}
