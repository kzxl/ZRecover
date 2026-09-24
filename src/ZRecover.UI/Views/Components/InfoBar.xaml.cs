using System.Windows.Controls;
using System.Windows.Media;

namespace ZRecover.UI.Views.Components;

public partial class InfoBar : UserControl
{
    public InfoBar()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        UpdateSeverityVisuals();
    }

    public void UpdateSeverityVisuals()
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            switch (vm.InfoBarSeverity)
            {
                case "Success":
                    SeverityIcon.Text = "✔";
                    SeverityIcon.Foreground = (Brush)FindResource("SuccessAccentBrush");
                    ContainerBorder.BorderBrush = (Brush)FindResource("SuccessAccentBrush");
                    break;
                case "Warning":
                    SeverityIcon.Text = "⚠";
                    SeverityIcon.Foreground = (Brush)FindResource("WarningAccentBrush");
                    ContainerBorder.BorderBrush = (Brush)FindResource("WarningAccentBrush");
                    break;
                case "Error":
                    SeverityIcon.Text = "✖";
                    SeverityIcon.Foreground = (Brush)FindResource("DangerAccentBrush");
                    ContainerBorder.BorderBrush = (Brush)FindResource("DangerAccentBrush");
                    break;
                default:
                    SeverityIcon.Text = "ℹ";
                    SeverityIcon.Foreground = (Brush)FindResource("PrimaryAccentBrush");
                    ContainerBorder.BorderBrush = (Brush)FindResource("BorderDefaultBrush");
                    break;
            }
        }
    }
}
