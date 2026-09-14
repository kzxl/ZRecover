using System.Buffers.Binary;
using System.Text;
using ZeroRecover.Core.Models;

namespace ZeroRecover.Core.Carving;

/// <summary>
/// Deep carver for ZIP archives and Office OpenXML documents (.zip, .docx, .xlsx, .pptx).
/// Traverses End of Central Directory (EOCD) to determine exact byte boundaries and classifies Office variants.
/// </summary>
public sealed class ZipOfficeCarver : IFileCarver
{
    public string FileExtension => ".zip";

    public FileCategory Category => FileCategory.Archives;

    private static readonly byte[] LocalHeaderSig = [0x50, 0x4B, 0x03, 0x04]; // "PK\x03\x04"
    private static readonly byte[] EocdSig = [0x50, 0x4B, 0x05, 0x06];        // "PK\x05\x06"

    public bool CanCarve(ReadOnlySpan<byte> buffer, out int headerOffset)
    {
        headerOffset = -1;
        if (buffer.Length < 4) return false;

        if (buffer[..4].SequenceEqual(LocalHeaderSig))
        {
            headerOffset = 0;
            return true;
        }

        return false;
    }

    public CarveResult Carve(ReadOnlySpan<byte> buffer, int headerOffset)
    {
        if (headerOffset < 0 || headerOffset + 22 >= buffer.Length)
            return CarveResult.Failed;

        // Search for EOCD record (PK\x05\x06) within buffer (up to 200MB or buffer length)
        int searchLimit = Math.Min(buffer.Length, 200 * 1024 * 1024);
        int lastEocd = -1;

        for (int i = headerOffset; i <= searchLimit - 22; i++)
        {
            if (buffer[i] == 0x50 && buffer[i + 1] == 0x4B && buffer[i + 2] == 0x05 && buffer[i + 3] == 0x06)
            {
                lastEocd = i;
            }
        }

        if (lastEocd == -1)
            return CarveResult.Failed;

        // Parse EOCD to calculate full length
        ushort commentLen = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(lastEocd + 20, 2));
        long totalLength = (lastEocd + 22 + commentLen) - headerOffset;

        if (totalLength > buffer.Length)
            return CarveResult.Failed;

        // Classify Office documents vs generic ZIP by scanning for distinctive path fragments in Central Directory
        var zipSlice = buffer.Slice(headerOffset, (int)Math.Min(totalLength, buffer.Length));
        string detectedExt = ".zip";
        FileCategory detectedCat = FileCategory.Archives;
        string desc = "Standard ZIP Archive";

        if (ContainsAscii(zipSlice, "word/document.xml"))
        {
            detectedExt = ".docx";
            detectedCat = FileCategory.Documents;
            desc = "Microsoft Word Document (.docx)";
        }
        else if (ContainsAscii(zipSlice, "xl/workbook.xml"))
        {
            detectedExt = ".xlsx";
            detectedCat = FileCategory.Documents;
            desc = "Microsoft Excel Spreadsheet (.xlsx)";
        }
        else if (ContainsAscii(zipSlice, "ppt/presentation.xml"))
        {
            detectedExt = ".pptx";
            detectedCat = FileCategory.Documents;
            desc = "Microsoft PowerPoint Presentation (.pptx)";
        }

        return new CarveResult
        {
            Success = true,
            Length = totalLength,
            Extension = detectedExt,
            Category = detectedCat,
            Health = RecoveryHealth.Excellent,
            Description = desc
        };
    }

    private static bool ContainsAscii(ReadOnlySpan<byte> data, string needle)
    {
        byte[] needleBytes = Encoding.ASCII.GetBytes(needle);
        return data.IndexOf(needleBytes) >= 0;
    }
}
