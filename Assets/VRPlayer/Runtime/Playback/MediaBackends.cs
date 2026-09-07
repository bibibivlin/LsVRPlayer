using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Video;

namespace VRPlayer
{
    public static class AndroidBridge
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        public static readonly AndroidJavaClass Api=new AndroidJavaClass("com.linxyi.vrplayer.PlayerBridge");
        public static void Initialize(){using var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer");using var activity=unity.GetStatic<AndroidJavaObject>("currentActivity");Api.CallStatic("initialize",activity);}
#endif
    }
    public sealed class AndroidMediaBackend : IMediaBackend
    {
        public MediaSnapshot Snapshot{get;private set;}=new MediaSnapshot();
        public Texture Texture=>texture;
        public bool RequiresGammaConversion=>true;
        public int RenderedFrames{
            get{
#if UNITY_ANDROID && !UNITY_EDITOR
                return GetRenderedFrames();
#else
                return 0;
#endif
            }
        }
        private Texture2D texture;
#if UNITY_ANDROID && !UNITY_EDITOR
        private int textureHandle,textureEpoch=-1;
        private float nextPoll;
#endif
        public AndroidMediaBackend(){
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge.Initialize();
#endif
        }
        public void Tick(){
#if UNITY_ANDROID && !UNITY_EDITOR
            if(Time.unscaledTime>=nextPoll){nextPoll=Time.unscaledTime+.15f;string json=AndroidBridge.Api.CallStatic<string>("getSnapshot");if(!string.IsNullOrEmpty(json))Snapshot=JsonUtility.FromJson<MediaSnapshot>(json)??new MediaSnapshot();}
            int w=Snapshot.width>0?Snapshot.width:1280,h=Snapshot.height>0?Snapshot.height:720;
            SetVideoSize(w,h);
            int handle=GetVideoTexture(),epoch=GetTextureEpoch();
            if(handle!=0&&(texture==null||handle!=textureHandle||epoch!=textureEpoch||texture.width!=w||texture.height!=h)){
                if(texture!=null)UnityEngine.Object.Destroy(texture);
                texture=Texture2D.CreateExternalTexture(w,h,TextureFormat.RGBA32,false,true,new IntPtr(handle));texture.name="Android video GPU texture";
                textureHandle=handle;textureEpoch=epoch;
            }
#endif
        }
        public void RenderFrame(){
#if UNITY_ANDROID && !UNITY_EDITOR
            GL.IssuePluginEvent(GetRenderEventFunc(),1);
#endif
        }
        public void Open(string uri,long position,int generation){Snapshot=new MediaSnapshot{generation=generation,state=2};
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge.Api.CallStatic("open",uri,position,generation);GL.IssuePluginEvent(GetRenderEventFunc(),3);
#endif
        }
        public void Play()=>Command("play");public void Pause()=>Command("pause");public void Stop()=>Command("stop");
        private void Command(string method,params object[] args){
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge.Api.CallStatic(method,args);
#endif
        }
        public void Seek(long millis)=>Command("seek",millis);
        public void SetVolume(float value)=>Command("volume",value);
        public void SetSpeed(float value)=>Command("speed",value);
        public void SetRepeat(bool value)=>Command("repeat",value);
        public void SelectTrack(string id,int type)=>Command("selectTrack",id,type);
        public void Dispose(){
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge.Api.CallStatic("release");GL.IssuePluginEvent(GetRenderEventFunc(),2);
#endif
            if(texture!=null)UnityEngine.Object.Destroy(texture);
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        [DllImport("vrplayerrender")] private static extern IntPtr GetRenderEventFunc();
        [DllImport("vrplayerrender")] private static extern int GetVideoTexture();
        [DllImport("vrplayerrender")] private static extern int GetTextureEpoch();
        [DllImport("vrplayerrender")] private static extern int GetRenderedFrames();
        [DllImport("vrplayerrender")] private static extern void SetVideoSize(int width,int height);
#endif
    }
    public sealed class UnityMediaBackend : IMediaBackend
    {
        private readonly VideoPlayer player;
        private readonly AudioSource audio;
        private long pendingPosition;
        public MediaSnapshot Snapshot{get;private set;}=new MediaSnapshot();
        public Texture Texture=>player.texture;
        public bool RequiresGammaConversion=>false;
        public int RenderedFrames=>(int)Math.Max(0,player.frame);
        public UnityMediaBackend(GameObject host)
        {
            player=host.AddComponent<VideoPlayer>();player.playOnAwake=false;player.renderMode=VideoRenderMode.APIOnly;
            audio=host.AddComponent<AudioSource>();audio.spatialBlend=0;player.audioOutputMode=VideoAudioOutputMode.AudioSource;
            player.prepareCompleted+=_=>{Snapshot.state=3;player.time=pendingPosition/1000d;if(player.audioTrackCount>0){player.EnableAudioTrack(0,true);player.SetTargetAudioSource(0,audio);}player.Play();};
            player.errorReceived+=(_,message)=>{Snapshot.error=message;Snapshot.state=1;};
            player.loopPointReached+=_=>{if(!player.isLooping)Snapshot.state=4;};
        }
        public void Tick(){Snapshot.playing=player.isPlaying;if(player.isPrepared){Snapshot.position=(long)(player.time*1000);Snapshot.duration=(long)(player.length*1000);Snapshot.buffered=Snapshot.duration;Snapshot.width=(int)player.width;Snapshot.height=(int)player.height;Snapshot.fps=player.frameRate;Snapshot.decoder="Unity Editor preview";}}
        public void Open(string uri,long position,int generation){player.Stop();Snapshot=new MediaSnapshot{state=2,generation=generation};pendingPosition=position;player.url=uri;player.Prepare();}
        public void Play()=>player.Play();public void Pause()=>player.Pause();public void Stop(){player.Stop();Snapshot=new MediaSnapshot();}
        public void Seek(long millis){if(player.isPrepared)player.time=Math.Max(0,millis/1000d);}
        public void SetVolume(float value)=>audio.volume=value;public void SetSpeed(float value){if(player.canSetPlaybackSpeed)player.playbackSpeed=value;}
        public void SetRepeat(bool value)=>player.isLooping=value;
        public void SelectTrack(string id,int type){}
        public void Dispose(){UnityEngine.Object.Destroy(player);UnityEngine.Object.Destroy(audio);}
    }
}
