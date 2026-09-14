using System.Management;
using ZeroRecover.Core.Models;

namespace ZeroRecover.Core.Vss;

/// <summary>
/// Descriptor for a Windows Volume Shadow Copy (VSS Snapshot) point.
/// </summary>
public sealed class VssSnapshotInfo
{
    public string Id { get; set; } = string.Empty;

    public string DeviceObject { get; set; } = string.Empty;

    public string OriginalVolume { get; set; } = string.Empty;

    public DateTime CreationTime { get; set; }

    public string MountedDriveLetter { get; set; } = string.Empty;

    public bool IsMounted => !string.IsNullOrEmpty(MountedDriveLetter);
}

/// <summary>
/// Sovereign explorer discovering and mounting Windows VSS Volume Shadow Copies
/// via ZeroSystem.DosDeviceManager.
/// </summary>
public static class VssSnapshotExplorer
{
    public static List<VssSnapshotInfo> EnumerateSnapshots(string? volumeNameFilter = null)
    {
        var list = new List<VssSnapshotInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ID, DeviceObject, VolumeName, InstallDate FROM Win32_ShadowCopy");
            foreach (ManagementObject obj in searcher.Get())
            {
                string id = obj["ID"]?.ToString() ?? string.Empty;
                string deviceObject = obj["DeviceObject"]?.ToString() ?? string.Empty;
                string volName = obj["VolumeName"]?.ToString() ?? string.Empty;
                string dateStr = obj["InstallDate"]?.ToString() ?? string.Empty;

                DateTime creationTime = DateTime.MinValue;
                if (!string.IsNullOrEmpty(dateStr))
                {
                    try { creationTime = ManagementDateTimeConverter.ToDateTime(dateStr); } catch { }
                }

                if (!string.IsNullOrEmpty(volumeNameFilter) &&
                    !volName.Contains(volumeNameFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                list.Add(new VssSnapshotInfo
                {
                    Id = id,
                    DeviceObject = deviceObject,
                    OriginalVolume = volName,
                    CreationTime = creationTime
                });
            }
        }
        catch
        {
            // Non-elevated or VSS service unavailable
        }

        return list;
    }

    /// <summary>
    /// Mounts a VSS Shadow Copy device object to an available drive letter using ZeroSystem.DosDeviceManager.
    /// </summary>
    public static string MountSnapshot(VssSnapshotInfo snapshot)
    {
        if (snapshot.IsMounted)
            return snapshot.MountedDriveLetter;

        string targetDevice = snapshot.DeviceObject.TrimEnd('\\');
        if (ZeroSystem.DosDeviceManager.TryMountDevice(targetDevice, out char driveLetter, out string err))
        {
            snapshot.MountedDriveLetter = $"{driveLetter}:";
            return snapshot.MountedDriveLetter;
        }

        throw new InvalidOperationException($"Failed to mount shadow copy '{targetDevice}': {err}");
    }

    /// <summary>
    /// Unmounts a previously mounted VSS Snapshot using ZeroSystem.DosDeviceManager.
    /// </summary>
    public static void UnmountSnapshot(VssSnapshotInfo snapshot)
    {
        if (snapshot.IsMounted && snapshot.MountedDriveLetter.Length > 0)
        {
            char driveChar = snapshot.MountedDriveLetter[0];
            ZeroSystem.DosDeviceManager.TryUnmountDevice(driveChar, snapshot.DeviceObject, out _);
            snapshot.MountedDriveLetter = string.Empty;
        }
    }
}
