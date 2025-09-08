using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using DirectShowLib;

namespace GameCapturePlayer
{
    public partial class MainWindow : Window
    {
        private bool IsRunning => _mediaWorker != null && _mediaWorker.IsRunning;
        private Window? _introWindow;
        private IntroOverlay? _introControl;

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

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show intro overlay while we initialize the preview/monitor graphs
                try { ShowIntroOverlay(); } catch { }
                if (_settings.OneMsTimer) ApplyTimerResolution();
                if (_settings.HighPriority) ApplyHighPriority();
                if (_settings.LowLatencyGC) ApplyLowLatencyGC();

                // Gather device paths
                var vidPath = GetSelectedVideoDevicePath();
                var audPath = GetSelectedAudioDevicePath();
                if (string.IsNullOrEmpty(vidPath) || string.IsNullOrEmpty(audPath))
                    throw new InvalidOperationException("No devices selected");

                // Ensure panel handle and initial rect
                IntPtr handle = IntPtr.Zero;
                System.Drawing.Rectangle r = System.Drawing.Rectangle.Empty;
                var host = wfHost as System.Windows.Forms.Integration.WindowsFormsHost;
                if (host?.Child is System.Windows.Forms.Panel panel)
                {
                    _panel = panel;
                    handle = panel.Handle;
                    r = panel.ClientRectangle;
                    try { _panel.Resize += Panel_Resize; } catch { }
                    try { _panel.DoubleClick += Panel_DoubleClick; } catch { }
                }
                else
                {
                    throw new InvalidOperationException("Video panel host not ready");
                }

                var rect = new DsRect(r.Left, r.Top, r.Right, r.Bottom);
                await _mediaWorker.StartAsync(vidPath!, audPath!, handle, rect,
                    _settings.VmrSingleStream, _settings.MinimalBuffering, _settings.NoGraphClock);
                // Clear the temporary startup background once video is running
                try
                {
                    if (_panel != null)
                    {
                        try { _panel.BackgroundImage?.Dispose(); } catch { }
                        _panel.BackgroundImage = null;
                        _panel.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
                        _panel.BackColor = System.Drawing.Color.Black;
                    }
                }
                catch { }
                UpdateUiState(isRunning: true);
                ShowStatus("Running");
                if (_settings.StatsOverlay) ShowStatsOverlay(true);
                try { UpdateSleepInhibit(); } catch { }

                // Keep overlay for a brief minimum to avoid flash, then fade it out
                try { await Task.Delay(600); await FadeOutAndHideIntroOverlayAsync(); } catch { }
            }
            catch (Exception ex)
            {
                ShowStatus($"Start error: {ex.Message}");
                StopAll();
                try { HideIntroOverlayImmediate(); } catch { }
                try { UpdateSleepInhibit(); } catch { }
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            StopAll();
            UpdateUiState(isRunning: false);
            ShowStatus("Stopped");
            try { HideIntroOverlayImmediate(); } catch { }
            try { UpdateSleepInhibit(); } catch { }
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
                    try { this.Cursor = System.Windows.Input.Cursors.None; } catch { }
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
                    try { this.Cursor = System.Windows.Input.Cursors.Arrow; } catch { }
                    try { topBar.Visibility = Visibility.Visible; } catch { }
                    try { HideFullscreenHintOverlay(); } catch { }
                    try { if (_vmr9Windowless != null) { _vmr9Windowless.SetAspectRatioMode(DirectShowLib.VMR9AspectRatioMode.LetterBox); UpdateVideoPosition(); } } catch { }
                    try { if (btnFullscreen != null) btnFullscreen.ToolTip = "Enter fullscreen"; } catch { }
                }
                try { UpdateSleepInhibit(); } catch { }
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

        // Intro overlay helpers (airspace-safe via top-level window)
        private void ShowIntroOverlay()
        {
            try
            {
                HideIntroOverlayImmediate();
                _introControl = new IntroOverlay();
                _introWindow = new Window
                {
                    WindowStyle = System.Windows.WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowInTaskbar = false,
                    Topmost = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Owner = this,
                    ShowActivated = false,
                    Content = _introControl
                };
                _introWindow.Loaded += (s, e) => PositionIntroWindowOverVideoArea();
                _introWindow.Show();
                PositionIntroWindowOverVideoArea();
            }
            catch { }
        }

        private async Task FadeOutAndHideIntroOverlayAsync()
        {
            if (_introWindow == null || _introControl == null) return;
            try { await _introControl.FadeOutAsync(); } catch { }
            try { _introWindow.Close(); } catch { }
            _introWindow = null;
            _introControl = null;
        }

        private void HideIntroOverlayImmediate()
        {
            if (_introWindow == null) return;
            try { _introWindow.Close(); } catch { }
            _introWindow = null;
            _introControl = null;
        }

        private void StartIntroStoryboards()
        {
            try { _introControl?.StartAnimations(); } catch { }
        }

        private void RepositionIntroOverlayWindow()
        {
            PositionIntroWindowOverVideoArea();
        }

        private void PositionIntroWindowOverVideoArea()
        {
            if (_introWindow == null) return;
            try
            {
                // Position and size to cover the video area under the toolbar (wfHost)
                if (wfHost == null) return;
                wfHost.UpdateLayout();
                // Screen pixels -> WPF DIPs (handle DPI scaling)
                var topLeftPx = wfHost.PointToScreen(new System.Windows.Point(0, 0));
                var bottomRightPx = wfHost.PointToScreen(new System.Windows.Point(wfHost.ActualWidth, wfHost.ActualHeight));

                var source = System.Windows.PresentationSource.FromVisual(this);
                var m = source?.CompositionTarget?.TransformFromDevice ?? new System.Windows.Media.Matrix(1, 0, 0, 1, 0, 0);
                var topLeft = m.Transform(topLeftPx);
                var bottomRight = m.Transform(bottomRightPx);
                double width = Math.Max(0, bottomRight.X - topLeft.X);
                double height = Math.Max(0, bottomRight.Y - topLeft.Y);

                _introWindow.Left = topLeft.X;
                _introWindow.Top = topLeft.Y;
                _introWindow.Width = width;
                _introWindow.Height = height;
            }
            catch { }
        }
        
        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(this);
            dlg.Owner = this;
            dlg.ShowDialog();
        }
    }
}
