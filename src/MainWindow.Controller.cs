using System;

namespace GameCapturePlayer
{
    public partial class MainWindow
    {
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

        public AdvancedSettings GetSettings() => _settings;
    }
}
