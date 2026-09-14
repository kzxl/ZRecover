using ZeroRecover.Core.FileSystems.Ntfs;
using Xunit;

namespace ZeroRecover.Tests;

public class DataRunDecoderTests
{
    [Fact]
    public void DecodeRunList_SinglePositiveRun_ParsesCorrectly()
    {
        // Header: 0x21 -> 1 byte for length, 2 bytes for offset
        // Length: 0x10 (16 clusters)
        // Offset: 0x0100 (256 LCN)
        // End marker: 0x00
        byte[] runlist = [0x21, 0x10, 0x00, 0x01, 0x00];

        var runs = DataRunDecoder.DecodeRunList(runlist);

        Assert.Single(runs);
        Assert.Equal(16, runs[0].ClusterCount);
        Assert.Equal(256, runs[0].StartLcn);
        Assert.False(runs[0].IsSparse);
    }

    [Fact]
    public void DecodeRunList_NegativeDelta_SignExtendsCorrectly()
    {
        // First run: Header 0x21, Length 10, Offset 1000 (0x03E8) -> LCN 1000
        // Second run: Header 0x11, Length 5, Offset -10 (0xF6) -> LCN 990
        // End marker: 0x00
        byte[] runlist = [
            0x21, 0x0A, 0xE8, 0x03,       // Run 1: len=10, lcn=+1000
            0x11, 0x05, 0xF6,             // Run 2: len=5, delta=-10 (signed byte 0xF6) -> lcn=990
            0x00                          // End
        ];

        var runs = DataRunDecoder.DecodeRunList(runlist);

        Assert.Equal(2, runs.Count);
        Assert.Equal(10, runs[0].ClusterCount);
        Assert.Equal(1000, runs[0].StartLcn);

        Assert.Equal(5, runs[1].ClusterCount);
        Assert.Equal(990, runs[1].StartLcn); // 1000 + (-10) = 990
    }

    [Fact]
    public void DecodeRunList_SparseRun_MarksAsSparse()
    {
        // Header: 0x02 -> 2 bytes length, 0 bytes offset (Sparse)
        // Length: 0x0020 (32 clusters)
        byte[] runlist = [0x02, 0x20, 0x00, 0x00];

        var runs = DataRunDecoder.DecodeRunList(runlist);

        Assert.Single(runs);
        Assert.Equal(32, runs[0].ClusterCount);
        Assert.True(runs[0].IsSparse);
    }
}
