using System.Text;
using ZeroRecover.Core.Models;

namespace ZeroRecover.Core.Carving;

/// <summary>
/// Deep carver for Portable Document Format (.pdf) documents scanning for %PDF header and %%EOF trailer.
/// </summary>
public sealed class PdfCarver : IFileCarver
{
    public string FileExtension => ".pdf";

    public FileCategory Category => FileCategory.Documents;

    private static readonly byte[] PdfHeader = [0x25, 0x50, 0x44, 0x46, 0x2D]; // "%PDF-"
    private static readonly byte[] EofMarker = [0x25, 0x25, 0x45, 0x4F, 0x46]; // "%%EOF"

    public bool CanCarve(ReadOnlySpan<byte> buffer, out int headerOffset)
    {
        headerOffset = -1;
        if (buffer.Length < 5) return false;

        if (buffer[..5].SequenceEqual(PdfHeader))
        {
            headerOffset = 0;
            return true;
        }

        return false;
    }

    public CarveResult Carve(ReadOnlySpan<byte> buffer, int headerOffset)
    {
        if (headerOffset < 0 || headerOffset + 10 >= buffer.Length)
            return CarveResult.Failed;

        int searchLimit = Math.Min(buffer.Length, 150 * 1024 * 1024);
        int lastEof = -1;

        // Scan for the last occurrence of %%EOF
        for (int i = headerOffset + 5; i <= searchLimit - 5; i++)
        {
            if (buffer[i] == 0x25 && buffer[i + 1] == 0x25 &&
                buffer[i + 2] == 0x45 && buffer[i + 3] == 0x4F && buffer[i + 4] == 0x46)
            {
                lastEof = i;
            }
        }

        if (lastEof == -1)
            return CarveResult.Failed;

        // Skip potential trailing whitespace/newlines after %%EOF
        int endOffset = lastEof + 5;
        while (endOffset < buffer.Length && (buffer[endOffset] == 0x0D || buffer[endOffset] == 0x0A || buffer[endOffset] == 0x20))
        {
            endOffset++;
        }

        long totalLength = endOffset - headerOffset;

        return new CarveResult
        {
            Success = true,
            Length = totalLength,
            Extension = FileExtension,
            Category = Category,
            Health = RecoveryHealth.Excellent,
            Description = "Adobe PDF Document (Valid %PDF- to %%EOF)"
        };
    }
}
