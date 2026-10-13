using UltimateX.Core.Common;

namespace UltimateX.Services.Network;

public sealed record AdapterInfo(
    string Name,
    string Description,
    string Kind,
    bool IsUp,
    long? LinkSpeedBps,
    string? MacAddress,
    IReadOnlyList<string> IPv4,
    IReadOnlyList<string> IPv6,
    IReadOnlyList<string> SubnetMasks,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers,
    bool DhcpEnabled,
    IReadOnlyList<string> DhcpServers);

public sealed record PingStatistics(string Target, int Sent, int Received, long? MinMs, long? MaxMs, double? AvgMs, double? JitterMs)
{
    public double LossPercent => Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent;
}

public sealed record DnsTestResult(string Server, bool Reachable, double? ResponseMs, bool Resolved, string Detail);

public sealed record DiagnosisStep(string Name, DataState State, string Detail);

public sealed record PublicIpResult(string? IPv4, string? IPv6, string Source);
