using System;
using ZRecover.Core.Carving;
using Xunit;

namespace ZRecover.Tests;

public class ShannonEntropyTests
{
    [Fact]
    public void Calculate_AllZeroBytes_ReturnsZeroEntropy()
    {
        byte[] zeroBuffer = new byte[4096];
        double entropy = ShannonEntropy.Calculate(zeroBuffer);

        Assert.Equal(0.0, entropy, precision: 4);
        Assert.True(ShannonEntropy.IsZeroOrSparse(zeroBuffer));
    }

    [Fact]
    public void Calculate_RandomHighDensity_ReturnsHighEntropy()
    {
        byte[] randomBuffer = new byte[4096];
        new Random(42).NextBytes(randomBuffer);

        double entropy = ShannonEntropy.Calculate(randomBuffer);

        // Random bytes should yield entropy very close to 8.0 bits/byte
        Assert.InRange(entropy, 7.8, 8.0);
        Assert.False(ShannonEntropy.IsZeroOrSparse(randomBuffer));
    }

    [Fact]
    public void Calculate_AsciiText_ReturnsModerateEntropy()
    {
        string text = "The quick brown fox jumps over the lazy dog. Enterprise data recovery engine for Zero Universe.";
        byte[] textBuffer = System.Text.Encoding.ASCII.GetBytes(text);

        double entropy = ShannonEntropy.Calculate(textBuffer);

        // Standard English text typically has entropy between 3.5 and 5.0
        Assert.InRange(entropy, 3.5, 5.0);
    }
}
