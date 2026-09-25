namespace MyVoice.Windows.Core;

/// <summary>The overlay's sound bars (the Mac's OverlayIndicatorView), in 96-DPI pixels.</summary>
public static class OverlayMeter
{
    public const int BarCount = 7;
    public const float MinBar = 4, MaxBar = 20;

    /// <summary>Mic peak (0…1) to bar level with the Mac's formula: dB → (dB + 50) / 50, clamped.</summary>
    public static float Normalize(float peak) =>
        peak <= 0 ? 0 : Math.Clamp((20 * MathF.Log10(peak) + 50) / 50, 0, 1);

    /// <summary>The level to show next: rises at once, falls smoothly (the Mac animates bars over 0.1 s).</summary>
    public static float Next(float shown, float peak)
    {
        var target = Normalize(peak);
        return target >= shown ? target : shown + (target - shown) * 0.5f;
    }

    /// <summary>Recording: the Mac's formula — taller in the middle, a little variation per bar.
    /// Connecting: a slow wave that ignores the mic, so waiting never looks like hearing speech.</summary>
    public static float[] BarHeights(OverlayKind kind, float level, double seconds)
    {
        const int center = BarCount / 2;
        var heights = new float[BarCount];
        for (var i = 0; i < BarCount; i++)
        {
            double height;
            if (kind == OverlayKind.Connecting)
            {
                var wave = 0.5 + 0.5 * Math.Sin(2 * Math.PI * seconds * 1.2 - i * 0.8);
                height = MinBar + (MaxBar - MinBar) * 0.3 * wave;
            }
            else
            {
                var centerBias = 1.0 - Math.Abs(i - center) / (double)(center + 1) * 0.4;
                var variation = Math.Sin(i * 1.8 + level * 10) * 0.15 + 1.0;
                height = (MinBar + (MaxBar - MinBar) * level * centerBias) * variation;
            }
            heights[i] = (float)Math.Clamp(height, MinBar, MaxBar);
        }
        return heights;
    }
}
