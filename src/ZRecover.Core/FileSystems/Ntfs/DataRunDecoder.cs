using ZRecover.Core.Models;

namespace ZRecover.Core.FileSystems.Ntfs;

/// <summary>
/// A decoded runlist fragment representing a contiguous cluster extent on an NTFS volume.
/// </summary>
public readonly record struct NtfsDataRun(long StartLcn, long ClusterCount, bool IsSparse)
{
    public DataRunExtent ToExtent() => new(StartLcn, ClusterCount);
}

/// <summary>
/// Sovereign decoder for NTFS non-resident attribute cluster runlists.
/// Correctly handles variable byte field lengths, two's complement signed relative cluster offsets, and sparse extents.
/// </summary>
public static class DataRunDecoder
{
    public static List<NtfsDataRun> DecodeRunList(ReadOnlySpan<byte> runlistSpan)
    {
        var runs = new List<NtfsDataRun>();
        int index = 0;
        long currentLcn = 0;

        while (index < runlistSpan.Length)
        {
            byte header = runlistSpan[index++];
            if (header == 0x00) // End of runlist marker
                break;

            int lenBytes = header & 0x0F;
            int offsetBytes = (header >> 4) & 0x0F;

            if (lenBytes == 0 || index + lenBytes > runlistSpan.Length)
                break; // Corrupted runlist

            // Read cluster count (unsigned)
            long clusterCount = 0;
            for (int i = 0; i < lenBytes; i++)
            {
                clusterCount |= ((long)runlistSpan[index++]) << (i * 8);
            }

            // Read cluster offset (signed relative delta)
            if (offsetBytes == 0)
            {
                // Sparse run (clusters not committed to disk)
                runs.Add(new NtfsDataRun(0, clusterCount, IsSparse: true));
                continue;
            }

            if (index + offsetBytes > runlistSpan.Length)
                break; // Corrupted runlist

            long lcnDelta = 0;
            for (int i = 0; i < offsetBytes; i++)
            {
                lcnDelta |= ((long)runlistSpan[index++]) << (i * 8);
            }

            // Check if the most significant bit is set for sign extension
            int signBitPos = (offsetBytes * 8) - 1;
            if ((lcnDelta & (1L << signBitPos)) != 0)
            {
                // Sign-extend negative number
                long mask = -1L << (offsetBytes * 8);
                lcnDelta |= mask;
            }

            currentLcn += lcnDelta;
            runs.Add(new NtfsDataRun(currentLcn, clusterCount, IsSparse: false));
        }

        return runs;
    }
}
