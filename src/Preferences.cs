namespace Pulsive;

public class AppearanceSettings
{
    public int Mode { get; set; }
    public int Palette { get; set; }
    public double Gain { get; set; } = 1.5;
    public double Intensity { get; set; } = .9;
    public double Zoom { get; set; } = 1;

    public double FeatherLeft { get; set; } = .15;
    public double FeatherTop { get; set; } = .15;
    public double FeatherRight { get; set; } = .15;
    public double FeatherBottom { get; set; } = .15;
    public bool LinkFeather { get; set; } = true;
    public double FeatherBoundLeft { get; set; }
    public double FeatherBoundTop { get; set; }
    public double FeatherBoundRight { get; set; }
    public double FeatherBoundBottom { get; set; }
    public bool LinkFeatherBounds { get; set; } = true;

    public bool BackgroundBlur { get; set; }
    public double BackgroundBlurAmount { get; set; } = 20;
    public int BackgroundType { get; set; } = -1;
    public string BackgroundColor { get; set; } = "#101725";
    public string CustomColorStart { get; set; } = "#68EFDE";
    public string CustomColorEnd { get; set; } = "#C17AFF";
    public int BorderType { get; set; }
    public string BorderColor { get; set; } = "#68EFDE";
}

public sealed class Preferences : AppearanceSettings
{
    public bool Topmost { get; set; } = true;
    public double Width { get; set; } = 1000;
    public double Height { get; set; } = 560;
    public string? Device { get; set; }
    public bool ToolbarLocked { get; set; }
    public string? ActivePresetId { get; set; }
}