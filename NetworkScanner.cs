using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PortManager
{
    public class NetworkDevice
    {
        public string IpAddress { get; set; }
        public string Hostname { get; set; }
        public string Status { get; set; }
        public string OpenPorts { get; set; }
    }

    public static class NetworkScanner
    {
        // Broad Scan: Scan Subnet for online devices (Ping + Common Ports)
        public static async Task<List<NetworkDevice>> ScanSubnet(string subnetPrefix, IProgress<string> report = null) // subnetPrefix i.e. "192.168.1."
        {
            var detectedDevices = new ConcurrentBag<NetworkDevice>();
            var tasks = new List<Task>();

            // Scan 1 to 254
            for (int i = 1; i < 255; i++)
            {
                string ip = $"{subnetPrefix}{i}";
                tasks.Add(CheckDevice(ip, detectedDevices, fullPortScan: false));
            }

            await Task.WhenAll(tasks);
            return detectedDevices.OrderBy(d => d.IpAddress).ToList();
        }

        // Precise Scan: Scan Single IP for MANY ports (or even all)
        public static async Task<NetworkDevice> ScanSingleDevice(string ip, bool checkAllCommonPorts = true)
        {
             var devices = new ConcurrentBag<NetworkDevice>();
             await CheckDevice(ip, devices, fullPortScan: checkAllCommonPorts);
             return devices.FirstOrDefault();
        }

        private static async Task CheckDevice(string ip, ConcurrentBag<NetworkDevice> devices, bool fullPortScan)
        {
            using (var ping = new Ping())
            {
                bool isOnline = false;
                string status = "Offline";

                try
                {
                    // Check if online first
                    var reply = await ping.SendPingAsync(ip, 500); // Increased ping timeout slightly
                    
                    if (reply.Status == IPStatus.Success)
                    {
                        isOnline = true;
                        status = "Online";
                    }
                    else if (!fullPortScan)
                    {
                        // Fallback: If ping fails, try to connect to common ports (135/445/80) to detect "Hidden" devices (Firewall blocking ICMP)
                        // This helps find Windows PCs that default to blocking Ping
                        if (await ProbePort(ip, 135) || await ProbePort(ip, 445) || await ProbePort(ip, 80))
                        {
                            isOnline = true;
                            status = "Online (No Ping)";
                        }
                    }

                    // Always scan ports if full scan is requested, or if we found it to be online via Ping/Probe
                    if (isOnline || fullPortScan) 
                    {
                        var hostname = GetHostname(ip);
                        
                        // Determine port list
                        int[] portsToScan;
                        if (fullPortScan)
                        {
                            portsToScan = CommonPorts.Top100; 
                        }
                        else
                        {
                            portsToScan = CommonPorts.Basic;
                        }

                        string openPorts = await ScanPorts(ip, portsToScan);

                        // Only add if online or ports found
                        if (isOnline || !string.IsNullOrEmpty(openPorts)) 
                        {
                            devices.Add(new NetworkDevice
                            {
                                IpAddress = ip,
                                Status = status,
                                Hostname = hostname,
                                OpenPorts = string.IsNullOrEmpty(openPorts) ? "None (scanned)" : openPorts
                            });
                        }
                    }
                }
                catch { }
            }
        }

        private static string GetHostname(string ip)
        {
            try
            {
                return Dns.GetHostEntry(ip).HostName;
            }
            catch
            {
                return "Unknown";
            }
        }

        private static async Task<bool> ProbePort(string ip, int port)
        {
            using (var tcp = new TcpClient())
            {
                try
                {
                    var task = tcp.ConnectAsync(ip, port);
                    if (await Task.WhenAny(task, Task.Delay(200)) == task) // 200ms probe
                    {
                        return tcp.Connected;
                    }
                }
                catch { }
            }
            return false;
        }

        private static async Task<string> ScanPorts(string ip, int[] ports)
        {
            var openPorts = new ConcurrentBag<string>();
            var options = new ParallelOptions { MaxDegreeOfParallelism = 50 }; // Limit concurrency

            // We use Task.WhenAll instead of Parallel.ForEach for genuine async IO
            var chunks = ports.Chunk(20); // Process in chunks to simply avoiding opening too many sockets at once
            
            foreach (var chunk in chunks)
            {
                var tasks = chunk.Select(async port =>
                {
                    using (var tcpClient = new TcpClient())
                    {
                        try
                        {
                            var connectTask = tcpClient.ConnectAsync(ip, port);
                            // Increased timeout to 300ms for better reliability on WiFi
                            if (await Task.WhenAny(connectTask, Task.Delay(300)) == connectTask)
                            {
                                if (tcpClient.Connected)
                                {
                                    openPorts.Add(port.ToString());
                                }
                            }
                        }
                        catch { }
                    }
                });
                await Task.WhenAll(tasks);
            }
            
            if (openPorts.IsEmpty) return "";
            return string.Join(", ", openPorts.Select(int.Parse).OrderBy(x => x));
        }

        public static string GetLocalSubnetPrefix()
        {
             // Naive approach: get first IPv4 LAN IP
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string addr = ip.Address.ToString();
                             // Filter out APIPA (169.254) if possible, but basic logic assumes standard
                            if (addr.StartsWith("169.254")) continue;
                            
                            int lastDot = addr.LastIndexOf('.');
                            return addr.Substring(0, lastDot + 1);
                        }
                    }
                }
            }
            return "192.168.1.";
        }
    }

    public static class CommonPorts
    {
        public static readonly int[] Basic = { 21, 22, 23, 25, 53, 80, 110, 135, 139, 443, 445, 3306, 3389, 8080 };
        public static readonly int[] Top100 = { 
            7, 20, 21, 22, 23, 25, 26, 53, 80, 81, 88, 110, 111, 113, 119, 135, 137, 138, 139, 143, 179, 199, 
            389, 443, 445, 465, 513, 514, 515, 548, 554, 587, 631, 636, 873, 993, 995,
            1433, 1521, 1723, 2000, 2049, 2121, 2222, 2375, 2376, 2525, 3000, 3128, 3306, 3389, 3690, 4000, 
            4444, 4567, 5000, 5001, 5060, 5432, 5800, 5900, 5901, 6000, 6001, 6379, 6667, 8000, 8008, 8080, 8081, 
            8090, 8181, 8443, 8500, 8888, 9000, 9090, 9200, 9300, 10000, 27017, 27018, 50000 
            // truncated for brevity, but this covers major bases
        };
    }
}
