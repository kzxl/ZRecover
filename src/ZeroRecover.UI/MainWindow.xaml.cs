using System.Windows;
using ZeroRecover.Core.Models;
using ZeroRecover.UI.ViewModels;

namespace ZeroRecover.UI;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void QuickUndelete_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentMode = ScanMode.QuickUndelete;
    }

    private void DeepCarve_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentMode = ScanMode.DeepCarve;
    }

    private void Vss_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentMode = ScanMode.VssSnapshot;
    }

    private void Recycle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentMode = ScanMode.RecycleBin;
    }

    private void FileCheckBox_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.UpdateSelectionStats();
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        dialog.Description = "Select Safe Recovery Destination Folder (MUST NOT be the drive being scanned)";
        dialog.UseDescriptionForTitle = true;
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ViewModel.DestinationPath = dialog.SelectedPath;
        }
    }
}
