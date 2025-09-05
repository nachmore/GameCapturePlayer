using System;
using DirectShowLib;

namespace GameCapturePlayer
{
    public partial class MainWindow
    {
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

            if (_settings.MinimalBuffering)
            {
                try { RequestLowLatencyOnSource(_audioSource!); } catch { /* best-effort */ }
            }

            hr = _audioCaptureGraph.RenderStream(PinCategory.Capture, MediaType.Audio, _audioSource, null, null);
            DsError.ThrowExceptionForHR(hr);

            _audioMediaControl = (IMediaControl)_audioGraph;
            hr = _audioMediaControl.Run();
            DsError.ThrowExceptionForHR(hr);
        }
    }
}
