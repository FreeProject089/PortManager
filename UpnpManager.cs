using Open.Nat;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PortManager
{
    public static class UpnpManager
    {
        private static NatDevice _device;

        public static async Task<bool> InitializeAsync()
        {
            if (_device != null) return true;

            try
            {
                var discoverer = new NatDiscoverer();
                var cts = new CancellationTokenSource(5000); // 5 seconds timeout
                _device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);
                return _device != null;
            }
            catch (NatDeviceNotFoundException)
            {
                // No UPnP device found
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task MapPortAsync(int port, string protocol, string description)
        {
             if (_device == null)
             {
                 bool success = await InitializeAsync();
                 if (!success) throw new Exception("No UPnP Gateway found.");
             }

             var mapping = new Mapping(
                 protocol == "TCP" ? Open.Nat.Protocol.Tcp : Open.Nat.Protocol.Udp,
                 port,
                 port,
                 description
             );

             await _device.CreatePortMapAsync(mapping);
        }

        public static async Task DeleteMapAsync(int port, string protocol)
        {
            if (_device == null) return; // If we never initialized, we probably didn't map anything

             var proto = protocol == "TCP" ? Open.Nat.Protocol.Tcp : Open.Nat.Protocol.Udp;
             try
             {
                 await _device.DeletePortMapAsync(new Mapping(proto, port, port));
             }
             catch
             {
                 // Ignore if not found or already deleted
             }
        }
    }
}
