namespace FractalExplorerWPF.Models;

public sealed record FractalCatalogItem(
    IReadOnlyList<string> CategoryPath,
    string DisplayName,
    string Description,
    string PreviewResourcePath,
    string? LaunchKey = null)
{
    public string CategoryBreadcrumb => string.Join(" › ", CategoryPath);

    /// <summary>Режим окна <c>Fractal3DWindow</c>: плитка получает значок 3D, пункт попадает в меню «Трёхмерные».</summary>
    public bool IsThreeDimensional => Fractal3DCatalog.TryParseLaunchKey(LaunchKey, out _);
}
