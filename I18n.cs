using System;
using System.Globalization;

namespace TaskbarMusicWidget
{
    public static class I18n
    {
        public static bool IsSpanish { get; private set; }

        static I18n()
        {
            string uiLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            string sysLang = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            IsSpanish = uiLang == "es" || sysLang == "es";
        }

        public static string NoMusic => IsSpanish ? "Sin música" : "No music playing";
        public static string PlayerInactive => IsSpanish ? "Reproductor inactivo" : "Player inactive";
        public static string Waiting => IsSpanish ? "Esperando..." : "Waiting...";
        public static string OpenPlayerTooltip => IsSpanish ? "Abrir reproductor" : "Open player";
        public static string PrevTooltip => IsSpanish ? "Anterior" : "Previous";
        public static string PlayPauseTooltip => IsSpanish ? "Reproducir / Pausar" : "Play / Pause";
        public static string NextTooltip => IsSpanish ? "Siguiente" : "Next";
        public static string ShuffleTooltipOff => IsSpanish ? "Activar orden aleatorio" : "Turn on shuffle";
        public static string ShuffleTooltipOn => IsSpanish ? "Orden aleatorio activado" : "Shuffle on";
        public static string ShuffleTooltipSmart => IsSpanish ? "Orden aleatorio inteligente (Smart Shuffle)" : "Smart Shuffle on";
        public static string RepeatTooltipOff => IsSpanish ? "Activar repetición" : "Turn on repeat";
        public static string RepeatTooltipAll => IsSpanish ? "Repetir todo activado" : "Repeat all";
        public static string RepeatTooltipOne => IsSpanish ? "Repetir esta canción" : "Repeat one";
        public static string VolumeToast(int vol) => IsSpanish ? $"🔊 Volumen: {vol}%" : $"🔊 Volume: {vol}%";
        public static string MenuReconnect => IsSpanish ? "Reconectar reproductor" : "Reconnect player";
        public static string MenuExit => IsSpanish ? "Cerrar widget" : "Close widget";
        public static string StartupError => IsSpanish ? "Error al iniciar" : "Startup error";
        public static string CheckPermissions => IsSpanish ? "Verifica permisos" : "Check permissions";
        public static string PlayingFallback => IsSpanish ? "Reproduciendo" : "Playing";
    }
}
