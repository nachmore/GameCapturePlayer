using System;
using System.Windows;
using DirectShowLib;

namespace GameCapturePlayer
{
    public partial class MainWindow : Window
    {
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
        }

        private void StatsTimer_Tick(object? sender, EventArgs e)
        {
            if (_overlayLabel == null || _panel == null) return;

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
    }
}
