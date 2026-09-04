# 🎵 Taskbar Music Widget (Windows 11 / 10)

<p align="center">
  <b>English</b> | <a href="README.es.md"><b>Español</b></a>
</p>

![Version](https://img.shields.io/badge/version-v0.8.6-1ED760?style=flat-square)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?style=flat-square)
![Framework](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?style=flat-square)
![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)

A native, lightweight, and elegant Windows taskbar widget that delivers real-time media controls and an interactive flyout card inspired by Fluent Design and Spotify.

<p align="center">
  <img src="assets/demo.gif" alt="Taskbar Music Widget Demo" width="720" style="border-radius: 8px; box-shadow: 0 4px 16px rgba(0,0,0,0.4);" />
</p>

---

## ✨ Key Features

- **Seamless Taskbar Integration:** Docks cleanly into the system tray area with zero distracting frames or mismatched backgrounds.
- **Universal Media Detection (GSMTC):** Automatically supports Spotify, YouTube, Twitch, Netflix, SoundCloud, VLC, Chrome, Opera, Edge, Brave, and any Windows Media-compatible player.
- **Expandable Interactive Flyout Card:** Hovering over the widget displays an interactive floating card with high-resolution album art, track title, artist name, a full progress bar with manual scrubbing, and playback controls.
- **Cinematic Text Marquee (KeyFrame Animation):**
  - Implemented in both the taskbar widget and the floating card (*Flyout*).
  - Subpixel typographical measurement via `FormattedText` and `DesiredSize` to eliminate premature truncation.
  - Strategic 2-second pauses at the start and end of each cycle, allowing long song titles and artist names to be read comfortably.
- **Bilingual Interface Support (English / Spanish):** Automatically detects Windows display language (`CultureInfo.CurrentUICulture`), adapting HUD states (*"No music playing"* / *"Sin música"*), flyout controls, volume toasts, and context menus, while preserving original song and video titles completely untouched.
- **Spotify Smart Shuffle Integration:** Two-way integration with Spotify via **Windows UI Automation** that detects and toggles between *Disabled*, *Normal Shuffle*, and *Smart Shuffle* with its signature sparkle badge (`✦`).
- **Mouse Wheel Volume Control:** Adjust system master volume directly over the widget in precise **5%** steps via low-level COM interfaces (**CoreAudio IAudioEndpointVolume**).
- **Smart Window Focus & Browser Tab Switching:**
  - Clicking the album art or title brings the media application into focus **without unmaximizing or altering its window layout** (even on secondary displays).
  - In Chromium browsers (Opera, Chrome, Edge), intelligently locates the exact background tab playing audio (e.g., YouTube) and switches to it automatically using UI Automation.
- **Full-Screen Auto-Hide:** Reactively hides during full-screen games, borderless video playback, or when the Windows taskbar auto-hides.
- **Ultra-Low Resource Usage:** Virtually 0% CPU (< 0.1%) and minimal RAM footprint (~30 MB).

---

## 🛠️ Tech Stack & Architecture

- **Framework:** .NET 8 (C#) with Windows Presentation Foundation (WPF).
- **Windows Runtime (WinRT):** `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` for real-time media telemetry.
- **Win32 P/Invoke:** Advanced window manipulation (`user32.dll`, `dwmapi.dll`), Z-order layering (`WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`), and monitor calculation (`MonitorFromWindow`).
- **UI Automation:** `System.Windows.Automation` for Chromium accessibility tree inspection and cross-process button automation.
- **COM Interop:** Implementation of `IMMDeviceEnumerator` and `IAudioEndpointVolume` for direct hardware audio endpoint control.

---

## 🚀 Installation & Build

### Requirements
- Windows 10 (version 19041+) or Windows 11.
- .NET 8 SDK.

### Build from Terminal
```bash
# Clone repository
git clone https://github.com/KillianTR/TaskbarMusicWidget.git
cd TaskbarMusicWidget

# Build project
dotnet build -c Release

# Publish self-contained optimized single-file executable
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The ready-to-use executable will be generated at:
`bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/TaskbarMusicWidget.exe`

---

## 📌 Version History (Changelog)

### v0.8.6
- **Unconstrained Canvas Marquee Viewport (Critical Hotfix):** Resolved the underlying WPF layout engine clipping bug where `TextBlock` elements placed directly inside fixed-width `Grid` cells had an internal `GetLayoutClip` applied to them, truncating all characters beyond 116px (taskbar) and 238px (flyout) during translation.
- **Full-Width Glyphs Rendering:** Wrapped marquee `TextBlock` elements inside an unconstrained layout `Canvas`, allowing all glyphs (no matter how long the YouTube video or track title) to render completely without layout clipping.
- **Native Tooltip Integration:** Added full title and artist tooltips on hover over track labels in both the taskbar and flyout windows.

### v0.8.5
- **Marquee Title Scrolling & Stability Hotfix:** Resolved an issue where long track and video titles (e.g., YouTube videos like *"Ser Parte de Tantos Proyectos...¿Merece La Pena?"* or featured Spotify tracks) stalled or appeared truncated at container boundaries.
- **Cache Key Decoupling:** Decoupled animation tracking from volatile layout container widths, preventing recurring SMTC playback events from resetting running animations back to zero.
- **Persistent Flyout Animation:** Maintained background animation state across flyout hover cycles, ensuring text is immediately in motion without freezing upon card reveal.
- **Responsive Pacing & 40px Tail Clearance:** Reduced initial pause from 2.0s to 0.8s with a smooth 28 px/s scroll and +40px trailing margin, ensuring 100% of characters, punctuation, and parentheses are displayed without cut-offs.
- **Binary & Path Synchronization:** Ensured build and single-file publish targets are fully synchronized across `win-x64` and Startup shortcuts.

### v0.8.4
- **Windows 11 Flyout Spacing Calibration (Hotfix):** Adjusted the vertical position of the flyout card to maintain a **12px** gap above the taskbar, faithfully mirroring the native Windows 11 Notification Center and Calendar flyouts.
- **Smoother Cursor Transition:** Increased close debounce timer to 400ms for effortless cursor navigation between the widget and the floating card without premature dismissal.

### v0.8.3
- **Marquee Cutoff & Reset Fix (Hotfix):** Resolved an issue where long track titles stopped scrolling prematurely (e.g., halting at *"bert mccrac"* in the flyout or *"yungb"* in the taskbar widget).
- **Reset Prevention:** Implemented a state cache key (`cacheKey`) preventing secondary Windows SMTC events from resetting running animations when the track has not changed.
- **Complete Scroll Distance:** Added a generous margin (+35px) and composite measurement (`DesiredSize` + `FormattedText`) to ensure 100% of long titles and artists (including parentheses and `feat.` tags) scroll fully into view.

### v0.8.2
- **Taskbar Proportion Adjustment (Hotfix):** Reduced total widget width to 230px, eliminating excess space between text and buttons for a compact, harmonious layout.
- **Optimal Marquee Activation:** With container width set to ~110px, standard-length titles activate the cinematic marquee naturally without creating gaps for short titles.

### v0.8.1
- **Dynamic Internationalization (i18n):** Automatic detection of Windows OS display language (Spanish / English).
- **Interface & Status Localization:** Real-time adaptation of HUD strings (*"No music playing"* / *"Sin música"*), tooltips, volume notifications, and context menus. Original song and video titles are preserved untouched.
- **Visual Demo Integration:** Added continuous auto-playing `demo.gif` directly to the README.

### v0.8.0
- **Cinematic KeyFrame Marquee:** Replaced basic animation with `DoubleAnimationUsingKeyFrames` featuring 2-second pauses at both ends for comfortable reading.
- **Flyout Marquee Support:** The floating card now includes dynamic scrolling for overflow titles and artists.
- **Accurate Font Measurement:** Utilized `FormattedText` and system DPI to calculate real font widths.
- **Expanded Containers:** Increased widths (280px taskbar, 340px flyout) for better readability.
- **Bidirectional Smart Shuffle Sync:** Full support for Spotify's 3-state cycle with sparkle indicator (`✦`).
- **Maximized Window Preservation:** Removed disruptive DWM calls when focusing players across secondary monitors.

---

## 📄 License

This project is licensed under the MIT License. Feel free to use, modify, and distribute it.
