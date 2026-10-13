using System.Numerics;

namespace FractalExplorerWPF.Models;

/// <summary>
/// Трёхмерный режим окна <c>Fractal3DWindow</c>: всё, что зависит от пункта <see cref="Fractal3DKind"/>
/// и не касается разметки панелей и ресурсов Direct3D, — определение, признаки общих полей, подписи
/// источников цвета, стартовый и готовые виды, проверка параметров перед кадром, описание в менеджере
/// сохранений и общие слоты констант шейдера. Режим не хранит данных: состояние целиком лежит в
/// <see cref="Fractal3DState"/>, и формат JSON от режимов не зависит. Режимы перечислены в
/// <see cref="Fractal3DCatalog.Modes"/>; каждый — отдельный класс в <c>Models/Fractal3DModes/</c>.
/// </summary>
public abstract class Fractal3DMode(Fractal3DKind kind, Fractal3DDefinition definition)
{
    private static readonly IReadOnlyDictionary<Fractal3DColoringMode, string> NoReassignment =
        new Dictionary<Fractal3DColoringMode, string>();

    public Fractal3DKind Kind { get; } = kind;

    public Fractal3DDefinition Definition { get; } = definition;

    #region Общие поля окна

    /// <summary>Поле «Степень» имеет смысл для формулы.</summary>
    public virtual bool UsesPower => false;

    /// <summary>Формула берёт фиксированную константу C (Жюлиа-варианты, Феникс).</summary>
    public virtual bool UsesJuliaConstant => false;

    /// <summary>Формула с радиусом вылета: итерация убегает на бесконечность.</summary>
    public virtual bool UsesBailout => false;

    /// <summary>Общее поле «Итерации» (или число точек орбиты) имеет смысл для режима.</summary>
    public virtual bool UsesIterations => true;

    /// <summary>Кадр трассирует объём плотности (орбиты, облака, моделирования), а не поверхность по оценке расстояния.</summary>
    public virtual bool IsDensityVolume => false;

    /// <summary>Живое моделирование на ГП с паузой, шагами и точным полем в сохранении.</summary>
    public virtual bool IsGpuSimulation => false;

    /// <summary>Подпись, имя в сообщении об ошибке и диапазон общего поля «Итерации».</summary>
    public virtual Fractal3DField IterationsField { get; } = new("Итерации", "Итерации", 1, 64, IsInteger: true);

    /// <summary>Подпись и диапазон общего поля «Степень».</summary>
    public virtual Fractal3DField PowerField { get; } = new("Степень n", "Степень", -32, 32);

    /// <summary>Заголовок блока «Форма», если режим называет его по-своему.</summary>
    public virtual string? ShapeHeader => null;

    #endregion

    #region Окраска

    /// <summary>Есть ли у режима орбита итерации: ловушки, номер итерации и скорость убегания.</summary>
    protected virtual bool HasOrbit => true;

    /// <summary>
    /// Орбитальные источники цвета, переназначенные на величины режима (поле моделирования, параметр,
    /// номер кольца…), с подписями. Если таблица не пуста, из орбитальных источников доступны только
    /// переназначенные: остальным у режима нечего показать.
    /// </summary>
    protected virtual IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring => NoReassignment;

    /// <summary>
    /// Источник цвета имеет смысл для режима: у объёмов плотности нет орбит и убегания, у моделирований
    /// источники орбитальной ловушки и осей переназначены на величины поля.
    /// </summary>
    public bool HasColoringMode(Fractal3DColoringMode mode)
    {
        if (mode is not (Fractal3DColoringMode.OrbitTrap or Fractal3DColoringMode.CrossTrap or
            Fractal3DColoringMode.IterationIndex or Fractal3DColoringMode.Escape)) return true;
        return ReassignedColoring.Count > 0 ? ReassignedColoring.ContainsKey(mode) : HasOrbit;
    }

    /// <summary>Подпись источника цвета: своя у переназначенных, общая у остальных.</summary>
    public string ColoringModeName(Fractal3DColoringMode mode) =>
        ReassignedColoring.TryGetValue(mode, out string? name) ? name : Fractal3DCatalog.ColoringModeName(mode);

    #endregion

    #region Стартовый и готовые виды

    /// <summary>Состояние по умолчанию; оно же — превью пункта каталога.</summary>
    public Fractal3DState CreateDefaultState()
    {
        var state = new Fractal3DState
        {
            Kind = Kind,
            SaveName = Definition.Title,
            // У обычной поверхности цвет материала не меняется при облёте и приближении.
            // Режимы с содержательной окраской задают свой источник в ConfigureDefault; DTO
            // сохраняет прежнее значение по умолчанию для совместимости со старыми JSON.
            ColoringMode = Fractal3DColoringMode.Material,
            Palette = Fractal3DPalettes.Classic()
        };
        ConfigureDefault(state);
        return state;
    }

    /// <summary>Стартовый вид режима поверх общих умолчаний. При изменении — новая версия превью в <c>CatalogPreviewCache</c>.</summary>
    protected abstract void ConfigureDefault(Fractal3DState state);

    /// <summary>Готовые виды режима; они же — точки интереса менеджера сохранений. Каждый вызов строит новые состояния.</summary>
    public abstract IReadOnlyList<Fractal3DState> CreatePresets();

    /// <summary>Готовый вид: стартовое состояние режима с изменениями поверх него.</summary>
    protected Fractal3DState Preset(string name, Action<Fractal3DState> configure)
    {
        Fractal3DState state = CreateDefaultState();
        state.SaveName = name;
        configure(state);
        return state;
    }

    #endregion

    #region Кадр

    /// <summary>Приводит параметры режима к допустимым перед расчётом кадра.</summary>
    public virtual void Validate(Fractal3DState state)
    {
    }

    /// <summary>
    /// Что режим кладёт в общие слоты формы буфера констант кадра. По умолчанию — степень,
    /// константа C и инверсия Мандельбокса: так читают их формулы с оценкой расстояния.
    /// </summary>
    public virtual Fractal3DShapeConstants ShapeConstants(Fractal3DState state) =>
        new(new Vector3((float)state.Power, 0f, 0f), JuliaConstant(state), 0, BoxInversion(state));

    /// <summary>
    /// Сколько пикселей шейдер режима успевает за одну полосу, если это число постоянно; null —
    /// бюджет полосы считается по шагам луча и итерациям.
    /// </summary>
    public virtual int? StripPixelBudget => null;

    /// <summary>Во сколько раз пиксель режима дороже обычной формулы при расчёте бюджета полосы.</summary>
    public virtual double StripCostFactor => 1;

    protected static Vector4 JuliaConstant(Fractal3DState state) =>
        new((float)state.JuliaCX, (float)state.JuliaCY, (float)state.JuliaCZ, (float)state.JuliaCW);

    protected static Vector4 BoxInversion(Fractal3DState state) => new(
        (float)(Enum.IsDefined(state.BoxInversionShape) ? state.BoxInversionShape : BoxInversionShape.Sphere),
        (float)Math.Clamp(state.BoxInversionStretch, 0.5, 2),
        (float)Math.Clamp(state.BoxInversionPower, 2, 16), 0);

    #endregion

    #region Менеджер сохранений

    /// <summary>Описание вида в менеджере сохранений; <paramref name="prefix"/> — дата сохранения или «Точка интереса».</summary>
    public virtual string Describe(Fractal3DState state, string prefix) =>
        $"{prefix} · {IterationsField.Name}: {state.Iterations} · {ShapeSummary(state)}\n" +
        $"Камера: азимут {state.CameraYaw:F1}°, наклон {state.CameraPitch:F1}°" +
        (Math.Abs(state.CameraRoll) >= 0.05 ? $", крен {state.CameraRoll:F1}°" : "") +
        $", расстояние {state.CameraDistance:G6}\n" +
        $"Цель: {state.TargetX:G5}; {state.TargetY:G5}; {state.TargetZ:G5} · " +
        $"Окраска: {ColoringModeName(state.ColoringMode)} · Шагов: {state.MaxSteps}";

    /// <summary>Строка параметров формы в описании сохранения.</summary>
    protected virtual string ShapeSummary(Fractal3DState state) => $"Степень: {state.Power:G6}";

    #endregion
}

/// <summary>Числовое поле панели: подпись над полем, имя в сообщении об ошибке и допустимый диапазон.</summary>
public sealed record Fractal3DField(string Label, string Name, double Minimum, double Maximum, bool IsInteger = false);

/// <summary>
/// Слоты формы в буфере констант кадра, которые заполняет режим: ShapeA.xyz (ShapeA.w — радиус
/// вылета), ShapeB, ShapeC.w (ShapeC.xyz — срез кватерниона и масштаб со сдвигом окраски) и
/// BoxInversion. Шейдер режима читает их по тому же договору.
/// </summary>
public readonly record struct Fractal3DShapeConstants(Vector3 ShapeA, Vector4 ShapeB, float ShapeCW, Vector4 BoxInversion);
