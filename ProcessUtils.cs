using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace PortManager
{
    public static class ProcessUtils
    {
        public static void EnrichPortInfo(PortInfo info)
        {
            if (info.ProcessId <= 0)
            {
                info.ProcessName = "System";
                return;
            }

            try
            {
                var process = Process.GetProcessById(info.ProcessId);
                info.ProcessName = process.ProcessName;
                try
                {
                    // Accessing MainModule might fail for some system processes due to permissions
                    info.ProcessPath = process.MainModule?.FileName; 
                }
                catch
                {
                    info.ProcessPath = "Access Denied / System";
                }
            }
            catch (ArgumentException)
            {
                // Process might have ended between port check and now
                info.ProcessName = "Unknown (Exited)";
                info.ProcessPath = "";
            }
            catch (Exception)
            {
                info.ProcessName = "Unknown";
            }
        }

        public static void KillProcess(int pid)
        {
             try
            {
                var process = Process.GetProcessById(pid);
                process.Kill();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to kill process {pid}: {ex.Message}");
            }
        }
    }
}
