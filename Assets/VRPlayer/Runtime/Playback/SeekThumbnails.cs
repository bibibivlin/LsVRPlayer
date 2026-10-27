using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Video;

namespace VRPlayer
{
    public interface ISeekThumbnailSource
    {
        Task<Texture2D> ReadThumbnail(long position,int generation,CancellationToken cancellation);
    }

    // All texture ownership and continuations stay on Unity's main thread.
    public sealed class SeekThumbnailCache : IDisposable
    {
        public const int Capacity=24;
        private readonly Dictionary<long,Texture2D> frames=new Dictionary<long,Texture2D>();
        private readonly LinkedList<long> recent=new LinkedList<long>();
        private CancellationTokenSource lifetime=new CancellationTokenSource();
        private int generation=-1;
        public int Count=>frames.Count;
        public static long Position(float value,long duration)=>duration<=0?0:Math.Min(duration-1,(long)(Mathf.Clamp01(value)*duration));
        public static long Key(long position)=>Math.Max(0,position)/2000*2000;
        public void SetGeneration(int value)
        {
            if(value==generation)return;generation=value;lifetime.Cancel();lifetime.Dispose();lifetime=new CancellationTokenSource();
            foreach(var frame in frames.Values)Release(frame);frames.Clear();recent.Clear();
        }
        public async Task<Texture2D> Get(ISeekThumbnailSource source,long position,int expectedGeneration,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();if(source==null||expectedGeneration!=generation)return null;
            long key=Key(position);
            if(frames.TryGetValue(key,out var cached)){recent.Remove(key);recent.AddLast(key);return cached;}
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,cancellation);
            Texture2D frame=await source.ReadThumbnail(key,expectedGeneration,linked.Token);
            if(linked.IsCancellationRequested||expectedGeneration!=generation){Release(frame);return null;}
            if(frame==null)return null;
            if(frames.TryGetValue(key,out cached)){Release(frame);return cached;}
            while(frames.Count>=Capacity){long oldest=recent.First.Value;recent.RemoveFirst();Release(frames[oldest]);frames.Remove(oldest);}
            frames.Add(key,frame);recent.AddLast(key);return frame;
        }
        public void Dispose(){SetGeneration(-1);lifetime.Cancel();lifetime.Dispose();}
        public static void Release(UnityEngine.Object value){if(value==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
    }

    public static class AndroidSeekThumbnail
    {
        [Serializable] private sealed class Result {public bool ok;public int generation;public long position;public string image;}
        public static async Task<Texture2D> Read(long position,int generation,CancellationToken cancellation)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            cancellation.ThrowIfCancellationRequested();int id=AndroidBridge.Api.CallStatic<int>("beginThumbnail",position,generation);if(id==0)return null;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(8000);
            try{
                while(true){
                    timeout.Token.ThrowIfCancellationRequested();string json=AndroidBridge.Api.CallStatic<string>("takeThumbnail",id);
                    if(!string.IsNullOrEmpty(json)){
                        var result=JsonUtility.FromJson<Result>(json);
                        if(result==null||!result.ok||result.generation!=generation||result.position!=position||string.IsNullOrEmpty(result.image))return null;
                        var texture=new Texture2D(2,2,TextureFormat.RGB24,false){name="Seek thumbnail",wrapMode=TextureWrapMode.Clamp};
                        try{if(texture.LoadImage(Convert.FromBase64String(result.image),true))return texture;}
                        catch{SeekThumbnailCache.Release(texture);throw;}
                        SeekThumbnailCache.Release(texture);return null;
                    }
                    await Task.Delay(40,timeout.Token);
                }
            }finally{AndroidBridge.Api.CallStatic("cancelThumbnail",id);}
#else
            await Task.CompletedTask;return null;
#endif
        }
    }

    public static class UnitySeekThumbnail
    {
        private static readonly SemaphoreSlim decoder=new SemaphoreSlim(1,1);
        public static async Task<Texture2D> Read(string uri,long position,CancellationToken cancellation)
        {
            if(string.IsNullOrEmpty(uri)||!Uri.TryCreate(uri,UriKind.Absolute,out var location)||!location.IsFile)return null;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(8000);
            await decoder.WaitAsync(timeout.Token);GameObject host=null;RenderTexture target=null;
            try{
                host=new GameObject("Seek preview decoder"){hideFlags=HideFlags.HideAndDontSave};
                var player=host.AddComponent<VideoPlayer>();player.playOnAwake=false;player.audioOutputMode=VideoAudioOutputMode.None;
                player.renderMode=VideoRenderMode.RenderTexture;player.waitForFirstFrame=true;player.skipOnDrop=true;
                bool failed=false,ready=false;player.errorReceived+=(_,__)=>failed=true;
                player.url=uri;player.Prepare();
                while(!player.isPrepared&&!failed)await Task.Delay(20,timeout.Token);
                if(failed)return null;
                float scale=Mathf.Min(320f/Math.Max(1,player.width),180f/Math.Max(1,player.height));
                target=new RenderTexture(Math.Max(1,(int)(player.width*scale)),Math.Max(1,(int)(player.height*scale)),0);target.Create();
                player.targetTexture=target;player.sendFrameReadyEvents=true;
                double seconds=Math.Min(position/1000d,Math.Max(0,player.length-.1));
                player.frameReady+=(_,__)=>{if(player.time>=seconds-.15)ready=true;};
                player.time=seconds;player.Play();
                while(!ready&&!failed)await Task.Delay(20,timeout.Token);
                player.Pause();timeout.Token.ThrowIfCancellationRequested();if(failed)return null;
                var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false){name="Seek thumbnail",wrapMode=TextureWrapMode.Clamp};
                var previous=RenderTexture.active;
                try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply(false,true);return texture;}
                catch{SeekThumbnailCache.Release(texture);throw;}
                finally{RenderTexture.active=previous;}
            }finally{
                if(host!=null){var player=host.GetComponent<VideoPlayer>();player.Stop();player.targetTexture=null;SeekThumbnailCache.Release(host);}
                if(target!=null){target.Release();SeekThumbnailCache.Release(target);}decoder.Release();
            }
        }
    }
}
