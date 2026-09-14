using System.Buffers.Binary;
using System.IO;
using System.Text;
using ZeroRecover.Core.Models;

namespace ZeroRecover.Core.FileSystems.RecycleBin;

/// <summary>
/// Parsed metadata from a Windows $Recycle.Bin $I index file.
/// </summary>
public sealed class RecycleIndexRecord
{
    public string IndexPath { get; set; } = string.Empty;

    public string DataPath { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public string OriginalFullPath { get; set; } = string.Empty;

    public long OriginalFileSize { get; set; }

    public DateTime DeletionTime { get; set; }

    public bool DataFileExists => File.Exists(DataPath);
}

/// <summary>
/// Sovereign forensic parser for Windows $Recycle.Bin directories across all accessible drives.
/// </summary>
public static class RecycleBinExtractor
{
    public static List<RecycleIndexRecord> ScanDrive(string driveLetter)
    {
        var records = new List<RecycleIndexRecord>();
        string recycleRoot = Path.Combine(driveLetter.TrimEnd('\\') + "\\", "$Recycle.Bin");

        if (!Directory.Exists(recycleRoot))
            return records;

        try
        {
            var sidDirs = Directory.GetDirectories(recycleRoot);
            foreach (var sidDir in sidDirs)
            {
                try
                {
                    var indexFiles = Directory.GetFiles(sidDir, "$I*");
                    foreach (var indexFile in indexFiles)
                    {
                        if (TryParseIndexFile(indexFile, out var record) && record != null)
                        {
                            records.Add(record);
                        }
                    }
                }
                catch
                {
                    // Ignore permissions errors on specific SID folders
                }
            }
        }
        catch
        {
            // Ignore if access denied
        }

        return records;
    }

    public static bool TryParseIndexFile(string indexFilePath, out RecycleIndexRecord? record)
    {
        record = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(indexFilePath);
            if (bytes.Length < 28)
                return false;

            long version = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(0, 8));
            long fileSize = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(8, 8));
            long fileTime = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16, 8));
            DateTime deletedTime = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();

            string originalPath;
            if (version == 2 && bytes.Length >= 28)
            {
                // Windows 10/11 version 2: length prefix at 0x18 (4 bytes) followed by string
                uint charCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24, 4));
                int strByteLen = (int)(charCount * 2);
                if (28 + strByteLen <= bytes.Length)
                {
                    originalPath = Encoding.Unicode.GetString(bytes, 28, strByteLen).TrimEnd('\0');
                }
                else
                {
                    originalPath = Encoding.Unicode.GetString(bytes, 28, bytes.Length - 28).TrimEnd('\0');
                }
            }
            else
            {
                // Windows Vista/7/8 version 1: fixed UTF-16 characters starting at offset 24 (0x18)
                originalPath = Encoding.Unicode.GetString(bytes, 24, bytes.Length - 24).TrimEnd('\0');
            }

            // Derive $R data file path by replacing "$I" with "$R" in filename
            string dir = Path.GetDirectoryName(indexFilePath) ?? string.Empty;
            string fileName = Path.GetFileName(indexFilePath);
            string dataFileName = "$R" + fileName.Substring(2);
            string dataPath = Path.Combine(dir, dataFileName);

            record = new RecycleIndexRecord
            {
                IndexPath = indexFilePath,
                DataPath = dataPath,
                OriginalFullPath = originalPath,
                OriginalFileName = Path.GetFileName(originalPath),
                OriginalFileSize = fileSize,
                DeletionTime = deletedTime
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static RecoverableFile ToRecoverableFile(RecycleIndexRecord rec, string sourceDrive)
    {
        string ext = Path.GetExtension(rec.OriginalFileName).ToLowerInvariant();
        byte[]? preview = null;
        if (rec.DataFileExists)
        {
            try
            {
                using var fs = File.OpenRead(rec.DataPath);
                preview = new byte[Math.Min(256, (int)fs.Length)];
                int r = fs.Read(preview, 0, preview.Length);
                if (r < preview.Length)
                {
                    Array.Resize(ref preview, r);
                }
            }
            catch { }
        }

        return new RecoverableFile
        {
            FileName = rec.OriginalFileName,
            OriginalPath = rec.OriginalFullPath,
            PhysicalPath = rec.DataPath,
            Size = rec.OriginalFileSize,
            Extension = ext,
            Category = ClassifyExtension(ext),
            DeletedTime = rec.DeletionTime,
            Health = rec.DataFileExists ? RecoveryHealth.Excellent : RecoveryHealth.Overwritten,
            RecoveryMethod = "RECYCLE_BIN",
            SourceDrive = sourceDrive,
            SourceOffset = 0,
            PreviewBytes = preview
        };
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
