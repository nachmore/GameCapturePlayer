using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using DirectShowLib;

namespace GameCapturePlayer
{
    public partial class MainWindow
    {
        public class VideoFormatInfo
        {
            public int Width { get; init; }
            public int Height { get; init; }
            public double Fps { get; init; }
            public override string ToString() => Fps > 0 ? $"{Width}x{Height} @ {Fps:0.#} fps" : $"{Width}x{Height}";
        }

        public List<VideoFormatInfo> GetAvailableVideoFormats()
        {
            var result = new List<VideoFormatInfo>();
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
            return null;
        }
    }
}
