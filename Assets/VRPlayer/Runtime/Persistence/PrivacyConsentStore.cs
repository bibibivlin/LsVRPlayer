using System;
using System.IO;
using UnityEngine;

namespace VRPlayer
{
    public sealed class PrivacyConsentStore
    {
        public const string PolicyVersion="2026-10-17.1";
        [Serializable] private sealed class Consent {public string version,acceptedAtUtc;}
        private readonly string path;
        public bool Accepted{get;private set;}
        public PrivacyConsentStore(string path)
        {
            this.path=path;
            try{if(File.Exists(path)){var consent=JsonUtility.FromJson<Consent>(File.ReadAllText(path));Accepted=consent?.version==PolicyVersion&&DateTimeOffset.TryParse(consent.acceptedAtUtc,out _);}}
            catch(Exception){Accepted=false;}
        }
        public void Accept()
        {
            string temporary=path+".tmp";
            try{
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(temporary,JsonUtility.ToJson(new Consent{version=PolicyVersion,acceptedAtUtc=DateTime.UtcNow.ToString("O")}));
                File.Copy(temporary,path,true);Accepted=true;
            }finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
