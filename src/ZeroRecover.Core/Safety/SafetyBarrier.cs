using System.IO;

namespace ZeroRecover.Core.Safety;

/// <summary>
/// Enforces the sovereign Zero-Write safety policy, preventing recovery operations
/// from writing data to the very drive currently undergoing recovery.
/// </summary>
public static class SafetyBarrier
{
    /// <summary>
    /// Validates that the destination directory is safe and does not conflict with the source drive.
    /// </summary>
    public static void ValidateDestination(string sourceDrive, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDrive))
            throw new ArgumentException("Source drive must be specified.", nameof(sourceDrive));

        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("Destination directory must be specified.", nameof(destinationDirectory));

        string normalizedSource = sourceDrive.Trim().TrimEnd('\\', '/').ToUpperInvariant();
        if (normalizedSource.Length == 1 && char.IsLetter(normalizedSource[0]))
        {
            normalizedSource += ":";
        }

        string fullDestPath = Path.GetFullPath(destinationDirectory);
        string destRoot = Path.GetPathRoot(fullDestPath)?.TrimEnd('\\', '/').ToUpperInvariant() ?? string.Empty;

        if (string.Equals(normalizedSource, destRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Zero-Write Safety Barrier Alert: Destination path '{fullDestPath}' resides on the source drive '{normalizedSource}'. " +
                "Restoring recovered files onto the source drive will immediately overwrite remaining deleted clusters and permanently destroy recoverable data. " +
                "You must select a different drive (e.g., secondary hard drive, external SSD, or USB thumb drive).");
        }
    }

    /// <summary>
    /// Checks whether destination has sufficient free space for the selected payload.
    /// </summary>
    public static bool HasSufficientSpace(string destinationDirectory, long totalBytesRequired, out long availableBytes)
    {
        string fullPath = Path.GetFullPath(destinationDirectory);
        string driveRoot = Path.GetPathRoot(fullPath) ?? string.Empty;
        var driveInfo = new DriveInfo(driveRoot);

        availableBytes = driveInfo.AvailableFreeSpace;
        // Require 5% buffer margin above requested file size
        return availableBytes >= (long)(totalBytesRequired * 1.05);
    }
}
