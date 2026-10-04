# ExCord

**Language: [한국어](README.md) | [English](README.en.md)**

ExCord is a personal Windows tray utility for Discord workflows.

- Global-hotkey TTS input and playback
- On-screen WebView overlay (URL-based)
- Tray-resident app with settings management

---

## Features

### 1) TTS (Text-to-Speech)
- Open the input window with a global hotkey (default: `Ctrl + Alt + T`)
- Convert typed text to speech and play it through your selected device
- Voice selection support for OneCore and SAPI5
- Output device, volume, and monitor playback settings
- Input history navigation with ↑ / ↓ in the input window

### 2) WebView Overlay
- Display URL content (GIF/image/web page) as an overlay
- Always-on-top behavior with fade/click-through on mouse hover
- Edit mode for move/resize with confirm/cancel flow
- Numeric geometry controls (X/Y/Width/Height)

### 3) General
- Start with Windows
- Tray menu toggles and settings access
- Version display and update check

---

## Requirements

- Windows 10 (2004, build 19041+) or Windows 11
- .NET 8 Desktop Runtime
- WebView2 Runtime
- (For Discord TTS routing) Virtual Audio Cable (e.g., VB-CABLE)

---

## Quick Start

1. Launch the app and verify the tray icon
2. Configure TTS output device and voice in Settings
3. Set Discord input device to the same virtual audio device
4. Open input window with hotkey and send TTS
5. Apply URL and place overlay from the WebView tab

---

## Settings File

- `%AppData%\ExCord\settings.json`

---

## Changelog

- Version notes: [update.md](update.md)
