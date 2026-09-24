using System.Buffers.Binary;
using ZRecover.Core.Models;

namespace ZRecover.Core.Carving;

/// <summary>
/// Deep carver for JPEG raster images (.jpg, .jpeg) with marker parsing and Exif thumbnail extraction.
/// </summary>
public sealed class JpegCarver : IFileCarver
{
    public string FileExtension => ".jpg";

    public FileCategory Category => FileCategory.Pictures;

    public bool CanCarve(ReadOnlySpan<byte> buffer, out int headerOffset)
    {
        headerOffset = -1;
        if (buffer.Length < 4) return false;

        // Magic SOI: 0xFF 0xD8 0xFF
        if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
        {
            headerOffset = 0;
            return true;
        }

        return false;
    }

    public CarveResult Carve(ReadOnlySpan<byte> buffer, int headerOffset)
    {
        if (headerOffset < 0 || headerOffset + 4 >= buffer.Length)
            return CarveResult.Failed;

        int index = headerOffset + 2; // Skip 0xFF 0xD8
        byte[]? thumbnailBytes = null;

        // Walk through JPEG markers
        while (index + 4 <= buffer.Length)
        {
            if (buffer[index] != 0xFF)
            {
                index++;
                continue;
            }

            byte marker = buffer[index + 1];

            // Ignore byte stuffing and restart markers
            if (marker == 0x00 || (marker >= 0xD0 && marker <= 0xD7))
            {
                index += 2;
                continue;
            }

            // EOI (End of Image) marker
            if (marker == 0xD9)
            {
                long length = (index + 2) - headerOffset;
                return new CarveResult
                {
                    Success = true,
                    Length = length,
                    Extension = FileExtension,
                    Category = Category,
                    PreviewBytes = thumbnailBytes,
                    Health = RecoveryHealth.Excellent,
                    Description = "JPEG Image (Complete EOI)"
                };
            }

            // SOS (Start of Scan): compressed entropy scan data begins
            if (marker == 0xDA)
            {
                ushort sosLen = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(index + 2, 2));
                index += 2 + sosLen;

                // Scan through entropy data until true EOI
                while (index + 1 < buffer.Length)
                {
                    if (buffer[index] == 0xFF)
                    {
                        byte nextMarker = buffer[index + 1];
                        if (nextMarker == 0xD9) // EOI
                        {
                            long totalLen = (index + 2) - headerOffset;
                            return new CarveResult
                            {
                                Success = true,
                                Length = totalLen,
                                Extension = FileExtension,
                                Category = Category,
                                PreviewBytes = thumbnailBytes,
                                Health = RecoveryHealth.Excellent,
                                Description = "JPEG Image (Scan Stream Complete)"
                            };
                        }
                        if (nextMarker != 0x00 && !(nextMarker >= 0xD0 && nextMarker <= 0xD7))
                        {
                            // Encountered another structural marker
                            break;
                        }
                    }
                    index++;
                }
                continue;
            }

            // Marker with length payload (APPn, DQT, DHT, SOFn, etc.)
            if (index + 4 <= buffer.Length)
            {
                ushort segLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(index + 2, 2));
                if (segLength < 2)
                    break; // Corrupted marker length

                // Check for APP1 Exif thumbnail
                if (marker == 0xE1 && segLength > 14 && thumbnailBytes == null)
                {
                    var segSpan = buffer.Slice(index + 4, segLength - 2);
                    thumbnailBytes = TryExtractExifThumbnail(segSpan);
                }

                index += 2 + segLength;
            }
            else
            {
                break;
            }
        }

        return CarveResult.Failed;
    }

    private static byte[]? TryExtractExifThumbnail(ReadOnlySpan<byte> app1Data)
    {
        // Search for nested JPEG SOI within Exif data (FF D8 FF)
        for (int i = 0; i < app1Data.Length - 4; i++)
        {
            if (app1Data[i] == 0xFF && app1Data[i + 1] == 0xD8 && app1Data[i + 2] == 0xFF)
            {
                for (int j = i + 2; j < app1Data.Length - 1; j++)
                {
                    if (app1Data[j] == 0xFF && app1Data[j + 1] == 0xD9)
                    {
                        int thumbLen = (j + 2) - i;
                        if (thumbLen > 100 && thumbLen < 100_000)
                        {
                            return app1Data.Slice(i, thumbLen).ToArray();
                        }
                    }
                }
            }
        }
        return null;
    }
}
