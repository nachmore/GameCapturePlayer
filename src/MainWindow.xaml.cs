using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms.Integration;
using DirectShowLib;
using System.Windows.Forms;

namespace GameCapturePlayer
{
    public partial class MainWindow : Window
    {
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

        // Audio graph fields
        private IGraphBuilder? _audioGraph;
        private ICaptureGraphBuilder2? _audioCaptureGraph;
        private IBaseFilter? _audioSource;
        private IMediaControl? _audioMediaControl;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadDevices();
                UpdateUiState(isRunning: false);
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
            if (videoDevices.Count > 0) cmbVideo.SelectedIndex = 0;

            var audioDevices = DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice)
                .Select(d => new DeviceItem { Name = d.Name, Device = d })
                .ToList();
            cmbAudio.ItemsSource = audioDevices;
            if (audioDevices.Count > 0) cmbAudio.SelectedIndex = 0;
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StartVideoPreview();
                StartAudioMonitor();
                UpdateUiState(isRunning: true);
                ShowStatus("Running");
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

            _vmr9Windowless = (IVMRWindowlessControl9)_vmr9;

            // Ensure WindowsFormsHost is created and get handle
            var host = wfHost as WindowsFormsHost;
            if (host?.Child is System.Windows.Forms.Panel panel)
            {
                _panel = panel;
                _panel.Resize += Panel_Resize;
                hr = _vmr9Windowless.SetVideoClippingWindow(panel.Handle);
                DsError.ThrowExceptionForHR(hr);
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

            // Connect preview stream -> VMR9, fallback to capture pin if preview not available
            hr = _videoCaptureGraph.RenderStream(PinCategory.Preview, MediaType.Video, _videoSource, null, _vmr9);
            if (hr < 0)
            {
                hr = _videoCaptureGraph.RenderStream(PinCategory.Capture, MediaType.Video, _videoSource, null, _vmr9);
            }
            DsError.ThrowExceptionForHR(hr);

            _videoMediaControl = (IMediaControl)_videoGraph;
            hr = _videoMediaControl.Run();
            DsError.ThrowExceptionForHR(hr);
        }

        private void Panel_Resize(object? sender, EventArgs e)
        {
            try { UpdateVideoPosition(); } catch { }
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

            // Render the capture stream to the default audio renderer (monitoring)
            hr = _audioCaptureGraph.RenderStream(PinCategory.Capture, MediaType.Audio, _audioSource, null, null);
            DsError.ThrowExceptionForHR(hr);

            _audioMediaControl = (IMediaControl)_audioGraph;
            hr = _audioMediaControl.Run();
            DsError.ThrowExceptionForHR(hr);
        }

        private void StopAll()
        {
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

            ReleaseCom(ref _audioSource);
            ReleaseCom(ref _audioCaptureGraph);
            ReleaseCom(ref _audioGraph);
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

        private void UpdateUiState(bool isRunning)
        {
            cmbVideo.IsEnabled = !isRunning;
            cmbAudio.IsEnabled = !isRunning;
            btnStart.IsEnabled = !isRunning;
            btnStop.IsEnabled = isRunning;
        }

        private void ShowStatus(string text)
        {
            txtStatus.Text = text;
        }
    }
}
