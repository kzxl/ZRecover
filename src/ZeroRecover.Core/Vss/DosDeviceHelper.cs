using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace ZeroRecover.Core.Vss;

/// <summary>
/// Internal Win32 helper managing MS-DOS device name mapping via DefineDosDevice.
/// Allows mounting VSS shadow copy volumes (e.g. \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1)
/// into standard DOS drive letters (e.g. Z:) without external dependencies.
/// </summary>
internal static class DosDeviceHelper
{
    private const uint DDD_RAW_TARGET_PATH = 0x00000001;
    private const uint DDD_REMOVE_DEFINITION = 0x00000002;
    private const uint DDD_EXACT_MATCH_ON_REMOVE = 0x00000004;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool DefineDosDevice(uint dwFlags, string lpDeviceName, string? lpTargetPath);

    public static char? FindAvailableDriveLetter()
    {
        var taken = new HashSet<char>();
        foreach (var d in Directory.GetLogicalDrives())
        {
            if (!string.IsNullOrEmpty(d))
            {
                taken.Add(char.ToUpperInvariant(d[0]));
            }
        }

        for (char c = 'Z'; c >= 'D'; c--)
        {
            if (!taken.Contains(c))
                return c;
        }

        return null;
    }

    public static bool TryMountDevice(string targetDevicePath, out char mountedDriveLetter, out string errorMessage)
    {
        mountedDriveLetter = '\0';
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(targetDevicePath))
        {
            errorMessage = "Target device path cannot be empty.";
            return false;
        }

        var driveOpt = FindAvailableDriveLetter();
        if (!driveOpt.HasValue)
        {
            errorMessage = "No unused drive letters available to mount device.";
            return false;
        }

        char driveLetter = driveOpt.Value;
        string deviceName = $"{driveLetter}:";

        string targetDevice = targetDevicePath.TrimEnd('\\');
        if (targetDevice.StartsWith(@"\\?\GLOBALROOT", StringComparison.OrdinalIgnoreCase))
        {
            targetDevice = targetDevice.Substring(@"\\?\GLOBALROOT".Length);
        }

        bool result = DefineDosDevice(DDD_RAW_TARGET_PATH, deviceName, targetDevice);
        if (!result)
        {
            int errCode = Marshal.GetLastWin32Error();
            errorMessage = $"DefineDosDevice failed with Win32 error code {errCode}.";
            return false;
        }

        mountedDriveLetter = driveLetter;
        return true;
    }

    public static bool TryUnmountDevice(char driveLetter, string? targetDevicePath, out string errorMessage)
    {
        errorMessage = string.Empty;
        string deviceName = $"{char.ToUpperInvariant(driveLetter)}:";
        string? targetDevice = null;

        if (!string.IsNullOrWhiteSpace(targetDevicePath))
        {
            targetDevice = targetDevicePath.TrimEnd('\\');
            if (targetDevice.StartsWith(@"\\?\GLOBALROOT", StringComparison.OrdinalIgnoreCase))
            {
                targetDevice = targetDevice.Substring(@"\\?\GLOBALROOT".Length);
            }
        }

        bool result = false;
        if (targetDevice != null)
        {
            result = DefineDosDevice(DDD_REMOVE_DEFINITION | DDD_EXACT_MATCH_ON_REMOVE, deviceName, targetDevice);
        }

        if (!result)
        {
            result = DefineDosDevice(DDD_REMOVE_DEFINITION, deviceName, null);
        }

        if (!result)
        {
            int errCode = Marshal.GetLastWin32Error();
            errorMessage = $"Failed to remove DOS device for '{deviceName}' (Win32 error: {errCode}).";
            return false;
        }

        return true;
    }
}
