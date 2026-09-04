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
using System.Linq;
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

        #region Win32 API
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

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

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

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
            catch
            {
                // Ignorar fallo al instanciar ventana emergente
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
            if (MenuExitItem != null) MenuExitItem.Header = I18n.MenuExit;
        }

        private void WatchdogTimer_Tick(object? sender, EventArgs e)
        {
            // 1. Detectar si la barra de tareas está oculta o hay juego/vídeo en pantalla completa
            if (DebeOcultarse())
            {
                if (this.Visibility != Visibility.Collapsed)
                {
                    this.Visibility = Visibility.Collapsed;
                    _flyoutWindow?.HideFlyout();
                }
                return;
            }
            else
            {
                if (this.Visibility != Visibility.Visible)
                {
                    this.Visibility = Visibility.Visible;
                }
            }

            // Reasegurar que el widget permanece al frente si la barra es visible
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }

            // 2. Operaciones periódicas de temporizador (cada ~1 segundo)
            var now = DateTime.Now;
            if ((now - _lastTimelineTick).TotalMilliseconds >= 950)
            {
                _lastTimelineTick = now;

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

        private bool DebeOcultarse()
        {
            try
            {
                IntPtr myHandle = new WindowInteropHelper(this).Handle;
                if (myHandle == IntPtr.Zero) return false;

                // Obtener el monitor donde reside el widget
                IntPtr hMonitor = MonitorFromWindow(myHandle, MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfo(hMonitor, ref mi)) return false;

                // 1. Comprobar si la barra de tareas está oculta (auto-hide o no visible)
                IntPtr hTaskbar = FindWindow("Shell_TrayWnd", null);
                if (hTaskbar != IntPtr.Zero)
                {
                    if (!IsWindowVisible(hTaskbar))
                    {
                        return true;
                    }

                    if (GetWindowRect(hTaskbar, out RECT tbRect))
                    {
                        // Si la barra está configurada para ocultarse automáticamente y está escondida
                        int tbHeight = tbRect.Bottom - tbRect.Top;
                        if (tbRect.Top >= mi.rcMonitor.Bottom - 6 || tbHeight <= 6)
                        {
                            return true;
                        }
                    }
                }

                // 2. Comprobar si hay una aplicación en primer plano en pantalla completa (juegos, vídeos en YouTube/Netflix, etc.)
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero && fg != hTaskbar && fg != myHandle)
                {
                    IntPtr flyoutHandle = _flyoutWindow != null ? new WindowInteropHelper(_flyoutWindow).Handle : IntPtr.Zero;
                    if (fg == flyoutHandle) return false;

                    var sb = new StringBuilder(128);
                    GetClassName(fg, sb, sb.Capacity);
                    string cls = sb.ToString();

                    // Descartar escritorio y ventanas auxiliares de Windows
                    if (cls != "Progman" && cls != "WorkerW" && cls != "Shell_TrayWnd" && 
                        cls != "Windows.UI.Core.CoreWindow" && cls != "Xaml_WindowedPopupClass")
                    {
                        if (GetWindowRect(fg, out RECT fgRect))
                        {
                            // Si la ventana activa cubre o sobrepasa toda el área del monitor
                            if (fgRect.Left <= mi.rcMonitor.Left &&
                                fgRect.Top <= mi.rcMonitor.Top &&
                                fgRect.Right >= mi.rcMonitor.Right &&
                                fgRect.Bottom >= mi.rcMonitor.Bottom)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
                // En caso de excepción no interferir
            }

            return false;
        }

        private void PosicionarEnBarra()
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double workAreaBottom = SystemParameters.WorkArea.Bottom;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double taskbarHeight = screenHeight - workAreaBottom;

            // Mantenerse a la izquierda de la bandeja del sistema
            this.Left = screenWidth - this.Width - 260;
            this.Top = workAreaBottom + ((taskbarHeight - this.Height) / 2);
        }

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

                    TxtTitle.Text = _currentTitle;
                    TxtArtist.Text = _currentArtist;
                    TxtTitle.ToolTip = _currentTitle;
                    TxtArtist.ToolTip = _currentArtist;

                    if (props.Thumbnail != null)
                    {
                        using IRandomAccessStreamWithContentType stream = await props.Thumbnail.OpenReadAsync();
                        using Stream netStream = stream.AsStreamForRead();
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = netStream;
                        bmp.EndInit();
                        bmp.Freeze();
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
                double flyoutLeft = this.Left + (this.Width - _flyoutWindow.Width) / 2;

                // Espacio de ~12px entre la tarjeta flotante y la barra de tareas al estilo nativo de Windows 11
                // (Se descuenta el margen de 10px del borde interno de FlyoutWindow)
                double taskbarTop = SystemParameters.WorkArea.Bottom;
                double flyoutTop = taskbarTop - _flyoutWindow.Height - 2;

                // Evitar salirse de la pantalla horizontalmente
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                if (flyoutLeft + _flyoutWindow.Width > screenWidth - 10)
                {
                    flyoutLeft = screenWidth - _flyoutWindow.Width - 10;
                }
                if (flyoutLeft < 10) flyoutLeft = 10;

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