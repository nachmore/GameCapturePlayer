# Game Capture Player (WPF, .NET 8)

Low-latency preview for a video capture device or webcam, with live audio monitoring from a chosen microphone/line-in.

## Features
- Select a Video device (capture card or webcam) and an Audio input device.
- Low-latency video preview using DirectShow VMR9 in windowless mode.
- Audio monitoring via the default audio renderer.
- Resizes smoothly while preserving aspect ratio.
- Advanced settings window to toggle low-latency options (process priority, 1ms timer, low-latency GC, minimal buffering, VMR9 single stream, no graph clock).
- Optional stats overlay (FPS and dropped frames) with configurable corner position.

## Requirements
- Windows 10/11, x64.
- .NET 8 SDK installed.
- Video capture device drivers installed (WDM/DirectShow compatible).

## Build and Run
```pwsh
# From the src directory
 dotnet run -c Release
```
This will restore NuGet packages, build, and run the app.

## Usage
1. Launch the app.
2. Pick your capture card as the **Video Device** and the appropriate **Audio Device** (e.g., HDMI/Line-In) from the dropdowns.
3. Click **Start** to begin preview and audio monitoring.
4. Click **Advanced...** to open the advanced settings window:
   - Toggle low-latency options. System-level toggles apply immediately while running. Graph-level toggles apply on next Start.
   - Use "Show Stats Overlay" to display FPS and dropped frame counters; adjust the overlay position via the dropdown.
5. Click **Stop** to tear everything down.

## Tips for Low Latency
- The app renders the device's `Preview` pin when present. If your device only exposes a `Capture` pin, the preview still works but latency may vary.
- Prefer running as x64 to match most modern capture drivers.
- Use the device's control panel/driver software to set resolution and frame rate as desired; this app currently uses the device defaults.
- For best results, enable Minimal Pin Buffering and disable the graph clock; measure with the Stats overlay to find the best combo for your device.

## Troubleshooting
- If no devices are listed, ensure drivers are installed and that you are running a 64-bit process (this project targets x64 by default via .NET 8 runtime).
- If start fails with a graph build error, try stopping other apps using the device (e.g., OBS, camera app) and start again.
- For audio echo/feedback, use headphones while monitoring.

## Roadmap
- Device property pages (configure resolution, frame rate, formats).
- Buffer/latency controls for audio monitoring (exclusive mode when using WASAPI).
- Fullscreen toggle and always-on-top option.
