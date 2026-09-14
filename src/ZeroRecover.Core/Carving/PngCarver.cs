using System.Buffers.Binary;
using System.Text;
using ZeroRecover.Core.Models;

namespace ZeroRecover.Core.Carving;

/// <summary>
/// Deep carver for Portable Network Graphics (.png) files with chunk iteration and IEND boundary detection.
/// </summary>
public sealed class PngCarver : IFileCarver
{
    public string FileExtension => ".png";

    public FileCategory Category => FileCategory.Pictures;

    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] IendBytes = [0x49, 0x45, 0x4E, 0x44]; // "IEND"

    public bool CanCarve(ReadOnlySpan<byte> buffer, out int headerOffset)
    {
        headerOffset = -1;
        if (buffer.Length < 8) return false;

        if (buffer[..8].SequenceEqual(PngHeader))
        {
            headerOffset = 0;
            return true;
        }

        return false;
    }

    public CarveResult Carve(ReadOnlySpan<byte> buffer, int headerOffset)
    {
        if (headerOffset < 0 || headerOffset + 8 >= buffer.Length)
            return CarveResult.Failed;

        int index = headerOffset + 8; // Skip 8-byte PNG header
        bool foundIhdr = false;

        while (index + 12 <= buffer.Length)
        {
            uint dataLength = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(index, 4));
            var chunkType = buffer.Slice(index + 4, 4);

            if (!foundIhdr)
            {
                if (chunkType[0] == (byte)'I' && chunkType[1] == (byte)'H' &&
                    chunkType[2] == (byte)'D' && chunkType[3] == (byte)'R')
                {
                    foundIhdr = true;
                }
                else
                {
                    // PNG must start with IHDR chunk
                    return CarveResult.Failed;
                }
            }

            // Check for IEND chunk (End of PNG)
            if (chunkType.SequenceEqual(IendBytes))
            {
                // IEND has 0-length payload + 4 bytes CRC = 12 bytes total chunk size
                long totalLength = (index + 12) - headerOffset;
                return new CarveResult
                {
                    Success = true,
                    Length = totalLength,
                    Extension = FileExtension,
                    Category = Category,
                    Health = RecoveryHealth.Excellent,
                    Description = "PNG Image (Valid IHDR to IEND)"
                };
            }

            // Advance to next chunk: 4 (length) + 4 (type) + dataLength + 4 (CRC32)
            long nextChunk = (long)index + 12 + dataLength;
            if (nextChunk > buffer.Length || nextChunk < index)
            {
                // File exceeds buffer or length corrupted
                break;
            }

            index = (int)nextChunk;
        }

        return CarveResult.Failed;
    }
}
