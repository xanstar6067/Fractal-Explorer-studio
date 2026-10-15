namespace FractalExplorerWPF.Models;

/// <summary>Трёхмерные фракталы, которые обслуживает <c>Views/Fractal3DWindow</c>.</summary>
public enum Fractal3DKind
{
    Mandelbulb,
    Juliabulb,
    Mandelbox,
    MengerSponge,
    SierpinskiTetrahedron,
    QuaternionJulia,
    ApollonianPacking,
    Ifs3D,
    Vicsek,
    CantorDust,
    Terrain,
    BurningShip,
    BurningShipJulia,
    BulbBoxHybrid,
    Phoenix,
    StrangeAttractor,
    Flame3D,
    Kifs,
    Dla3D,
    LSystem3D,
    Buddhabrot4D,
    GrayScott3D,
    Turing3D,
    CahnHilliard3D,
    Hopf,
    Lichtenberg3D,
    Physarum3D,
    Lenia3D,
    Kobayashi3D,
    Lyapunov3D,
    Excitable3D,
    PeriodicSurfaces,
    ImplicitSurface,
    HyperbolicHoneycomb,
    AbcFlow,
    GinzburgLandau3D,
    SwiftHohenberg3D,
    PhaseFieldCrystal3D,
    Kleinian,
    AntoineNecklace
}

public sealed record Fractal3DDefinition(
    string Title,
    string PanelTitle,
    string Description,
    string SaveCategory,
    string ExportPrefix);
