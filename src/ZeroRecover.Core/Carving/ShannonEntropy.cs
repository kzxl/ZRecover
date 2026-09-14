namespace ZeroRecover.Core.Carving;

/// <summary>
/// Sovereign Shannon Entropy calculator for detecting compression density,
/// uncompressed structures, zero-slack space, and fragmentation gap boundaries.
/// </summary>
public static class ShannonEntropy
{
    /// <summary>
    /// Computes Shannon entropy (0.0 to 8.0 bits per byte) over a byte span.
    /// </summary>
    public static double Calculate(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return 0.0;

        Span<int> frequencies = stackalloc int[256];
        frequencies.Clear();

        for (int i = 0; i < data.Length; i++)
        {
            frequencies[data[i]]++;
        }

        double entropy = 0.0;
        double len = data.Length;

        for (int i = 0; i < 256; i++)
        {
            int count = frequencies[i];
            if (count > 0)
            {
                double p = count / len;
                entropy -= p * Math.Log2(p);
            }
        }

        return entropy;
    }

    /// <summary>
    /// Checks if a cluster/block appears to be zero-filled or empty slack space.
    /// </summary>
    public static bool IsZeroOrSparse(ReadOnlySpan<byte> data, double zeroThresholdPercent = 0.98)
    {
        if (data.IsEmpty) return true;
        int zeroCount = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == 0) zeroCount++;
        }
        return (double)zeroCount / data.Length >= zeroThresholdPercent;
    }
}
