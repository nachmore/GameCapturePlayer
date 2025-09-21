using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DirectShowLib;

namespace GameCapturePlayer
{
    // Runs DirectShow graphs on a dedicated STA thread to keep the UI thread free
    public sealed class MediaGraphWorker : IDisposable
    {
        private Thread? _thread;
        private Dispatcher? _dispatcher;
        private readonly AutoResetEvent _ready = new AutoResetEvent(false);
        private volatile bool _isRunning;

        // Video graph fields
        private IGraphBuilder? _videoGraph;
        private ICaptureGraphBuilder2? _videoCaptureGraph;
        private IBaseFilter? _videoSource;
        private IBaseFilter? _vmr9;
        private IVMRWindowlessControl9? _vmr9Windowless;
        private IMediaControl? _videoMediaControl;

        // Audio graph fields
        private IGraphBuilder? _audioGraph;
        private ICaptureGraphBuilder2? _audioCaptureGraph;
        private IBaseFilter? _audioSource;
        private IMediaControl? _audioMediaControl;

        // Current destination rect (relative to the clipping window)
        private DsRect _dstRect = new DsRect(0, 0, 1, 1);

        public bool IsRunning => _isRunning;

        public MediaGraphWorker()
        {
            EnsureThread();
        }

        private void EnsureThread()
        {
            if (_thread != null && _dispatcher != null) return;
            _thread = new Thread(() =>
            {
                try
                {
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    _ready.Set();
                    Dispatcher.Run();
                }
                catch { }
            });
            _thread.Name = "MediaGraphWorker";
            _thread.IsBackground = true;
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.WaitOne();
        }

        private Task<T> InvokeAsync<T>(Func<T> func)
        {
            if (_dispatcher == null) throw new InvalidOperationException("Worker dispatcher not ready");
            return _dispatcher.InvokeAsync(func).Task;
        }

        private Task InvokeAsync(Action action)
        {
            if (_dispatcher == null) throw new InvalidOperationException("Worker dispatcher not ready");
            return _dispatcher.InvokeAsync(action).Task;
        }

        public Task StartAsync(string videoDevicePath, string audioDevicePath, IntPtr panelHandle, DsRect initialRect, 
            bool vmrSingleStream, bool minimalBuffering, bool noGraphClock)
        {
            EnsureThread();
            return InvokeAsync(() => StartInternal(videoDevicePath, audioDevicePath, panelHandle, initialRect, vmrSingleStream, minimalBuffering, noGraphClock));
        }

        public Task UpdateVideoWindowAsync(DsRect rect)
        {
            return InvokeAsync(() =>
            {
                _dstRect = rect;
                if (_vmr9Windowless != null)
                {
                    try { _vmr9Windowless.SetVideoPosition(null, _dstRect); } catch { }
                }
            });
        }

        // Query renderer stats (dropped and drawn frames) on the worker thread
        public Task<(int dropped, int notDropped)> GetRendererFrameStatsAsync()
        {
            return InvokeAsync(() =>
            {
                int dropped = 0, notDropped = 0;
                try
                {
                    IPin? rpin = (_vmr9 != null) ? DsFindPin.ByDirection(_vmr9, PinDirection.Input, 0) : null;
                    if (rpin is IQualProp qp)
                    {
                        qp.get_FramesDroppedInRenderer(out dropped);
                        qp.get_FramesDrawn(out notDropped);
                    }
                    else
                    {
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
                return (dropped, notDropped);
            });
        }

        // Query the native video size from VMR9 Windowless control on the worker thread
        public Task<(int width, int height)> GetNativeVideoSizeAsync()
        {
            return InvokeAsync(() =>
            {
                int w = 0, h = 0;
                try
                {
                    if (_vmr9Windowless != null)
                    {
                        int arx, ary;
                        int hr = _vmr9Windowless.GetNativeVideoSize(out w, out h, out arx, out ary);
                        // Ignore hr; if w/h are 0 it will be treated as unavailable by caller
                    }
                }
                catch { }
                return (w, h);
            });
        }

        // Query the negotiated nominal FPS (AvgTimePerFrame) from the source pin
        public Task<double> GetNominalFpsAsync()
        {
            return InvokeAsync(() =>
            {
                double fps = 0;
                if (_videoCaptureGraph == null || _videoSource == null) return 0.0;
                object o;
                IAMStreamConfig? sc = null;
                try
                {
                    var iid = typeof(IAMStreamConfig).GUID;
                    int hr = _videoCaptureGraph.FindInterface(PinCategory.Preview, MediaType.Video, _videoSource, iid, out o);
                    if (hr >= 0 && o is IAMStreamConfig scPrev) sc = scPrev;
                }
                catch { }
                if (sc == null)
                {
                    try
                    {
                        var iid = typeof(IAMStreamConfig).GUID;
                        int hr = _videoCaptureGraph.FindInterface(PinCategory.Capture, MediaType.Video, _videoSource, iid, out o);
                        if (hr >= 0 && o is IAMStreamConfig scCap) sc = scCap;
                    }
                    catch { }
                }
                if (sc != null)
                {
                    try
                    {
                        sc.GetFormat(out AMMediaType mt);
                        try
                        {
                            long atpf = 0;
                            if (mt.formatType == FormatType.VideoInfo && mt.formatPtr != IntPtr.Zero)
                            {
                                var vih = (VideoInfoHeader)System.Runtime.InteropServices.Marshal.PtrToStructure(mt.formatPtr, typeof(VideoInfoHeader))!;
                                atpf = vih.AvgTimePerFrame;
                            }
                            else if (mt.formatType == FormatType.VideoInfo2 && mt.formatPtr != IntPtr.Zero)
                            {
                                var vih2 = (VideoInfoHeader2)System.Runtime.InteropServices.Marshal.PtrToStructure(mt.formatPtr, typeof(VideoInfoHeader2))!;
                                atpf = vih2.AvgTimePerFrame;
                            }
                            if (atpf > 0) fps = 10000000.0 / atpf;
                        }
                        finally
                        {
                            DsUtils.FreeAMMediaType(mt);
                        }
                    }
                    catch { }
                    finally
                    {
                        try { System.Runtime.InteropServices.Marshal.ReleaseComObject(sc); } catch { }
                    }
                }
                return fps;
            });
        }

        public Task StopAsync()
        {
            return InvokeAsync(StopInternal);
        }

        private void StartInternal(string videoDevicePath, string audioDevicePath, IntPtr panelHandle, DsRect initialRect,
            bool vmrSingleStream, bool minimalBuffering, bool noGraphClock)
        {
            StopInternal(); // ensure clean state
            _dstRect = initialRect;

            // Build video graph
            _videoGraph = (IGraphBuilder)new FilterGraph();
            _videoCaptureGraph = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
            int hr = _videoCaptureGraph.SetFiltergraph(_videoGraph);
            DsError.ThrowExceptionForHR(hr);

            var vidItem = FindDeviceByPath(FilterCategory.VideoInputDevice, videoDevicePath);
            if (vidItem == null) throw new InvalidOperationException("Video device not found");
            hr = ((IFilterGraph2)_videoGraph).AddSourceFilterForMoniker(vidItem.Mon, null, vidItem.Name, out _videoSource);
            DsError.ThrowExceptionForHR(hr);

            _vmr9 = (IBaseFilter)new VideoMixingRenderer9();
            var vmrConfig = (IVMRFilterConfig9)_vmr9;
            hr = vmrConfig.SetRenderingMode(VMR9Mode.Windowless);
            DsError.ThrowExceptionForHR(hr);
            if (vmrSingleStream)
            {
                hr = vmrConfig.SetNumberOfStreams(1);
                DsError.ThrowExceptionForHR(hr);
            }

            _vmr9Windowless = (IVMRWindowlessControl9)_vmr9;
            // Set the clipping window and AR mode
            hr = _vmr9Windowless.SetVideoClippingWindow(panelHandle);
            DsError.ThrowExceptionForHR(hr);
            try { _vmr9Windowless.SetBorderColor(System.Drawing.ColorTranslator.ToWin32(System.Drawing.Color.Black)); } catch { }
            hr = _vmr9Windowless.SetAspectRatioMode(VMR9AspectRatioMode.LetterBox);
            DsError.ThrowExceptionForHR(hr);
            // Set the target rectangle
            try { _vmr9Windowless.SetVideoPosition(null, _dstRect); } catch { }

            hr = _videoGraph.AddFilter(_vmr9, "VMR9");
            DsError.ThrowExceptionForHR(hr);

            if (minimalBuffering)
            {
                try { RequestLowLatencyOnSource(_videoSource!); } catch { }
            }

            // Connect preview stream -> VMR9, fallback to capture pin if preview not available
            hr = _videoCaptureGraph.RenderStream(PinCategory.Preview, MediaType.Video, _videoSource, null, _vmr9);
            if (hr < 0)
            {
                hr = _videoCaptureGraph.RenderStream(PinCategory.Capture, MediaType.Video, _videoSource, null, _vmr9);
            }
            DsError.ThrowExceptionForHR(hr);

            if (noGraphClock)
            {
                try { ((IMediaFilter)_videoGraph).SetSyncSource(null); } catch { }
            }

            _videoMediaControl = (IMediaControl)_videoGraph;
            hr = _videoMediaControl.Run();
            DsError.ThrowExceptionForHR(hr);

            // Build audio graph
            _audioGraph = (IGraphBuilder)new FilterGraph();
            _audioCaptureGraph = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
            hr = _audioCaptureGraph.SetFiltergraph(_audioGraph);
            DsError.ThrowExceptionForHR(hr);

            var audItem = FindDeviceByPath(FilterCategory.AudioInputDevice, audioDevicePath);
            if (audItem == null) throw new InvalidOperationException("Audio device not found");
            hr = ((IFilterGraph2)_audioGraph).AddSourceFilterForMoniker(audItem.Mon, null, audItem.Name, out _audioSource);
            DsError.ThrowExceptionForHR(hr);

            if (minimalBuffering)
            {
                try { RequestLowLatencyOnSource(_audioSource!); } catch { }
            }

            hr = _audioCaptureGraph.RenderStream(PinCategory.Capture, MediaType.Audio, _audioSource, null, null);
            DsError.ThrowExceptionForHR(hr);

            _audioMediaControl = (IMediaControl)_audioGraph;
            hr = _audioMediaControl.Run();
            DsError.ThrowExceptionForHR(hr);

            _isRunning = true;
        }

        private void StopInternal()
        {
            _isRunning = false;

            if (_videoMediaControl != null) { try { _videoMediaControl.Stop(); } catch { } MarshalRelease(ref _videoMediaControl); }
            if (_audioMediaControl != null) { try { _audioMediaControl.Stop(); } catch { } MarshalRelease(ref _audioMediaControl); }

            MarshalRelease(ref _vmr9Windowless);
            MarshalRelease(ref _vmr9);
            MarshalRelease(ref _videoSource);
            MarshalRelease(ref _videoCaptureGraph);
            MarshalRelease(ref _videoGraph);

            MarshalRelease(ref _audioSource);
            MarshalRelease(ref _audioCaptureGraph);
            MarshalRelease(ref _audioGraph);
        }

        private static void MarshalRelease<T>(ref T? comObj) where T : class
        {
            if (comObj != null)
            {
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(comObj); } catch { }
                comObj = null;
            }
        }

        private static DsDevice? FindDeviceByPath(Guid category, string devicePath)
        {
            try
            {
                return DsDevice.GetDevicesOfCat(category).FirstOrDefault(d => string.Equals(d.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        private void RequestLowLatencyOnSource(IBaseFilter source)
        {
            if (source == null) return;
            IPin? pin = null;
            try
            {
                pin = DsFindPin.ByCategory(source, PinCategory.Preview, 0) ??
                      DsFindPin.ByCategory(source, PinCategory.Capture, 0);
                if (pin is IAMBufferNegotiation bn)
                {
                    var props = new AllocatorProperties
                    {
                        cBuffers = 2,
                        cbAlign = 0,
                        cbBuffer = 0,
                        cbPrefix = 0
                    };
                    try { _ = bn.SuggestAllocatorProperties(props); } catch { }
                }
            }
            finally
            {
                if (pin != null)
                {
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(pin); } catch { }
                }
            }
        }

        public void Dispose()
        {
            try { StopAsync().GetAwaiter().GetResult(); } catch { }
            if (_dispatcher != null)
            {
                try { _dispatcher.InvokeShutdown(); } catch { }
                _dispatcher = null;
            }
            if (_thread != null)
            {
                try { _thread.Join(500); } catch { }
                _thread = null;
            }
        }
    }
}
