using System;
using System.Diagnostics;
using System.Security.Principal;

namespace PortManager
{
    public static class FirewallManager
    {
        public static bool IsAdmin()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static void AddRule(string name, int port, string protocol)
        {
            // netsh advfirewall firewall add rule name="PortManager_8080_TCP" dir=in action=allow protocol=TCP localport=8080
            string cmd = $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol={protocol} localport={port}";
            RunNetsh(cmd);
        }

        public static void RemoveRule(string name)
        {
            // netsh advfirewall firewall delete rule name="PortManager_8080_TCP"
            string cmd = $"advfirewall firewall delete rule name=\"{name}\"";
            RunNetsh(cmd);
        }

        private static void RunNetsh(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    string output = p.StandardOutput.ReadToEnd();
                    throw new Exception($"Netsh failed (Exit: {p.ExitCode}): {output}");
                }
            }
        }
    }
}
