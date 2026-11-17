using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace VRPlayer.Tests
{
    public sealed class EmbyDiscoveryTests
    {
        [Test] public void DiscoveryPreservesAdvertisedServerNameAddressAndReverseProxy()
        {
            var server = EmbyDiscovery.ParseResponse("{\"Address\":\"https://media.example.test:8920/emby/\",\"Id\":\"server-a\",\"Name\":\"Home\"}", IPAddress.Parse("192.0.2.10"));
            Assert.That(server, Is.Not.Null);
            Assert.That(server.Address, Is.EqualTo("https://media.example.test:8920/emby"));
            Assert.That(server.Id, Is.EqualTo("server-a"));
            Assert.That(server.Name, Is.EqualTo("Home"));
        }

        [TestCase("http://0.0.0.0:8096/emby", "http://192.0.2.10:8096/emby")]
        [TestCase("http://127.0.0.1:8096", "http://192.0.2.10:8096")]
        public void UnusableAnnouncedHostIsReplacedByRespondersAddress(string address, string expected)
        {
            var server = EmbyDiscovery.ParseResponse("{\"Address\":\"" + address + "\",\"Id\":\"one\",\"Name\":\"Home\"}", IPAddress.Parse("192.0.2.10"));
            Assert.That(server.Address, Is.EqualTo(expected));
        }

        [TestCase("not json")]
        [TestCase("{}")]
        [TestCase("{\"Address\":\"ftp://example.test\",\"Id\":\"one\"}")]
        [TestCase("{\"Address\":\"http://user:password@example.test\",\"Id\":\"one\"}")]
        [TestCase("{\"Address\":\"http://example.test?api_key=secret\",\"Id\":\"one\"}")]
        public void MalformedOrSecretBearingDiscoveryResponsesAreIgnored(string json)
            => Assert.That(EmbyDiscovery.ParseResponse(json, IPAddress.Loopback), Is.Null);

        [Test] public async Task UdpDiscoveryFindsServersAndDeduplicatesRepeatedResponses()
        {
            using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var target = (IPEndPoint)responder.Client.LocalEndPoint;
            var fixture = Task.Run(async () =>
            {
                var incoming = responder.ReceiveAsync();
                Assert.That(await Task.WhenAny(incoming, Task.Delay(3000)), Is.SameAs(incoming));
                var request = await incoming;
                Assert.That(Encoding.UTF8.GetString(request.Buffer), Is.EqualTo("who is EmbyServer?"));
                var response = Encoding.UTF8.GetBytes("{\"Address\":\"http://127.0.0.1:8096\",\"Id\":\"one\",\"Name\":\"Fixture\"}");
                await responder.SendAsync(response, response.Length, request.RemoteEndPoint);
                await responder.SendAsync(response, response.Length, request.RemoteEndPoint);
            });
            var result = await EmbyDiscovery.Scan(CancellationToken.None, new[] { target }, 500);
            await fixture;
            Assert.That(result.Select(server => server.Name), Is.EqualTo(new[] { "Fixture" }));
        }

        [Test] public async Task CancellingUdpDiscoveryStopsItsWaitingSocket()
        {
            using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            using var cancellation = new CancellationTokenSource();
            var scan = EmbyDiscovery.Scan(cancellation.Token, new[] { (IPEndPoint)responder.Client.LocalEndPoint }, 10000);
            var incoming = responder.ReceiveAsync();
            Assert.That(await Task.WhenAny(incoming, Task.Delay(3000)), Is.SameAs(incoming));
            await incoming;
            cancellation.Cancel();
            var completed = await Task.WhenAny(scan, Task.Delay(2000));
            Assert.That(completed, Is.SameAs(scan));
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await scan);
        }

        [Test] public void PublicUsersPreserveServerOrderAndIgnoreInvalidOrDuplicateRecords()
        {
            var users = EmbyClient.ParsePublicUsers("[{\"Id\":\"a\",\"Name\":\"Viewer\",\"HasPassword\":true},{\"Id\":\"b\",\"Name\":\"Guest\"},{\"Id\":\"a\",\"Name\":\"duplicate\"},{\"Id\":\"c\",\"Name\":\"\"}]");
            Assert.That(users.Select(user => user.Name), Is.EqualTo(new[] { "Viewer", "Guest" }));
            Assert.That(users[0].HasPassword, Is.True);
            Assert.That(EmbyClient.ParsePublicUsers("[]"), Is.Empty);
        }

        [TestCase("{}")][TestCase("not json")][TestCase("")]
        public void InvalidPublicUserResponseDoesNotProduceSelectableAccounts(string json)
            => Assert.Throws<InvalidOperationException>(() => EmbyClient.ParsePublicUsers(json));

        [Test] public void CancelledPublicUserLookupDoesNotIssueRequest()
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await new EmbyClient("fixture").GetPublicUsers("http://example.test", cancellation.Token));
        }
    }
}
