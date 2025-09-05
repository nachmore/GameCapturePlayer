using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace GameCapturePlayer
{
    public partial class MainWindow : Window
    {
        private bool IsRunning => _videoMediaControl != null;

        private void btnStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (IsRunning)
            {
                btnStop_Click(sender, e);
            }
            else
            {
                btnStart_Click(sender, e);
            }
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settings.OneMsTimer) ApplyTimerResolution();
                if (_settings.HighPriority) ApplyHighPriority();
                if (_settings.LowLatencyGC) ApplyLowLatencyGC();
                StartVideoPreview();
                StartAudioMonitor();
                UpdateUiState(isRunning: true);
                ShowStatus("Running");
                if (_settings.StatsOverlay) ShowStatsOverlay(true);
            }
            catch (Exception ex)
            {
                ShowStatus($"Start error: {ex.Message}");
                StopAll();
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            StopAll();
            UpdateUiState(isRunning: false);
            ShowStatus("Stopped");
        }

        private void btnFullscreen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isFullscreen)
                {
                    _isFullscreen = true;
                    this.WindowStyle = System.Windows.WindowStyle.None;
                    this.WindowState = System.Windows.WindowState.Maximized;
                    this.Topmost = true;
                    try { topBar.Visibility = Visibility.Collapsed; } catch { }
                    try { ShowFullscreenHintOverlay(); _fullscreenHintTimer.Start(); } catch { }
                    try { if (_vmr9Windowless != null) { _vmr9Windowless.SetAspectRatioMode(DirectShowLib.VMR9AspectRatioMode.LetterBox); UpdateVideoPosition(); } } catch { }
                    try { if (btnFullscreen != null) btnFullscreen.ToolTip = "Exit fullscreen"; } catch { }
                }
                else
                {
                    _isFullscreen = false;
                    this.Topmost = false;
                    this.WindowStyle = System.Windows.WindowStyle.SingleBorderWindow;
                    this.WindowState = System.Windows.WindowState.Normal;
                    try { topBar.Visibility = Visibility.Visible; } catch { }
                    try { HideFullscreenHintOverlay(); } catch { }
                    try { if (_vmr9Windowless != null) { _vmr9Windowless.SetAspectRatioMode(DirectShowLib.VMR9AspectRatioMode.LetterBox); UpdateVideoPosition(); } } catch { }
                    try { if (btnFullscreen != null) btnFullscreen.ToolTip = "Enter fullscreen"; } catch { }
                }
            }
            catch { }
        }

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape && _isFullscreen)
            {
                btnFullscreen_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (e.Key == System.Windows.Input.Key.F11)
            {
                btnFullscreen_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
        }

        private void UpdateUiState(bool isRunning)
        {
            try { cmbVideo.IsEnabled = !isRunning; } catch { }
            try { cmbAudio.IsEnabled = !isRunning; } catch { }
            try { if (playIcon != null) playIcon.Visibility = isRunning ? Visibility.Collapsed : Visibility.Visible; } catch { }
            try { if (stopIcon != null) stopIcon.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed; } catch { }
            try { if (btnStartStop != null) btnStartStop.ToolTip = isRunning ? "Stop" : "Start"; } catch { }
        }

        private void Panel_DoubleClick(object? sender, EventArgs e)
        {
            try { btnFullscreen_Click(this, new RoutedEventArgs()); } catch { }
        }

        private void ShowFullscreenHintOverlay()
        {
            try
            {
                HideFullscreenHintOverlay();
                var border = new System.Windows.Controls.Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(170, 128, 128, 128)),
                    CornerRadius = new System.Windows.CornerRadius(6),
                    Padding = new Thickness(12),
                    Child = new System.Windows.Controls.TextBlock
                    {
                        Text = "Press ESC to exit fullscreen mode",
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 16
                    },
                    IsHitTestVisible = false
                };

                _fullscreenHintWindow = new Window
                {
                    WindowStyle = System.Windows.WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowInTaskbar = false,
                    Topmost = true,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    Owner = this,
                    ShowActivated = false,
                    Content = border
                };

                _fullscreenHintWindow.Loaded += (s, e) => CenterFullscreenHintWindow();
                _fullscreenHintWindow.Show();
            }
            catch { }
        }

        private void HideFullscreenHintOverlay()
        {
            if (_fullscreenHintWindow != null)
            {
                try { _fullscreenHintWindow.Close(); } catch { }
                _fullscreenHintWindow = null;
            }
        }

        private void RepositionFullscreenHintOverlay()
        {
            CenterFullscreenHintWindow();
        }

        private void CenterFullscreenHintWindow()
        {
            if (_fullscreenHintWindow == null) return;
            try
            {
                var w = _fullscreenHintWindow;
                w.UpdateLayout();
                var left = this.Left + (this.ActualWidth - w.ActualWidth) / 2.0;
                var top = this.Top + (this.ActualHeight - w.ActualHeight) / 2.0;
                w.Left = Math.Max(0, left);
                w.Top = Math.Max(0, top);
            }
            catch { }
        }

        private void ShowStatus(string text)
        {
            try { this.Title = $"Game Capture Player — {text}"; } catch { }
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(this);
            dlg.Owner = this;
            dlg.ShowDialog();
        }
    }
}
