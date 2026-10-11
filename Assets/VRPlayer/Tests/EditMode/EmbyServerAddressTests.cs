using System;
using System.IO;
using NUnit.Framework;

namespace VRPlayer.Tests
{
    public sealed class EmbyServerAddressTests
    {
        [TestCase(false, "192.0.2.10", "8096", "http://192.0.2.10:8096")]
        [TestCase(true, "media.example.test", "8920", "https://media.example.test:8920")]
        [TestCase(false, " media.example.test/emby/ ", " 8096 ", "http://media.example.test:8096/emby")]
        [TestCase(true, "media.example.test/proxy/emby", "443", "https://media.example.test/proxy/emby")]
        [TestCase(false, "2001:db8::10", "8096", "http://[2001:db8::10]:8096")]
        [TestCase(true, "[2001:db8::10]/proxy/emby", "8920", "https://[2001:db8::10]:8920/proxy/emby")]
        public void SeparateProtocolAddressAndPortProduceNormalizedUrl(bool https, string address, string port, string expected)
            => Assert.That(EmbyServerAddress.Build(https, address, port), Is.EqualTo(expected));

        [TestCase("http://media.example.test")]
        [TestCase("https://media.example.test/emby")]
        [TestCase("viewer:secret@media.example.test")]
        [TestCase("media.example.test:8096")]
        [TestCase("[2001:db8::10]:8096")]
        [TestCase("media.example.test?api_key=secret")]
        [TestCase("media.example.test/#secret")]
        [TestCase("media.example.test\\emby")]
        [TestCase("media example.test")]
        [TestCase("[2001:db8::10")]
        [TestCase("[media.example.test]")]
        [TestCase("")]
        public void AddressFieldRejectsProtocolCredentialPortAndMalformedHost(string address)
            => Assert.Throws<ArgumentException>(() => EmbyServerAddress.Build(false, address, "8096"));

        [TestCase("")][TestCase(" ")][TestCase("0")][TestCase("65536")][TestCase("-1")]
        [TestCase("+8096")][TestCase("8096.0")][TestCase("eight")]
        public void PortMustBeExplicitIntegerWithinTcpRange(string port)
            => Assert.Throws<ArgumentException>(() => EmbyServerAddress.Build(false, "media.example.test", port));

        [TestCase("1")][TestCase("65535")]
        public void BoundaryPortsAreAccepted(string port)
            => Assert.That(EmbyServerAddress.Build(false, "media.example.test", port), Is.EqualTo("http://media.example.test:" + port));

        [TestCase("http://media.example.test", false, "media.example.test", "80")]
        [TestCase("https://media.example.test/emby/", true, "media.example.test/emby", "443")]
        [TestCase("http://media.example.test:8096/proxy/emby", false, "media.example.test/proxy/emby", "8096")]
        [TestCase("https://[2001:db8::10]:8920/emby", true, "[2001:db8::10]/emby", "8920")]
        [TestCase("http://[::1]/proxy%20path", false, "[::1]/proxy%20path", "80")]
        public void ExistingUrlKeepsItsSchemeEffectivePortAndProxyPath(string url, bool expectedHttps, string expectedAddress, string expectedPort)
        {
            EmbyServerAddress.Parse(url, out bool https, out string address, out string port);
            Assert.That(https, Is.EqualTo(expectedHttps)); Assert.That(address, Is.EqualTo(expectedAddress));
            Assert.That(port, Is.EqualTo(expectedPort));
            Assert.That(EmbyServerAddress.Build(https, address, port), Is.EqualTo(EmbyClient.NormalizeServerUrl(url)));
        }

        [Test] public void EditingExistingServerFieldsKeepsRememberedAccountIdentityAndCredentialBinding()
        {
            string directory = Path.Combine(Path.GetTempPath(), "VRPlayerEmbyAddressTests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "accounts.json"), vault = "";
            Func<string> load = () => vault;
            Func<string, bool> save = value => { vault = value; return true; };
            try
            {
                var store = new EmbyAccountStore(path, load, save);
                var original = new EmbyAccount { id = "stable-account", serverId = "server", userId = "user", username = "viewer",
                    serverUrl = "https://media.example.test:8920/proxy/emby", accessToken = "fixture-secret", remember = true };
                store.Upsert(original);
                EmbyServerAddress.Parse(original.serverUrl, out bool https, out string address, out string port);
                original.serverUrl = EmbyServerAddress.Build(https, address, port);
                store.Save();
                var restored = new EmbyAccountStore(path, load, save);
                Assert.That(restored.Accounts[0].id, Is.EqualTo("stable-account"));
                Assert.That(restored.Accounts[0].serverUrl, Is.EqualTo(original.serverUrl));
                Assert.That(restored.Accounts[0].accessToken, Is.EqualTo("fixture-secret"));
                Assert.That(File.ReadAllText(path), Does.Not.Contain("fixture-secret"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
