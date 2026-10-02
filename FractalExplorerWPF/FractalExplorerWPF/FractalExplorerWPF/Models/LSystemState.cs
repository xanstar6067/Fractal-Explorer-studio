namespace FractalExplorerWPF.Models;

public sealed class LSystemState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public LSystemDefinition Definition { get; set; } = new();
    public double ViewZoom { get; set; } = 1;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public double AnimationDurationSeconds { get; set; } = 6;

    public LSystemState Clone() => new()
    {
        SaveName = SaveName, Timestamp = Timestamp, Definition = Definition.Clone(),
        ViewZoom = ViewZoom, PanX = PanX, PanY = PanY,
        AnimationDurationSeconds = AnimationDurationSeconds
    };
}
