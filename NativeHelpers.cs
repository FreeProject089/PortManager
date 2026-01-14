using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Net;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PortManager
{
    public enum Protocol
    {
        TCP,
        UDP
    }

    public class PortInfo : INotifyPropertyChanged
    {
        private string _remoteHostname = string.Empty;
        private string _serviceName = string.Empty;
        private string _externalStatus = string.Empty;
        private string _state = string.Empty;
        private string _remoteAddress = string.Empty;

        public Protocol Protocol { get; set; }
        public int LocalPort { get; set; }
        public string LocalAddress { get; set; } = string.Empty;
        public int RemotePort { get; set; }
        public string RemoteAddress { get => _remoteAddress; set { _remoteAddress = value; OnPropertyChanged(); } }
        public string State { get => _state; set { _state = value; OnPropertyChanged(); } }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string ProcessPath { get; set; } = string.Empty;
        public string RemoteHostname { get => _remoteHostname; set { _remoteHostname = value; OnPropertyChanged(); } }
        public string ServiceName { get => _serviceName; set { _serviceName = value; OnPropertyChanged(); } }
        public string ExternalStatus { get => _externalStatus; set { _externalStatus = value; OnPropertyChanged(); } } 
        public bool IsSuspicious { get; set; }
        public string SuspiciousReason { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static class PortReader
    {
        private const int AF_INET = 2; // IPv4
        private const int AF_INET6 = 23; // IPv6

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, TcpTableClass tblClass, uint reserved = 0);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int dwOutBufLen, bool sort, int ipVersion, UdpTableClass tblClass, uint reserved = 0);

        public static List<PortInfo> GetPorts()
        {
            var ports = new List<PortInfo>();
            ports.AddRange(GetTcpPorts());
            ports.AddRange(GetUdpPorts());
            // TODO: IPv6 support if needed, currently focusing on IPv4 for simplicity
            return ports;
        }

        private static List<PortInfo> GetTcpPorts()
        {
            var buffer = IntPtr.Zero;
            var ports = new List<PortInfo>();

            try
            {
                int bufferSize = 0;
                // First call to get buffer size
                GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, AF_INET, TcpTableClass.TCP_TABLE_OWNER_PID_ALL);

                buffer = Marshal.AllocHGlobal(bufferSize);
                // Second call to get data
                if (GetExtendedTcpTable(buffer, ref bufferSize, false, AF_INET, TcpTableClass.TCP_TABLE_OWNER_PID_ALL) == 0)
                {
                    var table = Marshal.PtrToStructure<MIB_TCPTABLE_OWNER_PID>(buffer);
                    var rowPtr = (IntPtr)((long)buffer + Marshal.SizeOf(table.dwNumEntries));

                    for (int i = 0; i < table.dwNumEntries; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                        
                        ports.Add(new PortInfo
                        {
                            Protocol = Protocol.TCP,
                            LocalAddress = new IPAddress(BitConverter.GetBytes(row.localAddr)).ToString(),
                            LocalPort = (ushort)IPAddress.NetworkToHostOrder((short)row.localPort),
                            RemoteAddress = new IPAddress(BitConverter.GetBytes(row.remoteAddr)).ToString(),
                            RemotePort = (ushort)IPAddress.NetworkToHostOrder((short)row.remotePort),
                            State = ((TcpState)row.state).ToString(),
                            ProcessId = (int)row.owningPid
                        });

                        rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf(row));
                    }
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
            return ports;
        }

        private static List<PortInfo> GetUdpPorts()
        {
            var buffer = IntPtr.Zero;
            var ports = new List<PortInfo>();

            try
            {
                int bufferSize = 0;
                GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, false, AF_INET, UdpTableClass.UDP_TABLE_OWNER_PID);

                buffer = Marshal.AllocHGlobal(bufferSize);
                if (GetExtendedUdpTable(buffer, ref bufferSize, false, AF_INET, UdpTableClass.UDP_TABLE_OWNER_PID) == 0)
                {
                    var table = Marshal.PtrToStructure<MIB_UDPTABLE_OWNER_PID>(buffer);
                    var rowPtr = (IntPtr)((long)buffer + Marshal.SizeOf(table.dwNumEntries));

                    for (int i = 0; i < table.dwNumEntries; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                        
                        ports.Add(new PortInfo
                        {
                            Protocol = Protocol.UDP,
                            LocalAddress = new IPAddress(BitConverter.GetBytes(row.localAddr)).ToString(),
                            LocalPort = (ushort)IPAddress.NetworkToHostOrder((short)row.localPort),
                            RemoteAddress = "", // UDP works differently
                            RemotePort = 0,
                            State = "N/A",
                            ProcessId = (int)row.owningPid
                        });

                        rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf(row));
                    }
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
            return ports;
        }

        // --- Structs and Enums ---

        private enum TcpTableClass
        {
            TCP_TABLE_BASIC_LISTENER,
            TCP_TABLE_BASIC_CONNECTIONS,
            TCP_TABLE_BASIC_ALL,
            TCP_TABLE_OWNER_PID_LISTENER,
            TCP_TABLE_OWNER_PID_CONNECTIONS,
            TCP_TABLE_OWNER_PID_ALL,
            TCP_TABLE_OWNER_MODULE_LISTENER,
            TCP_TABLE_OWNER_MODULE_CONNECTIONS,
            TCP_TABLE_OWNER_MODULE_ALL
        }

        private enum UdpTableClass
        {
            UDP_TABLE_BASIC,
            UDP_TABLE_OWNER_PID,
            UDP_TABLE_OWNER_MODULE
        }

        private enum TcpState
        {
            Closed = 1,
            Listen = 2,
            SynSent = 3,
            SynReceived = 4,
            Established = 5,
            FinWait1 = 6,
            FinWait2 = 7,
            CloseWait = 8,
            Closing = 9,
            LastAck = 10,
            TimeWait = 11,
            DeleteTcb = 12
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPTABLE_OWNER_PID
        {
            public uint dwNumEntries;
            // Rows follow
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPTABLE_OWNER_PID
        {
            public uint dwNumEntries;
            // Rows follow
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            public uint localPort;
            public uint owningPid;
        }
    }
}
