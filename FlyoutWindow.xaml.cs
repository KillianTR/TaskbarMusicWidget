using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
        private bool _isUpdatingVolumeSliderInternally = false;
        private int _volumeBeforeMute = 50;

        private const string PlayPathData = "M 3.5,2 L 12,7 L 3.5,12 Z";
        private const string PausePathData = "M 3,2 L 5.5,2 L 5.5,12 L 3,12 Z M 8.5,2 L 11,2 L 11,12 L 8.5,12 Z";

        private const string SpeakerPathData = "M 3,5 L 0,5 L 0,11 L 3,11 L 7,15 L 7,1 L 3,5 Z M 9.5,4 C 10.8,5.1 11.5,6.5 11.5,8 C 11.5,9.5 10.8,10.9 9.5,12 L 8.5,10.8 C 9.4,9.9 10,8.8 10,8 C 10,7.2 9.4,6.1 8.5,5.2 L 9.5,4 Z M 11.5,1.5 C 13.5,3.2 14.5,5.5 14.5,8 C 14.5,10.5 13.5,12.8 11.5,14.5 L 10.5,13.2 C 12.2,11.8 13,9.9 13,8 C 13,6.1 12.2,4.2 10.5,2.8 L 11.5,1.5 Z";
        private const string MutePathData = "M 3,5 L 0,5 L 0,11 L 3,11 L 7,15 L 7,1 L 3,5 Z M 10.2,5.1 L 12,6.9 L 13.8,5.1 L 14.9,6.2 L 13.1,8 L 14.9,9.8 L 13.8,10.9 L 12,9.1 L 10.2,10.9 L 9.1,9.8 L 10.9,8 L 9.1,6.2 Z";

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        public FlyoutWindow(MainWindow mainWindow)
        {
            _isUpdatingVolumeSliderInternally = true;
            InitializeComponent();
            _mainWindow = mainWindow;
            Opacity = 0;
            Visibility = Visibility.Collapsed;
            InicializarTextosLocalizados();
            _isUpdatingVolumeSliderInternally = false;
            ActualizarEstadoVolumen();
        }

        private void InicializarTextosLocalizados()
        {
            FlyoutTitle.Text = I18n.NoMusic;
            FlyoutArtist.Text = I18n.PlayerInactive;
            BtnFlyoutPrev.ToolTip = I18n.PrevTooltip;
            BtnFlyoutPlayPause.ToolTip = I18n.PlayPauseTooltip;
            BtnFlyoutNext.ToolTip = I18n.NextTooltip;
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
            bool wasHidden = this.Visibility != Visibility.Visible;
            this.Visibility = Visibility.Visible;
            ActualizarEstadoVolumen();
            ActualizarMarqueeFlyout();

            if (wasHidden || this.Opacity < 0.95)
            {
                var fadeIn = new DoubleAnimation(this.Opacity, 1.0, TimeSpan.FromMilliseconds(180));
                this.BeginAnimation(OpacityProperty, fadeIn);
            }
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

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            _mainWindow.NotificarMouseEnFlyout(true);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            _mainWindow.NotificarMouseEnFlyout(false);
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            int delta = e.Delta > 0 ? 1 : -1;
            int nuevoVol = VolumeController.AjustarVolumen(delta * 0.05f);
            if (nuevoVol >= 0)
            {
                ActualizarVolumen(nuevoVol);
            }
            e.Handled = true;
        }

        #region Actualización de Datos y Controles Multimedia
        public void UpdateTrackInfo(ImageSource? cover, string title, string artist, bool isPlaying)
        {
            FlyoutCover.Source = cover;
            FlyoutTitle.Text = string.IsNullOrWhiteSpace(title) ? I18n.NoMusic : title;
            FlyoutArtist.Text = string.IsNullOrWhiteSpace(artist) ? I18n.PlayerInactive : artist;
            FlyoutTitle.ToolTip = FlyoutTitle.Text;
            FlyoutArtist.ToolTip = FlyoutArtist.Text;

            // Actualizar icono de Play/Pausa en el botón circular blanco
            if (FlyoutPlayPausePath != null)
            {
                FlyoutPlayPausePath.Data = Geometry.Parse(isPlaying ? PausePathData : PlayPathData);
                FlyoutPlayPausePath.Margin = isPlaying ? new Thickness(0) : new Thickness(1.5, 0, 0, 0);
                BtnFlyoutPlayPause.ToolTip = isPlaying ? (I18n.IsSpanish ? "Pausar" : "Pause") : (I18n.IsSpanish ? "Reproducir" : "Play");
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
                TimelineSlider.Maximum = duration.TotalSeconds;
                TimelineSlider.Value = position.TotalSeconds;
                TimelineSlider.IsEnabled = true;
            }
            else
            {
                TimelineSlider.Value = 0;
                TimelineSlider.Maximum = 100;
                TimelineSlider.IsEnabled = false;
            }
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

        private static string FormatTime(TimeSpan t)
        {
            if (t.TotalHours >= 1)
            {
                return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
            }
            return $"{t.Minutes}:{t.Seconds:D2}";
        }
        #endregion

        #region Control de Progreso con el Ratón (Seek)
        private void TimelineSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
            ActualizarPosicionSliderConRaton(e);
        }

        private void TimelineSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;
            double seconds = TimelineSlider.Value;
            _mainWindow.SolicitarCambioPosicion(TimeSpan.FromSeconds(seconds));
        }

        private void ActualizarPosicionSliderConRaton(MouseButtonEventArgs e)
        {
            if (TimelineSlider.ActualWidth <= 0) return;
            Point p = e.GetPosition(TimelineSlider);
            double ratio = Math.Clamp(p.X / TimelineSlider.ActualWidth, 0.0, 1.0);
            TimelineSlider.Value = TimelineSlider.Minimum + ratio * (TimelineSlider.Maximum - TimelineSlider.Minimum);
            TxtPosition.Text = FormatTime(TimeSpan.FromSeconds(TimelineSlider.Value));
        }
        #endregion

        #region Control de Volumen Interactivo
        private void FlyoutVolumeSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ActualizarPosicionVolumenConRaton(e);
        }

        private void ActualizarPosicionVolumenConRaton(MouseButtonEventArgs e)
        {
            if (FlyoutVolumeSlider.ActualWidth <= 0) return;
            Point p = e.GetPosition(FlyoutVolumeSlider);
            double ratio = Math.Clamp(p.X / FlyoutVolumeSlider.ActualWidth, 0.0, 1.0);
            int vol = (int)Math.Round(ratio * 100);
            FlyoutVolumeSlider.Value = vol;
        }

        private void FlyoutVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingVolumeSliderInternally || FlyoutVolumeSlider == null || TxtVolumePercent == null || MuteIconPath == null) return;

            int vol = (int)Math.Round(FlyoutVolumeSlider.Value);
            
            // Si estaba silenciado y el usuario mueve el slider a un volumen > 0, reactivar automáticamente
            if (vol > 0 && VolumeController.EstaSilenciado())
            {
                VolumeController.AlternarSilencio();
            }

            VolumeController.EstablecerVolumen((float)(vol / 100.0));
            TxtVolumePercent.Text = $"{vol}%";
            if (vol > 0)
            {
                _volumeBeforeMute = vol;
            }

            bool isMuted = vol == 0 || VolumeController.EstaSilenciado();
            MuteIconPath.Data = Geometry.Parse(isMuted ? MutePathData : SpeakerPathData);
            MuteIconPath.Fill = isMuted ? (SolidColorBrush)new BrushConverter().ConvertFrom("#E06060")! : (SolidColorBrush)new BrushConverter().ConvertFrom("#8E8E8E")!;
            BtnFlyoutMute.ToolTip = isMuted ? (I18n.IsSpanish ? "Reactivar sonido" : "Unmute") : (I18n.IsSpanish ? "Silenciar" : "Mute");
        }

        private void BtnFlyoutMute_Click(object sender, RoutedEventArgs e)
        {
            bool wasMuted = VolumeController.EstaSilenciado();
            int currentVol = VolumeController.ObtenerVolumenActual();

            if (!wasMuted)
            {
                // Silenciar (Mute): guarda el volumen previo y pone la barra en 0% visualmente
                // como en YouTube y Spotify. La música o vídeo sigue reproduciéndose en segundo plano sin pausarse
                if (currentVol > 0)
                {
                    _volumeBeforeMute = currentVol;
                }
                VolumeController.AlternarSilencio();
                ActualizarVolumen(0, isMutedParam: true);
            }
            else
            {
                // Reactivar sonido (Unmute): restaura el nivel previo
                VolumeController.AlternarSilencio();
                int restoreVol = _volumeBeforeMute > 0 ? _volumeBeforeMute : (currentVol > 0 ? currentVol : 50);
                VolumeController.EstablecerVolumen((float)(restoreVol / 100.0));
                ActualizarVolumen(restoreVol, isMutedParam: false);
            }
        }

        public void ActualizarEstadoVolumen()
        {
            int vol = VolumeController.ObtenerVolumenActual();
            bool isMuted = VolumeController.EstaSilenciado();

            if (vol >= 0)
            {
                ActualizarVolumen(vol, isMuted);
            }
        }

        public void ActualizarVolumen(int vol, bool? isMutedParam = null)
        {
            if (vol < 0 || FlyoutVolumeSlider == null || TxtVolumePercent == null || MuteIconPath == null || BtnFlyoutMute == null) return;

            bool isMuted = isMutedParam ?? VolumeController.EstaSilenciado();

            _isUpdatingVolumeSliderInternally = true;
            if (isMuted || vol == 0)
            {
                FlyoutVolumeSlider.Value = 0;
                TxtVolumePercent.Text = "0%";
                MuteIconPath.Data = Geometry.Parse(MutePathData);
                MuteIconPath.Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#E06060")!;
                BtnFlyoutMute.ToolTip = I18n.IsSpanish ? "Reactivar sonido" : "Unmute";
            }
            else
            {
                FlyoutVolumeSlider.Value = vol;
                TxtVolumePercent.Text = $"{vol}%";
                _volumeBeforeMute = vol;
                MuteIconPath.Data = Geometry.Parse(SpeakerPathData);
                MuteIconPath.Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#8E8E8E")!;
                BtnFlyoutMute.ToolTip = I18n.IsSpanish ? "Silenciar" : "Mute";
            }
            _isUpdatingVolumeSliderInternally = false;
        }
        #endregion

        #region Acciones de Botones Multimedia
        private void BtnPlayPause_Click(object sender, RoutedEventArgs e) => _mainWindow.EjecutarPlayPausa();
        private void BtnPrev_Click(object sender, RoutedEventArgs e) => _mainWindow.EjecutarAnterior();
        private void BtnNext_Click(object sender, RoutedEventArgs e) => _mainWindow.EjecutarSiguiente();
        private void BtnShuffle_Click(object sender, RoutedEventArgs e) => _mainWindow.AlternarShuffle();
        private void BtnRepeat_Click(object sender, RoutedEventArgs e) => _mainWindow.AlternarRepeat();
        private void TrackInfo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _mainWindow.EnfocarAppReproductora();
        #endregion

        #region Animación de Marquee Suave para Título y Artista
        public void ActualizarMarqueeFlyout()
        {
            ConfigurarMarquee(FlyoutTitleContainer, FlyoutTitle, FlyoutTitleTransform);
            ConfigurarMarquee(FlyoutArtistContainer, FlyoutArtist, FlyoutArtistTransform);
        }

        private void ConfigurarMarquee(FrameworkElement container, TextBlock tb, TranslateTransform transform)
        {
            if (string.IsNullOrEmpty(tb.Text) || tb.Text == I18n.NoMusic || tb.Text == I18n.PlayerInactive)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = 0;
                tb.Tag = null;
                return;
            }

            double containerWidth = container.ActualWidth > 0 ? container.ActualWidth : 255;

            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = Math.Max(tb.DesiredSize.Width, MedirAnchoTexto(tb));

            if (textWidth > containerWidth + 4)
            {
                string cacheKey = tb.Text;
                if (tb.Tag as string == cacheKey && transform.HasAnimatedProperties)
                {
                    return;
                }

                tb.Tag = cacheKey;
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = 0;

                double scrollDistance = -(textWidth - containerWidth + 40);
                double speed = 28.0;
                double scrollTimeSec = Math.Max(2.0, Math.Abs(scrollDistance) / speed);
                double pauseStart = 0.8;
                double pauseEnd = 1.5;
                double pauseReturn = 0.6;

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

        private static double MedirAnchoTexto(TextBlock tb)
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
        #endregion
    }
}
