using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace VRPlayer
{
    // Keep the complete script. libass owns ASS parsing, shaping, timing and effects.
    public sealed class AssDocument
    {
        public byte[] Utf8{get;private set;}
        public int EventCount{get;private set;}
        public string StreamId{get;private set;}
        internal static AssDocument FromEmbedded(string text,string stream)=>string.IsNullOrEmpty(text)?null:new AssDocument{Utf8=Encoding.UTF8.GetBytes(text),StreamId=stream};
        public static AssDocument Parse(string text)
        {
            byte[] bytes=Encoding.UTF8.GetBytes((text??"").TrimStart('\uFEFF'));
            if(bytes.Length>8*1024*1024)throw new ArgumentException("ASS 字幕超过 8 MiB 限制");
            int count=AssNative.AssValidate(bytes,bytes.Length);
            return count>0?new AssDocument{Utf8=bytes,EventCount=count}:null;
        }
    }

    public static class SubtitleFormat
    {
        public static string Extension(string value)
        {
            if(Uri.TryCreate(value,UriKind.Absolute,out var uri))value=uri.AbsolutePath;
            int dot=(value??"").LastIndexOf('.');
            return dot<0?"":value.Substring(dot).ToLowerInvariant();
        }
        public static bool IsAss(string value)=>Extension(value)==".ass"||Extension(value)==".ssa";
        public static bool IsSupported(string value)=>Extension(value)==".srt"||IsAss(value);
        public static string Label(string value)=>IsAss(value)?Extension(value).Substring(1).ToUpperInvariant():"SRT";
    }

    public static class AssPresentation
    {
        public static bool Supported(Projection projection)=>projection==Projection.Flat;
        public static long Timestamp(long position,double delay)=>Math.Max(-1,(long)Math.Round(position-delay*1000));
        // The raster follows the displayed eye aspect, including anamorphic and half stereo.
        // Bound both dimensions and total upload size even for extreme user aspect settings.
        public static Vector2Int FrameSize(float aspect)
        {
            aspect=Mathf.Clamp(aspect,.25f,8);
            float width=Mathf.Min(1920,1080*aspect),height=width/aspect;
            return new Vector2Int(Mathf.Max(1,Mathf.RoundToInt(width)),Mathf.Max(1,Mathf.RoundToInt(height)));
        }
    }

    internal static class AssNative
    {
        private const string Library="vrplayerass";
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int AssValidate(byte[] text,int length);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr AssCreate(byte[] text,int length,byte[] font,int fontLength);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int AssUpdate(IntPtr context,byte[] text,int length);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int AssRender(IntPtr context,long milliseconds,int width,int height,int force);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr AssPixels(IntPtr context);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void AssDestroy(IntPtr context);
    }
}
