using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VRPlayer
{
    public enum Projection { Flat, Equirect180, Equirect360, Fisheye, Cubemap3x2, Eac3x2 }
    public enum StereoLayout { Mono, SideBySide, TopBottom }
    [Serializable] public sealed class ViewSettings
    {
        public Projection projection;
        public StereoLayout stereo;
        public bool half=true, swapEyes, monoEye;
        public float distance=3.5f, width=4.5f, heightOffset=.1f, curvature, fisheyeFov=180, brightness=1, panoramaZoom=1;
        public ViewSettings Copy() => JsonUtility.FromJson<ViewSettings>(JsonUtility.ToJson(this));
        public static ViewSettings Detect(string name)
        {
            var value=new ViewSettings();FormatDetector.Resolve(FormatDetector.Filename(name)).Apply(value);return value;
        }
        public float Aspect(int width,int height,float pixelRatio=1)
        {
            float aspect=width>0&&height>0?width/(float)height*Mathf.Max(.01f,pixelRatio):16f/9f;
            if(!half){if(stereo==StereoLayout.SideBySide)aspect*=.5f;if(stereo==StereoLayout.TopBottom)aspect*=2;}
            return Mathf.Clamp(aspect,.25f,8);
        }
    }
    [Serializable] public sealed class MediaTrack { public string id,name,codec;public int type;public bool selected,supported; }
    [Serializable] public sealed class NetworkReadSnapshot
    {
        public int generation;
        public bool readAhead;
        public long monotonicMs,requests,networkBytes,deliveredBytes,errors,cancelledReads,smallReads,opens;
        public double mbps,averageRequestBytes,averageReadBytes,averageReadMs,readP95Ms;
    }
    [Serializable] public sealed class MediaSnapshot
    {
        public int generation,state,width,height,dropped,stereoMode=-1;
        public int assStream,assRevision;
        public bool playing;
        public long position,duration,buffered;
        public long monotonicMs,bufferAheadMs,allocatedBufferBytes,rebufferMs;
        public bool loading,playWhenReady;
        public int rebufferCount,suppressionReason;
        public string bufferReason="";
        public NetworkReadSnapshot network;
        public float pixelRatio=1,fps,bitrate;
        public string error="",decoder="",audioDecoder="",codec="",embeddedSubtitle="";
        public MediaTrack[] tracks=Array.Empty<MediaTrack>();
        public MediaFormatHints formatHints;
        public bool formatProbeComplete;
        public bool HasError=>!string.IsNullOrEmpty(error);
    }
    [Serializable] public sealed class FileEntry
    {
        public string name,uri;public bool directory;public long size,modified;
        [NonSerialized] public bool metadataVerified; // Only a live directory listing supplies file identity.
        [NonSerialized] public string formatName; // Server titles need not contain the source file's VR tags.
        public bool IsVideo=>!directory&&((uri??"").StartsWith("emby://",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(name??"",@"\.(mp4|mkv|m4v|mov|webm|ts|m2ts|avi|wmv|mpg|mpeg)$",RegexOptions.IgnoreCase));
        public bool IsSubtitle=>!directory&&SubtitleFormat.IsSupported(name??"");
    }
    [Serializable] public sealed class FileResult { public bool ok;public string error,text;public FileEntry[] entries; }
    [Serializable] public sealed class SmbServer
    {
        public string id="",name="",host="",share="",username="",password="",domain="";
        public int port=445;public bool remember=true,encrypt;
    }
    [Serializable] public sealed class ServerList { public SmbServer[] servers=Array.Empty<SmbServer>(); }
    [Serializable] public sealed class SaveServerResult {public bool ok;public string id,error;}
    public enum FileSource { Home, Smb, Favorites, Recent, Directory, Emby }
    [Serializable] public sealed class DiscoveredSmbHost {public string name,host;public int port=445;}
    [Serializable] public sealed class SmbDiscoverySnapshot {public bool running,complete;public int tested,total;public string error="";public DiscoveredSmbHost[] hosts=Array.Empty<DiscoveredSmbHost>();}
    [Serializable] public sealed class SmbShare {public string name,comment;}
    [Serializable] public sealed class SmbShareResult {public bool ok;public string error,code;public SmbShare[] shares=Array.Empty<SmbShare>();}
    public interface IMediaBackend : IDisposable
    {
        MediaSnapshot Snapshot{get;}
        Texture Texture{get;}
        bool RequiresGammaConversion{get;}
        int RenderedFrames{get;}
        void Tick();void Open(string uri,long position,int generation);void Play();void Pause();void Stop();void Seek(long millis);
        void SetVolume(float value);void SetSpeed(float value);void SetRepeat(bool value);void SelectTrack(string id,int type);
    }
}
