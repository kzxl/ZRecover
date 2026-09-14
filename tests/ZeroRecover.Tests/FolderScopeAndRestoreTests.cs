using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ZeroRecover.Core;
using ZeroRecover.Core.Models;
using Xunit;

namespace ZeroRecover.Tests;

public class FolderScopeAndRestoreTests : IDisposable
{
    private readonly string _tempTestDir;

    public FolderScopeAndRestoreTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), "ZeroRecover_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempTestDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempTestDir))
            {
                Directory.Delete(_tempTestDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task RestoreFileAsync_PreserveFolderStructureTrue_RecreatesHierarchy()
    {
        var service = new RecoveryService();
        string destDir = Path.Combine(_tempTestDir, "RestoredOutput");

        string destDrive = Path.GetPathRoot(destDir)!;
        string mockSourceDrive = destDrive.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? "D:" : "C:";

        var file = new RecoverableFile
        {
            FileName = "document.txt",
            OriginalPath = $@"{mockSourceDrive}\Finance\Quarterly\Reports\document.txt",
            SourceDrive = mockSourceDrive,
            IsResident = true,
            ResidentData = Encoding.UTF8.GetBytes("Quarterly Report Data"),
            CreatedTime = new DateTime(2025, 5, 10, 8, 30, 0, DateTimeKind.Local),
            ModifiedTime = new DateTime(2025, 5, 12, 14, 0, 0, DateTimeKind.Local)
        };

        string restoredPath = await service.RestoreFileAsync(file, destDir, preserveFolderStructure: true);

        Assert.True(File.Exists(restoredPath));
        string expectedSubdir = Path.Combine(destDir, "Finance", "Quarterly", "Reports");
        Assert.True(Directory.Exists(expectedSubdir));
        Assert.Equal(Path.Combine(expectedSubdir, "document.txt"), restoredPath);

        string content = await File.ReadAllTextAsync(restoredPath);
        Assert.Equal("Quarterly Report Data", content);
    }

    [Fact]
    public async Task RestoreFileAsync_PreserveFolderStructureFalse_RestoresDirectlyToDestination()
    {
        var service = new RecoveryService();
        string destDir = Path.Combine(_tempTestDir, "FlatOutput");

        string destDrive = Path.GetPathRoot(destDir)!;
        string mockSourceDrive = destDrive.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? "D:" : "C:";

        var file = new RecoverableFile
        {
            FileName = "notes.txt",
            OriginalPath = $@"{mockSourceDrive}\Work\Projects\Alpha\notes.txt",
            SourceDrive = mockSourceDrive,
            IsResident = true,
            ResidentData = Encoding.UTF8.GetBytes("Flat Restore Notes")
        };

        string restoredPath = await service.RestoreFileAsync(file, destDir, preserveFolderStructure: false);

        Assert.True(File.Exists(restoredPath));
        Assert.Equal(Path.Combine(destDir, "notes.txt"), restoredPath);
        Assert.False(Directory.Exists(Path.Combine(destDir, "Work")));
    }

    [Fact]
    public async Task RestoreFileAsync_SameSourceAndDestinationDrive_ThrowsInvalidOperationException()
    {
        var service = new RecoveryService();
        string destDir = Path.Combine(_tempTestDir, "UnsafeRestore");
        string destDrive = Path.GetPathRoot(destDir)!;

        var file = new RecoverableFile
        {
            FileName = "risky.txt",
            OriginalPath = Path.Combine(destDir, "risky.txt"),
            SourceDrive = destDrive, // Target drive equals destination drive!
            IsResident = true,
            ResidentData = Encoding.UTF8.GetBytes("Data")
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await service.RestoreFileAsync(file, destDir, preserveFolderStructure: true);
        });
    }

    [Fact]
    public async Task RestoreFileAsync_DefaultOverload_PreservesFolderStructure()
    {
        var service = new RecoveryService();
        string destDir = Path.Combine(_tempTestDir, "OverloadOutput");

        string destDrive = Path.GetPathRoot(destDir)!;
        string mockSourceDrive = destDrive.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? "D:" : "C:";

        var file = new RecoverableFile
        {
            FileName = "contract.pdf",
            OriginalPath = $@"{mockSourceDrive}\Legal\Contracts\contract.pdf",
            SourceDrive = mockSourceDrive,
            IsResident = true,
            ResidentData = Encoding.UTF8.GetBytes("%PDF-1.4 Mock")
        };

        // Calling backwards-compatible 4-argument overload
        string restoredPath = await service.RestoreFileAsync(file, destDir, optionalDiskReader: null);

        Assert.True(File.Exists(restoredPath));
        Assert.Contains(@"Legal\Contracts", restoredPath);
    }

    [Fact]
    public async Task ExecuteScanAsync_FolderScope_FindsCandidatesInTargetDirectory()
    {
        var service = new RecoveryService();
        string targetFolder = Path.Combine(_tempTestDir, "TargetWorkDir");
        Directory.CreateDirectory(targetFolder);

        // Create a temporary/backup file inside targetFolder
        string testTmpFile = Path.Combine(targetFolder, "document_draft.tmp");
        await File.WriteAllTextAsync(testTmpFile, "Temporary draft content for testing");

        // Create another temporary file outside targetFolder
        string outsideFolder = Path.Combine(_tempTestDir, "OutsideDir");
        Directory.CreateDirectory(outsideFolder);
        string outsideTmpFile = Path.Combine(outsideFolder, "outside.tmp");
        await File.WriteAllTextAsync(outsideTmpFile, "Should not be found in scoped scan");

        string drive = Path.GetPathRoot(targetFolder)!;
        var options = new ScanOptions
        {
            TargetDrive = drive,
            Mode = ScanMode.QuickUndelete,
            TargetFolderPath = targetFolder
        };

        var results = await service.ExecuteScanAsync(options, optionalDiskReader: null);

        Assert.NotNull(results);
        // The file inside targetFolder should be discovered
        Assert.Contains(results, f => f.OriginalPath.Equals(testTmpFile, StringComparison.OrdinalIgnoreCase));
        // The file outside targetFolder should NOT be included
        Assert.DoesNotContain(results, f => f.OriginalPath.Equals(outsideTmpFile, StringComparison.OrdinalIgnoreCase));
    }
}
