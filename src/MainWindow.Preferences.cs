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
              SetRememberDevices(true);
            }
            else
            {
              SetRememberDevices(false);
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
        var path = SettingsFilePath;
        if (File.Exists(path))
        {
          var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
          if (loaded != null)
          {
            // Copy over all fields into the existing _settings instance
            _settings.VmrSingleStream = loaded.VmrSingleStream;
            _settings.MinimalBuffering = loaded.MinimalBuffering;
            _settings.NoGraphClock = loaded.NoGraphClock;
            _settings.HighPriority = loaded.HighPriority;
            _settings.OneMsTimer = loaded.OneMsTimer;
            _settings.LowLatencyGC = loaded.LowLatencyGC;
            _settings.PreventSleepWhileStreaming = loaded.PreventSleepWhileStreaming;
            _settings.RememberDevices = loaded.RememberDevices;
            _settings.StatsOverlay = loaded.StatsOverlay;
            _settings.StatsPosition = loaded.StatsPosition;
            _settings.PreferredWidth = loaded.PreferredWidth;
            _settings.PreferredHeight = loaded.PreferredHeight;
            _settings.PreferredFps = loaded.PreferredFps;
            _settings.VideoDevicePath = loaded.VideoDevicePath;
            _settings.AudioDevicePath = loaded.AudioDevicePath;
            _settings.IsMuted = loaded.IsMuted;
          }
        }
      }
      catch { /* ignore, keep defaults */ }
    }

    public void SavePrefs()
    {
      try
      {
        // Ensure device paths reflect current selection when remembering devices
        if (_settings.RememberDevices)
        {
          _settings.VideoDevicePath = GetSelectedVideoDevicePath();
          _settings.AudioDevicePath = GetSelectedAudioDevicePath();
        }
        else
        {
          _settings.VideoDevicePath = null;
          _settings.AudioDevicePath = null;
        }

        var path = SettingsFilePath;
        var dir = System.IO.Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var json = System.Text.Json.JsonSerializer.Serialize(_settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
      }
      catch { }
    }

    // Toggle RememberDevices and immediately persist device paths (or clear them)
    public void SetRememberDevices(bool remember)
    {
      _settings.RememberDevices = remember;
      SavePrefs();
    }

    // Save only device preferences when RememberDevices is enabled
    public void SaveDevicePreferences()
    {
      if (!_settings.RememberDevices) return;
      _settings.VideoDevicePath = GetSelectedVideoDevicePath();
      _settings.AudioDevicePath = GetSelectedAudioDevicePath();
      SavePrefs();
    }
  }
}
