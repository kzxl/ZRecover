using System;
using System.Windows;
using ZeroUI.Core.Theme;
using ZeroUI.Wpf.Theme;

namespace ZRecover.UI;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            try { System.IO.File.WriteAllText("crash.log", e.Exception.ToString()); } catch { }
            MessageBox.Show($"Application error: {e.Exception}", "ZRecover Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            // 1. Initialize ZeroUI Standard Theme Engine & Skin Manager
            ZeroSkinManager.ResetToDefaults();
            ZeroThemeEngine.Initialize(this, "obsidian_dark");
            ZeroWpfStyles.ApplyStyles(this);

            // 2. Map and synchronize ZeroUI theme tokens to application resources
            SyncZeroUiTokens();
            ZeroWpfTheme.ThemeChanged += () =>
            {
                Dispatcher.BeginInvoke(new Action(SyncZeroUiTokens));
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize ZeroUI theme: {ex.Message}", "ZRecover Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        base.OnStartup(e);

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
            window.Focus();
        }
        catch (Exception ex)
        {
            try { System.IO.File.WriteAllText("crash.log", ex.ToString()); } catch { }
            MessageBox.Show($"Window initialization error: {ex}", "ZRecover Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void SyncZeroUiTokens()
    {
        Resources["BgDarkBrush"] = ZeroWpfTheme.BgPrimary;
        Resources["BgCardBrush"] = ZeroWpfTheme.BgCard;
        Resources["BgInputBrush"] = ZeroWpfTheme.BgInput;
        Resources["BgHoverBrush"] = ZeroWpfTheme.BgHover;
        Resources["BgActiveBrush"] = ZeroWpfTheme.BgActive;
        Resources["BorderDefaultBrush"] = ZeroWpfTheme.BorderDefault;
        Resources["BorderSubtleBrush"] = ZeroWpfTheme.BorderSubtle;
        Resources["PrimaryAccentBrush"] = ZeroWpfTheme.PrimaryAccent;
        Resources["PrimaryAccentDarkBrush"] = ZeroWpfTheme.PrimaryAccentDark;
        Resources["SecondaryAccentBrush"] = ZeroWpfTheme.SecondaryAccent;
        Resources["TextPrimaryBrush"] = ZeroWpfTheme.TextPrimary;
        Resources["TextSecondaryBrush"] = ZeroWpfTheme.TextSecondary;
        Resources["TextMutedBrush"] = ZeroWpfTheme.TextMuted;
        Resources["DangerAccentBrush"] = ZeroWpfTheme.DangerAccent;
        Resources["SuccessAccentBrush"] = ZeroWpfTheme.SuccessAccent;
        Resources["WarningAccentBrush"] = ZeroWpfTheme.WarningAccent;
    }
}
