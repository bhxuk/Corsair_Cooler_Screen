# What's new

## 1.0.1 (23 October 2026)

- Clearer wording on the Cooler page.

## 1.0.0 (21 October 2026)

The first version of Cooler Screen: pictures, GIFs, clocks and live stats on the CORSAIR NAUTILUS RS LCD, without iCUE.

**The screen**
- Talks to the cooler's screen directly over USB (1B1C:0C57, and 0C55 on some caps) using Windows' own HID support. No drivers, no services, no admin rights.
- Only ever opens the cooler's screen, found by its USB ID.
- Brightness, rotation (0°, 90°, 180°, 270°), up to 30 frames a second, and a picture quality setting.
- Hands the screen back to the cooler's own built-in display when Cooler Screen closes (can be turned off).
- Reconnects by itself after unplugging, sleep and resume.
- A test pattern, live connection details and a warning if iCUE or another Corsair app is also driving the screen.

**Backgrounds**
- A colour, a gradient, any picture or animated GIF/WebP, or a slideshow of a whole folder with crossfades.
- Fill, Fit or Stretch, with zoom, position, GIF speed and a darken slider.
- Pictures are copied into Cooler Screen's own folder, so screens keep working if the original moves.

**On top**
- Clocks: Digital, Stacked, Flip, Seconds ring, Analog, Analog minimal and Two time zones. 12 or 24 hour, seconds, date styles and any time zone.
- PC stats: Gauge, Two rings, Grid of four, Big number, Bars and a two-minute Graph. CPU and GPU temperature and load, memory, GPU memory, power, CPU clock, motherboard and drive temperature, and network speed. Readings turn amber when warm and red when hot.
- Now playing: the song in Spotify, a browser or any app on Windows' media controls, with artwork and progress round the edge.
- Weather from Open-Meteo, a countdown (or count up), and your own message, still or scrolling.
- Any installed font, five weights, text, accent and soft colours, size and position.

**Playlist and automations**
- A playlist that rotates through screens, in order or shuffled.
- A too-hot alert that takes over with a pulsing red warning.
- Show a screen while an app or game is running.
- Dim or turn the screen off at night.
- Change the screen, or turn it off, while the PC is locked.

**Temperatures**
- Uses Sector's agent for exact hardware temperatures if Sector is installed.
- Otherwise reads what Windows offers: CPU and GPU load, memory, network, NVIDIA GPU sensors and an approximate CPU temperature.

**The app**
- 16 ready-made screens to start from, duplicate, import and export (pictures travel inside the file).
- Live round preview of exactly what the cooler shows, and of the screen you're editing.
- Tray icon with a screen switcher, pause and next.
- Starts with Windows, checks for updates, light and dark themes, and backup and restore.
