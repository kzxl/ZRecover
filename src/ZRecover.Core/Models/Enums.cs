namespace ZRecover.Core.Models;

/// <summary>
/// Assessment of file integrity and recoverability.
/// </summary>
public enum RecoveryHealth
{
    /// <summary>
    /// File data clusters are completely unallocated and intact. High recovery certainty (95-100%).
    /// </summary>
    Excellent,

    /// <summary>
    /// File metadata is valid and most clusters are intact, with slight risk of partial slack overlap.
    /// </summary>
    Good,

    /// <summary>
    /// File suffers from fragmentation or some clusters have been claimed by subsequent allocations.
    /// </summary>
    Poor,

    /// <summary>
    /// Clusters have been fully overwritten by newer files. Unrecoverable via standard undelete.
    /// </summary>
    Overwritten
}

/// <summary>
/// Broad category of recoverable files for high-level filtering.
/// </summary>
public enum FileCategory
{
    All,
    Documents,
    Pictures,
    Videos,
    Audio,
    Archives,
    Databases,
    Executable,
    Other
}

/// <summary>
/// Operational mode for file recovery.
/// </summary>
public enum ScanMode
{
    /// <summary>
    /// Instant metadata scan using NTFS $MFT or FAT directory entries.
    /// </summary>
    QuickUndelete,

    /// <summary>
    /// Low-level physical sector carving scanning for known magic byte signatures.
    /// </summary>
    DeepCarve,

    /// <summary>
    /// Point-in-time recovery utilizing Windows VSS Shadow Copies.
    /// </summary>
    VssSnapshot,

    /// <summary>
    /// Forensic extraction of $Recycle.Bin index files and payload files.
    /// </summary>
    RecycleBin
}
