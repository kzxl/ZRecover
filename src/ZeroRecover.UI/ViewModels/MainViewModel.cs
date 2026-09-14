using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ZeroRecover.Core;
using ZeroRecover.Core.Disk;
using ZeroRecover.Core.Models;
using ZeroRecover.Core.Safety;

namespace ZeroRecover.UI.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly RecoveryService _recoveryService = new();
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<DriveVolumeInfo> Drives { get; } = [];
    public ObservableCollection<RecoverableFile> AllFiles { get; } = [];
    public ObservableCollection<RecoverableFile> DisplayFiles { get; } = [];

    private DriveVolumeInfo? _selectedDrive;
    public DriveVolumeInfo? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (_selectedDrive != value)
            {
                _selectedDrive = value;
                OnPropertyChanged();
                ValidateDestination();
            }
        }
    }

    private ScanMode _currentMode = ScanMode.QuickUndelete;
    public ScanMode CurrentMode
    {
        get => _currentMode;
        set { _currentMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentModeTitle)); }
    }

    public string CurrentModeTitle => CurrentMode switch
    {
        ScanMode.QuickUndelete => "🔍 Quick Undelete (NTFS / FAT)",
        ScanMode.DeepCarve => "🧬 Deep Raw Carving",
        ScanMode.VssSnapshot => "🛡️ Shadow Explorer (VSS)",
        ScanMode.RecycleBin => "🗑️ Recycle Rescue",
        _ => "Data Recovery"
    };

    private FileCategory _selectedCategory = FileCategory.All;
    public FileCategory SelectedCategory
    {
        get => _selectedCategory;
        set { _selectedCategory = value; OnPropertyChanged(); ApplyFilter(); }
    }

    private string _searchFilter = string.Empty;
    public string SearchFilter
    {
        get => _searchFilter;
        set { _searchFilter = value; OnPropertyChanged(); ApplyFilter(); }
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set { _isScanning = value; OnPropertyChanged(); }
    }

    private double _scanProgress;
    public double ScanProgress
    {
        get => _scanProgress;
        set { _scanProgress = value; OnPropertyChanged(); }
    }

    private string _statusText = "Ready to scan. Select a drive and mode.";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private string _throughputText = string.Empty;
    public string ThroughputText
    {
        get => _throughputText;
        set { _throughputText = value; OnPropertyChanged(); }
    }

    private string _destinationPath = string.Empty;
    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (_destinationPath != value)
            {
                _destinationPath = value;
                OnPropertyChanged();
                ValidateDestination();
            }
        }
    }

    private bool _isDestinationSafe;
    public bool IsDestinationSafe
    {
        get => _isDestinationSafe;
        set { _isDestinationSafe = value; OnPropertyChanged(); }
    }

    private string _safetyMessage = "Select an external drive or secondary partition as recovery destination.";
    public string SafetyMessage
    {
        get => _safetyMessage;
        set { _safetyMessage = value; OnPropertyChanged(); }
    }

    public ICommand StartScanCommand { get; }
    public ICommand CancelScanCommand { get; }
    public ICommand RestoreSelectedCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }

    public MainViewModel()
    {
        StartScanCommand = new RelayCommand(async _ => await ExecuteScanAsync(), _ => !IsScanning && SelectedDrive != null);
        CancelScanCommand = new RelayCommand(_ => CancelScan(), _ => IsScanning);
        RestoreSelectedCommand = new RelayCommand(async _ => await ExecuteRestoreAsync(), _ => !IsScanning && IsDestinationSafe && DisplayFiles.Any(f => f.IsSelected));
        SelectAllCommand = new RelayCommand(_ => SetAllSelected(true));
        DeselectAllCommand = new RelayCommand(_ => SetAllSelected(false));

        LoadDrives();
    }

    private void LoadDrives()
    {
        Drives.Clear();
        var detected = VolumeEnumerator.EnumerateDrives();
        foreach (var d in detected)
        {
            Drives.Add(d);
        }

        if (Drives.Count > 0)
        {
            SelectedDrive = Drives[0];
            // Propose a default safe destination if a secondary drive exists
            if (Drives.Count > 1)
            {
                DestinationPath = Path.Combine(Drives[1].DriveLetter + "\\", "ZeroRecover_Restored");
            }
            else
            {
                DestinationPath = @"D:\ZeroRecover_Restored";
            }
        }
    }

    private void ValidateDestination()
    {
        if (SelectedDrive == null || string.IsNullOrWhiteSpace(DestinationPath))
        {
            IsDestinationSafe = false;
            SafetyMessage = "Destination path must be specified.";
            return;
        }

        try
        {
            SafetyBarrier.ValidateDestination(SelectedDrive.DriveLetter, DestinationPath);
            IsDestinationSafe = true;
            SafetyMessage = "🛡️ Zero-Write Safety Active: Destination is on a distinct drive and safe to use.";
        }
        catch (Exception ex)
        {
            IsDestinationSafe = false;
            SafetyMessage = ex.Message;
        }
    }

    private async Task ExecuteScanAsync()
    {
        if (SelectedDrive == null) return;

        IsScanning = true;
        ScanProgress = 0;
        ThroughputText = string.Empty;
        StatusText = $"Scanning {SelectedDrive.DriveLetter} ({CurrentMode})...";
        AllFiles.Clear();
        DisplayFiles.Clear();

        _scanCts = new CancellationTokenSource();
        var options = new ScanOptions
        {
            TargetDrive = SelectedDrive.DriveLetter,
            Mode = CurrentMode,
            FilterCategory = SelectedCategory,
            SearchQuery = SearchFilter
        };

        var progress = new Progress<ScanProgressReport>(report =>
        {
            ScanProgress = report.Percent;
            ThroughputText = $"{report.MegaBytesPerSecond:F1} MB/s | {report.FilesFound} items";
            StatusText = report.CurrentOperation;
        });

        try
        {
            RawDiskReader? diskReader = null;
            if (CurrentMode is ScanMode.DeepCarve or ScanMode.QuickUndelete)
            {
                try
                {
                    diskReader = new RawDiskReader(SelectedDrive.DevicePath);
                }
                catch (Exception ex)
                {
                    StatusText = $"[Notice] Direct raw volume access requires Administrator privileges ({ex.Message}). Scanning standard paths.";
                }
            }

            using (diskReader)
            {
                var files = await _recoveryService.ExecuteScanAsync(options, diskReader, progress, _scanCts.Token);
                foreach (var f in files)
                {
                    AllFiles.Add(f);
                }
            }

            ApplyFilter();
            StatusText = $"Scan complete! Found {AllFiles.Count} recoverable files.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled by user.";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void CancelScan()
    {
        _scanCts?.Cancel();
    }

    private async Task ExecuteRestoreAsync()
    {
        if (SelectedDrive == null || !IsDestinationSafe || string.IsNullOrEmpty(DestinationPath))
            return;

        var selected = DisplayFiles.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        IsScanning = true;
        StatusText = $"Restoring {selected.Count} files to '{DestinationPath}'...";

        try
        {
            int successCount = 0;
            foreach (var file in selected)
            {
                await _recoveryService.RestoreFileAsync(file, DestinationPath);
                successCount++;
                StatusText = $"Restored {successCount}/{selected.Count} files...";
            }

            StatusText = $"Restoration complete! {successCount} files saved to {DestinationPath}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Restore error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void ApplyFilter()
    {
        DisplayFiles.Clear();
        foreach (var file in AllFiles)
        {
            if (SelectedCategory != FileCategory.All && file.Category != SelectedCategory)
                continue;

            if (!string.IsNullOrWhiteSpace(SearchFilter) &&
                !file.FileName.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            DisplayFiles.Add(file);
        }
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var f in DisplayFiles)
        {
            f.IsSelected = selected;
        }
        OnPropertyChanged(nameof(DisplayFiles));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public class RelayCommand : ICommand
{
    private readonly Func<object?, Task>? _asyncExecute;
    private readonly Action<object?>? _syncExecute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _syncExecute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        _asyncExecute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public async void Execute(object? parameter)
    {
        if (_asyncExecute != null)
            await _asyncExecute(parameter);
        else
            _syncExecute?.Invoke(parameter);
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
