using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VRPlayer
{
    [Serializable] public sealed class EmbyDiscoveredServer
    {
        public string Address, Id, Name;
    }

    public static class EmbyDiscovery
    {
        private const int DiscoveryPort = 7359, MaxPacketBytes = 8192, MaxServers = 64;
        private static readonly byte[] Probe = Encoding.UTF8.GetBytes("who is EmbyServer?");

        public static Task<EmbyDiscoveredServer[]> Scan(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.Run(() => Receive(BroadcastTargets(), 3000, token), token);
        }

        // The explicit targets also allow a loopback responder to exercise the real UDP path.
        public static Task<EmbyDiscoveredServer[]> Scan(CancellationToken token, IEnumerable<IPEndPoint> targets, int durationMs)
        {
            token.ThrowIfCancellationRequested();
            var destinations = (targets ?? Array.Empty<IPEndPoint>()).ToArray();
            return Task.Run(() => Receive(destinations, Math.Max(1, durationMs), token), token);
        }

        private static EmbyDiscoveredServer[] Receive(IPEndPoint[] targets, int durationMs, CancellationToken token)
        {
            var servers = new List<EmbyDiscoveredServer>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.EnableBroadcast = true;
            socket.ReceiveTimeout = 200;
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            using var registration = token.Register(() => { try { socket.Close(); } catch (ObjectDisposedException) { } });
            token.ThrowIfCancellationRequested();
            int sent = 0;
            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                if (target == null || target.AddressFamily != AddressFamily.InterNetwork) continue;
                try { socket.SendTo(Probe, target); sent++; }
                catch (SocketException) { token.ThrowIfCancellationRequested(); }
            }
            if (sent == 0) throw new InvalidOperationException("无法启动局域网搜索");

            var clock = Stopwatch.StartNew();
            var buffer = new byte[MaxPacketBytes + 1];
            while (clock.ElapsedMilliseconds < durationMs && servers.Count < MaxServers)
            {
                token.ThrowIfCancellationRequested();
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    int count = socket.ReceiveFrom(buffer, ref remote);
                    if (count > MaxPacketBytes) continue;
                    var server = ParseResponse(Encoding.UTF8.GetString(buffer, 0, count), ((IPEndPoint)remote).Address);
                    if (server != null && seen.Add(server.Id + "\n" + server.Address)) servers.Add(server);
                }
                catch (SocketException ex)
                {
                    token.ThrowIfCancellationRequested();
                    if (ex.SocketErrorCode != SocketError.TimedOut && ex.SocketErrorCode != SocketError.WouldBlock && ex.SocketErrorCode != SocketError.MessageSize) throw;
                }
                catch (ObjectDisposedException) { token.ThrowIfCancellationRequested(); throw; }
            }
            token.ThrowIfCancellationRequested();
            return servers.OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(server => server.Address, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static EmbyDiscoveredServer ParseResponse(string json, IPAddress sender)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxPacketBytes) return null;
            try
            {
                var server = JsonUtility.FromJson<EmbyDiscoveredServer>(json);
                if (server == null || string.IsNullOrWhiteSpace(server.Id) || server.Id.Length > 256 ||
                    string.IsNullOrWhiteSpace(server.Address) || server.Address.Length > 2048 ||
                    !Uri.TryCreate(server.Address, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return null;
                server.Address = EmbyClient.NormalizeServerUrl(server.Address);
                // Some installations announce a wildcard or loopback address; use the responder's LAN IP.
                if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var host) &&
                    (IPAddress.IsLoopback(host) || host.Equals(IPAddress.Any) || host.Equals(IPAddress.IPv6Any)))
                {
                    if (sender == null || sender.AddressFamily != AddressFamily.InterNetwork || IPAddress.Any.Equals(sender)) return null;
                    server.Address = new UriBuilder(uri) { Host = sender.ToString() }.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
                }
                EmbyServerAddress.Parse(server.Address, out bool https, out string address, out string port);
                server.Address = EmbyServerAddress.Build(https, address, port);
                server.Id = server.Id.Trim();
                server.Name = string.IsNullOrWhiteSpace(server.Name) ? server.Address : server.Name.Trim();
                if (server.Name.Length > 160) server.Name = server.Name.Substring(0, 160);
                return server;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is InvalidOperationException)
            {
                return null;
            }
        }

        private static IPEndPoint[] BroadcastTargets()
        {
            var addresses = new HashSet<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null) continue;
                        byte[] address = unicast.Address.GetAddressBytes(), mask = unicast.IPv4Mask.GetAddressBytes(), broadcast = new byte[4];
                        for (int i = 0; i < 4; i++) broadcast[i] = (byte)(address[i] | ~mask[i]);
                        addresses.Add(new IPAddress(broadcast));
                    }
                }
            }
            catch (Exception ex) when (ex is NetworkInformationException || ex is NotSupportedException || ex is PlatformNotSupportedException)
            {
                // Limited broadcast still works on platforms that do not expose interface masks.
            }
            return addresses.Select(address => new IPEndPoint(address, DiscoveryPort)).ToArray();
        }
    }
}
