using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;

namespace PortManager
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string Application { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public bool IsCritical { get; set; }
        
        public string DisplayTime => Timestamp.ToString("HH:mm:ss");
        public string DisplayDate => Timestamp.ToString("dd/MM");
    }

    public static class LogManager
    {
        private static List<LogEntry> _logs = new List<LogEntry>();
        private static HashSet<string> _notifiedPorts = new HashSet<string>();
        private static string LogFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pm_logs.csv");

        public static List<LogEntry> Logs => _logs;
        public static bool HasCriticalUnseen { get; set; } = false;

        static LogManager()
        {
            LoadLogs();
        }

        private static void LoadLogs()
        {
            try
            {
                if (File.Exists(LogFilePath))
                {
                    var lines = File.ReadAllLines(LogFilePath);
                    foreach (var line in lines.Skip(1))
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 7)
                        {
                            _logs.Add(new LogEntry
                            {
                                Timestamp = DateTime.TryParse(parts[0], out var dt) ? dt : DateTime.Now,
                                EventType = parts[1],
                                Category = parts[2],
                                Port = int.TryParse(parts[3], out var p) ? p : 0,
                                Protocol = parts[4],
                                Application = parts[5],
                                Details = parts[6].Replace(";", ","),
                                IsCritical = parts.Length > 7 && bool.TryParse(parts[7], out var crit) && crit
                            });
                        }
                    }
                    _logs = _logs.OrderByDescending(l => l.Timestamp).Take(500).ToList();
                }
            }
            catch { }
        }

        private static void SaveToFile(LogEntry entry)
        {
            try
            {
                bool needsHeader = !File.Exists(LogFilePath);
                using (var writer = new StreamWriter(LogFilePath, true))
                {
                    if (needsHeader)
                    {
                        writer.WriteLine("Timestamp,EventType,Category,Port,Protocol,Application,Details,IsCritical");
                    }
                    writer.WriteLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss},{entry.EventType},{entry.Category},{entry.Port},{entry.Protocol},{entry.Application},{entry.Details.Replace(",", ";")},{entry.IsCritical}");
                }
            }
            catch { }
        }

        public static void Log(string eventType, string category, string details, string application = "", int port = 0, string protocol = "", bool isCritical = false)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                EventType = eventType,
                Category = category,
                Details = details,
                Application = application,
                Port = port,
                Protocol = protocol,
                IsCritical = isCritical
            };
            
            _logs.Insert(0, entry);
            if (_logs.Count > 500) _logs.RemoveAt(_logs.Count - 1);

            if (isCritical) HasCriticalUnseen = true;

            SaveToFile(entry);
        }

        public static void LogPortOpened(int port, string protocol, string application) => Log("PORT_OPENED", "NETWORK", $"Port {port}/{protocol} opened", application, port, protocol);
        public static void LogPortClosed(int port, string protocol, string application) => Log("PORT_CLOSED", "NETWORK", $"Port {port}/{protocol} closed", application, port, protocol);

        public static bool LogNewPort(int port, string protocol, string application, bool playSound = false)
        {
            string key = $"{port}:{protocol}:{application}";
            if (_notifiedPorts.Contains(key)) return false;
            
            _notifiedPorts.Add(key);
            Log("NEW_PORT", "NETWORK", $"New active port: {port}/{protocol}", application, port, protocol, true);
            
            if (playSound) SystemSounds.Asterisk.Play();
            return true;
        }

        public static void LogSuspicious(int port, string application, string reason, bool playSound = false)
        {
            string key = $"SUSP:{port}:{application}";
            if (_notifiedPorts.Contains(key)) return;
            
            _notifiedPorts.Add(key);
            Log("SUSPICIOUS", "SECURITY", $"{reason} - Port {port}", application, port, "", true);
            
            if (playSound) SystemSounds.Hand.Play(); // Critical error sound
        }

        public static void LogFirewall(string ruleName, string action) => Log("FIREWALL", "FIREWALL", $"Rule '{ruleName}' {action}");
        public static void LogUpnp(int port, string protocol, string action) => Log("UPNP", "SYSTEM", $"UPnP {action} {port}/{protocol}", "", port, protocol);
        public static void LogSystem(string message) => Log("SYSTEM", "SYSTEM", message);

        public static void ExportToCsv(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,EventType,Category,Port,Protocol,Application,Details,IsCritical");
            foreach (var entry in _logs)
            {
                sb.AppendLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss},{entry.EventType},{entry.Category},{entry.Port},{entry.Protocol},{entry.Application},{entry.Details.Replace(",", ";")},{entry.IsCritical}");
            }
            File.WriteAllText(filePath, sb.ToString());
        }

        public static void ClearLogs()
        {
            _logs.Clear();
            _notifiedPorts.Clear();
            HasCriticalUnseen = false;
            try { if (File.Exists(LogFilePath)) File.Delete(LogFilePath); } catch { }
        }
    }
}
