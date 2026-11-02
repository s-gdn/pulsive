# pulsive

A transparent Windows audio visualiser that reacts to sound from other applications.

![pulsive logo](assets/pulsive-logo.png)

- Eight visual styles: Aurora, Spectrum, Orbit, Waveform, Bloom, Stardust, Terrain, and Helix.
- Custom gradients, zoom, adjustable feathering, and saved user presets.
- Optional background blur or solid colour, plus gradient and reactive glow borders.
- Resizable overlay with hover controls, toolbar locking, and optional startup on sign-in.

Requires Windows 10 version 2004 or newer (x64). Windows 11 provides live compositor blur; older systems use a compatibility path. Portable builds include .NET.

See [how-to.txt](how-to.txt) for running, building, controls, settings, and troubleshooting. Building from source requires the .NET 8 SDK on Windows.

Audio is processed locally in memory; it is not saved or transmitted. Built with WPF, Windows Composition, NAudio, Win2D, and SkiaSharp. Portable builds include dependency licence notices.
