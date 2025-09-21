using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DirectShowLib;
using System.Collections.Generic;

namespace GameCapturePlayer
{
    public partial class SettingsWindow : Window
    {
        private readonly MainWindow _main;
        private bool _init;

        private class DeviceItem
        {
            public string Name { get; set; } = string.Empty;
            public DsDevice Device { get; set; } = null!;
        }

        public SettingsWindow(MainWindow main)
        {
            InitializeComponent();
            _main = main;
            Loaded += SettingsWindow_Loaded;
            this.Closing += SettingsWindow_Closing;
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Initialize devices
                var video = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice)
                    .Select(d => new DeviceItem { Name = d.Name, Device = d })
                    .ToList();
                cmbVideo.ItemsSource = video;
                var selVidPath = _main.GetSelectedVideoDevicePath();
                var selVid = !string.IsNullOrEmpty(selVidPath) ? video.FirstOrDefault(v => string.Equals(v.Device.DevicePath, selVidPath, StringComparison.OrdinalIgnoreCase)) : null;
                cmbVideo.SelectedItem = selVid ?? (video.Count > 0 ? video[0] : null);

                var audio = DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice)
                    .Select(d => new DeviceItem { Name = d.Name, Device = d })
                    .ToList();
                cmbAudio.ItemsSource = audio;
                var selAudPath = _main.GetSelectedAudioDevicePath();
                var selAud = !string.IsNullOrEmpty(selAudPath) ? audio.FirstOrDefault(a => string.Equals(a.Device.DevicePath, selAudPath, StringComparison.OrdinalIgnoreCase)) : null;
                cmbAudio.SelectedItem = selAud ?? (audio.Count > 0 ? audio[0] : null);

                // Stats overlay
                var s = _main.GetAppSettings();
                chkShowStats.IsChecked = s.StatsOverlay;
                cmbStatsPos.SelectedIndex = s.StatsPosition switch
                {
                    MainWindow.StatsCorner.TopLeft => 0,
                    MainWindow.StatsCorner.TopRight => 1,
                    MainWindow.StatsCorner.BottomLeft => 2,
                    MainWindow.StatsCorner.BottomRight => 3,
                    _ => 0
                };

                // Remember devices preference (read from settings)
                chkRememberDevices.IsChecked = s.RememberDevices;

                // Advanced toggles
                chkHighPrio.IsChecked = s.HighPriority;
                chkOneMsTimer.IsChecked = s.OneMsTimer;
                chkLowLatencyGC.IsChecked = s.LowLatencyGC;
                chkPreventSleep.IsChecked = s.PreventSleepWhileStreaming;
                chkSingleStream.IsChecked = s.VmrSingleStream;
                chkMinimalBuffering.IsChecked = s.MinimalBuffering;
                chkNoGraphClock.IsChecked = s.NoGraphClock;

                // Formats
                PopulateFormats();

                _init = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Settings init error: {ex}");
            }
        }

        private void PopulateFormats()
        {
            try
            {
                cmbFormats.Items.Clear();
                var auto = new ComboBoxItem { Content = "Auto (device default)", Tag = null };
                cmbFormats.Items.Add(auto);

                var formats = _main.GetAvailableVideoFormats();
                foreach (var f in formats)
                {
                    cmbFormats.Items.Add(new ComboBoxItem
                    {
                        Content = f.ToString(),
                        Tag = f
                    });
                }

                // Select current preference
                var s = _main.GetAppSettings();
                ComboBoxItem? toSelect = auto;
                if (s.PreferredWidth > 0 && s.PreferredHeight > 0)
                {
                    var items = cmbFormats.Items.Cast<object>()
                        .OfType<ComboBoxItem>()
                        .Where(i => i.Tag is MainWindow.VideoFormatInfo)
                        .Select(i => new { Item = i, F = (MainWindow.VideoFormatInfo)i.Tag! })
                        .Where(x => x.F.Width == s.PreferredWidth && x.F.Height == s.PreferredHeight)
                        .ToList();
                    if (items.Count > 0)
                    {
                        toSelect = s.PreferredFps > 0
                            ? items.OrderBy(x => Math.Abs(x.F.Fps - s.PreferredFps)).First().Item
                            : items.OrderByDescending(x => x.F.Fps).First().Item;
                    }
                }
                cmbFormats.SelectedItem = toSelect;
            }
            catch { }
        }

        private void CmbVideo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_init) return;
            if (cmbVideo.SelectedItem is DeviceItem v)
            {
                var changed = _main.SelectDevicesByPath(v.Device.DevicePath, null);
                if (changed)
                {
                    // Refresh formats list for the newly selected video device
                    PopulateFormats();
                    _main.RestartPreviewIfRunning();
                }
            }
            // Save device preferences if RememberDevices is enabled
            if (chkRememberDevices.IsChecked == true)
            {
                _main.SaveDevicePreferences();
            }
        }

        private void CmbAudio_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_init) return;
            if (cmbAudio.SelectedItem is DeviceItem a)
            {
                var changed = _main.SelectDevicesByPath(null, a.Device.DevicePath);
                if (changed)
                {
                    _main.RestartPreviewIfRunning();
                }
            }
            // Save device preferences if RememberDevices is enabled
            if (chkRememberDevices.IsChecked == true)
            {
                _main.SaveDevicePreferences();
            }
        }

        private void ChkRememberDevices_Changed(object sender, RoutedEventArgs e)
        {
            if (!_init) return;
            _main.SetRememberDevices(chkRememberDevices.IsChecked == true);
        }

        private void ChkShowStats_Changed(object sender, RoutedEventArgs e)
        {
            if (!_init) return;
            _main.ShowStatsOverlay(chkShowStats.IsChecked == true);
            _main.SavePrefs();
        }

        private void CmbStatsPos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_init) return;
            if (cmbStatsPos.SelectedItem is ComboBoxItem item && item.Content is string s)
            {
                if (Enum.TryParse<MainWindow.StatsCorner>(s, out var corner))
                {
                    _main.SetStatsPosition(corner);
                    _main.SavePrefs();
                }
            }
        }

        private void ChkHighPrio_Changed(object sender, RoutedEventArgs e)
        {
            if (!_init) return;
            _main.SetHighPriorityEnabled(chkHighPrio.IsChecked == true);
            _main.SavePrefs();
        }

        private void ChkOneMsTimer_Changed(object sender, RoutedEventArgs e)
        {
            if (!_init) return;
            _main.SetOneMsTimerEnabled(chkOneMsTimer.IsChecked == true);
            _main.SavePrefs();
        }

        private void ChkLowLatencyGC_Changed(object sender, RoutedEventArgs e)
        {
            if (!_init) return;
            _main.SetLowLatencyGCEnabled(chkLowLatencyGC.IsChecked == true);
            _main.SavePrefs();
        }

        private void ChkPreventSleep_Changed(object sender, RoutedEventArgs e)
        {
            if (!_init) return;
            _main.SetPreventSleepWhileStreamingEnabled(chkPreventSleep.IsChecked == true);
            _main.SavePrefs();
        }

        private void GraphTweaks_Changed(object sender, RoutedEventArgs e)
        {
            if (!_init) return;
            _main.SetGraphTweaks(
                chkSingleStream.IsChecked == true,
                chkMinimalBuffering.IsChecked == true,
                chkNoGraphClock.IsChecked == true);
            _main.SavePrefs();
        }

        private void CmbFormats_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_init) return;
            if (cmbFormats.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag is MainWindow.VideoFormatInfo fmt)
                {
                    _main.SetPreferredFormat(fmt.Width, fmt.Height, fmt.Fps);
                    _main.RestartPreviewIfRunning();
                }
                else
                {
                    _main.SetPreferredFormat(0, 0, 0); // Auto
                    _main.RestartPreviewIfRunning();
                }
            }
            _main.SavePrefs();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            try { _main.SavePrefs(); } catch { }
            try { this.Close(); } catch { }
        }

        private void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try { _main.SavePrefs(); } catch { }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    DragMove();
                }
            }
            catch { }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                try { _main.SavePrefs(); } catch { }
                try { this.Close(); } catch { }
                e.Handled = true;
            }
        }
    }
}
