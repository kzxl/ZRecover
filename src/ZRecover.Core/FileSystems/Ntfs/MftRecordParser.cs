using System.Buffers.Binary;
using System.Text;
using ZRecover.Core.Models;

namespace ZRecover.Core.FileSystems.Ntfs;

/// <summary>
/// Parsed representation of an NTFS Master File Table (MFT) record.
/// </summary>
public sealed class MftRecord
{
    public uint RecordNumber { get; set; }

    public bool IsInUse { get; set; }

    public bool IsDirectory { get; set; }

    public bool IsDeleted => !IsInUse;

    public List<NtfsFileNameInfo> FileNames { get; set; } = [];

    public List<NtfsDataInfo> DataAttributes { get; set; } = [];

    public DateTime? CreatedTime { get; set; }

    public DateTime? ModifiedTime { get; set; }

    /// <summary>
    /// Preferred primary filename (preferring Win32 / POSIX namespace over short 8.3 names).
    /// </summary>
    public string PrimaryFileName
    {
        get
        {
            if (FileNames.Count == 0) return string.Empty;
            var win32Name = FileNames.FirstOrDefault(f => f.Namespace == 1 || f.Namespace == 3);
            return win32Name?.FileName ?? FileNames[0].FileName;
        }
    }

    /// <summary>
    /// Gets the primary unnamed $DATA attribute, or the first available data attribute.
    /// </summary>
    public NtfsDataInfo? DefaultData => DataAttributes.FirstOrDefault(d => string.IsNullOrEmpty(d.AttributeName)) ?? DataAttributes.FirstOrDefault();
}

/// <summary>
/// Sovereign parser for 1024-byte NTFS MFT FILE records with Fixup Array restoration.
/// </summary>
public static class MftRecordParser
{
    private static readonly byte[] FileSignature = "FILE"u8.ToArray();

    public static bool TryParse(byte[] rawRecord, out MftRecord? parsedRecord)
    {
        parsedRecord = null;
        if (rawRecord == null || rawRecord.Length < 1024)
            return false;

        // Check "FILE" magic header
        if (rawRecord[0] != FileSignature[0] ||
            rawRecord[1] != FileSignature[1] ||
            rawRecord[2] != FileSignature[2] ||
            rawRecord[3] != FileSignature[3])
        {
            return false;
        }

        // Apply Fixup Array (Update Sequence Array) to restore true sector end bytes
        byte[] record = (byte[])rawRecord.Clone();
        ushort usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(4, 2));
        ushort usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(6, 2));

        if (usaOffset > 0 && usaCount > 1 && usaOffset + usaCount * 2 <= record.Length)
        {
            ushort updateSequenceNumber = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(usaOffset, 2));
            for (int i = 1; i < usaCount; i++)
            {
                int sectorEndOffset = (i * 512) - 2;
                if (sectorEndOffset + 2 <= record.Length)
                {
                    ushort replacementVal = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(usaOffset + i * 2, 2));
                    // Overwrite the USN with the true original bytes
                    record[sectorEndOffset] = (byte)(replacementVal & 0xFF);
                    record[sectorEndOffset + 1] = (byte)((replacementVal >> 8) & 0xFF);
                }
            }
        }

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(0x16, 2));
        ushort firstAttrOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(0x14, 2));
        uint recordNumber = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(0x2C, 4));

        var mft = new MftRecord
        {
            RecordNumber = recordNumber,
            IsInUse = (flags & 0x0001) != 0,
            IsDirectory = (flags & 0x0002) != 0
        };

        // Parse attributes loop
        int offset = firstAttrOffset;
        while (offset + 8 <= record.Length)
        {
            uint attrType = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(offset, 4));
            if (attrType == NtfsAttributeType.EndMarker || attrType == 0)
                break;

            uint attrLength = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(offset + 4, 4));
            if (attrLength < 8 || offset + attrLength > record.Length)
                break; // Corrupted record boundary

            byte nonResident = record[offset + 8];
            byte nameLength = record[offset + 9];
            ushort nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(offset + 10, 2));

            string attrName = string.Empty;
            if (nameLength > 0 && offset + nameOffset + (nameLength * 2) <= offset + attrLength)
            {
                attrName = Encoding.Unicode.GetString(record, offset + nameOffset, nameLength * 2);
            }

            if (attrType == NtfsAttributeType.StandardInformation && nonResident == 0)
            {
                ushort valOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(offset + 0x14, 2));
                if (offset + valOffset + 16 <= record.Length)
                {
                    long createFt = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(offset + valOffset, 8));
                    long modFt = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(offset + valOffset + 8, 8));
                    mft.CreatedTime = DateTimeFromFileTimeSafe(createFt);
                    mft.ModifiedTime = DateTimeFromFileTimeSafe(modFt);
                }
            }
            else if (attrType == NtfsAttributeType.FileName && nonResident == 0)
            {
                ushort valOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(offset + 0x14, 2));
                int fnBase = offset + valOffset;
                if (fnBase + 66 <= record.Length)
                {
                    long parentDir = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(fnBase, 8)) & 0x0000FFFFFFFFFFFFL;
                    long createFt = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(fnBase + 8, 8));
                    long modFt = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(fnBase + 16, 8));
                    long realSize = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(fnBase + 48, 8));
                    byte fnLen = record[fnBase + 64];
                    byte ns = record[fnBase + 65];

                    if (fnBase + 66 + (fnLen * 2) <= record.Length)
                    {
                        string fileName = Encoding.Unicode.GetString(record, fnBase + 66, fnLen * 2);
                        mft.FileNames.Add(new NtfsFileNameInfo
                        {
                            ParentDirectoryRecordNumber = parentDir,
                            FileName = fileName,
                            CreatedTime = DateTimeFromFileTimeSafe(createFt) ?? DateTime.MinValue,
                            ModifiedTime = DateTimeFromFileTimeSafe(modFt) ?? DateTime.MinValue,
                            RealSize = realSize,
                            Namespace = ns
                        });
                    }
                }
            }
            else if (attrType == NtfsAttributeType.Data)
            {
                var dataInfo = new NtfsDataInfo
                {
                    AttributeName = attrName,
                    IsResident = nonResident == 0
                };

                if (nonResident == 0)
                {
                    // Resident data
                    uint valLength = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(offset + 0x10, 4));
                    ushort valOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(offset + 0x14, 2));
                    dataInfo.RealSize = valLength;

                    if (offset + valOffset + valLength <= record.Length)
                    {
                        dataInfo.ResidentBytes = new byte[valLength];
                        Array.Copy(record, offset + valOffset, dataInfo.ResidentBytes, 0, valLength);
                    }
                }
                else
                {
                    // Non-resident data
                    if (offset + 0x40 <= record.Length)
                    {
                        ushort runlistOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(offset + 0x20, 2));
                        long realSize = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(offset + 0x30, 8));
                        dataInfo.RealSize = realSize;

                        if (offset + runlistOffset < offset + attrLength)
                        {
                            int runlistLength = (int)(attrLength - runlistOffset);
                            ReadOnlySpan<byte> runlistSpan = record.AsSpan(offset + runlistOffset, runlistLength);
                            dataInfo.DataRuns = DataRunDecoder.DecodeRunList(runlistSpan);
                        }
                    }
                }

                mft.DataAttributes.Add(dataInfo);
            }

            offset += (int)attrLength;
        }

        parsedRecord = mft;
        return true;
    }

    private static DateTime? DateTimeFromFileTimeSafe(long fileTime)
    {
        if (fileTime <= 0 || fileTime > 0x7FFF_FFFF_FFFF_FFFFL)
            return null;
        try
        {
            return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
        }
        catch
        {
            return null;
        }
    }
}
