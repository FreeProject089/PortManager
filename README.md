# PortManager Ultimate

An advanced Network and Port Management Tool for Windows.

## New Features (v1.1)

- **Precise Network Scanning**:
  - Scan entire Subnets (e.g., 192.168.1.x) or Single IPs.
  - Choose between "Fast Scan" (Common ports) or "Precise Scan" (Top 100 ports).
- **Port Listener & Firewall**:
  - Open TCP/UDP ports on your local machine easily.
  - **Auto-add to Windows Firewall**: Check the box to automatically create an inbound rule allowing traffic (Requires Admin).
- **Modern UI**: Completely redesigned Dark Theme interface with glass-like aesthetics.

## How to Run

It is recommended to run as **Administrator** to use the Firewall and Process Killing features.

1. Open PowerShell / CMD as Administrator.
2. Navigate to this folder.
3. Run: `dotnet run`

## Troubleshooting

- **Scan is empty?** Ensure your firewall isn't blocking ICMP (Ping). Try "Precise Scan" on a single IP even if ping fails.
- **Firewall error?** The app must be run as Administrator to invoke `netsh advfirewall`.

## How to compile & publish

- run ; dotnet build
- run ; dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./PublishFinalvRefined1.2
