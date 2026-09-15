# Fractal Explorer WPF

[Русский](#русский) · [English](#english)

<a id="русский"></a>

## Русский

Интерактивная лаборатория фракталов, динамических систем и математической геометрии для Windows. Актуальная версия проекта построена на WPF и объединяет исследование комплексных множеств, стохастические визуализации, математические лаборатории, редакторы параметров, палитры, сохранения и экспорт изображений в одном приложении.

<p align="center">
  <img src="./Pictures/V2_0_WPF/00-main-catalog.png" alt="Каталог фракталов Fractal Explorer WPF" width="900">
</p>

> WPF — основное направление разработки. Предыдущая реализация на Windows Forms сохранена в каталоге `FractalExplorer` как legacy-версия.

### Версия 2.0

Каталог визуализаций выріс почти вдвое по сравнению с предыдущей версией — 49 пунктов вместо 33, в первую очередь за счёт нового раздела **«Математические лаборатории»** (18 модулей: теория чисел, геометрия и преобразования, комплексный анализ, генеративная геометрия, стохастические процессы, гармоники и волны). Параллельно проделан большой объём внутренних оптимизаций рендеринга:

- пертурбационный движок с BLA (bilinear approximation) и адаптивной точностью опорной орбиты для всего семейства Мандельброта — глубокий зум до **1e1000** на `FloatExp` и `BigFloat`;
- аналогичный сверхглубокий зум для фрактала **Phoenix** (до 1e1000, с гибридным ядром `FloatExp`/`double` за экстремальными порогами);
- глубокий зум до **1e50** для **Коллатца** и **Nova** через `BigFloat`/`ComplexBigFloat`;
- метод Ньютона для автонаведения на минимандельброты и на ядро Феникса;
- переработанная `BigMantissa` без лишних аллокаций.

Ниже — скриншот-демонстрация буквально каждого окна приложения: все фракталы и лаборатории, их уникальные редакторы параметров и палитр, а также общие служебные окна.

### Возможности

- **54 пункта каталога:** 52 визуализации (27 фракталов комплексной динамики и стохастики, 9 динамических систем, 18 математических лабораторий) и 2 галереи констант Julia.
- **Интерактивное исследование:** масштабирование колесом мыши, перемещение холста, сброс вида и полноэкранный режим.
- **Асинхронный рендеринг на CPU:** настройка числа потоков, отмена вычисления, индикаторы прогресса и восемь схем появления плиток.
- **Гибкое окрашивание:** встроенные и пользовательские палитры, плавные и дискретные режимы, Histogram, Orbit Trap, Stripe Average и Distance Estimation с псевдо-3D освещением для семейства Mandelbrot.
- **Сверхглубокий зум:** пертурбационные движки с BLA на `FloatExp`/`BigFloat` для Mandelbrot-семейства и Phoenix (до 1e1000), а также для Nova и Коллатца (до 1e50).
- **Математические лаборатории:** отдельный раздел из 18 интерактивных модулей — от теории чисел и апериодических мозаик до узлов, эпициклов Фурье и фигур Хладни.
- **Сохранение исследований:** параметры фрактала, превью и точки интереса хранятся в JSON и восстанавливаются через менеджер сохранений.
- **Экспорт изображений:** произвольное разрешение, пресеты вплоть до 8K, PNG/JPG/BMP, SSAA, Bicubic и Lanczos 3.
- **Настраиваемый WPF-интерфейс:** встроенные темы, системная тема Windows, редактор цветов и экранная пипетка.

### Каталог визуализаций

| Раздел | Доступные модули |
| --- | --- |
| Множество Мандельброта | Mandelbrot, Burning Ship, Tricorn (Mandelbar), Buffalo, Celtic Mandelbrot, Simonobrot, Generalized Mandelbrot |
| Множество Жюлиа | Julia, Julia Burning Ship и две галереи констант `C` |
| Итерируемые функции | Newton Pools+, бассейны методов Мюллера, Лагерра и секущих, бассейны рациональных отображений и периодических циклов, Phoenix, Collatz, Nova Mandelbrot, Nova Julia, Buddhabrot / Anti-Buddhabrot |
| Итерируемые и самоподобные | IFS Барнсли / Хейуэя, Фрактальное пламя, Серпинский — игра хаоса |
| Геометрические и стохастические фракталы | Аполлонова прокладка, DLA — диффузионно-ограниченная агрегация |
| Динамические системы и хаос | Lyapunov, Logistic Map, Bifurcation, Lorenz, Rössler, Hénon, Ikeda, странные 2D-аттракторы, Gray–Scott |
| Математические лаборатории | Арифметика по модулю, Паскаль mod N, рациональные числа, геометрия простых, Рекаман, обратное дерево Коллатца, филлотаксис, инверсия окружностей / Мёбиус, апериодические мозаики, гиперболическая геометрия, Вороной / Ллойд, узлы, Domain Coloring, Kleinian / Schottky, L-системы, Brownian motion / Lévy flights, эпициклы Фурье, фигуры Хладни |

### Интерфейс

Ниже — скриншот каждого окна приложения: рабочие окна фракталов и лабораторий, их уникальные редакторы параметров и палитр, а также общие служебные диалоги.

#### Семейство Мандельброта

Общий пертурбационный движок с BLA обслуживает классическое множество и все его отражённые/обобщённые варианты, целочисленный и дробный Multibrot, а также интерактивный выбор константы `C` для перехода в Julia.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-mandelbrot.png" width="260"><br><sub>Классический Мандельброт</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-burning-ship.png" width="260"><br><sub>Горящий Корабль</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-tricorn.png" width="260"><br><sub>Трикорн (Mandelbar)</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-buffalo.png" width="260"><br><sub>Буффало</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-celtic.png" width="260"><br><sub>Кельтский Мандельброт</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-simonobrot.png" width="260"><br><sub>Симоноброт</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-generalized.png" width="260"><br><sub>Обобщённый Мандельброт</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-palette-editor.png" width="260"><br><sub>Редактор палитры</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/julia-constant-picker.png" width="260"><br><sub>Выбор константы C для Julia</sub></td>
</tr>
</table>

#### Семейство Жюлиа

Классическое Julia и его вариант «Горящий корабль» доступны как самостоятельные окна, так и в виде пакетной галереи констант `C` на сетке.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-julia.png" width="260"><br><sub>Классическое Жюлиа</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-julia-burning-ship.png" width="260"><br><sub>Жюлиа — Горящий Корабль</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/julia-gallery.png" width="260"><br><sub>Галерея констант C (Жюлиа)</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/julia-burning-ship-gallery.png" width="260"><br><sub>Галерея констант C (Горящий Корабль)</sub></td>
</tr>
</table>

#### Бассейны притяжения и другие итерационные семейства

Newton Pools+ поддерживает методы Newton, Halley и Householder с собственной палитрой корней. Рядом — ещё пять исследователей бассейнов в общем окне: методы Мюллера (тройка начальных приближений), Лагерра (с картой расхождения с Ньютоном) и секущих (включая срезы четырёхмерного пространства состояний), а также рациональные отображения и периодические циклы, где точки раскрашиваются по конечному притягивающему циклу. Phoenix — двухпанельный исследователь параметрических плоскостей `C1`/`C2` со сверхглубоким зумом. Nova и Коллатца — комплексные обобщения с глубоким зумом до 1e50.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/newton-pools.png" width="260"><br><sub>Бассейны Ньютона+</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/newton-palette-editor.png" width="260"><br><sub>Палитра корней Ньютона</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/phoenix.png" width="260"><br><sub>Фрактал Феникс</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/phoenix-parameter-explorer.png" width="260"><br><sub>Исследователь плоскостей C1/C2 Феникса</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/nova-mandelbrot.png" width="260"><br><sub>Nova Mandelbrot</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/nova-julia.png" width="260"><br><sub>Nova Julia</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/nova-parameter-selector.png" width="260"><br><sub>Выбор параметра Nova</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/collatz.png" width="260"><br><sub>Фрактал Коллатца</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/buddhabrot.png" width="260"><br><sub>Буддаброт / Анти-Буддаброт</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/buddhabrot-palette-editor.png" width="260"><br><sub>Палитра Буддаброта</sub></td>
</tr>
</table>

#### Итерируемые, самоподобные, геометрические и стохастические фракталы

IFS и Fractal Flame включают редакторы аффинных преобразований. Серпинский в режиме «игра хаоса», Аполлонова прокладка с раскраской по глубине/кривизне/родительской ветви и DLA с растущим кластером частиц дополняют раздел.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/ifs.png" width="260"><br><sub>IFS Барнсли / Хейуэя</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/ifs-transform-editor.png" width="260"><br><sub>Редактор преобразований IFS</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/flame.png" width="260"><br><sub>Фрактальное пламя</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/flame-transform-editor.png" width="260"><br><sub>Редактор трансформаций Flame</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/serpinsky-chaos.png" width="260"><br><sub>Серпинский — игра хаоса</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/serpinsky-palette-editor.png" width="260"><br><sub>Палитра Серпинского</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/apollonian.png" width="260"><br><sub>Аполлонова прокладка</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dla.png" width="260"><br><sub>DLA</sub></td>
</tr>
</table>

#### Динамические системы и хаос

Общее окно обслуживает карту Ляпунова, орбиты логистического отображения, диаграмму бифуркации, аттракторы Лоренца и Рёсслера, карты Хенона и Икэды, а также облака странных 2D-аттракторов (Clifford, Peter de Jong, Tinkerbell, Gumowski–Mira). Gray–Scott — отдельная живая реакционно-диффузионная симуляция.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-lyapunov.png" width="260"><br><sub>Экспонента Ляпунова</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lyapunov-palette-editor.png" width="260"><br><sub>Палитра Ляпунова</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-logistic-map.png" width="260"><br><sub>Логистическое отображение</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-bifurcation.png" width="260"><br><sub>Диаграмма бифуркации</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-lorenz.png" width="260"><br><sub>Аттрактор Лоренца</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynamic-palette-editor.png" width="260"><br><sub>Общая палитра динамических систем</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-rossler.png" width="260"><br><sub>Аттрактор Рёсслера</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-henon.png" width="260"><br><sub>Карта Хенона</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-ikeda.png" width="260"><br><sub>Отображение Икэды</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-attractors2-d.png" width="260"><br><sub>Странные 2D-аттракторы</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/gray-scott.png" width="260"><br><sub>Gray–Scott</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/gray-scott-palette-editor.png" width="260"><br><sub>Палитра Gray–Scott</sub></td>
</tr>
</table>

#### Математические лаборатории

Новый в версии 2.0 раздел объединяет 18 интерактивных модулей на общем движке: теория чисел и дискретные структуры, геометрия и преобразования, комплексный анализ, генеративная геометрия, стохастические процессы, гармоники и волны.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-modular-arithmetic.png" width="260"><br><sub>Арифметика по модулю</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-pascal-modulo.png" width="260"><br><sub>Треугольник Паскаля mod N</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-rational-numbers.png" width="260"><br><sub>Лаборатория рациональных чисел</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-prime-geometry.png" width="260"><br><sub>Геометрия простых чисел</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-recaman-sequence.png" width="260"><br><sub>Последовательность Рекамана</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/inverse-collatz-tree.png" width="260"><br><sub>Обратное дерево Коллатца</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/inverse-collatz-palette-editor.png" width="260"><br><sub>Палитра обратного дерева</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-phyllotaxis.png" width="260"><br><sub>Филлотаксис</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-circle-inversion.png" width="260"><br><sub>Инверсия окружностей / Мёбиус</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-aperiodic-tilings.png" width="260"><br><sub>Апериодические мозаики</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-hyperbolic-geometry.png" width="260"><br><sub>Гиперболическая геометрия</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-voronoi-lloyd.png" width="260"><br><sub>Вороной / релаксация Ллойда</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-knot-studio.png" width="260"><br><sub>Студия узлов</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/domain-coloring.png" width="260"><br><sub>Domain Coloring</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-kleinian-schottky.png" width="260"><br><sub>Kleinian / Schottky groups</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lsystem.png" width="260"><br><sub>L-системы</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-stochastic-motion.png" width="260"><br><sub>Brownian motion / Lévy flights</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-fourier-epicycles.png" width="260"><br><sub>Fourier Epicycles</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-chladni-wave-interference.png" width="260"><br><sub>Фигуры Хладни / интерференция</sub></td>
</tr>
</table>

#### Общие служебные окна

Каталог, быстрый переключатель, редактор тем со своим выбором цвета, общий выбор цвета, менеджеры сохранений и экспорта изображений, а также диалог восстановления после ошибки — используются одинаково во всех фракталах и лабораториях.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/00-quick-switcher.png" width="260"><br><sub>Быстрый переключатель (Ctrl+K)</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/00-theme-editor.png" width="260"><br><sub>Редактор тем</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/00-theme-color-picker.png" width="260"><br><sub>Выбор цвета темы</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/00-color-picker.png" width="260"><br><sub>Общий выбор цвета</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/save-manager.png" width="260"><br><sub>Менеджер сохранений</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/image-export-manager.png" width="260"><br><sub>Менеджер экспорта изображений</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/00-about.png" width="260"><br><sub>О программе</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/00-crash-dialog.png" width="260"><br><sub>Восстановление после ошибки</sub></td>
</tr>
</table>

### Управление

| Действие | Управление |
| --- | --- |
| Масштабирование относительно курсора | Колесо мыши над холстом |
| Перемещение области просмотра | Перетаскивание левой кнопкой мыши |
| Полноэкранный режим | `F11` |
| Выход из полноэкранного режима | `Esc` |
| Скрытие панели параметров | Кнопка в левом верхнем углу холста |
| Быстрый переключатель каталога | `Ctrl+K` |

Конкретные параметры, доступные режимы окрашивания и дополнительные интерактивные карты зависят от выбранного фрактала или лаборатории.

### Сохранения и экспорт

Менеджеры сохранений запоминают формулу, координаты, масштаб, качество рендера, палитру и точки интереса. Для записей создаются превью, а данные хранятся локально в JSON — по файлу на сохранение, с превью рядом, в папке `%LOCALAPPDATA%\Fractal Explorer Studio` (её открывает кнопка в окне «О программе»). Удалённые и перезаписанные сохранения и превью не стираются, а перемещаются в Корзину Windows. Сохранения из папки `Saves` рядом с программой прежних версий переносятся автоматически при первом запуске.

Менеджер экспорта позволяет задавать размер изображения вручную или выбрать готовый пресет, формат файла и способ финальной обработки:

- нативный рендер или SSAA;
- бикубическое масштабирование;
- фильтр Lanczos 3;
- PNG, JPG с настройкой качества и BMP.

### Сборка и запуск

Требуются Windows и [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone <repository-url>
cd FractalExplorer
dotnet build .\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF.slnx
dotnet run --project .\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF.csproj
```

Для разработки решение также можно открыть в Visual Studio с установленной рабочей нагрузкой .NET Desktop Development.

### Структура репозитория

```text
FractalExplorerWPF/   основная WPF-версия
FractalExplorer/      предыдущая WinForms-версия
Pictures/             скриншоты; V2_0_WPF — актуальная галерея для этого README
README.md             описание актуальной WPF-версии
```

### Лицензия

Проект распространяется по лицензии [Apache License 2.0](./LICENSE).

---

<a id="english"></a>

## English

An interactive laboratory for fractals, dynamical systems, and mathematical geometry on Windows. The current version is built with WPF and brings complex-set exploration, stochastic visualizations, mathematical laboratories, parameter editors, palettes, saved states, and image export together in one application.

<p align="center">
  <img src="./Pictures/V2_0_WPF/00-main-catalog.png" alt="Fractal Explorer WPF catalog" width="900">
</p>

> WPF is the primary development direction. The previous Windows Forms implementation remains available in the `FractalExplorer` directory as a legacy version.

### Version 2.0

The visualization catalog nearly doubled compared to the previous version — 49 entries instead of 33 — mainly thanks to the new **"Mathematical Laboratories"** section (18 modules: number theory, geometry and transforms, complex analysis, generative geometry, stochastic processes, harmonics and waves). In parallel, a large amount of internal rendering optimization work was done:

- a perturbation engine with BLA (bilinear approximation) and adaptive reference-orbit precision for the entire Mandelbrot family — deep zoom down to **1e1000** on `FloatExp` and `BigFloat`;
- the same extreme-depth zoom for the **Phoenix** fractal (down to 1e1000, with a hybrid `FloatExp`/`double` core past extreme thresholds);
- deep zoom down to **1e50** for **Collatz** and **Nova** via `BigFloat`/`ComplexBigFloat`;
- a Newton's-method auto-centering tool for mini-Mandelbrots and for the Phoenix core;
- a reworked, allocation-free `BigMantissa`.

Below is a screenshot demonstration of essentially every window in the application: every fractal and laboratory, their unique parameter/palette editors, and the shared utility windows.

### Features

- **54 catalog entries:** 52 visualizations (27 complex-dynamics/stochastic fractals, 9 dynamical systems, 18 mathematical laboratories) and 2 Julia constant galleries.
- **Interactive exploration:** cursor-centered mouse-wheel zoom, canvas panning, view reset, and full-screen mode.
- **Asynchronous CPU rendering:** configurable thread count, cancellation, progress indicators, and eight tile scheduling patterns.
- **Flexible coloring:** built-in and custom palettes, smooth and discrete modes, plus Histogram, Orbit Trap, Stripe Average, and Distance Estimation with pseudo-3D lighting for the Mandelbrot family.
- **Extreme deep zoom:** perturbation engines with BLA on `FloatExp`/`BigFloat` for the Mandelbrot family and Phoenix (down to 1e1000), plus Nova and Collatz (down to 1e50).
- **Mathematical laboratories:** a dedicated section of 18 interactive modules — from number theory and aperiodic tilings to knots, Fourier epicycles, and Chladni figures.
- **Saved explorations:** fractal parameters, previews, and points of interest are stored as JSON and restored through dedicated save managers.
- **Image export:** custom resolutions, presets up to 8K, PNG/JPG/BMP, SSAA, Bicubic, and Lanczos 3.
- **Customizable WPF interface:** built-in themes, the Windows system theme, a color editor, and an on-screen eyedropper.

### Visualization catalog

| Section | Available modules |
| --- | --- |
| Mandelbrot set | Mandelbrot, Burning Ship, Tricorn (Mandelbar), Buffalo, Celtic Mandelbrot, Simonobrot, Generalized Mandelbrot |
| Julia set | Julia, Julia Burning Ship, and two constant-`C` galleries |
| Iterated functions | Newton Pools+, Müller, Laguerre and secant basins, rational map and periodic cycle basins, Phoenix, Collatz, Nova Mandelbrot, Nova Julia, Buddhabrot / Anti-Buddhabrot |
| Iterated and self-similar | Barnsley / Heighway IFS, Fractal Flame, Sierpiński chaos game |
| Geometric and stochastic fractals | Apollonian gasket, DLA (diffusion-limited aggregation) |
| Dynamical systems and chaos | Lyapunov, Logistic Map, Bifurcation, Lorenz, Rössler, Hénon, Ikeda, strange 2D attractors, Gray–Scott |
| Mathematical laboratories | Modular arithmetic, Pascal's triangle mod N, rational numbers, prime geometry, Recamán, inverse Collatz tree, phyllotaxis, circle inversion / Möbius, aperiodic tilings, hyperbolic geometry, Voronoi / Lloyd, knots, Domain Coloring, Kleinian / Schottky, L-systems, Brownian motion / Lévy flights, Fourier epicycles, Chladni figures |

### Interface

Below is a screenshot of every window in the application: the exploration windows for fractals and laboratories, their unique parameter and palette editors, and the shared utility dialogs.

#### Mandelbrot family

A shared perturbation engine with BLA serves the classic set and all of its reflected/generalized variants, integer and fractional Multibrot, plus an interactive picker for the Julia constant `C`.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-mandelbrot.png" width="260"><br><sub>Classic Mandelbrot</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-burning-ship.png" width="260"><br><sub>Burning Ship</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-tricorn.png" width="260"><br><sub>Tricorn (Mandelbar)</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-buffalo.png" width="260"><br><sub>Buffalo</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-celtic.png" width="260"><br><sub>Celtic Mandelbrot</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-simonobrot.png" width="260"><br><sub>Simonobrot</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-generalized.png" width="260"><br><sub>Generalized Mandelbrot</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-palette-editor.png" width="260"><br><sub>Palette editor</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/julia-constant-picker.png" width="260"><br><sub>Julia constant picker</sub></td>
</tr>
</table>

#### Julia family

The classic Julia set and its "Burning Ship" variant are available both as standalone windows and as a batch gallery of constants `C` over a grid.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-julia.png" width="260"><br><sub>Classic Julia</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/mandelbrot-julia-burning-ship.png" width="260"><br><sub>Julia Burning Ship</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/julia-gallery.png" width="260"><br><sub>Julia constant gallery</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/julia-burning-ship-gallery.png" width="260"><br><sub>Julia Burning Ship gallery</sub></td>
</tr>
</table>

#### Attraction basins and other iterated families

Newton Pools+ supports the Newton, Halley, and Householder methods with its own root palette. Five more basin explorers share one window: Müller's method (a triple of starting points), Laguerre's method (with a disagreement map against Newton), the secant method (including slices of its four-dimensional state space), rational maps, and periodic cycles, where points are colored by their final attracting cycle. Phoenix is a two-panel explorer of the `C1`/`C2` parameter planes with extreme deep zoom. Nova and Collatz are complex generalizations with deep zoom down to 1e50.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/newton-pools.png" width="260"><br><sub>Newton Pools+</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/newton-palette-editor.png" width="260"><br><sub>Newton root palette</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/phoenix.png" width="260"><br><sub>Phoenix fractal</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/phoenix-parameter-explorer.png" width="260"><br><sub>Phoenix C1/C2 parameter explorer</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/nova-mandelbrot.png" width="260"><br><sub>Nova Mandelbrot</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/nova-julia.png" width="260"><br><sub>Nova Julia</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/nova-parameter-selector.png" width="260"><br><sub>Nova parameter selector</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/collatz.png" width="260"><br><sub>Collatz fractal</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/buddhabrot.png" width="260"><br><sub>Buddhabrot / Anti-Buddhabrot</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/buddhabrot-palette-editor.png" width="260"><br><sub>Buddhabrot palette</sub></td>
</tr>
</table>

#### Iterated, self-similar, geometric, and stochastic fractals

IFS and Fractal Flame include affine-transform editors. Sierpiński in "chaos game" mode, the Apollonian gasket colored by depth/curvature/parent branch, and DLA with a growing particle cluster round out the section.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/ifs.png" width="260"><br><sub>Barnsley / Heighway IFS</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/ifs-transform-editor.png" width="260"><br><sub>IFS transform editor</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/flame.png" width="260"><br><sub>Fractal Flame</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/flame-transform-editor.png" width="260"><br><sub>Flame transform editor</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/serpinsky-chaos.png" width="260"><br><sub>Sierpiński chaos game</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/serpinsky-palette-editor.png" width="260"><br><sub>Sierpiński palette</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/apollonian.png" width="260"><br><sub>Apollonian gasket</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dla.png" width="260"><br><sub>DLA</sub></td>
</tr>
</table>

#### Dynamical systems and chaos

A shared window serves the Lyapunov map, logistic-map orbits, the bifurcation diagram, the Lorenz and Rössler attractors, the Hénon and Ikeda maps, and density clouds for strange 2D attractors (Clifford, Peter de Jong, Tinkerbell, Gumowski–Mira). Gray–Scott is a separate live reaction–diffusion simulation.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-lyapunov.png" width="260"><br><sub>Lyapunov exponent</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lyapunov-palette-editor.png" width="260"><br><sub>Lyapunov palette</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-logistic-map.png" width="260"><br><sub>Logistic map</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-bifurcation.png" width="260"><br><sub>Bifurcation diagram</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-lorenz.png" width="260"><br><sub>Lorenz attractor</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynamic-palette-editor.png" width="260"><br><sub>Generic dynamic-system palette</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-rossler.png" width="260"><br><sub>Rössler attractor</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-henon.png" width="260"><br><sub>Hénon map</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-ikeda.png" width="260"><br><sub>Ikeda map</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/dynsys-attractors2-d.png" width="260"><br><sub>Strange 2D attractors</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/gray-scott.png" width="260"><br><sub>Gray–Scott</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/gray-scott-palette-editor.png" width="260"><br><sub>Gray–Scott palette</sub></td>
</tr>
</table>

#### Mathematical laboratories

New in version 2.0, this section brings together 18 interactive modules on a shared engine: number theory and discrete structures, geometry and transforms, complex analysis, generative geometry, stochastic processes, harmonics and waves.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-modular-arithmetic.png" width="260"><br><sub>Modular arithmetic</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-pascal-modulo.png" width="260"><br><sub>Pascal's triangle mod N</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-rational-numbers.png" width="260"><br><sub>Rational numbers lab</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-prime-geometry.png" width="260"><br><sub>Prime geometry</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-recaman-sequence.png" width="260"><br><sub>Recamán sequence</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/inverse-collatz-tree.png" width="260"><br><sub>Inverse Collatz tree</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/inverse-collatz-palette-editor.png" width="260"><br><sub>Inverse Collatz palette</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-phyllotaxis.png" width="260"><br><sub>Phyllotaxis</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-circle-inversion.png" width="260"><br><sub>Circle inversion / Möbius</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-aperiodic-tilings.png" width="260"><br><sub>Aperiodic tilings</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-hyperbolic-geometry.png" width="260"><br><sub>Hyperbolic geometry</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-voronoi-lloyd.png" width="260"><br><sub>Voronoi / Lloyd relaxation</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-knot-studio.png" width="260"><br><sub>Knot studio</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/domain-coloring.png" width="260"><br><sub>Domain Coloring</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-kleinian-schottky.png" width="260"><br><sub>Kleinian / Schottky groups</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lsystem.png" width="260"><br><sub>L-systems</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-stochastic-motion.png" width="260"><br><sub>Brownian motion / Lévy flights</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-fourier-epicycles.png" width="260"><br><sub>Fourier Epicycles</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/lab-chladni-wave-interference.png" width="260"><br><sub>Chladni figures / wave interference</sub></td>
</tr>
</table>

#### Shared utility windows

The catalog, the quick switcher, the theme editor with its own color picker, the generic color picker, the save and image-export managers, and the crash recovery dialog are used identically across every fractal and laboratory.

<table>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/00-quick-switcher.png" width="260"><br><sub>Quick switcher (Ctrl+K)</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/00-theme-editor.png" width="260"><br><sub>Theme editor</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/00-theme-color-picker.png" width="260"><br><sub>Theme color picker</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/00-color-picker.png" width="260"><br><sub>Color picker</sub></td>
</tr>
<tr>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/save-manager.png" width="260"><br><sub>Save manager</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/image-export-manager.png" width="260"><br><sub>Image export manager</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/00-about.png" width="260"><br><sub>About</sub></td>
<td width="25%" align="center"><img src="./Pictures/V2_0_WPF/00-crash-dialog.png" width="260"><br><sub>Crash recovery dialog</sub></td>
</tr>
</table>

### Controls

| Action | Control |
| --- | --- |
| Zoom around the cursor | Mouse wheel over the canvas |
| Pan the viewport | Drag with the left mouse button |
| Enter full-screen mode | `F11` |
| Leave full-screen mode | `Esc` |
| Hide the parameter panel | Button in the canvas's upper-left corner |
| Quick catalog switcher | `Ctrl+K` |

The exact parameters, coloring modes, and additional interactive maps depend on the selected fractal or laboratory.

### Saved states and export

Save managers preserve the formula, coordinates, zoom level, render quality, palette, and points of interest. Each entry includes a preview, while its data is stored locally as JSON.

The export manager supports manual dimensions and ready-made presets, multiple formats, and several final-processing strategies:

- native rendering or SSAA;
- bicubic scaling;
- Lanczos 3 filtering;
- PNG, quality-configurable JPG, and BMP.

### Build and run

Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) are required.

```powershell
git clone <repository-url>
cd FractalExplorer
dotnet build .\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF.slnx
dotnet run --project .\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF.csproj
```

For development, the solution can also be opened in Visual Studio with the .NET Desktop Development workload installed.

### Repository structure

```text
FractalExplorerWPF/   primary WPF version
FractalExplorer/      previous WinForms version
Pictures/             screenshots; V2_0_WPF is the current gallery used by this README
README.md             documentation for the current WPF version
```

### License

This project is distributed under the [Apache License 2.0](./LICENSE).
