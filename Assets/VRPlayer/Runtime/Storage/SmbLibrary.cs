using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VRPlayer
{
    public sealed class SmbLibrary : IDisposable
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private int scanId;
#endif
        public void StartDiscovery()
        {
            StopDiscovery();
#if UNITY_ANDROID && !UNITY_EDITOR
            scanId=AndroidBridge.Api.CallStatic<int>("beginDiscovery");
#endif
        }
        public SmbDiscoverySnapshot PollDiscovery()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return scanId==0?new SmbDiscoverySnapshot():JsonUtility.FromJson<SmbDiscoverySnapshot>(AndroidBridge.Api.CallStatic<string>("pollDiscovery",scanId));
#else
            return new SmbDiscoverySnapshot{complete=true,error="局域网发现请在 PICO Android 版本中使用"};
#endif
        }
        public void StopDiscovery()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(scanId!=0)AndroidBridge.Api.CallStatic("stopDiscovery",scanId);
            scanId=0;
#endif
        }
        public async Task<SmbShareResult> Shares(SmbServer server,CancellationToken token)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            int id=AndroidBridge.Api.CallStatic<int>("beginShares",JsonUtility.ToJson(server));
            float deadline=Time.realtimeSinceStartup+30;
            try{while(true){token.ThrowIfCancellationRequested();string json=AndroidBridge.Api.CallStatic<string>("takeResult",id);if(!string.IsNullOrEmpty(json))return JsonUtility.FromJson<SmbShareResult>(json);if(Time.realtimeSinceStartup>deadline)return new SmbShareResult{error="共享枚举超时，可重试或手填共享名",code="network"};await Task.Delay(80,token);}}
            finally{AndroidBridge.Api.CallStatic("cancelRequest",id);}
#else
            await Task.Yield();token.ThrowIfCancellationRequested();return new SmbShareResult{error="共享枚举请在 PICO Android 版本中使用",code="unsupported"};
#endif
        }
        public void Dispose()=>StopDiscovery();
    }
}
