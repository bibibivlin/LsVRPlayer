using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace VRPlayer
{
    public sealed partial class PlayerApp : MonoBehaviour
    {
        public Camera headCamera;
        public IMediaBackend Backend{get;private set;}
        public PlayerStore Store{get;private set;}
        public VideoPresenter Presenter{get;private set;}
        public PlayerUI UI{get;private set;}
        public RenderQualityController RenderQuality{get;private set;}
        public FileSource Source{get;private set;}=FileSource.Home;
        public SmbLibrary Smb{get;}=new SmbLibrary();
        public SmbServer[] Servers{get;private set;}=Array.Empty<SmbServer>();
        public SmbDiscoverySnapshot Discovery{get;private set;}=new SmbDiscoverySnapshot();
        public event Action DiscoveryChanged;
        private Coroutine scan;
        public PlaybackRecord Current{get;private set;}
        public List<FileEntry> Files{get;private set;}=new List<FileEntry>();
        public List<FileEntry> SubtitleCandidates{get;private set;}=new List<FileEntry>();
        public string DirectoryUri{get;private set;}="";
        public string Status{get;private set;}="选择本地目录或 SMB 服务器开始观看";
        public bool Loading{get;private set;}
        public int Generation{get;private set;}
        public string SubtitleEncoding="Auto";
        public event Action Changed;
        private readonly FileLibrary library=new FileLibrary();
        private sealed class BrowserLocation {public FileSource source;public string uri;public PlayerUI.FileViewState view;public long position;}
        private readonly List<BrowserLocation> directories=new List<BrowserLocation>();
        private long nextBrowserHistoryPosition;
        public long BrowserHistoryPosition=>directories.Count==0?0:directories[directories.Count-1].position;
        private readonly Dictionary<string,string> directoryParents=new Dictionary<string,string>(StringComparer.Ordinal);
        private readonly Queue<string> directoryParentOrder=new Queue<string>();
        private bool preserveDirectoryHistory;
        private List<FileEntry> playlist=new List<FileEntry>();
        private CancellationTokenSource listing,subtitles,subtitleListing,storageAccess;
        private int subtitleRevision;
        private readonly RelativeSeekTarget seekTarget=new RelativeSeekTarget();
        public string LocateFileUri{get;private set;}="";
        public bool HasFocus{get;private set;}=true;
        public bool PresenceKnown{get;private set;}
        private SrtDocument subtitleDocument;
        private CaptionPresenter caption;
        private Transform captionRoot;
        private bool ended,wasPresent,tracksRestored;
        private int previousState;
        private string lastError="";
        private float saveAt,diagnosticsAt;
        private bool viewDistanceDirty;
        private void Start()
        {
            headCamera=headCamera!=null?headCamera:Camera.main;
            Store=new PlayerStore(Path.Combine(Application.persistentDataPath,"settings.json"));
            Screen.sleepTimeout=SleepTimeout.NeverSleep;Application.targetFrameRate=72;
            RenderQuality=gameObject.AddComponent<RenderQualityController>();RenderQuality.Initialize(this);
            Presenter=new GameObject("Video Presenter").AddComponent<VideoPresenter>();Presenter.Initialize(headCamera);
            try{
#if UNITY_ANDROID && !UNITY_EDITOR
                Backend=new AndroidMediaBackend();
#else
                Backend=new UnityMediaBackend(gameObject);
#endif
            }catch(Exception ex){Status="播放内核初始化失败："+ex.GetType().Name;Debug.LogException(ex);}
            InitializeEmby();
            UI=gameObject.AddComponent<PlayerUI>();UI.Initialize(this,headCamera);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            gameObject.AddComponent<PlayerDiagnostics>();
#endif
            if(GetComponent<UnityEngine.EventSystems.EventSystem>()==null)gameObject.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var input=gameObject.AddComponent<VrUiInput>();input.Initialize(this,headCamera,UI.Canvas);
            CreateCaption();
            Backend?.SetVolume(Store.Settings.volume);Backend?.SetSpeed(Store.Settings.speed);Backend?.SetRepeat(Store.Settings.loop);
            StartCoroutine(RenderFrames());
#if UNITY_ANDROID && !UNITY_EDITOR
            try{File.WriteAllText(Path.Combine(Application.persistentDataPath,"decoder-capabilities.json"),AndroidBridge.Api.CallStatic<string>("capabilities"));string launch=AndroidBridge.Api.CallStatic<string>("getLaunchUri");if(!string.IsNullOrEmpty(launch)){Open(new FileEntry{name=Path.GetFileName(new Uri(launch).LocalPath),uri=launch},FileLibrary.Parent(launch)??"");return;}}catch(Exception ex){Debug.LogWarning("VRPlayer diagnostic startup: "+ex.GetType().Name);}
#endif
            Home();
        }
        private void CreateCaption()
        {
            var root=new GameObject("VR Subtitles",typeof(RectTransform),typeof(Canvas));root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            captionRoot=root.transform;((RectTransform)captionRoot).sizeDelta=new Vector2(1300,300);captionRoot.localScale=Vector3.one*.0015f;
            root.GetComponent<Canvas>().worldCamera=headCamera;root.GetComponent<Canvas>().sortingOrder=5;
            caption=root.AddComponent<CaptionPresenter>();caption.Initialize(1300,300,true);
        }
        private IEnumerator RenderFrames(){var wait=new WaitForEndOfFrame();while(true){yield return wait;if(Backend is AndroidMediaBackend android)android.RenderFrame();}}
        private void Update()
        {
            if(Store==null||Backend==null)return;
            Backend.Tick();var state=Backend.Snapshot;TickFormatDetection(state);TickEmbyPlayback(state);
            Presenter.UpdateVideo(Backend.Texture,Backend.RequiresGammaConversion,state.width,state.height,state.pixelRatio);
            if(Current!=null&&state.generation==Generation){
                if(state.state!=previousState){previousState=state.state;if(state.state==3)SetStatus("媒体已准备好");else if(state.state==2)SetStatus("正在缓冲…");else if(state.state==4)SetStatus("播放完成");}
                if(!tracksRestored&&state.state==3&&state.tracks!=null&&state.tracks.Length>0){tracksRestored=true;if(Current.audio!="auto"&&state.tracks.Any(t=>t.type==1&&t.id==Current.audio))Backend.SelectTrack(Current.audio,1);if(Current.subtitle.StartsWith("track:")&&state.tracks.Any(t=>t.type==3&&t.id==Current.subtitle.Substring(6)))Backend.SelectTrack(Current.subtitle.Substring(6),3);}
                if(state.HasError&&lastError!=state.error){lastError=state.error;SetStatus("播放失败："+state.error+"。可点重试或选择其他文件。");_ = RetryWithStoragePermission(Current.uri,Generation);}
                if(!state.HasError&&state.state==4&&!ended){ended=true;if(Store.Settings.sequential&&!Store.Settings.loop)Next(1,false);}
                if(Time.unscaledTime>=saveAt){saveAt=Time.unscaledTime+5;SaveProgress();}
                caption.SetText(subtitleDocument!=null?subtitleDocument.TextAt(state.position/1000d,Current.subtitleDelay):Current.subtitle.StartsWith("track:")?state.embeddedSubtitle:"",Store.Settings.subtitleSize,Store.Settings.subtitleStyle);
            }else caption.SetText("",Store.Settings.subtitleSize,Store.Settings.subtitleStyle);
            caption.Position(headCamera,Presenter.transform,Store.Settings);
            InputDevice head=InputDevices.GetDeviceAtXRNode(XRNode.Head);PresenceKnown=head.TryGetFeatureValue(CommonUsages.userPresence,out bool present);if(PresenceKnown){if(wasPresent&&!present)Pause();wasPresent=present;}
            if(Time.unscaledTime>diagnosticsAt){diagnosticsAt=Time.unscaledTime+2;try{File.WriteAllText(Path.Combine(Application.persistentDataPath,"playback-diagnostics.json"),JsonUtility.ToJson(new Diagnostic{snapshot=state,frames=Backend.RenderedFrames,projection=Presenter.Settings.projection.ToString(),stereo=Presenter.Settings.stereo.ToString(),unityFps=RenderQuality.Fps,eyeWidth=XRSettings.eyeTextureWidth,eyeHeight=XRSettings.eyeTextureHeight,renderScale=RenderQuality.EffectiveScale,msaa=QualitySettings.antiAliasing,status=Status,caption=caption.Text,subtitle=Current?.subtitle??"",subtitleCandidates=SubtitleCandidates.Select(x=>x.name).ToArray(),format=Detection,formatDescription=FormatDescription,analysis=LastFrameAnalysis,analysisAttempts=AnalysisAttempts,analysisValidFrames=AnalysisValidFrames,analysisStatus=AnalysisStatus,recognitionP95=RecognitionMainThreadP95,subtitleSelection=Current?.subtitleSelection.ToString()??"",directory=DirectoryUri,source=Source.ToString(),
                uiSurface=UI.Visible?"full":UI.Quick.Visible?"quick":"hidden",quickPopup=UI.Quick.Popup,quickVolumeVisible=UI.Quick.VolumeVisible,quickBrowserUri=UI.Quick.BrowserUri,quickBrowserCount=UI.Quick.BrowserCount,quickBrowserLoading=UI.Quick.BrowserLoading,hasFocus=HasFocus,userPresent=wasPresent,presenceKnown=PresenceKnown,volume=Store.Settings.volume,speed=Store.Settings.speed,panoramaZoom=Presenter.Settings.panoramaZoom,distance=Presenter.Settings.distance,
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                timestampUtc=DateTime.UtcNow.ToString("O"),frame=Time.frameCount,fullVisible=UI.Visible,quickVisible=UI.Quick.Visible,loading=Loading,fullOffset=UI.FullOffset,fullCount=UI.FullCount,fullModal=UI.FullModal,fullQuery=UI.FullQuery,keyboardVisible=UI.KeyboardVisible,quickOffset=UI.Quick.BrowserOffset,
#endif
            },true));}catch{} }
        }
        [Serializable] private sealed class Diagnostic {public MediaSnapshot snapshot;public int frames;public string projection,stereo,status,caption,subtitle,subtitleSelection,directory,source,formatDescription;public FormatDetectionResult format;public FrameAnalysis analysis;public int analysisAttempts,analysisValidFrames;public string analysisStatus;public float recognitionP95;public string[] subtitleCandidates;public float unityFps,renderScale;public int eyeWidth,eyeHeight,msaa;public string uiSurface,quickPopup,quickBrowserUri;public bool quickVolumeVisible,quickBrowserLoading,hasFocus,userPresent,presenceKnown;public int quickBrowserCount;public float volume,speed,panoramaZoom,distance;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            public string fullModal,fullQuery,timestampUtc;public bool keyboardVisible,fullVisible,quickVisible,loading;public int fullCount,frame;public float fullOffset,quickOffset;
#endif
        }
        public void SetStatus(string value){Status=value;}
        public void Notify(){if(Source==FileSource.Recent)UpdateRecentFiles();Changed?.Invoke();}
        public void Home()
        {
            StopDiscovery();Source=FileSource.Home;CancelListing();ClearDirectoryHistory();DirectoryUri="";
            Files=new List<FileEntry>();
#if UNITY_ANDROID && !UNITY_EDITOR
            Files.Add(new FileEntry{name="设备存储",uri="file:///storage/emulated/0/",directory=true});Files.Add(new FileEntry{name="Movies 视频",uri="file:///storage/emulated/0/Movies/",directory=true});
#else
            Files.Add(new FileEntry{name="测试视频",uri=new Uri(Path.GetFullPath("TestMedia")).AbsoluteUri,directory=true});Files.Add(new FileEntry{name="我的视频",uri=new Uri(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)).AbsoluteUri,directory=true});
#endif
            Files.Add(new FileEntry{name="应用目录",uri=new Uri(Application.persistentDataPath).AbsoluteUri,directory=true});Status="添加目录或打开设备存储；SMB 可在服务器页配置";Notify();
        }
        private void ClearDirectoryHistory(){if(!preserveDirectoryHistory)directories.Clear();}
        private void RememberBrowserLocation()
        {
            if(Source==FileSource.Emby)return;
            if(directories.Count>=64)directories.RemoveAt(0);
            directories.Add(new BrowserLocation{source=Source,uri=DirectoryUri,view=UI?.CaptureFileView(),position=++nextBrowserHistoryPosition});
        }
        public bool BrowseBack()
        {
            if(Source==FileSource.Emby||directories.Count==0)return false;
            var previous=directories[directories.Count-1];directories.RemoveAt(directories.Count-1);
            LocateFileUri="";UI?.RestoreFileView(previous.view,previous.source,previous.uri);
            bool preserving=preserveDirectoryHistory;preserveDirectoryHistory=true;
            try{
                switch(previous.source){
                    case FileSource.Directory:_ = Browse(previous.uri,false);break;
                    case FileSource.Favorites:ShowFavorites();break;
                    case FileSource.Recent:ShowRecent();break;
                    case FileSource.Smb:ShowServers();break;
                    default:Home();break;
                }
            }finally{preserveDirectoryHistory=preserving;}
            return true;
        }
        public async Task Browse(string uri,bool push=true,string locateFile="",string physicalParent=null)
        {
            if(push&&(Source!=FileSource.Directory||DirectoryUri!=uri))RememberBrowserLocation();
            if(!string.IsNullOrEmpty(physicalParent)&&physicalParent!=uri){
                if(!directoryParents.ContainsKey(uri)){if(directoryParentOrder.Count>=64)directoryParents.Remove(directoryParentOrder.Dequeue());directoryParentOrder.Enqueue(uri);}
                directoryParents[uri]=physicalParent;
            }
            StopDiscovery();Source=FileSource.Directory;
            CancelListing();listing=new CancellationTokenSource();CancellationToken token=listing.Token;
            DirectoryUri=uri;LocateFileUri=locateFile;Loading=true;Status="正在读取目录…";Files=new List<FileEntry>();Notify();
            try{var result=await library.List(uri,token);if(token.IsCancellationRequested)return;Loading=false;if(result.ok){Files=(result.entries??Array.Empty<FileEntry>()).ToList();Status=$"{Files.Count} 项";}else Status=result.error;Notify();}
            catch(OperationCanceledException){}catch(Exception ex){if(!token.IsCancellationRequested){Loading=false;SetStatus("无法读取目录："+ex.Message);}}
        }
        public void Up()
        {
            string parent=FileLibrary.Parent(DirectoryUri);
            if(parent==null)directoryParents.TryGetValue(DirectoryUri??"",out parent);
            if(parent!=null){_ = Browse(parent);return;}
            if(Source==FileSource.Directory)RememberBrowserLocation();
            bool preserving=preserveDirectoryHistory;preserveDirectoryHistory=true;
            try{Home();}finally{preserveDirectoryHistory=preserving;}
        }
        public void Refresh()
        {
            bool preserving=preserveDirectoryHistory;preserveDirectoryHistory=true;
            try{switch(Source){case FileSource.Emby:RefreshEmby();break;case FileSource.Favorites:ShowFavorites();break;case FileSource.Recent:ShowRecent();break;case FileSource.Smb:ShowServers();break;case FileSource.Directory:_ = Browse(DirectoryUri,false);break;default:Home();break;}}
            finally{preserveDirectoryHistory=preserving;}
        }
        public void CancelListing(){listing?.Cancel();listing?.Dispose();listing=null;Loading=false;CancelEmbyBrowsing();}
        public void Open(FileEntry entry,string directory=null)
        {
            OpenVideo(entry,directory,null,false);
        }
        private void OpenVideo(FileEntry entry,string directory,ViewSettings inheritedFormat,bool keepPlaylist,EmbyPlayback embyPlayback=null)
        {
            CancelStorageAccess();
            if(TryOpenEmby(entry,directory,inheritedFormat,keepPlaylist,embyPlayback))return;
            if(entry.directory){_ = Browse(entry.uri);return;}if(entry.IsSubtitle){_ = LoadSubtitle(entry.uri);return;}
            if(Backend==null){SetStatus("播放内核不可用");return;}
            CancelEmbyPlaybackRequest();
            if(embyPlayback==null){embyMovieParts=null;embyMovieExtras=null;}
            SaveProgress();CancelSubtitleSelection();subtitleListing?.Cancel();subtitleDocument=null;SubtitleCandidates.Clear();lastError="";ended=false;tracksRestored=false;previousState=0;seekTarget.Reset();
            EndEmbyPlayback();
            Current=Store.Touch(entry,directory??DirectoryUri);Presenter.Settings=Current.view??new ViewSettings();Current.view=Presenter.Settings;if(Current.subtitleSelection==SubtitleSelection.Auto)Current.subtitle="";
            if(!keepPlaylist)playlist=Source==FileSource.Directory&&DirectoryUri==Current.directory&&!Loading&&Files.Any(item=>item.uri==entry.uri)
                ?Files.Where(item=>item.IsVideo).OrderBy(item=>item.name,StringComparer.OrdinalIgnoreCase).ToList():new List<FileEntry>();
            // Explicit adjacent navigation carries the current source format, before any detection can run.
            if(inheritedFormat!=null){
                var view=Presenter.Settings;view.projection=inheritedFormat.projection;view.stereo=inheritedFormat.stereo;
                view.half=inheritedFormat.half;view.swapEyes=inheritedFormat.swapEyes;view.fisheyeFov=inheritedFormat.fisheyeFov;
                Current.formatLocked=true;Current.formatLockOrigin=FormatLockOrigin.Inherited;Current.formatCache=null;
            }
            Generation++;BeginFormatDetection(entry);
            if(inheritedFormat!=null){FormatDescription="已继承上一部播放影片的格式；可重新识别格式";}
            if(embyPlayback!=null){
                Current.position=embyPlayback.startPositionMs;
                BeginEmbyPlayback(embyPlayback);
                if(Backend is IAuthenticatedMediaBackend authenticated)authenticated.OpenAuthenticated(embyPlayback.url,embyPlayback.token,Current.position,Generation);
                else Backend.Open(embyPlayback.url,Current.position,Generation);
            }else Backend.Open(entry.uri,Current.position,Generation);
            Backend.SelectTrack("off",3);
            Store.Save();
            Status="正在打开："+entry.name;UI.PlaybackOpened();Notify();_ = DiscoverSubtitles(Current.directory,Generation);
        }
        public void TogglePlay(){if(Backend==null||Current==null)return;if(Backend.Snapshot.playing)Pause();else Backend.Play();}
        public void Pause(){Backend?.Pause();if(embyPlayback!=null&&embyReportedStart)QueueEmbyReport("progress",Backend.Snapshot.position,true);SaveProgress();}
        public void Seek(long milliseconds){if(Backend==null||Current==null||Backend.Snapshot.generation!=Generation||Backend.Snapshot.duration<=0)return;ended=false;long target=seekTarget.Absolute(milliseconds,Backend.Snapshot.duration,Time.unscaledTime);Backend.Seek(target);}
        public void Skip(int seconds){if(Backend==null||Current==null||Backend.Snapshot.generation!=Generation||Backend.Snapshot.duration<=0)return;var state=Backend.Snapshot;long target=seekTarget.Relative(state.position,seconds*1000L,state.duration,Time.unscaledTime);ended=false;Backend.Seek(target);UI.ShowTransport((seconds<0?"快退":"快进")+" 10 秒 · "+TimeSpan.FromMilliseconds(target).ToString(@"hh\:mm\:ss"));}
        public void Next(int delta,bool inheritFormat=true)
        {
            if(Current==null||playlist.Count==0||delta==0)return;
            int index=playlist.FindIndex(x=>x.uri==Current.uri),next=index+delta;
            if(index<0||next<0||next>=playlist.Count){SetStatus("已到目录边界");return;}
            if(!AllowEmbyAdjacent(playlist[next].uri,!inheritFormat))return;
            OpenVideo(playlist[next],Current.directory,inheritFormat||InheritEmbyPartFormat(playlist[next].uri)?Presenter.Settings.Copy():null,true);
        }
        public void Retry(){if(Current==null)return;var record=Current;Open(new FileEntry{uri=record.uri,name=record.name,size=record.size,modified=record.modified},record.directory);}
        public void Stop(){CancelEmbyPlaybackRequest();CancelStorageAccess();SaveProgress();EndEmbyPlayback();embyMovieParts=null;embyMovieExtras=null;Generation++;CancelFormatDetection();Backend?.Stop();Current=null;subtitleDocument=null;CancelSubtitleSelection();subtitleListing?.Cancel();seekTarget.Reset();caption?.SetText("",Store.Settings.subtitleSize,Store.Settings.subtitleStyle);SetStatus("播放已停止");}
        public bool CanNavigateAdjacent(int delta){if(Current==null||delta==0)return false;int index=playlist.FindIndex(x=>x.uri==Current.uri);return index>=0&&index+delta>=0&&index+delta<playlist.Count&&AllowEmbyAdjacent(playlist[index+delta].uri,false);}
        public void SetVolume(float value){value=Mathf.Clamp01(value);Store.Settings.volume=value;if(value>0)Store.Settings.lastAudibleVolume=value;Backend?.SetVolume(value);}
        public void ToggleMute(){SetVolume(Store.Settings.volume>0?0:Store.Settings.lastAudibleVolume);Store.Save();}
        public void SetSpeed(float value){value=Mathf.Clamp(value,.25f,3);Store.Settings.speed=value;Backend?.SetSpeed(value);}
        public void SetSubtitleDelay(float value){if(Current==null)return;Current.subtitleDelay=Mathf.Clamp(value,-10,10);Store.Save();Notify();}
        public void SetViewDistance(float value)=>SetViewDistance(value,true);
        public void SetViewDistance(float value,bool save){Presenter.Settings.distance=Mathf.Clamp(value,1.2f,10);viewDistanceDirty=true;if(save)CommitViewDistance();}
        public void SetPanoramaZoom(float value)=>SetPanoramaZoom(value,true);
        public void SetPanoramaZoom(float value,bool save){Presenter.Settings.panoramaZoom=PlayerStore.NormalizeZoom(value);viewDistanceDirty=true;if(save)CommitViewDistance();}
        public void ResetViewDistance(){if(Presenter.Settings.projection==Projection.Flat)SetViewDistance(3.5f);else SetPanoramaZoom(1);}
        public bool AdjustViewDistance(float direction,float deltaTime)
        {
            if(Current==null||Presenter==null||deltaTime<=0||Mathf.Abs(direction)<=.25f)return false;
            var view=Presenter.Settings;direction=Mathf.Clamp(direction,-1,1);
            if(view.projection==Projection.Flat){float value=Mathf.Clamp(view.distance-direction*2*deltaTime,1.2f,10);if(value==view.distance)return false;SetViewDistance(value,false);}
            else{float value=Mathf.Clamp(view.panoramaZoom+direction*.5f*deltaTime,.5f,2);if(value==view.panoramaZoom)return false;SetPanoramaZoom(value,false);}
            return true;
        }
        public void CommitViewDistance(){if(!viewDistanceDirty||Store==null)return;Store.Save();viewDistanceDirty=false;}
        public void SaveProgress()
        {
            if(Store==null)return;var state=Backend?.Snapshot;
            if(!EmbyProgressSuppressed&&Current!=null&&state!=null&&state.generation==Generation&&state.duration>0){Current.position=state.state==4?0:state.position;}
            Store.Save();viewDistanceDirty=false;
            if(Source==FileSource.Recent)Notify();
        }
        public void AddFavorite(string name,string uri){if(Store.Settings.favorites.All(x=>x.uri!=uri))Store.Settings.favorites.Add(new Favorite{name=name,uri=uri});Store.Save();Notify();}
        public void ShowFavorites(){StopDiscovery();Source=FileSource.Favorites;ClearDirectoryHistory();CancelListing();DirectoryUri="";Files=Store.Settings.favorites.Select(f=>new FileEntry{name=f.name,uri=f.uri,directory=true}).ToList();SetStatus("收藏目录");Notify();}
        private void UpdateRecentFiles(){Files=Store?.Settings.history.Select(h=>new FileEntry{name=h.name,uri=h.uri,size=h.size,modified=h.modified,formatName=h.formatName}).ToList()??new List<FileEntry>();}
        public void ShowRecent(){StopDiscovery();Source=FileSource.Recent;ClearDirectoryHistory();CancelListing();DirectoryUri="";SetStatus("最近播放 · 按最近播放顺序显示，选择影片恢复进度");Notify();}
        public void ClearPlaybackHistory(){Store.Settings.history.Clear();Store.Save();SetStatus("播放记录已清空");Notify();}
        public void OpenRecentOrFile(FileEntry entry){if(Source==FileSource.Recent&&OpenEmbyHistoryItem(entry.uri,true))return;var record=Store.Find(entry.uri);Open(entry,record!=null&&string.IsNullOrEmpty(DirectoryUri)?record.directory:DirectoryUri);}
        public void OpenContainingDirectory(string uri)
        {
            if(OpenEmbyContainingItem(uri))return;
            var record=Store.Find(uri);string directory=ContainingDirectory(record);
            if(string.IsNullOrEmpty(directory)){SetStatus("该记录没有可用的父目录，请重新授权或从文件来源打开");return;}
            RememberBrowserLocation();UI.PrepareDirectoryNavigation();_ = Browse(directory,false,uri);
        }
        public static string ContainingDirectory(PlaybackRecord record)=>record==null?null:!string.IsNullOrEmpty(record.directory)?record.directory:FileLibrary.Parent(record.uri);
        public void ShowServers()
        {
            StopDiscovery();Source=FileSource.Smb;ClearDirectoryHistory();CancelListing();DirectoryUri="";Files=new List<FileEntry>();
#if UNITY_ANDROID && !UNITY_EDITOR
            Servers=JsonUtility.FromJson<ServerList>(AndroidBridge.Api.CallStatic<string>("listServers")).servers??Array.Empty<SmbServer>();Files=Servers.Select(s=>new FileEntry{name=string.IsNullOrEmpty(s.name)?s.host+" / "+s.share:s.name,uri="smb://"+s.id+"/",directory=true}).ToList();
#endif
            SetStatus("SMB 服务器");Notify();StartDiscovery();
        }
        public void StartDiscovery(){StopDiscovery();Smb.StartDiscovery();scan=StartCoroutine(PollDiscovery());}
        private IEnumerator PollDiscovery(){do{Discovery=Smb.PollDiscovery();DiscoveryChanged?.Invoke();if(!Discovery.running)break;yield return new WaitForSecondsRealtime(.3f);}while(true);scan=null;}
        public void StopDiscovery(){if(scan!=null){StopCoroutine(scan);scan=null;}Smb.StopDiscovery();Discovery.running=false;}
        public bool SaveServer(SmbServer server)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var result=JsonUtility.FromJson<SaveServerResult>(AndroidBridge.Api.CallStatic<string>("saveServer",JsonUtility.ToJson(server)));if(!result.ok){SetStatus(result.error);return false;}server.id=result.id;ShowServers();return true;
#else
            SetStatus("SMB 原生连接在 PICO Android 版本中可用");return false;
#endif
        }
        public SmbServer FindServer(string uri){
#if UNITY_ANDROID && !UNITY_EDITOR
            var servers=JsonUtility.FromJson<ServerList>(AndroidBridge.Api.CallStatic<string>("listServers"));return servers.servers.FirstOrDefault(s=>s.id==new Uri(uri).Host);
#else
            return null;
#endif
        }
        public void RemoveServer(string id){
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge.Api.CallStatic("removeServer",id);ShowServers();
#endif
        }
        private void CancelStorageAccess(){storageAccess?.Cancel();storageAccess?.Dispose();storageAccess=null;}
        private async Task RetryWithStoragePermission(string uri,int generation)
        {
            if(!FileLibrary.IsSharedStorage(uri))return;
            CancelStorageAccess();storageAccess=new CancellationTokenSource();var token=storageAccess.Token;
            try{if(await FileLibrary.TryRequestReadPermission(uri,token)&&!token.IsCancellationRequested&&Generation==generation&&Current?.uri==uri)Retry();}
            catch(OperationCanceledException){}
        }
        private void OnApplicationPause(bool paused){HasFocus=!paused;if(paused){StopDiscovery();UI?.CancelSmbConnection();Pause();}else if(Source==FileSource.Smb&&UI!=null&&UI.CanDiscover)StartDiscovery();}
        private void OnApplicationFocus(bool focused){HasFocus=focused;if(!focused)Pause();}
        private void OnDestroy(){SaveProgress();DisposeEmby();CancelStorageAccess();CancelFormatDetection();StopDiscovery();listing?.Cancel();subtitles?.Cancel();subtitleListing?.Cancel();Backend?.Dispose();if(Presenter!=null)Destroy(Presenter.gameObject);if(captionRoot!=null)Destroy(captionRoot.gameObject);}
    }
}
