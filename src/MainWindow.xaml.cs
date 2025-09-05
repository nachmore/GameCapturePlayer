using System;
using System.Linq;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms.Integration;
using DirectShowLib;
using System.Windows.Forms;
using System.Drawing;
using System.Windows.Threading;
using System.IO;
using System.Text.Json;

namespace GameCapturePlayer
{
    public partial class MainWindow : Window
    {
        public enum StatsCorner { TopLeft, TopRight, BottomLeft, BottomRight }

        public class AdvancedSettings
        {
            public bool VmrSingleStream { get; set; } = true;
            public bool MinimalBuffering { get; set; } = true;
            public bool NoGraphClock { get; set; } = true;

            public bool HighPriority { get; set; } = true;
            public bool OneMsTimer { get; set; } = true;
            public bool LowLatencyGC { get; set; } = true;

            public bool StatsOverlay { get; set; } = false;
            public StatsCorner StatsPosition { get; set; } = StatsCorner.TopLeft;

            // Preferred video format (applied on next Start)
            public int PreferredWidth { get; set; } = 0;   // 0 = auto
            public int PreferredHeight { get; set; } = 0;  // 0 = auto
            public double PreferredFps { get; set; } = 0;  // 0 = auto
        }

        // Moved: GetSelectedVideoDevicePath/GetSelectedAudioDevicePath/SelectDevicesByPath (see MainWindow.Preferences.cs)

        
        
        private class DeviceItem
        {
            public string Name { get; init; } = string.Empty;
            public DsDevice Device { get; init; } = null!;
            public override string ToString() => Name;
        }

        // Video graph fields
        private IGraphBuilder? _videoGraph;
        private ICaptureGraphBuilder2? _videoCaptureGraph;
        private IBaseFilter? _videoSource;
        private IBaseFilter? _vmr9;
        private IVMRWindowlessControl9? _vmr9Windowless;
        private IMediaControl? _videoMediaControl;
        private System.Windows.Forms.Panel? _panel;
        private System.Windows.Forms.Label? _overlayLabel;

        // Audio graph fields
        private IGraphBuilder? _audioGraph;
        private ICaptureGraphBuilder2? _audioCaptureGraph;
        private IBaseFilter? _audioSource;
        private IMediaControl? _audioMediaControl;

        // System perf tuning
        private bool _highPrioApplied = false;
        private ProcessPriorityClass _originalPriorityClass = ProcessPriorityClass.Normal;
        private bool _timerPeriodApplied = false;
        private GCLatencyMode _originalGcLatency = GCLatencyMode.Interactive;

        // Advanced settings and stats overlay
        private readonly AdvancedSettings _settings = new AdvancedSettings();
        private readonly DispatcherTimer _statsTimer = new DispatcherTimer();
        private int _prevDropped = 0, _prevNotDropped = 0;
        private DateTime _prevTime = DateTime.UtcNow;

        // Fullscreen state
        private bool _isFullscreen = false;
        private readonly DispatcherTimer _fullscreenHintTimer = new DispatcherTimer();
        private Window? _fullscreenHintWindow;

        // Preferences persistence
        private class PersistedPrefs
        {
            public bool RememberDevices { get; set; }
            public string? VideoDevicePath { get; set; }
            public string? AudioDevicePath { get; set; }
        }
        private PersistedPrefs _prefs = new PersistedPrefs();
        private static string PrefsFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameCapturePlayer", "settings.json");

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            // Capture original priority to restore later
            try { _originalPriorityClass = Process.GetCurrentProcess().PriorityClass; } catch { }
            try { _originalGcLatency = GCSettings.LatencyMode; } catch { }

            _statsTimer.Interval = TimeSpan.FromMilliseconds(500);
            _fullscreenHintTimer.Interval = TimeSpan.FromSeconds(2.5);
            _fullscreenHintTimer.Tick += (s, e) => { try { HideFullscreenHintOverlay(); } catch { } _fullscreenHintTimer.Stop(); };
            _statsTimer.Tick += StatsTimer_Tick;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadDevices();
                LoadPrefs();
                // Choose devices and auto-start according to prefs or prompt
                if (_prefs.RememberDevices)
                {
                    var vid = TryFindDeviceByPath(FilterCategory.VideoInputDevice, _prefs.VideoDevicePath);
                    var aud = TryFindDeviceByPath(FilterCategory.AudioInputDevice, _prefs.AudioDevicePath);
                    if (vid != null) cmbVideo.SelectedItem = vid;
                    if (aud != null) cmbAudio.SelectedItem = aud;
                    if (vid != null && aud != null)
                    {
                        // Start automatically
                        btnStart_Click(this, new RoutedEventArgs());
                    }
                    else
                    {
                        ShowDevicePickerAndMaybeStart();
                    }
                }
                else
                {
                    ShowDevicePickerAndMaybeStart();
                }
                UpdateUiState(isRunning: _videoMediaControl != null);
            }
            catch (Exception ex)
            {
                ShowStatus($"Init error: {ex.Message}");
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            StopAll();
        }

        // Moved: LoadDevices/TryFindDeviceByPath/ShowDevicePickerAndMaybeStart/LoadPrefs/SavePrefs (see MainWindow.Preferences.cs)

        

        

        

        // Methods moved to partial files:
        // - UI: MainWindow.UI.cs
        // - Video: MainWindow.Video.cs
        // - Audio: MainWindow.Audio.cs
        // - System tuning and COM helpers: MainWindow.System.cs
        // - Stats overlay: MainWindow.Stats.cs
        // - Formats and preferred settings: MainWindow.Formats.cs
        // - Preferences and device selection: MainWindow.Preferences.cs
        // - Controller toggles/restart: MainWindow.Controller.cs
    }
}
