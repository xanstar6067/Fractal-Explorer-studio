using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Theming;

public static class ThemeManager
{
    public const string DefaultThemeId = "dark-modern-lab-green";
    public const double NonTextUiContrastRatio = 3.0;
    public const double HighVisibilityInteractiveContrastRatio = 4.5;

    private static readonly List<ThemeDefinition> BuiltInThemes =
    [
        Create("light", "Светлая", true, (245,246,248), (233,235,240), (255,255,255), (34,36,42), (84,92,106), (196,203,216), (176,185,201), (222,226,234), (205,211,223), (176,185,201), (158,168,188)),
        Create("light-warm", "Тёплая", true, (255,249,238), (255,242,214), (255,252,244), (79,57,24), (130,99,57), (236,214,174), (220,183,122), (255,234,194), (247,214,162), (220,183,122), (207,165,102)),
        Create("dark-modern-lab-blue", "Тёмная (синяя)", true, (18,18,18), (28,30,36), (36,40,48), (236,240,245), (168,176,188), (38,132,255), (111,86,205), (54,61,74), (72,80,96), (64,74,92), (82,95,118)),
        Create(DefaultThemeId, "Тёмная (зелёная)", true, (18,18,18), (24,34,28), (34,48,39), (236,242,236), (170,188,172), (76,175,80), (139,195,74), (48,66,54), (61,83,68), (72,98,79), (90,118,98)),
        Create("dark-modern-lab-violet", "Тёмная (фиолетовая)", true, (18,18,18), (30,26,38), (42,36,52), (236,240,245), (176,168,188), (111,86,205), (38,132,255), (60,50,76), (78,65,95), (88,74,110), (105,90,132)),
        Create("light-fire", "Огненная", true, (255,246,238), (255,227,204), (255,250,243), (88,45,21), (140,85,56), (235,194,165), (224,157,119), (255,216,184), (248,193,152), (224,157,119), (212,139,102)),
        Create("light-violet", "Фиолетовая", true, (248,244,255), (235,226,250), (252,248,255), (62,46,96), (102,84,142), (210,196,233), (179,163,214), (224,214,244), (208,196,233), (179,163,214), (160,142,199))
    ];

    private static readonly Dictionary<string, ThemeDefinition> BuiltInById =
        BuiltInThemes.ToDictionary(theme => theme.Id, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ThemeDefinition> CustomById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> LegacyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DarkModernLabBlue"] = "dark-modern-lab-blue",
        ["DarkModernLabViolet"] = "dark-modern-lab-violet",
        ["Light"] = "light",
        ["DarkModernLabGreen"] = DefaultThemeId,
        ["LightWarm"] = "light-warm",
        ["LightFire"] = "light-fire",
        ["LightViolet"] = "light-violet"
    };

    private static bool _initialized;

    public static event EventHandler? ThemeChanged;
    public static event EventHandler? ThemesChanged;

    public static string CurrentThemeId { get; private set; } = DefaultThemeId;
    public static ThemeDefinition CurrentDefinition => TryGetTheme(CurrentThemeId, out ThemeDefinition theme)
        ? theme : BuiltInById[DefaultThemeId];

    public static string? InitializationWarning { get; private set; }

    public static void Initialize(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (_initialized) return;
        _initialized = true;

        try
        {
            foreach (ThemeDefinition theme in ThemeStorage.LoadCustomThemes())
            {
                if (!BuiltInById.ContainsKey(theme.Id))
                    CustomById[theme.Id] = theme.CloneWith(theme.Id, theme.DisplayName, false);
            }
        }
        catch (Exception exception)
        {
            InitializationWarning = $"Пользовательские темы не удалось загрузить: {exception.Message}";
        }

        string? selectedId = null;
        try { selectedId = ThemeStorage.LoadSelectedThemeId(); }
        catch (Exception exception)
        {
            InitializationWarning = string.Join(Environment.NewLine,
                new[] { InitializationWarning, $"Выбранную тему не удалось прочитать: {exception.Message}" }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        if (TryResolveThemeId(selectedId, out string resolved)) CurrentThemeId = resolved;
        ApplyResources(application, CurrentDefinition);

        // Class handler covers every current and future Window subclass. A migrated module cannot
        // silently retain the operating-system palette just because its author forgot registration.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAnyWindowLoaded), true);
    }

    public static IReadOnlyList<ThemeDefinition> GetAllThemes() =>
        BuiltInThemes.Concat(CustomById.Values.OrderBy(theme => theme.DisplayName,
            StringComparer.CurrentCultureIgnoreCase)).ToList();

    public static bool TryGetTheme(string? id, out ThemeDefinition theme)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            if (CustomById.TryGetValue(id, out theme!) || BuiltInById.TryGetValue(id, out theme!))
                return true;
            if (LegacyNames.TryGetValue(id, out string? mapped))
                return TryGetTheme(mapped, out theme);
        }

        theme = BuiltInById[DefaultThemeId];
        return false;
    }

    public static bool TryResolveThemeId(string? value, out string resolvedId)
    {
        if (TryGetTheme(value, out ThemeDefinition theme))
        {
            resolvedId = theme.Id;
            return !string.IsNullOrWhiteSpace(value);
        }
        resolvedId = DefaultThemeId;
        return false;
    }

    public static void SetTheme(string id)
    {
        if (!TryGetTheme(id, out ThemeDefinition theme)) return;
        bool changed = !string.Equals(CurrentThemeId, theme.Id, StringComparison.OrdinalIgnoreCase);
        CurrentThemeId = theme.Id;
        if (Application.Current is { } application) ApplyResources(application, theme);
        ThemeStorage.SaveSelectedThemeId(theme.Id);
        if (changed) ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void AddOrUpdateCustomTheme(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (string.IsNullOrWhiteSpace(theme.Id)) throw new ArgumentException("Theme id is required.", nameof(theme));
        if (BuiltInById.ContainsKey(theme.Id)) throw new InvalidOperationException("Встроенную тему нельзя перезаписать.");

        ThemeDefinition custom = theme.CloneWith(theme.Id, theme.DisplayName.Trim(), false);
        CustomById[custom.Id] = custom;
        SaveCustomThemes();
        ThemesChanged?.Invoke(null, EventArgs.Empty);
        if (string.Equals(CurrentThemeId, custom.Id, StringComparison.OrdinalIgnoreCase))
        {
            if (Application.Current is { } application) ApplyResources(application, custom);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static ThemeDefinition DuplicateTheme(string sourceId, string id, string displayName)
    {
        if (!TryGetTheme(sourceId, out ThemeDefinition source))
            throw new KeyNotFoundException($"Тема '{sourceId}' не найдена.");
        ThemeDefinition duplicate = source.CloneWith(id, displayName, false);
        AddOrUpdateCustomTheme(duplicate);
        return duplicate;
    }

    public static bool RemoveCustomTheme(string id)
    {
        if (!CustomById.Remove(id)) return false;
        bool removedCurrent = string.Equals(CurrentThemeId, id, StringComparison.OrdinalIgnoreCase);
        SaveCustomThemes();
        ThemesChanged?.Invoke(null, EventArgs.Empty);
        if (removedCurrent) SetTheme(DefaultThemeId);
        return true;
    }

    public static (Color Normal, Color Hover) GetInteractiveStateColors(ThemeDefinition theme, Color background)
    {
        Color normal = IsSpecified(theme.InteractiveBorderNormal) ? theme.InteractiveBorderNormal : theme.BorderColor;
        Color hover = theme.HighVisibilityInteractiveStates
            ? (IsSpecified(theme.HighVisibilityInteractiveHover) ? theme.HighVisibilityInteractiveHover : Color.FromRgb(255, 214, 0))
            : (IsSpecified(theme.InteractiveBorderHover) ? theme.InteractiveBorderHover : theme.AccentPrimary);
        double minimum = theme.HighVisibilityInteractiveStates ? HighVisibilityInteractiveContrastRatio : NonTextUiContrastRatio;
        return (EnsureContrast(normal, background, NonTextUiContrastRatio), EnsureContrast(hover, background, minimum));
    }

    public static Color GetInteractiveBorderColor(ThemeDefinition theme, Color background, bool hovered)
    {
        (Color normal, Color hover) = GetInteractiveStateColors(theme, background);
        return hovered ? hover : normal;
    }

    public static Color ResolveTextOn(Color background, Color preferred)
    {
        if (Contrast(preferred, background) >= 4.5) return preferred;
        return Contrast(Colors.White, background) >= Contrast(Colors.Black, background) ? Colors.White : Colors.Black;
    }

    private static void OnAnyWindowLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Window window || ThemeContract.GetExclude(window)) return;
        window.SetResourceReference(Control.BackgroundProperty, "Theme.BaseBackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty, "Theme.PrimaryTextBrush");
        ThemeContract.ScheduleAudit(window);
    }

    internal static void ApplyPreviewResources(ResourceDictionary resources, ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(theme);
        ApplyResources(resources, theme);
    }

    private static void ApplyResources(Application application, ThemeDefinition theme) =>
        ApplyResources(application.Resources, theme);

    private static void ApplyResources(ResourceDictionary resources, ThemeDefinition theme)
    {
        (Color interactive, Color interactiveHover) = GetInteractiveStateColors(theme, theme.ControlBackground);
        SetBrush(resources, "Theme.BaseBackgroundBrush", theme.BaseBackground);
        SetBrush(resources, "Theme.PanelBackgroundBrush", theme.PanelBackground);
        SetBrush(resources, "Theme.ControlBackgroundBrush", theme.ControlBackground);
        SetBrush(resources, "Theme.ControlBackgroundAltBrush", Blend(theme.ControlBackground, theme.PanelBackground, .45));
        SetBrush(resources, "Theme.PrimaryTextBrush", theme.PrimaryText);
        SetBrush(resources, "Theme.SecondaryTextBrush", theme.SecondaryText);
        SetBrush(resources, "Theme.DisabledTextBrush", Blend(theme.SecondaryText, theme.BaseBackground, .50));
        SetBrush(resources, "Theme.AccentPrimaryBrush", theme.AccentPrimary);
        SetBrush(resources, "Theme.AccentSecondaryBrush", theme.AccentSecondary);
        SetBrush(resources, "Theme.AccentForegroundBrush", ResolveTextOn(theme.AccentPrimary, theme.PrimaryText));
        SetBrush(resources, "Theme.HoverBackgroundBrush", theme.HoverBackground);
        SetBrush(resources, "Theme.PressedBackgroundBrush", theme.PressedBackground);
        SetBrush(resources, "Theme.BorderBrush", theme.BorderColor);
        SetBrush(resources, "Theme.InputBorderBrush", theme.InputBorderColor);
        SetBrush(resources, "Theme.InteractiveBorderBrush", interactive);
        SetBrush(resources, "Theme.InteractiveHoverBrush", interactiveHover);
        SetBrush(resources, "Theme.FocusBrush", interactiveHover);
        SetBrush(resources, "Theme.ScrollTrackBrush", Blend(theme.PanelBackground, theme.BaseBackground, .40));
        SetBrush(resources, "Theme.ScrollThumbBrush", Blend(theme.BorderColor, theme.SecondaryText, .42));
        SetBrush(resources, "Theme.ScrollThumbHoverBrush", EnsureContrast(theme.AccentPrimary, theme.PanelBackground, NonTextUiContrastRatio));
        SetBrush(resources, "Theme.ScrollThumbPressedBrush", EnsureContrast(theme.AccentSecondary, theme.PanelBackground, NonTextUiContrastRatio));
        SetStatusBrushes(resources, "Success", Color.FromRgb(76, 184, 96), theme);
        SetStatusBrushes(resources, "Warning", Color.FromRgb(222, 162, 38), theme);
        SetStatusBrushes(resources, "Danger", Color.FromRgb(232, 92, 80), theme);
        SetStatusBrushes(resources, "Info", Color.FromRgb(74, 150, 240), theme);
    }

    // Фиксированный оттенок состояния с текстовым контрастом на мягкой подложке; подложка — смесь с фоном панели.
    private static void SetStatusBrushes(ResourceDictionary resources, string name, Color hue, ThemeDefinition theme)
    {
        Color soft = Blend(hue, theme.PanelBackground, .82);
        SetBrush(resources, $"Theme.{name}SoftBrush", soft);
        SetBrush(resources, $"Theme.{name}Brush", EnsureContrast(hue, soft, HighVisibilityInteractiveContrastRatio));
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    private static void SaveCustomThemes() => ThemeStorage.SaveCustomThemes(CustomById.Values);

    private static ThemeDefinition Create(string id, string name, bool builtIn,
        (byte R, byte G, byte B) baseBg, (byte R, byte G, byte B) panel,
        (byte R, byte G, byte B) control, (byte R, byte G, byte B) primary,
        (byte R, byte G, byte B) secondary, (byte R, byte G, byte B) accent,
        (byte R, byte G, byte B) accent2, (byte R, byte G, byte B) hover,
        (byte R, byte G, byte B) pressed, (byte R, byte G, byte B) border,
        (byte R, byte G, byte B) input) => new()
        {
            Id = id, DisplayName = name, IsBuiltIn = builtIn,
            BaseBackground = ToColor(baseBg), PanelBackground = ToColor(panel),
            ControlBackground = ToColor(control), PrimaryText = ToColor(primary),
            SecondaryText = ToColor(secondary), AccentPrimary = ToColor(accent),
            AccentSecondary = ToColor(accent2), HoverBackground = ToColor(hover),
            PressedBackground = ToColor(pressed), BorderColor = ToColor(border),
            InputBorderColor = ToColor(input)
        };

    private static Color ToColor((byte R, byte G, byte B) value) => Color.FromRgb(value.R, value.G, value.B);
    private static bool IsSpecified(Color color) => color.A != 0 || color.R != 0 || color.G != 0 || color.B != 0;

    private static Color Blend(Color first, Color second, double weight)
    {
        weight = Math.Clamp(weight, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(first.R + (second.R - first.R) * weight),
            (byte)Math.Round(first.G + (second.G - first.G) * weight),
            (byte)Math.Round(first.B + (second.B - first.B) * weight));
    }

    private static Color EnsureContrast(Color color, Color background, double minimum)
    {
        if (Contrast(color, background) >= minimum) return color;
        Color target = Luminance(background) > .5 ? Colors.Black : Colors.White;
        for (int step = 1; step <= 20; step++)
        {
            Color candidate = Blend(color, target, step / 20d);
            if (Contrast(candidate, background) >= minimum) return candidate;
        }
        return target;
    }

    private static double Contrast(Color first, Color second)
    {
        double lighter = Math.Max(Luminance(first), Luminance(second));
        double darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + .05) / (darker + .05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            double channel = value / 255d;
            return channel <= .03928 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }
}
