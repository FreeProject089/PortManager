using System;
using System.Collections.Generic;
using System.Linq;

namespace PortManager
{
    public static class SuspiciousActivityAnalyzer
    {
        private static readonly HashSet<int> ThreatPorts = new HashSet<int>
        {
            4444, // Metasploit
            31337, // BackOrifice
            6667, // IRC (Botnets)
            12345, // NetBus
            27374, // Sub7
            5554, // Sasser
        };

        private static readonly HashSet<string> SensitiveProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "svchost.exe",
            "csrss.exe",
            "lsass.exe",
            "winlogon.exe",
            "services.exe",
            "explorer.exe",
        };

        public static void Analyze(PortInfo port)
        {
            port.IsSuspicious = false;
            port.SuspiciousReason = "";

            // Check 1: Known Threat Ports (Remote)
            // Only relevant if we are connected to it, or listening on it? 
            // Often malware LISTENs on these ports, or connects to C2 on these ports.
            // If LocalPort is one of them, it's suspicious.
            if (ThreatPorts.Contains(port.LocalPort))
            {
                port.IsSuspicious = true;
                port.SuspiciousReason = $"Port {port.LocalPort} is a known malware port.";
                return;
            }
            if (port.Protocol == Protocol.TCP && ThreatPorts.Contains(port.RemotePort))
            {
                port.IsSuspicious = true;
                port.SuspiciousReason = $"Connected to remote port {port.RemotePort} (Known threat).";
                return;
            }

            // Check 2: Process Location
            if (!string.IsNullOrEmpty(port.ProcessPath))
            {
                string path = port.ProcessPath.ToLower();

                // Temp folders are common for malware droppers
                if (path.Contains(@"\appdata\local\temp\") || path.Contains(@"\windows\temp\"))
                {
                    port.IsSuspicious = true;
                    port.SuspiciousReason = "Process running from Temp directory.";
                    return;
                }

                // Downloads folder implies user just ran something, but for a permanent listener it's weird
                if (path.Contains(@"\downloads\"))
                {
                    port.IsSuspicious = true;
                    port.SuspiciousReason = "Process running from Downloads folder.";
                    return;
                }

                // Check 3: Masquerading System Processes
                string fileName = System.IO.Path.GetFileName(port.ProcessPath).ToLower();
                if (SensitiveProcessNames.Contains(fileName))
                {
                    bool inSystem32 = path.Contains(@"\windows\system32") || path.Contains(@"\windows\syswow64");
                    
                    // Allow explorer.exe in Windows folder
                    if (fileName == "explorer.exe" && path.Contains(@"\windows\explorer.exe")) inSystem32 = true;

                    if (!inSystem32)
                    {
                        port.IsSuspicious = true;
                        port.SuspiciousReason = $"System process '{fileName}' running from non-standard location.";
                        return;
                    }
                }
            }

            // Check 4: PowerShell Network Activity
            // PowerShell connecting to remote hosts is often C2 or Downloader
            // But also used by sysadmins. We will flag it as 'Warning' context.
            if (!string.IsNullOrEmpty(port.ProcessName) && port.ProcessName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                 if (port.Protocol == Protocol.TCP && port.State != "Listen" && !IsLocalIp(port.RemoteAddress))
                 {
                     port.IsSuspicious = true;
                     port.SuspiciousReason = "PowerShell making external network connections.";
                     return;
                 }
            }
        }

        private static bool IsLocalIp(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            return ip.StartsWith("127.") || ip.StartsWith("192.168.") || ip.StartsWith("10.");
        }
    }
}
