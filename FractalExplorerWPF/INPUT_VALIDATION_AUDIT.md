# Аудит ввода: старый кадр остаётся, новый рендер не запускается

Дата: 19 октября 2026. База: `4b4de5e`. Проверена WPF-версия. Поведение приложения при проведении этого аудита не изменялось.

Последующее исправление: общий механизм и актуальный охват описаны в [INPUT_VALIDATION.md](INPUT_VALIDATION.md). Ниже сохранены исходные результаты аудита до исправлений.

Предмет проверки — завершённый некорректный ввод, после которого новый расчёт не начинается или изменение не применяется, а поле само не исправляется. Промежуточный минус или пустая строка во время набора сами по себе ошибкой не считаются. Кнопка «Построить» и потеря фокуса не должны оставлять пользователя без понятного способа продолжить работу.

**Главный результат:** проблема воспроизводится. Есть полностью молчаливое игнорирование ввода; есть отказ до запуска рендера с объяснением только в строке состояния. Во втором случае проверка значения в коде есть, но автокоррекции и привязанной к полю ошибки нет. Эти два случая нельзя называть одинаково «отсутствием валидации».

## 1. Проверка именно остановки запуска

Для следующих случаев вызван штатный метод запуска предпросмотра с неправильным значением. Он завершился до расчёта; прежний `Image.Source` остался тем же объектом. `Validation.GetHasError` у поля — `false`, введённый текст остался неправильным.

| Окно | Поле и ввод | Результат |
|---|---|---|
| Мандельброт | `IterationsBox = 0` | Новый рендер не начинается. Только статус: допустимо 50–2 000 000. |
| Мандельброт, обычная плавная окраска | `DistanceAmbientBox = 2` | Даже скрытый блок Distance Estimation блокирует рендер. Только статус о диапазоне ambient 0–1. |
| Коллатц, окраска по времени выхода | `MaximumPeriodBox = 0` | Скрытые настройки поиска периода продолжают блокировать рендер. Только статус. |
| Phoenix, плавная окраска | `OrbitTrapRadiusBox = -1` | Скрытая орбитальная ловушка продолжает блокировать рендер. Статус общий: «Проверьте C1/C2, степени…». |
| Бассейны Ньютона | `IterationsBox = 0` | Новый рендер не начинается. Только статус о диапазоне 1–100 000. |
| Аполлонова прокладка | `DepthBox = 0` | Новый рендер не начинается. Только статус о диапазоне 1–18. |
| Раузи, режим с отключённым радиусом мозаики | `TertiaryBox = abc` | Отключённое поле блокирует расчёт: ошибка «Радиус мозаики». Для этого окна проверен выход до расчёта; синтетический `Image.Source` не подставлялся. |

Для скрытого поля пользовательский сценарий: открыть соответствующий режим окраски/мозаики → ввести неправильное значение → переключиться в другой режим → попробовать построить или изменить другой параметр. Переключение не устраняет причину отказа.

Причина: параметры неактивных блоков читаются безусловно. См. [Мандельброт: чтение параметров](FractalExplorerWPF/FractalExplorerWPF/Views/MandelbrotWindow.xaml.cs#L224), [Коллатц](FractalExplorerWPF/FractalExplorerWPF/Views/CollatzWindow.xaml.cs#L91), [Phoenix](FractalExplorerWPF/FractalExplorerWPF/Views/PhoenixWindow.xaml.cs#L94), [лаборатории](FractalExplorerWPF/FractalExplorerWPF/Views/MathematicalLaboratoryWindow.xaml.cs#L128). Точки выхода до расчёта: [Мандельброт](FractalExplorerWPF/FractalExplorerWPF/Views/MandelbrotWindow.xaml.cs#L633), [Ньютон](FractalExplorerWPF/FractalExplorerWPF/Views/NewtonPoolsWindow.xaml.cs#L678), [3D](FractalExplorerWPF/FractalExplorerWPF/Views/Fractal3DWindow.xaml.cs#L935).

## 2. Полностью молчаливое игнорирование

Здесь неправильный текст остаётся в поле, но новое значение не применяется; сообщения и ошибки у поля нет. Это не означает, что кнопка рендера перестаёт работать вообще: в нескольких окнах она строит по прежнему внутреннему значению.

| Где | Элементы | Пример и поведение | Код |
|---|---|---|---|
| Семейства Мандельброта и Жюлиа | `ZoomBox` | `abc`, `0`, отрицательное значение: обработчик не меняет масштаб и не запрашивает новый рендер. Поле не восстанавливается. | [378](FractalExplorerWPF/FractalExplorerWPF/Views/MandelbrotWindow.xaml.cs#L378) |
| Бассейны Ньютона | `ZoomBox` | Аналогично. | [598](FractalExplorerWPF/FractalExplorerWPF/Views/NewtonPoolsWindow.xaml.cs#L598) |
| Все 11 режимов бассейнов | `ZoomBox` | Аналогично; отдельно от числовых параметров, которые выводят ошибку в статус. | [924](FractalExplorerWPF/FractalExplorerWPF/Views/BasinExplorerWindow.xaml.cs#L924) |
| Коллатц | `ZoomBox` | Непарсящийся, неконечный или неположительный масштаб игнорируется. | [249](FractalExplorerWPF/FractalExplorerWPF/Views/CollatzWindow.xaml.cs#L249) |
| Nova | `ZoomBox` | `abc` игнорируется. Неположительное число проходит в `SetZoom`, который сохраняет старый вид. | [137](FractalExplorerWPF/FractalExplorerWPF/Views/NovaWindow.xaml.cs#L137) |
| Phoenix | `ZoomBox` | Неправильный/неположительный масштаб игнорируется. | [200](FractalExplorerWPF/FractalExplorerWPF/Views/PhoenixWindow.xaml.cs#L200) |
| Domain Coloring | `ZoomBox` | `abc`, `0`, отрицательный масштаб игнорируются. | [183](FractalExplorerWPF/FractalExplorerWPF/Views/DomainColoringWindow.xaml.cs#L183) |
| Буддаброт | `ZoomBox` | `abc` игнорируется. Число вне диапазона ограничивается внутри, но текст в поле не приводится к фактическому масштабу. | [22](FractalExplorerWPF/FractalExplorerWPF/Views/BuddhabrotWindow.xaml.cs#L22) |
| Серпинский / игра хаоса | `ZoomBox` | `abc` игнорируется. Диапазон ограничивается внутри, без исправления текста. | [135](FractalExplorerWPF/FractalExplorerWPF/Views/SerpinskyWindow.xaml.cs#L135) |
| Общая 3D-камера, все 41 режима | `YawBox`, `PitchBox`, `RollBox`, `DistanceBox`, `FieldOfViewBox`, `TargetXBox`, `TargetYBox`, `TargetZBox` | `abc` сохраняет прежнее внутреннее значение. Для расстояния также игнорируется `<= 0`, для обзора — вне 5–160. Новый кадр может запрашиваться, но введённое значение не используется. | [735](FractalExplorerWPF/FractalExplorerWPF/Views/Fractal3DWindow.xaml.cs#L735) |
| Редактор преобразований IFS 2D | `ABox`, `BBox`, `CBox`, `DBox`, `EBox`, `FBox` | `abc` не меняет матрицу. «Применить» передаёт старое значение без повторной проверки текста. Проверено: текст `abc`, фактическое A осталось 0.5, `Validation.HasError = false`. | [100](FractalExplorerWPF/FractalExplorerWPF/Views/IfsTransformEditorWindow.xaml.cs#L100), [245](FractalExplorerWPF/FractalExplorerWPF/Views/IfsTransformEditorWindow.xaml.cs#L245) |
| Редактор преобразований Flame 2D | `ABox`, `BBox`, `CBox`, `DBox`, `EBox`, `FBox` | То же; «Применить»/«Готово» забирают прежнюю матрицу. | [48](FractalExplorerWPF/FractalExplorerWPF/Views/FlameTransformEditorWindow.xaml.cs#L48), [90](FractalExplorerWPF/FractalExplorerWPF/Views/FlameTransformEditorWindow.xaml.cs#L90) |
| Gray–Scott 2D, живая настройка окраски | `RangeMinimumBox`, `RangeMaximumBox` | Нечисловой ввод, максимум `<=` минимума, неконечная разность — ранний `return`, без сообщения. Перезапуск симуляции отдельно проверяет диапазон и сообщает ошибку. | [245](FractalExplorerWPF/FractalExplorerWPF/Views/GrayScottWindow.xaml.cs#L245) |
| Палитра Буддаброта, пользовательская копия | `GammaBox`, `StepsBox` | Гамма вне 0.1–5 или шаги вне 2–100 000: «Применить» молча ничего не делает. «Сохранить» игнорирует `false` и перезагружает старые значения редактора. | [19–21](FractalExplorerWPF/FractalExplorerWPF/Views/BuddhabrotPaletteWindow.xaml.cs#L19) |
| L-системы 2D, текущая отрисовка | `StartThicknessBox`, `EndThicknessBox` | После построения сцены неправильная толщина молча останавливает обновление оформления. При явном «Построить» уже появляется предупреждение. | [152](FractalExplorerWPF/FractalExplorerWPF/Views/LSystemWindow.xaml.cs#L152) |
| Обратное дерево Коллатца, текущая анимация | `AnimationIntervalBox` | Неправильный интервал не применяется; прежний интервал продолжает действовать. Следующий полноценный захват параметров уже выдаёт ошибку в статус. | [170](FractalExplorerWPF/FractalExplorerWPF/Views/InverseCollatzTreeWindow.xaml.cs#L170) |
| Бассейны Ньютона, метод Хаусхолдера | `HouseholderOrderBox` | `abc` молча заменяется внутренним 3; число ограничивается 2–12. Текст остаётся прежним. Это скрытая подстановка, а не остановка рендера. | [802](FractalExplorerWPF/FractalExplorerWPF/Views/NewtonPoolsWindow.xaml.cs#L802) |

Для девяти перечисленных групп окон с `ZoomBox` состояние/сохранение берёт внутренний масштаб, а не проверяет видимый текст повторно. Поэтому неправильное поле может сосуществовать с рабочим рендером старого вида.

## 3. Рендер блокируется, автокоррекции нет, причина только в статусе

Типовая цепочка: `TextChanged` → отложенный запуск → чтение параметров → исключение/`false` → запись в `StatusText` → `return` до расчёта. Старое изображение остаётся. У обычных числовых `TextBox` нет общей проверки на потерю фокуса или подсветки конкретного ошибочного поля.

Ниже перечислены поля/группы, которые нужно учитывать при исправлении именно этого поведения. Условные параметры относятся к режимам, где они читаются; ручные команды поиска/добавления рассматриваются отдельно от автоматического предпросмотра.

| Окно / семейство | Поля, способные остановить новый расчёт без исправления ввода |
|---|---|
| Мандельброт / все его Жюлиа | `IterationsBox`, `ThresholdBox`, `PowerBox` при доступной степени, `JuliaRealBox`/`JuliaImaginaryBox` для Жюлиа; **все** числовые параметры окраски: `SmoothBlendPowerBox`, `SmoothIterationOffsetBox`, `HistogramContrastBox`, `OrbitStrengthBox`, `OrbitBiasBox`, `StripeFrequencyBox`, `StripeStrengthBox`, `StripeBiasBox`, `PolyABox`, `PolyBBox`, `PolyCBox`, `PolyGammaBox`, `PolyBlendBox`, `PolyBiasBox`, `PalettePhaseBox`, `PaletteScaleBox`; **все** `Distance*Box`. Последние читаются даже при скрытом блоке. Степень/повторы строк гибрида имеют отдельный `HybridValidationText`, но также не исправляются. |
| Бассейны Ньютона | `IterationsBox`, `RootToleranceBox`, `RootSearchRadiusBox`; в расслабленном методе — `RelaxationRealBox`/`RelaxationImaginaryBox` или `FixedInitialZRealBox`/`FixedInitialZImaginaryBox` в зависимости от плоскости. Ошибка формулы имеет собственный путь применения. |
| 11 режимов бассейнов | `IterationsBox`, `ShadingScaleBox`; параметры корней/циклов, `ParameterC*`, параметры метода Мюллера/Лагерра/секущих; параметры физики, оптимизации, потока и векторного поля. Значения читают `ReadInt`/`ReadDouble`/`ReadComplex`, ошибки применения также пишутся в статус. Неактивные вспомогательные поля местами читаются мягко с подстановкой, это не блокировка. |
| Коллатц | `IterationsBox`, `ThresholdBox`, `PParameterBox`, `QRealParameterBox`, `QImaginaryParameterBox`, `ArgumentCyclesBox`, `MagnitudeScaleBox`, `TrapScaleBox`, `CycleToleranceBox`, `MaximumPeriodBox`, `DensityExposureBox`, `DensitySampleStepBox`. Параметры окраски читаются и для невыбранных вариантов. |
| Nova | `PRealBox`, `PImaginaryBox`, `Z0RealBox`, `Z0ImaginaryBox`, `CRealBox`, `CImaginaryBox`, `MBox`, `IterationsBox`, `ThresholdBox`. У основного предпросмотра есть статус ошибки; отдельная карта дополнительно имеет `catch { return; }`. |
| Phoenix | `C1RealBox`, `C1ImaginaryBox`, `C2RealBox`, `C2ImaginaryBox`, `PrimaryPowerBox`, `SecondaryPowerBox`, `InitialZRealBox`, `InitialZImaginaryBox`, `PreviousRealBox`, `PreviousImaginaryBox`, `IterationsBox`, `ThresholdBox`, `OrbitTrapRadiusBox`, `OrbitTrapStrengthBox`, `StripeFrequencyBox`, `StripeStrengthBox`, `CycleToleranceBox`, `MaximumPeriodBox`. Проверка общая, сообщение не называет конкретное поле. Неактивные панели окраски не исключаются. |
| Буддаброт / Анти-Буддаброт | `SamplesBox`, `IterationsBox`, `MinReBox`, `MaxReBox`, `MinImBox`, `MaxImBox`; также несовместимая пара границ. |
| Domain Coloring | `HueCyclesBox`, `SaturationBox`, `MagnitudeExposureBox`, `RingDensityBox`, `PhaseSectorsBox`, `ContourStrengthBox`, некорректная `FormulaBox`. |
| Серпинский / игра хаоса | Нечисловая `IterationsBox`. Число вне диапазона ограничивается внутри `ReadIterations`, но не исправляется в поле. |
| IFS 2D | `IterationsBox`, `CenterXBox`, `CenterYBox`, `ScaleBox`. |
| Flame 2D | `SamplesBox`, `IterationsBox`, `WarmupBox`, `ScaleBox`, `CenterXBox`, `CenterYBox`, `ExposureBox`, `GammaBox`. |
| DLA 2D | `ParticlesBox`, `GridSizeBox`, `MaxStepsBox`, `StickinessBox`, `DriftXBox`, `DriftYBox`, `RandomSeedBox`, `ParticleRadiusBox`, `CenterXBox`, `CenterYBox`, `ViewWidthBox`. У сетки дополнительно требуется нечётность. |
| Аполлонова прокладка | `DepthBox`, `CircleLimitBox`, `MinimumRadiusBox`, `LineWidthBox`, `CenterXBox`, `CenterYBox`, `ViewWidthBox`. |
| Обратное дерево Коллатца | `DepthBox`, `MaxNodesBox`, `NodeRadiusBox`, `LineThicknessBox`, `AnimationIntervalBox` при захвате состояния. |
| Динамические системы | Динамически создаваемые числовые поля из `Fields(kind)` и дополнительных панелей: Ляпунов, Лоренц, Рёсслер, логистическое отображение, бифуркации, Хенон, Икэда, шесть вариантов 2D-аттракторов, Popcorn, Hopalong. Нечисловой ввод и `NaN` в этих числовых полях отклоняются; границы допустимых чисел проверяются не везде одинаково. 12 коэффициентов Спротта, параметры Symmetric Icons, Popcorn и Hopalong имеют дополнительные проверки. |
| Все 17 математических лабораторий | `PrimaryBox`, `SecondaryBox`, `TertiaryBox`, `ParameterBox`, `RotationBox`. Подписи и диапазоны берутся из определения лаборатории. `TertiaryBox` Раузи проверяется даже после отключения поля. |
| Макмаллен | `PBox`, `QBox`, `LambdaRealBox`, `LambdaImaginaryBox`, `IterationsBox`, `EscapeRadiusBox`, `ColorScaleBox`, `ColorOffsetBox`, `TrapRadiusBox`, `PeriodBox`, `ToleranceBox`, `CenterXBox`, `CenterYBox`, `ZoomBox`. Здесь видимые поля навигации повторно проверяются при захвате состояния. |
| Трансцендентные Жюлиа | `RayLimitBox`, `RayAccuracyBox`, `LambdaRealBox`, `LambdaImaginaryBox`, `IterationsBox`, `PeriodBox`, `ToleranceBox`, `DepthBox`, `BranchesBox`, `BudgetBox`, `CenterXBox`, `CenterYBox`, `ZoomBox`. |
| Общие поля 3D | `IterationsBox`, `PowerBox` по диапазону режима; `BailoutBox`, `JuliaC*Box`, `SliceBox`, `PhoenixSecondaryPowerBox`, `PhoenixMemory*Box`, `BoxScaleBox`, `BoxMinRadiusBox`, `BoxFoldingBox`, `Hybrid*Box`, `SierpinskiScaleBox`, `CubeThicknessBox`; `AutoRotateSpeedBox`, `MaxStepsBox`, `DetailBox`, `MaxDistanceBox`, `LightYawBox`, `LightPitchBox`, `AmbientBox`, `SpecularBox`, `ShadowSharpnessBox`, `AoStrengthBox`, `EffectStrengthBox`, `ColorScaleBox`, `ColorOffsetBox`, `SkyLightMixBox`. Есть также общий экран ошибки подготовки; поэтому это не полностью молчаливый отказ. |
| Специализированные поля 3D | Числовые `Terrain*Box`, `Flame*Box`, `Attractor*Box`, `Lyapunov*Box`, `DlaRandomSeedBox`; поля KIFS, периодических и неявных поверхностей; `Buddhabrot4DEditor`, `KleinianEditor`, `AntoineEditor`, `Newton3DEditor`, `HyperbolicHoneycombEditor`. Чтение — через `PanelFields` либо проверки настроек с передачей ошибки окну. `LichtenbergEtaBox`/`LichtenbergSeedBox` проверяются при запуске роста с ошибкой в статусе. Неопределённые формулы имеют отдельные сообщения. |

Основные места проверки: [MandelbrotWindow.CaptureState](FractalExplorerWPF/FractalExplorerWPF/Views/MandelbrotWindow.xaml.cs#L187), [BasinExplorerWindow.CaptureState](FractalExplorerWPF/FractalExplorerWPF/Views/BasinExplorerWindow.xaml.cs#L106), [DynamicSystemWindow.CaptureState](FractalExplorerWPF/FractalExplorerWPF/Views/DynamicSystemWindow.xaml.cs#L397), [Fractal3DWindow.CaptureViewFields](FractalExplorerWPF/FractalExplorerWPF/Views/Fractal3DWindow.xaml.cs#L153), [PanelFields](FractalExplorerWPF/FractalExplorerWPF/Controls/PanelFields.cs#L13).

## 4. Симуляции и редакторы: момент применения имеет значение

| Где | Что проверено / чем отличается от остановки автоматического рендера |
|---|---|
| Gray–Scott 2D | Диффузии, F/K, шаг времени, сетка, шаги на кадр, seed/затравки/кисть проверяются при перезапуске, ошибку получает `StatusText`. Изменение этих полей само по себе показывает неприменённые параметры. Сетка и размер кадра имеют отдельные поля ошибки. Молчаливый отказ живого цветового диапазона выделен в §2. |
| Тьюринг 2D | Размер сетки, ширина/высота кадра, seed имеют сообщения при применении. Радиусы/отклики строк масштабов используют WPF-валидацию привязки; применение проверяет `Validation.GetHasError` и показывает `LayersError`. Эти поля не следует записывать в полностью незащищённые. |
| Песочная куча | `SizeBox`/`InitialBox` проверяются при сбросе; `BrushGrainsBox`/`BrushRadiusBox`/`DropBox` — при изменении. Ошибка в статусе, автокоррекции нет. |
| Снежные кристаллы | `DiffusionBox`/`VaporBox`/`DepositionBox` проверяются при изменении, `RadiusBox`/`SeedRadiusBox` — при новом поле. Ошибка в статусе. |
| Все десять GPU-симуляций 3D | Числовые параметры подготовки проверяются при соответствующей команде применения/перезапуска: Gray–Scott, Тьюринг, Кан–Хиллард, Lenia, Physarum, Кобаяши, возбудимые среды, Гинзбург–Ландау, Свифт–Хоэнберг, Phase Field Crystal. Ошибки передаются через `_host.ShowStatus`; поля не исправляются. Не каждый ввод в них должен немедленно перезапускать моделирование. |
| ABC Flow / Hopf | Текстовые поля, напрямую привязанные к `Slider.Value`, имеют WPF-ошибку преобразования. В отдельной проверке `abc` дало `Validation.HasError = true`; `4` при диапазоне 0–1 скорректировалось в `1`. Это отличается от обычных `TextBox` с ручным `TryParse`. |
| L-системы 2D и генератор форм | При явном построении неправильные параметры имеют предупреждение; генератор форм выводит `ResultText`. Молчаливая живая настройка толщин выделена отдельно. |
| Редакторы IFS/Flame 3D | Матрица проверяется как конечное число с ограничением модуля, выводится `ValidationText`. В отличие от 2D-редакторов. |
| Выбор C, карта 2D/3D, обозреватель Phoenix | Во время набора неполное/неправильное число может оставлять прежнюю константу. Но подтверждение повторно проверяет поля и показывает предупреждение; считать это полностью незащищённым «Применить» неверно. |
| Палитры | Неправильный обычный ввод при применении сообщает ошибку в окнах Mandelbrot, Domain Coloring, Dynamic, Lyapunov, Gray–Scott, Fractal3D/Ifs3D, Inverse Collatz. Исключение — молчаливые `GammaBox`/`StepsBox` Буддаброта (§2). Палитры Ньютона и Серпинского не имеют свободных числовых полей гаммы. |
| Экспорт | `WidthBox`/`HeightBox` проверяются при экспорте, есть предупреждение и подсказка разрешения. Не относится к полностью молчаливой блокировке построения в основном окне. |

Служебные текстовые поля поиска, названия сохранений/палитр, аккаунта, HEX-цвета и демонстрационные поля редактора темы не являются параметрами генерации. Они не включены в перечень остановок фрактального рендера.

## 5. Дополнительные пробелы, не смешивать с незапуском рендера

Эти случаи обнаружены при проверке допустимости ввода, но они **не доказывают** описанное пользователем поведение «старый кадр остаётся и новый расчёт не запускается». Некоторые, наоборот, пропускают плохие данные в расчёт.

- `NaN` проходит сравнения вида `value is < min or > max`, а `Math.Clamp(NaN, …)` не превращает его в корректное число. Нужна отдельная проверка конечности. `PanelFields.ReadDouble` такую проверку уже делает.
- Аполлонова прокладка: `MinimumRadiusBox`, `LineWidthBox`, `CenterXBox`, `CenterYBox`, `ViewWidthBox` пропускают `NaN`; координаты также пропускают переполнение `double`. IFS 2D: `CenterXBox`, `CenterYBox`, `ScaleBox`; Flame 2D: `CenterXBox`, `CenterYBox`, `ScaleBox`, `ExposureBox`, `GammaBox`. Приём в состояние подтверждён диагностикой.
- Матрицы IFS/Flame 2D пропускают `NaN` и бесконечность, а обычный нечисловой текст молча игнорируют. Это две разные ветки одного обработчика.
- Общая 3D-камера: `NaN` в углах/цели попадает во внутреннее состояние. `TargetXBox = 1e39` даёт `Infinity` уже при приведении к `float`; построение базиса камеры и проверка режима это не отклоняют. Для расстояния допускается положительная бесконечность. Обзор 5–160 проверяется, но неправильный текст не исправляется.
- Ньютон: `RootToleranceBox`/`RootSearchRadiusBox` пропускают `NaN`. Серпинский: `ZoomBox` пропускает `NaN` во внутренний масштаб.
- L-системы 2D: `StartThicknessBox`, `EndThicknessBox`, `AnimationDurationBox` с `NaN` проходят `TryReadDefinition` как корректные. Обычный неправильный ввод при явном построении имеет предупреждение.
- Палитры: проверка конечности отсутствует для гаммы в Mandelbrot, Buddhabrot, GrayScott, Fractal3D/Ifs3D; для диапазона/нулевой полосы в Dynamic и Lyapunov. Это пробел проверки чисел; дальнейшее поведение сохранения и рендера нельзя свести к одному молчаливому отказу.
- Логистическое отображение: `Iterations = 0` в орбите; `BifurcationSamples = 0`, `BifurcationPlottedPoints = 0` или равные границы r в бифуркационном представлении. Отдельная бифуркационная диаграмма: `Iterations = 0`, `SamplesPerR = 0`, равные границы r/x. Эти значения принимаются; движки возвращают фон/оси и сообщают завершение. Это **запустившийся пустой расчёт**, не незапуск. Ноль у Хенона/Икэды обрабатывается иначе; переносить вывод на них нельзя.

## 6. Общая причина и порядок исправления

`NumericSpinner` — только изменение текста стрелками/Up/Down. В нём нет диапазона, фиксации ввода по потере фокуса, сохранения последнего корректного значения или общей ошибки поля. Даже его штатная проверка ожидает переход `0 → -1` для целочисленного поля. Наличие стрелок поэтому не означает наличие защиты от некорректного ввода. См. [NumericSpinner](FractalExplorerWPF/FractalExplorerWPF/Controls/NumericSpinner.cs#L64).

Рекомендуемый порядок последующей работы:

1. Устранить блокировку активного расчёта скрытыми/отключёнными полями (примеры в §1). Для параметров, которые нужны сохранению, определить отдельное поведение сохранения и активного рендера.
2. Ввести общий механизм завершения числового ввода: диапазон, последнее корректное значение, нормализация текста на потере фокуса/Enter/явном применении. При этом сохранить возможность набрать промежуточный минус, дробь и научную нотацию, включая глубокие масштабы `1e1000`.
3. Подключить к нему обычные поля из §2–3. Для зависимых диапазонов проверять пару целиком; для нечётной сетки DLA учитывать нечётность. Ошибка должна указывать на поле, а не только прерывать запуск.
4. Отдельно закрыть приём `NaN`/бесконечности и переполнение в представлении, которое использует движок (`double`, `decimal`, `float`).

## 7. Объём и ограничения проверки

- Просмотрена структура ввода в `Views/` и `Controls/`: 612 объявлений `TextBox` в 69 XAML-файлах, включая служебные/только для чтения, плюс динамические поля C# (динамические системы, гибриды, масштабы Тьюринга).
- Дополнительная диагностика без показа окон создала 94 конфигурации: 15 основных вариантов окон, 10 видов динамических систем, 11 бассейнов, 17 лабораторий, 41 режим 3D. В доступные числовые поля подставлялись `abc`, `NaN`, `1e309`: 3888 попыток. Повторяющиеся общие поля 3D учитывались для каждого режима, поэтому это не число уникальных полей.
- Проверка `CaptureState` сама по себе не равна проверке кнопки/рендера: действие может иметь отдельное подтверждение. Такие случаи сопоставлены с обработчиками, а не автоматически признаны ошибками. Динамически создаваемые дополнительные редакторы и варианты формул проверены по общим путям чтения; не заявляется перебор всех возможных сочетаний состояний интерфейса.
- Семь сценариев §1 дополнительно проверены вызовом штатного метода запуска до раннего выхода, шесть — с проверкой сохранения ссылки на предыдущий кадр. Специальные случаи §2 проверены по обработчикам и отдельным вызовам применения.
- Пользовательские данные перенаправлены через `AppPaths.OverrideDataRoot` в отдельный каталог диагностики. Настоящие сохранения, настройки и палитры не использовались. Окна не показывались, скриншоты не снимались.
- WPF-решение собрано успешно: **0 ошибок, 0 предупреждений**. Исправления приложения и новые постоянные группы Verification в эту задачу не входят.
