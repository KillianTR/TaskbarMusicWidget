using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Windows.Media;

namespace TaskbarMusicWidget
{
    public partial class FlyoutWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private bool _isDraggingSlider = false;

        private const string PlayPathData = "M 3.5,2 L 12,7 L 3.5,12 Z";
        private const string PausePathData = "M 3,2 L 5.5,2 L 5.5,12 L 3,12 Z M 8.5,2 L 11,2 L 11,12 L 8.5,12 Z";

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        public FlyoutWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Opacity = 0;
            Visibility = Visibility.Collapsed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        public void ShowFlyout(double left, double top)
        {
            this.Left = left;
            this.Top = top;
            this.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
            this.BeginAnimation(OpacityProperty, fadeIn);
        }

        public void HideFlyout()
        {
            if (this.Visibility != Visibility.Visible) return;

            var fadeOut = new DoubleAnimation(this.Opacity, 0, TimeSpan.FromMilliseconds(180));
            fadeOut.Completed += (s, e) =>
            {
                if (this.Opacity == 0)
                {
                    this.Visibility = Visibility.Collapsed;
                }
            };
            this.BeginAnimation(OpacityProperty, fadeOut);
        }

        public void UpdateTrackInfo(ImageSource? cover, string title, string artist, bool isPlaying)
        {
            FlyoutCover.Source = cover;
            FlyoutTitle.Text = string.IsNullOrWhiteSpace(title) ? "Sin música" : title;
            FlyoutArtist.Text = string.IsNullOrWhiteSpace(artist) ? "Reproductor inactivo" : artist;

            // Actualizar icono de Play/Pausa en el botón circular blanco
            if (FlyoutPlayPausePath != null)
            {
                FlyoutPlayPausePath.Data = Geometry.Parse(isPlaying ? PausePathData : PlayPathData);
                FlyoutPlayPausePath.Margin = isPlaying ? new Thickness(0) : new Thickness(1.5, 0, 0, 0);
            }
        }

        public void UpdateTimeline(TimeSpan position, TimeSpan duration)
        {
            if (_isDraggingSlider) return;

            TxtPosition.Text = FormatTime(position);
            TxtDuration.Text = FormatTime(duration);

            if (duration.TotalSeconds > 0)
            {
                TimelineSlider.IsEnabled = true;
                TimelineSlider.Maximum = duration.TotalSeconds;
                TimelineSlider.Value = Math.Clamp(position.TotalSeconds, 0, duration.TotalSeconds);
            }
            else
            {
                TimelineSlider.IsEnabled = false;
                TimelineSlider.Value = 0;
            }
        }

        private static string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
            {
                return time.ToString(@"h\:mm\:ss");
            }
            return time.ToString(@"m\:ss");
        }

        private void TimelineSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void TimelineSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;
            double seconds = TimelineSlider.Value;
            _mainWindow.SolicitarCambioPosicion(TimeSpan.FromSeconds(seconds));
        }

        public void UpdateShuffleState(bool isSpotify, bool isShuffleActive, bool isSmartShuffle = false)
        {
            if (!isSpotify)
            {
                BtnFlyoutShuffle.Visibility = Visibility.Collapsed;
                return;
            }

            BtnFlyoutShuffle.Visibility = Visibility.Visible;
            var spotifyGreen = (SolidColorBrush)new BrushConverter().ConvertFrom("#1ED760")!;
            var inactiveGray = (SolidColorBrush)new BrushConverter().ConvertFrom("#A0A0A0")!;

            if (isSmartShuffle)
            {
                ShuffleIconPath.Fill = spotifyGreen;
                ShuffleDot.Visibility = Visibility.Visible;
                SmartShuffleSparkle.Visibility = Visibility.Visible;
                BtnFlyoutShuffle.ToolTip = "Aleatorio inteligente (Smart Shuffle)";
            }
            else if (isShuffleActive)
            {
                ShuffleIconPath.Fill = spotifyGreen;
                ShuffleDot.Visibility = Visibility.Visible;
                SmartShuffleSparkle.Visibility = Visibility.Collapsed;
                BtnFlyoutShuffle.ToolTip = "Aleatorio activado";
            }
            else
            {
                ShuffleIconPath.Fill = inactiveGray;
                ShuffleDot.Visibility = Visibility.Collapsed;
                SmartShuffleSparkle.Visibility = Visibility.Collapsed;
                BtnFlyoutShuffle.ToolTip = "Activar aleatorio";
            }
        }

        private void BtnShuffle_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.AlternarShuffle();
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.EjecutarAnterior();
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.EjecutarPlayPausa();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.EjecutarSiguiente();
        }

        public void UpdateRepeatState(bool isSpotify, MediaPlaybackAutoRepeatMode mode)
        {
            if (!isSpotify)
            {
                BtnFlyoutRepeat.Visibility = Visibility.Collapsed;
                return;
            }

            BtnFlyoutRepeat.Visibility = Visibility.Visible;
            var spotifyGreen = (SolidColorBrush)new BrushConverter().ConvertFrom("#1ED760")!;
            var inactiveGray = (SolidColorBrush)new BrushConverter().ConvertFrom("#A0A0A0")!;

            switch (mode)
            {
                case MediaPlaybackAutoRepeatMode.None:
                    RepeatIconPath.Fill = inactiveGray;
                    RepeatDot.Visibility = Visibility.Collapsed;
                    RepeatOneBadge.Visibility = Visibility.Collapsed;
                    break;
                case MediaPlaybackAutoRepeatMode.List:
                    RepeatIconPath.Fill = spotifyGreen;
                    RepeatDot.Visibility = Visibility.Visible;
                    RepeatOneBadge.Visibility = Visibility.Collapsed;
                    break;
                case MediaPlaybackAutoRepeatMode.Track:
                    RepeatIconPath.Fill = spotifyGreen;
                    RepeatDot.Visibility = Visibility.Visible;
                    RepeatOneBadge.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void BtnRepeat_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.AlternarRepeat();
        }

        private void TrackInfo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _mainWindow.EnfocarAppReproductora();
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            float delta = e.Delta > 0 ? 0.05f : -0.05f;
            int vol = VolumeController.AjustarVolumen(delta);
            _mainWindow.MostrarVolumenToast(vol);
            e.Handled = true;
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            _mainWindow.NotificarMouseEnFlyout(true);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            _mainWindow.NotificarMouseEnFlyout(false);
        }
    }
}
