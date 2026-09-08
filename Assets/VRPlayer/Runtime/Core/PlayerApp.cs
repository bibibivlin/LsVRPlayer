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
    public sealed class PlayerApp : MonoBehaviour
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
        private readonly Stack<string> directories=new Stack<string>();
        private List<FileEntry> playlist=new List<FileEntry>();
        private CancellationTokenSource listing,subtitles;
        private SrtDocument subtitleDocument;
        private CaptionPresenter caption;
        private Transform captionRoot;
        private bool ended,wasPresent,tracksRestored;
        private int previousState;
        private string lastError="";
        private float saveAt,diagnosticsAt;
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
            Backend.Tick();var state=Backend.Snapshot;
            Presenter.UpdateVideo(Backend.Texture,Backend.RequiresGammaConversion,state.width,state.height,state.pixelRatio);
            if(Current!=null&&state.generation==Generation){
                if(state.state!=previousState){previousState=state.state;if(state.state==3)SetStatus("媒体已准备好");else if(state.state==2)SetStatus("正在缓冲…");else if(state.state==4)SetStatus("播放完成");}
                if(!tracksRestored&&state.state==3&&state.tracks!=null&&state.tracks.Length>0){tracksRestored=true;if(Current.audio!="auto"&&state.tracks.Any(t=>t.type==1&&t.id==Current.audio))Backend.SelectTrack(Current.audio,1);if(Current.subtitle.StartsWith("track:")&&state.tracks.Any(t=>t.type==3&&t.id==Current.subtitle.Substring(6)))Backend.SelectTrack(Current.subtitle.Substring(6),3);}
                if(state.HasError&&lastError!=state.error){lastError=state.error;SetStatus("播放失败："+state.error+"。可点重试或选择其他文件。");}
                if(!state.HasError&&state.state==4&&!ended){ended=true;if(Store.Settings.sequential&&!Store.Settings.loop)Next(1);}
                if(Time.unscaledTime>=saveAt){saveAt=Time.unscaledTime+5;SaveProgress();}
                caption.SetText(subtitleDocument!=null?subtitleDocument.TextAt(state.position/1000d,Current.subtitleDelay):Current.subtitle.StartsWith("track:")?state.embeddedSubtitle:"",Store.Settings.subtitleSize,Store.Settings.subtitleStyle);
            }else caption.SetText("",Store.Settings.subtitleSize,Store.Settings.subtitleStyle);
            caption.Position(headCamera,Presenter.transform,Store.Settings);
            InputDevice head=InputDevices.GetDeviceAtXRNode(XRNode.Head);if(head.TryGetFeatureValue(CommonUsages.userPresence,out bool present)){if(wasPresent&&!present)Pause();wasPresent=present;}
#if UNITY_ANDROID && !UNITY_EDITOR
            string picked=AndroidBridge.Api.CallStatic<string>("takeFolder");if(!string.IsNullOrEmpty(picked)&&picked!="CANCELLED"){if(picked.StartsWith("ERROR:"))SetStatus(picked.Substring(6));else{AddFavorite("授权目录",picked);_ = Browse(picked);}}
#endif
            if(Time.unscaledTime>diagnosticsAt){diagnosticsAt=Time.unscaledTime+2;try{File.WriteAllText(Path.Combine(Application.persistentDataPath,"playback-diagnostics.json"),JsonUtility.ToJson(new Diagnostic{snapshot=state,frames=Backend.RenderedFrames,projection=Presenter.Settings.projection.ToString(),stereo=Presenter.Settings.stereo.ToString(),unityFps=RenderQuality.Fps,eyeWidth=XRSettings.eyeTextureWidth,eyeHeight=XRSettings.eyeTextureHeight,renderScale=RenderQuality.EffectiveScale,msaa=QualitySettings.antiAliasing,status=Status,caption=caption.Text,subtitle=Current?.subtitle??"",subtitleCandidates=SubtitleCandidates.Select(x=>x.name).ToArray()},true));}catch{} }
        }
        [Serializable] private sealed class Diagnostic {public MediaSnapshot snapshot;public int frames;public string projection,stereo,status,caption,subtitle;public string[] subtitleCandidates;public float unityFps,renderScale;public int eyeWidth,eyeHeight,msaa;}
        public void SetStatus(string value){Status=value;}
        public void Notify()=>Changed?.Invoke();
        public void Home()
        {
            StopDiscovery();Source=FileSource.Home;CancelListing();directories.Clear();DirectoryUri="";
            Files=new List<FileEntry>();
#if UNITY_ANDROID && !UNITY_EDITOR
            Files.Add(new FileEntry{name="设备存储",uri="file:///storage/emulated/0/",directory=true});Files.Add(new FileEntry{name="Movies 视频",uri="file:///storage/emulated/0/Movies/",directory=true});
#else
            Files.Add(new FileEntry{name="测试视频",uri=new Uri(Path.GetFullPath("TestMedia")).AbsoluteUri,directory=true});Files.Add(new FileEntry{name="我的视频",uri=new Uri(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)).AbsoluteUri,directory=true});
#endif
            Files.Add(new FileEntry{name="应用目录",uri=new Uri(Application.persistentDataPath).AbsoluteUri,directory=true});Status="添加目录或打开设备存储；SMB 可在服务器页配置";Notify();
        }
        public async Task Browse(string uri,bool push=true)
        {
            StopDiscovery();Source=FileSource.Directory;
            CancelListing();listing=new CancellationTokenSource();CancellationToken token=listing.Token;
            if(push&&!string.IsNullOrEmpty(DirectoryUri)&&DirectoryUri!=uri)directories.Push(DirectoryUri);
            DirectoryUri=uri;Loading=true;Status="正在读取目录…";Files=new List<FileEntry>();Notify();
            try{var result=await library.List(uri,token);if(token.IsCancellationRequested)return;Loading=false;if(result.ok){Files=(result.entries??Array.Empty<FileEntry>()).ToList();Status=$"{Files.Count} 项";}else Status=result.error;Notify();}
            catch(OperationCanceledException){}catch(Exception ex){if(!token.IsCancellationRequested){Loading=false;SetStatus("无法读取目录："+ex.Message);}}
        }
        public void Up(){if(directories.Count>0){_ = Browse(directories.Pop(),false);return;}string parent=FileLibrary.Parent(DirectoryUri);if(parent!=null)_ = Browse(parent,false);else Home();}
        public void Refresh(){switch(Source){case FileSource.Favorites:ShowFavorites();break;case FileSource.Recent:ShowRecent();break;case FileSource.Smb:ShowServers();break;case FileSource.Directory:_ = Browse(DirectoryUri,false);break;default:Home();break;}}
        public void CancelListing(){listing?.Cancel();listing?.Dispose();listing=null;Loading=false;}
        public void Open(FileEntry entry,string directory=null)
        {
            if(entry.directory){_ = Browse(entry.uri);return;}if(entry.IsSubtitle){_ = LoadSubtitle(entry.uri);return;}
            if(Backend==null){SetStatus("播放内核不可用");return;}
            SaveProgress();subtitles?.Cancel();subtitleDocument=null;SubtitleCandidates.Clear();lastError="";ended=false;tracksRestored=false;previousState=0;
            Current=Store.Touch(entry,directory??DirectoryUri);Presenter.Settings=Current.view??new ViewSettings();Current.view=Presenter.Settings;
            playlist=Files.Where(item=>item.IsVideo).OrderBy(item=>item.name,StringComparer.OrdinalIgnoreCase).ToList();
            Generation++;Backend.Open(entry.uri,Current.position,Generation);Backend.SelectTrack("off",3);
            Status="正在打开："+entry.name;UI.Show();Notify();_ = DiscoverSubtitles(Current.directory,Generation);
        }
        private async Task DiscoverSubtitles(string directory,int generation)
        {
            if(string.IsNullOrEmpty(directory))return;subtitles?.Cancel();subtitles?.Dispose();subtitles=new CancellationTokenSource();var token=subtitles.Token;
            try{
                var result=directory==DirectoryUri&&Files.Count>0?new FileResult{ok=true,entries=Files.ToArray()}:await library.List(directory,token);
                if(generation!=Generation||token.IsCancellationRequested||!result.ok)return;
                SubtitleCandidates=SubtitleMatcher.Find(Current.name,result.entries??Array.Empty<FileEntry>());
                if(playlist.Count==0)playlist=(result.entries??Array.Empty<FileEntry>()).Where(e=>e.IsVideo).OrderBy(e=>e.name,StringComparer.OrdinalIgnoreCase).ToList();
                string selected=Current.subtitle;Notify();if(!string.IsNullOrEmpty(selected)&&!selected.StartsWith("track:"))await LoadSubtitle(selected);
            }catch(OperationCanceledException){}catch(Exception ex){Debug.LogWarning("Subtitle discovery: "+ex.GetType().Name);}
        }
        public async Task LoadSubtitle(string uri)
        {
            if(Current==null){SetStatus("请先播放视频，再选择字幕");return;}
            subtitles?.Cancel();subtitles?.Dispose();subtitles=new CancellationTokenSource();var token=subtitles.Token;int generation=Generation;
            SetStatus("正在读取字幕…");
            try{var result=await library.Text(uri,SubtitleEncoding,token);if(token.IsCancellationRequested||generation!=Generation)return;if(!result.ok){SetStatus(result.error);return;}
                var document=SrtDocument.Parse(result.text);if(document.Cues.Count==0){SetStatus("未找到有效 SRT 时间轴，请检查字幕编码或文件内容");return;}
                subtitleDocument=document;Current.subtitle=uri;Backend.SelectTrack("off",3);Store.Save();SetStatus($"字幕已加载：{document.Cues.Count} 条"+(document.InvalidBlocks>0?$"，跳过 {document.InvalidBlocks} 个损坏块":""));
            }catch(OperationCanceledException){}catch(Exception ex){SetStatus("字幕读取失败："+ex.Message);}
        }
        public void DisableSubtitles(){subtitles?.Cancel();subtitleDocument=null;if(Current!=null)Current.subtitle="";Backend?.SelectTrack("off",3);Store.Save();SetStatus("字幕已关闭");}
        public void EmbeddedSubtitle(string id){subtitles?.Cancel();subtitleDocument=null;if(Current!=null)Current.subtitle="track:"+id;Backend?.SelectTrack(id,3);Store.Save();Notify();}
        public void TogglePlay(){if(Backend==null||Current==null)return;if(Backend.Snapshot.playing)Pause();else Backend.Play();}
        public void Pause(){Backend?.Pause();SaveProgress();}
        public void Seek(long milliseconds){if(Backend==null||Current==null)return;ended=false;Backend.Seek(Math.Clamp(milliseconds,0,Math.Max(0,Backend.Snapshot.duration-1)));}
        public void Skip(int seconds)=>Seek((Backend?.Snapshot.position??0)+seconds*1000L);
        public void Next(int delta){if(Current==null||playlist.Count==0)return;int index=playlist.FindIndex(x=>x.uri==Current.uri),next=index+delta;if(next<0||next>=playlist.Count){SetStatus("已到目录边界");return;}Open(playlist[next],Current.directory);}
        public void Retry(){if(Current==null)return;var record=Current;Open(new FileEntry{uri=record.uri,name=record.name,size=record.size,modified=record.modified},record.directory);}
        public void Stop(){SaveProgress();Generation++;Backend?.Stop();Current=null;subtitleDocument=null;subtitles?.Cancel();caption.SetText("",Store.Settings.subtitleSize,Store.Settings.subtitleStyle);SetStatus("播放已停止");}
        public void SetVolume(float value){Store.Settings.volume=value;Backend?.SetVolume(value);}
        public void SetSpeed(float value){Store.Settings.speed=value;Backend?.SetSpeed(value);}
        public void SaveProgress()
        {
            if(Store==null)return;var state=Backend?.Snapshot;
            if(Current!=null&&state!=null&&state.generation==Generation&&state.duration>0){Current.position=state.state==4?0:state.position;}
            Store.Save();
        }
        public void AddFavorite(string name,string uri){if(Store.Settings.favorites.All(x=>x.uri!=uri))Store.Settings.favorites.Add(new Favorite{name=name,uri=uri});Store.Save();Notify();}
        public void ShowFavorites(){StopDiscovery();Source=FileSource.Favorites;directories.Clear();CancelListing();DirectoryUri="";Files=Store.Settings.favorites.Select(f=>new FileEntry{name=f.name,uri=f.uri,directory=true}).ToList();SetStatus("收藏目录");Notify();}
        public void ShowRecent(){StopDiscovery();Source=FileSource.Recent;directories.Clear();CancelListing();DirectoryUri="";Files=Store.Settings.history.Select(h=>new FileEntry{name=h.name,uri=h.uri,size=h.size,modified=h.modified}).ToList();SetStatus("最近播放 · 选择影片恢复进度");Notify();}
        public void OpenRecentOrFile(FileEntry entry){var record=Store.Find(entry.uri);Open(entry,record!=null&&string.IsNullOrEmpty(DirectoryUri)?record.directory:DirectoryUri);}
        public void ShowServers()
        {
            StopDiscovery();Source=FileSource.Smb;directories.Clear();CancelListing();DirectoryUri="";Files=new List<FileEntry>();
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
        public void RequestStorage()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string permission="android.permission.READ_EXTERNAL_STORAGE";
            if(UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission)){Refresh();return;}
            var callbacks=new UnityEngine.Android.PermissionCallbacks();callbacks.PermissionGranted+=_=>Refresh();callbacks.PermissionDenied+=_=>SetStatus("未授予存储权限，可改用“授权目录”");UnityEngine.Android.Permission.RequestUserPermission(permission,callbacks);
#else
            SetStatus("编辑器可直接访问本机文件");
#endif
        }
        public void PickFolder(){
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBridge.Api.CallStatic("pickFolder");
#else
            SetStatus("在地址栏输入本机目录，然后选择打开");
#endif
        }
        public void Recenter(bool lying){Presenter.Recenter(lying);UI.Show();SetStatus(lying?"观看方向已对齐当前视线，可躺卧观看":"观看方向已重新居中");}
        private void OnApplicationPause(bool paused){if(paused){StopDiscovery();UI?.CancelSmbConnection();Pause();}else if(Source==FileSource.Smb&&UI!=null&&UI.CanDiscover)StartDiscovery();}
        private void OnApplicationFocus(bool focused){if(!focused)Pause();}
        private void OnDestroy(){SaveProgress();StopDiscovery();listing?.Cancel();subtitles?.Cancel();Backend?.Dispose();if(Presenter!=null)Destroy(Presenter.gameObject);if(captionRoot!=null)Destroy(captionRoot.gameObject);}
    }
}
