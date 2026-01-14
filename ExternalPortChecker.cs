using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PortManager
{
    public static class ExternalPortChecker
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>
        /// Get public IP address from external service
        /// </summary>
        public static async Task<string> GetPublicIpAsync()
        {
            try
            {
                var response = await _http.GetStringAsync("https://api.ipify.org");
                return response.Trim();
            }
            catch
            {
                try
                {
                    var response = await _http.GetStringAsync("https://icanhazip.com");
                    return response.Trim();
                }
                catch
                {
                    return "Unable to detect";
                }
            }
        }

        /// <summary>
        /// Check if a port is open from the internet (uses external service)
        /// </summary>
        public static async Task<(bool isOpen, string message)> CheckPortFromInternetAsync(int port)
        {
            try
            {
                // Use canyouseeme.org API style check
                var response = await _http.GetStringAsync($"https://portchecker.co/check?port={port}");
                
                if (response.Contains("open") || response.Contains("Open"))
                {
                    return (true, "Port is OPEN from internet");
                }
                else
                {
                    return (false, "Port is CLOSED from internet");
                }
            }
            catch
            {
                // Fallback: Try to connect to self from outside perspective
                try
                {
                    string publicIp = await GetPublicIpAsync();
                    using (var client = new TcpClient())
                    {
                        var connectTask = client.ConnectAsync(publicIp, port);
                        if (await Task.WhenAny(connectTask, Task.Delay(5000)) == connectTask)
                        {
                            return (true, $"Port {port} reachable on {publicIp}");
                        }
                        else
                        {
                            return (false, $"Port {port} not reachable on {publicIp} (timeout)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    return (false, $"Could not verify: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Check NAT type (basic detection)
        /// </summary>
        public static async Task<string> GetNatStatusAsync()
        {
            try
            {
                string publicIp = await GetPublicIpAsync();
                
                if (publicIp.StartsWith("10.") || publicIp.StartsWith("192.168.") || publicIp.StartsWith("172."))
                {
                    return "Double NAT detected (CGNAT or nested router)";
                }
                
                return "Single NAT (normal router)";
            }
            catch
            {
                return "Unable to determine NAT status";
            }
        }

        /// <summary>
        /// Detect service running on a port
        /// </summary>
        public static async Task<string> DetectServiceAsync(string host, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(host, port);
                    if (await Task.WhenAny(connectTask, Task.Delay(3000)) != connectTask)
                    {
                        return "No response";
                    }

                    using (var stream = client.GetStream())
                    {
                        stream.ReadTimeout = 2000;
                        stream.WriteTimeout = 2000;

                        // Send empty line to trigger banner
                        byte[] probe = new byte[] { 0x0D, 0x0A };
                        await stream.WriteAsync(probe, 0, probe.Length);

                        byte[] buffer = new byte[256];
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                        
                        if (read > 0)
                        {
                            string banner = System.Text.Encoding.ASCII.GetString(buffer, 0, read).Trim();
                            return IdentifyService(banner, port);
                        }
                    }
                }
            }
            catch
            {
                return GuessServiceByPort(port);
            }

            return GuessServiceByPort(port);
        }

        private static string IdentifyService(string banner, int port)
        {
            string lower = banner.ToLower();
            
            if (lower.Contains("ssh")) return "SSH " + ExtractVersion(banner);
            if (lower.Contains("http")) return "HTTP " + ExtractVersion(banner);
            if (lower.Contains("ftp")) return "FTP " + ExtractVersion(banner);
            if (lower.Contains("smtp")) return "SMTP " + ExtractVersion(banner);
            if (lower.Contains("mysql")) return "MySQL";
            if (lower.Contains("postgresql")) return "PostgreSQL";
            if (lower.Contains("redis")) return "Redis";
            if (lower.Contains("mongo")) return "MongoDB";
            
            if (!string.IsNullOrWhiteSpace(banner) && banner.Length < 50)
            {
                return $"Banner: {banner}";
            }

            return GuessServiceByPort(port);
        }

        private static string ExtractVersion(string banner)
        {
            int idx = banner.IndexOf('/');
            if (idx > 0 && idx < banner.Length - 1)
            {
                int end = banner.IndexOfAny(new[] { ' ', '\r', '\n' }, idx);
                if (end > idx) return banner.Substring(idx + 1, end - idx - 1);
            }
            return "";
        }

        private static string GuessServiceByPort(int port)
        {
            return port switch
            {
                21 => "FTP",
                22 => "SSH",
                23 => "Telnet",
                25 => "SMTP",
                53 => "DNS",
                80 => "HTTP",
                110 => "POP3",
                143 => "IMAP",
                443 => "HTTPS",
                445 => "SMB",
                993 => "IMAPS",
                995 => "POP3S",
                1433 => "MSSQL",
                1521 => "Oracle DB",
                3306 => "MySQL",
                3389 => "RDP",
                5432 => "PostgreSQL",
                5900 => "VNC",
                6379 => "Redis",
                8080 => "HTTP Proxy",
                8443 => "HTTPS Alt",
                27017 => "MongoDB",
                _ => "Unknown"
            };
        }
    }
}
