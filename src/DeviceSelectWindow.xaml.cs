using System;
using System.Linq;
using System.Windows;
using DirectShowLib;
using System.Collections.Generic;

namespace GameCapturePlayer
{
    public partial class DeviceSelectWindow : Window
    {
        private class DeviceItem
        {
            public string Name { get; set; } = string.Empty;
            public DsDevice Device { get; set; } = null!;
        }

        public string? SelectedVideoPath { get; private set; }
        public string? SelectedAudioPath { get; private set; }
        public bool RememberForNextTime => chkRemember.IsChecked == true;

        public DeviceSelectWindow()
        {
            InitializeComponent();
            Loaded += DeviceSelectWindow_Loaded;
        }

        private void DeviceSelectWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var video = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice)
                    .Select(d => new DeviceItem { Name = d.Name, Device = d })
                    .ToList();
                cmbVideo.ItemsSource = video;
                if (video.Count > 0) cmbVideo.SelectedIndex = 0;

                var audio = DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice)
                    .Select(d => new DeviceItem { Name = d.Name, Device = d })
                    .ToList();
                cmbAudio.ItemsSource = audio;
                if (audio.Count > 0) cmbAudio.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"Failed to enumerate devices: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var v = cmbVideo.SelectedItem as DeviceItem;
                var a = cmbAudio.SelectedItem as DeviceItem;
                if (v == null)
                {
                    System.Windows.MessageBox.Show(this, "Please select a video device.", "Select Device", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (a == null)
                {
                    System.Windows.MessageBox.Show(this, "Please select an audio device.", "Select Device", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                SelectedVideoPath = v.Device.DevicePath;
                SelectedAudioPath = a.Device.DevicePath;
                DialogResult = true;
            }
            catch
            {
                DialogResult = false;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
