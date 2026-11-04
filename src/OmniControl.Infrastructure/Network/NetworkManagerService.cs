using System.Net;
using System.Net.NetworkInformation;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Hardware;
using OmniControl.Core.Results;

namespace OmniControl.Infrastructure.Network;

public class NetworkManagerService : INetworkManagerService
{
    private readonly Dictionary<string, (long BytesRecv, long BytesSent, DateTime Timestamp)> _speedHistory = new();
    private readonly object _lock = new();

    public async Task<List<NetworkAdapterMetric>> GetNetworkAdaptersAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<NetworkAdapterMetric>();
            var now = DateTime.UtcNow;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = nic.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString() ?? string.Empty;
                var ipv6 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)?.Address.ToString() ?? string.Empty;
                var gateway = ipProps.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? string.Empty;
                var dns = string.Join(", ", ipProps.DnsAddresses.Select(d => d.ToString()));

                var stats = nic.GetIPStatistics();
                double dlSpeed = 0;
                double ulSpeed = 0;

                lock (_lock)
                {
                    if (_speedHistory.TryGetValue(nic.Id, out var prev))
                    {
                        var elapsed = (now - prev.Timestamp).TotalSeconds;
                        if (elapsed > 0.3)
                        {
                            dlSpeed = Math.Max(0, (stats.BytesReceived - prev.BytesRecv) / elapsed);
                            ulSpeed = Math.Max(0, (stats.BytesSent - prev.BytesSent) / elapsed);
                            _speedHistory[nic.Id] = (stats.BytesReceived, stats.BytesSent, now);
                        }
                    }
                    else
                    {
                        _speedHistory[nic.Id] = (stats.BytesReceived, stats.BytesSent, now);
                    }
                }

                list.Add(new NetworkAdapterMetric
                {
                    Id = nic.Id,
                    Name = nic.Name,
                    Description = nic.Description,
                    InterfaceType = nic.NetworkInterfaceType.ToString(),
                    MacAddress = nic.GetPhysicalAddress().ToString(),
                    Status = nic.OperationalStatus.ToString(),
                    IsConnected = nic.OperationalStatus == OperationalStatus.Up,
                    Ipv4Address = ipv4,
                    Ipv6Address = ipv6,
                    Gateway = gateway,
                    DnsServers = dns,
                    BytesReceived = stats.BytesReceived,
                    BytesSent = stats.BytesSent,
                    DownloadSpeedBps = Math.Round(dlSpeed, 1),
                    UploadSpeedBps = Math.Round(ulSpeed, 1)
                });
            }

            return list;
        }, cancellationToken);
    }

    public async Task<OperationResult<long>> RunPingTestAsync(string host = "1.1.1.1", CancellationToken cancellationToken = default)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 3000);

            if (reply.Status == IPStatus.Success)
            {
                return OperationResult<long>.Ok(reply.RoundtripTime, $"Ping to {host} succeeded ({reply.RoundtripTime} ms).");
            }

            return OperationResult<long>.Fail($"Ping to {host} failed: {reply.Status}");
        }
        catch (Exception ex)
        {
            return OperationResult<long>.Fail($"Ping diagnostics error: {ex.Message}");
        }
    }

    public async Task<OperationResult<string>> RunDnsLookupAsync(string host = "microsoft.com", CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(host, cancellationToken);
            var ips = string.Join(", ", entry.AddressList.Select(ip => ip.ToString()));
            return OperationResult<string>.Ok(ips, $"Resolved {host} -> {ips}");
        }
        catch (Exception ex)
        {
            return OperationResult<string>.Fail($"DNS lookup error: {ex.Message}");
        }
    }

    public Task<OperationResult> SetAdapterEnabledAsync(string adapterId, bool isEnabled, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.RequiresElevated("Enabling or disabling network adapters requires Administrator privileges."));
    }
}
