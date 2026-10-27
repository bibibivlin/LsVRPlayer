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
        private TextMeshProUGUI status,time;
        private CancellationTokenSource pending;
        private long requested=-1;
        private int generation=-1;
        private float trackX,trackWidth,top;
        public bool HasImage=>image!=null&&image.enabled&&image.texture!=null;
        public long Position{get;private set;}
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        [Serializable] public sealed class Snapshot
        {
            public bool visible,hasImage;public int generation,width,height;public long position,key;public Rect eye;
        }
        public Snapshot Diagnostics
        {
            get{
                // Cached textures may already be destroyed after Stop; Unity objects require its null check.
                var texture=image!=null?image.texture:null;
                return new Snapshot{visible=gameObject.activeInHierarchy,hasImage=HasImage,generation=generation,position=Position,key=requested,width=texture!=null?texture.width:0,height=texture!=null?texture.height:0,eye=image!=null?image.uvRect:default};
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
            preview.status=UiFactory.Label(rect,"",20,12,304,180,24,TextAlignmentOptions.Center);
            preview.time=UiFactory.Label(rect,"",12,192,320,48,28,TextAlignmentOptions.Center);
            preview.Hide();return preview;
        }
        public void Show(float value)
        {
            var snapshot=app.Backend?.Snapshot;
            if(app.Current==null||snapshot==null||snapshot.generation!=app.Generation||snapshot.duration<=0||!app.HasFocus){Hide();return;}
            gameObject.SetActive(true);transform.SetAsLastSibling();
            var rect=(RectTransform)transform;float width=((RectTransform)transform.parent).rect.width;
            rect.anchoredPosition=new Vector2(Mathf.Clamp(trackX+Mathf.Clamp01(value)*trackWidth-172,8,width-352),-top);
            Position=SeekThumbnailCache.Position(value,snapshot.duration);
            var stamp=TimeSpan.FromMilliseconds(Position);time.text=stamp.TotalHours>=1?$"{(int)stamp.TotalHours}:{stamp.Minutes:00}:{stamp.Seconds:00}":$"{stamp.Minutes:00}:{stamp.Seconds:00}";
            long key=SeekThumbnailCache.Key(Position);
            if(generation==app.Generation&&requested==key){Fit();return;}
            Cancel();generation=app.Generation;requested=key;image.texture=null;image.enabled=false;
            status.text=Localization.T("正在加载预览…");status.gameObject.SetActive(true);
            pending=new CancellationTokenSource();_ = Load(key,generation,pending.Token);
        }
        private async Task Load(long key,int expectedGeneration,CancellationToken cancellation)
        {
            try{
                // Avoid decoding every intermediate position while a ray crosses the timeline.
                await Task.Delay(160,cancellation);
                var frame=await app.Thumbnails.Get(app.Backend as ISeekThumbnailSource,key,expectedGeneration,cancellation);
                if(cancellation.IsCancellationRequested||this==null||!gameObject.activeInHierarchy||app.Generation!=expectedGeneration||requested!=key)return;
                image.texture=frame;image.enabled=frame!=null;status.gameObject.SetActive(frame==null);
                status.text=Localization.T("预览暂不可用");Fit();
            }catch(OperationCanceledException){if(!cancellation.IsCancellationRequested&&this!=null)status.text=Localization.T("预览暂不可用");}
            catch(Exception){if(!cancellation.IsCancellationRequested&&this!=null&&generation==expectedGeneration){image.enabled=false;status.text=Localization.T("预览暂不可用");}}
        }
        public static Rect EyeRect(ViewSettings view)
        {
            if(view.stereo==StereoLayout.SideBySide)return new Rect(view.swapEyes?.5f:0,0,.5f,1);
            if(view.stereo==StereoLayout.TopBottom)return new Rect(0,view.swapEyes?0:.5f,1,.5f);
            return new Rect(0,0,1,1);
        }
        private void Fit()
        {
            if(!HasImage||app.Presenter==null)return;
            var view=app.Presenter.Settings;var snapshot=app.Backend.Snapshot;image.uvRect=EyeRect(view);
            float aspect=view.Aspect(snapshot.width,snapshot.height,snapshot.pixelRatio);
            if(view.projection!=Projection.Flat)aspect=image.texture.width*image.uvRect.width/(image.texture.height*image.uvRect.height);
            float width=Mathf.Min(ImageWidth,ImageHeight*aspect),height=width/aspect;
            image.rectTransform.sizeDelta=new Vector2(width,height);image.rectTransform.anchoredPosition=new Vector2(ImageInset+(ImageWidth-width)/2,-ImageInset-(ImageHeight-height)/2);
        }
        public void Hide(){Cancel();requested=-1;if(image!=null){image.texture=null;image.enabled=false;}gameObject.SetActive(false);}
        private void Cancel(){pending?.Cancel();pending?.Dispose();pending=null;}
        private void LateUpdate(){if(app.Generation!=generation||app.Current==null||!app.HasFocus)Hide();else Fit();}
        private void OnDisable(){Cancel();requested=-1;if(image!=null){image.texture=null;image.enabled=false;}}
        private void OnDestroy()=>Cancel();
    }
}
