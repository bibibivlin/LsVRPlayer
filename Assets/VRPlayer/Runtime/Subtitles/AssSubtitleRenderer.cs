using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace VRPlayer
{
    // Exactly one worker job is outstanding. Native contexts never cross workers
    // concurrently; Unity textures are created, uploaded and destroyed on main.
    public sealed class AssSubtitleRenderer : IDisposable
    {
        private sealed class Frame
        {
            public int revision,width,height;
            public long position;
            public byte[] pixels;
            public string error="";
        }
        private sealed class Session : IDisposable
        {
            private AssDocument loaded;
            private IntPtr handle;
            private byte[] pixels;
            public Frame Render(AssDocument document,byte[] font,int revision,long position,Vector2Int size,bool force)
            {
                var result=new Frame{revision=revision,position=position,width=size.x,height=size.y};
                try{
                    if(handle==IntPtr.Zero){handle=AssNative.AssCreate(document.Utf8,document.Utf8.Length,font,font.Length);loaded=document;}
                    if(handle==IntPtr.Zero)throw new InvalidOperationException();
                    if(!ReferenceEquals(loaded,document)){
                        if(AssNative.AssUpdate(handle,document.Utf8,document.Utf8.Length)==0)throw new InvalidOperationException();
                        loaded=document;force=true;
                    }
                    int changed=AssNative.AssRender(handle,position,size.x,size.y,force?1:0);
                    if(changed<0)throw new InvalidOperationException();
                    if(changed>0){
                        int length=size.x*size.y*4;
                        if(pixels==null||pixels.Length!=length)pixels=new byte[length];
                        Marshal.Copy(AssNative.AssPixels(handle),pixels,0,length);result.pixels=pixels;
                    }
                }catch(Exception){result.error="ASS 渲染失败，请重新选择字幕";}
                return result;
            }
            public void Dispose(){if(handle!=IntPtr.Zero){AssNative.AssDestroy(handle);handle=IntPtr.Zero;}}
        }

        private AssDocument document;
        private Session session;
        private Task<Frame> pending;
        private Task<byte[]> fontTask;
        private Texture2D texture;
        private int revision;
        private bool visible,disposed,forceFrame;
        private float renderAt;
        private long lastPosition=long.MinValue;
        private Vector2Int lastSize;
        public Texture Texture=>visible?texture:null;
        public int UploadedFrames{get;private set;}
        public long RenderedPosition{get;private set;}
        public string Error{get;private set;}="";

        private static async Task<byte[]> LoadFont()
        {
            string path=Application.streamingAssetsPath+"/AssFonts/NotoSansCJKsc-Regular.otf";
#if UNITY_ANDROID && !UNITY_EDITOR
            using var request=UnityWebRequest.Get(path);var operation=request.SendWebRequest();
            while(!operation.isDone)await Task.Yield();
            if(request.result!=UnityWebRequest.Result.Success)throw new IOException("ASS font unavailable");
            return request.downloadHandler.data;
#else
            return await Task.Run(()=>File.ReadAllBytes(path));
#endif
        }

        public void Invalidate()
        {
            revision++;forceFrame=true;lastPosition=long.MinValue;
            if(texture!=null){UnityEngine.Object.Destroy(texture);texture=null;}
        }

        public void SetDocument(AssDocument value)
        {
            if(ReferenceEquals(value,document))return;
            if(value!=null&&document!=null&&!string.IsNullOrEmpty(value.StreamId)&&value.StreamId==document.StreamId){
                document=value;revision++;forceFrame=true;return;
            }
            document=value;Invalidate();Error="";
            var old=session;session=value==null?null:new Session();
            // A retired session is destroyed only after its last native call completes.
            if(old!=null){if(pending!=null)pending.ContinueWith(_=>old.Dispose(),TaskScheduler.Default);else Task.Run(()=>old.Dispose());}
            if(value!=null&&fontTask==null)fontTask=LoadFont();
        }

        public void Tick(AssDocument value,bool enabled,long mediaPosition,double delay,float aspect)
        {
            if(disposed)return;SetDocument(value);
            if(visible!=enabled){visible=enabled;Invalidate();}
            Vector2Int size=AssPresentation.FrameSize(aspect);
            long position=AssPresentation.Timestamp(mediaPosition,delay);
            if(pending!=null&&pending.IsCompleted){
                var frame=pending.GetAwaiter().GetResult();pending=null;
                if(frame.revision==revision&&visible&&frame.width==size.x&&frame.height==size.y&&Math.Abs(frame.position-position)<250){
                    if(frame.error.Length>0)Error=frame.error;
                    else if(frame.pixels!=null){
                        if(texture==null||texture.width!=size.x||texture.height!=size.y){
                            if(texture!=null)UnityEngine.Object.Destroy(texture);
                            // Raw sRGB premultiplied pixels; shader converts after unpremultiplying.
                            texture=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false,true){name="ASS video overlay",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
                        }
                        texture.LoadRawTextureData(frame.pixels);texture.Apply(false,false);
                        UploadedFrames++;RenderedPosition=frame.position;
                    }
                }else forceFrame=true;
            }
            if(document==null||!visible||pending!=null||Error.Length>0)return;
            if(fontTask==null||!fontTask.IsCompleted)return;
            if(fontTask.IsFaulted){Error="ASS 字体读取失败，请检查安装";return;}
            if(lastPosition==position&&lastSize==size&&!forceFrame)return;
            if(Time.unscaledTime<renderAt&&!forceFrame)return;
            renderAt=Time.unscaledTime+1f/30;lastPosition=position;lastSize=size;
            var owner=session;int requestRevision=revision;bool refresh=forceFrame;forceFrame=false;
            var requestDocument=document;
            pending=Task.Run(()=>owner.Render(requestDocument,fontTask.Result,requestRevision,position,size,refresh));
        }

        public void Dispose()
        {
            if(disposed)return;disposed=true;SetDocument(null);
        }
    }
}
