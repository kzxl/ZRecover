using System.Buffers.Binary;
using System.Text;
using ZRecover.Core.Disk;
using ZRecover.Core.Models;

namespace ZRecover.Core.FileSystems.Ntfs;

/// <summary>
/// NTFS Volume structure reader parsing $Boot sector and streaming $MFT records.
/// </summary>
public sealed class NtfsVolume
{
    private readonly RawDiskReader _diskReader;

    public int BytesPerSector { get; private set; } = 512;

    public int SectorsPerCluster { get; private set; } = 8;

    public int ClusterSize => BytesPerSector * SectorsPerCluster;

    public long MftStartingCluster { get; private set; }

    public int MftRecordSize { get; private set; } = 1024;

    public NtfsVolume(RawDiskReader diskReader)
    {
        _diskReader = diskReader ?? throw new ArgumentNullException(nameof(diskReader));
        ParseBootSector();
    }

    private void ParseBootSector()
    {
        byte[] bootSector = _diskReader.ReadSectors(0, 1);
        if (bootSector.Length < 512)
            throw new InvalidOperationException("Failed to read NTFS boot sector (insufficient bytes).");

        // Verify NTFS OEM ID "NTFS    "
        string oemId = Encoding.ASCII.GetString(bootSector, 3, 8);
        if (!oemId.StartsWith("NTFS", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Volume is not a valid NTFS filesystem (OEM ID: '{oemId}').");
        }

        BytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(bootSector.AsSpan(0x0B, 2));
        if (BytesPerSector <= 0) BytesPerSector = 512;

        SectorsPerCluster = bootSector[0x0D];
        if (SectorsPerCluster <= 0) SectorsPerCluster = 8;

        MftStartingCluster = BinaryPrimitives.ReadInt64LittleEndian(bootSector.AsSpan(0x30, 8));

        // Clusters per MFT record: if signed byte < 0, record size is 2^|val| bytes (e.g. -10 -> 2^10 = 1024)
        sbyte clustersPerMft = (sbyte)bootSector[0x40];
        if (clustersPerMft < 0)
        {
            MftRecordSize = 1 << (-clustersPerMft);
        }
        else
        {
            MftRecordSize = clustersPerMft * ClusterSize;
        }

        if (MftRecordSize <= 0) MftRecordSize = 1024;
    }

    /// <summary>
    /// Reads a single 1024-byte MFT record by its sequential record index.
    /// </summary>
    public bool TryReadMftRecord(long recordIndex, out MftRecord? record)
    {
        record = null;
        long mftByteOffset = (MftStartingCluster * ClusterSize) + (recordIndex * MftRecordSize);
        byte[] buffer = new byte[MftRecordSize];

        int bytesRead = _diskReader.ReadAligned(mftByteOffset, buffer, 0, MftRecordSize);
        if (bytesRead < MftRecordSize)
            return false;

        return MftRecordParser.TryParse(buffer, out record);
    }
}
