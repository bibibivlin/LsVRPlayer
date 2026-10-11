using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace VRPlayer
{
    // Public account metadata and remembered credentials deliberately use separate stores.
    public sealed class EmbyAccountStore
    {
        [Serializable] private sealed class Metadata
        {
            public int version = 1;
            public string selectedId;
            public List<EmbyAccount> accounts = new List<EmbyAccount>();
        }
        [Serializable] private sealed class Credential
        {
            public string id, serverUrl, serverId, userId, accessToken;
        }
        [Serializable] private sealed class Credentials
        {
            public List<Credential> tokens = new List<Credential>();
        }

        private readonly string path;
        private readonly Func<string> loadCredentials;
        private readonly Func<string, bool> saveCredentials;
        private string savedCredentials = "{\"tokens\":[]}";
        private string savedSelectedId = "";
        public List<EmbyAccount> Accounts { get; } = new List<EmbyAccount>();
        public string SelectedId { get; set; } = "";
        public string LastError { get; private set; } = "";

        public EmbyAccountStore(string path, Func<string> loadCredentials = null, Func<string, bool> saveCredentials = null)
        {
            this.path = Path.GetFullPath(path);
            this.loadCredentials = loadCredentials ?? LoadProtectedCredentials;
            this.saveCredentials = saveCredentials ?? SaveProtectedCredentials;
            try
            {
                if (File.Exists(this.path))
                {
                    var metadata = JsonUtility.FromJson<Metadata>(File.ReadAllText(this.path));
                    if (metadata?.accounts != null)
                        foreach (var account in metadata.accounts)
                        {
                            if (account == null || string.IsNullOrEmpty(account.id) || string.IsNullOrEmpty(account.userId)) continue;
                            try { account.serverUrl = EmbyClient.NormalizeServerUrl(account.serverUrl); }
                            catch (ArgumentException) { continue; }
                            account.accessToken = "";
                            if (Accounts.All(value => value.id != account.id)) Accounts.Add(account);
                        }
                    SelectedId = metadata?.selectedId ?? "";
                }
                string json = this.loadCredentials();
                if (!string.IsNullOrEmpty(json))
                {
                    var credentials = JsonUtility.FromJson<Credentials>(json);
                    foreach (var account in Accounts)
                        if (account.remember) account.accessToken = credentials?.tokens?.FirstOrDefault(value => value.id == account.id &&
                            value.serverUrl == account.serverUrl && value.serverId == account.serverId && value.userId == account.userId)?.accessToken ?? "";
                    savedCredentials = json;
                }
            }
            catch (Exception)
            {
                LastError = "Emby 账户读取失败，请重新登录";
                // A damaged credential vault must never trigger a plaintext fallback.
                foreach (var account in Accounts) account.accessToken = "";
            }
            if (!Accounts.Any(account => account.id == SelectedId)) SelectedId = Accounts.FirstOrDefault()?.id ?? "";
            savedSelectedId = SelectedId;
        }

        public void Upsert(EmbyAccount account)
        {
            if (account == null || string.IsNullOrEmpty(account.userId)) throw new ArgumentException("Emby 账户无效");
            var previousAccounts = new List<EmbyAccount>(Accounts);
            string previousSelection = SelectedId, originalId = account.id, originalServerUrl = account.serverUrl;
            try
            {
                account.serverUrl = EmbyClient.NormalizeServerUrl(account.serverUrl);
                var previous = Accounts.FirstOrDefault(value =>
                    (!string.IsNullOrEmpty(account.id) && value.id == account.id) ||
                    (value.userId == account.userId &&
                        ((!string.IsNullOrEmpty(account.serverId) && value.serverId == account.serverId) ||
                         (string.IsNullOrEmpty(account.serverId) && value.serverUrl.Equals(account.serverUrl, StringComparison.OrdinalIgnoreCase)))));
                if (previous != null) { account.id = previous.id; Accounts.Remove(previous); }
                if (string.IsNullOrEmpty(account.id)) account.id = Guid.NewGuid().ToString("N");
                Accounts.Add(account);
                SelectedId = account.id;
                Save();
            }
            catch
            {
                Accounts.Clear(); Accounts.AddRange(previousAccounts);
                SelectedId = previousSelection;
                account.id = originalId; account.serverUrl = originalServerUrl;
                throw;
            }
        }

        public void Remove(string id)
        {
            var previousAccounts = new List<EmbyAccount>(Accounts);
            string previousSelection = SelectedId;
            try
            {
                Accounts.RemoveAll(account => account.id == id);
                if (SelectedId == id) SelectedId = Accounts.FirstOrDefault()?.id ?? "";
                Save();
            }
            catch
            {
                Accounts.Clear(); Accounts.AddRange(previousAccounts);
                SelectedId = previousSelection;
                throw;
            }
        }

        public void Save()
        {
            string temporary = path + ".tmp";
            bool credentialWriteAttempted = false;
            try
            {
                var credentials = new Credentials();
                foreach (var account in Accounts)
                    if (account.remember && !string.IsNullOrEmpty(account.accessToken))
                        credentials.tokens.Add(new Credential { id = account.id, serverUrl = account.serverUrl,
                            serverId = account.serverId, userId = account.userId, accessToken = account.accessToken });
                string credentialJson = JsonUtility.ToJson(credentials);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // Stage metadata first so ordinary filesystem failures cannot alter the vault.
                File.WriteAllText(temporary, JsonUtility.ToJson(new Metadata { selectedId = SelectedId, accounts = Accounts }, true));
                credentialWriteAttempted = true;
                if (!saveCredentials(credentialJson)) throw new IOException();
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                savedCredentials = credentialJson;
                savedSelectedId = SelectedId;
                LastError = "";
            }
            catch (Exception)
            {
                // A failed commit leaves the preceding metadata in place. Restore its tokens,
                // including after an OS vault writer reports an uncertain partial failure.
                if (credentialWriteAttempted)
                    try { saveCredentials(savedCredentials); } catch (Exception) { }
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception) { }
                SelectedId = savedSelectedId;
                LastError = "Emby 账户保存失败，请检查设备存储后重试";
                throw new InvalidOperationException(LastError);
            }
        }

        private string LoadProtectedCredentials()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge.Initialize();
            return AndroidBridge.Api.CallStatic<string>("loadEmbyAccounts") ?? "";
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            string vaultPath = path + ".credentials";
            return File.Exists(vaultPath) ? Encoding.UTF8.GetString(Transform(File.ReadAllBytes(vaultPath), false)) : "";
#else
            return "";
#endif
        }

        private bool SaveProtectedCredentials(string json)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge.Initialize();
            return AndroidBridge.Api.CallStatic<bool>("saveEmbyAccounts", json);
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string vaultPath = path + ".credentials", temporary = vaultPath + ".tmp";
            File.WriteAllBytes(temporary, Transform(Encoding.UTF8.GetBytes(json), true));
            if (File.Exists(vaultPath)) File.Replace(temporary, vaultPath, null);
            else File.Move(temporary, vaultPath);
            return true;
#else
            // Accounts can be used for this session when no supported OS vault exists.
            return (JsonUtility.FromJson<Credentials>(json)?.tokens?.Count ?? 0) == 0;
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [StructLayout(LayoutKind.Sequential)] private struct DataBlob
        {
            public int length;
            public IntPtr data;
        }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DataBlob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

        private static byte[] Transform(byte[] bytes, bool protect)
        {
            var input = new DataBlob { length = bytes.Length, data = Marshal.AllocHGlobal(bytes.Length) };
            DataBlob output = default;
            try
            {
                Marshal.Copy(bytes, 0, input.data, bytes.Length);
                bool success = protect
                    ? CryptProtectData(ref input, "LsVRPlayer Emby", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!success) throw new IOException("Emby 凭据保护失败");
                var result = new byte[output.length];
                Marshal.Copy(output.data, result, 0, result.Length);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(input.data);
                if (output.data != IntPtr.Zero) LocalFree(output.data);
            }
        }
#endif
    }
}
