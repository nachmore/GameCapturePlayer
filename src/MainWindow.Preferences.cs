using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using DirectShowLib;

namespace GameCapturePlayer
{
    public partial class MainWindow : Window
    {
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
      Debug.WriteLine("ShowDevicePickerAndMaybeStart");

            try
            {
                bool prevEnabled = this.IsEnabled;
                this.IsEnabled = false; // ensure main window cannot be interacted with
                try
                {
                    var dlg = new DeviceSelectWindow();
                    dlg.Owner = this;
                    if (dlg.ShowDialog() == true)
                    {
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

                        btnStart_Click(this, new RoutedEventArgs());
                    }
                }
                finally
                {
                    this.IsEnabled = prevEnabled;
                    try { this.Activate(); } catch { }
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
                    var json = System.Text.Json.JsonSerializer.Deserialize<PersistedPrefs>(File.ReadAllText(path));
                    if (json != null) _prefs = json;
                }
            }
            catch { _prefs = new PersistedPrefs(); }
        }

        private void SavePrefs()
        {
            try
            {
                var path = PrefsFilePath;
                var dir = System.IO.Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = System.Text.Json.JsonSerializer.Serialize(_prefs, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
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
    }
}
