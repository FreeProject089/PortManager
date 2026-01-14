using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace PortManager
{
    public class StartupItem
    {
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty; // Registry path or Folder path
        public string Status { get; set; } = "Enabled";
        public string ProcessPath { get; set; } = string.Empty;
    }

    public static class StartupManager
    {
        public static List<StartupItem> GetStartupItems()
        {
            var items = new List<StartupItem>();

            // 1. HKCU Run
            ReadRegistry(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", items);
            // 2. HKLM Run
            ReadRegistry(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", items);
            // 3. HKLM WOW6432 Run
            ReadRegistry(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", items, true);

            // 4. Startup Folder (User)
            ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), items);
            // 5. Startup Folder (Common)
            ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), items);

            return items;
        }

        private static void ReadRegistry(RegistryKey root, string keyPath, List<StartupItem> list, bool wow64 = false)
        {
            try
            {
                using (var key = root.OpenSubKey(keyPath, false))
                {
                    if (key == null) return;
                    foreach (var name in key.GetValueNames())
                    {
                        var cmd = key.GetValue(name)?.ToString() ?? "";
                        list.Add(new StartupItem
                        {
                            Name = name,
                            Command = cmd,
                            Location = $"Registry: {(wow64 ? "HKLM/WOW64" : root.Name)}\\{keyPath}",
                            ProcessPath = ExtractPath(cmd)
                        });
                    }
                }
            }
            catch { }
        }

        private static void ReadStartupFolder(string folderPath, List<StartupItem> list)
        {
            try
            {
                if (!Directory.Exists(folderPath)) return;
                foreach (var file in Directory.GetFiles(folderPath))
                {
                    list.Add(new StartupItem
                    {
                        Name = Path.GetFileName(file),
                        Command = file,
                        Location = "Startup Folder",
                        ProcessPath = file
                    });
                }
            }
            catch { }
        }

        private static string ExtractPath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return "";
            string path = command.Trim();
            if (path.StartsWith("\""))
            {
                int nextQuote = path.IndexOf("\"", 1);
                if (nextQuote > 0) return path.Substring(1, nextQuote - 1);
            }
            int spaceIndex = path.IndexOf(" ");
            if (spaceIndex > 0) return path.Substring(0, spaceIndex);
            return path;
        }

        public static void RemoveItem(StartupItem item)
        {
            if (item.Location.StartsWith("Registry:"))
            {
                bool isHklm = item.Location.Contains("HKEY_LOCAL_MACHINE");
                string path = item.Location.Split(new[] { '\\' }, 2)[1];
                var root = isHklm ? Registry.LocalMachine : Registry.CurrentUser;
                using (var key = root.OpenSubKey(path, true))
                {
                    key?.DeleteValue(item.Name, false);
                }
            }
            else if (item.Location == "Startup Folder")
            {
                if (File.Exists(item.ProcessPath))
                {
                    File.Delete(item.ProcessPath);
                }
            }
        }
    }
}
