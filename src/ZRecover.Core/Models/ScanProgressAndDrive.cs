namespace ZRecover.Core.Models;

/// <summary>
/// Execution parameters configuring a drive scan session.
/// </summary>
public sealed class ScanOptions
{
    public string TargetDrive { get; set; } = "C:";

    public ScanMode Mode { get; set; } = ScanMode.QuickUndelete;

    public FileCategory FilterCategory { get; set; } = FileCategory.All;

    public string SearchQuery { get; set; } = string.Empty;

    public long? MinFileSize { get; set; }

    public long? MaxFileSize { get; set; }

    public bool FastExitOnMatch { get; set; }

    /// <summary>
    /// Optional specific directory path to narrow scan scope (e.g. "C:\Users\User\Documents").
    /// If null or empty, the entire volume is scanned.
    /// </summary>
    public string? TargetFolderPath { get; set; }

    /// <summary>
    /// True if scan is targeted to a specific folder rather than entire drive.
    /// </summary>
    public bool IsFolderScope => !string.IsNullOrWhiteSpace(TargetFolderPath);
}

/// <summary>
/// Telemetry update report emitted during scanning.
/// </summary>
public sealed class ScanProgressReport
{
    public long ScannedBytes { get; set; }

    public long TotalBytes { get; set; }

    public int FilesFound { get; set; }

    public string CurrentOperation { get; set; } = string.Empty;

    public double MegaBytesPerSecond { get; set; }

    public double Percent => TotalBytes > 0 ? Math.Min(100.0, (double)ScannedBytes / TotalBytes * 100.0) : 0.0;

    public TimeSpan EstimatedRemainingTime { get; set; } = TimeSpan.Zero;
}

/// <summary>
/// Storage device or volume topology descriptor.
/// </summary>
public sealed class DriveVolumeInfo
{
    public string DriveLetter { get; set; } = string.Empty;

    public string VolumeLabel { get; set; } = string.Empty;

    public string FileSystem { get; set; } = string.Empty;

    public long TotalBytes { get; set; }

    public long FreeBytes { get; set; }

    public int BytesPerSector { get; set; } = 512;

    public int SectorsPerCluster { get; set; } = 8;

    public int ClusterSize => BytesPerSector * SectorsPerCluster;

    public bool IsReady { get; set; } = true;

    public string DevicePath => $@"\\.\{DriveLetter.TrimEnd('\\')}";

    public string FormattedTotal => FormatBytes(TotalBytes);

    public string FormattedFree => FormatBytes(FreeBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
