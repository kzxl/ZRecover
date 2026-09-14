using System.IO;
using ZeroRecover.Core.Models;

namespace ZeroRecover.Core.Disk;

/// <summary>
/// Discovers and queries available storage drives and volumes.
/// </summary>
public static class VolumeEnumerator
{
    public static List<DriveVolumeInfo> EnumerateDrives()
    {
        var result = new List<DriveVolumeInfo>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            if (drive.DriveType != DriveType.Fixed && 
                drive.DriveType != DriveType.Removable && 
                drive.DriveType != DriveType.Network) continue;

            var info = new DriveVolumeInfo
            {
                DriveLetter = drive.Name.TrimEnd('\\'),
                VolumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                FileSystem = drive.DriveFormat,
                TotalBytes = drive.TotalSize,
                FreeBytes = drive.AvailableFreeSpace,
                IsReady = drive.IsReady,
                BytesPerSector = 512,
                SectorsPerCluster = 8
            };

            // Heuristic for standard cluster size: NTFS defaults to 4096 bytes (8 sectors of 512)
            if (info.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
            {
                info.BytesPerSector = 512;
                info.SectorsPerCluster = 8;
            }
            else if (info.FileSystem.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
            {
                info.BytesPerSector = 512;
                info.SectorsPerCluster = 8;
            }

            result.Add(info);
        }

        return result;
    }
}
