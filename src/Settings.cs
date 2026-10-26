using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClawAmbilight
{
    public enum LightMode { Ambient, Vibrant, Static }
    public enum BrightnessSource { Display, Manual }
    public enum BatteryPolicy { Normal, Eco, Off }

    /// <summary>All user settings. Stored as JSON so the voice assistant (gemma_agent.py) can change
    /// them too; the app watches the file and applies external edits live.</summary>
    public sealed class Settings
    {
        public static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClawAmbilight");
        public static readonly string FilePath = Path.Combine(Dir, "settings.json");

        public bool Enabled { get; set; } = true;
        public LightMode Mode { get; set; } = LightMode.Ambient;
        public BrightnessSource BrightnessSource { get; set; } = BrightnessSource.Display;
        public int Brightness { get; set; } = 100;          // manual level, or the cap when synced with the display
        public double Saturation { get; set; } = 1.8;       // ambient saturation boost
        public double Smoothness { get; set; } = 0.08;      // lerp factor per frame (lower = smoother)
        public int Fps { get; set; } = 30;
        public string StaticColor { get; set; } = "#3E7BFA";   // legacy single color, seeds both sides
        public string? StaticLeft { get; set; }                // left stick
        public string? StaticRight { get; set; }               // right stick + ABXY

        public const string JoyConBlue = "#0AB9E6", JoyConRed = "#FF3C28";  // Nintendo Switch neon Joy-Con
        public BatteryPolicy OnBattery { get; set; } = BatteryPolicy.Eco;
        public bool Autostart { get; set; } = true;

        static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return Clamp(JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings());
            }
            catch (Exception e) { Log.Write("settings load failed: " + e.Message); }
            return Clamp(new Settings());
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, true);
        }

        static Settings Clamp(Settings s)
        {
            s.Brightness = Math.Clamp(s.Brightness, 0, 100);
            s.Saturation = Math.Clamp(s.Saturation, 0.5, 3.0);
            s.Smoothness = Math.Clamp(s.Smoothness, 0.02, 1.0);
            s.Fps = Math.Clamp(s.Fps, 5, 60);
            s.StaticLeft ??= s.StaticColor;
            s.StaticRight ??= s.StaticColor;
            return s;
        }

        public Settings Clone() => (Settings)MemberwiseClone();
    }

    static class Log
    {
        static readonly object Gate = new();
        public static void Write(string msg)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Settings.Dir);
                    string path = Path.Combine(Settings.Dir, "ambilight.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
