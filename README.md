# Fractal Explorer Studio

Welcome to Fractal Explorer Studio! This is a comprehensive Windows Forms application written in C# that allows you to generate, explore, customize, and save a wide variety of beautiful fractal images. Dive into the infinite complexity of mathematical art with a powerful and user-friendly toolset.

## 🌟 Key Features (Version 1.7)

- **Huge Fractal Library:** Explore 13 different types of fractals, including classics like Mandelbrot and Julia, as well as exotic modifications: Nova, Collatz, Buffalo, Simonbrot, and Generalized Mandelbrot.
- **Full UI Customization:** Personalize the app with the new Theme Manager. Customize the colors of every UI element, save, load, and share your themes.
- **Interactive 2D and 4D Selectors:** Visual parameter selection. Navigate maps (using the mouse wheel and Middle Mouse Button) to pick Julia set constants, or use the complex 4D selector in the form of two dynamic slices for the Phoenix fractal.
- **Advanced Color Management:** Upgraded palette managers for different fractal types and an entirely new Color Probe tool to grab colors from anywhere on your monitor.
- **Smart Save System:** A unified manager for user saves and built-in "Points of Interest". All data (states, palettes, themes) is securely stored in human-readable JSON files in the Saves folder.
- **Professional Image Export:** Save your fractals in JPG or PNG formats at any resolution. Features upscaling algorithms (Lanczos-3, Bicubic) and SSAA (Super-Sample Anti-Aliasing) for pixel-perfect quality.
- **Fullscreen Hotkeys:** Press **F11** to toggle fullscreen mode and **Esc** to exit fullscreen.

## 🎨 Visualization and Design

**Flexible Coloring System**
The application offers two main algorithms for color calculation (independent of the chosen palette):
- **Discrete (Iterative):** The classic method that creates sharp color bands based on the iteration count.
- **Smooth:** Uses a normalized iteration count algorithm to create soft, seamless gradients without color banding.

**Animated Tile Rendering**
Watch the mathematical generation process in real-time! In the Main Menu (Hub), you can choose the visual effect of how rendering tiles appear: From Center, Row-by-Row, Checkerboard, Spiral, Random, Edges to Center, or Z-curve (Morton).

**Professional Color Selection Tool**
The color configuration system has been completely overhauled. You now have three ways to assign colors:
- **Windows System Dialog:** The classic color picker (also opens by clicking on the current color thumbnail).
- **Color Probe Tool:** An innovative feature that launches a transparent overlay across your entire monitor. It allows you to copy any color from the screen with a single click (cancel with Esc or Right-Click).
- **Automatic Palettes:** For Newton's Pools, the system automatically detects the number of polynomial roots and generates the required set of colors.

## 🚀 Implemented Fractals and Formulas

- **Mandelbrot Family** (z₀ = 0, c = pixel):
  - Classic Mandelbrot: z = z² + c
  - Burning Ship: z = (|Re(z)| - i * |-Im(z)|)² + c
  - Buffalo: z = (|Re(z)| + i * |Im(z)|)² + c
  - Generalized Mandelbrot: z = z^p + c (with manual input for power p)
  - Simonbrot: z = z^p ⋅ |z|^p + c (with an option for X-axis mirror inversion)
  - Nova Mandelbrot: z = z - m * (z^p−1)/(p*z^(p−1)) + c (with adjustable relaxation coefficient m and power p)

- **Julia Family** (z₀ = pixel, c = constant):
  - Classic Julia: z = z² + c
  - Burning Ship (Julia): z = (|Re(z)| - i * |-Im(z)|)² + c
  - Nova Julia: z = z - m * (z^p−1)/(p*z^(p−1)) + c

- **Unique and Exotic Fractals:**
  - Newton's Pools: z = z - f(z)/f'(z). Equipped with an expression parser for custom user formulas (e.g., z^3 - 1).
  - Phoenix Fractal: z_{n+1} = z_n² + Re(C1) + Im(C1)*z_{n-1}
  - Collatz Fractal: A complex plane adaptation of the famous conjecture. Features 3 formulas: Standard (z = 0.25(2+7z−(2+5z)cos(πz))), Sine Variation, and Generalized P.
  - Sierpiński Triangle: Generated using the "Chaos Game" stochastic method.

## 🛠️ Technical Details and Optimization

- **Platform:** .NET Windows Forms (C#). Asynchronous, multi-threaded rendering engine.
- **Computational Precision:** Uses a custom ComplexDecimal structure for high-precision complex number operations at extreme zoom levels.
- **Algorithm Optimization:**
  - *Mandelbrot:* Implemented checking for points inside the main cardioid and period-2 bulb. This reduced rendering time for low iteration depths by dozens of times (e.g., from 0.4s to 0.02s).
  - *Newton's Pools:* Root-finding algorithms were optimized, providing an average performance boost of 20% across all scenarios.
- **RAM Usage:** High architectural efficiency keeps application memory footprint under 100 MB even during complex calculations.

## 🧭 Development Convention: Namespaces

- **Canonical rule:** namespace follows physical file path.
- For example:
  - `FractalExplorer/Utilities/ComplexDecimal.cs` → `namespace FractalExplorer.Utilities`
  - `FractalExplorer/Utilities/RenderUtilities/TileInfo.cs` → `namespace FractalExplorer.Utilities.RenderUtilities`
- Do not place utility/domain types into `FractalExplorer.Resources` unless the file is physically placed in a matching `Resources/` code folder.

## 🖼️ Gallery and Interface

Below is a detailed overview of the application's features and windows.

**Main Menu and Customization**
<table>
<tr>
<td align="center"><b>Hub (Main Page)</b><br>Fractal, theme & render selection.<br><img src="Pictures/V1_7/01_Hub.png" width="270"></td>
<td align="center"><b>Render Effects</b><br>Visual styles for rendering tiles.<br><img src="Pictures/V1_7/01_Hub_rendertypes.png" width="270"></td>
<td align="center"><b>Theme Manager</b><br>Deep UI customization.<br><img src="Pictures/V1_7/22_themes_manager.png" width="270"></td>
</tr>
</table>

**Mandelbrot Family**
<table>
<tr>
<td align="center"><b>Mandelbrot Set</b><br><img src="Pictures/V1_7/02_Mandelbrot.png" width="270"></td>
<td align="center"><b>Burning Ship</b><br><img src="Pictures/V1_7/03_Burning_ship.png" width="270"></td>
<td align="center"><b>Buffalo Fractal</b><br><img src="Pictures/V1_7/04_Buffalo.png" width="270"></td>
</tr>
<tr>
<td align="center"><b>Simonbrot</b><br><img src="Pictures/V1_7/05_Simonbrot.png" width="270"></td>
<td align="center"><b>Generalized Mandelbrot</b><br><img src="Pictures/V1_7/06_General_mandelbrot.png" width="270"></td>
<td align="center"><b>Nova Mandelbrot</b><br><img src="Pictures/V1_7/12_Nova_Mandelbrot.png" width="270"></td>
</tr>
</table>

**Julia Family and Interactive Maps (Selectors)**
Navigate selectors using the mouse wheel and middle mouse button; left-click to select the 'C' constant.
<table>
<tr>
<td align="center"><b>Julia Set</b><br><img src="Pictures/V1_7/07_Julia.png" width="270"></td>
<td align="center"><b>Julia (Burning Ship)</b><br><img src="Pictures/V1_7/08_Julia_Burning_ship.png" width="270"></td>
<td align="center"><b>Nova Julia</b><br><img src="Pictures/V1_7/13_Nova_Julia.png" width="270"></td>
</tr>
<tr>
<td align="center"><b>Julia Selector</b><br>(Map: Classic Mandelbrot)<br><img src="Pictures/V1_7/07_Julia_C_selector.png" width="270"></td>
<td align="center"><b>Julia B.S. Selector</b><br>(Map: Mandelbrot B.S.)<br><img src="Pictures/V1_7/08_Julia_Burning_ship_C_selector.png" width="270"></td>
<td align="center"><b>Nova Julia Selector</b><br>(Map: Nova Mandelbrot)<br><img src="Pictures/V1_7/13_Nova_Julia_C_selector.png" width="270"></td>
</tr>
</table>

**Unique Fractals**
<table>
<tr>
<td align="center"><b>Newton's Pools</b><br><img src="Pictures/V1_7/09_Newton_pools.png" width="270"></td>
<td align="center"><b>Phoenix Fractal</b><br><img src="Pictures/V1_7/10_Phoenix.png" width="270"></td>
<td align="center"><b>4D Selector (Phoenix)</b><br>Two dynamic 2D slices.<br><img src="Pictures/V1_7/10_Phoenix_C1C2_selector.png" width="270"></td>
</tr>
<tr>
<td align="center"><b>Collatz Fractal</b><br><img src="Pictures/V1_7/11_Collatz.png" width="270"></td>
<td align="center"><b>Sierpiński Triangle</b><br><img src="Pictures/V1_7/14_Serpinsky.png" width="270"></td>
<td align="center"></td>
</tr>
</table>

**Color and Palette Management**
<table>
<tr>
<td align="center"><b>Iterative Palette</b><br>For most fractals.<br><img src="Pictures/V1_7/02_iterative_palette.png" width="270"></td>
<td align="center"><b>Newton's Palette</b><br>Auto-detection of roots.<br><img src="Pictures/V1_7/09_Newton_pools_palette.png" width="270"></td>
<td align="center"><b>Sierpiński Palette</b><br>Background & point setup.<br><img src="Pictures/V1_7/14_Serpinsky_palette.png" width="270"></td>
</tr>
<tr>
<td align="center"><b>Theme Selection Menu</b><br>Accessible from the Hub.<br><img src="Pictures/V1_7/01_Hub_themes.png" width="270"></td>
<td align="center"><b>Color Tool & Probe</b><br>3 ways to pick colors.<br><img src="Pictures/V1_7/23_color&color_probe_setting.png" width="270"></td>
<td align="center"><b>Windows System Palette</b><br>Classic dialog box.<br><img src="Pictures/V1_7/23_color_standart_setting.png" width="270"></td>
</tr>
</table>

**Save and Export**
<table>
<tr>
<td align="center"><b>Points of Interest</b><br>Built-in beautiful presets.<br><img src="Pictures/V1_7/20_save_load_manager.png" width="270"></td>
<td align="center"><b>User Saves</b><br>Your personal discoveries.<br><img src="Pictures/V1_7/20_save_load_manager_user.png" width="270"></td>
<td align="center"><b>Image Export</b><br>SSAA, filters & custom res.<br><img src="Pictures/V1_7/21_save_image.png" width="270"></td>
</tr>
</table>

## 📜 License

This project is distributed under the Apache 2.0 license. The full text of the license is available in the LICENSE.md file.

*Project created with AI assistance.*

<br><br>
<hr>
<br><br>

<a name="fractal-explorer-studio-ru"></a>
# Fractal Explorer Studio (RU)

Добро пожаловать в Fractal Explorer Studio! Это комплексное приложение для Windows Forms, написанное на C#, которое позволяет генерировать, исследовать, настраивать и сохранять разнообразные и красивые фрактальные изображения. Погрузитесь в бесконечную сложность математического искусства с помощью мощного и удобного инструментария.

## 🌟 Ключевые Возможности (Версия 1.7)

- **Огромная Библиотека Фракталов:** Исследуйте 13 различных типов фракталов, включая как классику (Мандельброт, Жюлиа), так и экзотические модификации: Нова, Коллатц, Буффало, Симоноброт и Обобщенный Мандельброт.
- **Полная Кастомизация Интерфейса:** Персонализируйте приложение с помощью нового Менеджера Тем. Настраивайте цвета каждого элемента, сохраняйте, загружайте и делитесь своими темами.
- **Интерактивные 2D и 4D Селекторы:** Визуальный выбор параметров. Навигация по картам (с помощью колеса и СКМ) для выбора констант множеств Жюлиа, а также сложный 4D-селектор в виде двух динамических срезов для фрактала Феникс.
- **Продвинутая Работа с Цветом:** Усовершенствованные менеджеры палитр для разных типов фракталов и абсолютно новый инструмент "Пипетка" (Color Probe) для захвата цвета из любой точки вашего монитора.
- **Умная Система Сохранений:** Единый менеджер для пользовательских сохранений и встроенных "Точек интереса". Все данные (состояния, палитры, темы) надежно хранятся в JSON-файлах в папке Saves.
- **Профессиональный Экспорт Изображений:** Сохраняйте фракталы в форматах JPG или PNG с любым разрешением. Доступны алгоритмы масштабирования (Ланцош-3, Бикубический) и сглаживание SSAA (Сверхвысокое разрешение) для идеального качества.
- **Горячие клавиши полноэкранного режима:** **F11** — переключение полноэкранного режима, **Esc** — выход.

## 🎨 Визуализация и Дизайн

**Гибкая Система Окраски**
Приложение предлагает два основных алгоритма расчета цвета (независимо от выбранной палитры):
- **Дискретный (Iterative):** Классический метод, создающий четкие цветовые полосы в зависимости от числа итераций.
- **Плавный (Smooth):** Использует алгоритм нормализованного счётчика для создания мягких, непрерывных градиентов без "полосатости".

**Анимированный Плиточный Рендеринг**
Наблюдайте за процессом математической генерации в реальном времени! В Главном меню (Хабе) вы можете выбрать визуальный эффект появления плиток при рендеринге: От центра, Построчный, Шахматный, Спиральный, Случайный, От краев к центру или Z-кривая (Мортон).

**Профессиональный Инструмент Выбора Цвета**
Система настройки цвета была полностью переработана. Теперь при назначении цвета вам доступны три варианта:
- **Системный диалог Windows:** Классический выбор цвета (открывается также по клику на миниатюру текущего цвета).
- **Инструмент "Пипетка" (Color Probe):** Инновационная функция, запускающая прозрачный оверлей поверх всего монитора. Позволяет одним кликом скопировать любой цвет с экрана (отмена по Esc или ПКМ).
- **Автоматические Палитры:** Для Бассейнов Ньютона система сама определяет число корней уравнения и создает нужный набор цветов.

## 🚀 Реализованные Фракталы и Формулы

- **Семейство Мандельброта** (z₀ = 0, c = пиксель):
  - Классический Мандельброт: z = z² + c
  - Горящий Корабль: z = (|Re(z)| - i * |-Im(z)|)² + c
  - Буффало: z = (|Re(z)| + i * |Im(z)|)² + c
  - Обобщенный Мандельброт: z = z^p + c (с ручным вводом степени p)
  - Симоноброт: z = z^p ⋅ |z|^p + c (с опцией зеркальной инверсии по оси X)
  - Нова Мандельброт: z = z - m * (z^p−1)/(p*z^(p−1)) + c (с настройкой коэффициента релаксации m и степени p)

- **Семейство Жюлиа** (z₀ = пиксель, c = константа):
  - Классический Жюлиа: z = z² + c
  - Горящий Корабль (Жюлиа): z = (|Re(z)| - i * |-Im(z)|)² + c
  - Нова Жюлиа: z = z - m * (z^p−1)/(p*z^(p−1)) + c

- **Уникальные и Экзотические Фракталы:**
  - Бассейны Ньютона: z = z - f(z)/f'(z). Оснащен парсером выражений для ввода пользовательских формул (например, z^3 - 1).
  - Фрактал Феникс: z_{n+1} = z_n² + Re(C1) + Im(C1)*z_{n-1}
  - Фрактал Коллатца: Комплексная адаптация знаменитой гипотезы. Доступны 3 формулы: Standard (z = 0.25(2+7z−(2+5z)cos(πz))), Sine Variation и Generalized P.
  - Треугольник Серпинского: Метод "Игры Хаоса".

## 🛠️ Технические Детали и Оптимизация

- **Платформа:** .NET Windows Forms (C#). Асинхронный, многопоточный движок рендеринга.
- **Вычислительная Точность:** Использование кастомной структуры ComplexDecimal для операций с комплексными числами при экстремальных приближениях.
- **Оптимизация Алгоритмов:**
  - *Мандельброт:* Внедрена проверка нахождения точек внутри основной кардиоиды и бульбы периода 2. Это позволило сократить время рендера низких глубин ("мелководья") в десятки раз (например, с 0.4 сек до 0.02 сек).
  - *Бассейны Ньютона:* Алгоритмы поиска корней оптимизированы, что дало прирост производительности в среднем на 20% во всех сценариях.
- **Потребление ОЗУ:** Высокая эффективность архитектуры позволяет приложению потреблять до 100 МБ оперативной памяти даже при сложных расчетах.

## 🧭 Соглашение разработки: пространства имен

- **Каноничное правило:** пространство имен следует физическому пути файла.
- Примеры:
  - `FractalExplorer/Utilities/ComplexDecimal.cs` → `namespace FractalExplorer.Utilities`
  - `FractalExplorer/Utilities/RenderUtilities/TileInfo.cs` → `namespace FractalExplorer.Utilities.RenderUtilities`
- Не размещайте utility/domain-типы в `FractalExplorer.Resources`, если файл физически не лежит в соответствующей папке `Resources/` с кодом.

## 🖼️ Галерея и Интерфейс

Ниже представлен подробный обзор всех возможностей и окон приложения.

**Главное меню и Оформление**
<table>
<tr>
<td align="center"><b>Хаб (Главная страница)</b><br>Выбор фрактала, темы и рендера.<br><img src="Pictures/V1_7/01_Hub.png" width="270"></td>
<td align="center"><b>Эффекты рендеринга</b><br>Визуальные стили появления плиток.<br><img src="Pictures/V1_7/01_Hub_rendertypes.png" width="270"></td>
<td align="center"><b>Менеджер Тем</b><br>Глубокая кастомизация интерфейса.<br><img src="Pictures/V1_7/22_themes_manager.png" width="270"></td>
</tr>
</table>

**Семейство Мандельброта**
<table>
<tr>
<td align="center"><b>Множество Мандельброта</b><br><img src="Pictures/V1_7/02_Mandelbrot.png" width="270"></td>
<td align="center"><b>Горящий Корабль</b><br><img src="Pictures/V1_7/03_Burning_ship.png" width="270"></td>
<td align="center"><b>Фрактал Буффало</b><br><img src="Pictures/V1_7/04_Buffalo.png" width="270"></td>
</tr>
<tr>
<td align="center"><b>Симоноброт</b><br><img src="Pictures/V1_7/05_Simonbrot.png" width="270"></td>
<td align="center"><b>Обобщенный Мандельброт</b><br><img src="Pictures/V1_7/06_General_mandelbrot.png" width="270"></td>
<td align="center"><b>Нова Мандельброт</b><br><img src="Pictures/V1_7/12_Nova_Mandelbrot.png" width="270"></td>
</tr>
</table>

**Семейство Жюлиа и Интерактивные Карты (Селекторы)**
Навигация по селекторам осуществляется с помощью колеса и средней кнопки мыши, левый клик — выбор константы 'C'.
<table>
<tr>
<td align="center"><b>Множество Жюлиа</b><br><img src="Pictures/V1_7/07_Julia.png" width="270"></td>
<td align="center"><b>Жюлиа (Горящий Корабль)</b><br><img src="Pictures/V1_7/08_Julia_Burning_ship.png" width="270"></td>
<td align="center"><b>Нова Жюлиа</b><br><img src="Pictures/V1_7/13_Nova_Julia.png" width="270"></td>
</tr>
<tr>
<td align="center"><b>Селектор для Жюлиа</b><br>(Карта: Классический Мандельброт)<br><img src="Pictures/V1_7/07_Julia_C_selector.png" width="270"></td>
<td align="center"><b>Селектор для Жюлиа Г.К.</b><br>(Карта: Мандельброт Г.К.)<br><img src="Pictures/V1_7/08_Julia_Burning_ship_C_selector.png" width="270"></td>
<td align="center"><b>Селектор для Нова Жюлиа</b><br>(Карта: Нова Мандельброт)<br><img src="Pictures/V1_7/13_Nova_Julia_C_selector.png" width="270"></td>
</tr>
</table>

**Уникальные Фракталы**
<table>
<tr>
<td align="center"><b>Бассейны Ньютона</b><br><img src="Pictures/V1_7/09_Newton_pools.png" width="270"></td>
<td align="center"><b>Фрактал Феникс</b><br><img src="Pictures/V1_7/10_Phoenix.png" width="270"></td>
<td align="center"><b>4D-Селектор для Феникса</b><br>Два динамических 2D-среза.<br><img src="Pictures/V1_7/10_Phoenix_C1C2_selector.png" width="270"></td>
</tr>
<tr>
<td align="center"><b>Фрактал Коллатца</b><br><img src="Pictures/V1_7/11_Collatz.png" width="270"></td>
<td align="center"><b>Треугольник Серпинского</b><br><img src="Pictures/V1_7/14_Serpinsky.png" width="270"></td>
<td align="center"></td>
</tr>
</table>

**Управление Цветом и Палитрами**
<table>
<tr>
<td align="center"><b>Итеративная Палитра</b><br>Для большинства фракталов.<br><img src="Pictures/V1_7/02_iterative_palette.png" width="270"></td>
<td align="center"><b>Палитра Ньютона</b><br>Авто-определение корней.<br><img src="Pictures/V1_7/09_Newton_pools_palette.png" width="270"></td>
<td align="center"><b>Палитра Серпинского</b><br>Настройка фона и точек.<br><img src="Pictures/V1_7/14_Serpinsky_palette.png" width="270"></td>
</tr>
<tr>
<td align="center"><b>Меню выбора темы</b><br>Доступно из Хаба.<br><img src="Pictures/V1_7/01_Hub_themes.png" width="270"></td>
<td align="center"><b>Инструмент Цвета и Пипетка</b><br>3 способа выбора цвета.<br><img src="Pictures/V1_7/23_color&color_probe_setting.png" width="270"></td>
<td align="center"><b>Системная палитра Windows</b><br>Классический диалог.<br><img src="Pictures/V1_7/23_color_standart_setting.png" width="270"></td>
</tr>
</table>

**Сохранение и Экспорт**
<table>
<tr>
<td align="center"><b>Точки Интереса</b><br>Встроенные красивые пресеты.<br><img src="Pictures/V1_7/20_save_load_manager.png" width="270"></td>
<td align="center"><b>Пользовательские Сохранения</b><br>Ваши личные находки.<br><img src="Pictures/V1_7/20_save_load_manager_user.png" width="270"></td>
<td align="center"><b>Экспорт Изображений</b><br>SSAA, фильтры и кастомное разрешение.<br><img src="Pictures/V1_7/21_save_image.png" width="270"></td>
</tr>
</table>

## 📜 Лицензия

Этот проект распространяется под лицензией Apache 2.0. Полный текст лицензии доступен в файле LICENSE.md.

*Проект создан при помощи ИИ.*
