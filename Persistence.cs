using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PortManager
{
    public class ListenerProfile
    {
        public int Port { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public string FirewallRuleName { get; set; } = string.Empty;
        public bool UpnpEnabled { get; set; }
        public bool ForwardOnly { get; set; }
        public bool AutoStart { get; set; }
    }

    public class AlertSettings
    {
        public bool EnableNewPortAlerts { get; set; } = false;
        public bool EnableSuspiciousAlerts { get; set; } = false;
        public bool EnableSoundAlerts { get; set; } = false;
        public bool EnablePopupAlerts { get; set; } = false;
    }

    public class UserProfile
    {
        public string Name { get; set; } = "Default";
        public List<ListenerProfile> Listeners { get; set; } = new List<ListenerProfile>();
    }

    public class AppSettings
    {
        public string LastSubnet { get; set; } = "192.168.1.";
        public bool ScanFastMode { get; set; } = true;
        public bool ScanSingleScope { get; set; } = false;
        public List<ListenerProfile> SavedListeners { get; set; } = new List<ListenerProfile>();
        public AlertSettings Alerts { get; set; } = new AlertSettings();
        public List<UserProfile> Profiles { get; set; } = new List<UserProfile>();
        public string ActiveProfile { get; set; } = "Default";
        public bool MinimizeToTray { get; set; } = false;
        public List<string> KnownApplications { get; set; } = new List<string>();
    }

    public static class PersistenceManager
    {
        private static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static void Save(AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public static void ExportConfig(string filePath, AppSettings settings)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(filePath, json);
        }

        public static AppSettings ImportConfig(string filePath)
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
    }
}
