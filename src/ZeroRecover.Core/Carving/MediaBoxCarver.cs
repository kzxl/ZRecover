using System.Buffers.Binary;
using System.Text;
using ZeroRecover.Core.Models;

namespace ZeroRecover.Core.Carving;

/// <summary>
/// Deep carver for containerized audio and video formats:
/// 1. ISO Base Media (.mp4, .mov, .m4a) via Atom Box sequence walk.
/// 2. Resource Interchange File Format (.wav, .avi, .webp) via RIFF chunk sizing.
/// </summary>
public sealed class MediaBoxCarver : IFileCarver
{
    public string FileExtension => ".mp4";

    public FileCategory Category => FileCategory.Videos;

    private static readonly byte[] FtypBytes = [0x66, 0x74, 0x79, 0x70]; // "ftyp"
    private static readonly byte[] RiffBytes = [0x52, 0x49, 0x46, 0x46]; // "RIFF"

    public bool CanCarve(ReadOnlySpan<byte> buffer, out int headerOffset)
    {
        headerOffset = -1;
        if (buffer.Length < 12) return false;

        // Check MP4/MOV: 4 bytes size + "ftyp"
        if (buffer.Slice(4, 4).SequenceEqual(FtypBytes))
        {
            headerOffset = 0;
            return true;
        }

        // Check RIFF: "RIFF" at offset 0
        if (buffer[..4].SequenceEqual(RiffBytes))
        {
            headerOffset = 0;
            return true;
        }

        return false;
    }

    public CarveResult Carve(ReadOnlySpan<byte> buffer, int headerOffset)
    {
        if (headerOffset < 0 || headerOffset + 12 >= buffer.Length)
            return CarveResult.Failed;

        // 1. Check RIFF formats (.wav, .avi, .webp)
        if (buffer.Slice(headerOffset, 4).SequenceEqual(RiffBytes))
        {
            uint riffSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(headerOffset + 4, 4));
            long totalRiffLen = (long)riffSize + 8;

            if (totalRiffLen > buffer.Length || totalRiffLen <= 12)
                return CarveResult.Failed;

            string tag = Encoding.ASCII.GetString(buffer.Slice(headerOffset + 8, 4));
            string ext = ".wav";
            FileCategory cat = FileCategory.Audio;
            string desc = "RIFF Wave Audio (.wav)";

            if (tag == "AVI ")
            {
                ext = ".avi";
                cat = FileCategory.Videos;
                desc = "Audio Video Interleave (.avi)";
            }
            else if (tag == "WEBP")
            {
                ext = ".webp";
                cat = FileCategory.Pictures;
                desc = "WebP Raster Image (.webp)";
            }

            return new CarveResult
            {
                Success = true,
                Length = totalRiffLen,
                Extension = ext,
                Category = cat,
                Health = RecoveryHealth.Excellent,
                Description = desc
            };
        }

        // 2. Walk ISO Base Media MP4/MOV Atom Boxes
        int index = headerOffset;
        bool hasMoov = false;
        bool hasMdat = false;
        long totalMp4Len = 0;

        while (index + 8 <= buffer.Length)
        {
            uint boxSize = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(index, 4));
            string boxType = Encoding.ASCII.GetString(buffer.Slice(index + 4, 4));

            long actualBoxSize = boxSize;
            if (boxSize == 1) // 64-bit extended box size
            {
                if (index + 16 > buffer.Length) break;
                actualBoxSize = BinaryPrimitives.ReadInt64BigEndian(buffer.Slice(index + 8, 8));
            }
            else if (boxSize == 0) // Box extends to end of file
            {
                actualBoxSize = buffer.Length - index;
            }

            if (actualBoxSize <= 0 || actualBoxSize > 10L * 1024 * 1024 * 1024) // 10GB limit safety
                break;

            if (boxType == "moov") hasMoov = true;
            if (boxType == "mdat") hasMdat = true;

            totalMp4Len = (index + actualBoxSize) - headerOffset;
            index += (int)Math.Min(actualBoxSize, int.MaxValue);

            // If we have encountered both metadata (moov) and payload (mdat), and the next 4 bytes are not a valid box or zero, stop
            if (hasMoov && hasMdat)
            {
                if (index + 8 > buffer.Length || !IsValidAtomChar(buffer[index + 4]))
                {
                    break;
                }
            }
        }

        if (totalMp4Len > 0 && (hasMoov || hasMdat))
        {
            return new CarveResult
            {
                Success = true,
                Length = totalMp4Len,
                Extension = ".mp4",
                Category = FileCategory.Videos,
                Health = RecoveryHealth.Excellent,
                Description = "MP4 / ISO Media Container"
            };
        }

        return CarveResult.Failed;
    }

    private static bool IsValidAtomChar(byte b) => (b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9');
}
