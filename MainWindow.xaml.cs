using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TaskbarMusicWidget
{
    public partial class MainWindow : Window
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private FlyoutWindow? _flyoutWindow;

        private readonly DispatcherTimer _watchdogTimer;
        private readonly DispatcherTimer _closeFlyoutTimer;

        private bool _isPlaying = false;
        private TimeSpan _currentPosition = TimeSpan.Zero;
        private TimeSpan _duration = TimeSpan.Zero;
        private ImageSource? _currentCover = null;
        private string _currentTitle = "Sin música";
        private string _currentArtist = "Esperando reproductor...";

        private const string PlayPathData = "M 3.5,2 L 12,7 L 3.5,12 Z";
        private const string PausePathData = "M 3,2 L 5.5,2 L 5.5,12 L 3,12 Z M 8.5,2 L 11,2 L 11,12 L 8.5,12 Z";

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        private static readonly ConcurrentDictionary<string, ImageSource> _channelAvatarCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string _avatarCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarMusicWidget", "Avatars");

        private static readonly ConcurrentDictionary<string, ImageSource> _youtubeThumbnailCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string _thumbCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarMusicWidget", "Thumbnails");
        private int _lastTrayLeft = -1;

        static MainWindow()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                _httpClient.DefaultRequestHeaders.Add("Accept-Language", "es-ES,es;q=0.9,en;q=0.8");
            }
            catch { }
        }

        #region Win32 API
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            else
                return new IntPtr(SetWindowLong(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private const int GWL_EXSTYLE = -20;
        private const int GWL_HWNDPARENT = -8;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOPMOST = 0x00000008;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOWMAXIMIZED = 3;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
        private const int ASFW_ANY = -1;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        private const uint GW_OWNER = 4;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
        private const int DWMWA_CLOAKED = 14;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);
        private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
        #endregion

        private enum ModoMonitor
        {
            Automatico = 0,
            Pantalla1 = 1,
            Pantalla2 = 2
        }
        private ModoMonitor _modoMonitor = ModoMonitor.Automatico;

        private DispatcherTimer? _volumeToastTimer;
        private DateTime _lastTimelineTick = DateTime.Now;

        public MainWindow()
        {
            InitializeComponent();

            // Temporizador reactivo para monitorear visibilidad (pantalla completa/barra oculta) y refrescar progreso
            _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _watchdogTimer.Tick += WatchdogTimer_Tick;

            // Temporizador debounce para cerrar la tarjeta flotante suavemente al salir el cursor
            _closeFlyoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _closeFlyoutTimer.Tick += CloseFlyoutTimer_Tick;

            SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.Invoke(PosicionarEnBarra);
            SystemEvents.UserPreferenceChanged += (s, e) => Dispatcher.Invoke(PosicionarEnBarra);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;

                // 1. Estilos extendidos (No activar, ToolWindow, Topmost)
                int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
                SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST);

                // 2. Afianzar Topmost
                SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch
            {
                // Ignorar excepciones menores de estilo Win32
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PosicionarEnBarra();
            InicializarTextosLocalizados();

            try
            {
                _flyoutWindow = new FlyoutWindow(this);
            }
            catch (Exception ex)
            {
                try
                {
                    System.IO.File.AppendAllText("widget_error.log", $"[{DateTime.Now}] Error al instanciar FlyoutWindow: {ex}\n");
                }
                catch { }
            }

            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _sessionManager.CurrentSessionChanged += (s, args) => Dispatcher.Invoke(ConectarSesion);
                _sessionManager.SessionsChanged += (s, args) => Dispatcher.Invoke(ConectarSesion);
                ConectarSesion();
            }
            catch
            {
                TxtTitle.Text = I18n.StartupError;
                TxtArtist.Text = I18n.CheckPermissions;
            }

            _watchdogTimer.Start();
        }

        private void InicializarTextosLocalizados()
        {
            TxtTitle.Text = I18n.NoMusic;
            TxtArtist.Text = I18n.Waiting;
            BtnPrev.ToolTip = I18n.PrevTooltip;
            BtnPlayPause.ToolTip = I18n.PlayPauseTooltip;
            BtnNext.ToolTip = I18n.NextTooltip;
            if (AlbumArtBorder != null) AlbumArtBorder.ToolTip = I18n.OpenPlayerTooltip;
            if (TrackInfoPanel != null) TrackInfoPanel.ToolTip = I18n.OpenPlayerTooltip;
            if (MenuReconnectItem != null) MenuReconnectItem.Header = I18n.MenuReconnect;
            if (MenuMonitorItem != null) MenuMonitorItem.Header = I18n.MenuMonitor;
            if (MenuMonitorAuto != null) MenuMonitorAuto.Header = I18n.MenuMonitorAuto;
            if (MenuMonitor1 != null) MenuMonitor1.Header = I18n.MenuMonitor1;
            if (MenuMonitor2 != null) MenuMonitor2.Header = I18n.MenuMonitor2;
            if (MenuExitItem != null) MenuExitItem.Header = I18n.MenuExit;
        }

        private void WatchdogTimer_Tick(object? sender, EventArgs e)
        {
            // 1. Gestionar visibilidad y monitor activo (Pantalla 1 o Pantalla 2 ante pantalla completa/HDMI)
            ActualizarUbicacionYVisibilidad();
            if (this.Visibility == Visibility.Collapsed)
            {
                return;
            }

            // 2. Operaciones periódicas de temporizador (cada ~1 segundo)
            var now = DateTime.Now;
            if ((now - _lastTimelineTick).TotalMilliseconds >= 950)
            {
                _lastTimelineTick = now;

                // Sincronizar estado de volumen y silencio si la ventana flotante está abierta
                if (_flyoutWindow != null && _flyoutWindow.Visibility == Visibility.Visible)
                {
                    _flyoutWindow.ActualizarEstadoVolumen();
                }

                // Si no hay sesión o no hay música, intentar reconectar (útil para pestañas silenciadas que empiezan a emitir)
                if (_currentSession == null || _currentTitle == "Sin música")
                {
                    ConectarSesion();
                }

                // Si se está reproduciendo música, avanzar el tiempo transcurrido de forma fluida
                if (_isPlaying && _duration > TimeSpan.Zero)
                {
                    _currentPosition = _currentPosition.Add(TimeSpan.FromSeconds(1));
                    if (_currentPosition > _duration) _currentPosition = _duration;
                    _flyoutWindow?.UpdateTimeline(_currentPosition, _duration);
                }
            }
        }

        #region Soporte Multi-Monitor y Detección de Pantalla Completa
        private static List<MONITORINFO> ObtenerMonitores()
        {
            var list = new List<MONITORINFO>();
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr d) =>
                {
                    var mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(hMon, ref mi))
                    {
                        list.Add(mi);
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return list;
        }

        private static bool EsVentanaSuperpuestaOIgnorable(string cls, string title, int exStyle)
        {
            // Ignorar ventanas de herramientas / tool windows
            if ((exStyle & 0x00000080) != 0) // WS_EX_TOOLWINDOW
                return true;

            // Ignorar ventanas transparentes al ratón / click-through
            if ((exStyle & 0x00000020) != 0) // WS_EX_TRANSPARENT
                return true;

            // Ignorar ventanas sin título (los vídeos y videojuegos a pantalla completa SIEMPRE tienen título)
            if (string.IsNullOrWhiteSpace(title))
                return true;

            // Ignorar elementos del sistema de Windows / escritorio
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                cls == "Shell_SecondaryTrayWnd" || cls == "Windows.UI.Core.CoreWindow" ||
                cls == "Xaml_WindowedPopupClass" || cls == "EdgeUiInputTopWndClass" ||
                cls == "DesktopWindowXamlSource" || cls == "SysListView32")
            {
                return true;
            }

            // Ignorar overlays gráficos y de captura (NVIDIA GeForce Overlay, Discord, Steam, GameBar, OBS, etc.)
            if (cls.Contains("CEF-OSC-WIDGET", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("Overlay", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("NVIDIA GeForce Overlay", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("GeForce Overlay", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("NVIDIA Share", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Discord Overlay", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Steam Overlay", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Xbox Game Bar", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Game Bar", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("RTSS", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("RivaTuner", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private bool MonitorTienePantallaCompleta(RECT rcMon, IntPtr myHandle, IntPtr flyoutHandle, IntPtr hTaskbar)
        {
            // 1. Comprobar si la ventana activa en primer plano cubre este monitor
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != hTaskbar && fg != myHandle && fg != flyoutHandle)
            {
                int hrFg = DwmGetWindowAttribute(fg, DWMWA_CLOAKED, out int isCloakedFg, sizeof(int));
                if (hrFg != 0 || isCloakedFg == 0)
                {
                    int exStyleFg = GetWindowLong(fg, GWL_EXSTYLE);
                    var sbClsFg = new StringBuilder(128);
                    GetClassName(fg, sbClsFg, sbClsFg.Capacity);
                    string clsFg = sbClsFg.ToString();

                    var sbTitleFg = new StringBuilder(128);
                    GetWindowText(fg, sbTitleFg, sbTitleFg.Capacity);
                    string titleFg = sbTitleFg.ToString();

                    if (!EsVentanaSuperpuestaOIgnorable(clsFg, titleFg, exStyleFg))
                    {
                        if (GetWindowRect(fg, out RECT fgRect))
                        {
                            if (fgRect.Left <= rcMon.Left + 2 &&
                                fgRect.Top <= rcMon.Top + 2 &&
                                fgRect.Right >= rcMon.Right - 2 &&
                                fgRect.Bottom >= rcMon.Bottom - 2)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            // 2. Comprobar todas las ventanas visibles en este monitor
            bool tienePantallaCompleta = false;
            EnumWindows((hWnd, lParam) =>
            {
                if (hWnd == myHandle || hWnd == flyoutHandle || hWnd == hTaskbar)
                    return true;

                if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                    return true;

                int hrCloaked = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int isCloaked, sizeof(int));
                if (hrCloaked == 0 && isCloaked != 0)
                    return true;

                int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                var sbCls = new StringBuilder(128);
                GetClassName(hWnd, sbCls, sbCls.Capacity);
                string c = sbCls.ToString();

                var sbTitle = new StringBuilder(128);
                GetWindowText(hWnd, sbTitle, sbTitle.Capacity);
                string t = sbTitle.ToString();

                if (EsVentanaSuperpuestaOIgnorable(c, t, exStyle))
                {
                    return true;
                }

                if (GetWindowRect(hWnd, out RECT r))
                {
                    if (r.Left <= rcMon.Left + 2 &&
                        r.Top <= rcMon.Top + 2 &&
                        r.Right >= rcMon.Right - 2 &&
                        r.Bottom >= rcMon.Bottom - 2)
                    {
                        tienePantallaCompleta = true;
                        return false;
                    }
                }

                return true;
            }, IntPtr.Zero);

            return tienePantallaCompleta;
        }

        private void ActualizarUbicacionYVisibilidad()
        {
            try
            {
                IntPtr myHandle = new WindowInteropHelper(this).Handle;
                if (myHandle == IntPtr.Zero) return;

                var monitores = ObtenerMonitores();
                if (monitores.Count == 0) return;

                // Monitor 1 (Principal)
                var mon1 = monitores.FirstOrDefault(m => (m.dwFlags & 1) != 0);
                if (mon1.rcMonitor.Right == 0 && mon1.rcMonitor.Bottom == 0) mon1 = monitores[0];

                // Monitor 2 (Secundario, si existe)
                var mon2 = monitores.FirstOrDefault(m => (m.dwFlags & 1) == 0);
                bool hayMon2 = mon2.rcMonitor.Right != 0 && mon2.rcMonitor.Bottom != 0;

                IntPtr flyoutHandle = _flyoutWindow != null ? new WindowInteropHelper(_flyoutWindow).Handle : IntPtr.Zero;
                IntPtr hTaskbar = FindWindow("Shell_TrayWnd", null);

                bool mon1EnPantallaCompleta = MonitorTienePantallaCompleta(mon1.rcMonitor, myHandle, flyoutHandle, hTaskbar);
                bool mon2EnPantallaCompleta = hayMon2 && MonitorTienePantallaCompleta(mon2.rcMonitor, myHandle, flyoutHandle, hTaskbar);

                int targetMonitor = 1;

                if (_modoMonitor == ModoMonitor.Pantalla2)
                {
                    // Forzado manual en Pantalla 2
                    targetMonitor = hayMon2 ? 2 : 1;
                }
                else if (_modoMonitor == ModoMonitor.Pantalla1)
                {
                    // Forzado manual en Pantalla 1
                    targetMonitor = 1;
                }
                else // ModoMonitor.Automatico
                {
                    if (mon1EnPantallaCompleta)
                    {
                        // Monitor 1 tiene pantalla completa (Prime Video, juego, etc.).
                        // Si hay un monitor 2 disponible, nos movemos a la barra del monitor 2!
                        if (hayMon2 && !mon2EnPantallaCompleta)
                        {
                            targetMonitor = 2;
                        }
                        else
                        {
                            OcultarWidget();
                            return;
                        }
                    }
                    else
                    {
                        // Pantalla 1 libre: SIEMPRE en Pantalla 1
                        targetMonitor = 1;
                    }
                }

                // Si estamos en Monitor 1, verificar también auto-hide de la barra de tareas
                if (targetMonitor == 1)
                {
                    if (hTaskbar != IntPtr.Zero)
                    {
                        if (!IsWindowVisible(hTaskbar))
                        {
                            OcultarWidget();
                            return;
                        }

                        if (GetWindowRect(hTaskbar, out RECT tbRect))
                        {
                            int tbHeight = tbRect.Bottom - tbRect.Top;
                            if (tbRect.Top >= mon1.rcMonitor.Bottom - 6 || tbHeight <= 6)
                            {
                                OcultarWidget();
                                return;
                            }
                        }
                    }

                    PosicionarEnMonitor1(mon1);
                }
                else if (targetMonitor == 2 && hayMon2)
                {
                    PosicionarEnMonitor2(mon2);
                }

                if (this.Visibility != Visibility.Visible)
                {
                    this.Visibility = Visibility.Visible;
                }

                var source = PresentationSource.FromVisual(this);
                double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                int xPx = (int)Math.Round(this.Left * dpiScale);
                int yPx = (int)Math.Round(this.Top * dpiScale);
                int wPx = (int)Math.Round(this.Width * dpiScale);
                int hPx = (int)Math.Round(this.Height * dpiScale);

                SetWindowPos(myHandle, HWND_TOPMOST, xPx, yPx, wPx, hPx, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
        }

        private void OcultarWidget()
        {
            if (this.Visibility != Visibility.Collapsed)
            {
                this.Visibility = Visibility.Collapsed;
                _flyoutWindow?.HideFlyout();
            }
        }

        private void PosicionarEnMonitor1(MONITORINFO mon1)
        {
            try
            {
                var source = PresentationSource.FromVisual(this);
                double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                double mon1WidthDips = (mon1.rcMonitor.Right - mon1.rcMonitor.Left) / dpiScale;
                double workAreaBottomDips = mon1.rcWork.Bottom / dpiScale;
                double taskbarHeightDips = (mon1.rcMonitor.Bottom - mon1.rcWork.Bottom) / dpiScale;
                if (taskbarHeightDips <= 0) taskbarHeightDips = 48;

                // Base por defecto: 280px a la izquierda del borde derecho
                double baseLeft = (mon1.rcMonitor.Left / dpiScale) + mon1WidthDips - this.Width - 280;

                // Detectar dinámicamente TrayNotifyWnd
                IntPtr hTaskbar = FindWindow("Shell_TrayWnd", null);
                if (hTaskbar != IntPtr.Zero)
                {
                    IntPtr hNotify = FindWindowEx(hTaskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                    if (hNotify != IntPtr.Zero && GetWindowRect(hNotify, out RECT nRect))
                    {
                        _lastTrayLeft = nRect.Left;
                        double trayLeftDips = nRect.Left / dpiScale;
                        double trayWidthDips = (nRect.Right - nRect.Left) / dpiScale;

                        bool updatePending = HayActualizacionWindowsPendiente();
                        double offsetMargin = (trayWidthDips > 185 || updatePending) ? 70 : 26;
                        double trayBasedLeft = trayLeftDips - this.Width - offsetMargin;

                        if (trayWidthDips > 185 || updatePending)
                        {
                            this.Left = Math.Min(baseLeft - 65, trayBasedLeft);
                        }
                        else
                        {
                            this.Left = Math.Min(baseLeft, trayBasedLeft);
                        }

                        this.Top = workAreaBottomDips + ((taskbarHeightDips - this.Height) / 2);
                        return;
                    }
                }

                bool pending = HayActualizacionWindowsPendiente();
                this.Left = pending ? baseLeft - 65 : baseLeft;
                this.Top = workAreaBottomDips + ((taskbarHeightDips - this.Height) / 2);
            }
            catch
            {
                this.Left = SystemParameters.PrimaryScreenWidth - this.Width - 280;
            }
        }

        private void PosicionarEnMonitor2(MONITORINFO mon2)
        {
            try
            {
                var source = PresentationSource.FromVisual(this);
                double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                double mon2RightDips = mon2.rcMonitor.Right / dpiScale;
                double mon2WorkBottomDips = mon2.rcWork.Bottom / dpiScale;
                double taskbarHeightDips = (mon2.rcMonitor.Bottom - mon2.rcWork.Bottom) / dpiScale;
                if (taskbarHeightDips <= 0) taskbarHeightDips = 48;

                // En la barra de tareas secundaria de Windows, situarse a la izquierda con separación holgada de la fecha y hora
                this.Left = mon2RightDips - this.Width - 170;
                this.Top = mon2WorkBottomDips + ((taskbarHeightDips - this.Height) / 2);
            }
            catch { }
        }

        private void PosicionarEnBarra()
        {
            ActualizarUbicacionYVisibilidad();
        }

        private static bool HayActualizacionWindowsPendiente()
        {
            try
            {
                var p1 = System.Diagnostics.Process.GetProcessesByName("MusNotification");
                if (p1.Length > 0) return true;
                var p2 = System.Diagnostics.Process.GetProcessesByName("MusNotificationUx");
                if (p2.Length > 0) return true;

                using var k1 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
                if (k1 != null) return true;
                using var k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\Orchestrator\RebootRequired");
                if (k2 != null) return true;
                using var k3 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                if (k3 != null) return true;
                using var k4 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings");
                if (k4 != null)
                {
                    object? reboot = k4.GetValue("RebootPending");
                    if (reboot is int r && r == 1) return true;
                    object? restart = k4.GetValue("RestartRequired");
                    if (restart is int rr && rr == 1) return true;
                }
                using var k5 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\PostRebootReporting");
                if (k5 != null) return true;
            }
            catch { }
            return false;
        }

        private void MenuMonitorAuto_Click(object sender, RoutedEventArgs e)
        {
            _modoMonitor = ModoMonitor.Automatico;
            ActualizarCheckmarksMonitor();
            ActualizarUbicacionYVisibilidad();
        }

        private void MenuMonitor1_Click(object sender, RoutedEventArgs e)
        {
            _modoMonitor = ModoMonitor.Pantalla1;
            ActualizarCheckmarksMonitor();
            ActualizarUbicacionYVisibilidad();
        }

        private void MenuMonitor2_Click(object sender, RoutedEventArgs e)
        {
            _modoMonitor = ModoMonitor.Pantalla2;
            ActualizarCheckmarksMonitor();
            ActualizarUbicacionYVisibilidad();
        }

        private void ActualizarCheckmarksMonitor()
        {
            if (MenuMonitorAuto != null) MenuMonitorAuto.IsChecked = _modoMonitor == ModoMonitor.Automatico;
            if (MenuMonitor1 != null) MenuMonitor1.IsChecked = _modoMonitor == ModoMonitor.Pantalla1;
            if (MenuMonitor2 != null) MenuMonitor2.IsChecked = _modoMonitor == ModoMonitor.Pantalla2;
        }
        #endregion

        private async void ConectarSesion()
        {
            if (_sessionManager == null) return;

            var nuevaSesion = await ObtenerMejorSesionAsync();

            if (_currentSession != null && _currentSession != nuevaSesion)
            {
                _currentSession.MediaPropertiesChanged -= Sesion_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= Sesion_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= Sesion_TimelinePropertiesChanged;
            }

            _currentSession = nuevaSesion;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= Sesion_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= Sesion_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= Sesion_TimelinePropertiesChanged;

                _currentSession.MediaPropertiesChanged += Sesion_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += Sesion_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += Sesion_TimelinePropertiesChanged;

                RefrescarDatos();
                RefrescarTimeline();
            }
            else
            {
                _isPlaying = false;
                _currentTitle = I18n.NoMusic;
                _currentArtist = I18n.PlayerInactive;
                _currentCover = null;
                _currentPosition = TimeSpan.Zero;
                _duration = TimeSpan.Zero;

                TxtTitle.Text = _currentTitle;
                TxtArtist.Text = _currentArtist;
                AlbumArt.Source = null;
                MainPlayPausePath.Data = Geometry.Parse(PlayPathData);
                MainPlayPausePath.Margin = new Thickness(1, 0, 0, 0);

                _spotifyShuffleMode = 0;
                _flyoutWindow?.UpdateTrackInfo(null, _currentTitle, _currentArtist, false);
                _flyoutWindow?.UpdateTimeline(TimeSpan.Zero, TimeSpan.Zero);
                _flyoutWindow?.UpdateShuffleState(false, false, false);
                _flyoutWindow?.UpdateRepeatState(false, MediaPlaybackAutoRepeatMode.None);
                ActualizarMarquee();
            }
        }

        private async Task<GlobalSystemMediaTransportControlsSession?> ObtenerMejorSesionAsync()
        {
            if (_sessionManager == null) return null;

            // 1. Probar la sesión marcada como actual por Windows
            var sesionActual = _sessionManager.GetCurrentSession();
            if (sesionActual != null)
            {
                try
                {
                    var props = await sesionActual.TryGetMediaPropertiesAsync();
                    if (props != null && !string.IsNullOrWhiteSpace(props.Title))
                    {
                        return sesionActual;
                    }
                }
                catch { }
            }

            // 2. Si la sesión actual es nula o no reporta título (muy común con pestañas silenciadas en Twitch o YouTube),
            // explorar todas las sesiones activas en el sistema
            try
            {
                var sesiones = _sessionManager.GetSessions();
                if (sesiones != null && sesiones.Count > 0)
                {
                    // Prioridad A: Sesiones que estén reproduciendo (Playing) y tengan título
                    foreach (var s in sesiones)
                    {
                        try
                        {
                            var info = s.GetPlaybackInfo();
                            if (info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                            {
                                var props = await s.TryGetMediaPropertiesAsync();
                                if (props != null && !string.IsNullOrWhiteSpace(props.Title))
                                {
                                    return s;
                                }
                            }
                        }
                        catch { }
                    }

                    // Prioridad B: Cualquier sesión con título válido (pestañas silenciadas que reporten Paused/Opened)
                    foreach (var s in sesiones)
                    {
                        try
                        {
                            var props = await s.TryGetMediaPropertiesAsync();
                            if (props != null && !string.IsNullOrWhiteSpace(props.Title))
                            {
                                return s;
                            }
                        }
                        catch { }
                    }
                }

                // 3. Si no encontramos nada nuevo pero la sesión que ya teníamos sigue viva en GetSessions(), conservarla
                if (_currentSession != null && sesiones != null)
                {
                    foreach (var s in sesiones)
                    {
                        if (s.SourceAppUserModelId == _currentSession.SourceAppUserModelId)
                        {
                            return _currentSession;
                        }
                    }
                }
            }
            catch { }

            return sesionActual;
        }

        private void Sesion_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            Dispatcher.Invoke(RefrescarDatos);
        }

        private void Sesion_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            Dispatcher.Invoke(RefrescarDatos);
        }

        private void Sesion_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            Dispatcher.Invoke(RefrescarTimeline);
        }

        private static ImageSource? _netflixDefaultCover;

        private static ImageSource ObtenerPortadaNetflixDefault()
        {
            if (_netflixDefaultCover != null) return _netflixDefaultCover;

            int width = 96;
            int height = 96;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bgBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
                dc.DrawRoundedRectangle(bgBrush, null, new Rect(0, 0, width, height), 8, 8);

                var redLight = new SolidColorBrush(Color.FromRgb(0xE5, 0x09, 0x14));
                var redDark = new SolidColorBrush(Color.FromRgb(0xB8, 0x1D, 0x24));

                dc.DrawRectangle(redLight, null, new Rect(24, 18, 14, 60));
                dc.DrawRectangle(redLight, null, new Rect(58, 18, 14, 60));

                var streamGeom = new StreamGeometry();
                using (var ctx = streamGeom.Open())
                {
                    ctx.BeginFigure(new Point(24, 18), true, true);
                    ctx.LineTo(new Point(38, 18), true, false);
                    ctx.LineTo(new Point(72, 78), true, false);
                    ctx.LineTo(new Point(58, 78), true, false);
                }
                streamGeom.Freeze();
                dc.DrawGeometry(redDark, null, streamGeom);
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            _netflixDefaultCover = rtb;
            return _netflixDefaultCover;
        }

        private string ExtraerTituloSerieNetflix(GlobalSystemMediaTransportControlsSessionMediaProperties props)
        {
            if (!string.IsNullOrWhiteSpace(props.Subtitle) && !props.Subtitle.Trim().Equals("Netflix", StringComparison.OrdinalIgnoreCase))
            {
                return props.Subtitle.Trim();
            }

            if (!string.IsNullOrWhiteSpace(props.AlbumTitle) && !props.AlbumTitle.Trim().Equals("Netflix", StringComparison.OrdinalIgnoreCase))
            {
                return props.AlbumTitle.Trim();
            }

            if (!string.IsNullOrWhiteSpace(props.AlbumArtist) && !props.AlbumArtist.Trim().Equals("Netflix", StringComparison.OrdinalIgnoreCase))
            {
                return props.AlbumArtist.Trim();
            }

            string t = props.Title ?? "";
            if (t.Contains(" | Netflix", StringComparison.OrdinalIgnoreCase))
            {
                return t.Substring(0, t.IndexOf(" | Netflix", StringComparison.OrdinalIgnoreCase)).Trim();
            }
            if (t.Contains(" - Netflix", StringComparison.OrdinalIgnoreCase))
            {
                return t.Substring(0, t.IndexOf(" - Netflix", StringComparison.OrdinalIgnoreCase)).Trim();
            }
            if (t.StartsWith("Netflix - ", StringComparison.OrdinalIgnoreCase))
            {
                return t.Substring("Netflix - ".Length).Trim();
            }
            if (!t.Trim().Equals("Netflix", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(t))
            {
                return t.Trim();
            }

            string a = props.Artist ?? "";
            if (!a.Trim().Equals("Netflix", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(a))
            {
                return a.Trim();
            }

            return I18n.PlayingFallback;
        }

        #region Detección y Carátulas para YouTube y YouTube Shorts
        private static bool EsNavegador(string? appModelId)
        {
            if (string.IsNullOrEmpty(appModelId)) return false;
            return appModelId.Contains("Opera", StringComparison.OrdinalIgnoreCase) ||
                   appModelId.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                   appModelId.Contains("MSEdge", StringComparison.OrdinalIgnoreCase) ||
                   appModelId.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
                   appModelId.Contains("Brave", StringComparison.OrdinalIgnoreCase) ||
                   appModelId.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase) ||
                   appModelId.Contains("Firefox", StringComparison.OrdinalIgnoreCase);
        }

        private static bool EsYouTubeShort(string title, int pixelWidth, int pixelHeight, bool isBrowser)
        {
            if (!isBrowser) return false;

            // 1. Detección por título explícito (#shorts o #short)
            if (!string.IsNullOrWhiteSpace(title))
            {
                if (title.Contains("#shorts", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("#short", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // 2. Detección por relación de aspecto vertical (vídeos 9:16 típicos de Shorts)
            // Los vídeos horizontales de YouTube son 16:9 (pixelWidth > pixelHeight * 1.5).
            // En Shorts, la altura es significativamente mayor que el ancho.
            if (pixelHeight > 0 && pixelWidth > 0 && pixelHeight > pixelWidth)
            {
                return true;
            }

            return false;
        }

        private static bool EsSesionYouTube(string? appModelId, string? title, string? artist, string? albumTitle)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;

            if (title.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ||
                artist?.Contains("YouTube", StringComparison.OrdinalIgnoreCase) == true ||
                albumTitle?.Contains("YouTube", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            if (title.Contains("#shorts", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("#short", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (EsNavegador(appModelId))
            {
                try
                {
                    bool foundYouTube = false;
                    EnumWindows((hWnd, lParam) =>
                    {
                        if (!IsWindowVisible(hWnd) || IsIconic(hWnd)) return true;
                        var sb = new StringBuilder(256);
                        GetWindowText(hWnd, sb, sb.Capacity);
                        string wTitle = sb.ToString();
                        if (wTitle.Contains("YouTube", StringComparison.OrdinalIgnoreCase))
                        {
                            GetWindowThreadProcessId(hWnd, out uint pid);
                            try
                            {
                                var proc = Process.GetProcessById((int)pid);
                                if (proc != null && EsNavegador(proc.ProcessName))
                                {
                                    foundYouTube = true;
                                    return false;
                                }
                            }
                            catch { }
                        }
                        return true;
                    }, IntPtr.Zero);

                    if (foundYouTube) return true;
                }
                catch { }
            }

            return false;
        }

        private static string LimpiarTituloParaBusquedaYouTube(string rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle)) return "";
            string clean = rawTitle.Trim();
            // Quitar badge de notificaciones de pestañas en navegadores como (1), (99+)
            clean = Regex.Replace(clean, @"^\(\d+\+?\)\s*", "");
            // Quitar sufijo "- YouTube"
            clean = Regex.Replace(clean, @"\s*-\s*YouTube$", "", RegexOptions.IgnoreCase);
            // Quitar etiquetas #shorts
            clean = Regex.Replace(clean, @"#shorts?", "", RegexOptions.IgnoreCase);
            return clean.Trim();
        }

        private static bool TryObtenerLogoCanalYouTubeEnCache(string channelName, out ImageSource? logo)
        {
            logo = null;
            if (string.IsNullOrWhiteSpace(channelName)) return false;

            channelName = channelName.Trim();

            // 1. Comprobar caché en memoria
            if (_channelAvatarCache.TryGetValue(channelName, out logo) && logo != null)
            {
                return true;
            }

            // 2. Comprobar caché persistente en disco
            try
            {
                string safeFileName = Regex.Replace(channelName, @"[^\w\-\.]", "_") + ".jpg";
                string cachePath = Path.Combine(_avatarCacheDir, safeFileName);
                if (File.Exists(cachePath))
                {
                    byte[] fileBytes = File.ReadAllBytes(cachePath);
                    if (fileBytes != null && fileBytes.Length > 0)
                    {
                        logo = CrearBitmapDesdeBytes(fileBytes);
                        if (logo != null)
                        {
                            _channelAvatarCache[channelName] = logo;
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool TryObtenerThumbnailYouTubeEnCache(string titleKey, out ImageSource? cover)
        {
            cover = null;
            if (string.IsNullOrWhiteSpace(titleKey)) return false;

            titleKey = titleKey.Trim();

            // 1. Comprobar caché en memoria
            if (_youtubeThumbnailCache.TryGetValue(titleKey, out cover) && cover != null)
            {
                return true;
            }

            // 2. Comprobar caché persistente en disco
            try
            {
                string safeFileName = Regex.Replace(titleKey, @"[^\w\-\.]", "_") + ".jpg";
                string cachePath = Path.Combine(_thumbCacheDir, safeFileName);
                if (File.Exists(cachePath))
                {
                    byte[] fileBytes = File.ReadAllBytes(cachePath);
                    if (fileBytes != null && fileBytes.Length > 0)
                    {
                        cover = CrearBitmapDesdeBytes(fileBytes);
                        if (cover != null)
                        {
                            _youtubeThumbnailCache[titleKey] = cover;
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private static BitmapImage? CrearBitmapDesdeBytes(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<ImageSource?> ObtenerLogoCanalYouTubeAsync(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName)) return null;

            channelName = channelName.Trim();

            if (TryObtenerLogoCanalYouTubeEnCache(channelName, out var cachedLogo) && cachedLogo != null)
            {
                return cachedLogo;
            }

            try
            {
                string searchUrl = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(channelName)}";
                var response = await _httpClient.GetAsync(searchUrl);
                if (!response.IsSuccessStatusCode) return null;

                string html = await response.Content.ReadAsStringAsync();

                // Expresión regular para localizar el avatar oficial del canal (yt3.ggpht.com o yt3.googleusercontent.com)
                var match = Regex.Match(html, @"https://yt3\.(?:googleusercontent|ggpht)\.com/(?:ytc/)?[a-zA-Z0-9_\-=]+");
                if (match.Success)
                {
                    string avatarUrl = match.Value;
                    if (avatarUrl.Contains("=s"))
                    {
                        avatarUrl = Regex.Replace(avatarUrl, @"=s\d+.*", "=s256-c-k-c0x00ffffff-no-rj");
                    }
                    else
                    {
                        avatarUrl += "=s256-c-k-c0x00ffffff-no-rj";
                    }

                    byte[] imgBytes = await _httpClient.GetByteArrayAsync(avatarUrl);
                    if (imgBytes != null && imgBytes.Length > 0)
                    {
                        var bmp = CrearBitmapDesdeBytes(imgBytes);
                        if (bmp != null)
                        {
                            _channelAvatarCache[channelName] = bmp;

                            // Guardar en caché de disco de forma asíncrona
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    if (!Directory.Exists(_avatarCacheDir))
                                    {
                                        Directory.CreateDirectory(_avatarCacheDir);
                                    }
                                    string safeFileName = Regex.Replace(channelName, @"[^\w\-\.]", "_") + ".jpg";
                                    string cachePath = Path.Combine(_avatarCacheDir, safeFileName);
                                    await File.WriteAllBytesAsync(cachePath, imgBytes);
                                }
                                catch { }
                            });

                            return bmp;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private async Task CargarLogoCanalAsync(string channelName, string expectedTitle)
        {
            try
            {
                var logo = await ObtenerLogoCanalYouTubeAsync(channelName);
                if (logo != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Asegurarse de que el usuario sigue reproduciendo el mismo canal y título
                        if (_currentArtist == channelName && _currentTitle == expectedTitle)
                        {
                            _currentCover = logo;
                            AlbumArt.Source = logo;
                            _flyoutWindow?.UpdateTrackInfo(_currentCover, _currentTitle, _currentArtist, _isPlaying);
                        }
                    });
                }
            }
            catch { }
        }

        private static async Task<(ImageSource? Cover, string? ChannelName)> ObtenerThumbnailVideoYouTubeAsync(string cleanTitle, string artist)
        {
            if (string.IsNullOrWhiteSpace(cleanTitle)) return (null, null);

            if (TryObtenerThumbnailYouTubeEnCache(cleanTitle, out var cached) && cached != null)
            {
                return (cached, null);
            }

            try
            {
                string query = cleanTitle;
                if (!string.IsNullOrWhiteSpace(artist) &&
                    !artist.Contains("YouTube", StringComparison.OrdinalIgnoreCase) &&
                    !artist.Contains("Opera", StringComparison.OrdinalIgnoreCase) &&
                    !artist.Contains("Chrome", StringComparison.OrdinalIgnoreCase) &&
                    !artist.Contains("Edge", StringComparison.OrdinalIgnoreCase))
                {
                    query += " " + artist;
                }

                string searchUrl = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}";
                var response = await _httpClient.GetAsync(searchUrl);
                if (!response.IsSuccessStatusCode) return (null, null);

                string html = await response.Content.ReadAsStringAsync();

                // Extraer el nombre del canal si es posible
                string? detectedChannel = null;
                var chMatch = Regex.Match(html, @"\""ownerText\""\s*:\s*\{\s*\""runs\""\s*:\s*\[\s*\{\s*\""text\""\s*:\s*\""([^\""]+)\""");
                if (chMatch.Success)
                {
                    detectedChannel = chMatch.Groups[1].Value;
                }

                // Extraer videoId del vídeo en los resultados
                var match = Regex.Match(html, @"\""videoId\""\s*:\s*\""([a-zA-Z0-9_-]{11})\""");
                if (!match.Success)
                {
                    match = Regex.Match(html, @"/watch\?v=([a-zA-Z0-9_-]{11})");
                }

                if (match.Success)
                {
                    string videoId = match.Groups[1].Value;

                    byte[]? imgBytes = null;
                    // Probar primero con maxresdefault.jpg (1280x720) y luego hqdefault.jpg (480x360)
                    try
                    {
                        var maxResResp = await _httpClient.GetAsync($"https://i.ytimg.com/vi/{videoId}/maxresdefault.jpg");
                        if (maxResResp.IsSuccessStatusCode)
                        {
                            byte[] candidate = await maxResResp.Content.ReadAsByteArrayAsync();
                            if (candidate != null && candidate.Length > 2000)
                            {
                                imgBytes = candidate;
                            }
                        }
                    }
                    catch { }

                    if (imgBytes == null || imgBytes.Length == 0)
                    {
                        try
                        {
                            imgBytes = await _httpClient.GetByteArrayAsync($"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg");
                        }
                        catch { }
                    }

                    if (imgBytes != null && imgBytes.Length > 0)
                    {
                        var bmp = CrearBitmapDesdeBytes(imgBytes);
                        if (bmp != null)
                        {
                            _youtubeThumbnailCache[cleanTitle] = bmp;

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    if (!Directory.Exists(_thumbCacheDir))
                                    {
                                        Directory.CreateDirectory(_thumbCacheDir);
                                    }
                                    string safeFileName = Regex.Replace(cleanTitle, @"[^\w\-\.]", "_") + ".jpg";
                                    string cachePath = Path.Combine(_thumbCacheDir, safeFileName);
                                    await File.WriteAllBytesAsync(cachePath, imgBytes);
                                }
                                catch { }
                            });

                            return (bmp, detectedChannel);
                        }
                    }
                }
            }
            catch { }

            return (null, null);
        }

        private async Task CargarThumbnailYouTubeAsync(string cleanTitle, string artist)
        {
            try
            {
                var (thumb, channel) = await ObtenerThumbnailVideoYouTubeAsync(cleanTitle, artist);
                if (thumb != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_currentTitle == cleanTitle)
                        {
                            _currentCover = thumb;
                            AlbumArt.Source = thumb;
                            if (!string.IsNullOrWhiteSpace(channel) && (_currentArtist == "Artista desconocido" || _currentArtist == "YouTube"))
                            {
                                _currentArtist = channel;
                                TxtArtist.Text = _currentArtist;
                                TxtArtist.ToolTip = _currentArtist;
                            }
                            _flyoutWindow?.UpdateTrackInfo(_currentCover, _currentTitle, _currentArtist, _isPlaying);
                        }
                    });
                }
            }
            catch { }
        }
        #endregion

        private async void RefrescarDatos()
        {
            if (_currentSession == null) return;

            try
            {
                var props = await _currentSession.TryGetMediaPropertiesAsync();
                var info = _currentSession.GetPlaybackInfo();

                if (props != null)
                {
                    bool isNetflix = (_currentSession.SourceAppUserModelId?.Contains("Netflix", StringComparison.OrdinalIgnoreCase) == true) ||
                                     (props.Title?.Contains("Netflix", StringComparison.OrdinalIgnoreCase) == true) ||
                                     (props.Artist?.Contains("Netflix", StringComparison.OrdinalIgnoreCase) == true);

                    if (isNetflix)
                    {
                        string serie = ExtraerTituloSerieNetflix(props);
                        _currentTitle = "Netflix";
                        _currentArtist = serie;
                    }
                    else
                    {
                        _currentTitle = string.IsNullOrWhiteSpace(props.Title) ? "Pista desconocida" : props.Title;
                        _currentArtist = string.IsNullOrWhiteSpace(props.Artist) ? "Artista desconocido" : props.Artist;
                    }

                    BitmapImage? bmp = null;
                    if (props.Thumbnail != null)
                    {
                        try
                        {
                            using IRandomAccessStreamWithContentType stream = await props.Thumbnail.OpenReadAsync();
                            using Stream netStream = stream.AsStreamForRead();
                            var tempBmp = new BitmapImage();
                            tempBmp.BeginInit();
                            tempBmp.CacheOption = BitmapCacheOption.OnLoad;
                            tempBmp.StreamSource = netStream;
                            tempBmp.EndInit();
                            tempBmp.Freeze();
                            bmp = tempBmp;
                        }
                        catch { }
                    }

                    bool isBrowser = EsNavegador(_currentSession.SourceAppUserModelId);
                    bool isYouTube = !isNetflix && EsSesionYouTube(_currentSession.SourceAppUserModelId, _currentTitle, _currentArtist, props.AlbumTitle);
                    bool isShort = isYouTube && EsYouTubeShort(_currentTitle, bmp?.PixelWidth ?? 0, bmp?.PixelHeight ?? 0, isBrowser);

                    if (isYouTube)
                    {
                        // Limpiar título de YouTube para remover sufijos molestos como " - YouTube" o contadores de pestañas "(1) "
                        string cleanTitle = LimpiarTituloParaBusquedaYouTube(_currentTitle);
                        if (!string.IsNullOrEmpty(cleanTitle))
                        {
                            _currentTitle = cleanTitle;
                        }
                    }

                    TxtTitle.Text = _currentTitle;
                    TxtArtist.Text = _currentArtist;
                    TxtTitle.ToolTip = _currentTitle;
                    TxtArtist.ToolTip = _currentArtist;

                    if (isShort && !string.IsNullOrWhiteSpace(_currentArtist))
                    {
                        string canal = _currentArtist;
                        if (TryObtenerLogoCanalYouTubeEnCache(canal, out var logoCached) && logoCached != null)
                        {
                            _currentCover = logoCached;
                            AlbumArt.Source = logoCached;
                        }
                        else
                        {
                            // Mostramos el thumbnail temporalmente mientras se descarga el avatar en segundo plano
                            _currentCover = bmp;
                            AlbumArt.Source = bmp;
                            _ = CargarLogoCanalAsync(canal, _currentTitle);
                        }
                    }
                    else if (isYouTube && !isShort)
                    {
                        string titleKey = _currentTitle;
                        if (TryObtenerThumbnailYouTubeEnCache(titleKey, out var ytCached) && ytCached != null)
                        {
                            _currentCover = ytCached;
                            AlbumArt.Source = ytCached;
                        }
                        else
                        {
                            // Mostramos el thumbnail de SMTC temporalmente si existe mientras se descarga la miniatura oficial
                            _currentCover = bmp;
                            AlbumArt.Source = bmp;
                            _ = CargarThumbnailYouTubeAsync(titleKey, _currentArtist);
                        }
                    }
                    else if (bmp != null)
                    {
                        _currentCover = bmp;
                        AlbumArt.Source = bmp;
                    }
                    else if (isNetflix)
                    {
                        // Portada estilizada de Netflix oficial en alta resolución cuando DRM bloquea el thumbnail
                        _currentCover = ObtenerPortadaNetflixDefault();
                        AlbumArt.Source = _currentCover;
                    }
                    else
                    {
                        _currentCover = null;
                        AlbumArt.Source = null;
                    }
                }

                if (info != null)
                {
                    _isPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    MainPlayPausePath.Data = Geometry.Parse(_isPlaying ? PausePathData : PlayPathData);
                    MainPlayPausePath.Margin = _isPlaying ? new Thickness(0) : new Thickness(1, 0, 0, 0);

                    // Actualizar estado de Shuffle y Repeat para Spotify
                    bool isSpotify = _currentSession.SourceAppUserModelId?.Contains("Spotify", StringComparison.OrdinalIgnoreCase) == true;
                    bool isShuffle = info.IsShuffleActive == true;
                    var repeatMode = info.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None;

                    if (isSpotify)
                    {
                        if (!isShuffle)
                        {
                            _spotifyShuffleMode = 0;
                            _flyoutWindow?.UpdateShuffleState(true, false, false);
                        }
                        else
                        {
                            _flyoutWindow?.UpdateShuffleState(true, true, _spotifyShuffleMode == 2);
                            SincronizarModoShuffleSpotifyAsync();
                        }
                    }
                    else
                    {
                        _flyoutWindow?.UpdateShuffleState(false, false, false);
                    }

                    _flyoutWindow?.UpdateRepeatState(isSpotify, repeatMode);
                }

                _flyoutWindow?.UpdateTrackInfo(_currentCover, _currentTitle, _currentArtist, _isPlaying);
                RefrescarTimeline();
                ActualizarMarquee();
            }
            catch
            {
                // Evitar cierres por cambio rápido de pista
            }
        }

        private void RefrescarTimeline()
        {
            if (_currentSession == null) return;

            try
            {
                var timeline = _currentSession.GetTimelineProperties();
                if (timeline != null)
                {
                    _currentPosition = timeline.Position;
                    _duration = timeline.EndTime;
                    _flyoutWindow?.UpdateTimeline(_currentPosition, _duration);
                }
            }
            catch
            {
                // Manejar reproductores que no expongan timeline
            }
        }

        #region Acciones de Control Multimedia
        public async void EjecutarPlayPausa()
        {
            if (_currentSession != null)
            {
                await _currentSession.TryTogglePlayPauseAsync();
            }
        }

        public async void EjecutarAnterior()
        {
            if (_currentSession != null)
            {
                await _currentSession.TrySkipPreviousAsync();
            }
        }

        public async void EjecutarSiguiente()
        {
            if (_currentSession != null)
            {
                await _currentSession.TrySkipNextAsync();
            }
        }

        public async void SolicitarCambioPosicion(TimeSpan position)
        {
            if (_currentSession != null)
            {
                try
                {
                    await _currentSession.TryChangePlaybackPositionAsync(position.Ticks);
                    _currentPosition = position;
                    _flyoutWindow?.UpdateTimeline(_currentPosition, _duration);
                }
                catch
                {
                    // Algunos reproductores pueden rechazar el cambio de posición
                }
            }
        }

        private int _spotifyShuffleMode = 0; // 0 = Desactivado, 1 = Aleatorio normal, 2 = Smart Shuffle

        private static readonly System.Windows.Automation.Condition SpotifyShuffleCondition = new OrCondition(
            new PropertyCondition(AutomationElement.NameProperty, "Activar el orden aleatorio inteligente"),
            new PropertyCondition(AutomationElement.NameProperty, "Activar el orden aleatorio"),
            new PropertyCondition(AutomationElement.NameProperty, "Desactivar el orden aleatorio"),
            new PropertyCondition(AutomationElement.NameProperty, "Enable Smart Shuffle"),
            new PropertyCondition(AutomationElement.NameProperty, "Enable shuffle"),
            new PropertyCondition(AutomationElement.NameProperty, "Disable shuffle")
        );

        private DateTime _lastShuffleSync = DateTime.MinValue;

        private void SincronizarModoShuffleSpotifyAsync()
        {
            if ((DateTime.Now - _lastShuffleSync).TotalMilliseconds < 1200) return;
            _lastShuffleSync = DateTime.Now;

            Task.Run(() =>
            {
                try
                {
                    IntPtr hWnd = ObtenerVentanaPrincipal("Spotify");
                    if (hWnd == IntPtr.Zero) return;

                    var root = AutomationElement.FromHandle(hWnd);
                    if (root == null) return;

                    var btn = root.FindFirst(TreeScope.Descendants, SpotifyShuffleCondition);
                    if (btn != null)
                    {
                        ActualizarEstadoShuffleDesdeBoton(btn);
                    }
                }
                catch { }
            });
        }

        private void ActualizarEstadoShuffleDesdeBoton(AutomationElement btn)
        {
            try
            {
                string name = btn.Current.Name ?? "";
                bool isShuffle = false;
                bool isSmart = false;

                if (name.Contains("desactivar", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("disable", StringComparison.OrdinalIgnoreCase))
                {
                    // En Spotify, si el botón dice "Desactivar", el estado actual es Smart Shuffle (el último del ciclo)
                    isShuffle = true;
                    isSmart = true;
                    _spotifyShuffleMode = 2;
                }
                else if (name.Contains("inteligente", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("smart", StringComparison.OrdinalIgnoreCase))
                {
                    // En Spotify, si el botón dice "Activar orden aleatorio inteligente", el estado actual es Shuffle normal
                    isShuffle = true;
                    isSmart = false;
                    _spotifyShuffleMode = 1;
                }
                else
                {
                    // "Activar el orden aleatorio" -> Shuffle apagado
                    isShuffle = false;
                    isSmart = false;
                    _spotifyShuffleMode = 0;
                }

                Dispatcher.Invoke(() =>
                {
                    _flyoutWindow?.UpdateShuffleState(true, isShuffle, isSmart);
                });
            }
            catch { }
        }

        public async void AlternarShuffle()
        {
            if (_currentSession == null) return;

            bool isSpotify = _currentSession.SourceAppUserModelId?.Contains("Spotify", StringComparison.OrdinalIgnoreCase) == true;
            if (!isSpotify) return;

            bool clicked = false;
            try
            {
                clicked = await Task.Run(() =>
                {
                    IntPtr hWnd = ObtenerVentanaPrincipal("Spotify");
                    if (hWnd == IntPtr.Zero) return false;

                    var root = AutomationElement.FromHandle(hWnd);
                    if (root == null) return false;

                    var btn = root.FindFirst(TreeScope.Descendants, SpotifyShuffleCondition);
                    if (btn != null)
                    {
                        if (btn.TryGetCurrentPattern(InvokePattern.Pattern, out object invObj))
                        {
                            ((InvokePattern)invObj).Invoke();
                            System.Threading.Thread.Sleep(80);
                            ActualizarEstadoShuffleDesdeBoton(btn);
                            return true;
                        }
                    }
                    return false;
                });
            }
            catch { }

            if (!clicked)
            {
                try
                {
                    _spotifyShuffleMode = (_spotifyShuffleMode + 1) % 3;
                    if (_spotifyShuffleMode == 0)
                    {
                        await _currentSession.TryChangeShuffleActiveAsync(false);
                        _flyoutWindow?.UpdateShuffleState(true, false, false);
                    }
                    else if (_spotifyShuffleMode == 1)
                    {
                        await _currentSession.TryChangeShuffleActiveAsync(true);
                        _flyoutWindow?.UpdateShuffleState(true, true, false);
                    }
                    else
                    {
                        _flyoutWindow?.UpdateShuffleState(true, true, true);
                    }
                }
                catch { }
            }
        }

        public async void AlternarRepeat()
        {
            if (_currentSession != null)
            {
                try
                {
                    var info = _currentSession.GetPlaybackInfo();
                    var actual = info?.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None;

                    MediaPlaybackAutoRepeatMode siguiente = actual switch
                    {
                        MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
                        MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
                        MediaPlaybackAutoRepeatMode.Track => MediaPlaybackAutoRepeatMode.None,
                        _ => MediaPlaybackAutoRepeatMode.None
                    };

                    await _currentSession.TryChangeAutoRepeatModeAsync(siguiente);
                    _flyoutWindow?.UpdateRepeatState(true, siguiente);
                }
                catch
                {
                    // Algunos reproductores pueden no soportar repeat
                }
            }
        }

        private static void TraerVentanaAlFrente(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;

            try
            {
                if (IsIconic(hWnd))
                {
                    // Solo si la ventana está explícitamente minimizada en la barra de tareas, restaurarla
                    var wp = new WINDOWPLACEMENT();
                    wp.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
                    if (GetWindowPlacement(hWnd, ref wp) && (wp.showCmd == SW_SHOWMAXIMIZED || (wp.flags & 2) != 0))
                    {
                        ShowWindow(hWnd, SW_SHOWMAXIMIZED);
                    }
                    else
                    {
                        ShowWindow(hWnd, SW_SHOWNORMAL);
                    }
                }

                // Si la ventana ya está abierta (por ejemplo, maximizada en el monitor 2 o en segundo plano),
                // NUNCA llamamos a ShowWindow ni a BringWindowToTop, ya que eso desmaximiza la ventana en DWM.
                // Permitimos el cambio de foco en Windows de forma limpia:
                AllowSetForegroundWindow(ASFW_ANY);
                SetForegroundWindow(hWnd);
            }
            catch
            {
                SetForegroundWindow(hWnd);
            }
        }

        private static IntPtr ObtenerVentanaPrincipal(string processName)
        {
            var procs = Process.GetProcessesByName(processName);
            if (procs.Length == 0) return IntPtr.Zero;

            var targetPids = new HashSet<int>(procs.Select(p => p.Id));
            IntPtr bestHwnd = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (targetPids.Contains((int)pid))
                    {
                        // La ventana principal no debe pertenecer a otra ventana (GW_OWNER == 0)
                        if (GetWindow(hWnd, GW_OWNER) == IntPtr.Zero)
                        {
                            var sbTitle = new StringBuilder(256);
                            GetWindowText(hWnd, sbTitle, 256);
                            string title = sbTitle.ToString();

                            // Filtrar ventanas de renderizado CEF u offscreen sin título
                            if (!string.IsNullOrWhiteSpace(title))
                            {
                                if (GetWindowRect(hWnd, out RECT r))
                                {
                                    int w = r.Right - r.Left;
                                    int h = r.Bottom - r.Top;
                                    if (w > 300 && h > 200)
                                    {
                                        bestHwnd = hWnd;
                                        return false; // Primera ventana principal encontrada
                                    }
                                }
                            }
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (bestHwnd == IntPtr.Zero)
            {
                foreach (var p in procs)
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        bestHwnd = p.MainWindowHandle;
                        break;
                    }
                }
            }

            return bestHwnd;
        }

        public void EnfocarAppReproductora()
        {
            if (_currentSession == null) return;

            try
            {
                string appModelId = _currentSession.SourceAppUserModelId ?? "";
                string processName = "";
                bool isBrowser = false;

                if (appModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
                {
                    processName = "Spotify";
                }
                else if (appModelId.Contains("Netflix", StringComparison.OrdinalIgnoreCase))
                {
                    processName = "Netflix";
                }
                else if (appModelId.Contains("Opera", StringComparison.OrdinalIgnoreCase))
                {
                    processName = "opera";
                    isBrowser = true;
                }
                else if (appModelId.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
                {
                    processName = "chrome";
                    isBrowser = true;
                }
                else if (appModelId.Contains("MSEdge", StringComparison.OrdinalIgnoreCase) || appModelId.Contains("Edge", StringComparison.OrdinalIgnoreCase))
                {
                    processName = "msedge";
                    isBrowser = true;
                }
                else if (appModelId.Contains("Brave", StringComparison.OrdinalIgnoreCase))
                {
                    processName = "brave";
                    isBrowser = true;
                }

                if (string.IsNullOrEmpty(processName))
                {
                    int idx = appModelId.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    if (idx > 0)
                    {
                        string sub = appModelId.Substring(0, idx);
                        int lastSlash = Math.Max(sub.LastIndexOf('\\'), sub.LastIndexOf('/'));
                        if (lastSlash >= 0) processName = sub.Substring(lastSlash + 1);
                        else processName = sub;
                    }
                }

                if (string.IsNullOrEmpty(processName)) return;

                IntPtr mainHwnd = ObtenerVentanaPrincipal(processName);
                if (mainHwnd == IntPtr.Zero) return;

                if (isBrowser)
                {
                    ActivarPestañaONavegador(mainHwnd, _currentTitle, _currentArtist);
                }
                else
                {
                    TraerVentanaAlFrente(mainHwnd);
                }
            }
            catch
            {
                // Ignorar fallos de enfoque
            }
        }

        private void ActivarPestañaONavegador(IntPtr hWnd, string targetTitle, string targetArtist)
        {
            // Traer primero al frente la ventana principal respetando al 100% su estado maximizado
            TraerVentanaAlFrente(hWnd);

            // En segundo plano buscar la pestaña que coincide con la canción/vídeo y seleccionarla
            Task.Run(() =>
            {
                try
                {
                    var root = AutomationElement.FromHandle(hWnd);
                    if (root == null) return;

                    var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
                    var tabs = root.FindAll(TreeScope.Descendants, cond);
                    if (tabs == null || tabs.Count == 0) return;

                    string cleanTitle = LimpiarTextoComparacion(targetTitle);
                    string cleanArtist = LimpiarTextoComparacion(targetArtist);

                    AutomationElement? bestTab = null;
                    int bestScore = 0;

                    foreach (AutomationElement tab in tabs)
                    {
                        string tabName = tab.Current.Name ?? "";
                        string cleanTab = LimpiarTextoComparacion(tabName);

                        int score = 0;
                        if (!string.IsNullOrEmpty(cleanTitle) && cleanTab.Contains(cleanTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 50;
                        }
                        if (!string.IsNullOrEmpty(cleanArtist) && cleanTab.Contains(cleanArtist, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 30;
                        }
                        var words = cleanTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var w in words)
                        {
                            if (w.Length > 3 && cleanTab.Contains(w, StringComparison.OrdinalIgnoreCase))
                            {
                                score += 10;
                            }
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestTab = tab;
                        }
                    }

                    if (bestTab != null && bestScore >= 15)
                    {
                        if (bestTab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object selObj))
                        {
                            ((SelectionItemPattern)selObj).Select();
                        }
                        else if (bestTab.TryGetCurrentPattern(InvokePattern.Pattern, out object invObj))
                        {
                            ((InvokePattern)invObj).Invoke();
                        }
                    }
                }
                catch { }
            });
        }

        private static string LimpiarTextoComparacion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return text.Replace("- YouTube", "", StringComparison.OrdinalIgnoreCase)
                       .Replace(" - Twitch", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("| Netflix", "", StringComparison.OrdinalIgnoreCase)
                       .Trim();
        }

        private void Widget_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            float delta = e.Delta > 0 ? 0.05f : -0.05f;
            int vol = VolumeController.AjustarVolumen(delta);
            MostrarVolumenToast(vol);
            e.Handled = true;
        }

        public void MostrarVolumenToast(int vol)
        {
            if (vol < 0) return;

            _flyoutWindow?.ActualizarVolumen(vol);

            _volumeToastTimer?.Stop();
            TxtArtist.Text = I18n.VolumeToast(vol);
            TxtArtist.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#1ED760")!;

            _volumeToastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            _volumeToastTimer.Tick += (s, e) =>
            {
                _volumeToastTimer.Stop();
                TxtArtist.Text = _currentArtist;
                TxtArtist.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#A8A8A8")!;
            };
            _volumeToastTimer.Start();
        }

        private void TrackInfo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EnfocarAppReproductora();
        }

        private void ActualizarMarquee()
        {
            Dispatcher.InvokeAsync(() =>
            {
                AplicarMarquee(TxtTitle, TitleContainer, TitleTransform);
                AplicarMarquee(TxtArtist, ArtistContainer, ArtistTransform);
            }, DispatcherPriority.Loaded);
        }

        private static void AplicarMarquee(TextBlock tb, FrameworkElement container, TranslateTransform transform)
        {
            if (string.IsNullOrWhiteSpace(tb.Text) || 
                tb.Text == I18n.NoMusic || 
                tb.Text == I18n.PlayerInactive || 
                tb.Text == I18n.Waiting ||
                tb.Text == "Sin música" || 
                tb.Text == "No music playing" ||
                tb.Text == "Esperando..." ||
                tb.Text == "Waiting...")
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = 0;
                tb.Tag = null;
                return;
            }

            double containerWidth = container.ActualWidth > 0 ? container.ActualWidth : 116;

            // Medir el ancho real del texto usando el mayor entre DesiredSize y FormattedText
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = Math.Max(tb.DesiredSize.Width, MedirAnchoTexto(tb));

            if (textWidth > containerWidth + 4)
            {
                string cacheKey = tb.Text;
                if (tb.Tag as string == cacheKey && transform.HasAnimatedProperties)
                {
                    // La animación ya está activa para este texto; no reiniciar para no interrumpir el desplazamiento
                    return;
                }

                tb.Tag = cacheKey;
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = 0;

                // Margen generoso de 40px para garantizar que se lean hasta los últimos caracteres y paréntesis sin cortar
                double scrollDistance = -(textWidth - containerWidth + 40);
                double speed = 28.0; // Píxeles por segundo para lectura suave y fluida
                double scrollTimeSec = Math.Max(2.0, Math.Abs(scrollDistance) / speed);
                double pauseStart = 0.8; // Pausa inicial reactiva para que el usuario aprecie el movimiento casi de inmediato
                double pauseEnd = 1.5;   // Pausa en el extremo para leer el final del título
                double pauseReturn = 0.6; // Pausa breve de regreso

                TimeSpan t0 = TimeSpan.Zero;
                TimeSpan t1 = TimeSpan.FromSeconds(pauseStart);
                TimeSpan t2 = t1 + TimeSpan.FromSeconds(scrollTimeSec);
                TimeSpan t3 = t2 + TimeSpan.FromSeconds(pauseEnd);
                TimeSpan t4 = t3 + TimeSpan.FromSeconds(scrollTimeSec);
                TimeSpan t5 = t4 + TimeSpan.FromSeconds(pauseReturn);

                var anim = new DoubleAnimationUsingKeyFrames
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };

                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(t0)));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(t1)));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(scrollDistance, KeyTime.FromTimeSpan(t2)));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(scrollDistance, KeyTime.FromTimeSpan(t3)));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(t4)));
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(t5)));

                transform.BeginAnimation(TranslateTransform.XProperty, anim);
            }
            else
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = 0;
                tb.Tag = null;
            }
        }

        public static double MedirAnchoTexto(TextBlock tb)
        {
            if (string.IsNullOrEmpty(tb.Text)) return 0;
            try
            {
                var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
                double pixelsPerDip = 1.0;
                try
                {
                    pixelsPerDip = VisualTreeHelper.GetDpi(tb).PixelsPerDip;
                }
                catch
                {
                    pixelsPerDip = 1.0;
                }

                var ft = new FormattedText(
                    tb.Text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    tb.FontSize,
                    tb.Foreground ?? Brushes.White,
                    pixelsPerDip);
                return ft.WidthIncludingTrailingWhitespace;
            }
            catch
            {
                return tb.DesiredSize.Width;
            }
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e) => EjecutarPlayPausa();
        private void BtnPrev_Click(object sender, RoutedEventArgs e) => EjecutarAnterior();
        private void BtnNext_Click(object sender, RoutedEventArgs e) => EjecutarSiguiente();
        #endregion

        #region Gestión del Flyout al pasar el ratón
        private void Widget_MouseEnter(object sender, MouseEventArgs e)
        {
            _closeFlyoutTimer.Stop();

            if (_flyoutWindow != null)
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                var source = PresentationSource.FromVisual(this);
                double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                double flyoutLeft = this.Left + (this.Width - _flyoutWindow.Width) / 2;
                double flyoutTop = this.Top - _flyoutWindow.Height - 6;

                // Restringir la tarjeta flotante dentro del monitor actual donde reside el widget
                IntPtr hMon = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
                if (hMon != IntPtr.Zero)
                {
                    var mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(hMon, ref mi))
                    {
                        double workLeft = mi.rcWork.Left / dpiScale;
                        double workRight = mi.rcWork.Right / dpiScale;
                        double workBottom = mi.rcWork.Bottom / dpiScale;

                        flyoutTop = workBottom - _flyoutWindow.Height - 2;

                        if (flyoutLeft + _flyoutWindow.Width > workRight - 10)
                        {
                            flyoutLeft = workRight - _flyoutWindow.Width - 10;
                        }
                        if (flyoutLeft < workLeft + 10)
                        {
                            flyoutLeft = workLeft + 10;
                        }
                    }
                }
                else
                {
                    double screenWidth = SystemParameters.PrimaryScreenWidth;
                    if (flyoutLeft + _flyoutWindow.Width > screenWidth - 10)
                    {
                        flyoutLeft = screenWidth - _flyoutWindow.Width - 10;
                    }
                    if (flyoutLeft < 10) flyoutLeft = 10;
                }

                _flyoutWindow.ShowFlyout(flyoutLeft, flyoutTop);
            }
        }

        private void Widget_MouseLeave(object sender, MouseEventArgs e)
        {
            _closeFlyoutTimer.Start();
        }

        public void NotificarMouseEnFlyout(bool estaDentro)
        {
            if (estaDentro)
            {
                _closeFlyoutTimer.Stop();
            }
            else
            {
                _closeFlyoutTimer.Start();
            }
        }

        private void CloseFlyoutTimer_Tick(object? sender, EventArgs e)
        {
            bool ratonEnWidget = RootBorder.IsMouseOver;
            bool ratonEnFlyout = _flyoutWindow != null && _flyoutWindow.IsMouseOver;

            if (!ratonEnWidget && !ratonEnFlyout)
            {
                _flyoutWindow?.HideFlyout();
                _closeFlyoutTimer.Stop();
            }
        }
        #endregion

        #region Menú Contextual (Clic Derecho)
        private void MenuReconnect_Click(object sender, RoutedEventArgs e)
        {
            ConectarSesion();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            _watchdogTimer.Stop();
            _closeFlyoutTimer.Stop();
            _flyoutWindow?.Close();
            this.Close();
            Application.Current.Shutdown();
        }
        #endregion
    }
}