using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
            InicializarTextosLocalizados();
        }

        private void InicializarTextosLocalizados()
        {
            FlyoutTitle.Text = I18n.NoMusic;
            FlyoutArtist.Text = I18n.PlayerInactive;
            BtnFlyoutPrev.ToolTip = I18n.PrevTooltip;
            BtnFlyoutPlayPause.ToolTip = I18n.PlayPauseTooltip;
            BtnFlyoutNext.ToolTip = I18n.NextTooltip;
            if (FlyoutHeaderGrid != null) FlyoutHeaderGrid.ToolTip = I18n.OpenPlayerTooltip;
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
            ActualizarMarqueeFlyout();

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
            FlyoutTitle.Text = string.IsNullOrWhiteSpace(title) ? I18n.NoMusic : title;
            FlyoutArtist.Text = string.IsNullOrWhiteSpace(artist) ? I18n.PlayerInactive : artist;

            // Actualizar icono de Play/Pausa en el botón circular blanco
            if (FlyoutPlayPausePath != null)
            {
                FlyoutPlayPausePath.Data = Geometry.Parse(isPlaying ? PausePathData : PlayPathData);
                FlyoutPlayPausePath.Margin = isPlaying ? new Thickness(0) : new Thickness(1.5, 0, 0, 0);
            }

            ActualizarMarqueeFlyout();
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
                BtnFlyoutShuffle.ToolTip = I18n.ShuffleTooltipSmart;
            }
            else if (isShuffleActive)
            {
                ShuffleIconPath.Fill = spotifyGreen;
                ShuffleDot.Visibility = Visibility.Visible;
                SmartShuffleSparkle.Visibility = Visibility.Collapsed;
                BtnFlyoutShuffle.ToolTip = I18n.ShuffleTooltipOn;
            }
            else
            {
                ShuffleIconPath.Fill = inactiveGray;
                ShuffleDot.Visibility = Visibility.Collapsed;
                SmartShuffleSparkle.Visibility = Visibility.Collapsed;
                BtnFlyoutShuffle.ToolTip = I18n.ShuffleTooltipOff;
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
                    BtnFlyoutRepeat.ToolTip = I18n.RepeatTooltipOff;
                    break;
                case MediaPlaybackAutoRepeatMode.List:
                    RepeatIconPath.Fill = spotifyGreen;
                    RepeatDot.Visibility = Visibility.Visible;
                    RepeatOneBadge.Visibility = Visibility.Collapsed;
                    BtnFlyoutRepeat.ToolTip = I18n.RepeatTooltipAll;
                    break;
                case MediaPlaybackAutoRepeatMode.Track:
                    RepeatIconPath.Fill = spotifyGreen;
                    RepeatDot.Visibility = Visibility.Visible;
                    RepeatOneBadge.Visibility = Visibility.Visible;
                    BtnFlyoutRepeat.ToolTip = I18n.RepeatTooltipOne;
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

        #region Animación de Marquee Suave para Título y Artista
        public void ActualizarMarqueeFlyout()
        {
            Dispatcher.InvokeAsync(() =>
            {
                AplicarMarquee(FlyoutTitle, FlyoutTitleContainer, FlyoutTitleTransform);
                AplicarMarquee(FlyoutArtist, FlyoutArtistContainer, FlyoutArtistTransform);
            }, DispatcherPriority.Loaded);
        }

        private static void AplicarMarquee(TextBlock tb, FrameworkElement container, TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;

            if (string.IsNullOrWhiteSpace(tb.Text) || 
                tb.Text == I18n.NoMusic || 
                tb.Text == I18n.PlayerInactive || 
                tb.Text == "Sin música" || 
                tb.Text == "No music playing")
                return;

            double textWidth = MedirAnchoTexto(tb);
            double containerWidth = container.ActualWidth > 0 ? container.ActualWidth : 250;

            if (textWidth > containerWidth + 4)
            {
                // Margen de 16px para garantizar que se lean hasta los últimos caracteres y paréntesis
                double scrollDistance = -(textWidth - containerWidth + 16);
                double scrollTimeSec = Math.Max(2.5, Math.Abs(scrollDistance) / 22.0);
                double pauseStart = 2.0; // 2 segundos al inicio
                double pauseEnd = 2.0;   // 2 segundos en el final para leer cómodamente
                double pauseReturn = 1.0;

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
        }

        private static double MedirAnchoTexto(TextBlock tb)
        {
            if (string.IsNullOrEmpty(tb.Text)) return 0;
            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            var dpi = VisualTreeHelper.GetDpi(tb);
            var ft = new FormattedText(
                tb.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                tb.FontSize,
                tb.Foreground,
                dpi.PixelsPerDip);
            return ft.WidthIncludingTrailingWhitespace;
        }
        #endregion
    }
}
