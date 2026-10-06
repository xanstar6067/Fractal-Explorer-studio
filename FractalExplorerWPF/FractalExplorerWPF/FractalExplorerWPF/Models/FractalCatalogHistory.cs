using System.Globalization;

namespace FractalExplorerWPF.Models;

/// <summary>
/// Первое внедрение рабочего режима в WPF, восстановленное по истории Git.
/// Ранние записи каталога были заглушками: для них указана дата рабочего запуска.
/// История привязана к ключам запуска, чтобы переименование и правки режима не меняли дату.
/// Git при запуске приложения не требуется. Для новых режимов добавляйте дату здесь.
/// </summary>
internal static class FractalCatalogHistory
{
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> Introductions = Create();

    public static DateTimeOffset? GetIntroducedAt(string? launchKey) =>
        launchKey is not null && Introductions.TryGetValue(launchKey, out DateTimeOffset date) ? date : null;

    private static Dictionary<string, DateTimeOffset> Create()
    {
        var dates = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        // 4bcf5d4
        Add("2026-07-10T15:28:37+03:00",
            "Generalized",
            "Simonobrot",
            "Celtic",
            "Buffalo",
            "Tricorn",
            "BurningShip",
            "Mandelbrot");

        // 80ce12c
        Add("2026-07-11T14:46:08+03:00",
            "JuliaBurningShipGallery",
            "JuliaGallery",
            "JuliaBurningShip",
            "Julia");

        // af7bc2a
        Add("2026-07-11T16:56:17+03:00",
            "NewtonPools");

        // af659de
        Add("2026-07-11T18:57:28+03:00",
            "Phoenix");

        // 96764ae
        Add("2026-07-11T19:50:44+03:00",
            "Collatz");

        // 2fb6467
        Add("2026-07-12T09:03:30+03:00",
            "NovaJulia",
            "NovaMandelbrot");

        // 905f866
        Add("2026-07-12T11:42:55+03:00",
            "Buddhabrot");

        // 93377e3
        Add("2026-07-12T12:15:31+03:00",
            "Flame");

        // 7943fd3
        Add("2026-07-12T13:30:42+03:00",
            "IFS");

        // 57d2019
        Add("2026-07-12T21:40:58+03:00",
            "Henon",
            "Ikeda",
            "Rossler",
            "Lorenz",
            "Bifurcation",
            "LogisticMap",
            "Lyapunov");

        // 477d916
        Add("2026-08-16T16:58:17+03:00",
            "InverseCollatzTree");

        // ad55bbf
        Add("2026-08-16T20:12:51+03:00",
            "Attractors2D");

        // 450bb67
        Add("2026-08-16T22:30:51+03:00",
            "LSystem");

        // 4499db8
        Add("2026-08-16T22:50:03+03:00",
            "SerpinskyChaos");

        // b4411a0
        Add("2026-08-17T14:48:40+03:00",
            "DomainColoring");

        // ff97e9b
        Add("2026-08-17T17:41:13+03:00",
            "ApollonianGasket",
            "DLA");

        // 7111322
        Add("2026-08-24T18:04:07+03:00",
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.HyperbolicGeometry),
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.AperiodicTilings),
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.CircleInversion),
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.Phyllotaxis),
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.PrimeGeometry),
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.RationalNumbers),
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.PascalModulo),
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.ModularArithmetic),
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.FourierEpicycles));

        // e1208c6
        Add("2026-08-24T19:11:56+03:00",
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.ChladniWaveInterference));

        // 7d3cf00
        Add("2026-08-25T00:11:21+03:00",
            "GrayScott");

        // c613e0f
        Add("2026-08-26T00:10:53+03:00",
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.RecamanSequence),
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.VoronoiLloyd),
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.StochasticMotion),
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.KleinianSchottky),
            MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.KnotStudio));

        // b2e1976
        Add("2026-09-15T20:25:59+03:00",
            BasinExplorerCatalog.LaunchKey(BasinExplorerKind.PeriodicCycles),
            BasinExplorerCatalog.LaunchKey(BasinExplorerKind.Muller),
            BasinExplorerCatalog.LaunchKey(BasinExplorerKind.Laguerre),
            BasinExplorerCatalog.LaunchKey(BasinExplorerKind.Secant),
            BasinExplorerCatalog.LaunchKey(BasinExplorerKind.RationalMap));

        // 45bfeb5
        Add("2026-09-16T10:38:59+03:00",
            BasinExplorerCatalog.LaunchKey(BasinExplorerKind.ComplexLogistic),
            BasinExplorerCatalog.LaunchKey(BasinExplorerKind.MagneticPendulum),
            BasinExplorerCatalog.LaunchKey(BasinExplorerKind.GravityCenters));

        // 652bc0b
        Add("2026-09-16T11:30:56+03:00",
            BasinExplorerCatalog.LaunchKey(BasinExplorerKind.GradientDescent),
            BasinExplorerCatalog.LaunchKey(BasinExplorerKind.ComplexGradientFlow),
            BasinExplorerCatalog.LaunchKey(BasinExplorerKind.PolynomialVectorField));

        // 653998d
        Add("2026-09-22T18:34:06+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.Juliabulb),
            Fractal3DCatalog.LaunchKey(Fractal3DKind.Mandelbulb),
            Fractal3DCatalog.LaunchKey(Fractal3DKind.Mandelbox),
            Fractal3DCatalog.LaunchKey(Fractal3DKind.MengerSponge),
            Fractal3DCatalog.LaunchKey(Fractal3DKind.QuaternionJulia),
            Fractal3DCatalog.LaunchKey(Fractal3DKind.SierpinskiTetrahedron));

        // 019d957
        Add("2026-09-23T13:27:00+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.ApollonianPacking));

        // 81bf5af
        Add("2026-09-23T14:03:59+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.Ifs3D));

        // 121d39d
        Add("2026-09-23T23:11:46+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.Vicsek),
            Fractal3DCatalog.LaunchKey(Fractal3DKind.CantorDust));

        // 4629b89
        Add("2026-09-24T13:27:36+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.Terrain));

        // 89b3d96
        Add("2026-09-25T14:28:17+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.BurningShip));

        // 27ec995
        Add("2026-09-25T14:51:32+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.BurningShipJulia));

        // d1f172c
        Add("2026-09-25T16:06:28+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.BulbBoxHybrid));

        // 4df901b
        Add("2026-09-25T23:51:28+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.Phoenix));

        // 6b55b1b
        Add("2026-09-26T00:22:19+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.StrangeAttractor));

        // 3092b1a
        Add("2026-09-26T01:07:54+03:00",
            "SprottQuadratic");

        // 9c33c2e
        Add("2026-09-30T14:02:58+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.Flame3D));

        // 7023035
        Add("2026-09-30T15:53:16+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.Kifs));

        // c12a1d8
        Add("2026-10-02T13:46:34+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.Dla3D));

        // c509dee
        Add("2026-10-02T14:39:16+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.LSystem3D));

        // 3f9577a
        Add("2026-10-03T23:35:18+03:00",
            "SymmetricIcon");

        // 6ae2ed3
        Add("2026-10-04T00:08:26+03:00",
            "Popcorn");

        // 6bd7535
        Add("2026-10-04T17:46:58+03:00",
            "SnowCrystal");

        // 05083f4
        Add("2026-10-04T18:38:32+03:00",
            "Hopalong");

        // 7f404d0
        Add("2026-10-04T19:45:24+03:00",
            "TuringPatterns");

        // c12321c
        Add("2026-10-04T21:04:49+03:00",
            Fractal3DCatalog.LaunchKey(Fractal3DKind.Buddhabrot4D));

        Add("2026-10-06T07:43:02+03:00", Fractal3DCatalog.LaunchKey(Fractal3DKind.GrayScott3D));
        Add("2026-10-06T19:50:00+03:00", Fractal3DCatalog.LaunchKey(Fractal3DKind.Turing3D));
        Add("2026-10-07T00:03:36+03:00", Fractal3DCatalog.LaunchKey(Fractal3DKind.CahnHilliard3D));
        return dates;

        void Add(string timestamp, params string[] launchKeys)
        {
            DateTimeOffset date = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture);
            foreach (string key in launchKeys) dates.Add(key, date);
        }
    }
}
