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

        public class AppSettings
        {
            public bool VmrSingleStream { get; set; } = true;
            public bool MinimalBuffering { get; set; } = true;
            public bool NoGraphClock { get; set; } = true;

            public bool HighPriority { get; set; } = true;
            public bool OneMsTimer { get; set; } = true;
            public bool LowLatencyGC { get; set; } = true;

            // Prevent system sleep while streaming (off by default)
            public bool PreventSleepWhileStreaming { get; set; } = false;

            // Whether to remember device selection across runs
            public bool RememberDevices { get; set; } = false;

            // Remembered device selections (persisted only when RememberDevices is true)
            public string? VideoDevicePath { get; set; }
            public string? AudioDevicePath { get; set; }

            // Persisted audio mute state
            public bool IsMuted { get; set; } = false;

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

        // Application settings and stats overlay
        private readonly AppSettings _settings = new AppSettings();
        private readonly DispatcherTimer _statsTimer = new DispatcherTimer();
        private int _prevDropped = 0, _prevNotDropped = 0;
        private DateTime _prevTime = DateTime.UtcNow;

        // Fullscreen state
        private bool _isFullscreen = false;
        private readonly DispatcherTimer _fullscreenHintTimer = new DispatcherTimer();
        private Window? _fullscreenHintWindow;

        // Periodic refresh to keep system/display awake while needed
        private readonly DispatcherTimer _sleepInhibitTimer = new DispatcherTimer();

        // Audio mute state (UI-level cache; actual control via MediaGraphWorker)
        private bool _isMuted = false;

        // Settings persistence
        private static string SettingsFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameCapturePlayer", "settings.json");

        // Media graphs on a dedicated STA thread
        private readonly MediaGraphWorker _mediaWorker = new MediaGraphWorker();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            LocationChanged += MainWindow_LocationOrSizeChanged;
            SizeChanged += MainWindow_LocationOrSizeChanged;
            // Capture original priority to restore later
            try { _originalPriorityClass = Process.GetCurrentProcess().PriorityClass; } catch { }
            try { _originalGcLatency = GCSettings.LatencyMode; } catch { }

            _statsTimer.Interval = TimeSpan.FromMilliseconds(500);
            _fullscreenHintTimer.Interval = TimeSpan.FromSeconds(2.5);
            _fullscreenHintTimer.Tick += (s, e) => { try { HideFullscreenHintOverlay(); } catch { } _fullscreenHintTimer.Stop(); };
            _statsTimer.Tick += StatsTimer_Tick;

            // Refresh sleep inhibition every 60 seconds when enabled
            _sleepInhibitTimer.Interval = TimeSpan.FromSeconds(60);
            _sleepInhibitTimer.Tick += (s, e) => { try { UpdateSleepInhibit(); } catch { } };
        }

        private void MainWindow_LocationOrSizeChanged(object? sender, EventArgs e)
        {
            try { CenterFullscreenHintWindow(); } catch { }
            try { PositionIntroWindowOverVideoArea(); } catch { }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show background intro overlay behind dialogs on startup
                try { ShowIntroOverlay(); PositionIntroWindowOverVideoArea(); } catch { }
                // Also paint the WinForms video panel with the purple background and loading image
                try { ApplyStartupBackgroundToPanel(); } catch { }
                LoadDevices();
                LoadPrefs();
                // Initialize mute state and button UI based on persisted setting
                try { _isMuted = _settings.IsMuted; } catch { }
                try { UpdateMuteButtonUi(); } catch { }
                // Choose devices and auto-start according to settings or prompt
                if (_settings.RememberDevices)
                {
                    var vid = TryFindDeviceByPath(FilterCategory.VideoInputDevice, _settings.VideoDevicePath);
                    var aud = TryFindDeviceByPath(FilterCategory.AudioInputDevice, _settings.AudioDevicePath);
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

        private void ApplyStartupBackgroundToPanel()
        {
            try
            {
                // Set purple background
                try { videoPanel.BackColor = System.Drawing.ColorTranslator.FromHtml("#7030a0"); } catch { }

                // Load the embedded WPF resource as a System.Drawing.Image
                var uri = new Uri("pack://application:,,,/img/loading.png", UriKind.Absolute);
                var sri = System.Windows.Application.GetResourceStream(uri);
                if (sri != null)
                {
                    using var s = sri.Stream;
                    using var ms = new System.IO.MemoryStream();
                    s.CopyTo(ms);
                    ms.Position = 0;
                    using var img = System.Drawing.Image.FromStream(ms);
                    // Clone into a new Image so we can dispose the stream
                    var bmp = new System.Drawing.Bitmap(img);
                    try { videoPanel.BackgroundImage?.Dispose(); } catch { }
                    videoPanel.BackgroundImage = bmp;
                    videoPanel.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
                }
            }
            catch { }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try { SavePrefs(); } catch { }
            StopAll();
            try { _mediaWorker.Dispose(); } catch { }
        }
    }
}
