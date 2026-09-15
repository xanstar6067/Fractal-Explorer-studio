# Инструкции для агентов

## Главный приоритет: только WPF

Актуальная и единственная активно развиваемая версия проекта — **WPF-версия** в каталоге `FractalExplorerWPF/`.

Если пользователь не указал технологию или версию явно, всегда считай, что задача относится к WPF. Ищи, анализируй, изменяй, собирай и проверяй код только WPF-версии.

Каталог `FractalExplorer/` содержит устаревшую **WinForms-версию**. Она заморожена и больше не обновляется.

- Не вноси изменения в WinForms-версию по умолчанию.
- Не дублируй в нее исправления и новые возможности из WPF.
- Не используй ее solution или project для сборки и проверки обычных задач.
- Не выбирай WinForms только потому, что там уже существует похожая реализация.
- Использовать WinForms-код как справочный материал можно только при необходимости; итоговая реализация все равно должна находиться в WPF-проекте.
- Работать с `FractalExplorer/` разрешено только тогда, когда пользователь прямо и однозначно попросил изменить именно WinForms-версию.

При любом неоднозначном запросе выбирай WPF без дополнительного уточнения и кратко указывай это допущение в результате работы.

## Расположение актуального проекта

- Решение: `FractalExplorerWPF/FractalExplorerWPF/FractalExplorerWPF.slnx`
- Проект: `FractalExplorerWPF/FractalExplorerWPF/FractalExplorerWPF/FractalExplorerWPF.csproj`
- Исходный код: `FractalExplorerWPF/FractalExplorerWPF/FractalExplorerWPF/`
- Точка входа и ресурсы приложения: `App.xaml`, `App.xaml.cs`
- Главное окно: `MainWindow.xaml`, `MainWindow.xaml.cs`

WPF-проект использует `net10.0-windows`, nullable reference types и implicit usings. В `.csproj` присутствует framework reference на `Microsoft.WindowsDesktop.App.WindowsForms` для ограниченной совместимости с Windows API/компонентами; эта ссылка не меняет приоритет проекта и не означает, что legacy WinForms-версию нужно обновлять.

## Структура WPF-проекта

```text
FractalExplorerWPF/FractalExplorerWPF/FractalExplorerWPF/
├── App.xaml, App.xaml.cs          # запуск и ресурсы приложения
├── MainWindow.xaml, .xaml.cs      # каталог и главное окно
├── Views/                         # окна фракталов, редакторы и диалоги
├── Controls/                      # переиспользуемые WPF-контролы
├── Core/
│   ├── Math/                      # комплексная арифметика и парсеры выражений
│   └── Rendering/                 # движки, рендереры и растровая инфраструктура
├── Models/                        # модели UI, параметров, палитр и сохранений
├── Infrastructure/
│   ├── ColorPicking/              # выбор цвета и экранная пипетка
│   └── Serialization/             # JSON-конвертеры
│                                  # также хранилища, настройки, экспорт и сервисы
├── Theming/                       # темы, стили и их хранение
├── Assets/                        # иконки, превью и изображения
└── Properties/                    # настройки проекта и ресурсов
```

Основные границы ответственности:

- Разметка и визуальное состояние окон относятся к `Views/` и XAML.
- Общие элементы интерфейса относятся к `Controls/`.
- Математика и вычислительный рендеринг не должны зависеть от UI и относятся к `Core/`.
- DTO, состояния и параметры относятся к `Models/`.
- Сериализация, файловые хранилища, настройки и интеграция с ОС относятся к `Infrastructure/`.
- Общие ресурсы оформления относятся к `Theming/` и `Assets/`.

## Каталог всех WPF-окон

В WPF нет классов `Form`: интерфейс состоит из классов `Window` и вложенных `UserControl`. Ниже перечислены **все актуальные WPF-окна**. WinForms-формы из замороженного каталога `FractalExplorer/` намеренно не включены.

Для каждого окна с именем `NameWindow` основная разметка находится в `Views/NameWindow.xaml`, а логика событий и взаимодействия — в `Views/NameWindow.xaml.cs`. Исключение — главное окно, чьи файлы лежат в корне WPF-проекта.

### Точка входа и основные исследовательские окна

| Окно и файлы | Назначение |
|---|---|
| `MainWindow.xaml` / `MainWindow.xaml.cs` | Главный каталог фракталов: дерево разделов, карточка выбранного режима, предпросмотр, выбор стратегии тайлового рендера и запуск соответствующего окна. Список доступных пунктов задаёт `Models/FractalCatalog.cs`. |
| `Views/ApollonianWindow.xaml` / `.xaml.cs` | Исследователь Аполлоновой прокладки: рекурсивная упаковка касающихся окружностей, параметры построения и раскраски, навигация, сохранения и экспорт. Связаны `Models/ApollonianModels.cs`, `Core/Rendering/ApollonianRenderer.cs`, `Infrastructure/ApollonianSaveStore.cs`. |
| `Views/BuddhabrotWindow.xaml` / `.xaml.cs` | Рендер Буддаброта и Анти-Буддаброта с накоплением плотности орбит, каналами/экспозицией, навигацией, сохранениями и экспортом. Связаны `Models/BuddhabrotModels.cs`, `Core/Rendering/BuddhabrotRenderer.cs`, `Infrastructure/BuddhabrotSaveStore.cs`. |
| `Views/CollatzWindow.xaml` / `.xaml.cs` | Комплексный фрактал Коллатца: параметры итераций и области, рендер, навигация, палитра, сохранения и экспорт. Поддерживает глубокий зум до 1e50 — выше 1e10 орбита считается напрямую в произвольной точности, а центр ведётся в `BigFloat`. Связаны `Models/CollatzModels.cs`, `Core/Rendering/CollatzRenderer.cs`, `Core/Rendering/CollatzRenderer.DeepZoom.cs`, `Core/Math/BigFloatMath.cs`, `Core/Math/ComplexBigFloat.cs`, `Infrastructure/CollatzSaveStore.cs`. |
| `Views/DlaWindow.xaml` / `.xaml.cs` | DLA (диффузионно-ограниченная агрегация): моделирование роста кластера случайными частицами, отображение процесса, настройки, сохранения и экспорт. Связаны `Models/DlaModels.cs`, `Core/Rendering/DlaRenderer.cs`, `Infrastructure/DlaSaveStore.cs`. |
| `Views/DomainColoringWindow.xaml` / `.xaml.cs` | Domain Coloring комплексных функций: ввод/выбор функции, раскраска аргумента и модуля, контуры, навигация, сохранения и экспорт. Связаны `Models/DomainColoringModels.cs`, `Core/Rendering/DomainColoringRenderer.cs`, `Core/Math/CompiledComplexExpression.cs`, `Infrastructure/DomainColoringSaveStore.cs`. |
| `Views/DynamicSystemWindow.xaml` / `.xaml.cs` | Универсальное окно динамических систем. Обслуживает режимы Ляпунова, Лоренца, Рёсслера, логистического отображения, бифуркации, Хенона, Икэды и странных 2D-аттракторов (Clifford, Peter de Jong, Tinkerbell, Gumowski–Mira). Связаны `Models/DynamicSystemModels.cs`, `Core/Rendering/DynamicSystemRenderer.cs`, `Core/Rendering/Attractor2DRenderer.cs`, `Infrastructure/DynamicSystemStores.cs`. |
| `Views/FlameWindow.xaml` / `.xaml.cs` | Стохастический Fractal Flame: набор аффинных преобразований и вариаций, HDR-накопление, тональная коррекция, случайная генерация, сохранения и экспорт. Связаны `Models/FlameModels.cs`, `Core/Rendering/FlameRenderer.cs`, `Core/Rendering/FlameVariations.cs`, `Infrastructure/FlameSaveStore.cs`. |
| `Views/GrayScottWindow.xaml` / `.xaml.cs` | Интерактивная реакционно‑диффузионная система Gray–Scott: эволюция двух концентраций в реальном времени, пресеты структур, добавление реагента кистью, собственные палитры, сохранения и экспорт текущего кадра. Связаны `Models/GrayScottModels.cs`, `Core/Rendering/GrayScottRenderer.cs`, `Infrastructure/GrayScottSaveStore.cs`. |
| `Views/IfsWindow.xaml` / `.xaml.cs` | IFS Барнсли/Хейуэя: стохастический рендер системы аффинных преобразований, пресеты и рандомизация, сохранения и экспорт. Связаны `Models/IfsModels.cs`, `Core/Rendering/IfsRenderer.cs`, `Infrastructure/IfsSaveStore.cs`. |
| `Views/InverseCollatzTreeWindow.xaml` / `.xaml.cs` | Визуализация обратного дерева целочисленного Коллатца: радиальная/древовидная раскладка, фильтры, раскраска и анимация роста. Связаны `Models/InverseCollatzModels.cs`, `Core/Rendering/InverseCollatzTreeRenderer.cs`, `Infrastructure/InverseCollatzSaveStore.cs`. |
| `Views/JuliaGalleryWindow.xaml` / `.xaml.cs` | Пакетная галерея множеств Жюлиа по сетке констант `C`; поддерживает классический Julia и Julia Burning Ship и открывает выбранный вариант для исследования. Использует модели и рендерер семейства Мандельброта. |
| `Views/LSystemWindow.xaml` / `.xaml.cs` | Редактор L-систем и черепашьей графики: аксиома, правила, пресеты, параметры интерпретации и анимированное построение. Связан `Models/LSystemModels.cs` и `Core/Rendering/LSystemEngine.cs`. |
| `Views/MandelbrotWindow.xaml` / `.xaml.cs` | Универсальный исследователь семейства Мандельброта/Жюлиа: Mandelbrot, Burning Ship, Tricorn, Buffalo, Celtic, Simonobrot, generalized Multibrot, Julia и Julia Burning Ship; масштабирование, варианты окрашивания, палитры, сохранения и экспорт. Все варианты (Mandelbrot, Julia, отражённые Burning Ship/Tricorn/Buffalo/Celtic и их Julia, Multibrot и Симоноброт целой степени) считаются одним пертурбационным движком с BLA и поддерживают глубокий зум до 1e1000: зум и сетка кадра ведутся в `FloatExp` (мантисса double + 32-битная двоичная экспонента), точность мантиссы опорной орбиты и центра адаптивна (`BigFloat` на `BigMantissa` без аллокаций), δ на пиксель и производная Distance Estimation за ~1e72 переходят в `FloatExp`. Дробная и отрицательная степень Multibrot/Симоноброта остаются на `decimal` и фактически упираются в его точность (~1e28). Ввод зума принимает научную нотацию, колесо мыши с Ctrl даёт шаг ×10. Кнопка «Навести на минимандельброт» уточняет центр методом Ньютона (`Core/Rendering/MandelbrotNewtonZoom.cs`). Связаны `Models/MandelbrotModels.cs`, `Core/Rendering/MandelbrotFamilyRenderer.cs` (и частичные `.DeepZoom.cs`, `.Bla.cs`, `.RealBla.cs`, `.ExactReference.cs`), `Core/Rendering/MandelbrotNewtonZoom.cs`, `Core/Rendering/MandelbrotTileScheduler.cs`, `Core/Math/FloatExp.cs`, `Core/Math/BigFloat.cs`, `Core/Math/BigMantissa.cs`, `Infrastructure/MandelbrotSaveStore.cs`. |
| `Views/MathematicalLaboratoryWindow.xaml` / `.xaml.cs` | Универсальное окно математических лабораторий: арифметика по модулю, Паскаль modulo N, рациональные числа, геометрия простых, последовательность Рекамана, филлотаксис, инверсия окружностей/Мёбиус, апериодические мозаики, гиперболическая геометрия, диаграммы Вороного/релаксация Ллойда, торические и Лиссажу-узлы/косы, Brownian motion/Lévy flights, Kleinian/Schottky groups, Fourier Epicycles и фигуры Хладни/интерференция. Связаны `Models/MathematicalLaboratoryModels.cs`, `Core/Rendering/MathematicalLaboratoryRenderer.cs`, `Core/Rendering/AdvancedMathematicalLaboratoryRenderer.cs`, `Infrastructure/MathematicalLaboratorySaveStore.cs`. |
| `Views/NewtonPoolsWindow.xaml` / `.xaml.cs` | Бассейны притяжения корней для методов Newton, Halley и Householder: выражение, параметры итераций, корни, окрашивание, сохранения и экспорт. Связаны `Models/NewtonModels.cs`, `Core/Rendering/NewtonPoolsEngine.cs`, `Core/Rendering/NewtonRootFinder.cs`, `Core/Math/NewtonExpressionParser.cs`, `Infrastructure/NewtonSaveStore.cs`. |
| `Views/NovaWindow.xaml` / `.xaml.cs` | Семейство Nova в режимах Mandelbrot и Julia: комплексная степень, начальное значение, релаксация, константа `C`, палитра, сохранения и экспорт. Поддерживает глубокий зум до 1e50: выше 1.5e9 кадр считает пертурбационный движок с опорной орбитой в `BigFloat`, а центр ведётся в `BigFloat`. Целая степень раскладывается биномиально, дробная и комплексная — через `log1p`/`expm1`. Связаны `Models/NovaModels.cs`, `Core/Rendering/NovaRenderer.cs`, `Core/Rendering/NovaRenderer.DeepZoom.cs`, `Core/Rendering/NovaRenderer.ExactReference.cs`, `Core/Math/BigFloatMath.cs`, `Core/Math/ComplexBigFloat.cs`, `Infrastructure/NovaSaveStore.cs`. |
| `Views/PhoenixWindow.xaml` / `.xaml.cs` | Исследователь семейства Phoenix: динамическая и параметрическая плоскости, комплексные `C1`/`C2`, обобщённые степени, классические и экспериментальные варианты формулы, расширенные окраски, навигация, сохранения и экспорт. Поддерживает сверхглубокий зум до 1e1000: выше 1.5e9 кадр считает пертурбационный движок с опорной орбитой в `BigFloat`; зум и сетка кадра ведутся в `FloatExp`, центр — в `BigFloat` с адаптивной точностью. За порогом `2^(900/P)` (P — старшая степень; ≈3e135 для a = 2) включается гибридное ядро: δ в `FloatExp`, пока мало, дальше double, плюс линейный пропуск начала орбиты (матрицы переноса и префиксы метрик окраски считаются один раз на кадр). Ввод зума принимает научную нотацию, колесо мыши с Ctrl даёт шаг ×10, с Shift — ×1.05. Кнопка «Навести на ядро» (только классический вариант) уточняет центр методом Ньютона до точки, чья орбита проходит через ноль. Связаны `Models/PhoenixModels.cs`, `Core/Rendering/PhoenixRenderer.cs` (и частичные `.DeepZoom.cs`, `.Extended.cs`, `.ExactReference.cs`), `Core/Rendering/PhoenixNewtonZoom.cs`, `Core/Math/FloatExp.cs`, `Core/Math/BigFloat.cs`, `Infrastructure/PhoenixSaveStore.cs`. |
| `Views/SerpinskyWindow.xaml` / `.xaml.cs` | Треугольник Серпинского и режим игры хаоса: параметры построения, анимация/рендер, палитра, сохранения и экспорт. Связаны `Models/SerpinskySaveState.cs`, `Models/SerpinskyPalette.cs`, `Core/Rendering/FractalSerpinskyEngine.cs`, `Infrastructure/SerpinskySaveStore.cs`. |

### Редакторы параметров и палитр

| Окно и файлы | Назначение |
|---|---|
| `Views/BuddhabrotPaletteWindow.xaml` / `.xaml.cs` | Менеджер палитр и цветовых каналов Буддаброта; вызывается из `BuddhabrotWindow`. |
| `Views/DynamicPaletteWindow.xaml` / `.xaml.cs` | Общий менеджер динамических палитр, используемый режимами `DynamicSystemWindow` (в частности логистическим отображением). |
| `Views/FlameTransformEditorWindow.xaml` / `.xaml.cs` | Редактор списка аффинных преобразований, весов, цветов и вариаций Fractal Flame; вызывается из `FlameWindow`. |
| `Views/GrayScottPaletteWindow.xaml` / `.xaml.cs` | Менеджер сохраняемых палитр концентрации Gray–Scott: встроенные и пользовательские градиенты, гамма, копирование и случайная генерация; вызывается из `GrayScottWindow`. |
| `Views/IfsTransformEditorWindow.xaml` / `.xaml.cs` | Редактор аффинных преобразований и вероятностей IFS; вызывается из `IfsWindow`. |
| `Views/InverseCollatzPaletteWindow.xaml` / `.xaml.cs` | Менеджер палитр обратного дерева Коллатца; вызывается из `InverseCollatzTreeWindow`. |
| `Views/JuliaConstantPickerWindow.xaml` / `.xaml.cs` | Интерактивный выбор комплексной константы `C` на карте исходного множества для Julia-вариантов: быстрый зум (уже посчитанный кадр сразу растягивается под новый вид, синхронно с маркером; Ctrl — ×10, Shift — ×1.05), перемещение средней кнопкой, маркер выбранной `C` — небольшое перекрестье; вызывается из `MandelbrotWindow`. |
| `Views/LyapunovPaletteWindow.xaml` / `.xaml.cs` | Специализированный менеджер палитр карты экспоненты Ляпунова; вызывается из режима Lyapunov окна `DynamicSystemWindow`. |
| `Views/MandelbrotPaletteWindow.xaml` / `.xaml.cs` | Менеджер палитр семейства Мандельброта; также переиспользуется окном `NovaWindow`. |
| `Views/NewtonPaletteWindow.xaml` / `.xaml.cs` | Настройка цветов корней и палитр бассейнов Ньютона; вызывается из `NewtonPoolsWindow`. |
| `Views/NovaParameterSelectorWindow.xaml` / `.xaml.cs` | Выбор точки/константы `C` для Nova Mandelbrot на интерактивной карте; вызывается из `NovaWindow`. |
| `Views/PhoenixParameterExplorerWindow.xaml` / `.xaml.cs` | Двухпанельный исследователь комплексных параметрических плоскостей `C1` и `C2` для Phoenix с масштабированием, навигацией и переходом выбранного `C1` в динамическую плоскость; вызывается из `PhoenixWindow`. |
| `Views/SerpinskyPaletteWindow.xaml` / `.xaml.cs` | Менеджер палитр треугольника Серпинского; вызывается из `SerpinskyWindow`. |

### Общие служебные окна

| Окно и файлы | Назначение |
|---|---|
| `Views/AboutWindow.xaml` / `.xaml.cs` | Диалог «О программе»: название, версия и справочная информация. |
| `Views/CrashDialogWindow.xaml` / `.xaml.cs` | Диалог восстановления после необработанного исключения: краткое описание, разворачиваемые подробности, копирование, открытие журнала и выбор «продолжить работу» / «закрыть приложение». Показывается из `Infrastructure/GlobalExceptionHandler.cs`; журнал пишет `Infrastructure/CrashLogger.cs`. |
| `Views/ColorPickerWindow.xaml` / `.xaml.cs` | Полноразмерный общий диалог выбора цвета, построенный вокруг `Controls/ColorPickerPanel`. |
| `Views/ImageExportManagerWindow.xaml` / `.xaml.cs` | Универсальный менеджер экспорта изображения: размеры, SSAA, формат/путь, прогресс, отмена и вызов переданного render callback. Конфигурация находится в `Infrastructure/ImageExportConfiguration.cs`. |
| `Views/QuickSwitcherWindow.xaml` / `.xaml.cs` | Быстрый переключатель по каталогу фракталов (Ctrl+K из `MainWindow`): фильтрация по названию и категории, навигация стрелками, запуск по Enter. |
| `Views/SaveManagerWindow.xaml` / `.xaml.cs` | Универсальная оболочка менеджера состояний: сохраняет текущий кадр полотна как PNG-превью, открывает, загружает и удаляет сохранения; пересчёт превью запускается только вручную с прогрессом и отменой. Использует `Controls/SaveManagerControl` и конфигурации из `Infrastructure/SaveManagerConfigurations.cs`. |
| `Views/ThemeColorPickerWindow.xaml` / `.xaml.cs` | Компактный выбор одного цвета специально для редактора темы. |
| `Views/ThemeEditorWindow.xaml` / `.xaml.cs` | Создание, редактирование, импорт, выбор и сохранение тем оформления приложения. Основная логика тем находится в `Theming/`. |

### Общие составные элементы интерфейса

| Контрол и файлы | Назначение |
|---|---|
| `Controls/ColorPickerPanel.xaml` / `.xaml.cs` | Общая панель выбора цвета: каналы, ввод значения, предпросмотр и экранная пипетка. |
| `Controls/ColorSelectorControl.xaml` / `.xaml.cs` | Компактный переиспользуемый селектор цвета для панелей параметров. |
| `Controls/SaveManagerControl.xaml` / `.xaml.cs` | Переиспользуемое содержимое менеджера сохранений: список, PNG-превью текущего кадра, ручной пересчёт с прогрессом, временем и отменой; выбор записи не запускает вычислений. |
| `Controls/FractalControlPanel.cs` | Программно создаваемая общая панель параметров фрактала. |
| `Controls/RenderProgressOverlay.cs` | Общий оверлей состояния и прогресса рендера. |

При добавлении, удалении, переименовании или изменении назначения WPF-окна либо общего контрола обязательно обновляй этот каталог в том же изменении.

## Отдельный эксперимент perturbation theory

По отдельному запросу пользователя создан самостоятельный WPF / .NET 10 эксперимент в `perturbation theory/`. Он не заменяет основную WPF-версию; обычные задачи по-прежнему относятся к `FractalExplorerWPF/`.

| Окно и файлы | Назначение |
|---|---|
| `perturbation theory/perturbation theory/perturbation theory/MainWindow.xaml` / `.xaml.cs` | Упрощённый исследователь классического Мандельброта: переключение между perturbation (три режима точности double/decimal) и классическим движком основной WPF-версии (автоматическая точность), быстрые масштабирование и перемещение полотна, фиксированные палитры, плавная и дискретная окраска, время рендера для сравнения. Сохранений и редактора палитр нет. |

Движок находится в `perturbation theory/perturbation theory/perturbation theory/Core/Rendering/PerturbationRenderer.cs`. Описание, ограничения точности и команды запуска — в `perturbation theory/README.md`. Численная проверка — отдельный консольный проект `perturbation theory/Verification/Verification.csproj`.

## Скриншоты окон для документации

`FractalExplorerWPF/ScreenshotGen/` — консольный инструмент, который открывает
каждое WPF-окно приложения по одному (далеко за пределами экрана, невидимо
для пользователя) и сохраняет его как PNG с настоящей рамкой Windows. Он
покрывает буквально весь каталог из «Каталога всех WPF-окон» выше: все пункты
`FractalCatalog.cs`, их «уникальные инструменты» (редакторы палитр,
трансформаций, выбор параметров) и общие служебные окна.

Используй его, когда пользователь просит (пере)сделать скриншоты для README
или аналогичной документации — после заметных визуальных изменений (тема,
раскладка) или после добавления нового окна/фрактала/лаборатории. Не
запускай его по своей инициативе для обычных задач правки кода — это
самостоятельный, не входящий в основное решение инструмент, вызываемый явно:

```powershell
dotnet build .\FractalExplorerWPF\ScreenshotGen\ScreenshotGen.csproj
dotnet run --project .\FractalExplorerWPF\ScreenshotGen\ScreenshotGen.csproj -- .\Pictures\V2_0_WPF
```

Полный набор — около 70 окон и несколько минут; аргумент `smoke` в конце
команды запускает только 4 быстрых скриншота для проверки, что инструмент
работает. Устройство инструмента (подмена `Application.ResourceAssembly`,
захват через склейку `PrintWindow`+`RenderTargetBitmap`, ожидание конца
рендера по приватному `_isRendering`) и порядок поддержки при изменениях в
приложении — в `FractalExplorerWPF/ScreenshotGen/README.md`. При добавлении,
удалении или переименовании WPF-окна обновляй заодно и список внутри
инструмента (см. его README), не только таблицы окон в этом файле.

## Сборка и проверка

Запускай команды из корня репозитория:

```powershell
dotnet build .\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF.slnx
dotnet run --project .\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF.csproj
```

Минимальная обязательная автоматическая проверка изменений — успешная сборка WPF-решения. Для менеджера сохранений и глубокого зума есть отдельная проверка без показа окон и без захвата экрана:

```powershell
dotnet run --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj
```

Полный набор идёт больше десяти минут. Первым аргументом можно ограничить группу:
`manager` — только менеджер сохранений, `deep` — глубокий зум, `extreme` — сверхглубокий зум
Мандельброта и Феникса (`FloatExp`-зум до 1e1000) и поиск ядра по Ньютону, `phoenix` — только
глубокий и сверхглубокий зум Феникса:

```powershell
dotnet run --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj -- extreme
```

Сценарии ручной проверки описаны в `FractalExplorerWPF/Verification/README.md`. Не редактируй `bin/` и `obj/` и не добавляй их содержимое в репозиторий.
