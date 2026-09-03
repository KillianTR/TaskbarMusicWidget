# 🎵 Taskbar Music Widget (Windows 11 / 10)

Un widget nativo, ligero y elegante para la barra de tareas de Windows que proporciona controles multimedia integrados en tiempo real con una interfaz flotante (*Flyout*) inspirada en el diseño Fluent y Spotify.

---

## ✨ Características Principales

- **Integración Fluida con la Barra de Tareas:** Se acopla de manera limpia al espacio de la bandeja del sistema sin marcos molestos ni fondos desentonados.
- **Detección Universal de Medios (GSMTC):** Compatible automáticamente con Spotify, YouTube, Twitch, Netflix, Soundcloud, VLC, Chrome, Opera, Edge, Brave y cualquier reproductor compatible con Windows Media.
- **Tarjeta Flotante Expandible (*Flyout*):** Al pasar el ratón sobre el widget, se despliega una tarjeta flotante interactiva con carátula en alta resolución, título, artista, barra de progreso con desplazamiento manual (*scrubbing*) y controles completos.
- **Aleatorio Inteligente (*Smart Shuffle*) de Spotify:** Integración bidireccional con Spotify mediante **Windows UI Automation** que detecta y conmuta entre *Desactivado*, *Aleatorio normal* y *Smart Shuffle* con su destello característico (`✦`).
- **Control de Volumen con Rueda del Ratón:** Ajuste directo del volumen maestro del sistema en saltos exactos del **5%** mediante interfaces COM de bajo nivel (**CoreAudio IAudioEndpointVolume**).
- **Enfoque Inteligente y Conmutación de Pestañas:**
  - Al hacer clic en la carátula o título, activa la aplicación correspondiente **sin alterar su tamaño ni desmaximizarla** (incluso en segundas pantallas).
  - En navegadores (Opera, Chrome, Edge), localiza la pestaña exacta que está reproduciendo contenido (por ejemplo, YouTube) y cambia a ella automáticamente mediante UI Automation.
- **Ocultación Automática en Pantalla Completa:** Monitoreo reactivo para ocultarse instantáneamente al jugar a pantalla completa, ver vídeos sin bordes o cuando la barra de tareas de Windows se auto-oculta.
- **Texto con Desplazamiento Suave (*Marquee*):** Los títulos y artistas largos se desplazan suavemente de lado a lado (*ping-pong*) para que nunca queden cortados.
- **Optimización Extrema de Recursos:** Consumo prácticamente nulo de CPU (< 0.1%) y uso reducido de memoria RAM (~30 MB).

---

## 🛠️ Stack Tecnológico y Arquitectura

- **Framework:** .NET 8 (C#) con Windows Presentation Foundation (WPF).
- **Windows Runtime (WinRT):** `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` para escucha de eventos multimedia en tiempo real.
- **Win32 P/Invoke:** Manipulación avanzada de ventanas (`user32.dll`, `dwmapi.dll`), gestión de Z-order (`WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`) y cálculo de monitores (`MonitorFromWindow`).
- **UI Automation:** `System.Windows.Automation` para inspección de árboles de accesibilidad de Chromium y control de botones nativos entre procesos.
- **COM Interop:** Implementación de `IMMDeviceEnumerator` y `IAudioEndpointVolume` para control directo del hardware de sonido de Windows.

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

## 📄 Licencia

Este proyecto está bajo la Licencia MIT. Siéntete libre de utilizarlo, modificarlo y distribuirlo.

