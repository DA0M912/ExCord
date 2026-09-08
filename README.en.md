# ExCord

**Language: [한국어](README.md) | [English](README.en.md)**

ExCord is a personal Windows tray utility built to smooth over a handful of things I found annoying while using Discord. It sits quietly as a single icon in the system tray, and you can toggle two features on and off whenever you need them: reading typed text aloud as speech (TTS), and displaying a moving character/GIF overlay on top of your screen (GifTalk).

---

## Why I Made This

During Discord calls with friends, there were plenty of moments where I thought, "it'd be nice if whatever I typed could just be read out loud." Sometimes my mic sounded bad, sometimes my throat wasn't up for talking, and sometimes I just wanted to mess around and sound like a bot. Most existing TTS programs either weren't built with Discord in mind or required a fiddly setup, so I built a lightweight tool of my own that I could pop open with a single hotkey and start talking through immediately. On top of that, I added a "GifTalk" feature that overlays a reactive character (GIF) on the screen, to make calls a little more fun.

Rather than focusing on one big feature, this is a personal toolkit-style project that I keep adding to whenever something about using Discord bugs me.

---

## Feature Summary

| Feature | Description |
|---|---|
| **TTS (Text-to-Speech)** | Press a global hotkey to bring up a small input box, type your text, and it's converted to speech and played through a virtual audio device. If Discord recognizes that virtual device as your microphone, the other person hears it as if you were "speaking." |
| **GifTalk** | Displays a GIF, image, or web page URL of your choice as an always-on-top overlay on your screen. It automatically fades when you hover over it and lets clicks pass through, so it never gets in the way of whatever else you're doing. |
| **Tray Residency** | Runs with no main window, living only as an icon in the taskbar corner; double-click it to open the settings window. |

---

## Detailed Feature Guide

### 1. General Tab (Global Settings)

- **Start with Windows**: When enabled, ExCord launches automatically at Windows startup. (This uses the `HKCU\...\Run` registry entry and doesn't require administrator privileges.)
- **Text-to-Speech**: Turns the entire TTS feature on or off. When off, the global hotkey is disabled and the settings window's TTS tab is greyed out.
- **GifTalk**: Turns the entire GifTalk feature on or off. When off, the on-screen overlay disappears and the settings window's GifTalk tab is greyed out.

### 2. TTS Tab

#### 2-1. Global Hotkey

- Combine the `Ctrl` / `Alt` / `Shift` checkboxes with the key dropdown to build the hotkey you want. The default is **`Ctrl + Alt + T`**.
- Pressing this hotkey brings up a small, Discord-style input box at the bottom center of your screen, no matter what program you're currently using (Discord, a game, a browser, etc.).
- Using the input box:
  - **Enter**: Immediately converts the typed text to speech, plays it, and closes the box.
  - **Esc**: Closes the box without doing anything.
  - The input box automatically hides when it loses focus (e.g., if you click another window).
  - If you type and send a new sentence while one is already being spoken, the current playback stops immediately and the new sentence plays instead.

#### 2-2. TTS Voice

- The dropdown lists two kinds of voices together.
  1. **Modern Windows voices (OneCore)**: The natural-sounding voices you can add via `Settings → Accessibility → Narrator → Add voices`.
  2. **SAPI5 voices**: Older voice engines that have long been built into Windows (Microsoft David, Zira, etc.), or separately installed SAPI5 voice packs.
- The list is sorted by language, then by name.
- **Want more voice options?** Download additional voices for your preferred language from Windows' "Add voices" settings, then reopen ExCord — they'll show up automatically.
- If a voice you had selected disappears for some reason (e.g., it was removed), the program automatically switches to another voice in a similar language and lets you know via a tray notification.

#### 2-3. Virtual Output Device

- Speech converted by TTS is played through whichever audio output device you select here. **For the other person on a Discord call to hear this audio, you need to set Discord's microphone input device to the same virtual device.**
- If you leave a regular speaker or headphone selected, only you will hear it — it won't reach the other person. That's why a **"Virtual Audio Cable"** program is required separately. A popular free option is **[VB-CABLE](https://vb-audio.com/Cable/)**, which is also the default setting (`VB-Cable`).
- Setup summary:
  1. Install VB-CABLE (or another virtual audio cable, such as SteelSeries GG) and restart your computer.
  2. In ExCord's Virtual Output Device dropdown, select the newly created virtual input device (`CABLE Input`).
  3. In Discord's settings → Voice & Video → Input Device, select the same virtual input device (`CABLE Input`).
- ExCord automatically scans and lists the audio devices connected to your computer, prioritizing devices with "VB" in their name as the default selection.

#### 2-4. Output Volume

- A 0–100% slider that controls the output volume of the TTS speech itself.

#### 2-5. Play Monitor Sound on Default Speaker

- When checked, the same speech is played simultaneously through both the virtual device and **your computer's default speaker/headphones**. This lets you hear directly what you're currently "sending out" via TTS. When unchecked, only the virtual device plays the audio — you won't hear it yourself.

### 3. GifTalk Tab

#### 3-1. GIF URL

- Type a web address into the text box (a direct GIF link, an image, or a regular webpage URL all work) and click **Apply**, and the on-screen overlay immediately loads that address.
- Since it uses Microsoft Edge WebView2 (a Chromium-based browser engine) internally, it can display not just animated GIFs but also simple HTML pages or web widgets.
- Using a GIF/PNG with a transparent background lets the character float on screen without a visible rectangular frame.

#### 3-2. Edit Mode (Position & Size Adjustment)

- Toggling **Edit Mode** on shows a blue border box over the overlay, along with check (✓) / cancel (✕) buttons in the bottom-right corner.
- **Moving position**: Click anywhere inside the border box with the left mouse button and drag to move the entire overlay wherever you like.
- **Resizing**: In the current version, there's no way to resize the overlay by dragging it directly — instead, you set the exact position and size by typing numbers into the **X / Y / Width / Height** fields below. Values are reflected on the overlay in real time.
  - X, Y: The overlay's coordinates (in pixels), measured from the top-left of the screen.
  - Width, Height: The overlay's width/height (in pixels, minimum 80).
- **✓ (Check) button**: Saves the current position/size as-is and exits edit mode.
- **✕ (Cancel) button**: Reverts to the state right before you entered edit mode and exits. (Handy if you accidentally moved it somewhere odd.)

#### 3-3. Auto-Fade on Hover + Click-Through

- Outside of edit mode, the overlay normally appears sharp, but **automatically fades (becomes semi-transparent) and lets clicks pass through to the window underneath when your cursor moves over it**. It returns to normal once the cursor leaves.
- In other words, even though the overlay is always floating on top of your screen, it never gets in the way of clicking or interacting with the window beneath it.

### 4. Taskbar (Tray) Icon

You can control ExCord through its tray icon, located in the bottom-right corner of the taskbar.

- **Double-click**: Opens the settings window.
- **Right-click menu**:
  - **Settings**: Opens the settings window.
  - **Overlay: ON/OFF**: A quick toggle to turn the overlay on or off without opening the settings window.
  - **Exit**: Fully quits the program. (Any speech playback in progress is safely cleaned up before exiting.)

### 5. How Settings Are Saved

- Most settings are **saved automatically the moment they're changed**. There's no separate "Save" button to press.
- The settings file is stored at `%AppData%\ExCord\settings.json`.

---

## Installation Guide

Laid out step by step so that even first-time users can just follow along.

### Prerequisites Checklist

| Item | Required? | Notes |
|---|---|---|
| Windows 10 (version 2004 / build 19041 or later) or Windows 11 | Required | The app may not run on earlier versions. |
| [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) | Auto-prompted at runtime | If not bundled with the executable, Windows will automatically pop up an install link the first time you run it. Just follow the prompt to install it. |
| WebView2 Runtime | Usually already installed | Built in by default on Windows 11 and recent Windows 10. If the GifTalk screen doesn't appear, download the "Evergreen Bootstrapper" from [here](https://developer.microsoft.com/microsoft-edge/webview2/) and install it. |
| A virtual audio cable (e.g., [VB-CABLE](https://vb-audio.com/Cable/)) | Required to use TTS in Discord | Not needed to run the program itself, but without it, your voice won't reach the other person. |

### Installation & Setup Steps

1. **Download the latest version**
   👉 **[Download the latest ExCord release](https://github.com/DA0M912/ExCord/releases/latest)**
   Follow the link and download the latest installer from the `Assets` list.

2. **Run it**
   Double-click the downloaded `ExCord.exe` to run it.
   - A blue "Windows protected your PC" (SmartScreen) warning may appear. This is normal — see the [SmartScreen Warning note](#note) below.
   - If the .NET 8 runtime isn't installed, an install prompt will appear automatically. Install it and run ExCord again.

3. **Confirm it's running**
   Once running, no window will appear — you'll simply see the ExCord icon appear in the bottom-right corner of the taskbar (click the `^` arrow if it's hidden).

4. **Install a virtual audio cable (if you plan to use TTS in Discord)**
   1. Download the `VBCABLE_Driver_Pack` zip from the [VB-CABLE](https://vb-audio.com/Cable/) page and extract it.
   2. Run `VBCABLE_Setup_x64.exe` inside the folder **as an administrator** to install it.
   3. **Restart your computer** after installation. (If you skip this, the new audio device may not appear.)
   - Any virtual audio device works, not just VB-CABLE.
   - If you already use a different virtual audio device, that will work fine too.

5. **First-time ExCord setup**
   1. Double-click the tray icon to open the settings window.
   2. In the `TTS` tab, set **Virtual Output Device** to your virtual input device.
   3. Choose the voice you want in `TTS Voice`.
   4. Optionally, change the `Global Hotkey` to your liking (default `Ctrl+Alt+T`).
   5. In Discord's settings → Voice & Video → Input Device, change it to the same virtual input device.
   6. During a Discord call, press `Ctrl+Alt+T` (or your chosen hotkey), type a sentence, and press Enter to check whether the other person can hear the voice.

6. **Try out GifTalk**
   1. In the settings window's `General` tab, turn on `GifTalk`.
   2. In the `GifTalk` tab, enter the GIF/image address you want and click `Apply`.
   3. Turn on `Edit Mode`, drag it to the position you want, adjust the Width/Height numerically if needed, and click ✓ to save.

7. **Auto-launch at Windows startup**
   Turn on `Start with Windows` in the `General` tab, and from the next boot onward, ExCord will start automatically without you needing to launch it manually.

---

## Notes

- **About the Windows SmartScreen warning** <a id="note"></a>
  ExCord is a personal project and isn't signed with a paid Microsoft code-signing certificate. Because of this, a blue "Windows protected your PC" screen may appear the first time you run it. This doesn't mean it's malware — it just means **the program is unsigned and doesn't yet have any reputation data.** Click **"More info" → "Run anyway"** on that screen to run it normally. If you'd feel more comfortable, feel free to scan the downloaded file with your antivirus or [VirusTotal](https://www.virustotal.com/) before running it.
- **False positives from some antivirus programs**: Because the app uses global hotkey hooking and screen overlays (always-on-top, click-through, etc.), some antivirus software may misclassify it as suspicious behavior. The source code is public, so feel free to check it out yourself if you're curious.
- **Why can't the other person hear the sound?**: The most common causes are (1) the virtual audio cable isn't installed, (2) ExCord's Virtual Output Device and Discord's input device are set to different devices, or (3) Discord's "Advanced Voice Activity" setting is either too sensitive or not sensitive enough.
- **Test environment (developer's PC specs)**: Below is the environment actually used for testing during development. For reference only — most lower-spec systems should still work fine.
  - OS: `[Enter Windows version/build number here]`
  - CPU: `[Enter CPU model here]`
  - RAM: `[Enter RAM capacity here]`
  - GPU: `[Enter GPU model here, affects WebView2 rendering]`
  - Virtual audio cable used: `[Enter product name here, e.g., VB-CABLE]`
- **Automatic update check**: Every time you open the settings window, it automatically checks GitHub for the latest release. If a new version is available, a blinking blue "New version available" message appears in the bottom-left of the settings window — click it to go to the release page. (It doesn't install automatically; you'll need to download and overwrite-install the new version yourself.)
- **Log files**: If an error occurs, a log file is saved in the `%AppData%\ExCord\` folder. Please attach it when reporting a bug — it helps a lot with diagnosing the issue.
- **Known limitations**
  - The GifTalk overlay currently can't be resized by dragging — resizing is only possible by entering numbers manually.
  - On displays with a scaling factor other than 100%, or in multi-monitor setups with different scaling factors, the overlay's position may be slightly off.
  - As a personal project, it hasn't gone through commercial-grade QA. Please file an issue if you run into any problems.
- **License**: This project is distributed under the [MIT License](LICENSE). You're free to use, modify, and distribute it.

---

If you have any questions or ideas for improvement, feel free to let us know via a GitHub issue.
