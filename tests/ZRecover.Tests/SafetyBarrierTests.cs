using System;
using ZRecover.Core.Safety;
using Xunit;

namespace ZRecover.Tests;

public class SafetyBarrierTests
{
    [Fact]
    public void ValidateDestination_SameDrive_ThrowsInvalidOperationException()
    {
        string sourceDrive = "C:";
        string unsafeDest = @"C:\Users\User\RestoredFiles";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SafetyBarrier.ValidateDestination(sourceDrive, unsafeDest));

        Assert.Contains("Zero-Write Safety Barrier Alert", ex.Message);
        Assert.Contains("overwrite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateDestination_DifferentDrive_Succeeds()
    {
        string sourceDrive = "C:";
        string safeDest = @"D:\BackupRestoration";

        // Should not throw
        SafetyBarrier.ValidateDestination(sourceDrive, safeDest);
    }

    [Fact]
    public void ValidateDestination_DriveLetterVariations_NormalizesSafely()
    {
        string sourceDrive = "c";
        string unsafeDest = @"C:\Recovered";

        Assert.Throws<InvalidOperationException>(() =>
            SafetyBarrier.ValidateDestination(sourceDrive, unsafeDest));
    }
}
