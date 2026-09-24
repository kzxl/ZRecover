namespace ZRecover.Core.FileSystems.Ntfs;

/// <summary>
/// NTFS Attribute Type identifiers.
/// </summary>
public static class NtfsAttributeType
{
    public const uint StandardInformation = 0x10;
    public const uint AttributeList = 0x20;
    public const uint FileName = 0x30;
    public const uint ObjectId = 0x40;
    public const uint SecurityDescriptor = 0x50;
    public const uint VolumeName = 0x60;
    public const uint VolumeInformation = 0x70;
    public const uint Data = 0x80;
    public const uint IndexRoot = 0x90;
    public const uint IndexAllocation = 0xA0;
    public const uint Bitmap = 0xB0;
    public const uint ReparsePoint = 0xC0;
    public const uint EndMarker = 0xFFFFFFFF;
}

/// <summary>
/// Decoded $FILE_NAME attribute information.
/// </summary>
public sealed class NtfsFileNameInfo
{
    public long ParentDirectoryRecordNumber { get; set; }

    public string FileName { get; set; } = string.Empty;

    public DateTime CreatedTime { get; set; }

    public DateTime ModifiedTime { get; set; }

    public long RealSize { get; set; }

    public byte Namespace { get; set; }
}

/// <summary>
/// Decoded $DATA attribute information.
/// </summary>
public sealed class NtfsDataInfo
{
    public string AttributeName { get; set; } = string.Empty;

    public bool IsResident { get; set; }

    public long RealSize { get; set; }

    public byte[]? ResidentBytes { get; set; }

    public List<NtfsDataRun> DataRuns { get; set; } = [];
}
