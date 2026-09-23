using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

namespace FractalExplorerWPF.Infrastructure;

public static class SaveManagerConfigurations
{
    public static SaveManagerConfiguration<MathematicalLaboratoryState> ForMathematicalLaboratory(
        MathematicalLaboratoryWindow window, MathematicalLaboratorySaveStore store) => new()
    {
        WindowTitle = $"Сохранение/Загрузка: {window.LaboratoryTitle}",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = window.LoadState,
        RenderPreviewAsync = window.RenderStatePreviewAsync,
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = state =>
            $"{Prefix(state.Timestamp)} · Режим: {window.GetModeName(state.Mode)}\n" +
            $"{window.GetStateDetails(state)} · Масштаб: {state.Zoom:G5} · Поворот: {state.Rotation:G5}°"
    };

    public static SaveManagerConfiguration<MandelbrotState> ForMandelbrot(
        MandelbrotWindow window, MandelbrotSaveStore store) => new()
    {
        WindowTitle = $"Сохранение/Загрузка: {window.SaveManagerDisplayName}",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = window.LoadState,
        RenderPreviewAsync = window.RenderStatePreviewAsync,
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = DescribeMandelbrot,
        PointsOfInterest = PresetManager.GetMandelbrotPresets(window.SaveManagerVariant)
    };

    public static SaveManagerConfiguration<SerpinskySaveState> ForSerpinsky(
        SerpinskyWindow window, SerpinskySaveStore store) => new()
    {
        WindowTitle = "Сохранение/Загрузка: Серпинский",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = window.LoadState,
        RenderPreviewAsync = window.RenderStatePreviewAsync,
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = state => $"{Prefix(state.Timestamp)} · Режим: {state.RenderMode}\n" +
                                    $"Итерации: {state.Iterations} · Масштаб: {state.Zoom:0.####}",
        PointsOfInterest = PresetManager.GetSerpinskyPresets()
    };

    public static SaveManagerConfiguration<NewtonState> ForNewton(
        NewtonPoolsWindow window, NewtonSaveStore store) => new()
    {
        WindowTitle = "Сохранение/Загрузка: Бассейны Ньютона",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = window.LoadState,
        RenderPreviewAsync = window.RenderStatePreviewAsync,
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = DescribeNewton,
        PointsOfInterest = PresetManager.GetNewtonPresets()
    };

    public static SaveManagerConfiguration<BasinExplorerState> ForBasinExplorer(
        BasinExplorerWindow window, BasinExplorerSaveStore store) => new()
    {
        WindowTitle = $"Сохранение/Загрузка: {window.DisplayTitle}",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = window.LoadState,
        RenderPreviewAsync = window.RenderStatePreviewAsync,
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = DescribeBasinExplorer,
        PointsOfInterest = BasinExplorerCatalog.GetPresets(window.Kind)
    };

    public static SaveManagerConfiguration<NovaState> ForNova(
        NovaWindow window, NovaSaveStore store, NovaVariant variant) => new()
    {
        WindowTitle = $"Сохранение/Загрузка: {(variant == NovaVariant.Julia ? "Nova Julia" : "Nova Mandelbrot")}",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = window.LoadState,
        RenderPreviewAsync = window.RenderStatePreviewAsync,
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = DescribeNova,
        PointsOfInterest = PresetManager.GetNovaPresets(variant)
    };

    public static SaveManagerConfiguration<PhoenixState> ForPhoenix(
        PhoenixWindow window, PhoenixSaveStore store) => new()
    {
        WindowTitle = "Сохранение/Загрузка: Феникс",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = window.LoadState,
        RenderPreviewAsync = window.RenderStatePreviewAsync,
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = state => $"{Prefix(state.Timestamp)} · {state.PlaneMode} · {state.Variant} · a={state.PrimaryPower}, b={state.SecondaryPower}\n" +
                                    $"C1: {Complex(state.C1Real, state.C1Imaginary)} · C2: {Complex(state.C2Real, state.C2Imaginary)}",
        PointsOfInterest = PresetManager.GetPhoenixPresets()
    };

    public static SaveManagerConfiguration<CollatzState> ForCollatz(
        CollatzWindow window, CollatzSaveStore store) => new()
    {
        WindowTitle = "Сохранение/Загрузка: Коллатц",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = window.LoadState,
        RenderPreviewAsync = window.RenderStatePreviewAsync,
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = DescribeCollatz,
        PointsOfInterest = PresetManager.GetCollatzPresets()
    };

    public static SaveManagerConfiguration<InverseCollatzState> ForInverseCollatz(
        InverseCollatzTreeWindow window, InverseCollatzSaveStore store) => new()
    {
        WindowTitle = "Сохранение/Загрузка: Обратное дерево Коллатца",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = window.LoadState,
        RenderPreviewAsync = window.RenderStatePreviewAsync,
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = state => $"{Prefix(state.Timestamp)} · Глубина: {state.Depth} · " +
                                    $"Узлов до: {state.MaxNodes:N0}\n" +
                                    $"Раскладка: {(state.Layout == InverseCollatzLayout.Radial ? "радиальная" : "по уровням")} · " +
                                    $"Фильтр: {DescribeInverseCollatzFilter(state)} · Масштаб: {state.Zoom:G6}\n" +
                                    $"Палитра: {state.Palette.Name} · " +
                                    (state.Palette.Mapping == InverseCollatzPaletteMapping.RepeatByLevel
                                        ? $"цикл через {state.Palette.LevelsPerCycle} уровней"
                                        : "растяжение по глубине")
    };

    public static SaveManagerConfiguration<DomainColoringState> ForDomainColoring(
        DomainColoringWindow window, DomainColoringSaveStore store) => new()
    {
        WindowTitle = "Сохранение/Загрузка: Domain Coloring",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = window.LoadState,
        RenderPreviewAsync = window.RenderStatePreviewAsync,
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = state => $"{Prefix(state.Timestamp)} · {DomainColoringModeName(state.ColoringMode)} · " +
                                    $"Масштаб: {state.Zoom:G8}\n" +
                                    $"f(z) = {state.Formula}\n" +
                                    $"Центр: {state.CenterX:G8}; {state.CenterY:G8} · " +
                                    $"Оттенок: {state.HueCycles:G6} об. · Насыщенность: {state.Saturation:G6}"
    };

    public static SaveManagerConfiguration<BuddhabrotState> ForBuddhabrot(
        BuddhabrotWindow window, BuddhabrotSaveStore store) => new()
    {
        WindowTitle = "Сохранение/Загрузка: Буддаброт",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = window.LoadState,
        RenderPreviewAsync = window.RenderStatePreviewAsync,
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = state => $"{Prefix(state.Timestamp)} · {state.RenderMode} · {state.SampleCount:N0} сэмплов\n" +
                                    $"Итерации: {state.MaxIterations} · Палитра: {state.Palette.Name}",
        PointsOfInterest = PresetManager.GetBuddhabrotPresets()
    };

    public static SaveManagerConfiguration<FlameState> ForFlame(
        FlameWindow window, FlameSaveStore store) => new()
    {
        WindowTitle = "Сохранение/Загрузка: Flame",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = state => window.LoadState(state.Clone()),
        RenderPreviewAsync = (state, width, height, token, progress) => window.RenderStatePreviewAsync(state.Clone(), width, height, token, progress),
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = state => $"{Prefix(state.Timestamp)} · {state.Samples:N0} сэмплов · {state.IterationsPerSample} итераций\n" +
                                    $"Трансформаций: {state.Transforms.Count} · Экспозиция: {state.Exposure:F2} · Гамма: {state.Gamma:F2}",
        PointsOfInterest = PresetManager.GetFlamePresets()
    };

    public static SaveManagerConfiguration<IfsState> ForIfs(
        IfsWindow window, IfsSaveStore store) => new()
    {
        WindowTitle = "Сохранение/Загрузка: IFS",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = state => window.LoadState(state.Clone()),
        RenderPreviewAsync = (state, width, height, token, progress) => window.RenderStatePreviewAsync(state.Clone(), width, height, token, progress),
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = state => $"{Prefix(state.Timestamp)} · {state.Iterations:N0} итераций · Масштаб: {state.Scale:F4}\n" +
                                    $"Преобразований: {state.Transforms.Count}",
        PointsOfInterest = PresetManager.GetIfsPresets()
    };

    public static SaveManagerConfiguration<ApollonianState> ForApollonian(
        ApollonianWindow window, ApollonianSaveStore store) => new()
    {
        WindowTitle = "Сохранение/Загрузка: Аполлонова прокладка",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = state => window.LoadState(state.Clone()),
        RenderPreviewAsync = (state, width, height, token, progress) =>
            window.RenderStatePreviewAsync(state.Clone(), width, height, token, progress),
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = state => $"{Prefix(state.Timestamp)} · глубина {state.MaxDepth} · до {state.MaxCircles:N0} окружностей\n" +
                                    $"Раскраска: {state.ColoringMode} · вид: {state.DrawMode} · ширина: {state.ViewWidth:G6}"
    };

    public static SaveManagerConfiguration<DlaState> ForDla(
        DlaWindow window, DlaSaveStore store) => new()
    {
        WindowTitle = "Сохранение/Загрузка: DLA",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = state => window.LoadState(state.Clone()),
        RenderPreviewAsync = (state, width, height, token, progress) =>
            window.RenderStatePreviewAsync(state.Clone(), width, height, token, progress),
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = state => $"{Prefix(state.Timestamp)} · {state.ParticleCount:N0} частиц · сетка {state.GridSize}×{state.GridSize}\n" +
                                    $"Затравка: {state.SeedMode} · прилипание: {state.Stickiness:G4} · seed: {state.RandomSeed}"
    };

    public static SaveManagerConfiguration<GrayScottState> ForGrayScott(
        GrayScottWindow window, GrayScottSaveStore store) => new()
    {
        WindowTitle = "Сохранение/Загрузка: Gray–Scott",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = state => window.LoadState(state.Clone()),
        RenderPreviewAsync = (state, width, height, token, progress) =>
            window.RenderStatePreviewAsync(state.Clone(), width, height, token, progress),
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = state => $"{Prefix(state.Timestamp)} · сетка {state.GridSize}×{state.GridSize} · {state.StepsPerFrame} шагов/кадр\n" +
                                    $"F={state.Feed:G6} · K={state.Kill:G6} · Du={state.DiffusionU:G5} · Dv={state.DiffusionV:G5} · палитра: {state.Palette.Name}"
    };

    public static SaveManagerConfiguration<Fractal3DState> ForFractal3D(
        Fractal3DWindow window, Fractal3DSaveStore store) => new()
    {
        WindowTitle = $"Сохранение/Загрузка: {window.DisplayTitle}",
        Store = store,
        CaptureState = window.CaptureState,
        CapturePreview = window.CaptureCurrentPreview,
        LoadState = state => window.LoadState(state.Clone()),
        RenderPreviewAsync = (state, width, height, token, progress) =>
            window.RenderStatePreviewAsync(state.Clone(), width, height, token, progress),
        GetName = state => state.SaveName,
        GetTimestamp = state => state.Timestamp,
        GetDetails = DescribeFractal3D,
        PointsOfInterest = Fractal3DCatalog.GetPresets(window.Kind)
    };

    private static string DescribeFractal3D(Fractal3DState state)
    {
        if (state.Kind == Fractal3DKind.Ifs3D)
            return $"{Prefix(state.Timestamp)} · {state.Iterations:N0} точек · " +
                   $"{state.IfsTransforms.Count} преобразований\n" +
                   $"Камера: азимут {state.CameraYaw:F1}°, наклон {state.CameraPitch:F1}°, " +
                   $"крен {state.CameraRoll:F1}°, расстояние {state.CameraDistance:G6}";
        string shape = state.Kind switch
        {
            Fractal3DKind.Mandelbox =>
                $"Масштаб: {state.BoxScale:G6} · Мин. радиус: {state.BoxMinRadius:G6} · Свёртка: {state.BoxFoldingLimit:G6}",
            Fractal3DKind.SierpinskiTetrahedron => $"Масштаб складывания: {state.SierpinskiScale:G6}",
            Fractal3DKind.MengerSponge => "Рекурсивные тоннели куба",
            Fractal3DKind.ApollonianPacking => "Взаимно касающиеся сферы",
            Fractal3DKind.QuaternionJulia =>
                $"C: {state.JuliaCX:G5}; {state.JuliaCY:G5}; {state.JuliaCZ:G5}; {state.JuliaCW:G5} · срез w: {state.QuaternionSlice:G5}",
            Fractal3DKind.Juliabulb =>
                $"Степень: {state.Power:G6} · C: {state.JuliaCX:G5}; {state.JuliaCY:G5}; {state.JuliaCZ:G5}",
            _ => $"Степень: {state.Power:G6}"
        };
        string iterationLabel = state.Kind == Fractal3DKind.ApollonianPacking ? "Поколения сфер" : "Итерации";
        return $"{Prefix(state.Timestamp)} · {iterationLabel}: {state.Iterations} · {shape}\n" +
               $"Камера: азимут {state.CameraYaw:F1}°, наклон {state.CameraPitch:F1}°" +
               (Math.Abs(state.CameraRoll) >= 0.05 ? $", крен {state.CameraRoll:F1}°" : "") +
               $", расстояние {state.CameraDistance:G6}\n" +
               $"Цель: {state.TargetX:G5}; {state.TargetY:G5}; {state.TargetZ:G5} · " +
               $"Окраска: {Fractal3DCatalog.ColoringModeName(state.ColoringMode, state.Kind)} · Шагов: {state.MaxSteps}";
    }

    private static string DescribeMandelbrot(MandelbrotState state)
    {
        string details = $"{Prefix(state.Timestamp)} · Итерации: {state.Iterations} · Масштаб: {state.Zoom:G6}\n" +
                         $"Центр: {state.CenterX:G8}; {state.CenterY:G8} · Палитра: {state.PaletteName} · {state.ColoringMode}";
        if (state.Variant is MandelbrotVariant.Julia or MandelbrotVariant.JuliaBurningShip)
            details += $"\nКонстанта C: {Complex(state.JuliaCReal, state.JuliaCImaginary)}";
        return details;
    }

    private static string DescribeNova(NovaState state)
    {
        string details = $"{Prefix(state.Timestamp)} · Итерации: {state.Iterations} · Масштаб: {state.Zoom:G8}\n" +
                         $"P: {Complex(state.PReal, state.PImaginary)} · Z₀: {Complex(state.Z0Real, state.Z0Imaginary)} · m: {state.M:G8}";
        return state.Variant == NovaVariant.Julia ? details + $"\nC: {Complex(state.CReal, state.CImaginary)}" : details;
    }

    private static string DescribeNewton(NewtonState state)
    {
        string details = $"{Prefix(state.Timestamp)} · Метод: {state.IterationMethod} · Итерации: {state.MaxIterations}\n" +
                         $"Формула: {state.Formula} · Масштаб: {state.Zoom:G8}\n" +
                         $"Корней: {state.Roots.Count} · Точность: {state.RootTolerance:G3} · Поиск: {state.RootSearchMode}";
        if (state.IterationMethod == NewtonIterationMethod.RelaxedNewton)
        {
            details += state.RelaxedPlaneMode == NewtonRelaxedPlaneMode.LambdaPlane
                ? $"\nПлоскость λ · z₀: {FormatComplex(state.FixedInitialZ)}"
                : $"\nПлоскость z · λ: {FormatComplex(state.Relaxation)}";
        }
        if (state.DiagnosticColoringMode != NewtonDiagnosticColoringMode.Disabled)
            details += $"\nДиагностика: {state.DiagnosticColoringMode}";
        return details;
    }

    private static string DescribeBasinExplorer(BasinExplorerState state)
    {
        if (BasinExplorerCatalog.UsesPhysics(state.Kind))
            return $"{Prefix(state.Timestamp)} · Центров: {state.Physics.Centers.Count} · Масштаб: {state.Zoom:G6}\n" +
                $"Трение: {state.Physics.Damping:G4} · Высота: {state.Physics.Height:G4} · Шаг: {state.Physics.TimeStep:G4}\n" +
                $"Время: {state.Physics.MaxTime:G4} · Лимит шагов: {state.MaxIterations} · Скорость: {FormatComplex(state.Physics.InitialVelocity)}\n" +
                $"Захват: {(state.Physics.CaptureMode == PhysicalCaptureMode.Absorb ? "поглощение" : "после успокоения")} · Раскраска: {state.ColoringMode}";
        if (BasinExplorerCatalog.UsesPlanar(state.Kind))
            return $"{Prefix(state.Timestamp)} · Масштаб: {state.Zoom:G6} · Аттракторов: {state.PlanarAttractors.Count}\n" +
                (state.Kind == BasinExplorerKind.GradientDescent ? $"V = {state.Planar.Potential}\n{state.Planar.Optimizer} · α = {state.Planar.LearningRate:G5}" :
                 state.Kind == BasinExplorerKind.ComplexGradientFlow ? $"V = ½|{state.Formula}|²" : $"x′ = {state.Planar.FieldX}\ny′ = {state.Planar.FieldY}") +
                $"\nЛимит шагов: {state.MaxIterations} · Допуск: {state.Planar.ConvergenceTolerance:G4} · Раскраска: {state.ColoringMode}";
        if (state.Kind == BasinExplorerKind.ComplexLogistic)
            return $"{Prefix(state.Timestamp)} · z → λz(1−z) · Масштаб: {state.Zoom:G6}\n" +
                (state.LogisticPlane == LogisticPlaneMode.Parameter ? $"Плоскость λ · z₀ = {FormatComplex(state.LogisticSeed)}" : $"Плоскость z₀ · λ = {FormatComplex(state.ParameterC)}") +
                $"\nМакс. период: {state.MaxPeriod} · Итерации: {state.MaxIterations} · Раскраска: {state.ColoringMode}";
        string formula = state.Kind == BasinExplorerKind.RationalMap
            ? $"R(z) = ({state.Numerator}) / ({state.Denominator})"
            : $"f(z) = {state.Formula}";
        string details = $"{Prefix(state.Timestamp)} · Итерации: {state.MaxIterations} · Масштаб: {state.Zoom:G6}\n{formula}";
        if (!BasinExplorerCatalog.UsesRoots(state.Kind))
        {
            int cycles = state.Attractors.Count(attractor => !attractor.IsInfinity);
            return details + $" · c = {FormatComplex(state.ParameterC)}\n" +
                   $"Циклов: {(state.UseSavedAttractors ? cycles.ToString() : "ищутся при загрузке")} · " +
                   $"Макс. период: {state.MaxPeriod} · Допуск: {state.CycleTolerance:G3} · Раскраска: {state.ColoringMode}";
        }

        details += $"\nКорней: {state.Roots.Count} · Точность: {state.RootTolerance:G3} · Раскраска: {state.ColoringMode}";
        return state.Kind switch
        {
            BasinExplorerKind.Muller => details + $"\nТройка: {state.MullerSeedMode} · " + (state.MullerSeedMode == MullerSeedMode.FixedAnchors
                ? $"a = {FormatComplex(state.MullerAnchorA)}, b = {FormatComplex(state.MullerAnchorB)}"
                : $"h = {FormatComplex(state.MullerOffset)}"),
            BasinExplorerKind.Laguerre => details + $"\nn = {(state.LaguerreAutoDegree ? "степень полинома" : state.LaguerreDegree.ToString("G6"))} · Показ: {state.LaguerreComparison}",
            _ => details + $"\nПлоскость: {state.SecantPlaneMode}" + state.SecantPlaneMode switch
            {
                SecantPlaneMode.FixedFirstPoint => $" · x₀ = {FormatComplex(state.SecantFirstPoint)}",
                SecantPlaneMode.OffsetPair => $" · h = {FormatComplex(state.SecantOffset)}",
                _ => $" · оси {state.SecantHorizontalAxis}/{state.SecantVerticalAxis}"
            }
        };
    }

    private static string DescribeCollatz(CollatzState state)
    {
        string details = $"{Prefix(state.Timestamp)} · Режим: {VariationName(state.Variation)} · " +
                         $"Окрашивание: {CollatzColoringName(state.ColoringMode)}\n" +
                         $"Итерации: {state.Iterations} · " +
                         $"Порог: {state.Threshold:G8} · Масштаб: {state.Zoom:G8}";
        if (state.Variation is CollatzVariation.ParityBranchVariation or
            CollatzVariation.GeneralizedP or CollatzVariation.GeneralizedPQ)
            details += $" · p: {state.PParameter:G8}";
        if (state.Variation == CollatzVariation.GeneralizedPQ)
            details += $" · q: {Complex(state.QRealParameter, state.QImaginaryParameter)}";
        return details + $"\n{CollatzColoringDetails(state)} · {CollatzInteriorFillDetails(state)}";
    }

    private static string CollatzColoringName(CollatzColoringMode mode) => mode switch
    {
        CollatzColoringMode.FinalArgument => "Final Argument",
        CollatzColoringMode.FinalMagnitude => "Final Magnitude",
        CollatzColoringMode.CycleBasins => "Cycle Basins",
        CollatzColoringMode.IntegerTrap => "Integer Trap",
        CollatzColoringMode.RealAxisTrap => "Real Axis Trap",
        CollatzColoringMode.OrbitDensity => "Orbit Density",
        CollatzColoringMode.PeriodDetection => "Period Detection",
        _ => "Escape Time"
    };

    private static string CollatzColoringDetails(CollatzState state) => state.ColoringMode switch
    {
        CollatzColoringMode.FinalArgument => $"Обороты аргумента: {state.ArgumentCycles:G6}",
        CollatzColoringMode.FinalMagnitude => $"Масштаб модуля: {state.MagnitudeScale:G6}",
        CollatzColoringMode.CycleBasins or CollatzColoringMode.PeriodDetection =>
            $"Допуск цикла: {state.CycleTolerance:G3} · Макс. период: {state.MaximumDetectedPeriod}",
        CollatzColoringMode.IntegerTrap or CollatzColoringMode.RealAxisTrap =>
            $"Чувствительность ловушки: {state.TrapScale:G6}",
        CollatzColoringMode.OrbitDensity =>
            $"Экспозиция: {state.OrbitDensityExposure:G6} · Шаг: {state.OrbitDensitySampleStep} · " +
            (state.OrbitDensityEscapedOnly ? "только вышедшие" : "все орбиты"),
        _ => state.UseSmoothColoring ? "Плавный Escape Time" : "Дискретный Escape Time"
    };

    private static string CollatzInteriorFillDetails(CollatzState state) => state.InteriorFillMode switch
    {
        CollatzInteriorFillMode.Auto => "Внутри: авто",
        CollatzInteriorFillMode.Black => "Внутри: чёрный",
        CollatzInteriorFillMode.White => "Внутри: белый",
        CollatzInteriorFillMode.Custom => $"Внутри: {state.CustomInteriorColor}",
        _ => "Внутри: по режиму"
    };

    private static string DescribeInverseCollatzFilter(InverseCollatzState state)
    {
        if (state.Modulus <= 0) return "выключен";
        string residue = state.Residue < 0 ? "все остатки" : $"r = {state.Residue}";
        string behavior = state.FilterBehavior == InverseCollatzFilterBehavior.OnlyMatching
            ? "только совпавшие" : "подсветка";
        return $"mod {state.Modulus}, {residue}, {behavior}";
    }

    private static string DomainColoringModeName(DomainColoringMode mode) => mode switch
    {
        DomainColoringMode.LogarithmicRings => "Логарифмические кольца",
        DomainColoringMode.PhaseContours => "Фазовые линии",
        DomainColoringMode.PolarGrid => "Полярная сетка",
        DomainColoringMode.ArgumentOnly => "Только аргумент",
        _ => "Плавный модуль"
    };

    private static string Prefix(DateTime timestamp) => timestamp == DateTime.MinValue ? "Точка интереса" : timestamp.ToString("g");

    private static string Complex(decimal real, decimal imaginary) =>
        $"{real:G8} {(imaginary < 0 ? '−' : '+')} {Math.Abs(imaginary):G8}i";

    private static string FormatComplex(System.Numerics.Complex value) =>
        $"{value.Real:G8} {(value.Imaginary < 0 ? '−' : '+')} {Math.Abs(value.Imaginary):G8}i";

    private static string VariationName(CollatzVariation variation) => variation switch
    {
        CollatzVariation.SineVariation => "Синусная (арт)",
        CollatzVariation.ParityBranchVariation => "Ветви 1 / (p−1)n (арт)",
        CollatzVariation.GeneralizedP => "Обобщённая Cₚ",
        CollatzVariation.GeneralizedPQ => "Семейство C(p,q)",
        _ => "Стандартная"
    };
}
