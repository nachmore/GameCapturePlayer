using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using DirectShowLib;

namespace GameCapturePlayer
{
    public partial class MainWindow
    {
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

        private void StopAll()
        {
            ShowStatsOverlay(false);
            StopGraph(ref _videoMediaControl);
            StopGraph(ref _audioMediaControl);

            if (_panel != null)
            {
                try { _panel.Resize -= Panel_Resize; } catch { }
                try { _panel.DoubleClick -= Panel_DoubleClick; } catch { }
                _panel = null;
            }

            HideFullscreenHintOverlay();
            try { HideIntroOverlayImmediate(); } catch { }
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
    }
}
