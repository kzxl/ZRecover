using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZeroRecover.Core.Models;

/// <summary>
/// Represents a deleted or carved file candidate ready for restoration.
/// </summary>
public sealed class RecoverableFile : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public Guid Id { get; init; } = Guid.NewGuid();

    private string _fileName = string.Empty;
    public string FileName
    {
        get => _fileName;
        set
        {
            if (_fileName != value)
            {
                _fileName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSuggestion));
            }
        }
    }

    public string OriginalPath { get; set; } = string.Empty;

    /// <summary>
    /// Actual physical path on disk where data can currently be read from (e.g. $Recycle.Bin $R file or temp store).
    /// </summary>
    public string? PhysicalPath { get; set; }

    public long Size { get; set; }

    private string _extension = string.Empty;
    public string Extension
    {
        get => _extension;
        set
        {
            if (_extension != value)
            {
                _extension = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSuggestion));
            }
        }
    }

    private FileCategory _category = FileCategory.Other;
    public FileCategory Category
    {
        get => _category;
        set
        {
            if (_category != value)
            {
                _category = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? CreatedTime { get; set; }

    public DateTime? ModifiedTime { get; set; }

    public DateTime? DeletedTime { get; set; }

    public RecoveryHealth Health { get; set; } = RecoveryHealth.Excellent;

    /// <summary>
    /// Engine method used to discover this candidate:
    /// "NTFS_MFT", "FAT_ENTRY", "DEEP_CARVE", "VSS_SNAPSHOT", "RECYCLE_BIN"
    /// </summary>
    public string RecoveryMethod { get; set; } = "UNKNOWN";

    public string SourceDrive { get; set; } = string.Empty;

    public long SourceOffset { get; set; }

    public List<DataRunExtent> DataExtents { get; set; } = [];

    public byte[]? PreviewBytes { get; set; }

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// True if file content is resident directly inside MFT record.
    /// </summary>
    public bool IsResident { get; set; }

    public byte[]? ResidentData { get; set; }

    /// <summary>
    /// Gets formatted human-readable file size string.
    /// </summary>
    public string FormattedSize => FormatBytes(Size);

    /// <summary>
    /// Intelligent suggested extension identified through magic bytes and payload heuristics.
    /// </summary>
    private string? _suggestedExtension;
    public string? SuggestedExtension
    {
        get => _suggestedExtension;
        set
        {
            if (_suggestedExtension != value)
            {
                _suggestedExtension = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSuggestion));
            }
        }
    }

    /// <summary>
    /// Intelligent suggested file name synthesized from payload contents (JSON keys, doc titles, headers).
    /// </summary>
    private string? _suggestedFileName;
    public string? SuggestedFileName
    {
        get => _suggestedFileName;
        set
        {
            if (_suggestedFileName != value)
            {
                _suggestedFileName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSuggestion));
            }
        }
    }

    /// <summary>
    /// Human-readable format description (e.g. "JSON Data Object", "Word Document (DOCX)").
    /// </summary>
    private string? _detectedFormat;
    public string? DetectedFormat
    {
        get => _detectedFormat;
        set
        {
            if (_detectedFormat != value)
            {
                _detectedFormat = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Decoded/formatted text preview if content is text, JSON, XML, or source code.
    /// </summary>
    private string? _previewText;
    public string? PreviewText
    {
        get => _previewText;
        set
        {
            if (_previewText != value)
            {
                _previewText = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Returns true if an intelligent extension or file name recommendation is available.
    /// </summary>
    public bool HasSuggestion =>
        (!string.IsNullOrEmpty(SuggestedExtension) && !string.Equals(Extension, SuggestedExtension, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrEmpty(SuggestedFileName) && !string.Equals(FileName, SuggestedFileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Applies the smart suggested file name and extension to this recoverable candidate.
    /// </summary>
    public void ApplySuggestedName()
    {
        if (!string.IsNullOrEmpty(SuggestedFileName))
        {
            FileName = SuggestedFileName;
            if (!string.IsNullOrEmpty(SuggestedExtension))
            {
                Extension = SuggestedExtension;
            }
        }
        else if (!string.IsNullOrEmpty(SuggestedExtension) && !string.Equals(Extension, SuggestedExtension, StringComparison.OrdinalIgnoreCase))
        {
            string baseName = Path.GetFileNameWithoutExtension(FileName);
            FileName = $"{baseName}{SuggestedExtension}";
            Extension = SuggestedExtension;
        }
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(Extension));
        OnPropertyChanged(nameof(HasSuggestion));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}

/// <summary>
/// A contiguous segment of clusters or bytes on disk.
/// </summary>
public readonly record struct DataRunExtent(long StartLcn, long ClusterCount);
