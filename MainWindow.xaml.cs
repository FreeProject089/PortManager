using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;
// Use explicit namespaces for WinForms/Drawing to avoid ambiguity with WPF
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using WinIcon = System.Drawing.Icon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using FormsApplication = System.Windows.Forms.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfApplication = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;

namespace PortManager
{
    public class ActiveListener
    {
        public int Port { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public object? ListenerObject { get; set; }
        public bool FirewallRuleAdded { get; set; }
        public string FirewallName { get; set; } = string.Empty;
        public bool UpnpEnabled { get; set; }
        public bool ForwardOnly { get; set; }
        
        public string DisplayString => $"{Protocol} :{Port}{(ForwardOnly ? " [FWD]" : "")}{(UpnpEnabled ? " [UPnP]" : "")}";
        public string FirewallStatus => FirewallRuleAdded ? $"FW: {FirewallName}" : "FW: None";
    }

    public partial class MainWindow : Window
    {
        private DispatcherTimer _timer;
        private ObservableCollection<ActiveListener> _activeListeners = new ObservableCollection<ActiveListener>();
        private ObservableCollection<PortInfo> _displayedPorts = new ObservableCollection<PortInfo>();
        private AppSettings _settings;
        private Dictionary<string, string> _dnsCache = new Dictionary<string, string>();
        private Dictionary<string, string> _serviceCache = new Dictionary<string, string>();
        private Dictionary<string, string> _externalCache = new Dictionary<string, string>();
        private HashSet<string> _knownPorts = new HashSet<string>();
        private List<double> _portCountHistory = new List<double>();
        private HashSet<string> _previousPorts = new HashSet<string>();
        private bool _isUpdating = false;
        private NotifyIcon _notifyIcon;
        private bool _isClosing = false;
        
        // Stats
        private DispatcherTimer _statsTimer;
        
        // Services
        public class ServiceItem { public string ServiceName { get; set; } public string DisplayName { get; set; } public string Status { get; set; } }
        
        // Process Stats
        public class ProcessInfoItem 
        { 
            public int Id { get; set; } 
            public string ProcessName { get; set; } 
            public double MemoryMB { get; set; } 
            public double CpuPercent { get; set; } 
            public double GpuPercent { get; set; }
            public double VramMB { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            
            _settings = PersistenceManager.Load();
            InitializeProfiles();
            
            TxtSubnet.Text = _settings.LastSubnet;
            TxtSearch.Text = "";

            UpdateIpInfo();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            ListActiveListeners.ItemsSource = _activeListeners;
            GridLocalPorts.ItemsSource = _displayedPorts;
            ListLogs.ItemsSource = LogManager.Logs;
            
            // Load listeners from active profile
            var profile = _settings.Profiles.FirstOrDefault(p => p.Name == _settings.ActiveProfile);
            if (profile != null)
            {
                foreach (var lp in profile.Listeners) StartListenerFromProfile(lp);
            }

            RefreshLocalPorts();
            RefreshStartupItems();
            
            InitializeTrayIcon();
            
            LogManager.LogSystem("PM Ultimate started");
            
            if (!FirewallManager.IsAdmin()) TxtGlobalStatus.Text = "? Not Admin";
        }

        private void BtnRefreshLocal_Click(object sender, RoutedEventArgs e) => RefreshLocalPorts();

        private void InitializeProfiles()
        {
            // If no profiles exist, we start with 0 (or just Default)
            if (_settings.Profiles.Count == 0)
            {
                // We keep a 'Default' profile if none exists, as per settings.json logic
                _settings.Profiles.Add(new UserProfile { Name = "Default", Listeners = new List<ListenerProfile>() });
                PersistenceManager.Save(_settings);
            }
            
            RefreshProfilesUI();
        }

        private void RefreshProfilesUI()
        {
            CmbActiveProfile.Items.Clear();
            ListProfilesUI.Items.Clear();
            foreach (var p in _settings.Profiles)
            {
                var item = new ComboBoxItem { Content = p.Name };
                CmbActiveProfile.Items.Add(item);
                if (p.Name == _settings.ActiveProfile) CmbActiveProfile.SelectedItem = item;

                ListProfilesUI.Items.Add(p.Name);
            }
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = WinIcon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName!);
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "PM Ultimate";
            _notifyIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = WindowState.Normal; };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show PM Ultimate", null, (s, e) => { this.Show(); this.WindowState = WindowState.Normal; });
            contextMenu.Items.Add("Exit", null, (s, e) => { _isClosing = true; System.Windows.Application.Current.Shutdown(); });
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
            {
                this.Hide();
            }
            base.OnStateChanged(e);
        }

        private async void UpdateIpInfo()
        {
            try
            {
                string hostName = Dns.GetHostName();
                TxtHeaderHostname.Text = hostName.ToUpper();
                TxtMyHostname.Text = hostName;
                
                var host = await Dns.GetHostEntryAsync(hostName);
                string ip = host.AddressList.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "0.0.0.0";
                TxtHeaderIp.Text = ip;
                TxtMyIp.Text = ip;
            }
            catch { }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _settings.LastSubnet = TxtSubnet.Text;
            _settings.SavedListeners.Clear();
            foreach (var l in _activeListeners)
            {
                _settings.SavedListeners.Add(new ListenerProfile 
                { 
                    Port = l.Port, 
                    Protocol = l.Protocol,
                    FirewallRuleName = l.FirewallName,
                    UpnpEnabled = l.UpnpEnabled,
                    ForwardOnly = l.ForwardOnly
                });
            }
            PersistenceManager.Save(_settings);

            foreach (var l in _activeListeners.ToList())
            {
                 StopListener(l);
            }
            
            LogManager.LogSystem("PM Ultimate closed");
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (ChkAutoRefresh.IsChecked == true && !_isUpdating)
            {
                RefreshLocalPorts();
            }
            
            if (MainTabs.SelectedIndex == 5) // Logs/Alerts tab
            {
                ListLogs.Items.Refresh();
                LogManager.HasCriticalUnseen = false;
                UpdateTaskbarIcon(false);
            }
            else if (LogManager.HasCriticalUnseen)
            {
                UpdateTaskbarIcon(true);
            }

            // Stats Auto-Refresh
            if (MainTabs.SelectedIndex == 4 && ChkStatsAutoRefresh.IsChecked == true)
            {
                 RefreshProcessStats(true);
            }
        }

        private void UpdateStatistics() { }

        private void UpdateTaskbarIcon(bool isRed)
        {
            try
            {
                if (isRed)
                {
                    TaskbarItemInfo.Overlay = (ImageSource)System.Windows.Application.Current.Resources["RedAlertOverlay"];
                }
                else
                {
                    TaskbarItemInfo.Overlay = null;
                }
            }
            catch { }
        }

        private void CmbActiveProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbActiveProfile.SelectedItem is ComboBoxItem item)
            {
                string profileName = item.Content.ToString()!;
                if (_settings.ActiveProfile == profileName) return;

                _settings.ActiveProfile = profileName;
                PersistenceManager.Save(_settings);
                
                // Switch display to match
                var p = _settings.Profiles.FirstOrDefault(x => x.Name == profileName);
                if (p != null) GridProfilePorts.ItemsSource = p.Listeners;
            }
        }

        // --- Profiles Tab Logic ---
        private void ListProfilesUI_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListProfilesUI.SelectedItem is string profileName)
            {
                var profile = _settings.Profiles.FirstOrDefault(p => p.Name == profileName);
                if (profile != null)
                {
                    GridProfilePorts.ItemsSource = profile.Listeners;
                }
            }
        }

        private void BtnCreateProfile_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtNewProfileName.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (_settings.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                WpfMessageBox.Show("Profile already exists!");
                return;
            }

            _settings.Profiles.Add(new UserProfile { Name = name, Listeners = new List<ListenerProfile>() });
            PersistenceManager.Save(_settings);
            RefreshProfilesUI();
            TxtNewProfileName.Text = "";
        }

        private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ListProfilesUI.SelectedItem is string name)
            {
                if (name == "Default") { WpfMessageBox.Show("Cannot delete Default profile."); return; }
                var profile = _settings.Profiles.FirstOrDefault(p => p.Name == name);
                if (profile != null)
                {
                    _settings.Profiles.Remove(profile);
                    if (_settings.ActiveProfile == name) _settings.ActiveProfile = "Default";
                    PersistenceManager.Save(_settings);
                    RefreshProfilesUI();
                }
            }
        }

        private void BtnAddPortToProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ListProfilesUI.SelectedItem is string profileName)
            {
                var profile = _settings.Profiles.FirstOrDefault(p => p.Name == profileName);
                if (profile != null && int.TryParse(TxtProfileNewPort.Text, out int port))
                {
                    string proto = (CmbProfileNewProto.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "TCP";
                    string fwName = $"PM_Unique_{port}_{proto}";
                    profile.Listeners.Add(new ListenerProfile { 
                        Port = port, 
                        Protocol = proto, 
                        UpnpEnabled = ChkProfileUpnp.IsChecked == true,
                        ForwardOnly = ChkProfileFwd.IsChecked == true,
                        FirewallRuleName = fwName
                    });
                    PersistenceManager.Save(_settings);
                    GridProfilePorts.Items.Refresh();
                }
            }
        }

        private void GridProfilePorts_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            PersistenceManager.Save(_settings);
        }

        private void BtnImportFromSession_Click(object sender, RoutedEventArgs e)
        {
            if (ListProfilesUI.SelectedItem is string profileName)
            {
                var profile = _settings.Profiles.FirstOrDefault(p => p.Name == profileName);
                if (profile != null)
                {
                    foreach (var active in _activeListeners)
                    {
                        if (!profile.Listeners.Any(l => l.Port == active.Port && l.Protocol == active.Protocol))
                        {
                            profile.Listeners.Add(new ListenerProfile 
                            { 
                                Port = active.Port, 
                                Protocol = active.Protocol, 
                                UpnpEnabled = active.UpnpEnabled,
                                ForwardOnly = active.ForwardOnly, 
                                FirewallRuleName = active.FirewallName 
                            });
                        }
                    }
                    PersistenceManager.Save(_settings);
                    GridProfilePorts.Items.Refresh();
                    WpfMessageBox.Show($"Imported {_activeListeners.Count} listeners to profile '{profileName}'.");
                }
            }
        }

        private void BtnRemovePortFromProfile_Click(object sender, RoutedEventArgs e)
        {
            if (GridProfilePorts.SelectedItem is ListenerProfile lp && ListProfilesUI.SelectedItem is string profileName)
            {
                var profile = _settings.Profiles.FirstOrDefault(p => p.Name == profileName);
                if (profile != null)
                {
                    profile.Listeners.Remove(lp);
                    PersistenceManager.Save(_settings);
                    GridProfilePorts.Items.Refresh();
                }
            }
        }

        // REMOVED ALERT SETTINGS HANDLERS

        private async void MenuIdentifyService_Click(object sender, RoutedEventArgs e)
        {
            if (GridLocalPorts.SelectedItem is PortInfo portInfo)
            {
                portInfo.ServiceName = "Scanning...";
                GridLocalPorts.Items.Refresh();
                
                string service = await ExternalPortChecker.DetectServiceAsync("127.0.0.1", portInfo.LocalPort);
                portInfo.ServiceName = string.IsNullOrEmpty(service) ? "Unknown" : service;
                
                string key = $"{portInfo.LocalPort}:{portInfo.Protocol}:{portInfo.ProcessId}";
                lock(_serviceCache) { _serviceCache[key] = portInfo.ServiceName; }
                
                GridLocalPorts.Items.Refresh();
            }
        }

        private async void MenuTestExternal_Click(object sender, RoutedEventArgs e)
        {
            if (GridLocalPorts.SelectedItem is PortInfo portInfo)
            {
                portInfo.ExternalStatus = "Testing...";
                GridLocalPorts.Items.Refresh();
                
                var (isOpen, message) = await ExternalPortChecker.CheckPortFromInternetAsync(portInfo.LocalPort);
                portInfo.ExternalStatus = isOpen ? "OPEN" : "CLOSED";
                
                string key = $"{portInfo.LocalPort}:{portInfo.Protocol}:{portInfo.ProcessId}";
                lock(_externalCache) { _externalCache[key] = portInfo.ExternalStatus; }
                
                GridLocalPorts.Items.Refresh();
                
                LogManager.Log("NETWORK", "TEST", $"External test for port {portInfo.LocalPort}: {message}");
            }
        }

        private void BtnExportConfig_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON files (*.json)|*.json", FileName = "pm_config_export.json" };
            if (dialog.ShowDialog() == true)
            {
                PersistenceManager.ExportConfig(dialog.FileName, _settings);
                System.Windows.MessageBox.Show("Configuration exported successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnImportConfig_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON files (*.json)|*.json" };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var newSettings = PersistenceManager.ImportConfig(dialog.FileName);
                    _settings = newSettings;
                    PersistenceManager.Save(_settings);
                    System.Windows.MessageBox.Show("Configuration imported! Please restart the application to apply all changes.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void RefreshLocalPorts()
        {
            if (_isUpdating) return;
            _isUpdating = true;
            try
            {
                string searchText = TxtSearch.Text.ToLower().Trim();
                
                var allPorts = await Task.Run(() => 
                {
                    var ports = PortReader.GetPorts();
                    foreach (var p in ports)
                    {
                        ProcessUtils.EnrichPortInfo(p);
                        SuspiciousActivityAnalyzer.Analyze(p);
                        
                        string key = $"{p.LocalPort}:{p.Protocol}:{p.ProcessId}";
                        lock(_serviceCache) { if (_serviceCache.ContainsKey(key)) p.ServiceName = _serviceCache[key]; }
                        lock(_externalCache) { if (_externalCache.ContainsKey(key)) p.ExternalStatus = _externalCache[key]; }

                        if (!string.IsNullOrEmpty(p.RemoteAddress) && 
                            p.RemoteAddress != "0.0.0.0" && 
                            p.RemoteAddress != "::" && 
                            p.RemoteAddress != "*" &&
                            p.RemoteAddress != "127.0.0.1")
                        {
                            lock(_dnsCache)
                            {
                                if (_dnsCache.ContainsKey(p.RemoteAddress)) p.RemoteHostname = _dnsCache[p.RemoteAddress];
                                else p.RemoteHostname = "Resolving...";
                            }
                        }
                        else { p.RemoteHostname = "-"; }
                    }
                    return ports.OrderByDescending(p => p.IsSuspicious).ThenBy(p => p.Protocol).ThenBy(p => p.LocalPort).ToList();
                });

                CheckForNewPorts(allPorts);

                var filtered = allPorts;
                if (!string.IsNullOrEmpty(searchText))
                {
                    filtered = filtered.Where(p => 
                        p.LocalPort.ToString().Contains(searchText) ||
                        p.ProcessName.ToLower().Contains(searchText) ||
                        p.RemoteAddress.ToLower().Contains(searchText) ||
                        p.RemoteHostname.ToLower().Contains(searchText)
                    ).ToList();
                }

                // Update ObservableCollection incrementally to preserve selection
                Dispatcher.Invoke(() => 
                {
                    var selected = GridLocalPorts.SelectedItem as PortInfo;
                    string selectedKey = selected != null ? $"{selected.LocalPort}:{selected.Protocol}:{selected.ProcessId}" : "";

                    // Simple merge: clear and add if list is very different, or update items
                    // For now, let's do a smart clear/add that keeps selection if key matches
                    if (_displayedPorts.Count != filtered.Count || 
                        (_displayedPorts.Count > 0 && filtered.Count > 0 && _displayedPorts[0].LocalPort != filtered[0].LocalPort))
                    {
                        _displayedPorts.Clear();
                        foreach (var p in filtered) _displayedPorts.Add(p);
                    }
                    else
                    {
                        for (int i = 0; i < filtered.Count; i++)
                        {
                            _displayedPorts[i].State = filtered[i].State;
                            _displayedPorts[i].RemoteAddress = filtered[i].RemoteAddress;
                            _displayedPorts[i].RemotePort = filtered[i].RemotePort;
                            _displayedPorts[i].RemoteHostname = filtered[i].RemoteHostname;
                            _displayedPorts[i].ServiceName = filtered[i].ServiceName;
                            _displayedPorts[i].ExternalStatus = filtered[i].ExternalStatus;
                        }
                    }

                    if (!string.IsNullOrEmpty(selectedKey))
                    {
                        var newSel = _displayedPorts.FirstOrDefault(p => $"{p.LocalPort}:{p.Protocol}:{p.ProcessId}" == selectedKey);
                        if (newSel != null && GridLocalPorts.SelectedItem != newSel) GridLocalPorts.SelectedItem = newSel;
                    }

                    TxtConnCount.Text = filtered.Count.ToString();
                    TxtStatusLocal.Text = $"Updated: {DateTime.Now:HH:mm:ss}";
                });

                ResolveMissingDns(allPorts);
            }
            finally { _isUpdating = false; }
        }

        private void CheckForNewPorts(List<PortInfo> ports)
        {
            var currentPorts = new HashSet<string>(ports.Select(p => $"{p.LocalPort}:{p.Protocol}:{p.ProcessName}"));
            
            if (_previousPorts.Count > 0 && _settings.Alerts.EnableNewPortAlerts)
            {
                var newPorts = currentPorts.Except(_previousPorts).ToList();
                foreach (var np in newPorts)
                {
                    var parts = np.Split(':');
                    if (parts.Length >= 3)
                    {
                        int port = int.Parse(parts[0]);
                        string protocol = parts[1];
                        string app = parts[2];
                        
                        // LogNewPort returns false if already notified this session
                        bool isNew = LogManager.LogNewPort(port, protocol, app, _settings.Alerts.EnableSoundAlerts);
                        
                        // Icon turning red is handled in Timer_Tick based on LogManager.HasCriticalUnseen
                    }
                }
            }
            
            _previousPorts = currentPorts;
        }

        private async void ResolveMissingDns(List<PortInfo> ports)
        {
            var toResolve = ports.Where(p => p.RemoteHostname == "Resolving...").Select(p => p.RemoteAddress).Distinct().ToList();
            
            foreach (var ip in toResolve)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var entry = await Dns.GetHostEntryAsync(ip);
                        lock(_dnsCache) { _dnsCache[ip] = entry.HostName; }
                    }
                    catch
                    {
                        lock(_dnsCache) { _dnsCache[ip] = "Unknown"; }
                    }
                });
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshLocalPorts();
        }

        // ===== STARTUP MANAGER =====
        private void BtnRefreshStartup_Click(object sender, RoutedEventArgs e) => RefreshStartupItems();
        
        private void RefreshStartupItems()
        {
            try { GridStartup.ItemsSource = StartupManager.GetStartupItems(); } catch { }
        }

        private void MenuStartupOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var item = GridStartup.SelectedItem as StartupItem;
            if (item != null && !string.IsNullOrEmpty(item.ProcessPath))
            {
                try { Process.Start("explorer.exe", $"/select,\"{item.ProcessPath}\""); } catch { }
            }
        }

        private void MenuStartupRemove_Click(object sender, RoutedEventArgs e)
        {
            var item = GridStartup.SelectedItem as StartupItem;
            if (item == null) return;

            if (WpfMessageBox.Show($"Remove '{item.Name}' from startup?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try { StartupManager.RemoveItem(item); RefreshStartupItems(); } catch { }
            }
        }

        // ===== FORWARD ONLY AUTO-SELECT =====
        private void ChkForwardOnly_Checked(object sender, RoutedEventArgs e)
        {
            ChkUpnp.IsChecked = true;
            ChkAddFirewall.IsChecked = true;
        }

        // ===== LISTENER LOGIC =====
        private void BtnStartListener_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtListenPort.Text, out int port))
            {
                WpfMessageBox.Show("Invalid port number.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? protocolSelection = (CmbProtocol.SelectedItem as ComboBoxItem)?.Content.ToString();
            bool addFirewall = ChkAddFirewall.IsChecked == true;
            bool useUpnp = ChkUpnp.IsChecked == true;
            bool forwardOnly = ChkForwardOnly.IsChecked == true;
            string firewallName = $"PM_Ultimate_{port}";

            if (protocolSelection?.Contains("&") == true)
            {
                StartSingleListener(port, "TCP", addFirewall, firewallName + "_TCP", false, useUpnp, forwardOnly);
                StartSingleListener(port, "UDP", addFirewall, firewallName + "_UDP", false, useUpnp, forwardOnly);
            }
            else
            {
                StartSingleListener(port, protocolSelection ?? "TCP", addFirewall, firewallName, false, useUpnp, forwardOnly);
            }
        }

        private void StartListenerFromProfile(ListenerProfile profile)
        {
            try { StartSingleListener(profile.Port, profile.Protocol, false, profile.FirewallRuleName, true, profile.UpnpEnabled, profile.ForwardOnly); } catch { }
        }

        private async void StartSingleListener(int port, string protocol, bool addFirewall, string fwName, bool isRestore = false, bool useUpnp = false, bool forwardOnly = false)
        {
            if (_activeListeners.Any(l => l.Port == port && l.Protocol == protocol))
            {
                if (!isRestore) WpfMessageBox.Show($"Port {port}/{protocol} already configured!", "Already Active", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                object? listenerObj = null;
                
                if (!forwardOnly)
                {
                    if (protocol == "TCP")
                    {
                        var listener = new TcpListener(IPAddress.Any, port);
                        listener.Start();
                        listenerObj = listener;
                    }
                    else
                    {
                        listenerObj = new UdpClient(port);
                    }
                }

                bool fwAdded = false;
                if (addFirewall)
                {
                    try { FirewallManager.AddRule(fwName, port, protocol); fwAdded = true; LogManager.LogFirewall(fwName, "created"); }
                    catch { if (!isRestore) WpfMessageBox.Show("Firewall failed. Run as Admin?"); }
                }

                if (useUpnp)
                {
                    try { await UpnpManager.MapPortAsync(port, protocol, fwName); LogManager.LogUpnp(port, protocol, "mapped"); }
                    catch { if (!isRestore) WpfMessageBox.Show("UPnP mapping failed."); }
                }

                _activeListeners.Add(new ActiveListener { 
                    Port = port, Protocol = protocol, ListenerObject = listenerObj,
                    FirewallRuleAdded = fwAdded, FirewallName = fwName, 
                    UpnpEnabled = useUpnp, ForwardOnly = forwardOnly
                });
                
                LogManager.LogPortOpened(port, protocol, forwardOnly ? "Forward Only" : "Listener");

                if (!isRestore)
                {
                    WpfMessageBox.Show($"Port {port}/{protocol} {(forwardOnly ? "configured (Forward Only)" : "started")}!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                if (!isRestore)
                {
                    WpfMessageBox.Show($"Port {port} is probably in use. Try 'Forward Only' mode.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnStopListener_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ActiveListener listener)
            {
                StopListener(listener);
                _activeListeners.Remove(listener);
            }
        }

        private async void StopListener(ActiveListener listener)
        {
            try
            {
                if (!listener.ForwardOnly && listener.ListenerObject != null)
                {
                    if (listener.Protocol == "TCP") ((TcpListener)listener.ListenerObject).Stop();
                    else ((UdpClient)listener.ListenerObject).Close();
                }

                if (listener.FirewallRuleAdded && !string.IsNullOrEmpty(listener.FirewallName))
                {
                    try { FirewallManager.RemoveRule(listener.FirewallName); LogManager.LogFirewall(listener.FirewallName, "removed"); } catch {}
                }

                if (listener.UpnpEnabled)
                {
                    try { await UpnpManager.DeleteMapAsync(listener.Port, listener.Protocol); LogManager.LogUpnp(listener.Port, listener.Protocol, "unmapped"); } catch {}
                }
                
                LogManager.LogPortClosed(listener.Port, listener.Protocol, "");
            }
            catch { }
        }

        // ===== KILL PORT (FORCE CLOSE) =====
        private void MenuClosePort_Click(object sender, RoutedEventArgs e)
        {
            var portInfo = GridLocalPorts.SelectedItem as PortInfo;
            if (portInfo == null) return;
            
            if (WpfMessageBox.Show($"Kill process using port {portInfo.LocalPort}?\n\nProcess: {portInfo.ProcessName}", "Close Port", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    ProcessUtils.KillProcess(portInfo.ProcessId);
                    LogManager.Log("PORT_CLOSED", "NETWORK", $"Killed process {portInfo.ProcessName} on port {portInfo.LocalPort}", portInfo.ProcessName, portInfo.LocalPort);
                    RefreshLocalPorts();
                }
                catch (Exception ex)
                {
                    WpfMessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ===== CONTEXT MENUS =====
        private void MenuKillProcess_Click(object sender, RoutedEventArgs e)
        {
            var portInfo = GridLocalPorts.SelectedItem as PortInfo;
            if (portInfo == null) return;

            if (WpfMessageBox.Show($"Kill process '{portInfo.ProcessName}'?", "Confirm Kill", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try { ProcessUtils.KillProcess(portInfo.ProcessId); RefreshLocalPorts(); } catch { }
            }
        }

        private void MenuOpenExplorer_Click(object sender, RoutedEventArgs e)
        {
            var portInfo = GridLocalPorts.SelectedItem as PortInfo;
            if (portInfo != null && !string.IsNullOrEmpty(portInfo.ProcessPath))
            {
                try { Process.Start("explorer.exe", $"/select,\"{portInfo.ProcessPath}\""); } catch { }
            }
        }

        private void MenuCopyLocal_Click(object sender, RoutedEventArgs e)
        {
            var p = GridLocalPorts.SelectedItem as PortInfo;
            if (p != null) System.Windows.Clipboard.SetText($"{p.LocalAddress}:{p.LocalPort}");
        }

        private void MenuCopyRemote_Click(object sender, RoutedEventArgs e)
        {
            var p = GridLocalPorts.SelectedItem as PortInfo;
            if (p != null) System.Windows.Clipboard.SetText($"{p.RemoteAddress}:{p.RemotePort}");
        }

        private void MenuCopyPath_Click(object sender, RoutedEventArgs e)
        {
            var p = GridLocalPorts.SelectedItem as PortInfo;
            if (p != null && !string.IsNullOrEmpty(p.ProcessPath)) System.Windows.Clipboard.SetText(p.ProcessPath);
        }

        // ===== SERVICES & STATS =====
        private void BtnRefreshServices_Click(object sender, RoutedEventArgs e)
        {
            var services = new List<ServiceItem>();
            try
            {
                // Using SC query to avoid references dependency issues
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sc",
                        Arguments = "query state= all",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string currentName = "", currentDisplay = "", currentState = "";

                foreach (var line in lines)
                {
                    string trim = line.Trim();
                    if (trim.StartsWith("SERVICE_NAME:")) currentName = trim.Substring(13).Trim();
                    else if (trim.StartsWith("DISPLAY_NAME:")) currentDisplay = trim.Substring(13).Trim();
                    else if (trim.StartsWith("STATE"))
                    {
                        string statePart = trim.Substring(trim.IndexOf(":") + 1).Trim();
                        // State looks like "4  RUNNING"
                        var parts = statePart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 1) currentState = parts[1];
                        
                        if (!string.IsNullOrEmpty(currentName))
                        {
                            services.Add(new ServiceItem { ServiceName = currentName, DisplayName = currentDisplay, Status = currentState });
                            currentName = ""; currentDisplay = ""; currentState = "";
                        }
                    }
                }
            }
            catch { }
            
            GridServices.ItemsSource = services;
            TxtServicesCount.Text = $"{services.Count} services found";
        }

        private void BtnRefreshStats_Click(object sender, RoutedEventArgs e)
        {
            RefreshProcessStats();
        }

        private async void RefreshProcessStats(bool isAuto = false)
        {
            if (!isAuto) BtnRefreshStats.IsEnabled = false;
            try
            {
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                TxtSystemUptime.Text = $"Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";

                // Global RAM
                try
                {
                    // Total RAM can be got via ComputerInfo or performance counter. Simple counter:
                    // Using basic Process info for total.
                    // For System wide we resort to basic counters if possible, else N/A.
                    // Let's rely on summing up known processes or just show N/A if complex.
                    // Actually, Environment.WorkingSet is current process.
                    // Stick to N/A for global unless we add PerformanceCounters cleanly.
                    TxtGlobalCpu.Text = "CPU: -"; // Requires PerfCounter
                    TxtGlobalRam.Text = "RAM: -";
                    TxtGlobalGpu.Text = "GPU: - (Req. Admin/Perf)";
                    TxtGlobalVram.Text = "VRAM: -";
                }
                catch {}

                var list = await Task.Run(() => 
                {
                    var results = new List<ProcessInfoItem>();
                    var processes = Process.GetProcesses();
                    var gpuCounters = new Dictionary<int, float>();

                    // Attempt global GPU fetch via Counters if possible (experimental)
                    // If failing, ignore.
                    
                    foreach (var p in processes)
                    {
                        try
                        {
                            if (p.Id == 0 || p.Id == 4) continue; 
                            
                            double mem = p.PrivateMemorySize64 / 1024.0 / 1024.0;
                            
                            results.Add(new ProcessInfoItem 
                            { 
                                Id = p.Id, 
                                ProcessName = p.ProcessName, 
                                MemoryMB = mem,
                                CpuPercent = 0,
                                GpuPercent = 0, // Placeholder
                                VramMB = 0      // Placeholder
                            });
                        }
                        catch { }
                    }
                    return results.OrderByDescending(r => r.MemoryMB).ToList();
                });

                GridProcessStats.ItemsSource = list;
            }
            finally
            {
                if (!isAuto) BtnRefreshStats.IsEnabled = true;
            }
        }

        // ===== SCANNER =====
        private void BtnScanLocal_Click(object sender, RoutedEventArgs e)
        {
            TxtSubnet.Text = NetworkScanner.GetLocalSubnetPrefix();
            RadioScopeSubnet.IsChecked = true;
            BtnScan_Click(sender, e);
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtSubnet.Text)) return;

            BtnScan.IsEnabled = false;
            ProgressScan.Visibility = Visibility.Visible;
            GridNetworkDevices.ItemsSource = null;

            try
            {
                string target = TxtSubnet.Text.Trim();
                bool singleIp = RadioScopeSingle.IsChecked == true;

                List<NetworkDevice> devices = new List<NetworkDevice>();

                if (singleIp)
                {
                     var device = await NetworkScanner.ScanSingleDevice(target, checkAllCommonPorts: true);
                     if(device != null) devices.Add(device);
                }
                else
                {
                    if (!target.EndsWith("."))
                    {
                         var segments = target.Split('.');
                         if (segments.Length == 4) target = string.Join(".", segments.Take(3)) + ".";
                         else if (segments.Length == 3) target += ".";
                    }
                    devices = await NetworkScanner.ScanSubnet(target);
                }

                GridNetworkDevices.ItemsSource = devices.Where(d => d != null).ToList();
            }
            catch { }
            finally
            {
                BtnScan.IsEnabled = true;
                ProgressScan.Visibility = Visibility.Hidden;
            }
        }

        // ===== LOGS =====
        private void BtnExportLogs_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV Files|*.csv", FileName = "pm_logs.csv" };
            if (dialog.ShowDialog() == true)
            {
                try { LogManager.ExportToCsv(dialog.FileName); WpfMessageBox.Show("Logs exported!"); } catch { }
            }
        }

        private void BtnRefreshLogs_Click(object sender, RoutedEventArgs e)
        {
            ListLogs.Items.Refresh();
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            LogManager.ClearLogs();
            ListLogs.Items.Refresh();
        }

    }
}
