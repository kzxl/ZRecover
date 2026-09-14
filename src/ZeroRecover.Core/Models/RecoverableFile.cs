namespace ZeroRecover.Core.Models;

/// <summary>
/// Represents a deleted or carved file candidate ready for restoration.
/// </summary>
public sealed class RecoverableFile
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string FileName { get; set; } = string.Empty;

    public string OriginalPath { get; set; } = string.Empty;

    public long Size { get; set; }

    public string Extension { get; set; } = string.Empty;

    public FileCategory Category { get; set; } = FileCategory.Other;

    public DateTime? CreatedTime { get; set; }

    public DateTime? ModifiedTime { get; set; }

    public DateTime? DeletedTime { get; set; }

    public RecoveryHealth Health { get; set; } = RecoveryHealth.Excellent;

    /// <summary>
    /// Engine method used to discover this candidate:
    /// "NTFS_MFT", "FAT_ENTRY", "DEEP_CARVE", "VSS_SNAPSHOT", "RECYCLE_BIN"
    /// </summary>
    public string RecoveryMethod { get; set; } = "UNKNOWN";

    public string SourceDrive { get; set; } = string.Empty;

    public long SourceOffset { get; set; }

    public List<DataRunExtent> DataExtents { get; set; } = [];

    public byte[]? PreviewBytes { get; set; }

    public bool IsSelected { get; set; } = true;

    /// <summary>
    /// True if file content is resident directly inside MFT record.
    /// </summary>
    public bool IsResident { get; set; }

    public byte[]? ResidentData { get; set; }

    /// <summary>
    /// Gets formatted human-readable file size string.
    /// </summary>
    public string FormattedSize => FormatBytes(Size);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}

/// <summary>
/// A contiguous segment of clusters or bytes on disk.
/// </summary>
public readonly record struct DataRunExtent(long StartLcn, long ClusterCount);
