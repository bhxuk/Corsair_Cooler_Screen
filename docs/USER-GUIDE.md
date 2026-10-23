# Cooler Screen user guide

Cooler Screen puts pictures, GIFs, clocks and live PC stats on the round screen of a **CORSAIR NAUTILUS RS LCD** cooler (or a NAUTILUS RS with the LCD screen module), without installing iCUE.

## Contents

- [Before you start](#before-you-start)
- [Installing](#installing)
- [Home](#home)
- [Screens and the editor](#screens-and-the-editor)
- [Playlist](#playlist)
- [Automations](#automations)
- [The Cooler page](#the-cooler-page)
- [Temperatures and Sector](#temperatures-and-sector)
- [Settings, updates and backup](#settings-updates-and-backup)
- [Troubleshooting](#troubleshooting)

## Before you start

- The screen needs its USB cable plugged into a **USB 2.0 (9-pin) header** on your motherboard. That's the only cable it needs for the picture.
- The fans and pump plug into your motherboard's **PWM headers** and follow your BIOS fan curves. Cooler Screen doesn't touch them.
- If **iCUE** is installed, uninstall it or turn off its LCD control. Two apps sending pictures to the same screen make it flicker. Cooler Screen warns you if it sees iCUE or another Corsair app running.

To check Windows can see the screen, open PowerShell and run:

```powershell
Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match 'VID_1B1C&PID_0C5[57]' }
```

You should see a line ending `VID_1B1C&PID_0C57` (or `0C55`).

## Installing

1. Download **[CoolerScreen-Setup.exe](https://github.com/bhxuk/Corsair_Cooler_Screen/releases/latest/download/CoolerScreen-Setup.exe)** and double-click it. No admin rights are needed.
2. If Windows says "Windows protected your PC", click **More info** and then **Run anyway**. This happens because the app isn't code-signed.
3. Cooler Screen opens and puts a clock on the cooler straight away. It lives in the notification area by the clock from then on.

Prefer not to install? **[CoolerScreen.exe](https://github.com/bhxuk/Corsair_Cooler_Screen/releases/latest/download/CoolerScreen.exe)** runs on its own from any folder.

To uninstall, use **Settings > Apps** in Windows. The cooler goes back to its own built-in screen.

## Home

The big round preview shows exactly what the cooler is showing right now, and underneath it says why (the screen you chose, the playlist, an alert, an app that's running and so on).

- **Pause** hands the screen back to the cooler's built-in display until you press **Resume**.
- **Show on the cooler**: click any screen to put it up. Right-click one to edit it.
- The cards on the right show the connection, where temperatures are coming from, and a brightness slider.

## Screens and the editor

**Screens** lists everything your cooler can show. **New screen** opens 16 ready-made starting points. Click a screen's picture or **Edit** to change it, **Show** to put it on the cooler, and **…** to duplicate, export or delete it.

While you edit, the preview on the right updates as you go, and (if **Show on the cooler while editing** is on) so does the cooler. Changes are saved automatically.

### Background

| Choice | What it does |
|---|---|
| Colour | One solid colour. |
| Gradient | Two colours blending, at any angle. |
| Picture or GIF | GIF, PNG, JPEG, WebP or BMP. Animated GIFs and WebPs play. The file is copied into Cooler Screen's own folder. |
| Slideshow | Every picture and GIF in a folder, one after another with a crossfade. New files are picked up automatically. Optional shuffle. |

For pictures: **Fill** crops to the round screen, **Fit** shows all of it with a colour around it, **Stretch** squashes it to fit. **Zoom** and the two **Move** sliders choose which part shows. **GIF speed** goes from a quarter to four times speed. **Darken** dims the background so a clock or stats on top are easier to read.

### On top

| Choice | Options |
|---|---|
| Clock | Digital, Stacked, Flip, Seconds ring, Analog, Analog minimal, Two time zones. 24-hour, seconds, date and date style, time zone, a small label. |
| PC stats | Gauge, Two rings, Grid of four, Big number, Bars, Graph. Pick which readings go where. Colour by temperature turns readings amber when warm and red when hot. |
| Message | Your own text, drawn as big as it fits. A second line uses your accent colour. Turn on **Scroll across** for a moving banner. |
| Countdown | Days, hours, minutes and seconds to a date, with a label. Or count up from a date. |
| Now playing | The song playing in Spotify, a browser or any app that shows on Windows' volume pop-up, with its artwork and progress round the edge. |
| Weather | Search for your town. Current temperature, conditions and the day's high and low, from Open-Meteo (free, no account). |

### Look

Any font installed on your PC, five weights, text, accent and soft colours (for labels and dates), size, position and a shadow behind text on pictures.

### Import and export

**Export** saves a screen as a `.coolerscreen` file. If it uses a picture or GIF (up to 25 MB), the picture goes inside the file. **Import** on the Screens page adds it back, on this PC or another.

## Playlist

Turn on **Rotate through screens** and the cooler shows each screen in the list for as long as you set (5 seconds to 10 minutes), in order or shuffled. **Next in playlist** on Home and in the tray menu skips ahead. When the playlist is off, the cooler shows the screen you chose.

## Automations

These take over from the playlist or your chosen screen when they apply, in this order:

1. **Too-hot alert**: when a reading stays over its limit for a few seconds, the screen turns pulsing red with the reading. It stays until the reading drops 3 below the limit. Limits are in °C. Add as many as you like, and use **Preview the alert** to see it.
2. **While editing** a screen (if that's switched on).
3. **When the PC is locked**: show another screen, or turn the screen off.
4. **When an app is running**: for example, show GPU stats while a game is open. Type the program's name (like `cs2` or `Spotify.exe`) or use **Pick…** to choose from what's running.
5. The **playlist**, then the screen you chose.

**Night time** dims the screen (or turns it off) between two times, like 23:00 to 07:00.

## The Cooler page

- Connection details: product, USB ID, serial number, firmware, frames a second and how much has been sent.
- **Reconnect**, **Show a test pattern** and **Copy details** (handy if you report a problem).
- **Brightness** and **Rotation** if the cooler is mounted sideways or upside down.
- **Frames a second for GIFs** (10 to 30) and **Picture quality**. Clocks and stats only update once a second, so they use very little.
- **When Cooler Screen closes, give the screen back to the cooler**: on by default, so the cooler shows its own display when the app isn't running.

### No drivers needed

Cooler Screen looks for one device only: USB ID **1B1C:0C57** (or 0C55). It matches that ID before opening anything, opens it shared, and uses Windows' built-in USB support. It installs no drivers and runs no background services.

## Temperatures and Sector

Windows doesn't let ordinary apps read CPU temperatures directly. Rather than install a driver, Cooler Screen:

- uses **[Sector](https://github.com/bhxuk/Sector)**'s agent if it's installed on the same PC, for exact CPU, GPU, motherboard and drive temperatures, power and clocks;
- otherwise reads what Windows gives any app: CPU and GPU load, memory, network speed, NVIDIA GPU temperature and power (through NVIDIA's own driver), and an approximate CPU temperature from the motherboard's thermal zones where it reports one.

Home shows which is in use. You can turn Sector off in **Settings > Temperatures**.

## Settings, updates and backup

- **Start with Windows**, °C or °F, theme and accent colour.
- **Updates**: checks every few hours and offers new versions in a notification and the tray menu, or installs them automatically if you turn that on. Cooler Screen reopens by itself.
- **Backup**: saves every screen, picture, the playlist, automations and settings in one zip. **Restore a backup** on a new PC puts it all back.
- Your files live in `%LOCALAPPDATA%\CoolerScreen`, with a log file (`coolerscreen.log`) there too.

## Troubleshooting

**"Cooler screen not found"**
Check the screen's USB cable is on a USB 2.0 header and the header is enabled in the BIOS. Run the PowerShell check above. Press **Reconnect** on the Cooler page.

**The screen flickers between two pictures**
Another app is also sending pictures. Close iCUE (or turn off its LCD control), SignalRGB, or any other RGB software that supports the NAUTILUS LCD.

**CPU temperature shows -- or looks wrong**
Install Sector for exact readings, or use load instead of temperature on your screens.

**A GIF plays slowly**
Raise **Frames a second for GIFs** on the Cooler page. Very large GIFs (hundreds of frames at high resolution) take a moment to load the first time.

**The picture is upside down or sideways**
Set **Rotation** on the Cooler page.

Still stuck? [Report a problem](https://github.com/bhxuk/Corsair_Cooler_Screen/issues) and paste in **Copy details** from the Cooler page.
