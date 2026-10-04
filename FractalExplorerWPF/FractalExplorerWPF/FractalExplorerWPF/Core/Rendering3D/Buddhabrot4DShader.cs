namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Project orbit counts along the whole ray before applying tone mapping.</summary>
internal static class Buddhabrot4DShader
{
    // Camera, probe and surface styles share the colored-volume implementation.
    // Glow is additive: a front view integrates all depth layers, like the 2D Buddhabrot.
    // The cloud style retains absorption for examining the spatial distribution.
    public static readonly string Source = Flame3DShader.Source
        .Replace("float alpha = 1 - exp(-v.a * dt * ShapeA.z * (style == 3 ? 18 : 48));",
            "float alpha = style == 3 ? 0 : 1 - exp(-v.a * dt * ShapeA.z * 48);")
        .Replace("radiance += transmission * alpha * tint * (style == 3 ? 3.0 : 1.5) * max(strength, 0.001);",
            "radiance += style == 3 ? v.rgb * dt * ShapeA.z * 12 * max(strength, 0.001) : transmission * alpha * tint * 1.5 * max(strength, 0.001);");
}
