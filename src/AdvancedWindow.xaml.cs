using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GameCapturePlayer
{
    public partial class AdvancedWindow : Window
    {
        private readonly MainWindow _main;
        private bool _init;

        public AdvancedWindow(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            // Initialize UI from current settings
            var s = _main.GetSettings();
            chkHighPrio.IsChecked = s.HighPriority;
            chkOneMsTimer.IsChecked = s.OneMsTimer;
            chkLowLatencyGC.IsChecked = s.LowLatencyGC;

            chkSingleStream.IsChecked = s.VmrSingleStream;
            chkMinimalBuffering.IsChecked = s.MinimalBuffering;
            chkNoGraphClock.IsChecked = s.NoGraphClock;

            chkShowStats.IsChecked = s.StatsOverlay;

            // Set position combo
            cmbStatsPos.SelectedIndex = s.StatsPosition switch
            {
                MainWindow.StatsCorner.TopLeft => 0,
                MainWindow.StatsCorner.TopRight => 1,
                MainWindow.StatsCorner.BottomLeft => 2,
                MainWindow.StatsCorner.BottomRight => 3,
                _ => 0
            };

            // Populate formats
            PopulateFormats();

            _init = true;
        }

        private void ChkHighPrio_Changed(object sender, RoutedEventArgs e)
        {
            if (!_init) return;
            _main.SetHighPriorityEnabled(chkHighPrio.IsChecked == true);
        }

        private void ChkOneMsTimer_Changed(object sender, RoutedEventArgs e)
        {
            if (!_init) return;
            _main.SetOneMsTimerEnabled(chkOneMsTimer.IsChecked == true);
        }

        private void ChkLowLatencyGC_Changed(object sender, RoutedEventArgs e)
        {
            if (!_init) return;
            _main.SetLowLatencyGCEnabled(chkLowLatencyGC.IsChecked == true);
        }

        private void GraphTweaks_Changed(object sender, RoutedEventArgs e)
        {
            if (!_init) return;
            _main.SetGraphTweaks(
                chkSingleStream.IsChecked == true,
                chkMinimalBuffering.IsChecked == true,
                chkNoGraphClock.IsChecked == true);
        }

        private void ChkShowStats_Changed(object sender, RoutedEventArgs e)
        {
            if (!_init) return;
            _main.ShowStatsOverlay(chkShowStats.IsChecked == true);
        }

        private void CmbStatsPos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_init) return;
            if (cmbStatsPos.SelectedItem is ComboBoxItem item && item.Content is string s)
            {
                if (Enum.TryParse<MainWindow.StatsCorner>(s, out var corner))
                {
                    _main.SetStatsPosition(corner);
                }
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
                var s = _main.GetSettings();
                ComboBoxItem? toSelect = auto;
                if (s.PreferredWidth > 0 && s.PreferredHeight > 0)
                {
                    // Select exact match if exists, otherwise nearest fps for matching resolution
                    var items = cmbFormats.Items.Cast<object>()
                        .OfType<ComboBoxItem>()
                        .Where(i => i.Tag is MainWindow.VideoFormatInfo)
                        .Select(i => new { Item = i, F = (MainWindow.VideoFormatInfo)i.Tag! })
                        .Where(x => x.F.Width == s.PreferredWidth && x.F.Height == s.PreferredHeight)
                        .ToList();
                    if (items.Count > 0)
                    {
                        if (s.PreferredFps > 0)
                            toSelect = items.OrderBy(x => Math.Abs(x.F.Fps - s.PreferredFps)).First().Item;
                        else
                            toSelect = items.OrderByDescending(x => x.F.Fps).First().Item;
                    }
                }

                cmbFormats.SelectedItem = toSelect;
            }
            catch { }
        }

        private void CmbFormats_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_init) return;
            if (cmbFormats.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag is MainWindow.VideoFormatInfo fmt)
                {
                    _main.SetPreferredFormat(fmt.Width, fmt.Height, fmt.Fps);
                }
                else
                {
                    _main.SetPreferredFormat(0, 0, 0); // Auto
                }
            }
        }
    }
}
