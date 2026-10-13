# Инструкции для агентов

## Синхронизация AGENTS.md и CLAUDE.md

[AGENTS.md](AGENTS.md) и [CLAUDE.md](CLAUDE.md) в корне репозитория — две одинаковые копии общих инструкций для всех агентов. Их содержимое должно совпадать побайтово.

- Перед изменением инструкций прочитай оба файла. Если они разошлись, объедини актуальные сведения из обеих версий, сверяя противоречия с кодом и указаниями пользователя; не теряй дополнения другого агента при копировании.
- Любое изменение правил, каталога окон или команд проверки вноси в оба файла в рамках одной задачи. Не веди отдельную историю для Codex или Claude.
- После редактирования перенеси итоговое содержимое в парный файл и проверь совпадение: `git diff --no-index -- AGENTS.md CLAUDE.md` (без вывода, код возврата 0).
- Эти файлы загружаются в каждую сессию, поэтому здесь — только правила, краткий каталог и команды. **Не превращай их в журнал изменений.** Подробности окна — в [FractalExplorerWPF/WINDOWS.md](FractalExplorerWPF/WINDOWS.md), подробности режима — в его документе `FractalExplorerWPF/<РЕЖИМ>.md`, группы проверок — в [FractalExplorerWPF/Verification/README.md](FractalExplorerWPF/Verification/README.md). Новый режим добавляет сюда не больше одной строки.

## Главный приоритет: только WPF

Актуальная и единственная активно развиваемая версия проекта — **WPF-версия** в каталоге `FractalExplorerWPF/`. Если пользователь не указал технологию или версию явно, считай, что задача относится к WPF: ищи, анализируй, изменяй, собирай и проверяй только её. При неоднозначном запросе выбирай WPF без уточнения и кратко указывай это допущение в результате.

Каталог `FractalExplorer/` содержит устаревшую **WinForms-версию**. Она заморожена:

- не вноси в неё изменения по умолчанию и не дублируй в неё исправления и возможности из WPF;
- не используй её solution или project для сборки и проверки обычных задач;
- не выбирай её только потому, что там уже есть похожая реализация; как справочный материал она допустима, но итоговая реализация — в WPF;
- работать с `FractalExplorer/` можно, только если пользователь прямо и однозначно попросил изменить именно WinForms-версию.

## Расположение актуального проекта

- Решение: `FractalExplorerWPF/FractalExplorerWPF/FractalExplorerWPF.slnx`
- Проект: `FractalExplorerWPF/FractalExplorerWPF/FractalExplorerWPF/FractalExplorerWPF.csproj`
- Исходный код: `FractalExplorerWPF/FractalExplorerWPF/FractalExplorerWPF/`
- Точка входа и ресурсы приложения: `App.xaml`, `App.xaml.cs`; главное окно: `MainWindow.xaml`, `MainWindow.xaml.cs`

`net10.0-windows`, nullable reference types, implicit usings. Framework reference на `Microsoft.WindowsDesktop.App.WindowsForms` нужна для ограниченной совместимости с Windows API и не означает, что legacy WinForms-версию нужно обновлять.

Единственная внешняя зависимость — NuGet-пакеты `Vortice.Direct3D11`, `Vortice.DXGI`, `Vortice.D3DCompiler` (обёртки Direct3D 11). Трёхмерные режимы и вычислительные шейдеры считаются на ГП (`Core/Rendering3D/`, общее устройство окна — `Direct3DDeviceHost.cs`); двумерные — на ЦП, кроме Тьюринга и Gray–Scott (ЦП или ГП) и Жюлиа с картой выбора C (ГП на малом зуме).

## Структура WPF-проекта

```text
FractalExplorerWPF/FractalExplorerWPF/FractalExplorerWPF/
├── App.xaml, App.xaml.cs          # запуск и ресурсы приложения
├── MainWindow.xaml, .xaml.cs      # каталог и главное окно
├── Views/                         # окна фракталов, редакторы и диалоги
├── Controls/                      # переиспользуемые WPF-контролы и панели режимов
├── Core/
│   ├── Math/                      # комплексная арифметика и парсеры выражений
│   ├── Rendering/                 # движки, рендереры и растровая инфраструктура
│   └── Rendering3D/               # GPU-рендер и вычислительные шейдеры (Direct3D 11)
├── Models/                        # модели UI, параметров, палитр и сохранений
├── Infrastructure/
│   ├── ColorPicking/              # выбор цвета и экранная пипетка
│   ├── Migrations/                # версии данных, миграции, перенос старой папки Saves
│   └── Serialization/             # JSON-конвертеры
│                                  # также хранилища, настройки, экспорт и сервисы
├── Theming/                       # темы, стили и их хранение
├── Assets/                        # иконки, превью и изображения
└── Properties/                    # настройки проекта и ресурсов
```

Границы ответственности:

- Разметка и визуальное состояние окон — `Views/` и XAML; общие элементы интерфейса и панели режимов — `Controls/`.
- Математика и вычислительный рендеринг не зависят от UI и относятся к `Core/`; расчёт кадра на видеокарте — к `Core/Rendering3D/`.
- DTO, состояния и параметры — `Models/`; сериализация, файловые хранилища, настройки и интеграция с ОС — `Infrastructure/`; оформление — `Theming/` и `Assets/`.

## Как добавлять и менять режимы

Не копируй соседний режим — встраивайся в общие механизмы:

- **Пункт каталога.** Новый режим получает дату первого рабочего внедрения в WPF в `Models/FractalCatalogHistory.cs` по стабильному ключу запуска (со временем и часовым поясом, чтобы режимы одного дня сортировались правильно). Переименование, исправления и обновление превью дату не меняют. Группа проверки `catalog` контролирует полноту дат.
- **Трёхмерный режим — пункт `Fractal3DKind` и класс-наследник `Fractal3DMode`** в `Models/Fractal3DModes/`, зарегистрированный в `Fractal3DCatalog.Modes`. Класс задаёт определение, стартовый вид (`ConfigureDefault`) и пресеты, признаки общих полей (`UsesIterations`, `UsesBailout`, `IsDensityVolume`, `IsGpuSimulation`), поля «Итерации» и «Степень», заголовок блока «Форма» (`ShapeHeader`), переназначенные источники цвета (`ReassignedColoring`), проверку параметров, описание в менеджере сохранений и слоты констант шейдера (`ShapeConstants`, бюджет полосы). Состояние — общий DTO `Fractal3DState`; формат JSON от классов режимов не зависит. Не добавляй новые списки `Kind is … or …` в окно, рендерер и менеджер сохранений. Правку общих механизмов сверяй группой `fractal3d-modes --record/--compare`.
- **Панель режима — отдельный контрол** в `Controls/` (как `HopfEditor`, `Lenia3DEditor`), а не разметка внутри `Views/Fractal3DWindow.xaml`.
- **Живое моделирование на ГП:** расчёт — наследник `Core/Rendering3D/GpuVolumeSimulation.cs` (слоты публикации, порции шагов, контрольные точки); превью — строка `GpuSimulationPreview` в `Fractal3DRenderer.Simulations.cs`; панель реализует `IFractal3DSimulationEditor` и создаёт `Controls/Fractal3DLiveSimulation.cs` (общий цикл шагов, подготовка, кисть, ошибки); класс режима — наследник `Models/Fractal3DModes/GpuSimulation3DMode.cs`; регистрация — `Fractal3DWindow.CreateSimulation`.
- **Шейдеры.** Любой новый GPU/3D-режим или вариант шейдера включай в общий дисковый кэш `Infrastructure/ShaderBytecodeCache.cs` через `AppPaths.ShaderCacheDirectory`. Ключ учитывает исходник, точку входа и профиль; при изменении флагов или способа компиляции повышай версию сигнатуры кэша. Вариант участвует в команде «Пересобрать шейдеры» на главной странице. Не храни байткод рядом с exe и не заводи отдельную папку кэша для фрактала.
- **Стартовая окраска.** У поверхностных 3D-фракталов и IFS — «Материал»; ландшафты, аттракторы, симуляции, Хопф и аполлонова упаковка окрашиваются по своим данным. Стартовый вид задаёт `ConfigureDefault` класса режима (умолчание DTO `Fractal3DState` не трогать — по нему читаются старые JSON); при его изменении обновляй версию превью в `CatalogPreviewCache`.
- **Экран подготовки.** Каждый режим `Fractal3DWindow` до первого кадра показывает общий экран подготовки (`Fractal3DWindow.Preparation.cs`, `Controls/PreparationOverlay`; моделирования — через `Fractal3DLiveSimulation`), пустого холста быть не должно. Экран закрывает только показ актуального кадра; при ошибке — причина без бесконечного индикатора. Фазы ГП — `Fractal3DRenderStage`.
- **Документ режима.** Описание модели, параметров и ручной проверки — в `FractalExplorerWPF/<РЕЖИМ>.md`; окно — строка в каталоге ниже и раздел в `WINDOWS.md`.

## Каталог WPF-окон

В WPF нет классов `Form`: интерфейс состоит из `Window` и вложенных `UserControl`. Для окна `NameWindow` разметка — `Views/NameWindow.xaml`, логика — `Views/NameWindow.xaml.cs` (главное окно — в корне проекта). WinForms-формы не включены. **Подробное описание каждого окна, его файлов и поведения — в [FractalExplorerWPF/WINDOWS.md](FractalExplorerWPF/WINDOWS.md).** При добавлении, удалении, переименовании или изменении назначения окна либо общего контрола обновляй строку здесь и раздел в `WINDOWS.md` в том же изменении (и список инструмента скриншотов — см. ниже).

### Исследовательские окна

| Окно | Назначение |
|---|---|
| `MainWindow` | Каталог режимов: разделы, избранное, недавние, «Трёхмерные», «Новые поступления»; сетка превью с постоянным PNG-кэшем (`Infrastructure/CatalogPreviewLoader.cs`, `CatalogPreviewCache.cs`), поиск (Ctrl+F), быстрый переключатель (Ctrl+K), настройки, облако. Пункты — `Models/FractalCatalog.cs`. |
| `ApollonianWindow` | Аполлонова прокладка окружностей. |
| `BasinExplorerWindow` | Одиннадцать режимов «Бассейны притяжения» (`BasinExplorerKind`): Мюллер, Лагерр, секущие, рациональные отображения и циклы, логистическое отображение, магнитный маятник и центры, градиентный спуск, комплексный поток, векторные поля. |
| `BuddhabrotWindow` | Буддаброт и Анти-Буддаброт. |
| `CollatzWindow` | Комплексный Коллатц, глубокий зум до 1e50. |
| `DlaWindow` | Двумерный DLA. |
| `DomainColoringWindow` | Раскраска комплексных функций с циклической палитрой аргумента. |
| `DynamicSystemWindow` | Ляпунов, Лоренц, Рёсслер, логистическое отображение, бифуркации, Хенон, Икэда, 2D-аттракторы, Спротт, Symmetric Icons (`SYMMETRIC_ICONS.md`), Popcorn (`POPCORN.md`), Hopalong (`HOPALONG.md`). |
| `FlameWindow` | Fractal Flame. |
| `Fractal3DWindow` | Тридцать восемь трёхмерных режимов (`Fractal3DKind`): поверхности по оценке расстояния, объёмы плотности, десять живых моделирований на ГП, аналитические сцены. Общие камера и навигация (`Fractal3DWindow.Navigation.cs`), подготовка, палитры, сохранения, экспорт; рендер — `Core/Rendering3D/Fractal3DRenderer*.cs`. Документы режимов — `FractalExplorerWPF/*3D*.md`, `HOPF.md`, `KIFS.md`, `TERRAIN.md`, `ABC_FLOW.md` и др. |
| `Fractal3DConstantPickerWindow` | Выбор константы C Жюлиабульба и горящего корабля 3D — Жюлиа на карте исходного фрактала. |
| `GrayScottWindow` | Gray–Scott 2D на ЦП/ГП с поиском узоров (`GRAY_SCOTT.md`). |
| `IfsWindow` | IFS Барнсли/Хейуэя. |
| `InverseCollatzTreeWindow` | Обратное дерево целочисленного Коллатца. |
| `LSystemWindow` | L-системы и черепашья графика, генератор форм. |
| `MandelbrotWindow` | 61 режим семейства Мандельброта и 61 — Жюлиа, гибриды; глубокий зум до 1e1000 (`FOLDED_*.md`, `PERPENDICULAR_FRACTALS.md`, `JULIA_GPU.md`). |
| `MathematicalLaboratoryWindow` | Математические лаборатории (модульная арифметика, Паскаль, Рекаман, филлотаксис, мозаики, Вороной, узлы, Фурье, Хладни и др.). |
| `NewtonPoolsWindow` | Бассейны Ньютона/Галлея/Хаусхолдера, глубокий зум до 1e1000. |
| `NovaWindow` | Семейство Nova, глубокий зум до 1e50. |
| `PhoenixWindow` | Семейство Phoenix, сверхглубокий зум до 1e1000. |
| `SerpinskyWindow` | Треугольник Серпинского и игра хаоса. |
| `SnowCrystalWindow` | Снежные кристаллы Рейтера (`SNOW_CRYSTALS.md`). |
| `TuringWindow` | Узоры Тьюринга 2D на ЦП/ГП (`TURING_PATTERNS.md`); объёмный вариант — режим `Fractal3DWindow`. |

### Редакторы параметров и палитр

`BuddhabrotPaletteWindow`, `DomainColoringPaletteWindow`, `DynamicPaletteWindow` (общий для режимов `DynamicSystemWindow` и `TuringWindow`), `Fractal3DPaletteWindow` и `Ifs3DPaletteWindow`, `Ifs3DTransformEditorWindow`, `Flame3DTransformEditorWindow`, `FlameTransformEditorWindow`, `GrayScottPaletteWindow`, `IfsTransformEditorWindow`, `InverseCollatzPaletteWindow`, `JuliaConstantPickerWindow`, `LSystemRandomizerWindow` и `LSystem3DRandomizerWindow`, `LyapunovPaletteWindow`, `MandelbrotPaletteWindow` (также Nova и Коллатц), `NewtonPaletteWindow` (также `BasinExplorerWindow`), `NovaParameterSelectorWindow`, `PhoenixParameterExplorerWindow`, `SerpinskyPaletteWindow`.

### Служебные окна

`AboutWindow`, `CloudSaveManagerWindow` и `CloudConflictWindow` (облако FractalCloud, `CLOUD.md`), `CrashDialogWindow`, `ColorPickerWindow`, `ImageExportManagerWindow`, `QuickSwitcherWindow`, `SaveManagerWindow` (по файлу на сохранение, PNG-превью рядом), `ThemeColorPickerWindow`, `ThemeEditorWindow`.

### Общие контролы

`ColorPickerPanel`, `ColorSelectorControl`, `SaveManagerControl`, `FractalControlPanel`, `UniformWrapPanel`, `NumericSpinner`, `DirectionCompass`, `TurtleTurnDial`, `PreparationOverlay`, `RenderProgressOverlay`, `TiledRenderSession` (прогрессивный тайловый предпросмотр двумерных окон), `DeepZoomViewport` (центр, зум, колесо и перетаскивание двумерных окон), `FullscreenToggle` (F11/Esc), `RenderThreadsBox` (выбор числа потоков), `PanelFields` (чтение числовых полей). Панели режимов `Fractal3DWindow`: `Buddhabrot4DEditor`, `LSystem3DEditor`, `LSystemRandomizerPanel`, `HyperbolicHoneycombEditor`, `HopfEditor`, `AbcFlowEditor`, `TuringReactionEditor`, редакторы моделирований `GrayScott3DEditor`, `Turing3DEditor`, `CahnHilliard3DEditor`, `Lenia3DEditor`, `Physarum3DEditor`, `Kobayashi3DEditor`, `Excitable3DEditor`, `GinzburgLandau3DEditor`, `SwiftHohenberg3DEditor`, `PhaseFieldCrystal3DEditor` с общим циклом `Fractal3DLiveSimulation`, стиль подписей `Fractal3DPanelStyles.xaml`.

## Пользовательские данные, сохранения и миграции

Все данные пользователя лежат в `%LOCALAPPDATA%\Fractal Explorer Studio` (`Infrastructure/AppPaths.cs`), а не рядом с exe. Папку открывает кнопка «Открыть папку с данными» в панели «Настройки» главного окна.

```text
Saves\<режим>\<имя>.json + <имя>.png   # по файлу на сохранение, превью рядом с ним
PointsOfInterest\<режим>\<имя>.png     # превью встроенных точек интереса (кэш)
Palettes\  Themes\  Settings\  Logs\   # палитры, темы, настройки, журнал ошибок
CatalogPreviews\                       # постоянный PNG-кэш всех превью каталога
shadercache\                           # кэш байткода всех GPU-шейдеров Direct3D
Backups\                               # копии файлов, изменённых миграциями
data-version.json                      # достигнутая версия каталога данных
```

Правила:

- Путь к любому пользовательскому файлу — только через `AppPaths` (`GetSavesDirectory`, `GetPaletteFile`, `GetThemeFile`, `GetSettingsFile`, `EnsureDirectoryFor`, `GetShaderCacheFile`). Не писать в `AppContext.BaseDirectory`. «Пересобрать шейдеры» пересоздаёт только `shadercache\`, другие данные не трогает.
- Сохранения режима — наследник `FractalSaveStore<TState>` (`Infrastructure/FractalSaveStore.cs`, классы `*SaveStore.cs`). Категория — имя каталога в `Saves\`; переименовывать её можно только вместе с миграцией.
- **Удаляемые и заменяемые сохранения и превью уходят в Корзину Windows** через `Infrastructure/RecycleBin.cs`; `File.Delete` — только для собственных временных файлов.
- `RecycleBin.ReplaceWith` заменяет файл атомарно через `<имя>.<идентификатор>.bak` и откатывает замену при отказе Корзины; оставшиеся `.bak` (путь — в исключении и журнале) не удалять как мусор.
- `MandelbrotPaletteManager` (также Nova и Коллатц) после ошибки загрузки блокирует запись библиотеки палитр; не снимать блокировку сохранением неполного списка поверх повреждённого файла. Палитры пишутся через временный файл с атомарной заменой.
- Изменение формата сохранения (переименование или перенос свойства, смена единиц) — `SaveFormatUpgrade` в конец списка в `Infrastructure/Migrations/SaveFormat.cs`; файл несёт `SaveFormatVersion`, апгрейды применяются при чтении. Выпущенные апгрейды не править и не удалять.
- Изменение раскладки данных, палитр или настроек — новый `IUserDataMigration` (версия +1) в конец списка `Infrastructure/Migrations/UserDataMigrator.cs`. Шаг повторяем после сбоя; файлы менять через `context.RewriteFile` или после `context.BackUp`. Миграции идут в `App.xaml.cs` до тем и окон; сбой не блокирует запуск. Шаг 1 переносит старую папку `Saves` рядом с exe (только чтение), шаг 2 создаёт кэш превью каталога.
- Проверки и инструменты не трогают настоящие данные: `AppPaths.OverrideDataRoot` перенаправляет каталог (Verification — в `bin/.../VerificationData`, ScreenshotGen — в `bin/.../ScreenshotData`), `RecycleBin.SendOverrideForTests` подменяет Корзину.

Статусы замечаний аудита от 5 октября 2026 — в `FractalExplorerWPF/AUDIT_STATUS.md`. Оба замечания галереи Жюлиа не актуальны после удаления `JuliaGalleryWindow` (`fe82c37`); не переносить их автоматически на карту выбора C.

## Отдельный эксперимент perturbation theory

По отдельному запросу пользователя создан самостоятельный WPF / .NET 10 эксперимент в `perturbation theory/`: упрощённый исследователь классического Мандельброта для сравнения perturbation-движка (`Core/Rendering/PerturbationRenderer.cs`) с классическим движком основной версии. Он не заменяет основную WPF-версию; обычные задачи по-прежнему относятся к `FractalExplorerWPF/`. Описание и команды — `perturbation theory/README.md`, численная проверка — `perturbation theory/Verification/Verification.csproj`.

## Скриншоты окон для документации

`FractalExplorerWPF/ScreenshotGen/` снимает каждое окно (пункты каталога, их редакторы, служебные окна) в PNG с рамкой Windows. Запускай, только когда пользователь просит (пере)сделать скриншоты для документации.

```powershell
dotnet build .\FractalExplorerWPF\ScreenshotGen\ScreenshotGen.csproj
dotnet run --project .\FractalExplorerWPF\ScreenshotGen\ScreenshotGen.csproj -- .\Pictures\WPF
```

Полный набор занимает несколько минут; аргумент `smoke` в конце делает 4 быстрых скриншота, `--only=<LaunchKey>` (например, `--only=Ikeda`) — один пункт с редакторами (со `smoke` не совмещается). Устройство инструмента и порядок поддержки — в `FractalExplorerWPF/ScreenshotGen/README.md`. При добавлении, удалении или переименовании окна обновляй и список внутри инструмента.

## Сборка и проверка

Команды запускай из корня репозитория:

```powershell
dotnet build .\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF.slnx
dotnet run --project .\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF.csproj
```

Минимальная обязательная проверка изменений — успешная сборка WPF-решения. Автоматические проверки без показа окон и захвата экрана — отдельный проект:

```powershell
dotnet run --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj -- <группа>
```

Без аргумента (или `all`) идёт полный набор — больше десяти минут. Первым аргументом выбирается группа; для многих после неё можно указать папку для PNG-снимков. **Что проверяет каждая группа — в [FractalExplorerWPF/Verification/README.md](FractalExplorerWPF/Verification/README.md)**; там же устройство проекта проверок (`Program.cs` — точка входа, группы — в отдельных `*Verification.cs`). Изменяя режим, прогоняй его группу.

- Сохранения, данные, облако, каталог: `manager`, `cloud`, `cloud-live` (только анонимные GET к настоящему серверу), `catalog [--out папка]`, `catalog-cache`, `numeric`, `shadercache`.
- Двумерные режимы и глубокий зум: `deep`, `extreme`, `phoenix`, `newton`, `mandelbrot-saves`, `julia-saves`, `julia-family`, `julia-gpu`, `perpendicular-family`, `folded-cubic`, `folded-polynomials`, `basins`, `planar`, `sprott`, `symmetric-icons`, `popcorn`, `hopalong`, `snow-crystal`, `lsystemrandom`, `tiled-preview [--record|--compare файл]` (попиксельная сверка тайлового предпросмотра до и после правки), `deep-viewport [--record|--compare файл]` (центр и зум окон с глубоким зумом), `poi [фильтр] [--out папка]` (при добавлении или правке точки интереса прогоняй её группу и смотри PNG), `poi-probe`.
- Gray–Scott и Тьюринг: `gray-scott`, `gray-scott-gpu`, `gray-scott-random`, `turing`, `turing-gpu`, `turing-reactions`.
- Трёхмерные режимы: `fractal3d`, `fractal3d-modes [--record|--compare файл]` (всё, что зависит от режима, без ГП), `preparation3d`, `picker-marker`, `terrain`, `kifs`, `flame3d`, `attractors`, `buddhabrot4d`, `lsystem3d`, `dla3d`, `lichtenberg3d`, `hopf`, `abc-flow`, `hyperbolic-honeycomb`, `implicit-surface`, `periodic-surfaces`, `lyapunov3d`, `gray-scott3d`, `turing3d`, `cahn-hilliard3d`, `lenia3d`, `physarum3d`, `physarum-random`, `kobayashi3d`, `kobayashi-random`, `excitable3d`, `ginzburg-landau3d`, `swift-hohenberg3d`, `phase-field-crystal3d` (нужен Direct3D 11).
- Замеры в Release (`dotnet run -c Release …`): `gray-scott-perf`, `turing-perf`, `cahn-hilliard-perf`, `catalog-perf [папка отчёта]` (не входят в `all` и не меняют настоящие данные).

Сценарии ручной проверки — в `FractalExplorerWPF/Verification/README.md`. Не редактируй `bin/` и `obj/` и не добавляй их содержимое в репозиторий.
