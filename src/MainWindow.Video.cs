using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using DirectShowLib;

namespace GameCapturePlayer
{
    public partial class MainWindow : Window
    {
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
                _panel.DoubleClick += Panel_DoubleClick;
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
            try { RepositionFullscreenHintOverlay(); } catch { }
        }

        private void UpdateVideoPosition()
        {
            if (_vmr9Windowless == null || _panel == null) return;
            var r = _panel.ClientRectangle;
            var dst = new DsRect(r.Left, r.Top, r.Right, r.Bottom);
            int hr = _vmr9Windowless.SetVideoPosition(null, dst);
            DsError.ThrowExceptionForHR(hr);
        }
    }
}
