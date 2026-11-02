namespace Pulsive;

// HostBackdrop includes a fixed Windows blur. Blend it in across the entire
// range, alongside a growing Gaussian radius, to avoid a jump at tiny settings.
internal static class BlurStrength
{
    internal static double Amount(double setting) => Math.Clamp(setting/48,0,1);
    internal static double Radius(double setting) => 48*Math.Pow(Amount(setting),1.6);
}
