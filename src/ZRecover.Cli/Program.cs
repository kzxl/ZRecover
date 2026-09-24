using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZRecover.Core;
using ZRecover.Core.Disk;
using ZRecover.Core.Models;
using ZRecover.Core.Safety;
using ZRecover.Core.Vss;

namespace ZRecover.Cli;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        PrintBanner();

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        try
        {
            switch (command)
            {
                case "drives":
                    return ListDrives();

                case "recycle":
                    return await ScanRecycleBin(args);

                case "vss":
                    return ListVss(args);

                case "carve":
                    return await RunCarve(args);

                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ERROR] Unknown command '{command}'. Run 'zerorecover --help' for options.");
                    Console.ResetColor();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FATAL ERROR] {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine("  ⚡ ZRecover — Sovereign Deep Data Recovery Suite (.NET 8 BCL)");
        Console.WriteLine("  🌌 Part of the Zero Universe Ecosystem | Zero-Dependency & Zero-Write Policy");
        Console.WriteLine("================================================================================");
        Console.ResetColor();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("\nUsage: zerorecover <command> [arguments]\n");
        Console.WriteLine("Commands:");
        Console.WriteLine("  drives                     List all connected physical and logical storage volumes.");
        Console.WriteLine("  recycle <drive>            Forensically parse $Recycle.Bin on the specified drive.");
        Console.WriteLine("  vss <drive>                List available Volume Shadow Copies (VSS restore points).");
        Console.WriteLine("  carve <drive> [--out <dir>] Execute raw signature sector carving for deleted files.");
        Console.WriteLine("\nOptions:");
        Console.WriteLine("  --out <directory>          Destination directory to restore files (MUST be on another drive).");
        Console.WriteLine("  --category <cat>           Filter by category: All, Documents, Pictures, Videos, Audio, Archives, Databases.");
        Console.WriteLine("  -h, --help                 Display this help screen.");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  zerorecover drives");
        Console.WriteLine("  zerorecover recycle C:");
        Console.WriteLine("  zerorecover vss C:");
        Console.WriteLine("  zerorecover carve D: --out E:\\Restored --category Pictures\n");
    }

    private static int ListDrives()
    {
        Console.WriteLine("\n[DISCOVERED VOLUMES]");
        var drives = VolumeEnumerator.EnumerateDrives();
        Console.WriteLine("{0,-10} {1,-20} {2,-10} {3,-15} {4,-15}", "Letter", "Label", "Format", "Total", "Free");
        Console.WriteLine(new string('-', 75));

        foreach (var d in drives)
        {
            Console.WriteLine("{0,-10} {1,-20} {2,-10} {3,-15} {4,-15}",
                d.DriveLetter, d.VolumeLabel, d.FileSystem, d.FormattedTotal, d.FormattedFree);
        }
        return 0;
    }

    private static async Task<int> ScanRecycleBin(string[] args)
    {
        string drive = args.Length > 1 ? args[1] : "C:";
        Console.WriteLine($"\n[RECYCLE BIN EXTRACTION] Scanning drive {drive}...");

        var service = new RecoveryService();
        var options = new ScanOptions { Mode = ScanMode.RecycleBin, TargetDrive = drive };
        var files = await service.ExecuteScanAsync(options);

        Console.WriteLine($"\nFound {files.Count} recoverable files in $Recycle.Bin on {drive}:");
        Console.WriteLine("{0,-35} {1,-12} {2,-20} {3}", "Original File Name", "Size", "Deleted At", "Health");
        Console.WriteLine(new string('-', 85));

        foreach (var f in files.Take(25))
        {
            Console.WriteLine("{0,-35} {1,-12} {2,-20} {3}",
                Truncate(f.FileName, 33), f.FormattedSize, f.DeletedTime?.ToString("yyyy-MM-dd HH:mm") ?? "N/A", f.Health);
        }

        if (files.Count > 25)
        {
            Console.WriteLine($"... and {files.Count - 25} more items.");
        }

        return 0;
    }

    private static int ListVss(string[] args)
    {
        string drive = args.Length > 1 ? args[1] : "C:";
        Console.WriteLine($"\n[VSS SHADOW COPIES] Querying snapshot points for volume {drive}...");

        var snapshots = VssSnapshotExplorer.EnumerateSnapshots(drive);
        if (snapshots.Count == 0)
        {
            Console.WriteLine("No VSS snapshots found (or requires elevation to query).");
            return 0;
        }

        Console.WriteLine("{0,-40} {1,-25} {2}", "Device Object", "Creation Time", "Original Volume");
        Console.WriteLine(new string('-', 85));

        foreach (var s in snapshots)
        {
            Console.WriteLine("{0,-40} {1,-25} {2}", s.DeviceObject, s.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"), s.OriginalVolume);
        }
        return 0;
    }

    private static async Task<int> RunCarve(string[] args)
    {
        string drive = args.Length > 1 ? args[1] : "C:";
        string? outDir = null;
        FileCategory filterCategory = FileCategory.All;

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i].Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                outDir = args[++i];
            }
            else if (args[i].Equals("--category", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (Enum.TryParse<FileCategory>(args[++i], true, out var cat))
                {
                    filterCategory = cat;
                }
            }
        }

        if (!string.IsNullOrEmpty(outDir))
        {
            // Safety Barrier Verification early
            SafetyBarrier.ValidateDestination(drive, outDir);
        }

        Console.WriteLine($"\n[DEEP RAW CARVING] Initializing sector scanner on {drive} (Category: {filterCategory})...");

        string devicePath = $@"\\.\{drive.TrimEnd('\\')}";
        try
        {
            using var reader = new RawDiskReader(devicePath);
            var service = new RecoveryService();
            var options = new ScanOptions
            {
                Mode = ScanMode.DeepCarve,
                TargetDrive = drive,
                FilterCategory = filterCategory
            };

            var progress = new Progress<ScanProgressReport>(p =>
            {
                Console.Write($"\r[SCANNING] {p.Percent:F1}% | {p.ScannedBytes / (1024 * 1024)}MB | Found: {p.FilesFound} files | {p.MegaBytesPerSecond:F1} MB/s");
            });

            var results = await service.ExecuteScanAsync(options, reader, progress);
            Console.WriteLine($"\n\n[SUCCESS] Carving complete! Found {results.Count} candidates.");

            if (!string.IsNullOrEmpty(outDir) && results.Count > 0)
            {
                Console.WriteLine($"[RESTORE] Saving {results.Count} carved files to '{outDir}'...");
                int restoredCount = 0;
                foreach (var file in results)
                {
                    await service.RestoreFileAsync(file, outDir, reader);
                    restoredCount++;
                    Console.Write($"\rRestored: {restoredCount}/{results.Count} files.");
                }
                Console.WriteLine("\n[RESTORE FINISHED] All carved items safely recovered.");
            }
            else
            {
                Console.WriteLine("Tip: Provide '--out <destination_directory>' on another drive to auto-restore carved files.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] Could not open raw volume {devicePath}: {ex.Message}");
            Console.WriteLine("Note: Physical drive carving requires running the terminal as Administrator.");
            return 1;
        }
    }

    private static string Truncate(string val, int maxLen)
    {
        if (string.IsNullOrEmpty(val)) return string.Empty;
        return val.Length <= maxLen ? val : val.Substring(0, maxLen - 3) + "...";
    }
}
