using System.IO;
using ZeroRecover.Core.Carving;
using ZeroRecover.Core.Disk;
using ZeroRecover.Core.FileSystems.Ntfs;
using ZeroRecover.Core.FileSystems.RecycleBin;
using ZeroRecover.Core.Models;
using ZeroRecover.Core.Safety;
using ZeroRecover.Core.Vss;

namespace ZeroRecover.Core;

/// <summary>
/// Sovereign recovery orchestrator managing scan sessions and file restoration.
/// </summary>
public sealed class RecoveryService
{
    private readonly DeepCarverEngine _carverEngine = new();

    /// <summary>
    /// Executes a scan according to the provided options.
    /// </summary>
    public async Task<List<RecoverableFile>> ExecuteScanAsync(
        ScanOptions options,
        RawDiskReader? optionalDiskReader = null,
        IProgress<ScanProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        switch (options.Mode)
        {
            case ScanMode.RecycleBin:
                return await Task.Run(() =>
                {
                    var recs = RecycleBinExtractor.ScanDrive(options.TargetDrive);
                    var files = recs.Select(r => RecycleBinExtractor.ToRecoverableFile(r, options.TargetDrive)).ToList();
                    return FilterResults(files, options);
                }, cancellationToken);

            case ScanMode.VssSnapshot:
                return await Task.Run(() =>
                {
                    var snapshots = VssSnapshotExplorer.EnumerateSnapshots(options.TargetDrive);
                    var files = new List<RecoverableFile>();
                    foreach (var s in snapshots)
                    {
                        files.Add(new RecoverableFile
                        {
                            FileName = $"VSS_Snapshot_{s.CreationTime:yyyyMMdd_HHmmss}",
                            OriginalPath = s.DeviceObject,
                            Size = 0,
                            Extension = ".vss",
                            Category = FileCategory.Other,
                            Health = RecoveryHealth.Excellent,
                            RecoveryMethod = "VSS_SNAPSHOT",
                            SourceDrive = options.TargetDrive,
                            CreatedTime = s.CreationTime
                        });
                    }
                    return files;
                }, cancellationToken);

            case ScanMode.DeepCarve:
                if (optionalDiskReader != null)
                {
                    long totalBytes = 500 * 1024 * 1024; // Default 500MB window or stream limit
                    var carved = await _carverEngine.CarveAsync(optionalDiskReader, 0, totalBytes, options, progress, cancellationToken);
                    return FilterResults(carved, options);
                }
                break;

            case ScanMode.QuickUndelete:
                if (optionalDiskReader != null)
                {
                    return await Task.Run(() =>
                    {
                        var files = new List<RecoverableFile>();
                        try
                        {
                            var volume = new NtfsVolume(optionalDiskReader);
                            // Scan first 10,000 MFT records for deleted items
                            for (int i = 0; i < 10000; i++)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (volume.TryReadMftRecord(i, out var mft) && mft != null)
                                {
                                    if (mft.IsDeleted && !mft.IsDirectory && !string.IsNullOrEmpty(mft.PrimaryFileName))
                                    {
                                        var defData = mft.DefaultData;
                                        string ext = Path.GetExtension(mft.PrimaryFileName).ToLowerInvariant();
                                        long size = defData?.RealSize ?? 0;

                                        files.Add(new RecoverableFile
                                        {
                                            FileName = mft.PrimaryFileName,
                                            OriginalPath = $@"{options.TargetDrive}\{mft.PrimaryFileName}",
                                            Size = size,
                                            Extension = ext,
                                            Category = ClassifyExtension(ext),
                                            CreatedTime = mft.CreatedTime,
                                            ModifiedTime = mft.ModifiedTime,
                                            Health = RecoveryHealth.Excellent,
                                            RecoveryMethod = "NTFS_MFT",
                                            SourceDrive = options.TargetDrive,
                                            IsResident = defData?.IsResident ?? false,
                                            ResidentData = defData?.ResidentBytes,
                                            DataExtents = defData?.DataRuns.Select(r => r.ToExtent()).ToList() ?? []
                                        });
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Fallback if not an NTFS formatted volume
                        }

                        return FilterResults(files, options);
                    }, cancellationToken);
                }
                break;
        }

        return [];
    }

    /// <summary>
    /// Restores a single recoverable candidate to the destination directory.
    /// Strictly verifies the Zero-Write Safety Barrier before writing a single byte.
    /// </summary>
    public async Task<string> RestoreFileAsync(
        RecoverableFile file,
        string destinationDirectory,
        RawDiskReader? optionalDiskReader = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Enforce Zero-Write Safety Barrier
        SafetyBarrier.ValidateDestination(file.SourceDrive, destinationDirectory);

        Directory.CreateDirectory(destinationDirectory);
        string targetFilePath = Path.Combine(destinationDirectory, file.FileName);

        // Ensure unique filename if already exists
        int counter = 1;
        string baseName = Path.GetFileNameWithoutExtension(file.FileName);
        string ext = Path.GetExtension(file.FileName);
        while (File.Exists(targetFilePath))
        {
            targetFilePath = Path.Combine(destinationDirectory, $"{baseName}_{counter++}{ext}");
        }

        // 2. Execute restoration based on candidate type
        if (file.IsResident && file.ResidentData != null)
        {
            await File.WriteAllBytesAsync(targetFilePath, file.ResidentData, cancellationToken);
        }
        else if (file.RecoveryMethod == "RECYCLE_BIN")
        {
            // Copy from $R data file
            string dir = Path.GetDirectoryName(file.OriginalPath) ?? string.Empty;
            // The OriginalPath holds the original path, but if we have RecycleIndexRecord it maps to $R
            // If direct byte copy is needed:
            if (File.Exists(file.OriginalPath))
            {
                File.Copy(file.OriginalPath, targetFilePath, overwrite: true);
            }
        }
        else if (file.RecoveryMethod == "DEEP_CARVE" && optionalDiskReader != null && file.Size > 0)
        {
            // Read from raw stream at SourceOffset
            byte[] fileBuffer = new byte[file.Size];
            optionalDiskReader.ReadAligned(file.SourceOffset, fileBuffer, 0, (int)file.Size);
            await File.WriteAllBytesAsync(targetFilePath, fileBuffer, cancellationToken);
        }

        // 3. Restore timestamps if available
        if (File.Exists(targetFilePath))
        {
            if (file.CreatedTime.HasValue && file.CreatedTime.Value > DateTime.MinValue)
                File.SetCreationTime(targetFilePath, file.CreatedTime.Value);
            if (file.ModifiedTime.HasValue && file.ModifiedTime.Value > DateTime.MinValue)
                File.SetLastWriteTime(targetFilePath, file.ModifiedTime.Value);
        }

        return targetFilePath;
    }

    private static List<RecoverableFile> FilterResults(List<RecoverableFile> files, ScanOptions options)
    {
        var query = files.AsEnumerable();

        if (options.FilterCategory != FileCategory.All)
        {
            query = query.Where(f => f.Category == options.FilterCategory);
        }

        if (!string.IsNullOrWhiteSpace(options.SearchQuery))
        {
            query = query.Where(f => f.FileName.Contains(options.SearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        if (options.MinFileSize.HasValue)
        {
            query = query.Where(f => f.Size >= options.MinFileSize.Value);
        }

        if (options.MaxFileSize.HasValue)
        {
            query = query.Where(f => f.Size <= options.MaxFileSize.Value);
        }

        return query.ToList();
    }

    private static FileCategory ClassifyExtension(string ext)
    {
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => FileCategory.Pictures,
            ".doc" or ".docx" or ".pdf" or ".xls" or ".xlsx" or ".txt" or ".ppt" or ".pptx" => FileCategory.Documents,
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => FileCategory.Videos,
            ".mp3" or ".wav" or ".flac" or ".m4a" => FileCategory.Audio,
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => FileCategory.Archives,
            ".sqlite" or ".db" or ".mdf" => FileCategory.Databases,
            ".exe" or ".dll" or ".msi" => FileCategory.Executable,
            _ => FileCategory.Other
        };
    }
}
