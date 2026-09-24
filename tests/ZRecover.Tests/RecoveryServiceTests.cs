using System;
using System.Threading;
using System.Threading.Tasks;
using ZRecover.Core;
using ZRecover.Core.Models;
using Xunit;

namespace ZRecover.Tests;

public class RecoveryServiceTests
{
    [Fact]
    public async Task ExecuteScanAsync_RecycleBin_ExecutesWithProgressReports()
    {
        var service = new RecoveryService();
        var options = new ScanOptions
        {
            TargetDrive = "C:",
            Mode = ScanMode.RecycleBin
        };

        int progressReportCount = 0;
        var progress = new Progress<ScanProgressReport>(_ =>
        {
            progressReportCount++;
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var results = await service.ExecuteScanAsync(options, null, progress, cts.Token);

        Assert.NotNull(results);
        // Ensure that progress was reported during scan
        Assert.True(progressReportCount > 0, "Progress should be reported during scan execution.");
    }

    [Fact]
    public async Task ExecuteScanAsync_QuickUndeleteWithoutRawReader_FallsBackToUserModeScanner()
    {
        var service = new RecoveryService();
        var options = new ScanOptions
        {
            TargetDrive = "C:",
            Mode = ScanMode.QuickUndelete
        };

        bool operationReported = false;
        var progress = new Progress<ScanProgressReport>(report =>
        {
            if (!string.IsNullOrEmpty(report.CurrentOperation))
                operationReported = true;
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var results = await service.ExecuteScanAsync(options, null, progress, cts.Token);

        Assert.NotNull(results);
        Assert.True(operationReported, "CurrentOperation should report actual scan activity during fallback search.");
    }

    [Fact]
    public async Task ExecuteScanAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var service = new RecoveryService();
        var options = new ScanOptions
        {
            TargetDrive = "C:",
            Mode = ScanMode.QuickUndelete
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.ExecuteScanAsync(options, null, null, cts.Token);
        });
    }
}
