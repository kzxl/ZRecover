using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ZeroRecover.Core.Disk;

/// <summary>
/// Sovereign Win32 raw disk and volume reader enforcing strict sector alignment.
/// Supports both direct hardware volume paths ("\\.\C:") and backing Streams for test harnesses.
/// </summary>
public sealed class RawDiskReader : IDisposable
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(
        SafeFileHandle hFile,
        long liDistanceToMove,
        out long lpNewFilePointer,
        uint dwMoveMethod);

    private const uint FILE_BEGIN = 0;

    private readonly SafeFileHandle? _handle;
    private readonly Stream? _stream;
    private readonly bool _ownsStream;
    private readonly int _sectorSize;
    private bool _disposed;

    public int SectorSize => _sectorSize;

    /// <summary>
    /// Opens a raw volume or drive handle (e.g. "\\.\C:") with non-buffered aligned read access.
    /// </summary>
    public RawDiskReader(string devicePath, int sectorSize = 512)
    {
        _sectorSize = sectorSize > 0 ? sectorSize : 512;
        _handle = CreateFile(
            devicePath,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_NO_BUFFERING | FILE_FLAG_SEQUENTIAL_SCAN,
            IntPtr.Zero);

        if (_handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err, $"Failed to open raw disk device '{devicePath}' (Win32 Error: {err}). Administrator privileges may be required.");
        }
    }

    /// <summary>
    /// Constructs a RawDiskReader backed by an arbitrary Stream (used for unit tests and image files).
    /// </summary>
    public RawDiskReader(Stream stream, int sectorSize = 512, bool ownsStream = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _sectorSize = sectorSize > 0 ? sectorSize : 512;
        _ownsStream = ownsStream;
    }

    /// <summary>
    /// Reads an aligned buffer from the specified 64-bit byte offset on disk.
    /// If using a raw disk handle, offset and buffer length MUST be a multiple of SectorSize.
    /// </summary>
    public int ReadAligned(long byteOffset, byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_stream != null)
        {
            _stream.Seek(byteOffset, SeekOrigin.Begin);
            return _stream.Read(buffer, offset, count);
        }

        if (_handle != null)
        {
            if (byteOffset % _sectorSize != 0)
                throw new ArgumentException($"ByteOffset ({byteOffset}) must be aligned to sector size ({_sectorSize}).");
            if (count % _sectorSize != 0)
                throw new ArgumentException($"Count ({count}) must be a multiple of sector size ({_sectorSize}).");

            if (!SetFilePointerEx(_handle, byteOffset, out _, FILE_BEGIN))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to seek raw disk offset.");
            }

            // Pin buffer in unmanaged memory for direct DMA/kernel transfer
            GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr bufferPtr = pinned.AddrOfPinnedObject() + offset;
                if (!ReadFile(_handle, bufferPtr, (uint)count, out uint bytesRead, IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 38) // ERROR_HANDLE_EOF
                        return 0;
                    throw new Win32Exception(err, "ReadFile failed on raw disk device.");
                }
                return (int)bytesRead;
            }
            finally
            {
                pinned.Free();
            }
        }

        return 0;
    }

    /// <summary>
    /// Reads a specified number of sectors into a newly allocated byte array.
    /// </summary>
    public byte[] ReadSectors(long startSector, int sectorCount)
    {
        int bytesToRead = sectorCount * _sectorSize;
        byte[] buffer = new byte[bytesToRead];
        long byteOffset = startSector * _sectorSize;
        int read = ReadAligned(byteOffset, buffer, 0, bytesToRead);
        if (read < bytesToRead)
        {
            Array.Resize(ref buffer, read);
        }
        return buffer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _handle?.Dispose();
        if (_ownsStream)
        {
            _stream?.Dispose();
        }
    }
}
