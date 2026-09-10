# 🎵 Taskbar Music Widget (Windows 11 / 10)

<p align="center">
  <a href="README.md"><b>English</b></a> | <b>Español</b>
</p>

![Versión](https://img.shields.io/badge/versión-v0.9.0-1ED760?style=flat-square)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?style=flat-square)
![Framework](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?style=flat-square)
![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)

Un widget nativo, ligero y elegante para la barra de tareas de Windows que proporciona controles multimedia integrados en tiempo real con una interfaz flotante (*Flyout*) inspirada en el diseño Fluent y Spotify.

<p align="center">
  <img src="assets/demo.gif" alt="Taskbar Music Widget Demo" width="720" style="border-radius: 8px; box-shadow: 0 4px 16px rgba(0,0,0,0.4);" />
</p>

---

## ✨ Características Principales

- **Integración Fluida con la Barra de Tareas:** Se acopla de manera limpia al espacio de la bandeja del sistema sin marcos molestos ni fondos desentonados.
- **Detección Universal de Medios (GSMTC):** Compatible automáticamente con Spotify, YouTube, Twitch, Netflix, Soundcloud, VLC, Chrome, Opera, Edge, Brave y cualquier reproductor compatible con Windows Media.
- **Tarjeta Flotante Expandible (*Flyout*):** Al pasar el ratón sobre el widget, se despliega una tarjeta flotante interactiva con carátula en alta resolución, título, artista, barra de progreso con desplazamiento manual (*scrubbing*) y controles completos.
- **Barra de Volumen Interactiva con Porcentaje Numérico:** Barra de volumen opcional estilo Spotify situada justo debajo del tiempo de la pista, con botón de silenciar/activar sonido, barra deslizable y número de porcentaje exacto en tiempo real (ej. `45%`).
- **Conmutador de Dispositivo de Salida de Audio:** Cambia al instante entre auriculares, altavoces, audio HDMI y otros dispositivos conectados directamente desde la configuración o el menú contextual mediante CoreAudio `IPolicyConfig`.
- **Reubicación Multi-Monitor y Modo Gaming:**
  - Detección inteligente de juegos o vídeos en pantalla completa en el monitor principal o uso de entrada HDMI (como una PS5 en la Pantalla 1).
  - El widget se traslada automáticamente a la barra de tareas del segundo monitor para no perder el control de la música.
  - Selección de monitor configurable: *Automático*, *Pantalla 1 (Principal)* o *Pantalla 2 (Secundaria)*.
- **Panel de Configuración Integrado en el Flyout:** Accesible pulsando el icono de engranaje (`⚙`) en la cabecera del cuadro flotante, permitiendo activar la barra de volumen, cambiar de pantalla y seleccionar dispositivo de audio.
- **Animación Cinemática de Texto (*Marquee con KeyFrames*):**
  - Implementado tanto en el widget de la barra como en la tarjeta flotante (*Flyout*).
  - Medición tipográfica exacta subpíxel mediante `FormattedText` y `DesiredSize` para evitar cualquier recorte accidental.
  - Pausas estratégicas de 2 segundos al inicio y al final de cada ciclo, permitiendo leer títulos y nombres de artistas largos con total comodidad y sin prisas.
- **Sustitución de Carátula en YouTube Shorts por el Logo del Canal:** Detecta automáticamente vídeos verticales de YouTube Shorts y sustituye la miniatura cortada por el logo oficial del canal en alta resolución con caché de doble nivel (RAM y disco).
- **Soporte Multilingüe de la Interfaz (Español / English):** Detecta automáticamente el idioma de visualización de Windows (`CultureInfo.CurrentUICulture`), adaptando al instante los textos de la interfaz (como *"Sin música"* / *"No music playing"*), controles del flyout, notificaciones HUD de volumen y menús contextuales, preservando siempre intactos y fidedignos los títulos originales de las canciones y vídeos.
- **Aleatorio Inteligente (*Smart Shuffle*) de Spotify:** Integración bidireccional con Spotify mediante **Windows UI Automation** que detecta y conmuta entre *Desactivado*, *Aleatorio normal* y *Smart Shuffle* con su destello característico (`✦`).
- **Control de Volumen con Rueda del Ratón:** Ajuste directo del volumen maestro del sistema en saltos exactos del **5%** mediante interfaces COM de bajo nivel (**CoreAudio IAudioEndpointVolume**).
- **Enfoque Inteligente y Conmutación de Pestañas:**
  - Al hacer clic en la carátula o título, activa la aplicación correspondiente **sin alterar su tamaño ni desmaximizarla** (incluso en segundas pantallas).
  - En navegadores (Opera, Chrome, Edge), localiza la pestaña exacta que está reproduciendo contenido (por ejemplo, YouTube) y cambia a ella automáticamente mediante UI Automation.
- **Optimización Extrema de Recursos:** Consumo prácticamente nulo de CPU (< 0.1%) y uso reducido de memoria RAM (~30 MB).

---

## 🛠️ Stack Tecnológico y Arquitectura

- **Framework:** .NET 8 (C#) con Windows Presentation Foundation (WPF).
- **Windows Runtime (WinRT):** `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` para escucha de eventos multimedia en tiempo real.
- **Win32 P/Invoke:** Manipulación avanzada de ventanas (`user32.dll`, `dwmapi.dll`), gestión de Z-order (`WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`) y enumeración multi-monitor nativa (`EnumDisplayMonitors`, `GetMonitorInfo`).
- **UI Automation:** `System.Windows.Automation` para inspección de árboles de accesibilidad de Chromium y control de botones nativos entre procesos.
- **COM Interop:** Implementación de `IMMDeviceEnumerator`, `IAudioEndpointVolume` y la interfaz no documentada `IPolicyConfig` para enrutamiento directo de dispositivos de sonido y control de volumen.

---

## 🚀 Instalación y Compilación

### Requisitos
- Windows 10 (versión 19041+) o Windows 11.
- .NET 8 SDK.

### Compilar desde la terminal
```bash
# Clonar el repositorio
git clone https://github.com/KillianTR/TaskbarMusicWidget.git
cd TaskbarMusicWidget

# Compilar proyecto
dotnet build -c Release

# Publicar ejecutable optimizado autoportante
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

El ejecutable listo para usar se generará en:
`bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/TaskbarMusicWidget.exe`

---

## 📌 Historial de Versiones (Changelog)

### v0.9.0
- **Panel de Configuración Integrado en el Flyout:** Incorporación de un botón de engranaje (`⚙`) en la cabecera de la tarjeta flotante para acceder a la configuración sin ventanas emergentes externas.
- **Barra de Volumen con Porcentaje Numérico:** Fila interactiva opcional situada debajo del timeline con botón de silencio/altavoz, control deslizante de volumen y lectura numérica precisa (ej. `45%`).
- **Conmutación de Dispositivos de Audio:** Selección directa del dispositivo de salida de Windows (auriculares, altavoces, HDMI, DACs) desde los ajustes o desde el menú contextual mediante COM `IPolicyConfig`.
- **Reubicación Automática Multi-Monitor:** Detección de juegos y vídeos en pantalla completa en el monitor principal o uso de HDMI alternativo (ej. PS5 en la Pantalla 1), moviendo automáticamente el widget a la barra de tareas de la Pantalla 2. Configurable en *Automático*, *Pantalla 1* o *Pantalla 2*.
- **Arquitectura Multi-Monitor Pura en WPF:** Enumeración directa de monitores físicos con la API de Win32 (`EnumDisplayMonitors`), sin dependencias pesadas de Windows Forms.
- **Persistencia de Preferencias:** Guardado automático de ajustes del usuario en `%LocalAppData%\TaskbarMusicWidget\settings.json`.

### v0.8.7
- **Sustitución de Carátula en YouTube Shorts por el Logo del Canal (Hotfix / Mejora):** Detección inteligente de YouTube Shorts reproducidos en navegadores (mediante relación de aspecto vertical 9:16 `PixelHeight > PixelWidth` o etiquetas `#shorts`), sustituyendo la miniatura de vídeo cortada/dividida por el logo o avatar oficial del canal en alta resolución (256x256).
- **Caché de Avatares en Doble Nivel (Memoria + Disco):** Sistema de almacenamiento en memoria RAM y persistente en disco (`%LocalAppData%\TaskbarMusicWidget\Avatars`), logrando que reproducciones sucesivas de Shorts del mismo canal muestren el logo al instante (0 ms) sin peticiones de red redundantes.
- **Preservación Selectiva:** Mantiene intactas las miniaturas 16:9 de vídeos estándar de YouTube, las portadas cuadradas 1:1 de Spotify y los pósteres de Netflix.

### v0.8.6
- **Viewport Desacoplado con Canvas (Hotfix Crítico):** Resolución definitiva del recorte interno del motor de maquetación de WPF (`GetLayoutClip`), el cual truncaba todos los caracteres y glifos que sobrepasaran los 116px (en barra) y 238px (en flyout) al estar alojados directamente en celdas fijas de tipo `Grid`.
- **Renderizado Íntegro de Títulos Extensos:** Al envolver los `TextBlock` en un `Canvas` sin restricciones de slot de maquetación, todo el texto (por largo que sea el título del vídeo de YouTube o pista) se renderiza al 100% de su anchura real y se visualiza completo de principio a fin al desplazarse.
- **Tooltips Nativos Completos:** Al posar el ratón sobre los textos en el widget de la barra o en la tarjeta flotante, se muestra un tooltip nativo con el título y artista completos.

### v0.8.5
- **Hotfix de Desplazamiento y Estabilidad del Marquee:** Resolución del problema por el cual títulos largos de pistas o vídeos (ej. vídeos de YouTube como *"Ser Parte de Tantos Proyectos...¿Merece La Pena?"* o canciones en Spotify) se quedaban estáticos o cortados en el límite del recuadro.
- **Desacoplamiento de Clave de Caché:** La clave de caché del estado de animación se desacopla del ancho dinámico del contenedor, impidiendo que eventos SMTC recurrentes reinicien innecesariamente la animación a cero.
- **Persistencia de Animación en el Flyout:** Se mantiene el ciclo de animación activo en segundo plano durante los ciclos de apertura/cierre del flyout, garantizando que el texto ya esté en movimiento al desplegar la tarjeta sin esperas congeladas.
- **Ritmo Reactivo y Margen de Cola de 40px:** Reducción de la pausa inicial de 2.0s a 0.8s con velocidad uniforme de 28 px/s y margen de holgura de +40px, garantizando que el 100% de caracteres, puntuación y paréntesis se lean completos.
- **Sincronización de Binarios y Acceso Directo:** Homogeneización de ejecutables publicados y del acceso directo de Inicio.

### v0.8.4
- **Calibración de Espaciado del Flyout estilo Windows 11 (Hotfix):** Se ajusta la posición vertical de la tarjeta flotante para dejar un espacio libre de **12px** respecto al borde superior de la barra de tareas, emulando con exactitud la elevación del Centro de Notificaciones y Calendario nativo de Windows 11.
- **Suavizado de Transición del Cursor:** Aumento del temporizador de cierre a 400ms para permitir una navegación cómoda y continua entre el widget de la barra y el cuadro flotante sin cierres accidentales.

### v0.8.3
- **Corrección de Recorte y Reinicio del Marquee (Hotfix):** Se soluciona el problema por el cual el título de canciones largas se interrumpía prematuramente (ej. deteniéndose en *"bert mccrac"* en el flyout o *"yungb"* en el widget de la barra).
- **Protección contra Reinicios Innecesarios:** Se implementa un sistema de caché de estado (`cacheKey`) que evita que eventos periódicos o secundarios de Windows SMTC cancelen la animación en curso si la pista no ha cambiado.
- **Distancia de Desplazamiento Completa:** Incorporación de un margen dinámico generoso (+35px) y medición compuesta (`DesiredSize` + `FormattedText`) para garantizar que el 100% de títulos y artistas largos (incluyendo paréntesis y coletillas como `feat.`) se muestren por completo con holgura.

### v0.8.2
- **Ajuste de Proporción en la Barra (Hotfix):** Reducción del ancho total del widget a 230px, eliminando el espacio sobrante entre el texto y los botones para un diseño compacto y armónico.
- **Activación Óptima del Marquee:** Con un ancho de contenedor de ~110px, títulos de longitud estándar activan el marquee de forma natural sin crear huecos en pistas cortas.

### v0.8.1
- **Internacionalización Dinámica (i18n):** Detección automática del idioma del sistema operativo Windows (Español / Inglés).
- **Localización de Interfaz y Estados:** Adaptación en tiempo real de cadenas HUD (*"Sin música"* / *"No music playing"*), tooltips, avisos de volumen y menús contextuales. Los títulos de canciones y vídeos permanecen intactos.
- **Integración Visual de Demo:** Se añade `demo.gif` con reproducción continua directa al README.

### v0.8.0
- **Marquee Cinemático con KeyFrames:** Reemplazo de animación básica por `DoubleAnimationUsingKeyFrames` con pausas de 2 segundos en ambos extremos.
- **Soporte de Marquee en Flyout:** La tarjeta flotante ahora incluye desplazamiento dinámico para títulos y artistas que excedan el ancho.
- **Medición Precisa de Fuentes:** Uso de `FormattedText` y DPI del sistema para calcular el ancho real de la fuente.
- **Contenedores Ampliados:** Anchos optimizados (280px barra, 340px flyout) para mejor legibilidad.
- **Sincronización Bidireccional de Smart Shuffle:** Soporte completo para el ciclo de 3 estados de Spotify con indicador de destello (`✦`).
- **Preservación de Ventanas Maximizadas:** Eliminación de llamadas DWM disruptivas al enfocar reproductores en monitores secundarios.

---

## 📄 Licencia

Este proyecto está bajo la Licencia MIT. Eres libre de usarlo, modificarlo y distribuirlo.
