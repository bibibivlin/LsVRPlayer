using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using L = VRPlayer.Localization;

namespace VRPlayer
{
    public sealed partial class PlayerUI : MonoBehaviour
    {
        public Canvas Canvas{get;private set;}
        public bool Visible=>Canvas!=null&&Canvas.gameObject.activeSelf;
        public QuickControlsUI Quick{get;private set;}
        public bool AnyVisible=>Visible||Quick!=null&&Quick.Visible;
        public Canvas ActiveCanvas=>Visible?Canvas:Quick!=null&&Quick.Visible?Quick.Canvas:null;
        public int SurfaceRevision{get;private set;}
        public float LastInteraction{get;set;}
        private PlayerApp app;private Camera head;
        private RectTransform content,keyboard,subtitleNavigation;
        private readonly List<Button> subtitleTabs=new List<Button>();
        private ListScrollRect infoScroll;private RectTransform infoBody;private PlaybackInfoUI infoCards;
        private TextMeshProUGUI time,playLabel,keyboardPreview;
        private string lastStatus="";
        private Slider seek;private SeekGesture seekGesture;
        private Button fullPrevious,fullNext,fullPlay;private Button[] fullTransportIcons;private float infoRefreshAt;
        private VirtualFileList subtitleList;
        private Slider volumeSlider,fullSpeedSlider;private TextMeshProUGUI fullSpeedNumber;
        private TMP_InputField address,filter,keyboardTarget;
        private string tab="文件",query="";
        private int sort,subtitlePage;
        private bool subtitleBrowse,subtitleFileSelection,shift;
        private long subtitleBrowserEntry;
        private readonly Dictionary<string,Button> tabs=new Dictionary<string,Button>();
        private VirtualFileList fileList;
        private EmbyBrowserUI embyBrowser;
        private string fileKey="",modal="";
        private string fileViewKey="",fileQuery="";
        private bool fileRowsReady,fileRestorePending,returningDirectory;
        private sealed class FileViewPosition {public string query;public float offset;}
        private readonly Dictionary<string,FileViewPosition> filePositions=new Dictionary<string,FileViewPosition>();
        private readonly Queue<string> filePositionOrder=new Queue<string>();
        private float fileOffset,undoUntil;
        private Favorite removedFavorite;private int removedIndex;
        private TextMeshProUGUI scanLabel;
        private Button undoButton;
        private CaptionPresenter preview,brightPreview;
        private readonly List<Button> stylePresets=new List<Button>();
        private static readonly SubtitleStyle[] stylePresetValues={SubtitleStyle.Preset(0),SubtitleStyle.Preset(1),SubtitleStyle.Preset(2),SubtitleStyle.Preset(3)};
        private int stylePresetState=-2;
        private RectTransform transportRoot;
        private TextMeshProUGUI transportLabel;
        private float transportUntil;
        private string locatedFile="";
        private readonly List<Button> projectionButtons=new List<Button>(),stereoButtons=new List<Button>();
        private Button halfButton,eyesButton;
        private int projectionButtonState=-1;
        private bool projectionDistanceWasFlat;
        public bool BlocksTransport=>Visible&&(embyBrowser!=null&&embyBrowser.BlocksInput||modal!=""||(keyboard!=null&&keyboard.gameObject.activeSelf)||(seekGesture!=null&&seekGesture.Dragging))||Quick!=null&&Quick.BlocksTransport;
        public bool CastHovered=>Visible&&tab=="文件"&&app.Source==FileSource.Emby&&!BlocksScrolling&&embyBrowser!=null&&embyBrowser.CastHovered;
        public void ScrollCast(int direction)=>embyBrowser?.ScrollCast(direction);
        public bool BlocksScrolling=>Visible&&(embyBrowser!=null&&embyBrowser.BlocksInput||keyboard!=null&&keyboard.gameObject.activeSelf||modal!=""&&modal!="legal");
        private CancellationTokenSource connection;
        public bool CanDiscover=>!privacyGate&&Visible&&tab=="文件"&&modal==""&&!keyboard.gameObject.activeSelf;
        public string SelectedTab=>tab;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public float FullOffset=>fileList?.Offset??0;
        public int FullCount=>fileList?.Count??0;
        public string FullModal=>modal;
        public string FullQuery=>query;
        public bool KeyboardVisible=>keyboard!=null&&keyboard.gameObject.activeSelf;
        public EmbyBrowserUI.WallSnapshot EmbyWall=>embyBrowser?.WallDiagnostics;
#endif
        public void Initialize(PlayerApp owner,Camera camera)
        {
            app=owner;head=camera;
            L.SetLanguage(app.Store.Settings.language);
            var root=new GameObject("Player UI",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));Canvas=root.GetComponent<Canvas>();Canvas.renderMode=RenderMode.WorldSpace;Canvas.worldCamera=camera;Canvas.sortingOrder=10;
            var rect=(RectTransform)root.transform;rect.sizeDelta=new Vector2(1440,1040);ScaleMainPanel(app.Store.Settings.mainPanelScale);
            UiFactory.RoundedBox(rect,"Background",0,0,1440,1040,UiFactory.Background,true,28,SurfaceTreatment.Frame);
            UiFactory.RoundedBox(rect,"Page inset",20,90,1400,842,UiTheme.Surface,false,24,SurfaceTreatment.Inset);
            string[] names={"文件","投影","字幕","设置","信息"};
            var navigation=UiFactory.Rect(rect,"Primary tabs",0,14,(names.Length-1)*126+118,64);navigation.anchorMin=navigation.anchorMax=new Vector2(.5f,1);navigation.pivot=new Vector2(.5f,1);navigation.anchoredPosition=new Vector2(0,-14);
            for(int i=0;i<names.Length;i++){string name=names[i];tabs[name]=UiFactory.TabButton(navigation,name,i*126,0,118,64,()=>SelectTab(name));}
            UiFactory.Label(rect,"L's VR Player",1060,14,348,64,30,TextAlignmentOptions.Right);
            content=UiFactory.Rect(rect,"Page",44,114,UiTheme.PageWidth,UiTheme.PageHeight);
            subtitleNavigation=UiFactory.Rect(rect,"Subtitle tabs",0,114,642,60);subtitleNavigation.anchorMin=subtitleNavigation.anchorMax=new Vector2(.5f,1);subtitleNavigation.pivot=new Vector2(.5f,1);subtitleNavigation.anchoredPosition=new Vector2(0,-114);
            string[] sections={"选择","位置","样式"};
            for(int i=0;i<sections.Length;i++){int section=i;subtitleTabs.Add(UiFactory.TabButton(subtitleNavigation,sections[i],i*220,0,202,60,()=>{subtitlePage=section;Draw();}));}
            subtitleNavigation.gameObject.SetActive(false);
            fullPrevious=UiFactory.IconButton(rect,"上一部",QuickIcon.Previous,824,950,()=>app.Next(-1));
            var play=fullPlay=UiFactory.IconButton(rect,"播放",QuickIcon.Play,904,950,()=>app.TogglePlay(),true);playLabel=play.GetComponentInChildren<TextMeshProUGUI>(true);
            fullNext=UiFactory.IconButton(rect,"下一部",QuickIcon.Next,984,950,()=>app.Next(1));fullTransportIcons=new[]{fullPrevious,fullNext,fullPlay};UiFactory.ConfigureTransport(fullPrevious,false);UiFactory.ConfigureTransport(fullPlay,true);UiFactory.ConfigureTransport(fullNext,false);
            seek=UiFactory.Slider(rect,276,958,532,0,1,0,_=>{});seek.name="播放进度";seekGesture=seek.gameObject.AddComponent<SeekGesture>();seekGesture.Commit=value=>app.Seek((long)(value*(app.Backend?.Snapshot.duration??0)));
            time=UiFactory.Label(rect,"00:00 / 00:00",32,955,208,54,23,TextAlignmentOptions.Right);
            var mute=UiFactory.IconButton(rect,"音量",QuickIcon.Volume,1064,950,app.ToggleMute,tooltip:"静音 / 恢复音量");UiFactory.ConfigureTransport(mute,false);volumeSlider=UiFactory.Slider(rect,1144,958,104,0,1,app.Store.Settings.volume,app.SetVolume);volumeSlider.name="主面板音量";
            UiFactory.IconButton(rect,"重试",QuickIcon.Refresh,1264,950,app.Retry,tooltip:"重试播放");UiFactory.IconButton(rect,"停止",QuickIcon.Stop,1344,950,app.Stop,tooltip:"停止播放");
            keyboard=UiFactory.Box(rect,"Virtual keyboard",30,340,1380,492,new Color(.025f,.065f,.09f,1),true);keyboard.gameObject.SetActive(false);
            var hud=new GameObject("Transport feedback",typeof(RectTransform),typeof(Canvas));transportRoot=(RectTransform)hud.transform;transportRoot.sizeDelta=new Vector2(1200,120);transportRoot.localScale=Vector3.one*.001f;hud.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;hud.GetComponent<Canvas>().sortingOrder=12;
            UiFactory.Box(transportRoot,"Backdrop",0,0,1200,120,UiFactory.Background);transportLabel=UiFactory.Label(transportRoot,"",20,0,1160,120,26);transportLabel.textWrappingMode=TextWrappingModes.Normal;hud.SetActive(false);
            Quick=gameObject.AddComponent<QuickControlsUI>();Quick.Initialize(this,app,head);
            embyBrowser=gameObject.AddComponent<EmbyBrowserUI>();embyBrowser.Initialize(app,ShowKeyboard,CloseKeyboard,()=>SurfaceRevision++);
            app.Changed+=OnAppChanged;app.DiscoveryChanged+=RefreshFileRows;RefreshNavigationLabels();Draw();Show();
        }
        private void RefreshNavigationLabels()
        {
            float x=0;
            foreach(var pair in tabs){
                var label=pair.Value.GetComponentInChildren<TextMeshProUGUI>();label.text=L.T(pair.Key);
                float width=Mathf.Max(118,Mathf.Ceil(label.preferredWidth)+32);
                var rect=(RectTransform)pair.Value.transform;rect.anchoredPosition=new Vector2(x,0);rect.sizeDelta=new Vector2(width,64);label.rectTransform.sizeDelta=new Vector2(width-24,64);x+=width+8;
            }
            if(tabs.Count>0)((RectTransform)tabs["文件"].transform.parent).sizeDelta=new Vector2(x-8,64);
            string[] sections={"选择","位置","样式"};
            for(int i=0;i<subtitleTabs.Count;i++)subtitleTabs[i].GetComponentInChildren<TextMeshProUGUI>().text=L.T(sections[i]);
        }
        public void RefreshLanguage()
        {
            if(app?.Store==null||Canvas==null||!L.SetLanguage(app.Store.Settings.language))return;
            SurfaceRevision++;seekGesture?.Cancel();CloseKeyboard();RefreshNavigationLabels();Quick?.RefreshLanguage();
            transportUntil=0;lastStatus="";legalEnglish=L.EnglishLegalFirst;
            if(privacyGate){if(modal=="legal")ShowLegalDocument(legalDocument);else DrawPrivacyConsent();}
            else if(modal=="language")DrawLanguageSelection();
            else if(modal=="background")DrawBackgroundSelection();
            else if(modal=="cinema-comfort")DrawCinemaComfort();
            else if(modal=="legal")ShowLegalDocument(legalDocument);
            else if(modal=="")Draw();
        }
        private string CurrentFileKey=>$"{app.Source}|{app.DirectoryUri}|{query}|{sort}|{subtitleBrowse}";
        private string CurrentFileViewKey=>$"{app.Source}|{app.DirectoryUri}|{sort}|{subtitleBrowse}";
        public sealed class FileViewState {public string query;public int sort;public bool subtitles,subtitleSelection;public float offset;}
        public FileViewState CaptureFileView()
        {
            var position=filePositions.TryGetValue(CurrentFileViewKey,out var saved)?saved:null;
            string visibleQuery=fileList!=null?fileQuery:query;
            return new FileViewState{query=visibleQuery,sort=sort,subtitles=subtitleBrowse,subtitleSelection=subtitleFileSelection,offset=fileList!=null&&fileRowsReady?fileList.Offset:position!=null&&position.query==visibleQuery?position.offset:0};
        }
        public void RestoreFileView(FileViewState state,FileSource source,string uri)
        {
            RememberFileOffset();CloseKeyboard();locatedFile="";fileKey="";
            if(state==null)return;
            query=state.query;sort=state.sort;subtitleBrowse=state.subtitles;subtitleFileSelection=state.subtitleSelection;
            RememberFilePosition($"{source}|{uri}|{sort}|{subtitleBrowse}",query,state.offset);
        }
        private void RememberFileOffset()
        {
            if(fileList==null||!fileRowsReady||modal!=""||string.IsNullOrEmpty(fileViewKey))return;
            fileOffset=fileList.Offset;
            RememberFilePosition(fileViewKey,fileQuery,fileOffset);
        }
        private void RememberFilePosition(string key,string savedQuery,float offset)
        {
            if(!filePositions.ContainsKey(key)){if(filePositionOrder.Count>=64)filePositions.Remove(filePositionOrder.Dequeue());filePositionOrder.Enqueue(key);}
            filePositions[key]=new FileViewPosition{query=savedQuery,offset=offset};
        }
        private void UpDirectory()
        {
            RememberFileOffset();CloseKeyboard();query="";locatedFile="";returningDirectory=true;
            try{app.Up();}finally{returningDirectory=false;}
        }
        private void OnAppChanged(){if(modal!=""||keyboard.gameObject.activeSelf)return;if(tab=="文件"){if(app.Source==FileSource.Emby&&embyBrowser!=null&&fileKey==CurrentFileKey)embyBrowser.Render();else if(fileList!=null&&fileKey==CurrentFileKey)RefreshFileRows();else Draw();}else if(tab=="字幕"&&subtitlePage==0)Draw();}
        public void SelectTab(string name)
        {
            if(privacyGate)return;
            if(!tabs.ContainsKey(name))return;
            CancelSmbConnection();app.StopDiscovery();CloseKeyboard();modal="";tab=name;if(name!="文件")subtitleFileSelection=false;Draw();
            if(CanDiscover&&app.Source==FileSource.Smb)app.StartDiscovery();
        }
        public void PrepareDirectoryNavigation(){RememberFileOffset();query="";subtitleBrowse=subtitleFileSelection=false;fileOffset=0;fileKey="";locatedFile="";SelectTab("文件");}
        public void ShowTransport(string text){transportLabel.text=L.Message(text);transportUntil=Time.unscaledTime+1.6f;transportRoot.gameObject.SetActive(true);}
        private void UpdateStatusNotice()
        {
            if(lastStatus==app.Status)return;lastStatus=app.Status;
            if(string.IsNullOrEmpty(lastStatus)||lastStatus.StartsWith("正在")||lastStatus.EndsWith(" 项")||lastStatus.StartsWith("Emby · ")||lastStatus=="媒体已准备好"||lastStatus=="收藏目录"||lastStatus=="SMB 服务器"||lastStatus.StartsWith("最近播放 · ")||lastStatus.StartsWith("添加目录或打开设备存储")||lastStatus=="选择本地目录或 SMB 服务器开始观看")return;
            transportLabel.text=L.Message(lastStatus);transportUntil=Time.unscaledTime+5;transportRoot.gameObject.SetActive(true);
        }
        private void Update()
        {
            if(Canvas==null)return;var snapshot=app.Backend?.Snapshot;
            fullPrevious.interactable=app.CanNavigateAdjacent(-1);fullNext.interactable=app.CanNavigateAdjacent(1);fullPlay.interactable=app.Current!=null;
            foreach(var button in fullTransportIcons)button.GetComponentInChildren<QuickIconGraphic>().color=button.interactable?(button==fullPlay?UiTheme.Ink:UiTheme.Text):UiTheme.Muted*.65f;
            if(volumeSlider!=null&&!(volumeSlider as OwnedSlider).HasOwner)volumeSlider.SetValueWithoutNotify(app.Store.Settings.volume);
            if(fullSpeedSlider!=null&&!(fullSpeedSlider as OwnedSlider).HasOwner){fullSpeedSlider.SetValueWithoutNotify(app.Store.Settings.speed);fullSpeedNumber.text=L.F("{0:0.0#} 倍", app.Store.Settings.speed);}
            if(transportRoot!=null){transportRoot.gameObject.SetActive(Time.unscaledTime<transportUntil);transportRoot.SetPositionAndRotation(head.transform.position+head.transform.rotation*new Vector3(0,-.35f,1.9f),head.transform.rotation);}
            if(tab=="投影"&&modal==""&&halfButton!=null){
                var view=app.Presenter.Settings;
                if((view.projection==Projection.Flat)!=projectionDistanceWasFlat)DrawProjectionPage();
                int state=(int)view.projection|((int)view.stereo<<3)|(view.half?32:0)|(view.swapEyes?64:0);
                if(state!=projectionButtonState){
                    projectionButtonState=state;
                    for(int i=0;i<projectionButtons.Count;i++)UiFactory.SetAccent(projectionButtons[i],(int)view.projection==i);
                    for(int i=0;i<stereoButtons.Count;i++)UiFactory.SetAccent(stereoButtons[i],(int)view.stereo==i);

                }
            }
            if(undoButton!=null)undoButton.gameObject.SetActive(removedFavorite!=null&&Time.unscaledTime<undoUntil);
            if(preview!=null){
                var style=app.Store.Settings.subtitleStyle;
                preview.SetText(L.T("字幕样式实时预览\nSubtitle preview"),32,style,app.Presenter.Settings.projection==Projection.Flat?app.Store.Settings.cinemaComfort.subtitleBrightness:1);brightPreview?.SetText(L.T("字幕样式实时预览\nSubtitle preview"),32,style,app.Presenter.Settings.projection==Projection.Flat?app.Store.Settings.cinemaComfort.subtitleBrightness:1);
                int preset=-1;for(int i=0;i<stylePresetValues.Length;i++)if(SameStyle(style,stylePresetValues[i])){preset=i;break;}
                if(preset!=stylePresetState){stylePresetState=preset;for(int i=0;i<stylePresets.Count;i++)UiFactory.SetAccent(stylePresets[i],i==preset);}
            }
            UpdateStatusNotice();
            if(snapshot!=null){bool current=app.Current!=null&&snapshot.generation==app.Generation;playLabel.text=current&&snapshot.playing?L.T("暂停"):L.T("播放");playLabel.transform.parent.GetComponentInChildren<QuickIconGraphic>().Icon=current&&snapshot.playing?QuickIcon.Pause:QuickIcon.Play;if(!seekGesture.Dragging)seek.SetValueWithoutNotify(current&&snapshot.duration>0?snapshot.position/(float)snapshot.duration:0);time.text=current?Clock(snapshot.position)+" / "+Clock(snapshot.duration):"00:00 / 00:00";}
            if(infoCards!=null&&Visible&&tab=="信息"&&Time.unscaledTime>=infoRefreshAt){infoRefreshAt=Time.unscaledTime+.25f;infoCards.Update(app,snapshot);}
            if(Visible&&modal==""&&app.Store.Settings.autoHide&&snapshot!=null&&snapshot.playing&&!keyboard.gameObject.activeSelf&&Time.unscaledTime-LastInteraction>18)Hide();
        }
        private static string Clock(long milliseconds){var duration=TimeSpan.FromMilliseconds(Math.Max(0,milliseconds));return duration.TotalHours>=1?$"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}":$"{duration.Minutes:00}:{duration.Seconds:00}";}
        public void Show(){if(Canvas==null)return;bool opening=!Visible;Quick?.Hide();SurfaceRevision++;Canvas.gameObject.SetActive(true);if(opening)Draw();LastInteraction=Time.unscaledTime;Canvas.transform.position=head.transform.position+head.transform.rotation*new Vector3(0,-.05f,app.Store.Settings.mainPanelDistance);Canvas.transform.rotation=head.transform.rotation;if(opening&&CanDiscover&&app.Source==FileSource.Smb)app.StartDiscovery();}
        public void MoveMainPanel(float delta){if(Visible)Canvas.transform.position+=Canvas.transform.forward*delta;}
        public void ScaleMainPanel(float scale){if(Canvas!=null)Canvas.transform.localScale=Vector3.one*(.00145f*scale);}
        public void Hide(){if(privacyGate)return;CancelPermissionNotice();CloseKeyboard();CancelSmbConnection();app.StopDiscovery();embyBrowser?.Suspend();Quick?.Hide();seekGesture?.Cancel();if(Canvas!=null)Canvas.gameObject.SetActive(false);SurfaceRevision++;app?.Store?.Save();}
        public void Toggle(){if(Visible)Hide();else Show();}
        public void ShowQuick(){if(Quick==null||Visible)return;Quick.Show();SurfaceRevision++;}
        public void HideQuick(){Quick?.Hide();SurfaceRevision++;app?.Store?.Save();}
        public void ToggleQuick(){if(Visible)return;if(Quick!=null&&Quick.Visible)HideQuick();else ShowQuick();}
        public void StopAndShowFiles(){Quick?.Hide();app.Stop();SelectTab("文件");Show();}
        public bool Back()
        {
            if(!AnyVisible)return false;
            LastInteraction=Time.unscaledTime;SurfaceRevision++;
            if(privacyGate){DrawPrivacyConsent();return true;}
            if(modal=="storage-permission"){CompletePermissionNotice(false);return true;}
            if(Quick!=null&&Quick.Visible){if(!Quick.TryBack())HideQuick();return true;}
            if(keyboard!=null&&keyboard.gameObject.activeSelf){CompleteKeyboard();return true;}
            if(modal!=""){ReturnFromModal();return true;}
            if(tab=="文件"){
                if(app.Source==FileSource.Emby&&embyBrowser!=null&&embyBrowser.TryBack())return true;
                if(subtitleFileSelection&&app.BrowserHistoryPosition<=subtitleBrowserEntry){subtitleBrowse=false;subtitlePage=0;SelectTab("字幕");return true;}
                if(app.BrowseBack())return true;
                if(subtitleFileSelection){subtitleBrowse=false;subtitlePage=0;SelectTab("字幕");return true;}
            }
            Hide();return true;
        }
        public void PlaybackOpened(bool closeMainPanel=false){SurfaceRevision++;seekGesture?.Cancel();Quick?.PlaybackOpened();if(app?.Source==FileSource.Recent)fileList?.SetOffset(0);LastInteraction=Time.unscaledTime;if(closeMainPanel&&Visible)Hide();}
        private void Draw()
        {
            CancelPermissionNotice();
            if(content==null)return;RememberFileOffset();fileRowsReady=false;embyBrowser?.ReleasePanel();UiFactory.Clear(content);infoScroll=null;infoBody=null;infoCards=null;address=filter=null;fileList=null;subtitleList=null;preview=brightPreview=null;scanLabel=null;undoButton=null;fullSpeedSlider=null;fullSpeedNumber=null;modal="";
            if(returningDirectory&&filePositions.TryGetValue(CurrentFileViewKey,out var parentPosition))query=parentPosition.query;
            foreach(var pair in tabs)UiFactory.SetAccent(pair.Value,pair.Key==tab);
            subtitleNavigation.gameObject.SetActive(tab=="字幕");
            if(tab=="文件")DrawFiles();else if(tab=="投影")DrawProjection();else if(tab=="字幕")DrawSubtitles();else if(tab=="设置")DrawSettings();else DrawInfo();
        }
        private void DrawFiles()
        {
            UiFactory.Box(content,"Source divider",176,0,1,UiTheme.PageHeight,new Color(.17f,.30f,.37f,.35f));
            string[] labels={L.T("本地"),"SMB","Emby",L.T("收藏"),L.T("最近")};
            Action[] actions={app.Home,app.ShowServers,app.ShowEmby,app.ShowFavorites,app.ShowRecent};
            bool smbDirectory=app.Source==FileSource.Directory&&app.DirectoryUri.StartsWith("smb://",StringComparison.OrdinalIgnoreCase);
            bool[] selected={app.Source==FileSource.Home||app.Source==FileSource.Directory&&!smbDirectory,app.Source==FileSource.Smb||smbDirectory,app.Source==FileSource.Emby,app.Source==FileSource.Favorites,app.Source==FileSource.Recent};
            QuickIcon[] sourceIcons={QuickIcon.Local,QuickIcon.Network,QuickIcon.Play,QuickIcon.Star,QuickIcon.Clock};
            for(int i=0;i<labels.Length;i++){int index=i;UiFactory.SourceButton(content,labels[i],sourceIcons[i],0,i*80,160,64,()=>ChangeSource(actions[index]),selected[i]);}
            var page=UiFactory.Rect(content,"Browser page",196,0,1156,UiTheme.PageHeight);
            if(app.Source==FileSource.Emby){fileKey=CurrentFileKey;embyBrowser.Bind(page);return;}
            undoButton=UiFactory.Button(page,L.T("撤销移除"),868,724,264,54,UndoFavorite);undoButton.gameObject.SetActive(false);
            if(app.Source==FileSource.Smb){
                scanLabel=UiFactory.Label(page,"",0,0,916,64,23);
                UiFactory.IconButton(page,"手动添加",QuickIcon.Add,932,0,DrawServerForm,tooltip:"添加 SMB 服务器");UiFactory.IconButton(page,"重新扫描",QuickIcon.Refresh,1012,0,app.StartDiscovery);
                UiFactory.IconButton(page,"停止扫描",QuickIcon.Stop,1092,0,()=>{app.StopDiscovery();RefreshFileRows();});
            }else{
                string originalUri=app.DirectoryUri;
                UiFactory.IconButton(page,"返回",QuickIcon.Back,0,0,()=>Back());UiFactory.IconButton(page,"上级",QuickIcon.Up,80,0,UpDirectory);
                address=UiFactory.Input(page,FileLibrary.DisplayAddress(originalUri),L.T("输入目录或选择来源"),160,0,676,64,ShowKeyboard);
                UiFactory.IconButton(page,"打开",QuickIcon.Open,852,0,()=>{query="";_ = app.Browse(FileLibrary.ParseAddress(address.text,originalUri));},tooltip:"打开输入的地址");
                UiFactory.IconButton(page,"刷新",QuickIcon.Refresh,932,0,app.Refresh);
                UiFactory.Button(page,L.T("取消读取"),1012,0,144,64,()=>{app.CancelListing();app.SetStatus("已取消读取");}).name="取消";
            }
            filter=UiFactory.Input(page,query,L.T("搜索当前目录"),0,80,652,64,ShowKeyboard);filter.onValueChanged.AddListener(value=>query=value);
            UiFactory.IconButton(page,"搜索",QuickIcon.Search,668,80,()=>{query=filter.text;Draw();});
            var favorite=UiFactory.IconButton(page,"收藏",QuickIcon.Star,748,80,()=>{if(!string.IsNullOrEmpty(app.DirectoryUri))app.AddFavorite(Uri.UnescapeDataString(app.DirectoryUri.Split('/').LastOrDefault(x=>x.Length>0)??L.T("目录")),app.DirectoryUri);});favorite.name="Favorite directory";favorite.interactable=!string.IsNullOrEmpty(app.DirectoryUri);
            var order=UiFactory.Button(page,app.Source==FileSource.Recent?L.T("最近优先"):new[]{L.T("名称排序"),L.T("日期排序"),L.T("大小排序")}[sort],828,80,156,64,()=>{sort=(sort+1)%3;Draw();});order.name=app.Source==FileSource.Recent?L.T("最近播放优先"):order.name;order.interactable=app.Source!=FileSource.Recent;
            var mode=UiFactory.Button(page,app.Source==FileSource.Recent?L.T("播放历史"):subtitleBrowse?L.T("字幕文件"):L.T("视频文件"),1000,80,156,64,()=>{subtitleBrowse=!subtitleBrowse;Draw();});mode.interactable=app.Source!=FileSource.Recent;
            UiFactory.Label(page,L.T("名称"),18,160,852,42,24).color=UiTheme.Muted;UiFactory.Label(page,L.T("大小 / 操作"),922,160,210,42,23,TextAlignmentOptions.Right).color=UiTheme.Muted;
            float listHeight=page.rect.height-208;
            var viewport=UiFactory.Rect(page,"File list",0,208,1156,listHeight);fileList=viewport.gameObject.AddComponent<VirtualFileList>();fileList.Initialize(1156,listHeight,directoryStyle:true);undoButton.transform.SetAsLastSibling();
            fileViewKey=CurrentFileViewKey;fileQuery=query;fileKey=CurrentFileKey;fileRestorePending=true;
            fileOffset=filePositions.TryGetValue(fileViewKey,out var position)&&position.query==query?position.offset:0;
            RefreshFileRows();
        }
        private static string RecentDescription(PlaybackRecord record)
        {
            if(record==null)return "";
            string source=EmbyIdentity.TryParse(record.uri,out _,out _)?"Emby":(record.uri??"").StartsWith("smb://",StringComparison.OrdinalIgnoreCase)?"SMB":L.T("本地");
            string date=DateTimeOffset.TryParse(record.lastPlayed,out var time)?time.ToLocalTime().ToString("MM-dd HH:mm:ss"):L.T("时间未知");
            return source+" · "+date+L.T(" · 进度 ")+Clock(record.position);
        }
        private void ChangeSource(Action change){RememberFileOffset();CloseKeyboard();CancelSmbConnection();query="";fileOffset=0;fileKey="";SurfaceRevision++;change();}
        private void RefreshFileRows()
        {
            if(fileList==null||tab!="文件"||modal!="")return;
            RememberFileOffset();if(app.Loading)fileRestorePending=true;
            string DisplayName(FileEntry entry)=>app.Source==FileSource.Home?L.T(entry.name):entry.name;
            IEnumerable<FileEntry> items=app.Files.Where(e=>(e.directory||(app.Source==FileSource.Recent?e.IsVideo:subtitleBrowse?e.IsSubtitle:e.IsVideo))&&(string.IsNullOrEmpty(query)||(DisplayName(e)??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0));
            if(app.Source!=FileSource.Recent)items=sort==1?items.OrderByDescending(e=>e.directory).ThenByDescending(e=>e.modified):sort==2?items.OrderByDescending(e=>e.directory).ThenByDescending(e=>e.size):items.OrderByDescending(e=>e.directory).ThenBy(DisplayName,StringComparer.OrdinalIgnoreCase);
            var list=items.ToList();var rows=new List<BrowserRow>();
            if(app.Source==FileSource.Smb)rows.Add(new BrowserRow{key="saved",label=L.T("已保存的共享"),sectionHeader=true});
            foreach(FileEntry item in list){
                string prefix="";
                var row=new BrowserRow{key=item.uri,label=prefix+DisplayName(item),icon=item.directory?QuickIcon.Folder:item.IsSubtitle?QuickIcon.Subtitle:QuickIcon.Film,selected=app.Current?.uri==item.uri,detail=item.directory?L.T("目录"):Size(item.size),open=()=>{query="";if(item.directory)_ = app.Browse(item.uri,physicalParent:app.Source==FileSource.Directory?app.DirectoryUri:null);else if(item.IsSubtitle){_ = app.LoadSubtitle(item.uri);SelectTab("字幕");}else app.OpenRecentOrFile(item);}};
                if(app.Source==FileSource.Favorites){row.secondary=L.T("移除");row.actionIcon=QuickIcon.Trash;row.action=()=>RemoveFavorite(item.uri);}
                else if(app.Source==FileSource.Smb){row.secondary=L.T("编辑");row.actionIcon=QuickIcon.Edit;row.action=()=>DrawServerForm(app.FindServer(item.uri));}
                else if(app.Source==FileSource.Recent){row.subtitle=RecentDescription(app.Store.Find(item.uri));row.secondary=EmbyIdentity.TryParse(item.uri,out _,out _)?L.T("影片详情"):L.T("所在目录");row.action=()=>app.OpenContainingDirectory(item.uri);}
                if(app.Source==FileSource.Directory&&item.uri==app.LocateFileUri)row.selected=true;
                rows.Add(row);
            }
            if(app.Source==FileSource.Smb){
                rows.Add(new BrowserRow{key="discovered",label=L.T("局域网发现"),sectionHeader=true});
                foreach(var host in app.Discovery.hosts??Array.Empty<DiscoveredSmbHost>()){
                    var found=host;if(!string.IsNullOrEmpty(query)&&!((host.name??"")+host.host).Contains(query,StringComparison.OrdinalIgnoreCase))continue;
                    rows.Add(new BrowserRow{key="host:"+host.host+":"+host.port,label=(string.IsNullOrEmpty(host.name)?host.host:host.name)+"  ·  "+host.host+":"+host.port,detail=L.T("选择共享"),open=()=>ConnectHost(found)});
                }
                if(scanLabel!=null)scanLabel.text=app.Discovery.running?L.F("正在发现… 已探测 {0} / {1}  ·  {2} 台", app.Discovery.tested, app.Discovery.total, app.Discovery.hosts.Length):!string.IsNullOrEmpty(app.Discovery.error)?L.Message(app.Discovery.error):app.Discovery.complete?L.F("扫描完成 · {0} 台设备", app.Discovery.hosts.Length):L.T("扫描已停止 / 未完成");
            }
            if(rows.Count==0)rows.Add(new BrowserRow{key="empty",label=app.Loading?L.T("正在读取…"):L.T("没有匹配文件，可切换来源或清空搜索。")});
            fileList.SetRows(rows);
            if(!app.Loading&&fileRestorePending){fileList.SetOffset(fileOffset);fileRestorePending=false;}
            fileRowsReady=!app.Loading&&rows.Any(row=>row.open!=null||row.action!=null);
            if(!app.Loading&&!string.IsNullOrEmpty(app.LocateFileUri)&&locatedFile!=app.LocateFileUri){int index=rows.FindIndex(row=>row.key==app.LocateFileUri);if(index>=0){fileList.SetOffset(index*VirtualFileList.RowPitch);fileOffset=fileList.Offset;}locatedFile=app.LocateFileUri;}
        }
        private void RemoveFavorite(string uri){var item=app.Store.RemoveFavorite(uri,out int index);if(item==null)return;removedFavorite=item;removedIndex=index;undoUntil=Time.unscaledTime+5;app.ShowFavorites();app.SetStatus("已移除收藏，可在 5 秒内撤销");}
        private void UndoFavorite(){if(removedFavorite==null||Time.unscaledTime>=undoUntil)return;app.Store.RestoreFavorite(removedFavorite,removedIndex);removedFavorite=null;app.ShowFavorites();}
        private static string Size(long bytes)=>bytes>=1024L*1024*1024?$"{bytes/(1024d*1024*1024):0.00} GB":bytes>=1024*1024?$"{bytes/(1024d*1024):0.0} MB":$"{bytes/1024d:0} KB";
        private void DrawProjection()
        {
            projectionButtons.Clear();stereoButtons.Clear();projectionButtonState=-1;
            var view=app.Presenter.Settings;projectionDistanceWasFlat=view.projection==Projection.Flat;
            UiFactory.Label(content,L.T("投影与立体布局"),0,0,UiTheme.PageWidth,54,34);
            var formats=UiFactory.RoundedBox(content,"Projection and stereo",0,70,664,544,UiTheme.Surface,false,22,SurfaceTreatment.Inset);
            var geometry=UiFactory.RoundedBox(content,"Viewing geometry",688,70,664,544,UiTheme.Surface,false,22,SurfaceTreatment.Inset);
            UiFactory.Label(formats,L.T("画面投影"),24,24,616,44,28);
            string[] names={L.T("平面 / 曲面"),"VR 180°","VR 360°",L.T("鱼眼"),"Cubemap 3×2","EAC 3×2"};
            QuickIcon[] icons={QuickIcon.Flat,QuickIcon.VR180,QuickIcon.Globe,QuickIcon.Fisheye,QuickIcon.Cubemap,QuickIcon.Eac};
            const float tileWidth=(664-48-32)/3f,tilePitch=tileWidth+16;
            for(int i=0;i<names.Length;i++){int index=i;projectionButtons.Add(UiFactory.OptionTile(formats,names[i],icons[i],24+(i%3)*tilePitch,80+(i/3)*108,tileWidth,96,()=>{app.ChangeFormat(v=>v.projection=(Projection)index);Draw();},(int)view.projection==i));}
            UiFactory.Label(formats,L.T("立体布局"),24,304,616,44,28);
            string[] stereo={L.T("普通 2D"),L.T("左右 SBS"),L.T("上下 OU")};QuickIcon[] stereoIcons={QuickIcon.Flat,QuickIcon.Sbs,QuickIcon.Ou};
            for(int i=0;i<3;i++){int index=i;stereoButtons.Add(UiFactory.OptionTile(formats,stereo[i],stereoIcons[i],24+i*tilePitch,360,tileWidth,96,()=>{app.ChangeFormat(v=>v.stereo=(StereoLayout)index);Draw();},(int)view.stereo==i));}
            halfButton=UiFactory.ToggleRow(formats,L.T("Half 半宽/半高"),L.T("半幅"),()=>app.Presenter.Settings.half,24,472,tileWidth,value=>app.ChangeFormat(v=>v.half=value),48);halfButton.GetComponentInChildren<TextMeshProUGUI>().fontSize=23;
            eyesButton=UiFactory.ToggleRow(formats,L.T("交换左右眼"),L.T("左右互换"),()=>app.Presenter.Settings.swapEyes,24+tilePitch,472,tileWidth,value=>app.ChangeFormat(v=>v.swapEyes=value),48);eyesButton.GetComponentInChildren<TextMeshProUGUI>().fontSize=23;
            UiFactory.ToggleRow(formats,L.T("单眼 2D"),L.T("单眼 2D"),()=>app.Presenter.Settings.monoEye,24+2*tilePitch,472,tileWidth,value=>app.ChangeFormat(v=>v.monoEye=value),48).GetComponentInChildren<TextMeshProUGUI>().fontSize=23;
            UiFactory.Label(geometry,view.projection==Projection.Flat?L.T("屏幕与观看参数"):L.T("全景缩放"),24,24,616,48,30);
            int parameterCount=view.projection==Projection.Flat?4:view.projection==Projection.Fisheye?2:1;
            // Space parameter groups and the final action by their visual centers.
            const float firstParameterY=88,resetY=472;
            float parameterPitch=(resetY+24-firstParameterY-40)/parameterCount;
            if(view.projection==Projection.Flat){
                CompactSetting(geometry,L.T("屏幕宽度"),24,firstParameterY,616,1,10,view.width,app.SetScreenWidth,L.T("米"));
                CompactSetting(geometry,L.T("屏幕距离"),24,firstParameterY+parameterPitch,616,1.2f,10,view.distance,app.SetViewDistance,L.T("米"));
                CompactSetting(geometry,L.T("屏幕高度"),24,firstParameterY+parameterPitch*2,616,-3,3,view.heightOffset,v=>view.heightOffset=v,L.T("米"));
                CompactSetting(geometry,L.T("屏幕曲率"),24,firstParameterY+parameterPitch*3,616,0,150,view.curvature,v=>view.curvature=v,"°");
            }else{
                CompactSetting(geometry,L.T("全景缩放"),24,firstParameterY,616,.5f,2,view.panoramaZoom,app.SetPanoramaZoom,L.T("倍"));
                if(view.projection==Projection.Fisheye)CompactSetting(geometry,L.T("鱼眼角度"),24,firstParameterY+parameterPitch,616,120,240,view.fisheyeFov,v=>app.ChangeFormat(settings=>settings.fisheyeFov=v),"°");
            }
            UiFactory.Button(geometry,view.projection==Projection.Flat?L.T("重置距离"):L.T("重置缩放"),400,resetY,240,48,()=>{app.ResetViewDistance();Draw();});
            var recognition=UiFactory.RoundedBox(content,"Format recognition",0,638,UiTheme.PageWidth,156,UiTheme.Surface,false,22,SurfaceTreatment.Inset);
            UiFactory.Label(recognition,L.T("格式识别"),24,24,1304,44,28);
            UiFactory.Button(recognition,L.T("重新识别格式"),24,84,296,48,()=>{app.RedetectFormat();Draw();});
            UiFactory.Button(recognition,L.T("撤销本次识别"),336,84,280,48,()=>{app.UndoFormatDetection();Draw();});
            UiFactory.Button(recognition,L.T("采用建议"),632,84,252,48,()=>{app.ApplyFormatSuggestion();Draw();});
            UiFactory.ToggleRow(recognition,L.T("本目录优先 VR180"),L.T("本目录优先 VR180"),()=>app.CurrentDirectoryPrefersVr180,900,84,428,app.SetDirectoryVr180Preference,48);
        }
        private void CompactSetting(Transform parent,string name,float x,float y,float width,float min,float max,float value,Action<float> change,string unit="")
        {
            UiFactory.Label(parent,name,x,y,width-164,38,25);
            var number=UiFactory.Label(parent,ValueWithUnit(value,"0.##",unit),x+width-164,y,164,38,25,TextAlignmentOptions.Right);
            var slider=UiFactory.Slider(parent,x,y+31,width,min,max,value,v=>{change(v);number.text=ValueWithUnit(v,"0.##",unit);LastInteraction=Time.unscaledTime;});slider.name=name+" slider";
        }
        private void DrawProjectionPage(){UiFactory.Clear(content);DrawProjection();}
        private static string ValueWithUnit(float value,string format,string unit)=>value.ToString(format)+(string.IsNullOrEmpty(unit)?"":" "+unit);
        private void Setting(Transform parent,string name,float y,float min,float max,float value,Action<float> change,string unit="")
        {
            UiFactory.ValueSlider(parent,name,24,y,1304,min,max,value,v=>{change(v);LastInteraction=Time.unscaledTime;},v=>ValueWithUnit(v,"0.##",unit));
        }
        private void SteppedSetting(Transform parent,string name,float y,float min,float max,float value,Action<float> change,string unit="")
        {
            const float step=.1f;
            var label=UiFactory.Label(parent,name,24,y,200,48,26);float labelWidth=Mathf.Ceil(label.preferredWidth);label.rectTransform.sizeDelta=new Vector2(labelWidth,label.rectTransform.sizeDelta.y);
            float minusX=24+labelWidth+UiTheme.ControlGap,sliderX=minusX+88+UiTheme.ControlGap;
            var number=UiFactory.Label(parent,ValueWithUnit(value,"0.0#",unit),1088,y,112,48,24,TextAlignmentOptions.Center);
            var slider=UiFactory.Slider(parent,sliderX,y,984-UiTheme.ControlGap-sliderX,min,max,value,_=>{});slider.name=name+" slider";
            if(name==L.T("播放速度")){fullSpeedSlider=slider;fullSpeedNumber=number;}
            void Apply(float next)
            {
                next=Mathf.Clamp(Mathf.Round(next/step)*step,min,max);
                slider.SetValueWithoutNotify(next);change(next);number.text=ValueWithUnit(next,"0.0#",unit);LastInteraction=Time.unscaledTime;
            }
            slider.onValueChanged.AddListener(Apply);
            UiFactory.Button(parent,"−0.1",minusX,y,88,48,()=>Apply((Mathf.Ceil(slider.value/step-.0001f)-1)*step));
            UiFactory.Button(parent,"+0.1",984,y,88,48,()=>Apply((Mathf.Floor(slider.value/step+.0001f)+1)*step));
            UiFactory.Button(parent,L.T("重置"),1216,y,112,48,()=>Apply(1));
        }
        private void DrawSubtitles()
        {
            for(int i=0;i<subtitleTabs.Count;i++)UiFactory.SetAccent(subtitleTabs[i],subtitlePage==i);
            var settings=app.Store.Settings;
            if(app.AssSubtitlesSelected&&subtitlePage!=0){
                var panel=UiFactory.RoundedBox(content,"ASS script settings",0,76,UiTheme.PageWidth,240,UiTheme.Surface);
                Setting(panel,L.T("字幕延后"),24,-10,10,app.Current?.subtitleDelay??0,app.SetSubtitleDelay,L.T("秒"));
                UiFactory.Label(panel,L.T(app.AssSubtitleNotice),24,116,1304,100,25);return;
            }
            if(subtitlePage==2){DrawSubtitleStyle();return;}
            if(subtitlePage==1){var panel=UiFactory.RoundedBox(content,"Subtitle position settings",0,76,UiTheme.PageWidth,386,UiTheme.Surface);
                Setting(panel,L.T("字幕延后"),24,-10,10,app.Current?.subtitleDelay??0,app.SetSubtitleDelay,L.T("秒"));
                Setting(panel,L.T("字幕大小"),92,12,200,settings.subtitleSize,v=>settings.subtitleSize=v);
                Setting(panel,L.T("视觉高度"),160,-4,4,settings.subtitleOffset,v=>settings.subtitleOffset=v);
                Setting(panel,L.T("字幕距离"),228,.1f,6,settings.subtitleDistance,v=>settings.subtitleDistance=v,L.T("米"));
                UiFactory.Button(panel,L.T("−0.01 米"),24,306,230,56,()=>{settings.subtitleDistance=Mathf.Max(.1f,settings.subtitleDistance-.01f);Draw();});
                UiFactory.Button(panel,L.T("+0.01 米"),270,306,230,56,()=>{settings.subtitleDistance=Mathf.Min(6,settings.subtitleDistance+.01f);Draw();});
                UiFactory.ToggleRow(panel,L.T("字幕跟随视线"),L.T("字幕跟随视线"),()=>settings.subtitleFollow,548,306,440,value=>settings.subtitleFollow=value,56);
                UiFactory.Label(content,L.T("改变距离时保持视觉大小与相对高度；字号和高度可单独调整。"),24,486,1304,60,25);return;
            }
            var choices=UiFactory.RoundedBox(content,"Subtitle selection",0,76,UiTheme.PageWidth,718,UiTheme.Surface,false,22,SurfaceTreatment.Inset);
            const float actionWidth=314,actionPitch=330;
            UiFactory.Button(choices,L.T("关闭字幕"),24,24,actionWidth,64,app.DisableSubtitles);
            UiFactory.Button(choices,L.T("手动选择字幕"),24+actionPitch,24,actionWidth,64,()=>{subtitleBrowserEntry=app.BrowserHistoryPosition;subtitleBrowse=true;subtitleFileSelection=true;SelectTab("文件");});
            string[] encodings={"Auto","UTF-8","GB18030","UTF-16LE"};int encoding=Array.IndexOf(encodings,app.SubtitleEncoding);
            UiFactory.Button(choices,L.T("编码：")+app.SubtitleEncoding,24+2*actionPitch,24,actionWidth,64,()=>{app.SubtitleEncoding=encodings[(encoding+1)%encodings.Length];if(app.Current!=null&&!string.IsNullOrEmpty(app.Current.subtitle)&&!app.Current.subtitle.StartsWith("track:"))_ = app.LoadSubtitle(app.Current.subtitle);Draw();});
            UiFactory.Button(choices,L.T("恢复自动选择"),24+3*actionPitch,24,actionWidth,64,app.RestoreAutomaticSubtitles);
            UiFactory.Label(choices,L.T("同目录匹配字幕"),24,120,1304,48,30);
            var candidates=app.SubtitleCandidates;
            var listRoot=UiFactory.Rect(choices,"Subtitle choices",24,184,1304,406);subtitleList=listRoot.gameObject.AddComponent<VirtualFileList>();subtitleList.Initialize(1304,406,fullWidthRows:true);
            var rows=candidates.Select(item=>new BrowserRow{key=item.uri,label=item.name,detail=app.Current?.subtitle==item.uri?L.T("已选择"):SubtitleFormat.Label(item.name),selected=app.Current?.subtitle==item.uri,open=()=>_ = app.LoadSubtitle(item.uri)}).ToList();
            if(rows.Count==0)rows.Add(new BrowserRow{key="empty",label=L.T("未发现匹配字幕，可手动选择字幕 文件。")});subtitleList.SetRows(rows);
            UiFactory.Button(choices,L.T("内嵌字幕与音轨…"),468,630,416,64,DrawTracks);
        }
        private void DrawSubtitleStyle()
        {
            var style=app.Store.Settings.subtitleStyle;string[] presets={L.T("白字黑边"),L.T("黄字黑边"),L.T("阴影"),L.T("半透明底")};
            stylePresets.Clear();stylePresetState=-2;
            var controls=UiFactory.RoundedBox(content,"Subtitle style settings",0,76,664,718,UiTheme.Surface,false,18,SurfaceTreatment.Inset);
            UiFactory.Label(controls,L.T("预设样式"),24,24,616,48,28);
            for(int i=0;i<4;i++){int preset=i;var button=UiFactory.Button(controls,presets[i],24+i*158,88,142,56,()=>{app.Store.Settings.subtitleStyle=SubtitleStyle.Preset(preset);Draw();},SameStyle(style,SubtitleStyle.Preset(preset)));button.GetComponentInChildren<TextMeshProUGUI>().fontSize=23;stylePresets.Add(button);}
            UiFactory.Box(controls,"Style separator",24,164,616,1,new Color(.20f,.31f,.37f,.28f));
            ColorRow(controls,L.T("文字颜色"),176,style.textColor,c=>style.textColor=c);
            UiFactory.Box(controls,"Outline separator",24,264,616,1,new Color(.20f,.31f,.37f,.28f));
            UiFactory.ToggleRow(controls,L.T("描边开关"),L.T("描边"),()=>style.outline,24,280,616,value=>style.outline=value);
            ColorRow(controls,L.T("描边颜色"),360,style.outlineColor,c=>style.outlineColor=c);
            UiFactory.ValueSlider(controls,L.T("粗细"),24,432,616,0,.4f,style.outlineWidth,v=>style.outlineWidth=v,v=>$"{v:0.00}",24,128,64);
            UiFactory.Box(controls,"Shadow separator",24,508,616,1,new Color(.20f,.31f,.37f,.28f));
            UiFactory.ToggleRow(controls,L.T("阴影开关"),L.T("阴影"),()=>style.shadow,24,528,616,value=>style.shadow=value);
            UiFactory.ValueSlider(controls,L.T("强度"),24,616,616,0,1,style.shadowStrength,v=>style.shadowStrength=v,v=>$"{v:0.00}",24,128,64);
            var previewRoot=UiFactory.RoundedBox(content,"Subtitle preview",688,76,664,718,UiTheme.Surface,false,18,SurfaceTreatment.Inset);
            UiFactory.Label(previewRoot,L.T("样式预览"),24,24,616,48,28);
            var sample=UiFactory.RoundedBox(previewRoot,"Caption samples",24,88,616,252,UiTheme.Surface,false,14);sample.gameObject.AddComponent<Mask>().showMaskGraphic=true;
            var bright=UiFactory.Box(sample,"Bright scene",0,0,616,126,new Color(.70f,.72f,.69f,1));var dark=UiFactory.Box(sample,"Dark scene",0,126,616,126,new Color(.035f,.048f,.06f,1));
            brightPreview=bright.gameObject.AddComponent<CaptionPresenter>();brightPreview.Initialize(616,126,opticalCenter:true);
            preview=dark.gameObject.AddComponent<CaptionPresenter>();preview.Initialize(616,126,opticalCenter:true);
            preview.SetText(L.T("字幕样式实时预览\nSubtitle preview"),32,style,app.Presenter.Settings.projection==Projection.Flat?app.Store.Settings.cinemaComfort.subtitleBrightness:1);brightPreview.SetText(L.T("字幕样式实时预览\nSubtitle preview"),32,style,app.Presenter.Settings.projection==Projection.Flat?app.Store.Settings.cinemaComfort.subtitleBrightness:1);
            UiFactory.Label(previewRoot,L.T("明亮 / 深色背景"),24,352,616,40,23).color=UiTheme.Muted;
            UiFactory.Box(previewRoot,"Background separator",24,416,616,1,new Color(.20f,.31f,.37f,.28f));
            UiFactory.Label(previewRoot,L.T("字幕背景"),24,436,616,44,28);
            string[] backgrounds={L.T("无背景"),L.T("文字底框"),L.T("整行底条")};
            for(int i=0;i<3;i++){int bg=i;float width=(664-48-32)/3f;UiFactory.Button(previewRoot,backgrounds[i],24+i*(width+16),496,width,56,()=>{style.background=(SubtitleBackground)bg;Draw();},(int)style.background==i);}
            ColorRow(previewRoot,L.T("背景颜色"),568,style.backgroundColor,c=>style.backgroundColor=c);
            UiFactory.ValueSlider(previewRoot,L.T("背景透明度"),24,646,616,0,1,1-style.backgroundOpacity,v=>style.backgroundOpacity=1-v,v=>$"{v*100:0}%",24,128,64);
        }
        private void ColorRow(Transform parent,string name,float y,Color selected,Action<Color> change)
        {
            var label=UiFactory.Label(parent,name,24,y,128,60,24,TextAlignmentOptions.Right);
            float labelWidth=L.Language==AppLanguage.Chinese?128:Mathf.Max(128,Mathf.Ceil(label.preferredWidth));label.rectTransform.sizeDelta=new Vector2(labelWidth,60);
            float start=24+labelWidth+16,pitch=(616-labelWidth-16-60)/4;
            Color[] colors={Color.white,Color.yellow,Color.green,Color.cyan,Color.black};string[] names={L.T("白"),L.T("黄"),L.T("绿"),L.T("青"),L.T("黑")};
            for(int i=0;i<colors.Length;i++){Color color=colors[i];UiFactory.ColorSwatch(parent,name+" · "+names[i],color,selected==color,start+i*pitch,y,()=>{change(color);Draw();});}
        }
        private static bool SameStyle(SubtitleStyle a,SubtitleStyle b)=>a.textColor==b.textColor&&a.outlineColor==b.outlineColor&&a.backgroundColor==b.backgroundColor&&a.outline==b.outline&&a.shadow==b.shadow&&Mathf.Approximately(a.outlineWidth,b.outlineWidth)&&Mathf.Approximately(a.shadowStrength,b.shadowStrength)&&Mathf.Approximately(a.backgroundOpacity,b.backgroundOpacity)&&a.background==b.background;
        private void DrawSettings()
        {
            var playback=UiFactory.RoundedBox(content,"Playback settings",0,0,UiTheme.PageWidth,288,UiTheme.Surface,false,22,SurfaceTreatment.Inset);
            UiFactory.Label(playback,L.T("播放"),24,24,1304,48,30);
            SteppedSetting(playback,L.T("播放速度"),88,.25f,3,app.Store.Settings.speed,app.SetSpeed,L.T("倍"));
            SteppedSetting(playback,L.T("视频亮度"),152,.3f,1.8f,app.Presenter.Settings.brightness,v=>app.Presenter.Settings.brightness=v);
            var qualityLabel=UiFactory.Label(playback,L.T("渲染画质"),24,216,200,48,26);float qualityLabelWidth=Mathf.Ceil(qualityLabel.preferredWidth);qualityLabel.rectTransform.sizeDelta=new Vector2(qualityLabelWidth,qualityLabel.rectTransform.sizeDelta.y);
            float qualityStart=24+qualityLabelWidth+UiTheme.ControlGap,qualityWidth=(1328-qualityStart-2*UiTheme.ControlGap)/3;string[] qualities={L.T("均衡 1.0×"),L.T("高清 1.25×"),L.T("超清 1.5×")};
            for(int i=0;i<3;i++){int quality=i;UiFactory.Button(playback,qualities[i],qualityStart+i*(qualityWidth+UiTheme.ControlGap),216,qualityWidth,48,()=>{app.RenderQuality.Select((RenderQuality)quality);Draw();},(int)app.Store.Settings.renderQuality==i);}
            var behavior=UiFactory.RoundedBox(content,"Behavior settings",0,312,664,302,UiTheme.Surface,false,22,SurfaceTreatment.Inset);
            UiFactory.Label(behavior,L.T("播放行为"),24,24,616,48,30);
            UiFactory.ToggleRow(behavior,L.T("单片循环"),L.T("单片循环"),()=>app.Store.Settings.loop,24,80,616,value=>{app.Store.Settings.loop=value;app.Backend?.SetRepeat(value);},56);
            UiFactory.ToggleRow(behavior,L.T("目录连播"),L.T("目录连播"),()=>app.Store.Settings.sequential,24,150,616,value=>app.Store.Settings.sequential=value,56);
            UiFactory.ToggleRow(behavior,L.T("自动隐藏"),L.T("自动隐藏面板"),()=>app.Store.Settings.autoHide,24,220,616,value=>app.Store.Settings.autoHide=value,56);
            var subtitles=UiFactory.RoundedBox(content,"Subtitle settings",0,638,664,156,UiTheme.Surface,false,22,SurfaceTreatment.Inset);
            UiFactory.Label(subtitles,L.T("字幕与音轨"),24,24,616,44,30);
            UiFactory.ToggleRow(subtitles,L.T("自动加载同名字幕"),L.T("自动加载同名字幕"),()=>app.Store.Settings.autoLoadSubtitles,24,80,616,app.SetAutoLoadSubtitles,52);
            var recognition=UiFactory.RoundedBox(content,"Recognition settings",688,312,664,160,UiTheme.Surface,false,22,SurfaceTreatment.Inset);
            UiFactory.Label(recognition,L.T("格式识别"),24,24,616,48,30);
            string[] detection={L.T("关闭"),L.T("标签与元数据"),L.T("增强识别")};
            for(int i=0;i<3;i++){int mode=i;var option=UiFactory.Button(recognition,detection[i],24+i*210,88,196,48,()=>{app.SetFormatDetectionMode((FormatDetectionMode)mode);Draw();},(int)app.Store.Settings.formatDetection==i);option.GetComponentInChildren<TextMeshProUGUI>().fontSize=23;}
            var other=UiFactory.RoundedBox(content,"Other settings",688,496,664,298,UiTheme.Surface,false,22,SurfaceTreatment.Inset);
            UiFactory.Label(other,L.T("界面语言"),24,24,200,48,26);
            UiFactory.Button(other,L.LanguageName(app.Store.Settings.language),240,24,400,48,DrawLanguageSelection);
            UiFactory.Button(other,L.T("观影背景"),24,88,300,56,DrawBackgroundSelection).name="Background settings";
            UiFactory.Button(other,L.T("观影舒适"),340,88,300,56,DrawCinemaComfort).name="Cinema comfort settings";
            UiFactory.Button(other,L.T("选择音轨 / 内嵌字幕"),24,152,616,56,DrawTracks);
            var clear=UiFactory.Button(other,L.T("清空播放记录"),24,216,616,56,app.ClearPlaybackHistory);UiFactory.Icon(clear.transform,QuickIcon.Trash,28,13,30,UiTheme.Muted);
        }
        private void DrawLanguageSelection()
        {
            BeginModal("language");
            UiFactory.IconButton(content,"返回",QuickIcon.Back,0,0,Draw);
            UiFactory.Label(content,L.T("界面语言"),80,0,UiTheme.PageWidth-80,64,32);
            var choices=new[]{AppLanguage.System,AppLanguage.Chinese,AppLanguage.English,AppLanguage.Japanese};
            for(int i=0;i<choices.Length;i++){
                var language=choices[i];
                var button=UiFactory.Button(content,L.LanguageName(language),224,112+i*128,904,96,()=>{app.SetLanguage(language);Draw();},app.Store.Settings.language==language);
                button.name="Language "+language;
            }
        }
        private void DrawTracks()
        {
            BeginModal("tracks");UiFactory.IconButton(content,"返回",QuickIcon.Back,0,0,Draw);UiFactory.Label(content,L.T("音轨与内嵌字幕"),80,0,UiTheme.PageWidth-80,64,32);
            UiFactory.Button(content,L.T("自动音轨"),0,88,285,56,()=>app.Backend?.SelectTrack("auto",1));UiFactory.Button(content,L.T("关闭内嵌字幕"),304,88,320,56,app.DisableSubtitles);
            var tracks=app.Backend?.Snapshot.tracks??Array.Empty<MediaTrack>();
            for(int i=0;i<Math.Min(tracks.Length,8);i++){var track=tracks[i];UiFactory.Button(content,(track.type==1?L.T("音轨  "):L.T("字幕  "))+track.name+(track.supported?"":L.T("  [解码器不支持]")),0,168+i*64,UiTheme.PageWidth,56,()=>{if(track.type==1){app.Backend?.SelectTrack(track.id,1);if(app.Current!=null)app.Current.audio=track.id;}else app.EmbeddedSubtitle(track.id);},track.selected);}
            if(tracks.Length==0)UiFactory.Label(content,L.T("请先打开视频；轨道信息将在媒体准备完成后出现。"),0,168,UiTheme.PageWidth,64,25);
        }
        private void DrawInfo()
        {
            infoRefreshAt=0;UiFactory.Label(content,L.T("播放信息与诊断"),0,0,580,54,34);
            UiFactory.Button(content,L.T("隐私政策"),640,0,220,56,()=>ShowLegalDocument("privacy"));
            UiFactory.Button(content,L.T("权限说明"),880,0,220,56,()=>ShowLegalDocument("permissions"));
            UiFactory.Button(content,L.T("开源许可"),1120,0,232,56,()=>ShowLegalDocument("notices"));
            var host=UiFactory.Rect(content,"Diagnostics scroll",0,78,UiTheme.PageWidth,716);infoScroll=host.gameObject.AddComponent<ListScrollRect>();infoScroll.horizontal=false;infoScroll.movementType=ScrollRect.MovementType.Clamped;
            var viewport=UiFactory.Box(host,"Viewport",0,0,1320,716,Color.clear,true);viewport.gameObject.AddComponent<RectMask2D>();infoScroll.viewport=viewport;
            infoBody=UiFactory.Rect(viewport,"Diagnostics",0,0,1320,PlaybackInfoUI.Height);infoScroll.content=infoBody;infoCards=new PlaybackInfoUI(infoBody);
            var rail=UiFactory.Box(host,"Scrollbar",1336,0,10,716,UiTheme.Surface,true);var bar=rail.gameObject.AddComponent<Scrollbar>();var handle=UiFactory.Box(rail,"Handle",0,0,10,716,UiTheme.Muted,true);handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;bar.handleRect=handle;bar.targetGraphic=handle.GetComponent<Image>();bar.direction=Scrollbar.Direction.BottomToTop;infoScroll.verticalScrollbar=bar;
        }
        public void CancelSmbConnection(){connection?.Cancel();connection?.Dispose();connection=null;if(modal=="connecting")Draw();}
        private void CloseKeyboard(){keyboardTarget?.DeactivateInputField();keyboardTarget=null;if(keyboard!=null)keyboard.gameObject.SetActive(false);fileList?.Block(false);subtitleList?.Block(false);embyBrowser?.Block(false);}
        private void CompleteKeyboard(){bool search=keyboardTarget==filter;CloseKeyboard();if(search)Draw();else if(tab=="文件"&&app.Source==FileSource.Emby)embyBrowser?.Render();}
        private void BeginModal(string name){CancelPermissionNotice();SurfaceRevision++;CancelSmbConnection();app.StopDiscovery();CloseKeyboard();RememberFileOffset();fileRowsReady=false;subtitleNavigation.gameObject.SetActive(false);UiFactory.Clear(content);fileList=null;subtitleList=null;infoScroll=null;infoBody=null;infoCards=null;preview=brightPreview=null;modal=name;}
        private void ReturnFromModal(){CancelSmbConnection();CloseKeyboard();Draw();if(CanDiscover&&app.Source==FileSource.Smb)app.StartDiscovery();}
        private void ConnectHost(DiscoveredSmbHost host)=>_ = EnumerateShares(new SmbServer{name=host.name,host=host.host,port=host.port});
        private async Task EnumerateShares(SmbServer server)
        {
            BeginModal("connecting");UiFactory.IconButton(content,"取消",QuickIcon.Back,0,0,ReturnFromModal);UiFactory.Label(content,L.T("连接 SMB 服务器"),80,0,UiTheme.PageWidth-80,64,32);UiFactory.Label(content,L.T("正在连接 ")+server.host+L.T(" 并读取共享…"),0,88,UiTheme.PageWidth,80,30);
            connection=new CancellationTokenSource();var token=connection.Token;
            try{
                var result=await app.Smb.Shares(server,token);if(token.IsCancellationRequested)return;
                if(!result.ok){DrawConnectionForm(server,result.error);return;}
                BeginModal("shares");UiFactory.IconButton(content,"返回",QuickIcon.Back,0,0,ReturnFromModal);UiFactory.Label(content,L.T("选择共享 · ")+server.host,80,0,UiTheme.PageWidth-80,64,30);
                var root=UiFactory.Rect(content,"Shares",0,88,UiTheme.PageWidth,450);fileList=root.gameObject.AddComponent<VirtualFileList>();fileList.Initialize(UiTheme.PageWidth,450);
                var rows=(result.shares??Array.Empty<SmbShare>()).Select(share=>new BrowserRow{key=share.name,label=share.name+"  "+share.comment,detail=L.T("打开"),open=()=>SaveSharedFolder(server,share.name)}).ToList();
                if(rows.Count==0)rows.Add(new BrowserRow{key="empty",label=L.T("服务器未返回可用磁盘共享，可手填共享名。")});fileList.SetRows(rows);
                UiFactory.Button(content,L.T("手填共享名 / 更换账号"),0,548,480,48,()=>DrawConnectionForm(server,""));
            }catch(OperationCanceledException){}catch(Exception ex){if(!token.IsCancellationRequested)DrawConnectionForm(server,L.T("连接失败：")+ex.GetType().Name);}
        }
        private void DrawConnectionForm(SmbServer server,string error)
        {
            BeginModal("login");UiFactory.IconButton(content,"返回",QuickIcon.Back,0,0,ReturnFromModal);UiFactory.Label(content,L.T("连接 ")+server.host,80,0,UiTheme.PageWidth-80,64,30);
            UiFactory.Label(content,L.Message(error),0,88,UiTheme.PageWidth,56,23);
            string[] labels={L.T("用户名（空为匿名）"),L.T("密码"),L.T("域（可留空）"),L.T("共享名（可选）")};string[] initial={server.username,server.password,server.domain,server.share};var fields=new List<TMP_InputField>();
            for(int i=0;i<4;i++){UiFactory.Label(content,labels[i],0,168+i*66,350,48,25);fields.Add(UiFactory.Input(content,initial[i],labels[i],370,168+i*66,982,48,ShowKeyboard,i==1));}
            UiFactory.ToggleRow(content,L.T("保存密码"),L.T("保存密码"),()=>server.remember,0,440,310,value=>server.remember=value,64);
            UiFactory.ToggleRow(content,L.T("SMB 加密"),L.T("SMB 加密"),()=>server.encrypt,350,440,310,value=>server.encrypt=value,64);
            UiFactory.Button(content,L.T("列出共享"),0,528,470,56,()=>{ReadCredentials();CloseKeyboard();_ = EnumerateShares(server);},true);
            UiFactory.Button(content,L.T("打开指定共享"),492,528,470,56,()=>{ReadCredentials();if(string.IsNullOrWhiteSpace(fields[3].text)){app.SetStatus("请填写共享名，或选择“列出共享”");return;}SaveSharedFolder(server,fields[3].text);});
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
            BeginModal("server");UiFactory.IconButton(content,"取消",QuickIcon.Back,0,0,ReturnFromModal);UiFactory.Label(content,existing==null?L.T("添加 SMB 服务器"):L.T("编辑 SMB 服务器"),80,0,existing==null?UiTheme.PageWidth-80:UiTheme.PageWidth-80-240-UiTheme.ControlGap,64,32);
            if(existing!=null)UiFactory.Button(content,L.T("删除配置"),UiTheme.PageWidth-240,0,240,64,()=>{app.RemoveServer(existing.id);Draw();});
            string[] labels={L.T("显示名称"),L.T("主机 / IP"),L.T("共享名"),L.T("用户名（空为匿名）"),existing==null?L.T("密码"):L.T("密码（空则保留）"),L.T("域（可留空）")};var fields=new List<TMP_InputField>();var initial=existing??new SmbServer();string[] values={initial.name,initial.host,initial.share,initial.username,"",initial.domain};
            for(int i=0;i<labels.Length;i++){float y=88+i*67;UiFactory.Label(content,labels[i],0,y,345,50,25);fields.Add(UiFactory.Input(content,values[i],labels[i],370,y,982,50,ShowKeyboard,i==4));}
            bool remember=initial.remember,encrypt=initial.encrypt;UiFactory.ToggleRow(content,L.T("保存密码"),L.T("保存密码"),()=>remember,0,493,296,value=>remember=value,56);
            UiFactory.ToggleRow(content,L.T("SMB 加密"),L.T("SMB 加密"),()=>encrypt,320,493,296,value=>encrypt=value,56);
            var port=UiFactory.Input(content,initial.port.ToString(),L.T("端口"),632,493,186,48,ShowKeyboard);UiFactory.Label(content,L.T("端口"),834,493,150,48,24);
            UiFactory.Button(content,L.T("保存并返回服务器列表"),0,560,UiTheme.PageWidth,48,()=>{if(!int.TryParse(port.text,out int parsed)||parsed<1||parsed>65535){app.SetStatus("请输入 1–65535 之间的端口");return;}
                var server=new SmbServer{id=initial.id,name=fields[0].text,host=fields[1].text,share=fields[2].text,username=fields[3].text,password=fields[4].text,domain=fields[5].text,port=parsed,remember=remember,encrypt=encrypt};if(app.SaveServer(server)){keyboard.gameObject.SetActive(false);Draw();}},true);
        }
        private void ShowKeyboard(TMP_InputField target){keyboardTarget=target;LastInteraction=Time.unscaledTime;keyboard.gameObject.SetActive(true);fileList?.Block(true);subtitleList?.Block(true);embyBrowser?.Block(true);DrawKeyboard();}
        public void ScrollList(float direction){if(Visible&&infoScroll!=null&&infoScroll.enabled&&!infoScroll.HasDragOwner){infoScroll.StopMovement();infoBody.anchoredPosition=new Vector2(0,Mathf.Clamp(infoBody.anchoredPosition.y-direction*650*Time.unscaledDeltaTime,0,Mathf.Max(0,infoBody.rect.height-infoScroll.viewport.rect.height)));return;}if(Quick!=null&&Quick.Visible){Quick.Scroll(direction);return;}if(keyboard!=null&&!keyboard.gameObject.activeSelf){if(tab=="文件"&&app.Source==FileSource.Emby)embyBrowser?.Scroll(direction);else if(tab=="字幕")subtitleList?.Scroll(direction);else fileList?.Scroll(direction);}}
        private void UpdateKeyboardPreview(){if(keyboardPreview!=null&&keyboardTarget!=null)keyboardPreview.text=keyboardTarget.contentType==TMP_InputField.ContentType.Password?new string('•',keyboardTarget.text.Length):keyboardTarget.text;}
        private void DrawKeyboard()
        {
            UiFactory.Clear(keyboard);keyboardPreview=UiFactory.Label(keyboard,"",24,24,1252,64,25);UpdateKeyboardPreview();UiFactory.IconButton(keyboard,"完成",QuickIcon.Check,1292,24,CompleteKeyboard,tooltip:"完成输入");
            string[] rows={"1234567890-_=+","qwertyuiop[]\\","asdfghjkl;:'\"","zxcvbnm,./@:#"};
            const float keyWidth=(1380-48-13*8)/14f,keyPitch=keyWidth+8;
            for(int row=0;row<rows.Length;row++)for(int col=0;col<rows[row].Length;col++){char c=rows[row][col];string key=shift?c.ToString().ToUpperInvariant():c.ToString();float left=(1380-(rows[row].Length*keyPitch-8))/2;UiFactory.Button(keyboard,key,left+col*keyPitch,104+row*72,keyWidth,60,()=>Insert(key));}
            UiFactory.Button(keyboard,shift?L.T("小写"):L.T("大写"),24,404,192,64,()=>{shift=!shift;DrawKeyboard();});UiFactory.Button(keyboard,L.T("空格"),232,404,848,64,()=>Insert(" "));UiFactory.IconButton(keyboard,"退格",QuickIcon.Backspace,1096,404,()=>{if(keyboardTarget!=null&&keyboardTarget.text.Length>0)keyboardTarget.text=keyboardTarget.text.Substring(0,keyboardTarget.text.Length-1);UpdateKeyboardPreview();});UiFactory.Button(keyboard,L.T("清空"),1176,404,180,64,()=>{if(keyboardTarget!=null)keyboardTarget.text="";UpdateKeyboardPreview();});
        }
        private void Insert(string text){if(keyboardTarget==null)return;keyboardTarget.text+=text;keyboardTarget.caretPosition=keyboardTarget.text.Length;UpdateKeyboardPreview();}
        private void OnDestroy(){CancelPermissionNotice();legalOperations.Cancel();legalOperations.Dispose();CancelSmbConnection();if(app!=null){app.Changed-=OnAppChanged;app.DiscoveryChanged-=RefreshFileRows;}if(Canvas!=null)Destroy(Canvas.gameObject);if(transportRoot!=null)Destroy(transportRoot.gameObject);}
    }
}
