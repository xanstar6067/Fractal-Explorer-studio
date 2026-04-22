using FractalExplorer.Properties;

namespace FractalExplorer.Utilities.Theme
{
    public static class ThemeManager
    {
        public const double NonTextUiContrastRatio = 3.0d;
        public const double InteractiveStateMinDifferenceContrastRatio = 1.35d;
        public const double HighVisibilityInteractiveContrastRatio = 4.5d;

        private static readonly Color HighVisibilityNormalDark = Color.FromArgb(24, 24, 24);
        private static readonly Color HighVisibilityHoverFallback = Color.FromArgb(255, 214, 0);

        private const string PreserveBackColorTag = "preserve-backcolor";
        public const string DefaultThemeId = "dark-modern-lab-green";

        private static readonly List<ThemeDefinition> BuiltInThemes = new()
{
    new ThemeDefinition
    {
        Id = "light",
        DisplayName = "Светлая",
        IsBuiltIn = true,
        BaseBackground = Color.FromArgb(245, 246, 248),
        PanelBackground = Color.FromArgb(233, 235, 240),
        ControlBackground = Color.FromArgb(255, 255, 255),
        PrimaryText = Color.FromArgb(34, 36, 42),
        SecondaryText = Color.FromArgb(84, 92, 106),
        AccentPrimary = Color.FromArgb(196, 203, 216),
        AccentSecondary = Color.FromArgb(176, 185, 201),
        HoverBackground = Color.FromArgb(222, 226, 234),
        PressedBackground = Color.FromArgb(205, 211, 223),
        BorderColor = Color.FromArgb(176, 185, 201),
        InputBorderColor = Color.FromArgb(158, 168, 188)
    },
    new ThemeDefinition
    {
        Id = "light-warm",
        DisplayName = "Тёплая",
        IsBuiltIn = true,
        BaseBackground = Color.FromArgb(255, 249, 238),
        PanelBackground = Color.FromArgb(255, 242, 214),
        ControlBackground = Color.FromArgb(255, 252, 244),
        PrimaryText = Color.FromArgb(79, 57, 24),
        SecondaryText = Color.FromArgb(130, 99, 57),
        AccentPrimary = Color.FromArgb(236, 214, 174),
        AccentSecondary = Color.FromArgb(220, 183, 122),
        HoverBackground = Color.FromArgb(255, 234, 194),
        PressedBackground = Color.FromArgb(247, 214, 162),
        BorderColor = Color.FromArgb(220, 183, 122),
        InputBorderColor = Color.FromArgb(207, 165, 102)
    },
    new ThemeDefinition
    {
        Id = "dark-modern-lab-blue",
        DisplayName = "Тёмная (синяя)",
        IsBuiltIn = true,
        BaseBackground = Color.FromArgb(18, 18, 18),
        PanelBackground = Color.FromArgb(28, 30, 36),
        ControlBackground = Color.FromArgb(36, 40, 48),
        PrimaryText = Color.FromArgb(236, 240, 245),
        SecondaryText = Color.FromArgb(168, 176, 188),
        AccentPrimary = Color.FromArgb(38, 132, 255),
        AccentSecondary = Color.FromArgb(111, 86, 205),
        HoverBackground = Color.FromArgb(54, 61, 74),
        PressedBackground = Color.FromArgb(72, 80, 96),
        BorderColor = Color.FromArgb(64, 74, 92),
        InputBorderColor = Color.FromArgb(82, 95, 118)
    },
    new ThemeDefinition
    {
        Id = DefaultThemeId,
        DisplayName = "Тёмная (зелёная)",
        IsBuiltIn = true,
        BaseBackground = Color.FromArgb(18, 18, 18),
        PanelBackground = Color.FromArgb(24, 34, 28),
        ControlBackground = Color.FromArgb(34, 48, 39),
        PrimaryText = Color.FromArgb(236, 242, 236),
        SecondaryText = Color.FromArgb(170, 188, 172),
        AccentPrimary = Color.FromArgb(76, 175, 80),
        AccentSecondary = Color.FromArgb(139, 195, 74),
        HoverBackground = Color.FromArgb(48, 66, 54),
        PressedBackground = Color.FromArgb(61, 83, 68),
        BorderColor = Color.FromArgb(72, 98, 79),
        InputBorderColor = Color.FromArgb(90, 118, 98)
    },
    new ThemeDefinition
    {
        Id = "dark-modern-lab-violet",
        DisplayName = "Тёмная (фиолетовая)",
        IsBuiltIn = true,
        BaseBackground = Color.FromArgb(18, 18, 18),
        PanelBackground = Color.FromArgb(30, 26, 38),
        ControlBackground = Color.FromArgb(42, 36, 52),
        PrimaryText = Color.FromArgb(236, 240, 245),
        SecondaryText = Color.FromArgb(176, 168, 188),
        AccentPrimary = Color.FromArgb(111, 86, 205),
        AccentSecondary = Color.FromArgb(38, 132, 255),
        HoverBackground = Color.FromArgb(60, 50, 76),
        PressedBackground = Color.FromArgb(78, 65, 95),
        BorderColor = Color.FromArgb(88, 74, 110),
        InputBorderColor = Color.FromArgb(105, 90, 132)
    },
    new ThemeDefinition
    {
        Id = "light-fire",
        DisplayName = "Огненная",
        IsBuiltIn = true,
        BaseBackground = Color.FromArgb(255, 246, 238),
        PanelBackground = Color.FromArgb(255, 227, 204),
        ControlBackground = Color.FromArgb(255, 250, 243),
        PrimaryText = Color.FromArgb(88, 45, 21),
        SecondaryText = Color.FromArgb(140, 85, 56),
        AccentPrimary = Color.FromArgb(235, 194, 165),
        AccentSecondary = Color.FromArgb(224, 157, 119),
        HoverBackground = Color.FromArgb(255, 216, 184),
        PressedBackground = Color.FromArgb(248, 193, 152),
        BorderColor = Color.FromArgb(224, 157, 119),
        InputBorderColor = Color.FromArgb(212, 139, 102)
    },
    new ThemeDefinition
    {
        Id = "light-violet",
        DisplayName = "Фиолетовая",
        IsBuiltIn = true,
        BaseBackground = Color.FromArgb(248, 244, 255),
        PanelBackground = Color.FromArgb(235, 226, 250),
        ControlBackground = Color.FromArgb(252, 248, 255),
        PrimaryText = Color.FromArgb(62, 46, 96),
        SecondaryText = Color.FromArgb(102, 84, 142),
        AccentPrimary = Color.FromArgb(210, 196, 233),
        AccentSecondary = Color.FromArgb(179, 163, 214),
        HoverBackground = Color.FromArgb(224, 214, 244),
        PressedBackground = Color.FromArgb(208, 196, 233),
        BorderColor = Color.FromArgb(179, 163, 214),
        InputBorderColor = Color.FromArgb(160, 142, 199)
    }
};

        private static readonly Dictionary<string, ThemeDefinition> BuiltInThemesById = BuiltInThemes.ToDictionary(
            theme => theme.Id,
            theme => theme,
            StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, ThemeDefinition> CustomThemesById = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Control, ControlEventHandler> ControlAddedHandlers = new();
        private static readonly Dictionary<ComboBox, DrawItemEventHandler> ComboBoxDrawHandlers = new();
        private static readonly Dictionary<Button, EventHandler> ButtonRefreshHandlers = new();
        private static readonly Dictionary<CheckBox, PaintEventHandler> CheckBoxPaintHandlers = new();
        private static readonly Dictionary<CheckBox, EventHandler> CheckBoxRefreshHandlers = new();

        private static readonly Dictionary<string, string> LegacyThemeNameMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DarkModernLabBlue"] = "dark-modern-lab-blue",
            ["DarkModernLabViolet"] = "dark-modern-lab-violet",
            ["Light"] = "light",
            ["DarkModernLabGreen"] = DefaultThemeId,
            ["LightWarm"] = "light-warm",
            ["LightFire"] = "light-fire",
            ["LightViolet"] = "light-violet"
        };

        public static event EventHandler? ThemeChanged;
        public static event EventHandler? ThemesChanged;

        public static string CurrentThemeId { get; private set; } = DefaultThemeId;

        static ThemeManager()
        {
            try
            {
                foreach (ThemeDefinition customTheme in ThemeStorage.LoadCustomThemes())
                {
                    if (string.IsNullOrWhiteSpace(customTheme.Id) || BuiltInThemesById.ContainsKey(customTheme.Id))
                    {
                        continue;
                    }

                    CustomThemesById[customTheme.Id] = customTheme.CloneWith(customTheme.Id, customTheme.DisplayName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки пользовательских тем: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static ThemeDefinition CurrentDefinition =>
            TryGetTheme(CurrentThemeId, out ThemeDefinition theme)
                ? theme
                : BuiltInThemesById[DefaultThemeId];

        public static IReadOnlyList<ThemeDefinition> GetAllThemes()
        {
            List<ThemeDefinition> result = new(BuiltInThemes.Count + CustomThemesById.Count);
            result.AddRange(BuiltInThemes);
            result.AddRange(CustomThemesById.Values.OrderBy(theme => theme.DisplayName, StringComparer.CurrentCultureIgnoreCase));
            return result;
        }

        public static bool TryGetTheme(string id, out ThemeDefinition theme)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                if (CustomThemesById.TryGetValue(id, out theme))
                {
                    return true;
                }

                if (BuiltInThemesById.TryGetValue(id, out theme))
                {
                    return true;
                }

                if (TryMapLegacyThemeName(id, out string mappedId))
                {
                    return TryGetTheme(mappedId, out theme);
                }
            }

            theme = BuiltInThemesById[DefaultThemeId];
            return false;
        }

        public static bool TryResolveThemeId(string? rawValue, out string resolvedThemeId)
        {
            if (!string.IsNullOrWhiteSpace(rawValue) && TryGetTheme(rawValue, out ThemeDefinition theme))
            {
                resolvedThemeId = theme.Id;
                return true;
            }

            resolvedThemeId = DefaultThemeId;
            return false;
        }

        public static string GetThemeIdFromSettingsDefault()
        {
            object? defaultValue = Settings.Default.Properties[nameof(Settings.UiTheme)]?.DefaultValue;
            string? configuredTheme = defaultValue as string;

            TryResolveThemeId(configuredTheme, out string resolvedThemeId);
            return resolvedThemeId;
        }

        public static void SetTheme(string id)
        {
            if (!TryGetTheme(id, out ThemeDefinition theme))
            {
                return;
            }

            if (string.Equals(CurrentThemeId, theme.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CurrentThemeId = theme.Id;

            foreach (Form form in Application.OpenForms)
            {
                ApplyTheme(form);
            }

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void AddOrUpdateCustomTheme(ThemeDefinition theme)
        {
            if (theme is null)
            {
                throw new ArgumentNullException(nameof(theme));
            }

            if (string.IsNullOrWhiteSpace(theme.Id))
            {
                throw new ArgumentException("Theme id must be provided.", nameof(theme));
            }

            if (BuiltInThemesById.ContainsKey(theme.Id))
            {
                throw new InvalidOperationException("Cannot override built-in theme.");
            }

            ThemeDefinition customTheme = theme.CloneWith(theme.Id, theme.DisplayName, false);
            CustomThemesById[customTheme.Id] = customTheme;
            SaveCustomThemes();
            ThemesChanged?.Invoke(null, EventArgs.Empty);

            if (string.Equals(CurrentThemeId, customTheme.Id, StringComparison.OrdinalIgnoreCase))
            {
                ThemeChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static bool RemoveCustomTheme(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !CustomThemesById.Remove(id))
            {
                return false;
            }

            SaveCustomThemes();
            ThemesChanged?.Invoke(null, EventArgs.Empty);

            if (string.Equals(CurrentThemeId, id, StringComparison.OrdinalIgnoreCase))
            {
                SetTheme(GetThemeIdFromSettingsDefault());
            }

            return true;
        }

        public static ThemeDefinition DuplicateTheme(string sourceId, string newId, string newDisplayName)
        {
            if (!TryGetTheme(sourceId, out ThemeDefinition sourceTheme))
            {
                throw new KeyNotFoundException($"Theme with id '{sourceId}' was not found.");
            }

            ThemeDefinition duplicate = sourceTheme.CloneWith(newId, newDisplayName, false);
            AddOrUpdateCustomTheme(duplicate);
            return duplicate;
        }

        public static void RegisterForm(Form form)
        {
            ApplyTheme(form);
            RegisterDynamicControlTracking(form);

            EventHandler handler = (_, _) => ApplyTheme(form);
            ThemeChanged += handler;

            form.Disposed += (_, _) => ThemeChanged -= handler;
        }

        public static Color GetInteractiveStateColor(Color background, bool hovered)
        {
            (Color normal, Color hover) = GetInteractiveStateColors(background);
            return hovered ? hover : normal;
        }

        public static Color GetInteractiveStateColor(ThemeDefinition theme, Color background, bool hovered)
        {
            (Color normal, Color hover) = GetInteractiveStateColors(theme, background);
            return hovered ? hover : normal;
        }

        public static (Color Normal, Color Hover) GetInteractiveStateColors(Color background)
        {
            return GetInteractiveStateColors(CurrentDefinition, background);
        }

        public static (Color Normal, Color Hover) GetInteractiveStateColors(ThemeDefinition theme, Color background)
        {
            ArgumentNullException.ThrowIfNull(theme);

            if (theme.HighVisibilityInteractiveStates)
            {
                return GetHighVisibilityInteractiveStateColors(theme, background);
            }

            bool hasNormal = !theme.InteractiveBorderNormal.IsEmpty;
            bool hasHover = !theme.InteractiveBorderHover.IsEmpty;
            if (hasNormal && hasHover)
            {
                Color tokenNormal = EnsureMinimumContrast(theme.InteractiveBorderNormal, background, NonTextUiContrastRatio);
                Color tokenHover = EnsureMinimumContrast(theme.InteractiveBorderHover, background, NonTextUiContrastRatio);
                if (CalculateContrastRatio(tokenNormal, tokenHover) >= InteractiveStateMinDifferenceContrastRatio)
                {
                    return (tokenNormal, tokenHover);
                }
            }

            return GetAutoInteractiveStateColors(theme, background);
        }

        private static (Color Normal, Color Hover) GetHighVisibilityInteractiveStateColors(ThemeDefinition theme, Color background)
        {
            Color highVisibilityHoverBase = theme.HighVisibilityInteractiveHover.IsEmpty
                ? HighVisibilityHoverFallback
                : theme.HighVisibilityInteractiveHover;

            Color hover = EnsureMinimumContrast(highVisibilityHoverBase, background, HighVisibilityInteractiveContrastRatio);
            if (CalculateContrastRatio(hover, background) < HighVisibilityInteractiveContrastRatio)
            {
                hover = EnsureMinimumContrast(Color.White, background, HighVisibilityInteractiveContrastRatio);
            }

            Color normal = EnsureMinimumContrast(HighVisibilityNormalDark, background, NonTextUiContrastRatio);
            if (CalculateContrastRatio(normal, background) < NonTextUiContrastRatio)
            {
                normal = EnsureMinimumContrast(Color.Black, background, NonTextUiContrastRatio);
            }

            if (CalculateContrastRatio(normal, hover) < InteractiveStateMinDifferenceContrastRatio)
            {
                normal = MixColors(normal, background, 0.3f);
                normal = EnsureMinimumContrast(normal, background, NonTextUiContrastRatio);
            }

            return (normal, hover);
        }

        private static (Color Normal, Color Hover) GetAutoInteractiveStateColors(ThemeDefinition theme, Color background)
        {
            Color hover = GetAccessibleAccentOn(theme, background, NonTextUiContrastRatio);
            Color normal = MixColors(hover, background, 0.35f);
            double stateDifference = CalculateContrastRatio(normal, hover);
            if (stateDifference >= InteractiveStateMinDifferenceContrastRatio)
            {
                return (normal, hover);
            }

            double backgroundLuminance = CalculateRelativeLuminance(background);
            Color hoverTarget = backgroundLuminance < 0.5d ? Color.White : Color.Black;

            for (int iteration = 0; iteration < 8 && stateDifference < InteractiveStateMinDifferenceContrastRatio; iteration++)
            {
                normal = MixColors(normal, background, 0.45f);
                stateDifference = CalculateContrastRatio(normal, hover);
                if (stateDifference >= InteractiveStateMinDifferenceContrastRatio)
                {
                    break;
                }

                hover = MixColors(hover, hoverTarget, 0.22f);
                hover = EnsureMinimumContrast(hover, background, NonTextUiContrastRatio);
                stateDifference = CalculateContrastRatio(normal, hover);
            }

            return (normal, hover);
        }

        public static Color GetInteractiveBorderColor(Color background, bool hovered)
        {
            return GetInteractiveStateColor(background, hovered);
        }

        public static Color GetInteractiveBorderColor(ThemeDefinition theme, Color background, bool hovered)
        {
            return GetInteractiveStateColor(theme, background, hovered);
        }

        public static Color GetAccessibleAccentOn(Color background, double minContrast = NonTextUiContrastRatio)
        {
            return GetAccessibleAccentOn(CurrentDefinition, background, minContrast);
        }

        public static Color GetAccessibleAccentOn(ThemeDefinition theme, Color background, double minContrast = NonTextUiContrastRatio)
        {
            ArgumentNullException.ThrowIfNull(theme);

            double backgroundLuminance = CalculateRelativeLuminance(background);
            Color accentAdjusted = backgroundLuminance < 0.5d
                ? MixColors(theme.AccentPrimary, Color.White, 0.38f)
                : MixColors(theme.AccentPrimary, Color.Black, 0.42f);

            return EnsureMinimumContrast(accentAdjusted, background, minContrast);
        }

        public static void ApplyTheme(Form form)
        {
            var theme = CurrentDefinition;
            form.SuspendLayout();
            form.BackColor = theme.BaseBackground;
            form.ForeColor = theme.PrimaryText;

            ApplyThemeToControl(form, theme);

            form.ResumeLayout(true);
            form.Invalidate(true);
        }


        private static void SaveCustomThemes()
        {
            try
            {
                ThemeStorage.SaveCustomThemes(CustomThemesById.Values);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения пользовательских тем: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool TryMapLegacyThemeName(string rawThemeName, out string themeId)
        {
            return LegacyThemeNameMap.TryGetValue(rawThemeName, out themeId!);
        }

        private static void ApplyThemeToControl(Control control, ThemeDefinition theme)
        {
            switch (control)
            {
                case Form themedForm:
                    themedForm.BackColor = theme.BaseBackground;
                    themedForm.ForeColor = theme.PrimaryText;
                    break;
                case Button button:
                    StyleButton(button, theme);
                    break;
                case Label label:
                    StyleLabel(label, theme);
                    break;
                case TableLayoutPanel tableLayoutPanel:
                    StyleTableLayoutPanel(tableLayoutPanel, theme);
                    break;
                case Panel panel:
                    StylePanel(panel, theme);
                    break;
                case TextBox textBox:
                    StyleTextBox(textBox, theme);
                    break;
                case ComboBox comboBox:
                    StyleComboBox(comboBox, theme);
                    break;
                case NumericUpDown numericUpDown:
                    StyleNumericUpDown(numericUpDown, theme);
                    break;
                case ListBox listBox:
                    StyleListBox(listBox, theme);
                    break;
                case GroupBox groupBox:
                    StyleGroupBox(groupBox, theme);
                    break;
                case SplitContainer splitContainer:
                    StyleSplitContainer(splitContainer, theme);
                    break;
                case TrackBar trackBar:
                    StyleTrackBar(trackBar, theme);
                    break;
                case RichTextBox richTextBox:
                    StyleRichTextBox(richTextBox, theme);
                    break;
                case TreeView treeView:
                    StyleTreeView(treeView, theme);
                    break;
                case ProgressBar progressBar:
                    StyleProgressBar(progressBar, theme);
                    break;
                case CheckBox checkBox:
                    StyleCheckBox(checkBox, theme);
                    break;
                default:
                    if (control is not PictureBox)
                    {
                        if (!ShouldSkipBackColorTheming(control))
                        {
                            control.BackColor = theme.PanelBackground;
                        }

                        control.ForeColor = theme.PrimaryText;
                    }
                    break;
            }

            foreach (Control child in control.Controls)
            {
                ApplyThemeToControl(child, theme);
            }
        }

        private static void RegisterDynamicControlTracking(Control control)
        {
            if (ControlAddedHandlers.ContainsKey(control))
            {
                return;
            }

            ControlEventHandler handler = (_, args) =>
            {
                ApplyThemeToControl(args.Control, CurrentDefinition);
                RegisterDynamicControlTracking(args.Control);
            };

            ControlAddedHandlers[control] = handler;
            control.ControlAdded += handler;
            control.Disposed += (_, _) => UnregisterDynamicControlTracking(control);

            foreach (Control child in control.Controls)
            {
                RegisterDynamicControlTracking(child);
            }
        }

        private static void UnregisterDynamicControlTracking(Control control)
        {
            if (ControlAddedHandlers.TryGetValue(control, out ControlEventHandler? handler))
            {
                control.ControlAdded -= handler;
                ControlAddedHandlers.Remove(control);
            }
        }

        private static void StyleButton(Button button, ThemeDefinition theme)
        {
            bool isSecondaryAction = IsSecondaryButton(button);
            AttachButtonRefreshHandler(button);

            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = theme.HoverBackground;
            button.FlatAppearance.MouseDownBackColor = theme.PressedBackground;

            if (button.Enabled)
            {
                button.FlatAppearance.BorderColor = theme.BorderColor;
                button.BackColor = isSecondaryAction ? theme.AccentSecondary : theme.AccentPrimary;
                button.ForeColor = ResolveButtonTextColor(theme, button.BackColor);
            }
            else
            {
                button.FlatAppearance.BorderColor = MixColors(theme.BorderColor, theme.PanelBackground, 0.45f);
                button.BackColor = MixColors(theme.ControlBackground, theme.PanelBackground, 0.35f);
                button.ForeColor = MixColors(theme.PrimaryText, theme.PanelBackground, 0.55f);
            }

            button.Invalidate();
        }

        private static Color ResolveButtonTextColor(ThemeDefinition theme, Color buttonBackground)
        {
            if (!IsWindowsImportedTheme(theme))
            {
                return theme.PrimaryText;
            }

            const double minimumTextContrast = 4.5d;
            if (CalculateContrastRatio(theme.PrimaryText, buttonBackground) >= minimumTextContrast)
            {
                return theme.PrimaryText;
            }

            double whiteContrast = CalculateContrastRatio(Color.White, buttonBackground);
            double blackContrast = CalculateContrastRatio(Color.Black, buttonBackground);
            return whiteContrast >= blackContrast ? Color.White : Color.Black;
        }

        private static bool IsWindowsImportedTheme(ThemeDefinition theme)
        {
            return theme.Id.StartsWith("windows-system", StringComparison.OrdinalIgnoreCase);
        }

        private static void AttachButtonRefreshHandler(Button button)
        {
            if (ButtonRefreshHandlers.ContainsKey(button))
            {
                return;
            }

            EventHandler refreshHandler = (_, _) =>
            {
                StyleButton(button, CurrentDefinition);
                button.Invalidate();
            };

            ButtonRefreshHandlers[button] = refreshHandler;
            button.EnabledChanged += refreshHandler;
            button.Disposed += (_, _) => UnregisterButtonRefreshHandler(button);
        }

        private static void UnregisterButtonRefreshHandler(Button button)
        {
            if (!ButtonRefreshHandlers.TryGetValue(button, out EventHandler? refreshHandler))
            {
                return;
            }

            button.EnabledChanged -= refreshHandler;
            ButtonRefreshHandlers.Remove(button);
        }

        private static void StyleLabel(Label label, ThemeDefinition theme)
        {
            label.BackColor = Color.Transparent;
            label.ForeColor = theme.SecondaryText;
        }

        private static void StyleTableLayoutPanel(TableLayoutPanel tableLayoutPanel, ThemeDefinition theme)
        {
            tableLayoutPanel.BackColor = theme.PanelBackground;
            tableLayoutPanel.ForeColor = theme.PrimaryText;
        }

        private static void StylePanel(Panel panel, ThemeDefinition theme)
        {
            if (!ShouldSkipBackColorTheming(panel))
            {
                panel.BackColor = theme.PanelBackground;
            }

            panel.ForeColor = theme.PrimaryText;
        }

        private static bool ShouldSkipBackColorTheming(Control control)
        {
            if (control.Tag is string stringTag)
            {
                return string.Equals(stringTag, PreserveBackColorTag, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static void StyleTextBox(TextBox textBox, ThemeDefinition theme)
        {
            textBox.BackColor = theme.ControlBackground;
            textBox.ForeColor = theme.PrimaryText;

            // WinForms TextBox не поддерживает отдельную настройку цвета стандартной рамки.
            // Из-за платформенного ограничения InputBorderColor не может быть применён к TextBox.
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void StyleComboBox(ComboBox comboBox, ThemeDefinition theme)
        {
            comboBox.BackColor = theme.ControlBackground;
            comboBox.ForeColor = theme.PrimaryText;
            comboBox.FlatStyle = FlatStyle.Flat;

            if (comboBox.DropDownStyle == ComboBoxStyle.DropDownList)
            {
                comboBox.DrawMode = DrawMode.OwnerDrawFixed;

                if (!ComboBoxDrawHandlers.TryGetValue(comboBox, out DrawItemEventHandler? drawHandler))
                {
                    drawHandler = (_, args) => DrawComboBoxItem(comboBox, args, CurrentDefinition);
                    ComboBoxDrawHandlers[comboBox] = drawHandler;
                    comboBox.DrawItem += drawHandler;
                    comboBox.Disposed += (_, _) => UnregisterComboBoxDrawHandler(comboBox);
                }

                comboBox.Invalidate();
                return;
            }

            UnregisterComboBoxDrawHandler(comboBox);
            comboBox.DrawMode = DrawMode.Normal;
        }

        private static void StyleNumericUpDown(NumericUpDown numericUpDown, ThemeDefinition theme)
        {
            numericUpDown.BackColor = theme.ControlBackground;
            numericUpDown.ForeColor = theme.PrimaryText;
            numericUpDown.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void StyleListBox(ListBox listBox, ThemeDefinition theme)
        {
            listBox.BackColor = theme.ControlBackground;
            listBox.ForeColor = theme.PrimaryText;
            listBox.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void StyleGroupBox(GroupBox groupBox, ThemeDefinition theme)
        {
            groupBox.BackColor = theme.PanelBackground;
            groupBox.ForeColor = theme.SecondaryText;
        }

        private static void StyleSplitContainer(SplitContainer splitContainer, ThemeDefinition theme)
        {
            splitContainer.BackColor = theme.PanelBackground;
            splitContainer.ForeColor = theme.PrimaryText;

            splitContainer.Panel1.BackColor = theme.PanelBackground;
            splitContainer.Panel1.ForeColor = theme.PrimaryText;
            splitContainer.Panel2.BackColor = theme.PanelBackground;
            splitContainer.Panel2.ForeColor = theme.PrimaryText;
        }

        private static void StyleTrackBar(TrackBar trackBar, ThemeDefinition theme)
        {
            trackBar.BackColor = theme.PanelBackground;
            trackBar.ForeColor = theme.PrimaryText;
        }

        private static void StyleRichTextBox(RichTextBox richTextBox, ThemeDefinition theme)
        {
            richTextBox.BackColor = theme.ControlBackground;
            richTextBox.ForeColor = theme.SecondaryText;
            richTextBox.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void StyleTreeView(TreeView treeView, ThemeDefinition theme)
        {
            treeView.BackColor = theme.PanelBackground;
            treeView.ForeColor = theme.PrimaryText;
            treeView.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void StyleProgressBar(ProgressBar progressBar, ThemeDefinition theme)
        {
            progressBar.BackColor = theme.PanelBackground;
            progressBar.ForeColor = theme.AccentPrimary;
        }

        private static void StyleCheckBox(CheckBox checkBox, ThemeDefinition theme)
        {
            checkBox.BackColor = theme.PanelBackground;
            checkBox.ForeColor = theme.PrimaryText;

            AttachDisabledCheckBoxTextRenderer(checkBox);
            checkBox.Invalidate();
        }

        private static void AttachDisabledCheckBoxTextRenderer(CheckBox checkBox)
        {
            if (!CheckBoxPaintHandlers.TryGetValue(checkBox, out PaintEventHandler? paintHandler))
            {
                paintHandler = (_, args) => DrawCheckBoxDisabledTextOverride(checkBox, args, CurrentDefinition);
                CheckBoxPaintHandlers[checkBox] = paintHandler;
                checkBox.Paint += paintHandler;
                checkBox.Disposed += (_, _) => UnregisterCheckBoxHandlers(checkBox);
            }

            if (!CheckBoxRefreshHandlers.TryGetValue(checkBox, out EventHandler? refreshHandler))
            {
                refreshHandler = (_, _) => checkBox.Invalidate();
                CheckBoxRefreshHandlers[checkBox] = refreshHandler;
                checkBox.EnabledChanged += refreshHandler;
                checkBox.TextChanged += refreshHandler;
                checkBox.AppearanceChanged += refreshHandler;
                checkBox.SizeChanged += refreshHandler;
                checkBox.RightToLeftChanged += refreshHandler;
                checkBox.FontChanged += refreshHandler;
            }
        }

        private static void DrawCheckBoxDisabledTextOverride(CheckBox checkBox, PaintEventArgs args, ThemeDefinition theme)
        {
            if (checkBox.Enabled || string.IsNullOrEmpty(checkBox.Text))
            {
                return;
            }

            Color disabledTextColor = MixColors(theme.PrimaryText, theme.PanelBackground, 0.58f);
            Rectangle textBounds = GetCheckBoxTextBounds(checkBox, args.Graphics);
            if (textBounds.Width <= 0 || textBounds.Height <= 0)
            {
                return;
            }

            using SolidBrush backgroundBrush = new(checkBox.BackColor);
            args.Graphics.FillRectangle(backgroundBrush, textBounds);

            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (checkBox.RightToLeft == RightToLeft.Yes)
            {
                flags |= TextFormatFlags.RightToLeft;
            }

            if (IsCheckGlyphOnRight(checkBox.CheckAlign))
            {
                flags |= TextFormatFlags.Right;
            }
            else
            {
                flags |= TextFormatFlags.Left;
            }

            TextRenderer.DrawText(args.Graphics, checkBox.Text, checkBox.Font, textBounds, disabledTextColor, flags);
        }

        private static Rectangle GetCheckBoxTextBounds(CheckBox checkBox, Graphics graphics)
        {
            Size glyphSize = CheckBoxRenderer.GetGlyphSize(graphics, System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal);
            int spacing = 4;
            Rectangle client = checkBox.ClientRectangle;

            if (IsCheckGlyphOnRight(checkBox.CheckAlign))
            {
                return new Rectangle(client.Left, client.Top, Math.Max(0, client.Width - glyphSize.Width - spacing), client.Height);
            }

            return new Rectangle(client.Left + glyphSize.Width + spacing, client.Top, Math.Max(0, client.Width - glyphSize.Width - spacing), client.Height);
        }

        private static bool IsCheckGlyphOnRight(ContentAlignment checkAlign)
        {
            return checkAlign == ContentAlignment.TopRight ||
                   checkAlign == ContentAlignment.MiddleRight ||
                   checkAlign == ContentAlignment.BottomRight;
        }

        private static void UnregisterCheckBoxHandlers(CheckBox checkBox)
        {
            if (CheckBoxPaintHandlers.TryGetValue(checkBox, out PaintEventHandler? paintHandler))
            {
                checkBox.Paint -= paintHandler;
                CheckBoxPaintHandlers.Remove(checkBox);
            }

            if (CheckBoxRefreshHandlers.TryGetValue(checkBox, out EventHandler? refreshHandler))
            {
                checkBox.EnabledChanged -= refreshHandler;
                checkBox.TextChanged -= refreshHandler;
                checkBox.AppearanceChanged -= refreshHandler;
                checkBox.SizeChanged -= refreshHandler;
                checkBox.RightToLeftChanged -= refreshHandler;
                checkBox.FontChanged -= refreshHandler;
                CheckBoxRefreshHandlers.Remove(checkBox);
            }
        }

        private static Color MixColors(Color source, Color target, float targetWeight)
        {
            float clampedWeight = targetWeight < 0f ? 0f : targetWeight > 1f ? 1f : targetWeight;
            float sourceWeight = 1f - clampedWeight;

            return Color.FromArgb(
                (int)Math.Round(source.R * sourceWeight + target.R * clampedWeight),
                (int)Math.Round(source.G * sourceWeight + target.G * clampedWeight),
                (int)Math.Round(source.B * sourceWeight + target.B * clampedWeight));
        }

        private static Color EnsureMinimumContrast(Color accent, Color background, double minContrast)
        {
            double contrast = CalculateContrastRatio(accent, background);
            if (contrast >= minContrast)
            {
                return accent;
            }

            double backgroundLuminance = CalculateRelativeLuminance(background);
            float adjustmentWeight = 0.55f;
            Color target = backgroundLuminance < 0.5d ? Color.White : Color.Black;
            Color adjustedAccent = accent;

            for (int iteration = 0; iteration < 8 && contrast < minContrast; iteration++)
            {
                adjustedAccent = MixColors(adjustedAccent, target, adjustmentWeight);
                contrast = CalculateContrastRatio(adjustedAccent, background);
            }

            return adjustedAccent;
        }

        private static double CalculateContrastRatio(Color first, Color second)
        {
            double firstLuminance = CalculateRelativeLuminance(first);
            double secondLuminance = CalculateRelativeLuminance(second);
            double lighter = Math.Max(firstLuminance, secondLuminance);
            double darker = Math.Min(firstLuminance, secondLuminance);
            return (lighter + 0.05d) / (darker + 0.05d);
        }

        private static double CalculateRelativeLuminance(Color color)
        {
            static double ConvertChannel(byte channel)
            {
                double normalized = channel / 255d;
                return normalized <= 0.03928d
                    ? normalized / 12.92d
                    : Math.Pow((normalized + 0.055d) / 1.055d, 2.4d);
            }

            double red = ConvertChannel(color.R);
            double green = ConvertChannel(color.G);
            double blue = ConvertChannel(color.B);
            return 0.2126d * red + 0.7152d * green + 0.0722d * blue;
        }

        private static void DrawComboBoxItem(ComboBox comboBox, DrawItemEventArgs args, ThemeDefinition theme)
        {
            if (args.Index < 0)
            {
                return;
            }

            bool isSelected = (args.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color backgroundColor = isSelected ? theme.HoverBackground : theme.ControlBackground;

            using SolidBrush backgroundBrush = new(backgroundColor);
            using SolidBrush textBrush = new(theme.PrimaryText);

            args.Graphics.FillRectangle(backgroundBrush, args.Bounds);

            string itemText = comboBox.GetItemText(comboBox.Items[args.Index]);
            args.Graphics.DrawString(itemText, args.Font ?? comboBox.Font, textBrush, args.Bounds);

            args.DrawFocusRectangle();
        }

        private static void UnregisterComboBoxDrawHandler(ComboBox comboBox)
        {
            if (!ComboBoxDrawHandlers.TryGetValue(comboBox, out DrawItemEventHandler? drawHandler))
            {
                return;
            }

            comboBox.DrawItem -= drawHandler;
            ComboBoxDrawHandlers.Remove(comboBox);
        }

        private static bool IsSecondaryButton(Button button)
        {
            if (button.DialogResult == DialogResult.Cancel)
            {
                return true;
            }

            string buttonName = button.Name ?? string.Empty;
            string buttonText = button.Text ?? string.Empty;

            if (ContainsAny(buttonName, "background") || ContainsAny(buttonText, "фон"))
            {
                return false;
            }

            return ContainsAny(buttonName, "close", "cancel", "delete", "remove", "back") ||
                   ContainsAny(buttonText, "закры", "отмен", "удал", "назад", "close", "cancel", "delete", "back");
        }

        private static bool ContainsAny(string value, params string[] markers)
        {
            foreach (string marker in markers)
            {
                if (value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
