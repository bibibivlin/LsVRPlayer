using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRPlayer
{
    public sealed class SeekThumbnailPreview : MonoBehaviour
    {
        private const float OuterRadius=30,ImageInset=12,ImageWidth=320,ImageHeight=180;
        private const float ImageRadius=OuterRadius-ImageInset;
        private PlayerApp app;
        private RawImage image;
        private TextMeshProUGUI status,time,frameTime;
        private RectTransform frameBadge;
        private readonly SeekThumbnailTarget target=new SeekThumbnailTarget();
        private CancellationTokenSource pending;
        private long requested=-1,displayed=-1;
        private int generation=-1,displayRevision;
        private float trackX,trackWidth,top,cursorValue;
        public bool HasImage=>image!=null&&image.enabled&&image.texture!=null;
        public long Position{get;private set;}
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        [Serializable] public sealed class Snapshot
        {
            public bool visible,hasImage,loading,retainedImage;public int generation,width,height;public long position,key,imageKey;public Rect eye;public string status;
        }
        public Snapshot Diagnostics
        {
            get{
                // Cached textures may already be destroyed after Stop; Unity objects require its null check.
                var texture=image!=null?image.texture:null;
                return new Snapshot{visible=gameObject.activeInHierarchy,hasImage=HasImage,loading=pending!=null,retainedImage=HasImage&&displayed!=SeekThumbnailCache.Key(Position),status=status.text,generation=generation,position=Position,key=requested,imageKey=displayed,width=texture!=null?texture.width:0,height=texture!=null?texture.height:0,eye=image!=null?image.uvRect:default};
            }
        }
#endif
        public static SeekThumbnailPreview Create(RectTransform parent,PlayerApp app,float trackX,float trackWidth,float top)
        {
            var rect=UiFactory.RoundedBox(parent,"Seek thumbnail",0,top,344,244,UiTheme.Elevated,false,OuterRadius);
            var preview=rect.gameObject.AddComponent<SeekThumbnailPreview>();preview.app=app;preview.trackX=trackX;preview.trackWidth=trackWidth;preview.top=top;
            // The inset picture shares the outer card's top corner centers: outer radius = inner radius + inset.
            UiFactory.RoundedBox(rect,"Image backdrop",ImageInset,ImageInset,ImageWidth,ImageHeight,Color.black,false,ImageRadius);
            var frame=UiFactory.Rect(rect,"Frame",ImageInset,ImageInset,ImageWidth,ImageHeight).gameObject.AddComponent<RoundedRawImage>();
            frame.Radius=ImageRadius;frame.raycastTarget=false;preview.image=frame;
            preview.frameBadge=UiFactory.RoundedBox(frame.transform,"Preview frame time",6,144,196,30,new Color(0,0,0,.8f),false,6);
            preview.frameTime=UiFactory.Label(preview.frameBadge,"",8,0,180,30,18);
            preview.frameTime.enableAutoSizing=true;preview.frameTime.fontSizeMin=10;preview.frameTime.fontSizeMax=18;
            preview.status=UiFactory.Label(rect,"",20,12,304,180,24,TextAlignmentOptions.Center);
            preview.time=UiFactory.Label(rect,"",12,192,320,48,28,TextAlignmentOptions.Center);
            preview.Hide();return preview;
        }
        public void Show(float value)
        {
            var snapshot=app.Backend?.Snapshot;
            if(app.Current==null||snapshot==null||snapshot.generation!=app.Generation||snapshot.duration<=0||!app.HasFocus){Hide();return;}
            if(generation!=app.Generation){Cancel();ResetPreview();generation=app.Generation;}
            gameObject.SetActive(true);transform.SetAsLastSibling();
            cursorValue=Mathf.Clamp01(value);
            var rect=(RectTransform)transform;float width=((RectTransform)transform.parent).rect.width;
            rect.anchoredPosition=new Vector2(Mathf.Clamp(trackX+cursorValue*trackWidth-172,8,width-352),-top);
            Position=SeekThumbnailCache.Position(cursorValue,snapshot.duration);time.text=Stamp(Position);
            UpdateTarget();Fit();
        }
        private static string Stamp(long position)
        {
            var stamp=TimeSpan.FromMilliseconds(position);return stamp.TotalHours>=1?$"{(int)stamp.TotalHours}:{stamp.Minutes:00}:{stamp.Seconds:00}":$"{stamp.Minutes:00}:{stamp.Seconds:00}";
        }
        private void UpdateTarget()
        {
            if(!target.Update(cursorValue,trackWidth,Time.realtimeSinceStartupAsDouble))return;
            long key=SeekThumbnailCache.Key(SeekThumbnailCache.Position(target.Value,app.Backend.Snapshot.duration));
            if(requested==key)return;requested=key;
            if(app.Thumbnails.TryGet(key,out var cached)){Display(cached,key);return;}
            // Keep the last complete picture while its replacement is decoded.
            status.text=Localization.T("正在加载预览…");status.gameObject.SetActive(!HasImage);
            // Moving the ray updates one pending target. Do not restart an expensive network/index read.
            if(pending==null){pending=new CancellationTokenSource();_ = Load(generation,pending);}
        }
        private async Task Load(int expectedGeneration,CancellationTokenSource request)
        {
            var cancellation=request.Token;
            try{
                while(true){
                    // Fixed delay coalesces motion without requiring the controller to hold perfectly still.
                    await Task.Delay(160,cancellation);long key=requested;int revision=displayRevision;Texture2D frame=null;
                    if(HasImage)app.Thumbnails.TryGet(displayed,out _); // Keep the retained picture alive during long scrubs.
                    try{frame=await app.Thumbnails.Get(app.Backend as ISeekThumbnailSource,key,expectedGeneration,cancellation);}
                    catch(Exception){} // A cancelled native read may finish with an I/O error instead of cancellation.
                    if(cancellation.IsCancellationRequested||this==null||!gameObject.activeInHierarchy||app.Generation!=expectedGeneration)return;
                    if(requested==key){Display(frame,key);return;}
                    // Show completed samples progressively, without replacing a newer cache hit.
                    // A separate stamp identifies the retained picture; the seek time remains exact.
                    if(frame!=null&&revision==displayRevision)Display(frame,key);
                    if(app.Thumbnails.TryGet(requested,out var cached)){Display(cached,requested);return;}
                }
            }catch(OperationCanceledException){}
            finally{if(ReferenceEquals(pending,request)){pending=null;request.Dispose();}}
        }
        private void Display(Texture2D frame,long key)
        {
            if(frame!=null){image.texture=frame;image.enabled=true;displayed=key;displayRevision++;}
            status.gameObject.SetActive(!HasImage);
            status.text=frame==null?Localization.T("预览暂不可用"):displayed==requested?"":Localization.T("正在加载预览…");Fit();
        }
        public static Rect EyeRect(ViewSettings view)
        {
            if(view.stereo==StereoLayout.SideBySide)return new Rect(view.swapEyes?.5f:0,0,.5f,1);
            if(view.stereo==StereoLayout.TopBottom)return new Rect(0,view.swapEyes?0:.5f,1,.5f);
            return new Rect(0,0,1,1);
        }
        private void Fit()
        {
            bool retained=HasImage&&displayed!=SeekThumbnailCache.Key(Position);
            frameBadge.gameObject.SetActive(retained);
            if(retained)frameTime.text=string.Format(Localization.T("预览 {0}"),Stamp(displayed));
            if(!HasImage||app.Presenter==null)return;
            var view=app.Presenter.Settings;var snapshot=app.Backend.Snapshot;image.uvRect=EyeRect(view);
            float aspect=view.Aspect(snapshot.width,snapshot.height,snapshot.pixelRatio);
            if(view.projection!=Projection.Flat)aspect=image.texture.width*image.uvRect.width/(image.texture.height*image.uvRect.height);
            float width=Mathf.Min(ImageWidth,ImageHeight*aspect),height=width/aspect;
            image.rectTransform.sizeDelta=new Vector2(width,height);image.rectTransform.anchoredPosition=new Vector2(ImageInset+(ImageWidth-width)/2,-ImageInset-(ImageHeight-height)/2);
            float badgeWidth=Mathf.Max(0,Mathf.Min(196,width-12));frameBadge.sizeDelta=new Vector2(badgeWidth,30);
            frameBadge.anchoredPosition=new Vector2(6,-Mathf.Max(0,height-36));frameTime.rectTransform.sizeDelta=new Vector2(Mathf.Max(0,badgeWidth-16),30);
        }
        private void ResetPreview(){target.Reset();requested=displayed=-1;if(image!=null){image.texture=null;image.enabled=false;}if(frameBadge!=null)frameBadge.gameObject.SetActive(false);}
        public void Hide(){Cancel();ResetPreview();gameObject.SetActive(false);}
        private void Cancel(){pending?.Cancel();pending?.Dispose();pending=null;}
        private void LateUpdate(){if(app.Generation!=generation||app.Current==null||!app.HasFocus)Hide();else{UpdateTarget();Fit();}}
        private void OnDisable(){Cancel();ResetPreview();}
        private void OnDestroy()=>Cancel();
    }
}
