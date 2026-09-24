using System.Buffers.Binary;
using System.Text;
using ZRecover.Core.Carving;
using ZRecover.Core.Models;
using Xunit;

namespace ZRecover.Tests;

public class SignatureCarverTests
{
    [Fact]
    public void JpegCarver_ValidJpegStream_CarvesExactLength()
    {
        var carver = new JpegCarver();
        // Construct minimal valid JPEG
        byte[] jpeg = [
            0xFF, 0xD8, 0xFF, 0xE0, // SOI + APP0
            0x00, 0x10,             // APP0 length (16 bytes)
            0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xDA,             // SOS marker
            0x00, 0x08,             // SOS length (8 bytes)
            0x01, 0x01, 0x00, 0x00, 0x3F, 0x00,
            0x12, 0x34, 0x56, 0x78, // Compressed scan data
            0xFF, 0xD9              // EOI marker
        ];

        Assert.True(carver.CanCarve(jpeg, out int offset));
        Assert.Equal(0, offset);

        var result = carver.Carve(jpeg, offset);
        Assert.True(result.Success);
        Assert.Equal(jpeg.Length, result.Length);
        Assert.Equal(".jpg", result.Extension);
        Assert.Equal(FileCategory.Pictures, result.Category);
    }

    [Fact]
    public void PngCarver_ValidPngStream_CarvesExactLength()
    {
        var carver = new PngCarver();

        // 8-byte header
        byte[] header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        // IHDR chunk: 13 bytes data + 12 overhead = 25 bytes
        byte[] ihdrChunk = [
            0x00, 0x00, 0x00, 0x0D, // Length: 13
            0x49, 0x48, 0x44, 0x52, // "IHDR"
            0x00, 0x00, 0x00, 0x01, // width: 1
            0x00, 0x00, 0x00, 0x01, // height: 1
            0x08, 0x06, 0x00, 0x00, 0x00,
            0x1F, 0x15, 0xC4, 0x89  // CRC32
        ];
        // IEND chunk: 0 bytes data + 12 overhead = 12 bytes
        byte[] iendChunk = [
            0x00, 0x00, 0x00, 0x00, // Length: 0
            0x49, 0x45, 0x4E, 0x44, // "IEND"
            0xAE, 0x42, 0x60, 0x82  // CRC32
        ];

        byte[] png = [..header, ..ihdrChunk, ..iendChunk];

        Assert.True(carver.CanCarve(png, out int offset));
        Assert.Equal(0, offset);

        var result = carver.Carve(png, offset);
        Assert.True(result.Success);
        Assert.Equal(png.Length, result.Length);
        Assert.Equal(".png", result.Extension);
        Assert.Equal(FileCategory.Pictures, result.Category);
    }

    [Fact]
    public void ZipOfficeCarver_ValidZipWithEocd_CarvesExactLength()
    {
        var carver = new ZipOfficeCarver();

        // Local file header: 30 bytes minimum
        byte[] localHeader = [
            0x50, 0x4B, 0x03, 0x04, // "PK\x03\x04"
            0x14, 0x00, 0x00, 0x00, 0x08, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, // CRC32
            0x04, 0x00, 0x00, 0x00, // Compressed size
            0x04, 0x00, 0x00, 0x00, // Uncompressed size
            0x04, 0x00,             // File name length (4)
            0x00, 0x00,             // Extra field length
            0x74, 0x65, 0x73, 0x74, // "test"
            0xAA, 0xBB, 0xCC, 0xDD  // data
        ];

        // End of central directory (EOCD): 22 bytes
        byte[] eocd = [
            0x50, 0x4B, 0x05, 0x06, // "PK\x05\x06"
            0x00, 0x00,             // disk
            0x00, 0x00,             // disk start
            0x01, 0x00,             // count disk
            0x01, 0x00,             // count total
            0x20, 0x00, 0x00, 0x00, // CD size
            0x26, 0x00, 0x00, 0x00, // CD offset
            0x00, 0x00              // comment length
        ];

        byte[] zip = [..localHeader, ..eocd];

        Assert.True(carver.CanCarve(zip, out int offset));
        Assert.Equal(0, offset);

        var result = carver.Carve(zip, offset);
        Assert.True(result.Success);
        Assert.Equal(zip.Length, result.Length);
        Assert.Equal(".zip", result.Extension);
    }

    [Fact]
    public void PdfCarver_ValidPdf_CarvesUntilEof()
    {
        var carver = new PdfCarver();
        string pdfText = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n";
        byte[] pdf = Encoding.ASCII.GetBytes(pdfText);

        Assert.True(carver.CanCarve(pdf, out int offset));
        var result = carver.Carve(pdf, offset);

        Assert.True(result.Success);
        Assert.Equal(pdf.Length, result.Length);
        Assert.Equal(".pdf", result.Extension);
        Assert.Equal(FileCategory.Documents, result.Category);
    }

    [Fact]
    public void MediaBoxCarver_RiffWave_CarvesAccordingToSizeField()
    {
        var carver = new MediaBoxCarver();
        byte[] riff = new byte[100];
        // "RIFF"
        riff[0] = 0x52; riff[1] = 0x49; riff[2] = 0x46; riff[3] = 0x46;
        // Size = 92 (Total file = 92 + 8 = 100 bytes)
        BinaryPrimitives.WriteUInt32LittleEndian(riff.AsSpan(4, 4), 92);
        // "WAVE"
        riff[8] = (byte)'W'; riff[9] = (byte)'A'; riff[10] = (byte)'V'; riff[11] = (byte)'E';

        Assert.True(carver.CanCarve(riff, out int offset));
        var result = carver.Carve(riff, offset);

        Assert.True(result.Success);
        Assert.Equal(100, result.Length);
        Assert.Equal(".wav", result.Extension);
        Assert.Equal(FileCategory.Audio, result.Category);
    }

    [Fact]
    public void DatabaseCarver_ValidSqlite_ComputesExactFileSize()
    {
        var carver = new DatabaseCarver();
        byte[] sqlite = new byte[4096 * 5]; // 5 pages of 4096 bytes = 20480 bytes
        byte[] header = "SQLite format 3\0"u8.ToArray();
        Array.Copy(header, sqlite, 16);

        // Page size at offset 16 (4096 = 0x1000)
        BinaryPrimitives.WriteUInt16BigEndian(sqlite.AsSpan(16, 2), 4096);
        // Page count at offset 28 (5 pages)
        BinaryPrimitives.WriteUInt32BigEndian(sqlite.AsSpan(28, 4), 5);

        Assert.True(carver.CanCarve(sqlite, out int offset));
        var result = carver.Carve(sqlite, offset);

        Assert.True(result.Success);
        Assert.Equal(20480, result.Length);
        Assert.Equal(".sqlite", result.Extension);
        Assert.Equal(FileCategory.Databases, result.Category);
    }
}
