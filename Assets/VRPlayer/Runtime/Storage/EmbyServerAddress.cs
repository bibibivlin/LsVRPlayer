using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace VRPlayer
{
    public static class EmbyServerAddress
    {
        public static string Build(bool https, string address, string port)
        {
            string value = (address ?? "").Trim();
            if (value.Length == 0) throw new ArgumentException("请输入 Emby 服务器地址、IP 或域名");
            if (value.Contains("://")) throw new ArgumentException("请选择 HTTP 或 HTTPS，地址栏无需填写协议");
            if (value.IndexOfAny(new[] { '?', '#', '\\' }) >= 0)
                throw new ArgumentException("服务器地址不能包含查询参数、片段或反斜杠");
            foreach (char character in value)
                if (char.IsControl(character)) throw new ArgumentException("服务器地址包含无效字符");

            int slash = value.IndexOf('/');
            string host = slash < 0 ? value : value.Substring(0, slash);
            string path = slash < 0 ? "" : value.Substring(slash);
            if (host.Length == 0 || host.IndexOf('@') >= 0)
                throw new ArgumentException("服务器地址不能包含账户信息");
            foreach (char character in host)
                if (char.IsWhiteSpace(character)) throw new ArgumentException("服务器 IP 或域名无效");

            if (host.StartsWith("[", StringComparison.Ordinal))
            {
                int closing = host.IndexOf(']');
                if (closing < 0) throw new ArgumentException("IPv6 地址的方括号不完整");
                if (closing != host.Length - 1) throw new ArgumentException("请将端口填入独立的端口栏");
                string ipv6 = host.Substring(1, host.Length - 2);
                if (!IPAddress.TryParse(ipv6, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetworkV6)
                    throw new ArgumentException("IPv6 地址无效");
                host = "[" + parsed + "]";
            }
            else if (host.IndexOf(':') >= 0)
            {
                if (!IPAddress.TryParse(host, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetworkV6)
                    throw new ArgumentException("请将端口填入独立的端口栏");
                host = "[" + parsed + "]";
            }
            else if (Uri.CheckHostName(host) == UriHostNameType.Unknown || host.IndexOfAny(new[] { '[', ']' }) >= 0)
                throw new ArgumentException("服务器 IP 或域名无效");

            string portText = (port ?? "").Trim();
            if (portText.Length == 0) throw new ArgumentException("请输入 Emby 服务器端口");
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int portNumber) || portNumber < 1 || portNumber > 65535)
                throw new ArgumentException("服务器端口必须是 1 到 65535 的整数");
            return EmbyClient.NormalizeServerUrl((https ? "https://" : "http://") + host + ":" +
                portNumber.ToString(CultureInfo.InvariantCulture) + path);
        }

        public static void Parse(string serverUrl, out bool https, out string address, out string port)
        {
            var uri = new Uri(EmbyClient.NormalizeServerUrl(serverUrl), UriKind.Absolute);
            https = uri.Scheme == Uri.UriSchemeHttps;
            string host = uri.HostNameType == UriHostNameType.IPv6 ? "[" + uri.Host.Trim('[', ']') + "]" : uri.Host;
            string path = uri.AbsolutePath == "/" ? "" : uri.AbsolutePath;
            address = host + path;
            port = uri.Port.ToString(CultureInfo.InvariantCulture);
        }
    }
}
