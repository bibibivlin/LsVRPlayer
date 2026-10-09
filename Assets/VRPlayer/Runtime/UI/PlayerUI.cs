using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRPlayer
{
    public sealed class PlayerUI : MonoBehaviour
    {
        public Canvas Canvas{get;private set;}
        public bool Visible=>Canvas!=null&&Canvas.gameObject.activeSelf;
        public QuickControlsUI Quick{get;private set;}
        public bool AnyVisible=>Visible||Quick!=null&&Quick.Visible;
        public Canvas ActiveCanvas=>Visible?Canvas:Quick!=null&&Quick.Visible?Quick.Canvas:null;
        public int SurfaceRevision{get;private set;}
        public float LastInteraction{get;set;}
        private PlayerApp app;private Camera head;
        private RectTransform content,keyboard;
        private TextMeshProUGUI title,status,time,playLabel,detail,keyboardPreview;
        private Slider seek;private SeekGesture seekGesture;
        private Slider volumeSlider,fullSpeedSlider;private TextMeshProUGUI fullSpeedNumber;
        private TMP_InputField address,filter,keyboardTarget;
        private string tab="文件",query="";
        private int sort,subtitlePage;
        private bool subtitleBrowse,shift;
        private readonly Dictionary<string,Button> tabs=new Dictionary<string,Button>();
        private VirtualFileList fileList;
        private string fileKey="",modal="";
        private float fileOffset,undoUntil;
        private Favorite removedFavorite;private int removedIndex;
        private TextMeshProUGUI countLabel,scanLabel;
        private Button undoButton;
        private CaptionPresenter preview;
        private RectTransform transportRoot;
        private TextMeshProUGUI transportLabel,formatLabel;
        private float transportUntil;
        private string locatedFile="";
        private readonly List<Button> projectionButtons=new List<Button>(),stereoButtons=new List<Button>();
        private Button halfButton,eyesButton;
        private int projectionButtonState=-1;
        private bool projectionDistanceWasFlat;
        public bool BlocksTransport=>Visible&&(modal!=""||(keyboard!=null&&keyboard.gameObject.activeSelf)||(seekGesture!=null&&seekGesture.Dragging))||Quick!=null&&Quick.BlocksTransport;
        public bool BlocksScrolling=>Visible&&(keyboard!=null&&keyboard.gameObject.activeSelf||modal!="");
        private CancellationTokenSource connection;
        public bool CanDiscover=>Visible&&tab=="文件"&&modal==""&&!keyboard.gameObject.activeSelf;
        public string SelectedTab=>tab;
        public void Initialize(PlayerApp owner,Camera camera)
        {
            app=owner;head=camera;
            var root=new GameObject("Player UI",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));Canvas=root.GetComponent<Canvas>();Canvas.renderMode=RenderMode.WorldSpace;Canvas.worldCamera=camera;Canvas.sortingOrder=10;
            var rect=(RectTransform)root.transform;rect.sizeDelta=new Vector2(1440,1040);rect.localScale=Vector3.one*.00145f;
            UiFactory.Box(rect,"Background",0,0,1440,1040,UiFactory.Background,true);
            UiFactory.Label(rect,"L’s VRPlayer",32,20,490,65,42);
            title=UiFactory.Label(rect,"选择影片开始观看",32,85,1376,45,26);
            string[] names={"文件","投影","字幕","设置","信息"};for(int i=0;i<names.Length;i++){string name=names[i];tabs[name]=UiFactory.Button(rect,name,32+i*158,145,142,50,()=>SelectTab(name));}
            UiFactory.Button(rect,"正前方",1060,145,160,50,()=>app.Recenter(false));UiFactory.Button(rect,"隐藏面板",1236,145,172,50,Hide);
            content=UiFactory.Rect(rect,"Page",32,212,1376,610);
            status=UiFactory.Label(rect,"",32,827,1376,40,23);status.color=UiFactory.Muted;
            seek=UiFactory.Slider(rect,32,885,1050,0,1,0,_=>{});seekGesture=seek.gameObject.AddComponent<SeekGesture>();seekGesture.Commit=value=>app.Seek((long)(value*(app.Backend?.Snapshot.duration??0)));
            time=UiFactory.Label(rect,"00:00 / 00:00",1102,881,310,45,23,TextAlignmentOptions.Right);
            UiFactory.Button(rect,"上一部",32,953,112,54,()=>app.Next(-1));
            var play=UiFactory.Button(rect,"播放",156,953,128,54,()=>app.TogglePlay(),true);playLabel=play.GetComponentInChildren<TextMeshProUGUI>();
            UiFactory.Button(rect,"下一部",296,953,112,54,()=>app.Next(1));UiFactory.Button(rect,"−10 秒",424,953,112,54,()=>app.Skip(-10));UiFactory.Button(rect,"+10 秒",548,953,112,54,()=>app.Skip(10));
            UiFactory.Label(rect,"音量",690,953,68,54,23);volumeSlider=UiFactory.Slider(rect,765,960,225,0,1,app.Store.Settings.volume,app.SetVolume);
            UiFactory.Button(rect,"重试",1014,953,112,54,app.Retry);UiFactory.Button(rect,"停止",1138,953,112,54,app.Stop);UiFactory.Button(rect,"躺卧",1262,953,146,54,()=>app.Recenter(true));
            keyboard=UiFactory.Box(rect,"Virtual keyboard",30,340,1380,485,new Color(.025f,.065f,.09f,1),true);keyboard.gameObject.SetActive(false);
            var hud=new GameObject("Transport feedback",typeof(RectTransform),typeof(Canvas));transportRoot=(RectTransform)hud.transform;transportRoot.sizeDelta=new Vector2(700,70);transportRoot.localScale=Vector3.one*.001f;hud.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;hud.GetComponent<Canvas>().sortingOrder=12;
            UiFactory.Box(transportRoot,"Backdrop",0,0,700,70,UiFactory.Background);transportLabel=UiFactory.Label(transportRoot,"",0,0,700,70,30);hud.SetActive(false);
            Quick=gameObject.AddComponent<QuickControlsUI>();Quick.Initialize(this,app,head);
            app.Changed+=OnAppChanged;app.DiscoveryChanged+=RefreshFileRows;Draw();Show();
        }
        private string CurrentFileKey=>$"{app.Source}|{app.DirectoryUri}|{query}|{sort}|{subtitleBrowse}";
        private void OnAppChanged(){if(modal!=""||keyboard.gameObject.activeSelf)return;if(tab=="文件"){if(fileList!=null&&fileKey==CurrentFileKey)RefreshFileRows();else Draw();}else if(tab=="字幕"&&subtitlePage==0)Draw();}
        public void SelectTab(string name)
        {
            if(!tabs.ContainsKey(name))return;
            CancelSmbConnection();app.StopDiscovery();CloseKeyboard();modal="";tab=name;Draw();
            if(CanDiscover&&app.Source==FileSource.Smb)app.StartDiscovery();
        }
        public void PrepareDirectoryNavigation(){query="";subtitleBrowse=false;fileOffset=0;fileKey="";locatedFile="";SelectTab("文件");}
        public void ShowTransport(string text){transportLabel.text=text;transportUntil=Time.unscaledTime+1.6f;transportRoot.gameObject.SetActive(true);}
        private void Update()
        {
            if(Canvas==null)return;var snapshot=app.Backend?.Snapshot;
            if(volumeSlider!=null&&!(volumeSlider as OwnedSlider).HasOwner)volumeSlider.SetValueWithoutNotify(app.Store.Settings.volume);
            if(fullSpeedSlider!=null&&!(fullSpeedSlider as OwnedSlider).HasOwner){fullSpeedSlider.SetValueWithoutNotify(app.Store.Settings.speed);fullSpeedNumber.text=$"{app.Store.Settings.speed:0.0#} 倍";}
            if(transportRoot!=null){transportRoot.gameObject.SetActive(Time.unscaledTime<transportUntil);transportRoot.SetPositionAndRotation(head.transform.position+head.transform.rotation*new Vector3(-.35f,-.35f,2),head.transform.rotation);}
            if(formatLabel!=null)formatLabel.text=app.FormatDescription;
            if(tab=="投影"&&modal==""&&halfButton!=null){
                var view=app.Presenter.Settings;
                if((view.projection==Projection.Flat)!=projectionDistanceWasFlat)DrawProjectionPage();
                int state=(int)view.projection|((int)view.stereo<<3)|(view.half?32:0)|(view.swapEyes?64:0);
                if(state!=projectionButtonState){
                    projectionButtonState=state;
                    for(int i=0;i<projectionButtons.Count;i++)UiFactory.SetAccent(projectionButtons[i],(int)view.projection==i);
                    for(int i=0;i<stereoButtons.Count;i++)UiFactory.SetAccent(stereoButtons[i],(int)view.stereo==i);
                    halfButton.GetComponentInChildren<TextMeshProUGUI>().text=view.half?"Half 半宽/半高":"Full 全宽/全高";UiFactory.SetAccent(halfButton,view.half);
                    eyesButton.GetComponentInChildren<TextMeshProUGUI>().text=view.swapEyes?"已交换左右眼":"交换左右眼";UiFactory.SetAccent(eyesButton,view.swapEyes);
                }
            }
            if(undoButton!=null)undoButton.gameObject.SetActive(removedFavorite!=null&&Time.unscaledTime<undoUntil);
            if(preview!=null)preview.SetText("字幕样式实时预览\n明亮与暗色场景 · Subtitle preview",32,app.Store.Settings.subtitleStyle);
            title.text=app.Current?.name??"选择影片开始观看";status.text=app.Status;
            if(snapshot!=null){playLabel.text=snapshot.playing?"暂停":"播放";if(!seekGesture.Dragging)seek.SetValueWithoutNotify(snapshot.duration>0?snapshot.position/(float)snapshot.duration:0);time.text=Clock(snapshot.position)+" / "+Clock(snapshot.duration);}
            if(detail!=null&&tab=="信息")detail.text=snapshot==null?"播放内核未就绪":PlaybackDetails(snapshot);
            if(Visible&&app.Store.Settings.autoHide&&snapshot!=null&&snapshot.playing&&!keyboard.gameObject.activeSelf&&Time.unscaledTime-LastInteraction>18)Hide();
        }
        private static string Clock(long milliseconds){var duration=TimeSpan.FromMilliseconds(Math.Max(0,milliseconds));return duration.TotalHours>=1?$"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}":$"{duration.Minutes:00}:{duration.Seconds:00}";}
        private string PlaybackDetails(MediaSnapshot snapshot)
        {
            string network="";var reads=snapshot.network;
            if(reads!=null&&reads.generation==snapshot.generation&&reads.requests>0)
                network=$"\nSMB：{reads.mbps:0.00} Mbps    请求：{reads.requests}    平均请求：{reads.averageRequestBytes/1024:0.0} KiB"+
                    $"\n读取耗时：平均 {reads.averageReadMs:0.0} ms / P95 {reads.readP95Ms:0.0} ms    网络错误：{reads.errors}";
            return $"播放内核：{snapshot.decoder}\n视频：{snapshot.codec}  {snapshot.width} × {snapshot.height}  {snapshot.fps:0.##} fps"+
                $"\n码率：{(snapshot.bitrate>0?snapshot.bitrate/1000000f:0):0.##} Mbps    已缓冲到：{Clock(snapshot.buffered)}"+
                $"\n剩余缓冲：{snapshot.bufferAheadMs/1000d:0.0} 秒    正在加载：{(snapshot.loading?"是":"否")}    缓冲内存：{snapshot.allocatedBufferBytes/1048576d:0.0} MiB"+
                $"\n意外缓冲：{snapshot.rebufferCount} 次 / {snapshot.rebufferMs/1000d:0.00} 秒"+network+
                $"\n解码丢帧：{snapshot.dropped}    GPU 更新帧：{app.Backend.RenderedFrames}"+
                $"\n投影：{app.Presenter.Settings.projection}    立体：{app.Presenter.Settings.stereo}\n状态：{snapshot.state}    错误：{snapshot.error}"+
                $"\n{app.RenderQuality.Description}\n版本：{Application.version}    图形 API：{SystemInfo.graphicsDeviceType}"+
                "\n\n格式支持以当前设备解码器与样片实测为准。\nEAC / Cubemap 布局：上排 +X −X +Y，下排 −Y +Z −Z。";
        }
        public void Show(){if(Canvas==null)return;bool opening=!Visible;Quick?.Hide();SurfaceRevision++;Canvas.gameObject.SetActive(true);if(opening)Draw();LastInteraction=Time.unscaledTime;Canvas.transform.position=head.transform.position+head.transform.rotation*new Vector3(0,-.05f,2);Canvas.transform.rotation=head.transform.rotation;if(opening&&CanDiscover&&app.Source==FileSource.Smb)app.StartDiscovery();}
        public void Hide(){CloseKeyboard();CancelSmbConnection();app.StopDiscovery();Quick?.Hide();seekGesture?.Cancel();if(Canvas!=null)Canvas.gameObject.SetActive(false);SurfaceRevision++;app?.Store?.Save();}
        public void Toggle(){if(Visible)Hide();else Show();}
        public void ShowQuick(){if(Quick==null||Visible)return;Quick.Show();SurfaceRevision++;}
        public void HideQuick(){Quick?.Hide();SurfaceRevision++;app?.Store?.Save();}
        public void ToggleQuick(){if(Visible)return;if(Quick!=null&&Quick.Visible)HideQuick();else ShowQuick();}
        public void PlaybackOpened(){SurfaceRevision++;seekGesture?.Cancel();Quick?.PlaybackOpened();LastInteraction=Time.unscaledTime;}
        private void Draw()
        {
            if(content==null)return;if(fileList!=null)fileOffset=fileList.Offset;UiFactory.Clear(content);detail=null;formatLabel=null;address=filter=null;fileList=null;preview=null;scanLabel=countLabel=null;undoButton=null;fullSpeedSlider=null;fullSpeedNumber=null;modal="";
            foreach(var pair in tabs)UiFactory.SetAccent(pair.Value,pair.Key==tab);
            if(tab=="文件")DrawFiles();else if(tab=="投影")DrawProjection();else if(tab=="字幕")DrawSubtitles();else if(tab=="设置")DrawSettings();else DrawInfo();
        }
        private void DrawFiles()
        {
            UiFactory.Button(content,"本地",0,0,116,44,()=>ChangeSource(app.Home));UiFactory.Button(content,"SMB",128,0,116,44,()=>ChangeSource(app.ShowServers));
            UiFactory.Button(content,"收藏",256,0,116,44,()=>ChangeSource(app.ShowFavorites));UiFactory.Button(content,"最近",384,0,116,44,()=>ChangeSource(app.ShowRecent));
            UiFactory.Button(content,"存储权限",512,0,160,44,app.RequestStorage);UiFactory.Button(content,"授权目录",684,0,160,44,app.PickFolder);UiFactory.Button(content,"添加 SMB",856,0,176,44,DrawServerForm);
            UiFactory.Button(content,"收藏此目录",1044,0,200,44,()=>{if(!string.IsNullOrEmpty(app.DirectoryUri))app.AddFavorite(Uri.UnescapeDataString(app.DirectoryUri.Split('/').LastOrDefault(x=>x.Length>0)??"目录"),app.DirectoryUri);});
            UiFactory.Button(content,"取消",1256,0,120,44,()=>{app.CancelListing();app.SetStatus("已取消读取");});
            if(app.Source==FileSource.Smb){scanLabel=UiFactory.Label(content,"",0,60,988,48,23);UiFactory.Button(content,"重新扫描",1002,60,180,48,app.StartDiscovery);UiFactory.Button(content,"停止扫描",1196,60,180,48,()=>{app.StopDiscovery();RefreshFileRows();});}
            else{address=UiFactory.Input(content,app.DirectoryUri,"输入本地目录或选择一个来源",0,60,1034,48,ShowKeyboard);
            UiFactory.Button(content,"打开",1046,60,102,48,()=>{query="";_ = app.Browse(address.text);});UiFactory.Button(content,"上级",1160,60,102,48,()=>{query="";app.Up();});UiFactory.Button(content,"刷新",1274,60,102,48,app.Refresh);}
            filter=UiFactory.Input(content,query,"搜索当前目录",0,126,864,44,ShowKeyboard);filter.onValueChanged.AddListener(value=>query=value);
            UiFactory.Button(content,"搜索",876,126,100,44,()=>{query=filter.text;Draw();});UiFactory.Button(content,new[]{"名称排序","日期排序","大小排序"}[sort],988,126,184,44,()=>{sort=(sort+1)%3;Draw();});UiFactory.Button(content,subtitleBrowse?"字幕文件":"视频文件",1184,126,192,44,()=>{subtitleBrowse=!subtitleBrowse;Draw();});
            var viewport=UiFactory.Rect(content,"File list",0,190,1376,352);fileList=viewport.gameObject.AddComponent<VirtualFileList>();fileList.Initialize(1376,352);
            if(fileKey!=CurrentFileKey)fileOffset=0;fileKey=CurrentFileKey;
            countLabel=UiFactory.Label(content,"",0,556,930,44,23);
            undoButton=UiFactory.Button(content,"撤销移除",1120,556,256,44,UndoFavorite);undoButton.gameObject.SetActive(false);
            RefreshFileRows();fileList.SetOffset(fileOffset);
        }
        private void ChangeSource(Action change){CloseKeyboard();CancelSmbConnection();query="";fileOffset=0;fileKey="";change();}
        private void RefreshFileRows()
        {
            if(fileList==null||tab!="文件"||modal!="")return;
            IEnumerable<FileEntry> items=app.Files.Where(e=>(e.directory||(subtitleBrowse?e.IsSubtitle:e.IsVideo))&&(string.IsNullOrEmpty(query)||e.name.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0));
            items=sort==1?items.OrderByDescending(e=>e.directory).ThenByDescending(e=>e.modified):sort==2?items.OrderByDescending(e=>e.directory).ThenByDescending(e=>e.size):items.OrderByDescending(e=>e.directory).ThenBy(e=>e.name,StringComparer.OrdinalIgnoreCase);
            var list=items.ToList();var rows=new List<BrowserRow>();
            if(app.Source==FileSource.Smb)rows.Add(new BrowserRow{key="saved",label="已保存的共享"});
            foreach(FileEntry item in list){
                string prefix=item.directory?"目录  ":item.IsSubtitle?"SRT   ":"视频  ";
                var row=new BrowserRow{key=item.uri,label=prefix+item.name,detail=item.directory?"打开":Size(item.size),open=()=>{query="";if(item.directory)_ = app.Browse(item.uri);else if(item.IsSubtitle){_ = app.LoadSubtitle(item.uri);SelectTab("字幕");}else app.OpenRecentOrFile(item);}};
                if(app.Source==FileSource.Favorites){row.secondary="移除";row.action=()=>RemoveFavorite(item.uri);}
                else if(app.Source==FileSource.Smb){row.secondary="编辑";row.action=()=>DrawServerForm(app.FindServer(item.uri));}
                else if(app.Source==FileSource.Recent){row.secondary="所在目录";row.action=()=>app.OpenContainingDirectory(item.uri);}
                if(app.Source==FileSource.Directory&&item.uri==app.LocateFileUri)row.label="▶ "+row.label;
                rows.Add(row);
            }
            if(app.Source==FileSource.Smb){
                rows.Add(new BrowserRow{key="discovered",label="局域网发现"});
                foreach(var host in app.Discovery.hosts??Array.Empty<DiscoveredSmbHost>()){
                    var found=host;if(!string.IsNullOrEmpty(query)&&!((host.name??"")+host.host).Contains(query,StringComparison.OrdinalIgnoreCase))continue;
                    rows.Add(new BrowserRow{key="host:"+host.host+":"+host.port,label=(string.IsNullOrEmpty(host.name)?host.host:host.name)+"  ·  "+host.host+":"+host.port,detail="选择共享",open=()=>ConnectHost(found)});
                }
                if(scanLabel!=null)scanLabel.text=app.Discovery.running?$"正在发现… 已探测 {app.Discovery.tested} / {app.Discovery.total}  ·  {app.Discovery.hosts.Length} 台":!string.IsNullOrEmpty(app.Discovery.error)?app.Discovery.error:app.Discovery.complete?$"扫描完成 · {app.Discovery.hosts.Length} 台设备":"扫描已停止 / 未完成";
            }
            if(rows.Count==0)rows.Add(new BrowserRow{key="empty",label=app.Loading?"正在读取…":"没有匹配文件，可切换来源或清空搜索。"});
            fileList.SetRows(rows);if(countLabel!=null)countLabel.text=$"{list.Count} 项"+(app.Source==FileSource.Smb?$"已保存 · {app.Discovery.hosts.Length} 台发现设备":" · 扳机拖动 / 摇杆滚动");
            if(!app.Loading&&!string.IsNullOrEmpty(app.LocateFileUri)&&locatedFile!=app.LocateFileUri){int index=rows.FindIndex(row=>row.key==app.LocateFileUri);if(index>=0){fileList.SetOffset(index*VirtualFileList.RowPitch);fileOffset=fileList.Offset;}locatedFile=app.LocateFileUri;}
        }
        private void RemoveFavorite(string uri){var item=app.Store.RemoveFavorite(uri,out int index);if(item==null)return;removedFavorite=item;removedIndex=index;undoUntil=Time.unscaledTime+5;app.ShowFavorites();app.SetStatus("已移除收藏，可在 5 秒内撤销");}
        private void UndoFavorite(){if(removedFavorite==null||Time.unscaledTime>=undoUntil)return;app.Store.RestoreFavorite(removedFavorite,removedIndex);removedFavorite=null;app.ShowFavorites();}
        private static string Size(long bytes)=>bytes>=1024L*1024*1024?$"{bytes/(1024d*1024*1024):0.00} GB":bytes>=1024*1024?$"{bytes/(1024d*1024):0.0} MB":$"{bytes/1024d:0} KB";
        private void DrawProjection()
        {
            projectionButtons.Clear();stereoButtons.Clear();projectionButtonState=-1;
            projectionDistanceWasFlat=app.Presenter.Settings.projection==Projection.Flat;
            var view=app.Presenter.Settings;string[] names={"平面 / 曲面","VR 180°","VR 360°","鱼眼","Cubemap 3×2","EAC 3×2"};
            for(int i=0;i<names.Length;i++){int index=i;projectionButtons.Add(UiFactory.Button(content,names[i],i*230,0,216,52,()=>{app.ChangeFormat(v=>v.projection=(Projection)index);Draw();},(int)view.projection==i));}
            string[] stereo={"普通 2D","左右 SBS","上下 OU"};for(int i=0;i<3;i++){int index=i;stereoButtons.Add(UiFactory.Button(content,stereo[i],i*230,72,216,52,()=>{app.ChangeFormat(v=>v.stereo=(StereoLayout)index);Draw();},(int)view.stereo==i));}
            halfButton=UiFactory.Button(content,view.half?"Half 半宽/半高":"Full 全宽/全高",690,72,216,52,()=>{app.ChangeFormat(v=>v.half=!v.half);Draw();},view.half);
            eyesButton=UiFactory.Button(content,view.swapEyes?"已交换左右眼":"交换左右眼",920,72,216,52,()=>{app.ChangeFormat(v=>v.swapEyes=!v.swapEyes);Draw();},view.swapEyes);
            UiFactory.Button(content,view.monoEye?"单眼 2D 已开":"单眼 2D",1150,72,216,52,()=>{view.monoEye=!view.monoEye;Draw();},view.monoEye);
            Setting("屏幕宽度",140,1,10,view.width,v=>view.width=v,"米");if(view.projection==Projection.Flat)Setting("屏幕距离",196,1.2f,10,view.distance,app.SetViewDistance,"米");else Setting("全景缩放",196,.5f,2,view.panoramaZoom,app.SetPanoramaZoom,"倍");Setting("屏幕高度",252,-3,3,view.heightOffset,v=>view.heightOffset=v,"米");Setting("屏幕曲率",308,0,150,view.curvature,v=>view.curvature=v,"°");Setting("鱼眼角度",364,120,240,view.fisheyeFov,v=>app.ChangeFormat(settings=>settings.fisheyeFov=v),"°");
            UiFactory.Button(content,"对齐正前方",0,422,220,44,()=>app.Recenter(false));UiFactory.Button(content,"对齐视线 / 躺卧",232,422,270,44,()=>app.Recenter(true));UiFactory.Button(content,"重新识别格式",514,422,270,44,()=>{app.RedetectFormat();Draw();});UiFactory.Button(content,"撤销本次识别",796,422,270,44,()=>{app.UndoFormatDetection();Draw();});UiFactory.Button(content,"采用建议",1078,422,290,44,()=>{app.ApplyFormatSuggestion();Draw();});
            UiFactory.Button(content,app.CurrentDirectoryPrefersVr180?"本目录优先 VR180：开":"本目录优先 VR180：关",0,472,470,40,()=>{app.SetDirectoryVr180Preference(!app.CurrentDirectoryPrefersVr180);Draw();},app.CurrentDirectoryPrefersVr180);
            UiFactory.Label(content,"仅在确认 SBS 后使用；明确标签与元数据优先",490,472,880,40,20);
            formatLabel=UiFactory.Label(content,app.FormatDescription,0,516,1376,94,20);formatLabel.textWrappingMode=TextWrappingModes.Normal;
        }
        private void DrawProjectionPage(){UiFactory.Clear(content);DrawProjection();}
        private void Setting(string name,float y,float min,float max,float value,Action<float> change,string unit="")
        {
            UiFactory.Label(content,name,0,y,225,45,26);var number=UiFactory.Label(content,$"{value:0.##} {unit}",1180,y,196,45,24,TextAlignmentOptions.Right);
            UiFactory.Slider(content,240,y+2,910,min,max,value,v=>{change(v);number.text=$"{v:0.##} {unit}";LastInteraction=Time.unscaledTime;});
        }
        private void SteppedSetting(string name,float y,float min,float max,float value,Action<float> change,string unit="")
        {
            const float step=.1f;
            UiFactory.Label(content,name,0,y,225,45,26);
            var number=UiFactory.Label(content,$"{value:0.0#} {unit}",1050,y,150,45,24,TextAlignmentOptions.Right);
            var slider=UiFactory.Slider(content,370,y+2,540,min,max,value,_=>{});
            if(name=="播放速度"){fullSpeedSlider=slider;fullSpeedNumber=number;}
            void Apply(float next)
            {
                next=Mathf.Clamp(Mathf.Round(next/step)*step,min,max);
                slider.SetValueWithoutNotify(next);change(next);number.text=$"{next:0.0#} {unit}";LastInteraction=Time.unscaledTime;
            }
            slider.onValueChanged.AddListener(Apply);
            UiFactory.Button(content,"−0.1",240,y,110,45,()=>Apply((Mathf.Ceil(slider.value/step-.0001f)-1)*step));
            UiFactory.Button(content,"+0.1",930,y,110,45,()=>Apply((Mathf.Floor(slider.value/step+.0001f)+1)*step));
            UiFactory.Button(content,"重置",1216,y,160,45,()=>Apply(1));
        }
        private void DrawSubtitles()
        {
            string[] sections={"选择","位置","样式"};
            for(int i=0;i<3;i++){int section=i;UiFactory.Button(content,sections[i],i*220,0,202,48,()=>{subtitlePage=section;Draw();},subtitlePage==i);}
            var settings=app.Store.Settings;
            if(subtitlePage==2){DrawSubtitleStyle();return;}
            if(subtitlePage==1){
                Setting("字幕延后",78,-10,10,app.Current?.subtitleDelay??0,app.SetSubtitleDelay,"秒");
                Setting("字幕大小",146,12,200,settings.subtitleSize,v=>settings.subtitleSize=v);
                Setting("视觉高度",214,-4,4,settings.subtitleOffset,v=>settings.subtitleOffset=v);
                Setting("字幕距离",282,.1f,6,settings.subtitleDistance,v=>settings.subtitleDistance=v,"米");
                UiFactory.Button(content,"−0.01 米",0,356,230,48,()=>{settings.subtitleDistance=Mathf.Max(.1f,settings.subtitleDistance-.01f);Draw();});
                UiFactory.Button(content,"+0.01 米",248,356,230,48,()=>{settings.subtitleDistance=Mathf.Min(6,settings.subtitleDistance+.01f);Draw();});
                UiFactory.Button(content,settings.subtitleFollow?"字幕跟随视线":"字幕固定方向",500,356,330,48,()=>{settings.subtitleFollow=!settings.subtitleFollow;Draw();});
                UiFactory.Label(content,"改变距离时保持视觉大小与相对高度；字号和高度可单独调整。",0,444,1376,60,25);return;
            }
            UiFactory.Button(content,"关闭字幕",0,76,175,48,app.DisableSubtitles);
            UiFactory.Button(content,"恢复自动选择",796,76,270,48,app.RestoreAutomaticSubtitles);
            UiFactory.Button(content,"手动选择 SRT",190,76,260,48,()=>{subtitleBrowse=true;SelectTab("文件");});
            string[] encodings={"Auto","UTF-8","GB18030","UTF-16LE"};int encoding=Array.IndexOf(encodings,app.SubtitleEncoding);
            UiFactory.Button(content,"编码："+app.SubtitleEncoding,468,76,310,48,()=>{app.SubtitleEncoding=encodings[(encoding+1)%encodings.Length];if(app.Current!=null&&!string.IsNullOrEmpty(app.Current.subtitle)&&!app.Current.subtitle.StartsWith("track:"))_ = app.LoadSubtitle(app.Current.subtitle);Draw();});
            UiFactory.Label(content,"同目录匹配字幕",0,148,900,40,28);
            var candidates=app.SubtitleCandidates;
            for(int i=0;i<Math.Min(candidates.Count,4);i++){var item=candidates[i];UiFactory.Button(content,item.name,0,206+i*56,1376,48,()=>_ = app.LoadSubtitle(item.uri),app.Current?.subtitle==item.uri);}
            if(candidates.Count==0)UiFactory.Label(content,"未发现匹配字幕，可手动选择。支持 chs、zh、zh-CN 等后缀。",0,210,1376,50,24);
            if(candidates.Count>4)UiFactory.Label(content,$"另有 {candidates.Count-4} 个候选，请在文件页查看全部。",0,440,1376,40,22);
            UiFactory.Button(content,"内嵌字幕与音轨…",0,526,360,48,DrawTracks);
        }
        private void DrawSubtitleStyle()
        {
            var style=app.Store.Settings.subtitleStyle;string[] presets={"白字黑边","黄字黑边","阴影","半透明底"};
            for(int i=0;i<4;i++){int preset=i;UiFactory.Button(content,presets[i],i*300,66,280,44,()=>{app.Store.Settings.subtitleStyle=SubtitleStyle.Preset(preset);Draw();});}
            ColorRow("文字颜色",0,128,style.textColor,c=>style.textColor=c);ColorRow("描边颜色",700,128,style.outlineColor,c=>style.outlineColor=c);
            UiFactory.Button(content,style.outline?"描边：开":"描边：关",0,194,202,44,()=>{style.outline=!style.outline;Draw();});
            UiFactory.Slider(content,222,194,424,0,.4f,style.outlineWidth,v=>style.outlineWidth=v);
            UiFactory.Button(content,style.shadow?"阴影：开":"阴影：关",700,194,202,44,()=>{style.shadow=!style.shadow;Draw();});
            UiFactory.Slider(content,922,194,424,0,1,style.shadowStrength,v=>style.shadowStrength=v);
            string[] backgrounds={"无背景","文字底框","整行底条"};
            for(int i=0;i<3;i++){int bg=i;UiFactory.Button(content,backgrounds[i],i*220,260,204,44,()=>{style.background=(SubtitleBackground)bg;Draw();},(int)style.background==i);}
            ColorRow("背景颜色",700,260,style.backgroundColor,c=>style.backgroundColor=c);
            Setting("背景透明度",324,0,1,1-style.backgroundOpacity,v=>style.backgroundOpacity=1-v);
            var previewRoot=UiFactory.Box(content,"Subtitle preview",0,390,1376,208,new Color(.22f,.25f,.28f,1));
            preview=previewRoot.gameObject.AddComponent<CaptionPresenter>();preview.Initialize(1376,208);
            preview.SetText("字幕样式实时预览\n明亮与暗色场景 · Subtitle preview",32,style);
        }
        private void ColorRow(string name,float x,float y,Color selected,Action<Color> change)
        {
            UiFactory.Label(content,name,x,y,190,44,24);Color[] colors={Color.white,Color.yellow,Color.green,Color.cyan,Color.black};string[] names={"白","黄","绿","青","黑"};
            for(int i=0;i<colors.Length;i++){Color color=colors[i];var button=UiFactory.Button(content,names[i],x+192+i*94,y,84,44,()=>{change(color);Draw();},selected==color);button.GetComponentInChildren<TextMeshProUGUI>().color=color==Color.black?UiFactory.TextColor:color;}
        }
        private void DrawSettings()
        {
            SteppedSetting("播放速度",0,.25f,3,app.Store.Settings.speed,app.SetSpeed,"倍");SteppedSetting("视频亮度",70,.3f,1.8f,app.Presenter.Settings.brightness,v=>app.Presenter.Settings.brightness=v);
            UiFactory.Label(content,"渲染画质",0,140,225,45,26);string[] qualities={"均衡 1.0×","高清 1.25×","超清 1.5×"};for(int i=0;i<3;i++){int quality=i;UiFactory.Button(content,qualities[i],240+i*330,140,310,48,()=>{app.RenderQuality.Select((RenderQuality)quality);Draw();},(int)app.Store.Settings.renderQuality==i);}
            UiFactory.Button(content,app.Store.Settings.loop?"单片循环：开":"单片循环：关",0,225,310,52,()=>{app.Store.Settings.loop=!app.Store.Settings.loop;app.Backend?.SetRepeat(app.Store.Settings.loop);Draw();});
            UiFactory.Button(content,app.Store.Settings.sequential?"目录连播：开":"目录连播：关",330,225,310,52,()=>{app.Store.Settings.sequential=!app.Store.Settings.sequential;Draw();});
            UiFactory.Button(content,app.Store.Settings.autoHide?"自动隐藏：开":"自动隐藏：关",660,225,310,52,()=>{app.Store.Settings.autoHide=!app.Store.Settings.autoHide;Draw();});
            UiFactory.Button(content,"选择音轨 / 内嵌字幕",0,305,440,52,DrawTracks);
            UiFactory.Button(content,"清空播放记录",0,391,310,52,()=>{app.Store.Settings.history.Clear();app.Store.Save();app.SetStatus("播放记录已清空");});
            UiFactory.Button(content,"自动加载同名 SRT："+(app.Store.Settings.autoLoadSubtitles?"开":"关"),330,391,500,52,()=>{app.SetAutoLoadSubtitles(!app.Store.Settings.autoLoadSubtitles);Draw();});
            UiFactory.Button(content,"格式："+new[]{"关闭","标签与元数据","增强识别"}[(int)app.Store.Settings.formatDetection],850,391,526,52,()=>{app.SetFormatDetectionMode((FormatDetectionMode)(((int)app.Store.Settings.formatDetection+1)%3));Draw();});
            var instructions=UiFactory.Label(content,"扳机开关简易面板；A / X 或菜单键开关完整面板。摇杆上下滚动，左右 ±10 秒。\n自动字幕：中文后缀 → 无后缀 → 其他语言；手动选择与关闭状态优先。\n键盘：空格播放暂停，← / → 快退快进，Tab 完整面板，R 居中。",0,478,1376,122,24);instructions.textWrappingMode=TextWrappingModes.Normal;
        }
        private void DrawTracks()
        {
            BeginModal("tracks");UiFactory.Label(content,"音轨与内嵌字幕",0,0,1000,50,32);UiFactory.Button(content,"返回",1190,0,186,48,Draw);
            UiFactory.Button(content,"自动音轨",0,68,285,48,()=>app.Backend?.SelectTrack("auto",1));UiFactory.Button(content,"关闭内嵌字幕",304,68,320,48,app.DisableSubtitles);
            var tracks=app.Backend?.Snapshot.tracks??Array.Empty<MediaTrack>();
            for(int i=0;i<Math.Min(tracks.Length,8);i++){var track=tracks[i];UiFactory.Button(content,(track.type==1?"音轨  ":"字幕  ")+track.name+(track.supported?"":"  [解码器不支持]"),0,138+i*54,1376,48,()=>{if(track.type==1){app.Backend?.SelectTrack(track.id,1);if(app.Current!=null)app.Current.audio=track.id;}else app.EmbeddedSubtitle(track.id);},track.selected);}
            if(tracks.Length==0)UiFactory.Label(content,"请先打开视频；轨道信息将在媒体准备完成后出现。",0,150,1376,60,25);
        }
        private void DrawInfo(){detail=UiFactory.Label(content,"",0,0,1376,590,25);detail.textWrappingMode=TextWrappingModes.Normal;detail.alignment=TextAlignmentOptions.TopLeft;}
        public void CancelSmbConnection(){connection?.Cancel();connection?.Dispose();connection=null;if(modal=="connecting")Draw();}
        private void CloseKeyboard(){keyboardTarget=null;if(keyboard!=null)keyboard.gameObject.SetActive(false);fileList?.Block(false);}
        private void BeginModal(string name){CancelSmbConnection();app.StopDiscovery();CloseKeyboard();if(fileList!=null)fileOffset=fileList.Offset;UiFactory.Clear(content);fileList=null;preview=null;modal=name;}
        private void ReturnFromModal(){CancelSmbConnection();CloseKeyboard();Draw();if(CanDiscover&&app.Source==FileSource.Smb)app.StartDiscovery();}
        private void ConnectHost(DiscoveredSmbHost host)=>_ = EnumerateShares(new SmbServer{name=host.name,host=host.host,port=host.port});
        private async Task EnumerateShares(SmbServer server)
        {
            BeginModal("connecting");UiFactory.Label(content,"正在连接 "+server.host+" 并读取共享…",0,70,1376,80,30);UiFactory.Button(content,"取消",1100,0,276,48,ReturnFromModal);
            connection=new CancellationTokenSource();var token=connection.Token;
            try{
                var result=await app.Smb.Shares(server,token);if(token.IsCancellationRequested)return;
                if(!result.ok){DrawConnectionForm(server,result.error);return;}
                BeginModal("shares");UiFactory.Label(content,"选择共享 · "+server.host,0,0,1040,50,30);UiFactory.Button(content,"返回",1160,0,216,48,ReturnFromModal);
                var root=UiFactory.Rect(content,"Shares",0,74,1376,450);fileList=root.gameObject.AddComponent<VirtualFileList>();fileList.Initialize(1376,450);
                var rows=(result.shares??Array.Empty<SmbShare>()).Select(share=>new BrowserRow{key=share.name,label=share.name+"  "+share.comment,detail="打开",open=()=>SaveSharedFolder(server,share.name)}).ToList();
                if(rows.Count==0)rows.Add(new BrowserRow{key="empty",label="服务器未返回可用磁盘共享，可手填共享名。"});fileList.SetRows(rows);
                UiFactory.Button(content,"手填共享名 / 更换账号",0,548,480,48,()=>DrawConnectionForm(server,""));
            }catch(OperationCanceledException){}catch(Exception ex){if(!token.IsCancellationRequested)DrawConnectionForm(server,"连接失败："+ex.GetType().Name);}
        }
        private void DrawConnectionForm(SmbServer server,string error)
        {
            BeginModal("login");UiFactory.Label(content,"连接 "+server.host,0,0,1000,50,30);UiFactory.Button(content,"返回",1160,0,216,48,ReturnFromModal);
            UiFactory.Label(content,error,0,64,1376,56,23);
            string[] labels={"用户名（空为匿名）","密码","域（可留空）","共享名（可选）"};string[] initial={server.username,server.password,server.domain,server.share};var fields=new List<TMP_InputField>();
            for(int i=0;i<4;i++){UiFactory.Label(content,labels[i],0,142+i*66,350,48,25);fields.Add(UiFactory.Input(content,initial[i],labels[i],370,142+i*66,1006,48,ShowKeyboard,i==1));}
            UiFactory.Button(content,server.remember?"保存密码：开":"保存密码：关",0,430,310,48,()=>{ReadCredentials();server.remember=!server.remember;DrawConnectionForm(server,error);});
            UiFactory.Button(content,server.encrypt?"SMB 加密：开":"SMB 加密：关",330,430,310,48,()=>{ReadCredentials();server.encrypt=!server.encrypt;DrawConnectionForm(server,error);});
            UiFactory.Button(content,"列出共享",0,528,470,56,()=>{ReadCredentials();CloseKeyboard();_ = EnumerateShares(server);},true);
            UiFactory.Button(content,"打开指定共享",492,528,470,56,()=>{ReadCredentials();if(string.IsNullOrWhiteSpace(fields[3].text)){app.SetStatus("请填写共享名，或选择“列出共享”");return;}SaveSharedFolder(server,fields[3].text);});
            void ReadCredentials(){server.username=fields[0].text;server.password=fields[1].text;server.domain=fields[2].text;server.share=fields[3].text;}
        }
        private void SaveSharedFolder(SmbServer server,string share)
        {
            server.share=share.Trim();var existing=app.Servers.FirstOrDefault(s=>string.Equals(s.host,server.host,StringComparison.OrdinalIgnoreCase)&&s.port==server.port&&string.Equals(s.share,server.share,StringComparison.OrdinalIgnoreCase)&&s.username==server.username&&s.domain==server.domain);server.id=existing?.id??"";
            if(string.IsNullOrEmpty(server.name))server.name=server.host;server.name=server.name+" / "+server.share;
            if(!app.SaveServer(server))return;CloseKeyboard();modal="";query="";_ = app.Browse("smb://"+server.id+"/");Draw();
        }
        private void DrawServerForm()=>DrawServerForm(null);
        private void DrawServerForm(SmbServer existing)
        {
            BeginModal("server");UiFactory.Label(content,existing==null?"添加 SMB 服务器":"编辑 SMB 服务器",0,0,900,55,32);UiFactory.Button(content,"取消",1180,0,196,48,ReturnFromModal);
            if(existing!=null)UiFactory.Button(content,"删除配置",920,0,240,48,()=>{app.RemoveServer(existing.id);Draw();});
            string[] labels={"显示名称","主机 / IP","共享名","用户名（空为匿名）",existing==null?"密码":"密码（空则保留）","域（可留空）"};var fields=new List<TMP_InputField>();var initial=existing??new SmbServer();string[] values={initial.name,initial.host,initial.share,initial.username,"",initial.domain};
            for(int i=0;i<labels.Length;i++){float y=75+i*67;UiFactory.Label(content,labels[i],0,y,345,50,25);fields.Add(UiFactory.Input(content,values[i],labels[i],370,y,1006,50,ShowKeyboard,i==4));}
            bool remember=initial.remember,encrypt=initial.encrypt;var rememberButton=UiFactory.Button(content,remember?"保存密码：开":"保存密码：关",0,493,296,48,null);rememberButton.onClick.AddListener(()=>{remember=!remember;rememberButton.GetComponentInChildren<TextMeshProUGUI>().text=remember?"保存密码：开":"保存密码：关";});
            var encryptionButton=UiFactory.Button(content,encrypt?"SMB 加密：开":"SMB 加密：关",314,493,296,48,null);encryptionButton.onClick.AddListener(()=>{encrypt=!encrypt;encryptionButton.GetComponentInChildren<TextMeshProUGUI>().text=encrypt?"SMB 加密：开":"SMB 加密：关";});
            var port=UiFactory.Input(content,initial.port.ToString(),"端口",632,493,186,48,ShowKeyboard);UiFactory.Label(content,"端口",834,493,150,48,24);
            UiFactory.Button(content,"保存并返回服务器列表",0,560,1376,48,()=>{if(!int.TryParse(port.text,out int parsed)||parsed<1||parsed>65535){app.SetStatus("请输入 1–65535 之间的端口");return;}
                var server=new SmbServer{id=initial.id,name=fields[0].text,host=fields[1].text,share=fields[2].text,username=fields[3].text,password=fields[4].text,domain=fields[5].text,port=parsed,remember=remember,encrypt=encrypt};if(app.SaveServer(server)){keyboard.gameObject.SetActive(false);Draw();}},true);
        }
        private void ShowKeyboard(TMP_InputField target){keyboardTarget=target;LastInteraction=Time.unscaledTime;keyboard.gameObject.SetActive(true);fileList?.Block(true);DrawKeyboard();}
        public void ScrollList(float direction){if(Quick!=null&&Quick.Visible){Quick.Scroll(direction);return;}if(keyboard!=null&&!keyboard.gameObject.activeSelf)fileList?.Scroll(direction);}
        private void UpdateKeyboardPreview(){if(keyboardPreview!=null&&keyboardTarget!=null)keyboardPreview.text=keyboardTarget.contentType==TMP_InputField.ContentType.Password?new string('•',keyboardTarget.text.Length):keyboardTarget.text;}
        private void DrawKeyboard()
        {
            UiFactory.Clear(keyboard);keyboardPreview=UiFactory.Label(keyboard,"",20,12,1050,44,25);UpdateKeyboardPreview();UiFactory.Button(keyboard,"完成",1140,10,215,46,()=>{bool search=keyboardTarget==filter;keyboardTarget=null;keyboard.gameObject.SetActive(false);fileList?.Block(false);if(search)Draw();});
            string[] rows={"1234567890-_=+","qwertyuiop[]\\","asdfghjkl;:'\"","zxcvbnm,./@:#"};
            for(int row=0;row<rows.Length;row++)for(int col=0;col<rows[row].Length;col++){char c=rows[row][col];string key=shift?c.ToString().ToUpperInvariant():c.ToString();UiFactory.Button(keyboard,key,20+col*100,80+row*72,91,60,()=>Insert(key));}
            UiFactory.Button(keyboard,shift?"小写":"大写",20,389,195,64,()=>{shift=!shift;DrawKeyboard();});UiFactory.Button(keyboard,"空格",235,389,580,64,()=>Insert(" "));UiFactory.Button(keyboard,"退格",835,389,245,64,()=>{if(keyboardTarget!=null&&keyboardTarget.text.Length>0)keyboardTarget.text=keyboardTarget.text.Substring(0,keyboardTarget.text.Length-1);UpdateKeyboardPreview();});UiFactory.Button(keyboard,"清空",1100,389,245,64,()=>{if(keyboardTarget!=null)keyboardTarget.text="";UpdateKeyboardPreview();});
        }
        private void Insert(string text){if(keyboardTarget==null)return;keyboardTarget.text+=text;keyboardTarget.caretPosition=keyboardTarget.text.Length;UpdateKeyboardPreview();}
        private void OnDestroy(){CancelSmbConnection();if(app!=null){app.Changed-=OnAppChanged;app.DiscoveryChanged-=RefreshFileRows;}if(Canvas!=null)Destroy(Canvas.gameObject);if(transportRoot!=null)Destroy(transportRoot.gameObject);}
    }
}
