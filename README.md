<h1><img src="assets/pulsive-logo.png" alt="" width="36" height="36" align="absmiddle"> pulsive</h1>

A transparent Windows audio visualiser that reacts to sound from other applications.

![Terrain visualiser, a colour change in settings, and Stardust](assets/pulsive-demo.gif)

- Fourteen built-in visual styles and importable .milk collections with texture support, including projectM-powered MilkDrop presets **Nebula, Acid Tunnel, and Prism Flow**, plus the original eleven styles.
- Custom gradients, zoom, adjustable feathering, and saved user presets.
- Optional background blur or solid colour, plus gradient and reactive glow borders.
- Resizable overlay with hover controls, toolbar locking, and optional startup on sign-in.
- Fullscreen, customisable now-playing text, and shuffle with selectable presets and timed transitions.

Download the zip from the [latest release](https://github.com/s-gdn/pulsive/releases/latest) and simply extract. Ensure the exe and dlls stay in same folder. Then just run Pulsive.exe.


Requires Windows 10 version 2004 or newer (x64). Windows 11 provides live compositor blur; older systems use a compatibility path. Portable builds include .NET.

projectM styles require an OpenGL 3.3 graphics driver and the Visual C++ x64 runtime. Keep the included `projectm` folder beside the executable.

See [how-to.txt](how-to.txt) for running, building, controls, settings, and troubleshooting. Building from source requires the .NET 8 SDK on Windows.

Audio is processed locally in memory; it is not saved or transmitted. Built with WPF, Windows Composition, NAudio, Win2D, SkiaSharp, and [projectM](https://github.com/projectM-visualizer/projectm). Portable builds include dependency licence notices and projectM source.

**Please note: Older Windows Defender versions have been known to randomly flag the compressed zip releases as a virus and block the download. This is an error with windows defender's machine learning and is fixed by updating to the newest version of Windows Defender. And of course you can always inspect the source code and build the program yourself if this worries you.**
