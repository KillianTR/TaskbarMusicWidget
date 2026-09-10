using System;
using System.IO;
using System.Text.Json;

namespace TaskbarMusicWidget
{
    public class AppSettings
    {
        public bool ShowVolumeBar { get; set; } = false;
        public string TargetMonitor { get; set; } = "Auto"; // "Auto", "Primary", "Secondary"
    }

    public static class SettingsManager
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarMusicWidget");

        private static readonly string SettingsFilePath = Path.Combine(SettingsDir, "settings.json");

        private static AppSettings _current = new();
        public static AppSettings Current => _current;

        public static event Action? SettingsChanged;

        static SettingsManager()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        _current = loaded;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                {
                    Directory.CreateDirectory(SettingsDir);
                }

                string json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }

            SettingsChanged?.Invoke();
        }

        public static void SetShowVolumeBar(bool show)
        {
            if (_current.ShowVolumeBar != show)
            {
                _current.ShowVolumeBar = show;
                Save();
            }
        }

        public static void SetTargetMonitor(string monitor)
        {
            if (_current.TargetMonitor != monitor)
            {
                _current.TargetMonitor = monitor;
                Save();
            }
        }
    }
}
