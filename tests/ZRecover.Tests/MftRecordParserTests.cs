using System.Buffers.Binary;
using System.Text;
using ZRecover.Core.FileSystems.Ntfs;
using Xunit;

namespace ZRecover.Tests;

public class MftRecordParserTests
{
    [Fact]
    public void MftRecordParser_DeletedRecordWithFileName_IdentifiesAsDeleted()
    {
        byte[] record = new byte[1024];

        // "FILE" magic
        record[0] = (byte)'F'; record[1] = (byte)'I'; record[2] = (byte)'L'; record[3] = (byte)'E';

        // USA offset = 48 (0x30), USA count = 3 (1 USN + 2 sectors)
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4, 2), 48);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6, 2), 3);

        // USN value = 0x1234 at USA offset 48
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(48, 2), 0x1234);
        // Sector 0 end replacement = 0xAA01
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(50, 2), 0xAA01);
        // Sector 1 end replacement = 0xBB02
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(52, 2), 0xBB02);

        // Sector ends (510 and 1022) contain the USN 0x1234
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(510, 2), 0x1234);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1022, 2), 0x1234);

        // First attribute offset = 56 (0x38)
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x14, 2), 56);

        // Flags = 0x0000 (DELETED file, not in use!)
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x16, 2), 0x0000);

        // Construct $FILE_NAME attribute at offset 56:
        // Attribute Type: 0x30 (FileName)
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(56, 4), NtfsAttributeType.FileName);
        // Attribute Length: 120 bytes
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(60, 4), 120);
        // Resident (0)
        record[64] = 0;
        // Value offset = 24 (offset 56 + 24 = 80)
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(56 + 0x14, 2), 24);

        // Value content at offset 80:
        // FileName length = 12 characters ("document.txt")
        string testName = "document.txt";
        record[80 + 64] = (byte)testName.Length;
        record[80 + 65] = 1; // Win32 namespace
        byte[] nameBytes = Encoding.Unicode.GetBytes(testName);
        Array.Copy(nameBytes, 0, record, 80 + 66, nameBytes.Length);

        // End attribute marker at 56 + 120 = 176
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(176, 4), NtfsAttributeType.EndMarker);

        bool success = MftRecordParser.TryParse(record, out var mft);

        Assert.True(success);
        Assert.NotNull(mft);
        Assert.True(mft.IsDeleted);
        Assert.False(mft.IsInUse);
        Assert.Equal("document.txt", mft.PrimaryFileName);
    }
}
