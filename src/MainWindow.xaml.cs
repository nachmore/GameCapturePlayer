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

        public string? GetSelectedVideoDevicePath() => (cmbVideo.SelectedItem as DeviceItem)?.Device.DevicePath;
        public string? GetSelectedAudioDevicePath() => (cmbAudio.SelectedItem as DeviceItem)?.Device.DevicePath;

        public bool SelectDevicesByPath(string? videoPath, string? audioPath)
        {
            bool changed = false;
            try
            {
                if (!string.IsNullOrEmpty(videoPath))
                {
                    var v = TryFindDeviceByPath(FilterCategory.VideoInputDevice, videoPath);
                    if (v != null && !Equals(cmbVideo.SelectedItem, v))
                    {
                        cmbVideo.SelectedItem = v; changed = true;
                    }
                }
                if (!string.IsNullOrEmpty(audioPath))
                {
                    var a = TryFindDeviceByPath(FilterCategory.AudioInputDevice, audioPath);
                    if (a != null && !Equals(cmbAudio.SelectedItem, a))
                    {
                        cmbAudio.SelectedItem = a; changed = true;
                    }
                }
            }
            catch { }
            return changed;
        }

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
        public class VideoFormatInfo
        {
            public int Width { get; init; }
            public int Height { get; init; }
            public double Fps { get; init; }
            public override string ToString() => Fps > 0 ? $"{Width}x{Height} @ {Fps:0.#} fps" : $"{Width}x{Height}";
        }
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
            _fullscreenHintTimer.Tick += (s, e) => { try { fullscreenHint.Visibility = Visibility.Collapsed; } catch { } _fullscreenHintTimer.Stop(); };
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

        private void LoadDevices()
        {
            var videoDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice)
                .Select(d => new DeviceItem { Name = d.Name, Device = d })
                .ToList();
            cmbVideo.ItemsSource = videoDevices;
            if (videoDevices.Count > 0 && cmbVideo.SelectedIndex < 0) cmbVideo.SelectedIndex = 0;

            var audioDevices = DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice)
                .Select(d => new DeviceItem { Name = d.Name, Device = d })
                .ToList();
            cmbAudio.ItemsSource = audioDevices;
            if (audioDevices.Count > 0 && cmbAudio.SelectedIndex < 0) cmbAudio.SelectedIndex = 0;
        }

        private DeviceItem? TryFindDeviceByPath(Guid category, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var list = category == FilterCategory.VideoInputDevice ? cmbVideo.ItemsSource as System.Collections.Generic.List<DeviceItem>
                       : cmbAudio.ItemsSource as System.Collections.Generic.List<DeviceItem>;
            if (list == null) return null;
            return list.FirstOrDefault(d => string.Equals(d.Device.DevicePath, path, StringComparison.OrdinalIgnoreCase));
        }

        private void ShowDevicePickerAndMaybeStart()
        {
            try
            {
                var dlg = new DeviceSelectWindow();
                dlg.Owner = this;
                if (dlg.ShowDialog() == true)
                {
                    // Apply selections
                    if (!string.IsNullOrEmpty(dlg.SelectedVideoPath))
                    {
                        var vid = TryFindDeviceByPath(FilterCategory.VideoInputDevice, dlg.SelectedVideoPath);
                        if (vid != null) cmbVideo.SelectedItem = vid;
                    }
                    if (!string.IsNullOrEmpty(dlg.SelectedAudioPath))
                    {
                        var aud = TryFindDeviceByPath(FilterCategory.AudioInputDevice, dlg.SelectedAudioPath);
                        if (aud != null) cmbAudio.SelectedItem = aud;
                    }

                    if (dlg.RememberForNextTime)
                    {
                        _prefs.RememberDevices = true;
                        _prefs.VideoDevicePath = (cmbVideo.SelectedItem as DeviceItem)?.Device.DevicePath;
                        _prefs.AudioDevicePath = (cmbAudio.SelectedItem as DeviceItem)?.Device.DevicePath;
                        SavePrefs();
                    }
                    else
                    {
                        _prefs.RememberDevices = false;
                        _prefs.VideoDevicePath = _prefs.AudioDevicePath = null;
                        SavePrefs();
                    }

                    // Auto-start
                    btnStart_Click(this, new RoutedEventArgs());
                }
            }
            catch { }
        }

        private void LoadPrefs()
        {
            try
            {
                var path = PrefsFilePath;
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var p = JsonSerializer.Deserialize<PersistedPrefs>(json);
                    if (p != null) _prefs = p;
                }
            }
            catch { _prefs = new PersistedPrefs(); }
        }

        private void SavePrefs()
        {
            try
            {
                var path = PrefsFilePath;
                var dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_prefs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
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
                    try { fullscreenHint.Visibility = Visibility.Visible; _fullscreenHintTimer.Start(); } catch { }
                    try { if (_vmr9Windowless != null) { _vmr9Windowless.SetAspectRatioMode(VMR9AspectRatioMode.LetterBox); UpdateVideoPosition(); } } catch { }
                    btnFullscreen.Content = "Exit Fullscreen";
                }
                else
                {
                    _isFullscreen = false;
                    this.Topmost = false;
                    this.WindowStyle = System.Windows.WindowStyle.SingleBorderWindow;
                    this.WindowState = System.Windows.WindowState.Normal;
                    try { topBar.Visibility = Visibility.Visible; } catch { }
                    try { fullscreenHint.Visibility = Visibility.Collapsed; } catch { }
                    try { if (_vmr9Windowless != null) { _vmr9Windowless.SetAspectRatioMode(VMR9AspectRatioMode.LetterBox); UpdateVideoPosition(); } } catch { }
                    btnFullscreen.Content = "Fullscreen";
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
            }
        }

        private void StartVideoPreview()
        {
            if (cmbVideo.SelectedItem is not DeviceItem vidItem)
                throw new InvalidOperationException("No video device selected");

            // Build graph
            _videoGraph = (IGraphBuilder)new FilterGraph();
            _videoCaptureGraph = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
            int hr = _videoCaptureGraph.SetFiltergraph(_videoGraph);
            DsError.ThrowExceptionForHR(hr);

            // Add video source
            hr = ((IFilterGraph2)_videoGraph).AddSourceFilterForMoniker(vidItem.Device.Mon, null, vidItem.Name, out _videoSource);
            DsError.ThrowExceptionForHR(hr);

            // Create VMR9 in windowless mode
            _vmr9 = (IBaseFilter)new VideoMixingRenderer9();
            var vmrConfig = (IVMRFilterConfig9)_vmr9;
            hr = vmrConfig.SetRenderingMode(VMR9Mode.Windowless);
            DsError.ThrowExceptionForHR(hr);
            if (_settings.VmrSingleStream)
            {
                // Single stream helps VMR9 avoid unnecessary mixer overhead
                hr = vmrConfig.SetNumberOfStreams(1);
                DsError.ThrowExceptionForHR(hr);
            }

            _vmr9Windowless = (IVMRWindowlessControl9)_vmr9;

            // Ensure WindowsFormsHost is created and get handle
            var host = wfHost as WindowsFormsHost;
            if (host?.Child is System.Windows.Forms.Panel panel)
            {
                _panel = panel;
                _panel.Resize += Panel_Resize;
                hr = _vmr9Windowless.SetVideoClippingWindow(panel.Handle);
                DsError.ThrowExceptionForHR(hr);
                // Ensure letterboxing uses pure black borders
                try { _vmr9Windowless.SetBorderColor(System.Drawing.ColorTranslator.ToWin32(System.Drawing.Color.Black)); } catch { }
                hr = _vmr9Windowless.SetAspectRatioMode(VMR9AspectRatioMode.LetterBox);
                DsError.ThrowExceptionForHR(hr);
                UpdateVideoPosition();
            }
            else
            {
                throw new InvalidOperationException("Video panel host not ready");
            }

            hr = _videoGraph.AddFilter(_vmr9, "VMR9");
            DsError.ThrowExceptionForHR(hr);

            // Apply preferred format before connecting pins
            try { ApplyPreferredVideoFormat(_videoSource!); } catch { }

            // Hint the capture output/preview pin to use minimal buffering for lower latency
            if (_settings.MinimalBuffering)
            {
                try { RequestLowLatencyOnSource(_videoSource!); } catch { /* best-effort */ }
            }

            // Connect preview stream -> VMR9, fallback to capture pin if preview not available
            hr = _videoCaptureGraph.RenderStream(PinCategory.Preview, MediaType.Video, _videoSource, null, _vmr9);
            if (hr < 0)
            {
                hr = _videoCaptureGraph.RenderStream(PinCategory.Capture, MediaType.Video, _videoSource, null, _vmr9);
            }
            DsError.ThrowExceptionForHR(hr);

            if (_settings.NoGraphClock)
            {
                // Remove the graph reference clock for the video path to avoid renderer scheduling delays
                // This is typical for low-latency preview graphs driven by a push-source capture filter
                try { ((IMediaFilter)_videoGraph).SetSyncSource(null); } catch { /* optional */ }
            }

            _videoMediaControl = (IMediaControl)_videoGraph;
            hr = _videoMediaControl.Run();
            DsError.ThrowExceptionForHR(hr);
        }

        private void Panel_Resize(object? sender, EventArgs e)
        {
            try { UpdateVideoPosition(); } catch { }
            try { RepositionOverlayLabel(); } catch { }
        }

        private void UpdateVideoPosition()
        {
            if (_vmr9Windowless == null || _panel == null) return;
            var r = _panel.ClientRectangle;
            var dst = new DsRect(r.Left, r.Top, r.Right, r.Bottom);
            int hr = _vmr9Windowless.SetVideoPosition(null, dst);
            DsError.ThrowExceptionForHR(hr);
        }

        private void StartAudioMonitor()
        {
            if (cmbAudio.SelectedItem is not DeviceItem audItem)
                throw new InvalidOperationException("No audio device selected");

            _audioGraph = (IGraphBuilder)new FilterGraph();
            _audioCaptureGraph = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
            int hr = _audioCaptureGraph.SetFiltergraph(_audioGraph);
            DsError.ThrowExceptionForHR(hr);

            hr = ((IFilterGraph2)_audioGraph).AddSourceFilterForMoniker(audItem.Device.Mon, null, audItem.Name, out _audioSource);
            DsError.ThrowExceptionForHR(hr);

            // Hint the audio capture pin to minimize buffering for lower monitoring latency
            if (_settings.MinimalBuffering)
            {
                try { RequestLowLatencyOnSource(_audioSource!); } catch { /* best-effort */ }
            }

            // Render the capture stream to the default audio renderer (monitoring)
            hr = _audioCaptureGraph.RenderStream(PinCategory.Capture, MediaType.Audio, _audioSource, null, null);
            DsError.ThrowExceptionForHR(hr);

            _audioMediaControl = (IMediaControl)_audioGraph;
            hr = _audioMediaControl.Run();
            DsError.ThrowExceptionForHR(hr);
        }

        private void StopAll()
        {
            ShowStatsOverlay(false);
            StopGraph(ref _videoMediaControl);
            StopGraph(ref _audioMediaControl);

            if (_panel != null)
            {
                try { _panel.Resize -= Panel_Resize; } catch { }
                _panel = null;
            }

            ReleaseCom(ref _vmr9Windowless);
            ReleaseCom(ref _vmr9);
            ReleaseCom(ref _videoSource);
            ReleaseCom(ref _videoCaptureGraph);
            ReleaseCom(ref _videoGraph);
            _overlayLabel = null;

            ReleaseCom(ref _audioSource);
            ReleaseCom(ref _audioCaptureGraph);
            ReleaseCom(ref _audioGraph);

            RestorePriority();
            RestoreTimerResolution();
            RestoreGC();
        }

        /// <summary>
        /// Best-effort request to reduce buffering on the device's preview/capture pin
        /// to minimize end-to-end latency. This must be called before the pin is connected.
        /// </summary>
        private void RequestLowLatencyOnSource(IBaseFilter source)
        {
            if (source == null) return;

            IPin? pin = null;
            try
            {
                // Prefer the Preview pin; if not present, use the Capture pin
                pin = DsFindPin.ByCategory(source, PinCategory.Preview, 0) ??
                      DsFindPin.ByCategory(source, PinCategory.Capture, 0);

                if (pin is IAMBufferNegotiation bn)
                {
                    // Ask for a very small buffer queue. The allocator will clamp if needed.
                    var props = new AllocatorProperties
                    {
                        cBuffers = 2,   // 1-2 buffers keeps latency down while allowing some jitter
                        cbAlign = 0,
                        cbBuffer = 0,   // let downstream decide based on media type
                        cbPrefix = 0
                    };

                    // Some drivers ignore or fail this call; that's fine.
                    try { _ = bn.SuggestAllocatorProperties(props); } catch { }
                }
            }
            finally
            {
                if (pin != null)
                {
                    try { Marshal.ReleaseComObject(pin); } catch { }
                }
            }
        }

        private static void StopGraph(ref IMediaControl? mediaControl)
        {
            if (mediaControl != null)
            {
                try { mediaControl.Stop(); } catch { }
                Marshal.ReleaseComObject(mediaControl);
                mediaControl = null;
            }
        }

        private static void ReleaseCom<T>(ref T? comObj) where T : class
        {
            if (comObj != null)
            {
                try { Marshal.ReleaseComObject(comObj); } catch { }
                comObj = null;
            }
        }

        private void ApplyHighPriority()
        {
            if (_highPrioApplied) return;
            try
            {
                var proc = Process.GetCurrentProcess();
                _originalPriorityClass = proc.PriorityClass;
                proc.PriorityClass = ProcessPriorityClass.High;
                _highPrioApplied = true;
            }
            catch { }
        }

        private void RestorePriority()
        {
            if (!_highPrioApplied) return;
            try
            {
                Process.GetCurrentProcess().PriorityClass = _originalPriorityClass;
            }
            catch { }
            finally { _highPrioApplied = false; }
        }

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        private void ApplyTimerResolution()
        {
            if (_timerPeriodApplied) return;
            try
            {
                timeBeginPeriod(1);
                _timerPeriodApplied = true;
            }
            catch { }
        }

        private void RestoreTimerResolution()
        {
            if (!_timerPeriodApplied) return;
            try
            {
                timeEndPeriod(1);
            }
            catch { }
            finally { _timerPeriodApplied = false; }
        }

        private void ApplyLowLatencyGC()
        {
            try { GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency; } catch { }
        }

        private void RestoreGC()
        {
            try { GCSettings.LatencyMode = _originalGcLatency; } catch { }
        }

        private void UpdateUiState(bool isRunning)
        {
            try { cmbVideo.IsEnabled = !isRunning; } catch { }
            try { cmbAudio.IsEnabled = !isRunning; } catch { }
            try { if (playIcon != null) playIcon.Visibility = isRunning ? Visibility.Collapsed : Visibility.Visible; } catch { }
            try { if (stopIcon != null) stopIcon.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed; } catch { }
        }

        private void ShowStatus(string text)
        {
            try { this.Title = $"Game Capture Player — {text}"; } catch { }
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(this);
            dlg.Owner = this;
            dlg.Show();
        }

        // Public API used by Advanced window
        public AdvancedSettings GetSettings() => _settings;

        public System.Collections.Generic.List<VideoFormatInfo> GetAvailableVideoFormats()
        {
            var result = new System.Collections.Generic.List<VideoFormatInfo>();
            try
            {
                if (cmbVideo.SelectedItem is not DeviceItem vidItem) return result;
                IGraphBuilder g = (IGraphBuilder)new FilterGraph();
                ICaptureGraphBuilder2 cgb = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
                int hr = cgb.SetFiltergraph(g); DsError.ThrowExceptionForHR(hr);
                hr = ((IFilterGraph2)g).AddSourceFilterForMoniker(vidItem.Device.Mon, null, vidItem.Name, out var src); DsError.ThrowExceptionForHR(hr);

                IAMStreamConfig? sc = GetStreamConfigInterface(cgb, src);
                if (sc != null)
                {
                    int count, size; sc.GetNumberOfCapabilities(out count, out size);
                    IntPtr capsPtr = Marshal.AllocHGlobal(size);
                    try
                    {
                        for (int i = 0; i < count; i++)
                        {
                            AMMediaType mt;
                            sc.GetStreamCaps(i, out mt, capsPtr);
                            try
                            {
                                ExtractVideoFormat(mt, out int w, out int h, out double fps);
                                if (w > 0 && h > 0)
                                {
                                    result.Add(new VideoFormatInfo { Width = w, Height = h, Fps = fps });
                                }
                            }
                            finally
                            {
                                DsUtils.FreeAMMediaType(mt);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(capsPtr);
                        Marshal.ReleaseComObject(sc);
                    }
                }

                Marshal.ReleaseComObject(src);
                Marshal.ReleaseComObject(cgb);
                Marshal.ReleaseComObject(g);
            }
            catch { }
            return result
                .GroupBy(f => new { f.Width, f.Height, f.Fps })
                .Select(g => g.First())
                .OrderByDescending(f => f.Width * f.Height)
                .ThenByDescending(f => f.Fps)
                .ToList();
        }

        public void SetPreferredFormat(int width, int height, double fps)
        {
            _settings.PreferredWidth = width;
            _settings.PreferredHeight = height;
            _settings.PreferredFps = fps;
        }

        public void RestartPreviewIfRunning()
        {
            if (_videoMediaControl == null && _audioMediaControl == null) return;
            bool wasRunning = _videoMediaControl != null;
            try
            {
                StopAll();
                if (wasRunning)
                {
                    if (_settings.OneMsTimer) ApplyTimerResolution();
                    if (_settings.HighPriority) ApplyHighPriority();
                    if (_settings.LowLatencyGC) ApplyLowLatencyGC();
                    StartVideoPreview();
                    StartAudioMonitor();
                    UpdateUiState(isRunning: true);
                    if (_settings.StatsOverlay) ShowStatsOverlay(true);
                }
                else
                {
                    UpdateUiState(isRunning: false);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Restart error: {ex.Message}");
            }
        }

        public void SaveCurrentDevicePreferences()
        {
            try
            {
                _prefs.RememberDevices = true;
                _prefs.VideoDevicePath = (cmbVideo.SelectedItem as DeviceItem)?.Device.DevicePath;
                _prefs.AudioDevicePath = (cmbAudio.SelectedItem as DeviceItem)?.Device.DevicePath;
                SavePrefs();
            }
            catch { }
        }

        public void SetHighPriorityEnabled(bool enabled)
        {
            _settings.HighPriority = enabled;
            if (_videoMediaControl != null)
            {
                if (enabled) ApplyHighPriority(); else RestorePriority();
            }
        }

        public void SetOneMsTimerEnabled(bool enabled)
        {
            _settings.OneMsTimer = enabled;
            if (_videoMediaControl != null)
            {
                if (enabled) ApplyTimerResolution(); else RestoreTimerResolution();
            }
        }

        public void SetLowLatencyGCEnabled(bool enabled)
        {
            _settings.LowLatencyGC = enabled;
            if (_videoMediaControl != null)
            {
                if (enabled) ApplyLowLatencyGC(); else RestoreGC();
            }
        }

        public void SetGraphTweaks(bool singleStream, bool minimalBuffering, bool noGraphClock)
        {
            _settings.VmrSingleStream = singleStream;
            _settings.MinimalBuffering = minimalBuffering;
            _settings.NoGraphClock = noGraphClock;
        }

        public void ShowStatsOverlay(bool show)
        {
            _settings.StatsOverlay = show;
            if (show)
            {
                if (_panel == null) return; // will activate on next start
                if (_overlayLabel == null)
                {
                    _overlayLabel = new System.Windows.Forms.Label
                    {
                        AutoSize = true,
                        ForeColor = System.Drawing.Color.White,
                        BackColor = System.Drawing.Color.Black,
                        Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold),
                        Text = ""
                    };
                    _panel.Controls.Add(_overlayLabel);
                }
                _prevDropped = 0; _prevNotDropped = 0; _prevTime = DateTime.UtcNow;
                _statsTimer.Start();
                RepositionOverlayLabel();
            }
            else
            {
                _statsTimer.Stop();
                if (_overlayLabel != null && _panel != null)
                {
                    try { _panel.Controls.Remove(_overlayLabel); _overlayLabel.Dispose(); } catch { }
                }
                _overlayLabel = null;
            }
        }

        public void SetStatsPosition(StatsCorner corner)
        {
            _settings.StatsPosition = corner;
            // Next tick will reposition
        }

        private void StatsTimer_Tick(object? sender, EventArgs e)
        {
            if (_overlayLabel == null || _panel == null) return;

            // Collect stats
            int dropped = 0, notDropped = 0;
            try
            {
                // First try IQualProp on renderer input pin (more reliable for VMR9)
                IPin? rpin = (_vmr9 != null) ? DsFindPin.ByDirection(_vmr9, PinDirection.Input, 0) : null;
                if (rpin is IQualProp qp)
                {
                    qp.get_FramesDroppedInRenderer(out dropped);
                    qp.get_FramesDrawn(out notDropped); // using FramesDrawn as displayed frames
                }
                else
                {
                    // Fallback to IAMDroppedFrames
                    IAMDroppedFrames? df = null;
                    if (_vmr9 is IAMDroppedFrames dfFilter)
                        df = dfFilter;
                    else if (rpin is IAMDroppedFrames dfPin)
                        df = dfPin;
                    if (df != null)
                    {
                        df.GetNumDropped(out dropped);
                        df.GetNumNotDropped(out notDropped);
                    }
                }
            }
            catch { }

            var now = DateTime.UtcNow;
            double dt = (now - _prevTime).TotalSeconds;
            double fps = 0;
            if (dt > 0 && notDropped >= _prevNotDropped)
            {
                fps = (notDropped - _prevNotDropped) / dt;
            }
            _prevTime = now;
            _prevDropped = dropped;
            _prevNotDropped = notDropped;

            string res = GetCurrentVideoResolution();
            string text = $"Res: {res}  FPS: {(fps > 0 ? fps.ToString("F1") : "n/a")}  Dropped: {dropped}";
            try { _overlayLabel.Text = text; } catch { }
            RepositionOverlayLabel();
        }

        private void RepositionOverlayLabel()
        {
            if (_overlayLabel == null || _panel == null) return;
            var margin = 8;
            var size = _overlayLabel.PreferredSize;
            _overlayLabel.Size = size;
            int x = margin, y = margin;
            switch (_settings.StatsPosition)
            {
                case StatsCorner.TopLeft:
                    x = margin; y = margin; break;
                case StatsCorner.TopRight:
                    x = Math.Max(margin, _panel.ClientSize.Width - size.Width - margin); y = margin; break;
                case StatsCorner.BottomLeft:
                    x = margin; y = Math.Max(margin, _panel.ClientSize.Height - size.Height - margin); break;
                case StatsCorner.BottomRight:
                    x = Math.Max(margin, _panel.ClientSize.Width - size.Width - margin);
                    y = Math.Max(margin, _panel.ClientSize.Height - size.Height - margin);
                    break;
            }
            _overlayLabel.Location = new System.Drawing.Point(x, y);
        }

        private string GetCurrentVideoResolution()
        {
            try
            {
                if (_vmr9Windowless == null) return "n/a";
                int w, h, arx, ary;
                int hr = _vmr9Windowless.GetNativeVideoSize(out w, out h, out arx, out ary);
                DsError.ThrowExceptionForHR(hr);
                if (w > 0 && h > 0)
                    return $"{w}x{h}";
            }
            catch { }
            return "n/a";
        }

        private void ApplyPreferredVideoFormat(IBaseFilter source)
        {
            if (source == null) return;
            if (_settings.PreferredWidth <= 0 || _settings.PreferredHeight <= 0) return; // auto

            IAMStreamConfig? sc = GetStreamConfigInterface(_videoCaptureGraph!, source);
            if (sc == null) return;

            try
            {
                int count, size; sc.GetNumberOfCapabilities(out count, out size);
                IntPtr capsPtr = Marshal.AllocHGlobal(size);
                try
                {
                    AMMediaType? best = null;
                    double bestFpsScore = double.MinValue;
                    for (int i = 0; i < count; i++)
                    {
                        AMMediaType mt;
                        sc.GetStreamCaps(i, out mt, capsPtr);
                        try
                        {
                            ExtractVideoFormat(mt, out int w, out int h, out double fps);
                            if (w == _settings.PreferredWidth && h == _settings.PreferredHeight)
                            {
                                double score;
                                if (_settings.PreferredFps > 0 && fps > 0)
                                    score = -Math.Abs(fps - _settings.PreferredFps);
                                else
                                    score = fps; // prefer higher fps if not specified

                                if (score > bestFpsScore)
                                {
                                    if (best != null) DsUtils.FreeAMMediaType(best);
                                    best = mt; // take ownership
                                    bestFpsScore = score;
                                    mt = new AMMediaType(); // prevent double free
                                }
                            }
                        }
                        finally
                        {
                            DsUtils.FreeAMMediaType(mt);
                        }
                    }

                    if (best != null)
                    {
                        try { sc.SetFormat(best); }
                        finally { DsUtils.FreeAMMediaType(best); }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(capsPtr);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(sc);
            }
        }

        private static void ExtractVideoFormat(AMMediaType mt, out int width, out int height, out double fps)
        {
            width = height = 0; fps = 0;
            try
            {
                if (mt.formatType == FormatType.VideoInfo && mt.formatPtr != IntPtr.Zero)
                {
                    var vih = (VideoInfoHeader)Marshal.PtrToStructure(mt.formatPtr, typeof(VideoInfoHeader))!;
                    width = vih.BmiHeader.Width;
                    height = Math.Abs(vih.BmiHeader.Height);
                    long atpf = vih.AvgTimePerFrame;
                    if (atpf > 0) fps = 10000000.0 / atpf;
                }
                else if (mt.formatType == FormatType.VideoInfo2 && mt.formatPtr != IntPtr.Zero)
                {
                    var vih2 = (VideoInfoHeader2)Marshal.PtrToStructure(mt.formatPtr, typeof(VideoInfoHeader2))!;
                    width = vih2.BmiHeader.Width;
                    height = Math.Abs(vih2.BmiHeader.Height);
                    long atpf = vih2.AvgTimePerFrame;
                    if (atpf > 0) fps = 10000000.0 / atpf;
                }
            }
            catch { }
        }

        private static IAMStreamConfig? GetStreamConfigInterface(ICaptureGraphBuilder2 cgb, IBaseFilter source)
        {
            object o;
            try
            {
                var iid = typeof(IAMStreamConfig).GUID;
                int hr = cgb.FindInterface(PinCategory.Preview, MediaType.Video, source, iid, out o);
                if (hr >= 0 && o is IAMStreamConfig scPrev) return scPrev;
            }
            catch { }
            try
            {
                var iid = typeof(IAMStreamConfig).GUID;
                int hr = cgb.FindInterface(PinCategory.Capture, MediaType.Video, source, iid, out o);
                if (hr >= 0 && o is IAMStreamConfig scCap) return scCap;
            }
            catch { }
            return null;
        }

        private static IAMStreamConfig? GetStreamConfigInterface(IBaseFilter source)
        {
            // Helper for temp graph usage
            return null;
        }
    }
}
