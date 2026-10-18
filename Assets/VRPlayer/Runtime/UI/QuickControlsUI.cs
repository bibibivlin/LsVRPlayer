using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using L = VRPlayer.Localization;

namespace VRPlayer
{
    public sealed class QuickControlsUI : MonoBehaviour
    {
        public Canvas Canvas{get;private set;}
        public bool Visible=>Canvas!=null&&Canvas.gameObject.activeSelf;
        public string Popup{get;private set;}="";
        public bool VolumeVisible=>volumeExpansion!=null&&volumeExpansion.gameObject.activeSelf;
        public string BrowserUri=>browseUri;
        public int BrowserCount=>list?.Count??0;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public float BrowserOffset=>list?.Offset??0;
#endif
        public bool BrowserLoading{get;private set;}
        public bool BlocksTransport=>Visible&&(Popup!=""||seek.Dragging||volume.HasOwner||distanceSlider!=null&&distanceSlider.HasOwner||speedSlider!=null&&speedSlider.HasOwner);
        public bool Interacting=>Visible&&(region.Hovered||seek.Dragging||volume.HasOwner||distanceSlider!=null&&distanceSlider.HasOwner||speedSlider!=null&&speedSlider.HasOwner);
        private PlayerApp app;private PlayerUI owner;private Camera head;
        private RectTransform bar,popupRoot,popupContent,progress,volumeExpansion,previewLine,previewBadge,hoverBadge;
        private TextMeshProUGUI hoverText;
        private readonly Dictionary<int,(RectTransform anchor,string text)> hints=new Dictionary<int,(RectTransform,string)>();
        private TextMeshProUGUI nameLabel,positionLabel,durationLabel,previewTime,popupStatus,delayLabel,distanceLabel,speedLabel,subtitleToggle,subtitleSelection;
        private Button previous,next,play,subtitle,mode;
        private Button[] filmButtons;
        private QuickPointerRegion region,volumeRegion;
        private QuickSeekSurface seek;
        private OwnedSlider volume,distanceSlider,speedSlider;
        private readonly List<Button> projections=new List<Button>(),stereo=new List<Button>(),speeds=new List<Button>();
        private readonly float[] presets={.75f,1,1.25f,1.5f,2};
        private bool wasFlat;
        private int generation=-1,subtitleKey;
        private VirtualFileList list;
        private readonly FileLibrary library=new FileLibrary();
        private readonly List<(string uri,float? offset)> browseParents=new List<(string,float?)>();
        private readonly Dictionary<string,float> browseOffsets=new Dictionary<string,float>(StringComparer.Ordinal);
        private readonly Queue<string> browseOffsetOrder=new Queue<string>();
        private readonly Dictionary<string,string> browseDirectoryParents=new Dictionary<string,string>(StringComparer.Ordinal);
        private readonly Queue<string> browseDirectoryParentOrder=new Queue<string>();
        private CancellationTokenSource browse;
        private string browseUri="";
        private string browseOrigin="";
        private float browseOriginOffset;
        private bool browseRowsReady;
        private float lastInteraction;

        public void Initialize(PlayerUI controller,PlayerApp player,Camera camera)
        {
            owner=controller;app=player;head=camera;
            var root=new GameObject("Quick controls",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            Canvas=root.GetComponent<Canvas>();Canvas.renderMode=RenderMode.WorldSpace;Canvas.worldCamera=head;Canvas.sortingOrder=11;
            var rect=(RectTransform)root.transform;rect.sizeDelta=new Vector2(1440,1120);rect.localScale=Vector3.one*.00145f;
            region=root.AddComponent<QuickPointerRegion>();
            bar=QuickUiFactory.Box(rect,"Transport bar",0,880,1440,240,QuickUiFactory.Background);
            var glass=bar.GetComponent<RoundedPanelGraphic>();glass.Treatment=SurfaceTreatment.Transport;glass.border=1.2f;glass.shadow=0;bar.gameObject.AddComponent<Mask>().showMaskGraphic=true;
            progress=UiFactory.Box(bar,"Progress fill",0,0,0,240,Color.clear);
            var fade=UiFactory.Rect(bar,"Progress soft gradient",0,0,1440,240).gameObject.AddComponent<VerticalFadeGraphic>();fade.raycastTarget=false;
            positionLabel=UiFactory.Label(bar,"00:00",32,20,210,60,28);
            nameLabel=UiFactory.Label(bar,L.T("未打开影片"),285,20,870,60,28,TextAlignmentOptions.Center);
            durationLabel=UiFactory.Label(bar,"00:00",1198,20,210,60,28,TextAlignmentOptions.Right);
            var surface=UiFactory.Box(bar,"Seek surface",0,0,1440,110,Color.clear,true);
            seek=surface.gameObject.AddComponent<QuickSeekSurface>();seek.Initialize(app,Preview);
            subtitle=QuickUiFactory.IconButton(bar,"Subtitles",QuickIcon.Subtitle,36,124,()=>TogglePopup("subtitles"));
            var volumeRoot=UiFactory.Rect(bar,"Volume region",168,124,364,88);volumeRegion=volumeRoot.gameObject.AddComponent<QuickPointerRegion>();
            QuickUiFactory.IconButton(volumeRoot,"Volume",QuickIcon.Volume,0,0,app.ToggleMute);
            volumeExpansion=QuickUiFactory.Box(volumeRoot,"Volume expansion",88,0,276,88,Color.clear,true,0);
            volume=QuickUiFactory.Slider(volumeExpansion,"Volume slider",8,13,260,0,1,app.Store.Settings.volume,v=>{app.SetVolume(v);Touch();});
            volumeExpansion.gameObject.SetActive(false);
            previous=QuickUiFactory.IconButton(bar,"Previous",QuickIcon.Previous,532,124,()=>app.Next(-1));
            play=QuickUiFactory.IconButton(bar,"Play pause",QuickIcon.Play,676,124,app.TogglePlay);
            var playColors=play.colors;playColors.normalColor=UiTheme.Text;playColors.highlightedColor=Color.white;playColors.pressedColor=UiTheme.Accent;playColors.selectedColor=UiTheme.Text;playColors.disabledColor=UiTheme.Muted;UiFactory.SetColorsImmediate(play,playColors);
            next=QuickUiFactory.IconButton(bar,"Next",QuickIcon.Next,820,124,()=>app.Next(1));
            mode=QuickUiFactory.IconButton(bar,"Mode",QuickIcon.Mode,1180,124,()=>TogglePopup("mode"));
            QuickUiFactory.IconButton(bar,"Advanced",QuickIcon.Speed,1300,124,()=>TogglePopup("advanced"));
            filmButtons=new[]{previous,next,play,subtitle,mode};
            // The preview sits outside the bar's stencil and never intercepts a ray.
            previewLine=UiFactory.Rect(rect,"Seek preview line",0,856,18,164);var beam=previewLine.gameObject.AddComponent<SeekBeamGraphic>();beam.color=UiTheme.Accent;beam.raycastTarget=false;
            previewBadge=QuickUiFactory.Box(rect,"Seek preview time",0,780,184,66,UiTheme.Elevated,false,18);
            previewTime=UiFactory.Label(previewBadge,"",0,0,184,66,34,TextAlignmentOptions.Center);Preview(false,0);
            hoverBadge=QuickUiFactory.Box(rect,"Button tooltip",0,816,160,48,new Color(.035f,.04f,.05f,1),false,12);hoverText=UiFactory.Label(hoverBadge,"",0,0,160,48,24,TextAlignmentOptions.Center);hoverBadge.gameObject.SetActive(false);
            foreach(var button in bar.GetComponentsInChildren<Button>()){
                if(button.name=="Volume")continue;
                var anchor=(RectTransform)button.transform;string label=button.name=="Subtitles"?"字幕":button.name=="Previous"?"上一部":button.name=="Play pause"?"播放 / 暂停":button.name=="Next"?"下一部":button.name=="Mode"?"模式切换":"倍速";
                button.gameObject.AddComponent<QuickHoverLabel>().Change=(id,enter)=>Hint(id,enter,anchor,label);
            }
            var volumeAnchor=(RectTransform)volumeRoot.GetComponentInChildren<Button>().transform;volumeRoot.gameObject.AddComponent<QuickHoverLabel>().Change=(id,enter)=>Hint(id,enter,volumeAnchor,"音量");
            var close=QuickUiFactory.IconButton(rect,"Stop and open files",QuickIcon.Close,676,1144,owner.StopAndShowFiles);
            close.targetGraphic.GetComponent<RoundedPanelGraphic>().border=1.2f;var closeColors=close.colors;closeColors.normalColor=QuickUiFactory.Background;closeColors.selectedColor=QuickUiFactory.Background;closeColors.disabledColor=QuickUiFactory.Background;UiFactory.SetColorsImmediate(close,closeColors);
            close.gameObject.AddComponent<QuickHoverLabel>().Change=(id,enter)=>Hint(id,enter,(RectTransform)close.transform,"停止并返回");
            popupRoot=QuickUiFactory.Box(rect,"Quick popup",0,40,900,820,QuickUiFactory.Background);popupRoot.gameObject.SetActive(false);
            root.SetActive(false);
        }
        public void Show()
        {
            if(Canvas==null)return;Canvas.gameObject.SetActive(true);
            Vector3 offset=new Vector3(0,-.16f,2);
            Vector3 barCenter=Canvas.transform.InverseTransformPoint(bar.TransformPoint(bar.rect.center));
            float oldCenterHeight=offset.y+barCenter.y*Canvas.transform.localScale.y;
            offset.y-=oldCenterHeight/3;
            Canvas.transform.SetPositionAndRotation(head.transform.position+head.transform.rotation*offset,head.transform.rotation);
            generation=app.Generation;Touch();
        }
        public void Hide()
        {
            if(Canvas==null)return;ClosePopup();seek.Cancel();region.Clear();volumeRegion.Clear();hints.Clear();hoverBadge.gameObject.SetActive(false);volumeExpansion.gameObject.SetActive(false);Preview(false,0);Canvas.gameObject.SetActive(false);
        }
        public void RefreshLanguage()
        {
            seek?.Cancel();
            hints.Clear();RefreshHint();
            string popup=Popup;float offset=list?.Offset??0;
            if(popup=="files"){_ = Browse(browseUri,false,offset);}
            else if(popup!=""){Popup="";TogglePopup(popup);}
        }
        public void PlaybackOpened(){seek?.Cancel();ClosePopup();generation=app.Generation;Touch();}
        public bool TryBack()
        {
            if(!Visible||Popup=="")return false;
            if(Popup=="files"){
                if(browseParents.Count>0){
                    int last=browseParents.Count-1;var previous=browseParents[last];browseParents.RemoveAt(last);_ = Browse(previous.uri,false,previous.offset);
                }else{
                    string origin=browseOrigin;float offset=browseOriginOffset;
                    if(string.IsNullOrEmpty(origin))ClosePopup();else{TogglePopup(origin);list?.SetOffset(offset);}
                }
            }else ClosePopup();
            Touch();return true;
        }
        private void BrowseUp()
        {
            string parent=FileLibrary.Parent(browseUri);
            if(parent==null)browseDirectoryParents.TryGetValue(browseUri,out parent);
            _ = Browse(parent??"");
        }
        private void OpenBrowserDirectory(string uri,string parent)
        {
            if(uri.StartsWith("content:",StringComparison.OrdinalIgnoreCase)){
                if(!browseDirectoryParents.ContainsKey(uri)){
                    if(browseDirectoryParentOrder.Count>=64)browseDirectoryParents.Remove(browseDirectoryParentOrder.Dequeue());
                    browseDirectoryParentOrder.Enqueue(uri);
                }
                browseDirectoryParents[uri]=parent;
            }
            _ = Browse(uri);
        }
        public void Touch(){lastInteraction=Time.unscaledTime;owner.LastInteraction=lastInteraction;}
        public static bool ShouldAutoHide(bool enabled,bool playing,bool interacting,float now,float last)=>enabled&&playing&&!interacting&&now-last>=5;
        private void LateUpdate()
        {
            if(!Visible)return;
            if(generation!=app.Generation)PlaybackOpened();
            var snapshot=app.Backend?.Snapshot;bool current=app.Current!=null&&snapshot!=null&&snapshot.generation==app.Generation;
            nameLabel.text=app.Current?.name??L.T("未打开影片");positionLabel.text=Clock(current?snapshot.position:0);durationLabel.text=Clock(current?snapshot.duration:0);
            progress.sizeDelta=new Vector2(current&&snapshot.duration>0?1440*Mathf.Clamp01(snapshot.position/(float)snapshot.duration):0,240);bar.GetComponent<RoundedPanelGraphic>().Progress=progress.sizeDelta.x/1440;
            previous.interactable=app.CanNavigateAdjacent(-1);next.interactable=app.CanNavigateAdjacent(1);play.interactable=app.Current!=null;
            subtitle.interactable=mode.interactable=app.Current!=null;
            foreach(var button in filmButtons)button.GetComponentInChildren<QuickIconGraphic>().color=button.interactable?(button==play?UiTheme.Ink:UiTheme.Text):new Color(.5f,.62f,.68f,.65f);
            play.GetComponentInChildren<QuickIconGraphic>().Icon=current&&snapshot.playing?QuickIcon.Pause:QuickIcon.Play;
            volumeRegion.GetComponentInChildren<QuickIconGraphic>().Icon=app.Store.Settings.volume>0?QuickIcon.Volume:QuickIcon.Muted;
            volumeExpansion.gameObject.SetActive(volumeRegion.Hovered||volume.HasOwner);
            if(!volume.HasOwner)volume.SetValueWithoutNotify(app.Store.Settings.volume);
            if(Popup=="mode"){
                var view=app.Presenter.Settings;bool flat=view.projection==Projection.Flat;
                if(flat!=wasFlat)DrawMode();
                for(int i=0;i<projections.Count;i++)QuickUiFactory.Select(projections[i],(int)view.projection==i);
                for(int i=0;i<stereo.Count;i++)QuickUiFactory.Select(stereo[i],(int)view.stereo==i);
                if(distanceSlider!=null&&!distanceSlider.HasOwner)distanceSlider.SetValueWithoutNotify(flat?view.distance:view.panoramaZoom);
                distanceLabel.text=flat?L.F("{0:0.0} 米", view.distance):$"{view.panoramaZoom:0.00}×";
            }
            if(Popup=="advanced"){
                if(!speedSlider.HasOwner)speedSlider.SetValueWithoutNotify(app.Store.Settings.speed);
                speedLabel.text=$"{app.Store.Settings.speed:0.00}×";
                for(int i=0;i<speeds.Count;i++)QuickUiFactory.Select(speeds[i],Mathf.Abs(app.Store.Settings.speed-presets[i])<.001f);
            }
            if(Popup=="subtitles"){
                if(app.Current==null){ClosePopup();return;}
                int key=SubtitleKey();if(key!=subtitleKey)RefreshSubtitleRows();
                delayLabel.text=L.F("{0:+0.0;-0.0;0} 秒", app.Current?.subtitleDelay??0);
                subtitleSelection.text=SubtitleName();
            }
            if(popupStatus!=null)popupStatus.text=L.Message(app.Status);
            if(Interacting)Touch();
            if(ShouldAutoHide(app.Store.Settings.autoHide,current&&snapshot.playing,Interacting,Time.unscaledTime,lastInteraction))owner.HideQuick();
        }
        private static string Clock(long value){var t=TimeSpan.FromMilliseconds(Math.Max(0,value));return t.TotalHours>=1?$"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}":$"{t.Minutes:00}:{t.Seconds:00}";}
        private void Preview(bool visible,float value)
        {
            if(previewLine==null)return;previewLine.gameObject.SetActive(visible);previewBadge.gameObject.SetActive(visible);
            RefreshHint();
            previewLine.anchoredPosition=new Vector2(value*1440-9,-(880+(seek?.PreviewY??55)-79));previewBadge.anchoredPosition=new Vector2(Mathf.Clamp(value*1440-92,8,1248),-780);
            previewTime.text=Clock((long)(value*(app.Backend?.Snapshot.duration??0)));
        }
        private void Hint(int id,bool enter,RectTransform anchor,string text)
        {
            if(enter)hints[id]=(anchor,text);else hints.Remove(id);
            RefreshHint();
        }
        private void RefreshHint()
        {
            if(hoverBadge==null)return;hoverBadge.gameObject.SetActive(hints.Count>0&&!previewBadge.gameObject.activeSelf);if(hints.Count==0)return;
            var hint=hints.Values.First();Vector3 local=Canvas.transform.InverseTransformPoint(hint.anchor.TransformPoint(hint.anchor.rect.center));
            hoverText.text=L.T(hint.text);float width=Mathf.Clamp(hoverText.GetPreferredValues(hoverText.text).x+32,160,440);
            hoverBadge.sizeDelta=new Vector2(width,48);hoverText.rectTransform.sizeDelta=new Vector2(width,48);
            hoverBadge.anchoredPosition=new Vector2(Mathf.Clamp(local.x+720-width/2,8,1432-width),-816);
        }
        public void TogglePopup(string name)
        {
            if(!Visible)return;seek.Cancel();if(Popup==name){ClosePopup();return;}
            string origin=Popup;float originOffset=list?.Offset??0;ClosePopup();Popup=name;
            if(name=="files"){browseOrigin=origin;browseOriginOffset=originOffset;}
            Touch();popupRoot.gameObject.SetActive(true);
            popupRoot.anchoredPosition=new Vector2(name=="subtitles"||name=="files"?0:540,-40);
            if(name=="subtitles")DrawSubtitles();else if(name=="mode")DrawMode();else if(name=="advanced")DrawAdvanced();else if(name=="files")_ = Browse(app.Current?.directory??"");
        }
        public void ClosePopup()
        {
            browse?.Cancel();browse?.Dispose();browse=null;browseParents.Clear();browseOffsets.Clear();browseOffsetOrder.Clear();browseDirectoryParents.Clear();browseDirectoryParentOrder.Clear();browseRowsReady=false;browseUri=browseOrigin="";browseOriginOffset=0;BrowserLoading=false;Popup="";list=null;
            distanceSlider=speedSlider=null;popupStatus=delayLabel=distanceLabel=speedLabel=subtitleToggle=subtitleSelection=null;
            if(popupRoot!=null){popupRoot.gameObject.SetActive(false);UiFactory.Clear(popupRoot);}
        }
        private void Page(string title,Action reset=null)
        {
            UiFactory.Clear(popupRoot);list=null;distanceSlider=speedSlider=null;popupStatus=null;
            UiFactory.Label(popupRoot,title,100,20,700,66,32,TextAlignmentOptions.Center);
            if(reset!=null)QuickUiFactory.Button(popupRoot,L.T("重置"),744,28,120,50,()=>{reset();Touch();});
            popupContent=UiFactory.Rect(popupRoot,"Content",32,112,836,622);
            popupStatus=UiFactory.Label(popupRoot,"",32,750,836,48,21);popupStatus.color=new Color(.7f,.72f,.75f,1);
        }
        private int SubtitleKey()
        {
            unchecked{int key=app.Generation*31+(app.Current?.subtitle?.GetHashCode()??0);foreach(var item in app.SubtitleCandidates)key=key*31+(item.uri?.GetHashCode()??0);
                var state=app.Backend?.Snapshot;if(state!=null&&state.generation==app.Generation)foreach(var track in state.tracks??Array.Empty<MediaTrack>())if(track.type==3)key=key*31+(track.id?.GetHashCode()??0)+(track.supported?1:0);
                return key;}
        }
        private string SubtitleName()
        {
            if(app.Current==null)return L.T("未打开影片");if(app.Current.subtitleSelection==SubtitleSelection.Off)return L.T("字幕已关闭");
            if(app.AssSubtitlesSelected&&!AssPresentation.Supported(app.Presenter.Settings.projection))return L.T("ASS 仅支持平面视频，当前投影已暂停显示。");
            string uri=app.Current.subtitle;if(string.IsNullOrEmpty(uri))return L.T("尚未选择字幕");
            if(uri.StartsWith("track:"))return (app.Backend?.Snapshot.tracks??Array.Empty<MediaTrack>()).FirstOrDefault(t=>t.type==3&&"track:"+t.id==uri)?.name??L.T("内嵌字幕");
            try{return Uri.UnescapeDataString(uri.Substring(uri.LastIndexOf('/')+1));}catch{return L.T("外置字幕");}
        }
        private void DrawSubtitles()
        {
            if(app.Current==null){ClosePopup();return;}
            Page(L.T("字幕"));subtitleKey=SubtitleKey();
            var toggle=UiFactory.ToggleRow(popupContent,L.T("字幕：开"),L.T("字幕"),()=>app.Current?.subtitleSelection!=SubtitleSelection.Off,0,0,836,value=>{app.SetSubtitlesEnabled(value);Touch();});
            subtitleToggle=toggle.GetComponentInChildren<TextMeshProUGUI>();
            UiFactory.Label(popupContent,L.T("字幕进度调节"),0,88,330,60,26);
            QuickUiFactory.Button(popupContent,"−",470,88,72,60,()=>app.SetSubtitleDelay(app.Current.subtitleDelay-.5f));
            delayLabel=UiFactory.Label(popupContent,"",550,88,170,60,26,TextAlignmentOptions.Center);
            QuickUiFactory.Button(popupContent,"+",726,88,72,60,()=>app.SetSubtitleDelay(app.Current.subtitleDelay+.5f));
            QuickUiFactory.Button(popupContent,L.T("归零"),0,164,130,50,()=>app.SetSubtitleDelay(0));
            QuickUiFactory.Button(popupContent,L.T("选择字幕文件"),154,164,320,50,()=>TogglePopup("files"));
            QuickUiFactory.Button(popupContent,L.T("恢复自动选择"),498,164,338,50,()=>{app.RestoreAutomaticSubtitles();Touch();});
            subtitleSelection=UiFactory.Label(popupContent,"",0,232,836,48,24);
            var viewport=UiFactory.Rect(popupContent,"Subtitle list",0,298,836,314);list=viewport.gameObject.AddComponent<VirtualFileList>();list.Initialize(836,314,true);
            RefreshSubtitleRows();
        }
        private void RefreshSubtitleRows()
        {
            subtitleKey=SubtitleKey();var rows=new List<BrowserRow>();
            foreach(var item in app.SubtitleCandidates){var file=item;rows.Add(new BrowserRow{key=file.uri,label=file.name,selected=app.Current.subtitle==file.uri,detail=app.Current.subtitle==file.uri?L.T("已选择"):SubtitleFormat.Label(file.name),open=()=>{_ = app.LoadSubtitle(file.uri);Touch();}});}
            var state=app.Backend?.Snapshot;
            if(state!=null&&state.generation==app.Generation)foreach(var item in state.tracks??Array.Empty<MediaTrack>()){
                if(item.type!=3)continue;var track=item;rows.Add(new BrowserRow{key="track:"+track.id,label=track.name,selected=app.Current.subtitle=="track:"+track.id,detail=track.supported?L.T("内嵌"):L.T("不支持"),open=track.supported?()=>{app.EmbeddedSubtitle(track.id);Touch();}:null});
            }
            if(rows.Count==0)rows.Add(new BrowserRow{key="empty",label=L.T("未发现字幕，请选择 SRT / ASS 文件")});
            list.SetRows(rows);
        }
        private void DrawMode()
        {
            Page(L.T("模式切换"));var view=app.Presenter.Settings;wasFlat=view.projection==Projection.Flat;projections.Clear();stereo.Clear();
            UiFactory.Label(popupContent,L.T("全景角度"),0,0,836,44,26);string[] names={L.T("平面 / 曲面"),"VR180","VR360",L.T("鱼眼"),"Cubemap 3×2","EAC 3×2"};
            for(int i=0;i<names.Length;i++){int index=i;projections.Add(QuickUiFactory.Button(popupContent,names[i],i%3*282,56+i/3*104,272,92,()=>{app.ChangeFormat(v=>v.projection=(Projection)index);Touch();}));}
            UiFactory.Label(popupContent,wasFlat?L.T("画面远近"):L.T("画面远近 · 全景缩放"),0,280,524,48,26);
            distanceLabel=UiFactory.Label(popupContent,"",540,280,144,48,24,TextAlignmentOptions.Right);
            QuickUiFactory.Button(popupContent,L.T("重置远近"),700,280,136,48,()=>{app.ResetViewDistance();Touch();});
            distanceSlider=QuickUiFactory.Slider(popupContent,"Distance",0,344,836,wasFlat?1.2f:.5f,wasFlat?10:2,wasFlat?view.distance:view.panoramaZoom,v=>{if(wasFlat)app.SetViewDistance(v);else app.SetPanoramaZoom(v);Touch();},true);
            UiFactory.Label(popupContent,L.T("立体效果"),0,430,836,44,26);string[] layouts={"2D",L.T("3D 左右"),L.T("3D 上下")};
            for(int i=0;i<3;i++){int index=i;stereo.Add(QuickUiFactory.Button(popupContent,layouts[i],i*282,486,272,104,()=>{app.ChangeFormat(v=>v.stereo=(StereoLayout)index);Touch();}));}
        }
        private void DrawAdvanced()
        {
            Page(L.T("倍速"),()=>app.SetSpeed(1));speeds.Clear();
            UiFactory.Label(popupContent,L.T("倍速"),0,12,630,48,28);speedLabel=UiFactory.Label(popupContent,"",650,12,186,48,26,TextAlignmentOptions.Right);
            for(int i=0;i<presets.Length;i++){float value=presets[i];speeds.Add(QuickUiFactory.Button(popupContent,$"{value:0.##}×",i*168,86,164,82,()=>{app.SetSpeed(value);Touch();}));}
            QuickUiFactory.Button(popupContent,"−0.1",0,200,120,62,()=>{app.SetSpeed(app.Store.Settings.speed-.1f);Touch();});
            speedSlider=QuickUiFactory.Slider(popupContent,"Playback speed",136,200,564,.25f,3,app.Store.Settings.speed,v=>{app.SetSpeed(Mathf.Clamp(Mathf.Round(v*10)/10,.25f,3));Touch();},true);
            QuickUiFactory.Button(popupContent,"+0.1",716,200,120,62,()=>{app.SetSpeed(app.Store.Settings.speed+.1f);Touch();});
            UiFactory.Label(popupContent,L.T("调节范围 0.25–3×"),0,286,836,52,24,TextAlignmentOptions.Center);
        }
        public void Scroll(float value){list?.Scroll(value);Touch();}
        private List<BrowserRow> Roots()
        {
            var rows=new List<BrowserRow>();var entries=new List<FileEntry>();
            if(!string.IsNullOrEmpty(app.Current?.directory))entries.Add(new FileEntry{name=L.T("当前影片目录"),uri=app.Current.directory,directory=true});
#if UNITY_ANDROID && !UNITY_EDITOR
            entries.Add(new FileEntry{name=L.T("设备存储"),uri="file:///storage/emulated/0/",directory=true});
            entries.Add(new FileEntry{name=L.T("Movies 视频"),uri="file:///storage/emulated/0/Movies/",directory=true});
            try{
                using var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer");using var activity=unity.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver=activity.Call<AndroidJavaObject>("getContentResolver");using var permissions=resolver.Call<AndroidJavaObject>("getPersistedUriPermissions");
                for(int i=0;i<permissions.Call<int>("size");i++){using var permission=permissions.Call<AndroidJavaObject>("get",i);if(!permission.Call<bool>("isReadPermission"))continue;using var uri=permission.Call<AndroidJavaObject>("getUri");entries.Add(new FileEntry{name=L.T("已授权目录 ")+(i+1),uri=uri.Call<string>("toString"),directory=true});}
            }catch{app.SetStatus("已保存的目录授权无法读取，请检查系统权限");}
            try{var saved=JsonUtility.FromJson<ServerList>(AndroidBridge.Api.CallStatic<string>("listServers"));foreach(var server in saved.servers??Array.Empty<SmbServer>())entries.Add(new FileEntry{name=server.name,uri="smb://"+server.id+"/",directory=true});}catch{app.SetStatus("共享列表读取失败，可在完整面板重新配置");}
#else
            entries.Add(new FileEntry{name=L.T("测试视频"),uri=new Uri(Path.GetFullPath("TestMedia")).AbsoluteUri,directory=true});
            entries.Add(new FileEntry{name=L.T("我的视频"),uri=new Uri(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)).AbsoluteUri,directory=true});
#endif
            entries.AddRange(app.Store.Settings.favorites.Select(f=>new FileEntry{name=f.name,uri=f.uri,directory=true}));
            entries.Add(new FileEntry{name=L.T("应用目录"),uri=new Uri(Application.persistentDataPath).AbsoluteUri,directory=true});
            foreach(var item in entries.GroupBy(e=>e.uri).Select(g=>g.First())){var entry=item;rows.Add(new BrowserRow{key=entry.uri,label=entry.name,detail=L.T("打开"),open=()=>OpenBrowserDirectory(entry.uri,"")});}return rows;
        }
        public async Task Browse(string uri,bool push=true,float? restoreOffset=null)
        {
            if(Popup!="files"||!Visible)return;
            SaveBrowserOffset();uri=uri??"";
            browse?.Cancel();browse?.Dispose();browse=new CancellationTokenSource();var token=browse.Token;int capturedGeneration=app.Generation;
            var request=browse;
            if(push&&list!=null&&browseUri!=uri){
                if(browseParents.Count>=64)browseParents.RemoveAt(0);
                browseParents.Add((browseUri,browseRowsReady?list.Offset:(float?)null));
            }
            browseUri=uri;browseRowsReady=false;Page(L.T("选择字幕文件"));Touch();
            UiFactory.IconButton(popupContent,"返回字幕",QuickIcon.Back,0,0,()=>TogglePopup("subtitles"));
            UiFactory.IconButton(popupContent,"上级",QuickIcon.Up,80,0,BrowseUp);
            QuickUiFactory.Button(popupContent,L.T("来源"),160,0,596,64,()=>_ = Browse(""));
            UiFactory.IconButton(popupContent,"刷新",QuickIcon.Refresh,772,0,()=>_ = Browse(browseUri,false));
            UiFactory.Label(popupContent,string.IsNullOrEmpty(browseUri)?L.T("选择一个来源"):FileLibrary.DisplayAddress(browseUri),0,76,836,48,22);
            var viewport=UiFactory.Rect(popupContent,"Subtitle browser",0,142,836,472);list=viewport.gameObject.AddComponent<VirtualFileList>();list.Initialize(836,472,true);
            var requestList=list;
            BrowserLoading=false;if(string.IsNullOrEmpty(browseUri)){SetBrowserRows(requestList,Roots(),true,restoreOffset);return;}
            BrowserLoading=true;
            list.SetRows(new[]{new BrowserRow{key="loading",label=L.T("正在读取目录…")}},true);
            try{
                var result=await library.List(uri,token);
                if(token.IsCancellationRequested||browse!=request||list!=requestList||capturedGeneration!=app.Generation||Popup!="files"||!Visible)return;
                BrowserLoading=false;
                var rows=new List<BrowserRow>();
                foreach(var item in (result.entries??Array.Empty<FileEntry>()).Where(e=>e.directory||e.IsSubtitle).OrderByDescending(e=>e.directory).ThenBy(e=>e.name,StringComparer.OrdinalIgnoreCase)){
                    var entry=item;rows.Add(new BrowserRow{key=entry.uri,label=entry.name,detail=entry.directory?L.T("目录"):SubtitleFormat.Label(entry.name),open=()=>{if(entry.directory)OpenBrowserDirectory(entry.uri,uri);else{_ = app.LoadSubtitle(entry.uri);TogglePopup("subtitles");}}});
                }
                if(!result.ok||rows.Count==0)rows.Add(new BrowserRow{key="empty",label=result.ok?L.T("此目录没有 SRT / ASS 文件"):L.Message(result.error)??L.T("目录读取失败，请重试")});
                SetBrowserRows(requestList,rows,result.ok,restoreOffset);
            }catch(OperationCanceledException){}catch(Exception){if(!token.IsCancellationRequested&&browse==request&&list==requestList&&capturedGeneration==app.Generation&&Popup=="files"&&Visible){BrowserLoading=false;requestList.SetRows(new[]{new BrowserRow{key="error",label=L.T("目录读取失败，请刷新重试")}},true);}}
        }
        private void SaveBrowserOffset()
        {
            if(!browseRowsReady||list==null)return;
            if(!browseOffsets.ContainsKey(browseUri)){
                if(browseOffsetOrder.Count>=64)browseOffsets.Remove(browseOffsetOrder.Dequeue());
                browseOffsetOrder.Enqueue(browseUri);
            }
            browseOffsets[browseUri]=list.Offset;
        }
        private void SetBrowserRows(VirtualFileList target,IReadOnlyList<BrowserRow> rows,bool ready,float? restoreOffset=null)
        {
            target.SetRows(rows,true);browseRowsReady=ready;
            if(ready)target.SetOffset(restoreOffset??(browseOffsets.TryGetValue(browseUri,out var offset)?offset:0));
        }
        private void OnDestroy(){browse?.Cancel();browse?.Dispose();if(Canvas!=null)Destroy(Canvas.gameObject);}
    }
    public sealed class QuickSeekSurface : MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IDragHandler,IPointerEnterHandler,IPointerExitHandler,IPointerMoveHandler
    {
        private PlayerApp app;private Action<bool,float> preview;private int? owner,hoverOwner;private int generation;private long duration;private float value;
        public bool Dragging=>owner.HasValue;
        public float PreviewY{get;private set;}=55;
        public void Initialize(PlayerApp player,Action<bool,float> show){app=player;preview=show;}
        private bool Ready=>app?.Current!=null&&app.Backend?.Snapshot.generation==app.Generation&&app.Backend.Snapshot.duration>0;
        private void Position(PointerEventData data){if(RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,data.position,data.pressEventCamera??data.enterEventCamera,out var local)){Rect rect=((RectTransform)transform).rect;value=Mathf.Clamp01((local.x-rect.xMin)/rect.width);PreviewY=Mathf.Clamp(rect.yMax-local.y,0,rect.height);hoverOwner=data.pointerId;preview?.Invoke(true,value);}}
        public void OnPointerDown(PointerEventData data){if(!Ready||owner.HasValue||data.button!=PointerEventData.InputButton.Left)return;owner=data.pointerId;generation=app.Generation;duration=app.Backend.Snapshot.duration;Position(data);}
        public void OnDrag(PointerEventData data){if(owner==data.pointerId)Position(data);}
        public void OnPointerUp(PointerEventData data){if(owner!=data.pointerId)return;if(Ready&&generation==app.Generation){Position(data);app.Seek((long)(value*duration));}Cancel();}
        public void OnPointerEnter(PointerEventData data){if(Ready&&!Dragging)Position(data);else if(!Ready)Cancel();}
        public void OnPointerMove(PointerEventData data){if(Ready&&!Dragging)Position(data);else if(!Ready)Cancel();}
        public void OnPointerExit(PointerEventData data){if(!Dragging&&hoverOwner==data.pointerId){hoverOwner=null;preview?.Invoke(false,0);}}
        public void Cancel(){owner=hoverOwner=null;preview?.Invoke(false,0);}
        public void Cancel(int pointerId){if(owner==pointerId||!owner.HasValue&&hoverOwner==pointerId)Cancel();}
        private void OnDisable()=>Cancel();
    }
}
