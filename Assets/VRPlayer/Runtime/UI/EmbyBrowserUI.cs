using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

using L = VRPlayer.Localization;

namespace VRPlayer
{
    // Uses the player's Canvas and input paths; only the media browser is replaced.
    public sealed class EmbyBrowserUI : MonoBehaviour
    {
        private static readonly Color Background=UiTheme.Background,Card=UiTheme.Surface,Green=UiTheme.Accent,Muted=UiTheme.Muted;
        private const int MaxImageBytes=2*1024*1024,MaxTextures=48,MaxTextureBytes=24*1024*1024;
        private const float ImagePreload=180;
        private static readonly ProfilerMarker RenderMarker=new ProfilerMarker("Emby.Render");
        private static readonly ProfilerMarker CardMarker=new ProfilerMarker("Emby.BuildCard");
        private static readonly ProfilerMarker ImageMarker=new ProfilerMarker("Emby.DecodeImage");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        [Serializable] public sealed class WallSnapshot
        {
            public string view,size;public bool menuOpen;
            public int columns,poolCount,itemCount;public float offset,artworkWidth,artworkHeight,pitch;
            public string[] boundKeys;
        }
        public WallSnapshot WallDiagnostics=>new WallSnapshot{view=posterView.ToString(),size=ArtworkSize.ToString(),menuOpen=viewMenu!=null,
            columns=posterLayout.Columns,poolCount=posterPool.Count,itemCount=posterItems.Length,offset=Offset,artworkWidth=posterLayout.ArtworkWidth,artworkHeight=posterLayout.ArtworkHeight,pitch=posterLayout.Pitch,
            boundKeys=posterPool.Where(slot=>slot.root.gameObject.activeSelf&&slot.binding.Item!=null).Select(slot=>slot.binding.Item.key).ToArray()};
#endif
        private readonly Queue<Action> cardBuilds=new Queue<Action>();
        private int imageDecodeFrame=-1;
        private PlayerApp app;
        private RectTransform root,body;
        private float Width=>root==null?1376:root.rect.width;
        private float BodyWidth=>Width-52;
        private ListScrollRect castScroll;private RectTransform castBody;private QuickPointerRegion castRegion;
        private EmbyPerson[] castPeople=Array.Empty<EmbyPerson>();private readonly List<CastSlot> castPool=new List<CastSlot>();private int castFirst=-1;
        private sealed class CastSlot{public RectTransform root,art;public RawImage image;public TextMeshProUGUI name,role,placeholder;public RowBinding binding;public ImageBinding imageBinding;}
        public bool BlocksInput=>root!=null&&(viewMenu!=null||login||!string.IsNullOrEmpty(removing));
        public bool CastHovered=>castScroll!=null&&castScroll.enabled&&castRegion.Hovered&&!castScroll.HasDragOwner;
        private RectTransform viewMenu;
        private bool externallyBlocked;
        private ListScrollRect scroll;
        private Action<TMP_InputField> keyboard;
        private Action closeKeyboard,surfaceChanged;
        private int revision=-1,imageGeneration,loginGeneration;
        private string viewKey="",serverAddress="",serverPort="8096",username="",password="",loginError="",removing="";
        private bool login,remember=true,busy,useHttps;
        private TMP_InputField addressField,portField,userField,passwordField;
        private readonly Queue<ImageJob> imageQueue=new Queue<ImageJob>();
        private readonly Dictionary<string,ImageJob> imageJobs=new Dictionary<string,ImageJob>();
        private readonly List<UnityWebRequest> requests=new List<UnityWebRequest>();
        private readonly Dictionary<string,CachedImage> images=new Dictionary<string,CachedImage>();
        private readonly HashSet<string> pinned=new HashSet<string>();
        private readonly Dictionary<string,float> pageOffsets=new Dictionary<string,float>();
        private readonly List<PosterSlot> posterPool=new List<PosterSlot>();
        private EmbyItem[] posterItems=Array.Empty<EmbyItem>();
        private float posterTop;
        private EmbyLibraryView posterView;
        private EmbyWallLayout posterLayout;
        private int posterSpecialRow=-1;
        private const float SpecialHeadingHeight=74;
        private int posterFirstRow=int.MinValue;
        private int textureBytes,imageWorkerCount;
        private float imageOffset=float.NaN,imageViewportHeight;
        private bool imageScheduleDirty;
        private long accessOrder;
        private sealed class ImageBinding {public RawImage target;public float top,bottom;public bool active;public ImageJob job;}
        private sealed class ImageJob
        {
            public string key,url,token;public int generation;public float priority;public bool wanted,failed,cancelled;
            public UnityWebRequest request;public readonly List<ImageBinding> targets=new List<ImageBinding>();
        }
        private sealed class CachedImage {public Texture2D texture;public int bytes;public long used;}
        private sealed class PosterSlot
        {
            public RectTransform root,artwork,progressRail,progress,watched;
            public RawImage image;
            public TextMeshProUGUI label,meta,placeholder;
            public TextMeshProUGUI overview,year,duration,rating,resume;
            public RowBinding binding;
            public ImageBinding imageBinding;
        }

        public void Initialize(PlayerApp owner,Action<TMP_InputField> showKeyboard,Action hideKeyboard,Action invalidateSurface)
        {
            app=owner;keyboard=showKeyboard;closeKeyboard=hideKeyboard;surfaceChanged=invalidateSurface;
        }
        public void Bind(RectTransform panel)
        {
            ReleasePanel();root=panel;revision=-1;Render(true);
        }
        public void ReleasePanel()
        {
            CaptureLogin();RememberOffset();CloseViewMenu();cardBuilds.Clear();CancelImages();ClearPosterWall();ClearCast();root=null;scroll=null;body=null;addressField=portField=userField=passwordField=null;
        }
        public void Suspend(){CloseViewMenu();CancelImages();}
        public bool TryBack()
        {
            if(app==null)return false;
            if(viewMenu!=null){CloseViewMenu();return true;}
            if(login){ReturnFromLogin();return true;}
            if(!string.IsNullOrEmpty(removing)){closeKeyboard?.Invoke();removing="";Render(true);return true;}
            if(app.EmbyState.page==EmbyPage.Accounts)return app.ReturnToRecentFromEmby();
            closeKeyboard?.Invoke();app.EmbyBack();return true;
        }
        private void ReturnFromLogin()
        {
            closeKeyboard?.Invoke();loginGeneration++;busy=false;passwordField?.SetTextWithoutNotify("");password="";login=false;loginError="";removing="";app.ShowEmbyAccounts();
        }
        public void Block(bool blocked)
        {
            externallyBlocked=blocked;if(scroll==null)return;scroll.enabled=!blocked&&viewMenu==null;if(blocked)scroll.StopMovement();
        }
        public void Scroll(float axis)
        {
            if(scroll==null||!scroll.enabled||scroll.HasDragOwner||Mathf.Abs(axis)<=.2f)return;
            SetOffset(Offset-Mathf.Sign(axis)*(Mathf.Abs(axis)-.2f)/.8f*650*Time.unscaledDeltaTime);
        }
        private float Offset=>body==null?0:body.anchoredPosition.y;
        private void SetOffset(float value)
        {
            if(scroll==null||body==null)return;scroll.StopMovement();body.anchoredPosition=new Vector2(0,Mathf.Clamp(value,0,Mathf.Max(0,body.rect.height-scroll.viewport.rect.height)));
        }
        public void Render(bool force=false)
        {
            using(RenderMarker.Auto())RenderPage(force);
        }
        private void RenderPage(bool force)
        {
            if(root==null||app==null)return;var state=app.EmbyState;
            if(!force&&revision==state.revision)return;
            CaptureLogin();RememberOffset();CloseViewMenu();string key=string.Concat(new[]{state.page.ToString(),login.ToString(),state.parent?.Id,state.selected?.Id,state.filterKind,state.filterValue,state.filterId,state.search,app.ActiveEmbyAccount?.id,state.page==EmbyPage.Library?LibraryView.ToString():"",state.page==EmbyPage.Library?ArtworkSize.ToString():"",state.IsLibraryRoot?state.librarySort.ToString():"",state.IsLibraryRoot?state.librarySortDescending.ToString():""}.Select(value=>(value?.Length??0)+":"+(value??"")));
            float offset=pageOffsets.TryGetValue(key,out var remembered)?remembered:0;viewKey=key;revision=state.revision;
            cardBuilds.Clear();CancelImages();ClearPosterWall();ClearCast();UiFactory.Clear(root);scroll=null;body=null;addressField=portField=userField=passwordField=null;surfaceChanged?.Invoke();
            UiFactory.Box(root,"Emby input surface",0,0,Width,root.rect.height,Color.clear,true);
            if(login||state.page==EmbyPage.Accounts){if(login)DrawLogin();else DrawAccounts();}
            else{
                DrawHeader(state.title,state.page!=EmbyPage.Details);
                if(state.loading){UiFactory.Label(root,L.T("正在读取媒体库…"),28,160,Width-56,64,32);UiFactory.Label(root,L.T("请稍候"),28,226,Width-56,44,24).color=Muted;}
                else if(!string.IsNullOrEmpty(state.error)){DrawError(state.error);}
                else if(state.page==EmbyPage.Home)DrawHome();
                else if(state.page==EmbyPage.Library)DrawLibrary();
                else if(state.page==EmbyPage.Person)DrawPerson();
                else DrawDetails();
            }
            if(body!=null)SetOffset(offset);
            UpdatePosterWall();
            ScheduleImages();
        }
        private void RememberOffset()
        {
            if(body==null||string.IsNullOrEmpty(viewKey))return;
            if(!pageOffsets.ContainsKey(viewKey)&&pageOffsets.Count>=64)pageOffsets.Remove(pageOffsets.Keys.First());
            pageOffsets[viewKey]=Offset;
        }
        private void LateUpdate()
        {
            if(root==null||!root.gameObject.activeInHierarchy)return;
            // Each card also schedules text meshes/layout. Let those complete and
            // controller poses advance before creating the next card.
            BuildNextCard();
            float height=scroll==null?root.rect.height:scroll.viewport.rect.height;
            UpdateCast();
            if(imageScheduleDirty||float.IsNaN(imageOffset)||Mathf.Abs(Offset-imageOffset)>1||Mathf.Abs(height-imageViewportHeight)>1){UpdatePosterWall();ScheduleImages();}
        }
        private bool BuildNextCard()
        {
            if(cardBuilds.Count==0||body==null)return false;
            using(CardMarker.Auto())cardBuilds.Dequeue()();
            imageScheduleDirty=true;return true;
        }
        private void DrawHeader(string title,bool showTitle=true)
        {
            UiFactory.IconButton(root,"返回",QuickIcon.Back,0,0,()=>{closeKeyboard?.Invoke();app.EmbyBack();});
            if(showTitle){var label=UiFactory.Label(root,string.IsNullOrEmpty(title)?"Emby · "+AccountName(app.ActiveEmbyAccount):title,80,0,Width-476,64,30);label.color=Color.white;}
            UiFactory.IconButton(root,"首页",QuickIcon.Home,Width-380,0,()=>{closeKeyboard?.Invoke();app.EmbyHome();});
            Button(root,L.T("账户与服务器"),Width-300,0,220,64,()=>{closeKeyboard?.Invoke();login=false;removing="";app.ShowEmbyAccounts();});
            UiFactory.IconButton(root,"刷新",QuickIcon.Refresh,Width-64,0,()=>app.RefreshEmby());
        }
        private void DrawError(string message)
        {
            var text=UiFactory.Label(root,L.Message(message),28,150,Width-56,120,28);text.textWrappingMode=TextWrappingModes.Normal;text.alignment=TextAlignmentOptions.TopLeft;
            Button(root,L.T("重试"),28,298,240,56,()=>app.RefreshEmby(),true);
            Button(root,L.T("更换账户"),286,298,280,56,()=>{closeKeyboard?.Invoke();login=false;app.ShowEmbyAccounts();});
        }
        private void DrawAccounts()
        {
            bool canReturn=!string.IsNullOrEmpty(app.ActiveEmbyAccount?.accessToken);
            if(canReturn)UiFactory.IconButton(root,"返回媒体库",QuickIcon.Back,0,0,()=>{login=false;app.EmbyHome();});
            float titleX=canReturn?80:24;
            UiFactory.Label(root,L.T("Emby · 服务器与账户"),titleX,0,Width-titleX,64,32);
            BeginScroll(76,root.rect.height-158);
            var saved=app.EmbyAccounts??Array.Empty<EmbyAccount>();
            float errorHeight=string.IsNullOrEmpty(app.EmbyState.error)?0:68;
            if(errorHeight>0){var error=UiFactory.Label(body,L.Message(app.EmbyState.error),18,0,BodyWidth-36,58,24);error.textWrappingMode=TextWrappingModes.Normal;}
            if(saved.Count==0){
                UiFactory.Label(body,L.T("连接你的 Emby 媒体库"),18,55,BodyWidth-36,66,36);
                UiFactory.Label(body,L.T("使用已有的 Emby 账户登录，可添加多个服务器和账户。"),18,131,BodyWidth-36,60,25).color=Muted;
                FinishScroll(260);
            }else{
                int index=0;foreach(var account in saved){
                    var selected=account;float y=errorHeight+index++*120;
                    bool active=selected.id==app.ActiveEmbyAccount?.id;
                    float cardWidth=BodyWidth-28;
                    var card=UiFactory.RoundedBox(body,"Server account",14,y,cardWidth,104,active?UiTheme.SelectedMuted:UiTheme.Elevated,false,14,SurfaceTreatment.Control);
                    card.GetComponent<RoundedPanelGraphic>().Selected=active;
                    var open=Button(card,"",0,0,cardWidth-208,104,()=>{
                        removing="";
                        if(string.IsNullOrEmpty(selected.accessToken)){login=true;EmbyServerAddress.Parse(selected.serverUrl,out useHttps,out serverAddress,out serverPort);username=selected.username;password="";loginError="";remember=selected.remember;Render(true);}
                        else{login=false;app.SelectEmbyAccount(selected.id);}
                    });
                    open.targetGraphic.color=Color.white;var colors=open.colors;colors.normalColor=colors.selectedColor=Color.clear;colors.highlightedColor=UiTheme.Elevated;colors.pressedColor=UiTheme.SelectedMuted;UiFactory.SetColorsImmediate(open,colors);
                    var surface=(RoundedPanelGraphic)open.targetGraphic;surface.shadow=0;surface.border=0;
                    UiFactory.Clear(open.transform);
                    UiFactory.Label(open.transform,AccountName(selected)+(active?L.T("  ·  当前"):""),24,12,cardWidth-256,40,28).color=active?Green:UiTheme.Text;
                    UiFactory.Label(open.transform,(selected.username??"")+"  ·  "+SafeServer(selected.serverUrl),24,52,cardWidth-256,40,22).color=active?UiTheme.Text:Muted;
                    Button(card,removing==selected.id?L.T("确认移除"):L.T("移除"),cardWidth-192,24,168,56,()=>{
                        if(removing!=selected.id){removing=selected.id;Render(true);return;}
                        removing="";app.RemoveEmbyAccount(selected.id);Render(true);
                    });
                }
                FinishScroll(errorHeight+index*120);
            }
            Button(root,L.T("添加服务器 / 账户"),24,root.rect.height-64,600,54,()=>{login=true;useHttps=false;serverPort="8096";serverAddress=username=password=loginError="";remember=true;Render(true);},true);
            UiFactory.Label(root,L.T("切换账户后显示该账户可访问的媒体库"),644,root.rect.height-64,Width-668,54,23,TextAlignmentOptions.Right).color=Muted;
        }
        private void DrawLogin()
        {
            UiFactory.IconButton(root,busy?L.T("取消登录"):L.T("返回"),QuickIcon.Back,0,0,ReturnFromLogin);
            UiFactory.Label(root,L.T("添加 Emby 服务器 / 账户"),80,0,Width-80,64,32);
            UiFactory.Label(root,L.T("输入服务器地址，并使用该服务器已有的账户登录"),24,73,Width-48,44,24).color=Muted;
            UiFactory.Label(root,L.T("连接协议"),24,130,224,52,26);
            var protocol=Button(root,useHttps?"HTTPS":"HTTP",258,130,190,52,()=>{CaptureLogin();closeKeyboard?.Invoke();useHttps=!useHttps;Render(true);},true);protocol.interactable=!busy;
            UiFactory.Label(root,L.T("点击按钮切换 HTTP / HTTPS"),468,130,Width-492,52,24).color=Muted;
            UiFactory.Label(root,L.T("地址 / IP"),24,194,224,58,26);
            addressField=UiFactory.Input(root,serverAddress,L.T("IP 地址或域名"),258,194,Width-588,58,keyboard);addressField.name="Emby server address";
            UiFactory.Label(root,L.T("端口"),Width-314,194,86,58,26);
            portField=UiFactory.Input(root,serverPort,"8096",Width-212,194,198,58,keyboard);portField.name="Emby server port";portField.characterLimit=5;
            UiFactory.Label(root,L.T("用户名"),24,270,224,58,26);
            userField=UiFactory.Input(root,username,L.T("Emby 用户名"),258,270,Width-272,58,keyboard);
            UiFactory.Label(root,L.T("密码"),24,346,224,58,26);
            passwordField=UiFactory.Input(root,password,L.T("账户密码"),258,346,Width-272,58,keyboard,true);
            addressField.interactable=portField.interactable=userField.interactable=passwordField.interactable=!busy;
            var keep=UiFactory.ToggleRow(root,L.T("记住登录"),L.T("记住登录"),()=>remember,24,422,350,value=>remember=value,64);keep.interactable=!busy;
            UiFactory.Label(root,L.T("可在账户列表切换或移除登录"),398,422,Width-422,58,23).color=Muted;
            string error=string.IsNullOrEmpty(loginError)?app.EmbyState.error:loginError;
            var errorText=UiFactory.Label(root,busy?L.T("正在登录…"):L.Message(error)??"",24,496,Width-48,60,22);errorText.textWrappingMode=TextWrappingModes.Normal;
            var submit=Button(root,busy?L.T("正在连接…"):L.T("登录并打开媒体库"),24,578,Width-48,64,()=>{CaptureLogin();closeKeyboard?.Invoke();_ = Login();},true);submit.interactable=!busy;
        }
        private void CaptureLogin()
        {
            if(addressField!=null)serverAddress=addressField.text;if(portField!=null)serverPort=portField.text;if(userField!=null)username=userField.text;if(passwordField!=null)password=passwordField.text;
        }
        private async System.Threading.Tasks.Task Login()
        {
            if(busy)return;
            string server;
            try{server=EmbyServerAddress.Build(useHttps,serverAddress,serverPort);}
            catch(ArgumentException ex){loginError=ex.Message;Render(true);return;}
            if(string.IsNullOrWhiteSpace(username)){loginError=L.T("请填写 Emby 用户名");Render(true);return;}
            int capturedGeneration=++loginGeneration;
            busy=true;loginError="";Render(true);
            try{
                await app.LoginEmby(server,username,password,remember);
                if(this==null||capturedGeneration!=loginGeneration||!login)return;
                if(app.EmbyState.page!=EmbyPage.Accounts&&app.ActiveEmbyAccount!=null){login=false;passwordField?.SetTextWithoutNotify("");password="";}
            }catch(OperationCanceledException){}catch(Exception){if(this!=null&&capturedGeneration==loginGeneration&&login)loginError=L.T("登录失败，请检查服务器地址与账户");}
            finally{if(this!=null&&capturedGeneration==loginGeneration){busy=false;Render(true);}}
        }
        private void DrawHome()
        {
            BeginScroll(76,root.rect.height-90);var state=app.EmbyState;float y=PlayedFeedback(0);
            y=SectionTitle(L.T("我的媒体"),y);
            var views=state.views??Array.Empty<EmbyItem>();
            if(views.Length==0){UiFactory.Label(body,L.T("此账户没有可访问的媒体库"),18,y,BodyWidth-36,58,26).color=Muted;y+=78;}
            else{
                int columns=Mathf.Max(1,Mathf.FloorToInt((BodyWidth-28+18)/318));
                float width=(BodyWidth-28-(columns-1)*18)/columns,pitch=width*9f/16+88;
                for(int i=0;i<views.Length;i++){
                    var item=views[i];float x=14+(i%columns)*(width+18),top=y+(i/columns)*pitch;
                    cardBuilds.Enqueue(()=>LibraryCard(item,x,top,width));
                }
                y+=Mathf.CeilToInt(views.Length/(float)columns)*pitch+16;
            }
            y=HomePosters(L.T("继续播放"),state.resume,y,L.T("暂无未看完的影片，播放进度会同步到 Emby"),true);
            y=HomePosters(L.T("最新加入"),state.latest,y,L.T("媒体库中暂无可显示的影片"));
            FinishScroll(y+12);
        }
        private float SectionTitle(string title,float y)
        {
            UiFactory.Box(body,"Section accent",14,y+9,4,28,Green);
            UiFactory.Label(body,title,30,y,BodyWidth-44,48,30);return y+58;
        }
        private float HomePosters(string title,EmbyItem[] items,float y,string empty,bool thumbnails=false)
        {
            y=SectionTitle(title,y);items=items??Array.Empty<EmbyItem>();
            if(items.Length==0){UiFactory.Label(body,empty,18,y,BodyWidth-36,48,24).color=Muted;return y+78;}
            var layout=EmbyLibraryViews.HomeLayout(BodyWidth,thumbnails);
            int count=Math.Min(12,items.Length);for(int i=0;i<count;i++){
                var item=items[i];float x=14+(i%layout.Columns)*(layout.Width+18),top=y+(i/layout.Columns)*layout.Pitch;
                cardBuilds.Enqueue(()=>PosterCard(item,x,top,layout,thumbnails));
            }
            return y+layout.ContentHeight(count)+18;
        }
        private void LibraryCard(EmbyItem item,float x,float y,float width)
        {
            float artHeight=width*9f/16;
            var captured=item;var card=Button(body,"",x,y,width,artHeight+64,()=>app.OpenEmbyLibrary(captured));UiFactory.Clear(card.transform);card.name="Emby library "+item.Id;
            Image(card.transform,item.Id,0,0,width,artHeight,!string.IsNullOrEmpty(item.ImageTags?.Primary),item);
            UiFactory.Label(card.transform,item.Name??L.T("媒体库"),12,artHeight+8,width-24,44,25);
        }
        private void DrawLibrary()
        {
            var state=app.EmbyState;
            float selectorWidth=state.IsLibraryRoot?560:296;
            var search=UiFactory.Input(root,state.search??"",L.T("搜索当前影片列表"),0,80,Width-160-selectorWidth,64,keyboard);
            UiFactory.IconButton(root,"搜索",QuickIcon.Search,Width-144-selectorWidth,80,()=>{closeKeyboard?.Invoke();app.SearchEmbyLibrary(search.text);},true);
            UiFactory.IconButton(root,"清空",QuickIcon.Close,Width-64-selectorWidth,80,()=>{closeKeyboard?.Invoke();app.SearchEmbyLibrary("");},tooltip:"清空搜索");
            if(state.IsLibraryRoot){
                var sort=Button(root,L.T("排序 · ")+L.T(EmbyLibraryViews.Label(state.librarySort)),Width-544,80,248,64,ToggleSortMenu);
                sort.name="Emby sort selector";AlignMenuText(sort,16);var label=sort.GetComponentInChildren<TextMeshProUGUI>();label.enableAutoSizing=true;label.fontSizeMin=18;label.fontSizeMax=26;
            }
            if(!FixedSeriesView){
                var selector=Button(root,L.T("视图 · ")+L.T(EmbyLibraryViews.Label(LibraryView)),Width-280,80,280,64,ToggleViewMenu);selector.name="Emby view selector";
                AlignMenuText(selector,16);
            }
            else Button(root,L.T("图片大小")+" · "+L.T(EmbyLibraryViews.Label(ArtworkSize)),Width-280,80,280,64,ToggleViewMenu).name="Emby size selector";
            BeginScroll(160,root.rect.height-160);var items=state.items??Array.Empty<EmbyItem>();
            if(items.Length==0&&state.specialEpisodes.Length==0){UiFactory.Label(body,string.IsNullOrEmpty(state.search)?L.T("此媒体库暂无影片"):L.T("未找到匹配的影片"),18,80,BodyWidth-36,68,30).color=Muted;FinishScroll(260);}
            else{
                float top=LibraryView==EmbyLibraryView.Table?TableHeader():state.episodeSeason!=null&&items.Length>0?SectionTitle(state.episodeSeason.Name,0):0;
                FinishScroll(BuildPosterWall(items,top,LibraryView,state.specialEpisodes));
            }
        }
        private string ViewLibraryId=>!string.IsNullOrEmpty(app.EmbyState.viewLibraryId)?app.EmbyState.viewLibraryId:app.EmbyState.parent?.Id??"all-movies";
        private EmbyArtworkSize ArtworkSize=>app.Store?.GetEmbyArtworkSize(app.ActiveEmbyAccount?.id,ViewLibraryId)??EmbyArtworkSize.Large;
        private bool FixedSeriesView=>app.EmbyState.parent?.Type=="Series"||app.EmbyState.parent?.Type=="Season";
        private EmbyLibraryView LibraryView
        {
            get{
                if(FixedSeriesView)return app.EmbyState.episodesView||app.EmbyState.parent.Type=="Season"?EmbyLibraryView.Thumb:EmbyLibraryView.Poster;
                return app.Store?.GetEmbyLibraryView(app.ActiveEmbyAccount?.id,ViewLibraryId)??EmbyLibraryView.Poster;
            }
        }
        private void ToggleSortMenu()
        {
            closeKeyboard?.Invoke();if(viewMenu!=null){CloseViewMenu();return;}
            var state=app.EmbyState;if(!state.IsLibraryRoot)return;
            viewMenu=UiFactory.Box(root,"Emby sort menu",0,0,Width,root.rect.height,Color.clear,true);
            var outside=viewMenu.gameObject.AddComponent<Button>();outside.onClick.AddListener(CloseViewMenu);
            const float menuWidth=392;var panel=MenuPanel(viewMenu,"Sort options",Width-296-menuWidth,148,menuWidth,416);
            var openedMenu=viewMenu;string accountId=app.ActiveEmbyAccount?.id,libraryId=state.viewLibraryId;
            bool Current()=>viewMenu==openedMenu&&app.Source==FileSource.Emby&&app.EmbyState==state&&app.ActiveEmbyAccount?.id==accountId&&state.IsLibraryRoot&&state.viewLibraryId==libraryId;
            void Apply(EmbyLibrarySort sort,bool descending){if(!Current())return;CloseViewMenu();app.SetEmbyLibrarySort(sort,descending);}
            UiFactory.Label(panel,L.T("排序方式"),16,16,menuWidth-32,40,25).color=Muted;
            foreach(EmbyLibrarySort sort in Enum.GetValues(typeof(EmbyLibrarySort))){
                var selected=sort;var option=MenuButton(panel,L.T(EmbyLibraryViews.Label(sort)),16,64+(int)sort*72,menuWidth-32,60,()=>Apply(selected,state.librarySortDescending),state.librarySort==sort);
                option.name="Emby sort "+sort;AlignMenuText(option,16);
            }
            UiFactory.Label(panel,L.T("排序方向"),16,284,menuWidth-32,40,25).color=Muted;
            var direction=UiFactory.Rect(panel,"Sort direction group",16,334,menuWidth-32,68);float directionWidth=(menuWidth-52)/2;
            bool title=state.librarySort==EmbyLibrarySort.Title;
            var ascending=MenuButton(direction,L.T(title?"标题正序":"较早在前"),4,4,directionWidth,60,()=>Apply(state.librarySort,false),!state.librarySortDescending);ascending.name="Emby sort ascending";
            var descending=MenuButton(direction,L.T(title?"标题倒序":"较晚在前"),16+directionWidth,4,directionWidth,60,()=>Apply(state.librarySort,true),state.librarySortDescending);descending.name="Emby sort descending";
            if(scroll!=null){scroll.StopMovement();scroll.enabled=false;}HighlightMenuSelector("Emby sort selector");surfaceChanged?.Invoke();
        }
        private void ToggleViewMenu()
        {
            closeKeyboard?.Invoke();if(viewMenu!=null){CloseViewMenu();return;}
            viewMenu=UiFactory.Box(root,"Emby view menu",0,0,Width,root.rect.height,Color.clear,true);
            var outside=viewMenu.gameObject.AddComponent<Button>();outside.onClick.AddListener(CloseViewMenu);
            float menuWidth=FixedSeriesView?384:544;
            var panel=MenuPanel(viewMenu,"Options",Width-menuWidth-14,148,menuWidth,FixedSeriesView?148:488);
            var openedMenu=viewMenu;var state=app.EmbyState;string accountId=app.ActiveEmbyAccount?.id,libraryId=ViewLibraryId;
            if(!FixedSeriesView){
                UiFactory.Label(panel,L.T("视图样式"),16,16,menuWidth-32,40,25).color=Muted;
                float optionWidth=(menuWidth-44)/2;
                foreach(EmbyLibraryView view in Enum.GetValues(typeof(EmbyLibraryView))){
                    var selected=view;bool active=LibraryView==view;int index=(int)view;
                    var option=MenuButton(panel,L.T(EmbyLibraryViews.Label(view)),16+(index%2)*(optionWidth+12),64+(index/2)*72,optionWidth,60,()=>{
                        if(viewMenu!=openedMenu||app.Source!=FileSource.Emby||app.EmbyState!=state||app.ActiveEmbyAccount?.id!=accountId||ViewLibraryId!=libraryId)return;
                        app.Store?.SetEmbyLibraryView(accountId,libraryId,selected);Render(true);
                    },active);
                    AlignMenuText(option,52);UiFactory.Icon(option.transform,ViewIcon(view),14,16,28,active?UiTheme.Ink:Muted);
                }
            }
            float sizeTop=FixedSeriesView?16:356;
            UiFactory.Label(panel,L.T("图片大小"),16,sizeTop,menuWidth-32,40,25).color=Muted;
            float sizeWidth=(menuWidth-64)/3;
            var sizes=UiFactory.Rect(panel,"Artwork sizes",16,sizeTop+50,menuWidth-32,68);
            foreach(EmbyArtworkSize size in Enum.GetValues(typeof(EmbyArtworkSize))){
                var selected=size;var option=MenuButton(sizes,L.T(EmbyLibraryViews.Label(size)),4+(int)size*(sizeWidth+12),4,sizeWidth,60,()=>{
                    if(viewMenu!=openedMenu||app.Source!=FileSource.Emby||app.EmbyState!=state||app.ActiveEmbyAccount?.id!=accountId||ViewLibraryId!=libraryId)return;
                    app.Store?.SetEmbyArtworkSize(accountId,libraryId,selected);Render(true);
                },ArtworkSize==size);option.name="Emby artwork size "+size;
            }
            if(scroll!=null){scroll.StopMovement();scroll.enabled=false;}HighlightMenuSelector(FixedSeriesView?"Emby size selector":"Emby view selector");surfaceChanged?.Invoke();
        }
        private static RectTransform MenuPanel(Transform parent,string name,float x,float y,float width,float height)
        {
            return UiFactory.RoundedBox(parent,name,x,y,width,height,new Color(Background.r,Background.g,Background.b,1),true,14);
        }
        private static Button MenuButton(Transform parent,string text,float x,float y,float width,float height,Action clicked,bool selected)
        {
            var button=Button(parent,text,x,y,width,height,clicked,selected);var graphic=(RoundedPanelGraphic)button.targetGraphic;
            graphic.Treatment=SurfaceTreatment.Control;graphic.shadow=0;graphic.border=.65f;graphic.color=Color.white;graphic.Selected=selected;
            var colors=button.colors;colors.normalColor=selected?UiTheme.Selected:Color.clear;
            colors.highlightedColor=selected?Color.Lerp(UiTheme.Selected,Color.white,.08f):Color.clear;
            colors.selectedColor=colors.highlightedColor;colors.pressedColor=selected?UiTheme.Accent:Color.clear;
            UiFactory.SetColorsImmediate(button,colors);
            if(!selected){
                var outline=UiFactory.Rect(button.transform,"Menu hover outline",0,0,width,height).gameObject.AddComponent<MenuHoverOutlineGraphic>();
                outline.color=UiTheme.Accent;outline.raycastTarget=false;outline.gameObject.SetActive(false);
                var pointers=new HashSet<int>();var hover=button.gameObject.AddComponent<QuickHoverLabel>();
                hover.Change=(id,enter)=>{if(enter)pointers.Add(id);else pointers.Remove(id);if(outline!=null)outline.gameObject.SetActive(pointers.Count>0);};
            }
            return button;
        }
        private void HighlightMenuSelector(string name)
        {
            if(root==null)return;
            foreach(var button in root.GetComponentsInChildren<Button>(true)){
                if(button.name!="Emby sort selector"&&button.name!="Emby view selector"&&button.name!="Emby size selector")continue;
                if(button.targetGraphic is RoundedPanelGraphic panel)panel.Selected=button.name==name;
            }
        }
        private static void AlignMenuText(Button button,float left)
        {
            var label=button.GetComponentInChildren<TextMeshProUGUI>();label.alignment=TextAlignmentOptions.MidlineLeft;
            label.rectTransform.anchoredPosition=new Vector2(left,label.rectTransform.anchoredPosition.y);
            label.rectTransform.sizeDelta=new Vector2(((RectTransform)button.transform).rect.width-left-12,label.rectTransform.sizeDelta.y);
        }
        private static QuickIcon ViewIcon(EmbyLibraryView view)=>view switch {
            EmbyLibraryView.Banner=>QuickIcon.ViewBanner,EmbyLibraryView.Disc=>QuickIcon.ViewDisc,EmbyLibraryView.Logo=>QuickIcon.ViewLogo,
            EmbyLibraryView.Thumb=>QuickIcon.ViewThumb,EmbyLibraryView.List=>QuickIcon.ViewList,EmbyLibraryView.Table=>QuickIcon.ViewTable,_=>QuickIcon.ViewPoster
        };
        private void CloseViewMenu()
        {
            if(viewMenu==null)return;viewMenu.gameObject.SetActive(false);
            if(Application.isPlaying)Destroy(viewMenu.gameObject);else DestroyImmediate(viewMenu.gameObject);
            viewMenu=null;HighlightMenuSelector(null);if(scroll!=null)scroll.enabled=!externallyBlocked;surfaceChanged?.Invoke();
        }
        private float TableHeader()
        {
            foreach(var column in new[]{new{title=L.T("名称"),x=124f,w=BodyWidth-648},new{title=L.T("年份"),x=BodyWidth-530,w=86f},new{title=L.T("时长"),x=BodyWidth-430,w=126f},new{title=L.T("评分"),x=BodyWidth-290,w=90f},new{title=L.T("进度"),x=BodyWidth-176,w=146f}})
                UiFactory.Label(body,column.title,column.x,0,column.w,42,23).color=Muted;
            return 50;
        }
        private void PosterCard(EmbyItem item,float x,float y,EmbyWallLayout layout,bool thumbnails)
        {
            float width=layout.Width,artHeight=layout.ArtworkHeight;
            var captured=item;var card=Button(body,"",x,y,width,layout.CardHeight,()=>app.OpenEmbyItem(captured));UiFactory.Clear(card.transform);card.name=(thumbnails?"Emby resume ":"Emby latest ")+item.Id;
            var artwork=EmbyLibraryViews.Artwork(item,thumbnails?EmbyLibraryView.Thumb:EmbyLibraryView.Poster);
            Image(card.transform,artwork.ItemId,0,0,width,artHeight,artwork.Available,item,artwork.Tag,artwork.Type);
            bool episode=thumbnails&&item.Type=="Episode"&&!string.IsNullOrEmpty(item.SeriesName);
            var name=UiFactory.Label(card.transform,episode?item.SeriesName:DisplayName(item),8,artHeight+4,width-16,42,24);name.alignment=TextAlignmentOptions.MidlineLeft;
            string meta=episode?$"S{item.ParentIndexNumber}:E{item.IndexNumber} · {item.Name}":ItemMeta(item);
            UiFactory.Label(card.transform,meta,8,artHeight+46,width-16,34,20).color=Muted;
            float progress=Progress(item);if(progress>0){UiFactory.Box(card.transform,"Progress rail",0,artHeight-6,width,6,new Color(0,0,0,.6f));UiFactory.Box(card.transform,"Progress",0,artHeight-6,width*progress,6,Green);}
            if(item.UserData?.Played==true){UiFactory.Box(card.transform,"Watched",width-52,8,44,28,Green);UiFactory.Label(card.transform,L.T("已看"),width-52,8,44,28,17,TextAlignmentOptions.Center);}
            if(thumbnails&&item.IsPlayable){
                const float markSize=44;
                var mark=UiFactory.IconButton(card.transform,"标记为已播放",QuickIcon.Check,width-markSize-10,artHeight-markSize-14,PlayedAction(item,true),tooltip:"标记为已播放",size:markSize);
                mark.name="Emby played "+item.Id;mark.interactable=!app.EmbyPlayedBusy;
                card.gameObject.AddComponent<UiHoverAction>().Initialize(mark.gameObject.AddComponent<CanvasGroup>());
            }
        }
        private Action PlayedAction(EmbyItem item,bool played)
        {
            var state=app.EmbyState;var account=app.ActiveEmbyAccount;
            return ()=>{if(app.Source!=FileSource.Emby||app.EmbyState!=state||app.ActiveEmbyAccount!=account)return;closeKeyboard?.Invoke();_ = app.SetEmbyPlayed(item,played);};
        }
        private float PlayedFeedback(float y)
        {
            string message=app.EmbyState.playedError;
            if(string.IsNullOrEmpty(message)&&!string.IsNullOrEmpty(app.EmbyState.playedItemId))message=L.T("正在同步播放状态…");
            if(string.IsNullOrEmpty(message))return y;
            var label=UiFactory.Label(body,L.Message(message),18,y,BodyWidth-36,44,23);label.color=Muted;label.textWrappingMode=TextWrappingModes.Normal;
            float height=Mathf.Max(44,label.GetPreferredValues(message,BodyWidth-36,float.PositiveInfinity).y+12);label.rectTransform.sizeDelta=new Vector2(BodyWidth-36,height);return y+height+14;
        }
        private float BuildPosterWall(EmbyItem[] items,float top,EmbyLibraryView view=EmbyLibraryView.Poster,EmbyItem[] specials=null)
        {
            posterItems=items??Array.Empty<EmbyItem>();posterTop=top;posterFirstRow=int.MinValue;posterView=view;posterLayout=EmbyLibraryViews.Layout(view,BodyWidth,ArtworkSize);
            var layout=posterLayout;
            posterSpecialRow=-1;
            if(specials?.Length>0){
                posterSpecialRow=Mathf.CeilToInt(posterItems.Length/(float)layout.Columns);
                SectionTitle(L.T("特别篇"),top+posterSpecialRow*layout.Pitch+16);
                var combined=new EmbyItem[posterSpecialRow*layout.Columns+specials.Length];
                Array.Copy(posterItems,combined,posterItems.Length);Array.Copy(specials,0,combined,posterSpecialRow*layout.Columns,specials.Length);posterItems=combined;
            }
            int count=layout.PoolSize(posterItems.Length,scroll.viewport.rect.height,ImagePreload);
            for(int i=0;i<count;i++)cardBuilds.Enqueue(BuildPosterCard);
            return top+layout.ContentHeight(posterItems.Length)+(posterSpecialRow>=0?SpecialHeadingHeight:0);
        }
        private void BuildPosterCard()
        {
            var layout=posterLayout;var view=posterView;bool list=view==EmbyLibraryView.List,table=view==EmbyLibraryView.Table;
            float top=posterTop;
            var slot=new PosterSlot();var card=Button(body,"",14,top,layout.Width,layout.CardHeight,null);UiFactory.Clear(card.transform);slot.root=(RectTransform)card.transform;
            slot.binding=slot.root.gameObject.AddComponent<RowBinding>();slot.root.gameObject.AddComponent<RowPress>().Bind(slot.binding);card.onClick.AddListener(slot.binding.Open);
            bool alphaArtwork=view==EmbyLibraryView.Logo||view==EmbyLibraryView.Disc;
            slot.artwork=UiFactory.RoundedBox(slot.root,"Artwork",list||table?8:0,list||table?12:0,layout.ArtworkWidth,layout.ArtworkHeight,alphaArtwork?Color.clear:UiTheme.Elevated,false,12);
            if(!alphaArtwork)slot.artwork.gameObject.AddComponent<Mask>().showMaskGraphic=true;
            slot.placeholder=UiFactory.Label(slot.artwork,"",4,0,layout.ArtworkWidth-8,layout.ArtworkHeight,table?14:list?17:22,TextAlignmentOptions.Center);slot.placeholder.name="Text";slot.placeholder.color=Muted;
            var imageRect=UiFactory.Rect(slot.artwork,"Image",0,0,layout.ArtworkWidth,layout.ArtworkHeight);imageRect.pivot=new Vector2(.5f,.5f);slot.image=imageRect.gameObject.AddComponent<RawImage>();slot.image.raycastTarget=false;slot.image.color=Color.clear;
            var fit=imageRect.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=alphaArtwork||FixedSeriesView?AspectRatioFitter.AspectMode.FitInParent:AspectRatioFitter.AspectMode.EnvelopeParent;fit.aspectRatio=layout.ArtworkWidth/layout.ArtworkHeight;
            slot.label=UiFactory.Label(slot.root,"",list||table?110:8,list?8:table?14:layout.ArtworkHeight+4,table?layout.Width-634:list?layout.Width-126:layout.Width-16,42,24);
            if(table){
                slot.year=UiFactory.Label(slot.root,"",layout.Width-544,14,86,42,23);slot.duration=UiFactory.Label(slot.root,"",layout.Width-444,14,126,42,23);
                slot.rating=UiFactory.Label(slot.root,"",layout.Width-304,14,90,42,23);slot.resume=UiFactory.Label(slot.root,"",layout.Width-190,14,146,42,23);
            }else{
                slot.meta=UiFactory.Label(slot.root,"",list?110:8,list?52:layout.ArtworkHeight+46,list?layout.Width-126:layout.Width-16,34,20);slot.meta.color=Muted;
                if(list){slot.overview=UiFactory.Label(slot.root,"",110,92,layout.Width-126,42,23);slot.overview.color=Muted;}
            }
            slot.progressRail=UiFactory.Box(slot.artwork,"Progress rail",0,layout.ArtworkHeight-6,layout.ArtworkWidth,6,new Color(0,0,0,.6f));slot.progress=UiFactory.Box(slot.artwork,"Progress",0,layout.ArtworkHeight-6,0,6,Green);
            slot.watched=UiFactory.Box(slot.artwork,"Watched",layout.ArtworkWidth-48,4,44,28,Green);UiFactory.Label(slot.watched,L.T("已看"),0,0,44,28,17,TextAlignmentOptions.Center);
            posterPool.Add(slot);
            BindPosterCard(slot,posterPool.Count-1,FirstPosterRow());
        }
        private int FirstPosterRow()
        {
            float offset=Offset-posterTop-ImagePreload;
            if(posterSpecialRow>=0&&offset>=posterSpecialRow*posterLayout.Pitch)offset=Mathf.Max(posterSpecialRow*posterLayout.Pitch,offset-SpecialHeadingHeight);
            return Mathf.Clamp(Mathf.FloorToInt(offset/posterLayout.Pitch),0,Mathf.Max(0,Mathf.CeilToInt(posterItems.Length/(float)posterLayout.Columns)-1));
        }
        private void UpdatePosterWall()
        {
            if(posterPool.Count==0||body==null)return;
            var layout=posterLayout;
            int first=FirstPosterRow();
            if(first==posterFirstRow)return;posterFirstRow=first;
            for(int i=0;i<posterPool.Count;i++)BindPosterCard(posterPool[i],i,first);
            imageScheduleDirty=true;
        }
        private void BindPosterCard(PosterSlot slot,int i,int first)
        {
            var layout=posterLayout;UnbindImage(slot.imageBinding);slot.imageBinding=null;
            int index=first*layout.Columns+i;var item=index<posterItems.Length?posterItems[index]:null;
            slot.root.gameObject.SetActive(item!=null);if(item==null){slot.binding.Item=null;return;}
            int row=index/layout.Columns;
            slot.root.anchoredPosition=new Vector2(14+(i%layout.Columns)*(layout.Width+18),-(posterTop+row*layout.Pitch+(posterSpecialRow>=0&&row>=posterSpecialRow?SpecialHeadingHeight:0)));
            slot.binding.Item=new BrowserRow{key=(app.ActiveEmbyAccount?.id??"")+":"+item.Id,open=()=>app.OpenEmbyItem(item)};
            slot.root.GetComponent<Button>().interactable=!string.IsNullOrEmpty(item.Id);
            slot.label.text=item.Type=="Episode"&&FixedSeriesView?(item.IndexNumber>0?item.IndexNumber+(item.IndexNumberEnd>item.IndexNumber?"–"+item.IndexNumberEnd:"")+". ":"")+(item.Name??""):DisplayName(item);if(slot.meta!=null)slot.meta.text=posterView==EmbyLibraryView.List?DetailsMeta(item)+"   ·   "+PlaybackProgress(item):ItemMeta(item);
            if(slot.overview!=null)slot.overview.text=string.IsNullOrWhiteSpace(item.Overview)?L.T("暂无影片简介"):PlainOverview(item.Overview);
            if(slot.year!=null){slot.year.text=item.ProductionYear>0?item.ProductionYear.ToString():"—";slot.duration.text=item.RunTimeTicks>0?TicksClock(item.RunTimeTicks):"—";slot.rating.text=item.CommunityRating>0?item.CommunityRating.ToString("0.0"):"—";slot.resume.text=PlaybackProgress(item);}
            var artwork=EmbyLibraryViews.Artwork(item,posterView);
            slot.placeholder.text=artwork.Available?"Emby":item.Type=="Season"?L.T("暂无海报"):item.Type=="Episode"?L.T("暂无缩略图"):item.IsBrowsable?L.T("媒体库"):posterView==EmbyLibraryView.Table?L.T("无图片"):L.T("暂无图片");
            slot.imageBinding=RegisterImage(slot.image,slot.artwork,artwork.ItemId,layout.ArtworkHeight,artwork.Available,item,artwork.Tag,artwork.Type);
            float progress=Progress(item);slot.progressRail.gameObject.SetActive(progress>0);slot.progress.gameObject.SetActive(progress>0);slot.progress.sizeDelta=new Vector2(layout.ArtworkWidth*progress,6);slot.watched.gameObject.SetActive(item.UserData?.Played==true);
        }
        private void ClearPosterWall(){posterPool.Clear();posterItems=Array.Empty<EmbyItem>();posterFirstRow=int.MinValue;posterSpecialRow=-1;}
        private void ClearCast()
        {
            castRegion?.Clear();castPool.Clear();castPeople=Array.Empty<EmbyPerson>();castScroll=null;castBody=null;castRegion=null;castFirst=-1;
        }
        private void BuildCast(EmbyPerson[] people,float top)
        {
            castPeople=people;
            var host=UiFactory.Rect(body,"Cast horizontal list",14,top,BodyWidth-28,286);
            castRegion=host.gameObject.AddComponent<QuickPointerRegion>();
            castScroll=host.gameObject.AddComponent<ListScrollRect>();castScroll.horizontal=true;castScroll.vertical=false;castScroll.movementType=ScrollRect.MovementType.Clamped;castScroll.scrollSensitivity=64;
            var viewport=UiFactory.Box(host,"Cast viewport",0,0,BodyWidth-28,264,Color.clear,true);viewport.gameObject.AddComponent<RectMask2D>();castScroll.viewport=viewport;
            castBody=UiFactory.Rect(viewport,"Cast cards",0,0,Mathf.Max(viewport.rect.width,people.Length*216-16),264);castScroll.content=castBody;
            var rail=UiFactory.Box(host,"Cast scrollbar",0,276,BodyWidth-28,10,UiTheme.Rail,true);var scrollbar=rail.gameObject.AddComponent<Scrollbar>();
            var handle=UiFactory.Box(rail,"Handle",0,0,BodyWidth-28,10,Green,true);handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;
            scrollbar.handleRect=handle;scrollbar.targetGraphic=handle.GetComponent<Image>();scrollbar.direction=Scrollbar.Direction.LeftToRight;castScroll.horizontalScrollbar=scrollbar;
            int count=Mathf.Min(people.Length,Mathf.CeilToInt(viewport.rect.width/216)+2);
            for(int i=0;i<count;i++){
                var slot=new CastSlot();var button=Button(castBody,"",0,0,200,264,null);button.name="Cast";UiFactory.Clear(button.transform);slot.root=(RectTransform)button.transform;
                slot.binding=slot.root.gameObject.AddComponent<RowBinding>();slot.root.gameObject.AddComponent<RowPress>().Bind(slot.binding);button.onClick.AddListener(slot.binding.Open);
                slot.art=UiFactory.RoundedBox(slot.root,"Portrait",28,8,144,180,UiTheme.Elevated);
                slot.placeholder=UiFactory.Label(slot.art,L.T("暂无头像"),0,0,144,180,23,TextAlignmentOptions.Center);slot.placeholder.color=Muted;
                var imageRect=UiFactory.Rect(slot.art,"Image",0,0,144,180);imageRect.pivot=new Vector2(.5f,.5f);slot.image=imageRect.gameObject.AddComponent<RawImage>();slot.image.color=Color.clear;slot.image.raycastTarget=false;
                var fitter=imageRect.gameObject.AddComponent<AspectRatioFitter>();fitter.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fitter.aspectRatio=.8f;
                slot.name=UiFactory.Label(slot.root,"",12,192,176,40,25);slot.role=UiFactory.Label(slot.root,"",12,228,176,36,23);slot.role.color=Muted;castPool.Add(slot);
            }
            castScroll.onValueChanged.AddListener(_=>UpdateCast());UpdateCast();
        }
        public void ScrollCast(int direction){if(CastHovered)MoveCast(direction);}
        private void MoveCast(int direction)
        {
            if(castScroll==null||!castScroll.enabled||castScroll.HasDragOwner)return;castScroll.StopMovement();float offset=Mathf.Clamp(-castBody.anchoredPosition.x+Mathf.Sign(direction)*216,0,Mathf.Max(0,castBody.rect.width-castScroll.viewport.rect.width));
            castBody.anchoredPosition=new Vector2(-offset,0);UpdateCast();
        }
        private void UpdateCast()
        {
            if(castBody==null||castPool.Count==0)return;
            int first=Mathf.Max(0,Mathf.FloorToInt(-castBody.anchoredPosition.x/216));if(first==castFirst)return;castFirst=first;
            for(int i=0;i<castPool.Count;i++){
                var slot=castPool[i];UnbindImage(slot.imageBinding);slot.imageBinding=null;int index=first+i;
                slot.root.gameObject.SetActive(index<castPeople.Length);slot.binding.Item=null;if(index>=castPeople.Length)continue;
                var person=castPeople[index];slot.root.anchoredPosition=new Vector2(index*216,0);slot.name.text=person.Name;slot.role.text=string.IsNullOrEmpty(person.Role)?person.Type:person.Role;
                slot.placeholder.gameObject.SetActive(true);slot.root.GetComponent<Button>().interactable=!string.IsNullOrEmpty(person.Id);
                slot.binding.Item=new BrowserRow{key=(app.ActiveEmbyAccount?.id??"")+":"+person.Id,open=()=>{closeKeyboard?.Invoke();app.OpenEmbyPerson(person);}};
                slot.imageBinding=RegisterImage(slot.image,slot.art,person.Id,180,!string.IsNullOrEmpty(person.PrimaryImageTag),null,person.PrimaryImageTag,"Primary");
            }
            imageScheduleDirty=true;
        }
        private void DrawDetails()
        {
            var item=app.EmbyState.selected;if(item==null){UiFactory.Label(root,L.T("无法读取影片详情"),24,170,Width-48,66,30);return;}
            BeginScroll(76,root.rect.height-90);
            var artwork=EmbyLibraryViews.Artwork(item,EmbyLibraryView.Poster);
            Image(body,artwork.ItemId,14,8,246,item.Type=="Episode"?246*9f/16:369,artwork.Available,item,artwork.Tag,artwork.Type);
            var name=UiFactory.Label(body,DisplayName(item),290,8,BodyWidth-304,78,36);name.textWrappingMode=TextWrappingModes.Normal;name.alignment=TextAlignmentOptions.TopLeft;
            name.overflowMode=TextOverflowModes.Overflow;
            float nameHeight=Mathf.Max(78,name.GetPreferredValues(name.text,BodyWidth-304,float.PositiveInfinity).y+12);name.rectTransform.sizeDelta=new Vector2(BodyWidth-304,nameHeight);
            float metaY=nameHeight+20;
            UiFactory.Label(body,DetailsMeta(item),290,metaY,BodyWidth-304,42,25).color=Muted;
            float playY=metaY+60;
            float actionsBottom=DrawDetailActions(item,playY);
            string overview=string.IsNullOrWhiteSpace(item.Overview)?L.T("暂无影片简介"):PlainOverview(item.Overview);
            if(overview.Length>10000)overview=overview.Substring(0,10000)+"…";
            float overviewY=actionsBottom+22;
            UiFactory.Label(body,L.T("简介"),290,overviewY,BodyWidth-304,46,28);
            var description=UiFactory.Label(body,overview,290,overviewY+52,BodyWidth-304,100,24);description.textWrappingMode=TextWrappingModes.Normal;description.alignment=TextAlignmentOptions.TopLeft;
            float overviewHeight=Mathf.Max(100,description.GetPreferredValues(overview,BodyWidth-304,float.PositiveInfinity).y+12);description.rectTransform.sizeDelta=new Vector2(BodyWidth-304,overviewHeight);
            float categoryY=overviewY+64+overviewHeight;
            categoryY=CategoryRow(L.T("类型"),"Genre",Categories(item.GenreItems,item.Genres),categoryY);
            categoryY=CategoryRow(L.T("标签"),"Tag",Categories(item.TagItems,item.Tags),categoryY);
            categoryY=CategoryRow(L.T("制片厂"),"Studio",Categories(item.Studios,null),categoryY);
            float y=Mathf.Max(410,categoryY);
            y=DetailVideos(L.T("影片片段"),app.EmbyState.parts,y,true);
            var people=(item.People??Array.Empty<EmbyPerson>()).Where(person=>person!=null&&!string.IsNullOrWhiteSpace(person.Name)).ToArray();
            if(people.Length>0){float heading=y+16;y=SectionTitle(L.T("演员与主创"),heading)+22;BuildCast(people,y);UiFactory.IconButton(body,"←",QuickIcon.Back,BodyWidth-158,heading,()=>MoveCast(-1),tooltip:"向左浏览演员与主创");UiFactory.IconButton(body,"→",QuickIcon.Forward,BodyWidth-78,heading,()=>MoveCast(1),tooltip:"向右浏览演员与主创");y+=306;}
            y=DetailVideos(L.T("花絮与预告"),app.EmbyState.extras,y,false);
            FinishScroll(y+20);
        }
        private float DrawDetailActions(EmbyItem item,float y)
        {
            if(item.IsBrowsable){Button(body,L.T("浏览季 / 剧集"),290,y,280,64,()=>app.OpenEmbyLibrary(item),true);return y+64;}
            var last=app.EmbyMovieResume;
            long currentTicks=app.EmbyPartProgress(item)?.PositionTicks??item.UserData?.PlaybackPositionTicks??0;
            bool played=item.Type=="Movie"&&app.EmbyState.parts.Length>1?app.EmbyState.parts.All(part=>part.UserData?.Played==true):item.UserData?.Played==true;
            int cd=last==null?1:Array.FindIndex(app.EmbyState.parts,part=>part.Id==last.Item.Id)+1;
            string progress=last!=null?(last.HasLastPlayed?L.T("上次播放"):L.T("已保存进度"))+"：CD"+cd+" · "+TicksClock(last.PositionTicks):currentTicks>0?L.T("已播放 · ")+TicksClock(currentTicks):played?L.T("已播放"):"";
            if(!string.IsNullOrEmpty(progress)){UiFactory.Label(body,progress,290,y,BodyWidth-304,40,24).color=Muted;y+=50;}
            float width=BodyWidth-304;int columns=width>=722?3:2;float actionWidth=(width-(columns-1)*16)/columns;
            int count=0;
            void Action(string title,QuickIcon icon,System.Action callback,bool primary=false,bool selected=false,bool enabled=true)
            {
                var button=Button(body,title,290+count%columns*(actionWidth+16),y+count/columns*80,actionWidth,64,callback,primary||selected);
                button.name="Emby action "+title;button.interactable=enabled;
                var label=button.GetComponentInChildren<TextMeshProUGUI>();label.fontSize=23;label.rectTransform.anchoredPosition=new Vector2(50,0);label.rectTransform.sizeDelta=new Vector2(actionWidth-62,64);
                UiFactory.Icon(button.transform,icon,16,20,24,primary||selected?UiTheme.Ink:UiTheme.Text);count++;
            }
            bool resume=last!=null||currentTicks>0;
            Action(last!=null?last.PositionTicks>0?L.T("继续播放 · CD")+cd:L.T("播放上次 CD"):currentTicks>0?L.T("继续播放"):L.T("播放"),QuickIcon.Play,()=>{if(last!=null)app.ContinueEmbyMovie();else app.PlayEmby(item,true);},true,enabled:!app.EmbyPlayedBusy);
            if(resume)Action(app.EmbyState.parts.Length>1?L.T("CD1 从头播放"):L.T("从头播放"),QuickIcon.Play,()=>app.PlayEmby(item,false),enabled:!app.EmbyPlayedBusy);
            if(item.IsPlayable)Action(!string.IsNullOrEmpty(app.EmbyState.playedItemId)?L.T("正在同步…"):played?L.T("标记为未播放"):L.T("标记为已播放"),QuickIcon.Check,PlayedAction(item,!played),selected:played,enabled:!app.EmbyPlayedBusy);
            if(last!=null&&last.Item.Id!=item.Id&&currentTicks>0)Action(L.T("仅继续 CD1"),QuickIcon.Play,()=>app.PlayEmby(item,true),enabled:!app.EmbyPlayedBusy);
            float bottom=y+Mathf.CeilToInt(count/(float)columns)*80-16;
            if(app.EmbyState.parts.Length>1){UiFactory.Label(body,L.T("播放状态应用到整部电影的全部 CD"),290,bottom+10,width,38,22).color=Muted;bottom+=48;}
            if(!string.IsNullOrEmpty(app.EmbyState.playedError)){
                var error=UiFactory.Label(body,L.Message(app.EmbyState.playedError),290,bottom+12,width,44,23);error.textWrappingMode=TextWrappingModes.Normal;
                float height=Mathf.Max(44,error.GetPreferredValues(error.text,width,float.PositiveInfinity).y+12);error.rectTransform.sizeDelta=new Vector2(width,height);bottom+=height+12;
            }
            return bottom;
        }
        private static List<KeyValuePair<string,string>> Categories(EmbyNamedItem[] named,string[] names)
        {
            var result=new List<KeyValuePair<string,string>>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in named??Array.Empty<EmbyNamedItem>())if(item!=null&&!string.IsNullOrWhiteSpace(item.Name)&&seen.Add(item.Name.Trim()))result.Add(new KeyValuePair<string,string>(item.Name.Trim(),item.Id>0?item.Id.ToString(CultureInfo.InvariantCulture):""));
            foreach(var name in names??Array.Empty<string>())if(!string.IsNullOrWhiteSpace(name)&&seen.Add(name.Trim()))result.Add(new KeyValuePair<string,string>(name.Trim(),""));
            return result;
        }
        private float CategoryRow(string title,string kind,List<KeyValuePair<string,string>> entries,float y)
        {
            if(entries.Count==0)return y;
            var label=UiFactory.Label(body,title,290,y,106,44,23);label.color=Muted;float x=410,rowY=y;
            foreach(var entry in entries){
                var value=entry;float width=Mathf.Clamp(label.GetPreferredValues(value.Key,float.PositiveInfinity,44).x+28,90,400);
                width=Mathf.Min(width,BodyWidth-424);if(x+width>BodyWidth-14){x=410;rowY+=54;}
                var button=Button(body,value.Key,x,rowY,width,44,()=>{closeKeyboard?.Invoke();app.OpenEmbyCategory(kind,value.Key,value.Value);});button.GetComponentInChildren<TextMeshProUGUI>().fontSize=23;
                x+=width+12;
            }
            return rowY+54;
        }
        private void DrawPerson()
        {
            var item=app.EmbyState.selected;if(item==null){UiFactory.Label(root,L.T("无法读取人物详情"),24,170,Width-48,66,30);return;}
            BeginScroll(76,root.rect.height-90);Image(body,item.Id,14,8,246,369,!string.IsNullOrEmpty(item.ImageTags?.Primary),item);
            var name=UiFactory.Label(body,item.Name??L.T("未命名人物"),290,8,BodyWidth-304,78,36);name.textWrappingMode=TextWrappingModes.Normal;name.alignment=TextAlignmentOptions.TopLeft;
            float nameHeight=Mathf.Max(78,name.GetPreferredValues(name.text,BodyWidth-304,float.PositiveInfinity).y+12);name.rectTransform.sizeDelta=new Vector2(BodyWidth-304,nameHeight);
            float y=PersonLine(L.T("演员与主创"),nameHeight+28);
            if(!string.IsNullOrWhiteSpace(item.PremiereDate))y=PersonLine(L.T("出生日期 · ")+DateText(item.PremiereDate),y);
            var locations=(item.ProductionLocations??Array.Empty<string>()).Where(location=>!string.IsNullOrWhiteSpace(location)).Distinct().ToArray();
            if(locations.Length>0)y=PersonLine(L.T("出生地 · ")+string.Join(" / ",locations),y);
            if(!string.IsNullOrWhiteSpace(item.EndDate))y=PersonLine(L.T("逝世日期 · ")+DateText(item.EndDate),y);
            y+=16;UiFactory.Label(body,L.T("简介"),290,y,BodyWidth-304,46,28);y+=52;
            string overview=string.IsNullOrWhiteSpace(item.Overview)?L.T("暂无人物简介"):PlainOverview(item.Overview);if(overview.Length>10000)overview=overview.Substring(0,10000)+"…";
            var description=UiFactory.Label(body,overview,290,y,BodyWidth-304,100,24);description.textWrappingMode=TextWrappingModes.Normal;description.alignment=TextAlignmentOptions.TopLeft;
            float height=Mathf.Max(100,description.GetPreferredValues(overview,BodyWidth-304,float.PositiveInfinity).y+12);description.rectTransform.sizeDelta=new Vector2(BodyWidth-304,height);
            y=Mathf.Max(410,y+height+24);var items=app.EmbyState.items??Array.Empty<EmbyItem>();y=SectionTitle(L.F("出演作品 · {0}", items.Length),y+16);
            if(items.Length==0){UiFactory.Label(body,L.T("暂无可显示的出演作品"),18,y,BodyWidth-36,58,26).color=Muted;y+=78;}
            else y=BuildPosterWall(items,y);
            FinishScroll(y+20);
        }
        private float PersonLine(string text,float y)
        {
            var label=UiFactory.Label(body,text,290,y,BodyWidth-304,40,24);label.color=Muted;label.textWrappingMode=TextWrappingModes.Normal;label.alignment=TextAlignmentOptions.TopLeft;
            float height=Mathf.Max(40,label.GetPreferredValues(text,BodyWidth-304,float.PositiveInfinity).y+8);label.rectTransform.sizeDelta=new Vector2(BodyWidth-304,height);return y+height+8;
        }
        private static string DateText(string value)=>DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces,out var date)?date.ToString(L.T("yyyy年M月d日"),L.Culture):value;
        private float DetailVideos(string title,EmbyItem[] items,float y,bool parts)
        {
            var videos=(items??Array.Empty<EmbyItem>()).Where(item=>item!=null&&!string.IsNullOrEmpty(item.Id)).ToArray();
            if(videos.Length==0)return y;
            var last=parts?app.EmbyMovieResume:null;
            y=SectionTitle(title,y+16);
            int columns=Mathf.Max(1,Mathf.FloorToInt((BodyWidth-14)/360));float width=(BodyWidth-28-(columns-1)*18)/columns,artHeight=width*9/16,pitch=artHeight+(L.Language==AppLanguage.Chinese?118:146);
            for(int i=0;i<videos.Length;i++){
                var video=videos[i];float x=14+(i%columns)*(width+18),py=y+(i/columns)*pitch;
                var card=Button(body,"",x,py,width,pitch-18,()=>{closeKeyboard?.Invoke();if(parts)app.PlayEmbyPart(video);else app.PlayEmbyExtra(video);});UiFactory.Clear(card.transform);
                string backdropTag=video.BackdropImageTags?.FirstOrDefault();
                string imageType=!string.IsNullOrEmpty(video.ImageTags?.Thumb)?"Thumb":!string.IsNullOrEmpty(backdropTag)?"Backdrop":"Primary";
                string imageTag=imageType=="Thumb"?video.ImageTags.Thumb:imageType=="Backdrop"?backdropTag:video.ImageTags?.Primary;
                Image(card.transform,video.Id,0,0,width,artHeight,!string.IsNullOrEmpty(imageTag),video,imageTag,imageType);
                string name=(parts?"CD"+(i+1)+" · ":"")+(string.IsNullOrWhiteSpace(video.Name)?parts?L.T("影片部分"):L.T("附加篇"):video.Name);
                UiFactory.Label(card.transform,name,12,artHeight+4,width-24,44,25);
                long ticks=video.RunTimeTicks>0?video.RunTimeTicks:(video.MediaSources?.FirstOrDefault()?.RunTimeTicks??0);
                string duration=ticks>0?TicksClock(ticks):L.T("时长未知");
                var progress=parts?app.EmbyPartProgress(video):null;
                string resume=progress?.PositionTicks>0?L.T(" · 进度 ")+TicksClock(progress.PositionTicks):"";
                var subtitle=UiFactory.Label(card.transform,(last?.Item.Id==video.Id?last.HasLastPlayed?L.T("上次播放 · "):L.T("已存进度 · "):parts&&i==0?L.T("主片 · "):"")+duration+resume,12,artHeight+52,width-24,44,22);
                if(L.Language!=AppLanguage.Chinese){subtitle.rectTransform.sizeDelta=new Vector2(width-24,72);subtitle.textWrappingMode=TextWrappingModes.Normal;}
                subtitle.color=last?.Item.Id==video.Id?Green:Muted;
            }
            return y+Mathf.CeilToInt(videos.Length/(float)columns)*pitch;
        }
        private void BeginScroll(float y,float height)
        {
            var container=UiFactory.Rect(root,"Emby scrolling content",14,y,Width-28,height);
            scroll=container.gameObject.AddComponent<ListScrollRect>();scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=48;scroll.decelerationRate=.12f;
            var viewport=UiFactory.Box(container,"Viewport",0,0,BodyWidth,height,Color.clear,true);viewport.gameObject.AddComponent<RectMask2D>();scroll.viewport=viewport;
            body=UiFactory.Rect(viewport,"Cards",0,0,BodyWidth,height);scroll.content=body;
            var barRect=UiFactory.Box(container,"Scrollbar",Width-38,0,10,height,Card,true);var bar=barRect.gameObject.AddComponent<Scrollbar>();
            var handle=UiFactory.Box(barRect,"Handle",0,0,10,height,Muted,true);handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;
            bar.handleRect=handle;bar.targetGraphic=handle.GetComponent<Image>();bar.direction=Scrollbar.Direction.BottomToTop;scroll.verticalScrollbar=bar;
        }
        private void FinishScroll(float height){body.sizeDelta=new Vector2(BodyWidth,Mathf.Max(scroll.viewport.rect.height,height));}
        private static Button Button(Transform parent,string text,float x,float y,float width,float height,Action clicked,bool selected=false)
        {
            var button=UiFactory.Button(parent,text,x,y,width,height,clicked);Paint(button,selected);return button;
        }
        private static void Paint(Button button,bool selected)
        {
            UiFactory.SetAccent(button,selected);
        }
        private void Image(Transform parent,string itemId,float x,float y,float width,float height,bool available,EmbyItem item=null,string imageTag="",string imageType="Primary")
        {
            var holder=UiFactory.RoundedBox(parent,"Artwork",x,y,width,height,UiTheme.Elevated,false,12);holder.gameObject.AddComponent<Mask>().showMaskGraphic=true;
            var placeholder=UiFactory.Label(holder,available?"Emby":item?.IsBrowsable==true?L.T("媒体库"):width>height?L.T("暂无缩略图"):L.T("暂无海报"),8,0,width-16,height,22,TextAlignmentOptions.Center);placeholder.color=Muted;
            if(!available||string.IsNullOrEmpty(itemId)||app.ActiveEmbyAccount==null)return;
            var rect=UiFactory.Rect(holder,"Image",0,0,width,height);rect.pivot=new Vector2(.5f,.5f);var image=rect.gameObject.AddComponent<RawImage>();image.raycastTarget=false;
            var fit=rect.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=item?.Type=="Episode"?AspectRatioFitter.AspectMode.FitInParent:AspectRatioFitter.AspectMode.EnvelopeParent;fit.aspectRatio=width/height;
            RegisterImage(image,holder,itemId,height,available,item,imageTag,imageType);
        }
        private ImageBinding RegisterImage(RawImage image,RectTransform holder,string itemId,float height,bool available,EmbyItem item,string imageTag,string imageType)
        {
            Detach(image);if(!available||string.IsNullOrEmpty(itemId)||app.ActiveEmbyAccount==null)return null;
            var account=app.ActiveEmbyAccount;string tag=string.IsNullOrEmpty(imageTag)?item?.ImageTags?.Primary??"":imageTag,key=account.id+":"+itemId+":"+imageType+":"+tag;
            if(!imageJobs.TryGetValue(key,out var job)){
                string url=app.EmbyApi.ImageUrl(account,itemId,imageType,imageType=="Primary"&&item?.Type!="Episode"?256:428);
                if(!string.IsNullOrEmpty(tag))url+="&tag="+Uri.EscapeDataString(tag);
                job=new ImageJob{key=key,url=url,token=account.accessToken,generation=imageGeneration};imageJobs.Add(key,job);
            }
            float top=0;for(var ancestor=holder;ancestor!=null&&ancestor!=body;ancestor=ancestor.parent as RectTransform)top-=ancestor.anchoredPosition.y;
            var binding=new ImageBinding{target=image,top=top,bottom=top+height,job=job};job.targets.Add(binding);return binding;
        }
        private void UnbindImage(ImageBinding binding)
        {
            if(binding==null)return;Detach(binding.target);var job=binding.job;job.targets.Remove(binding);
            if(job.targets.Count>0)return;
            // A different recycled slot may bind this same artwork later in the batch.
            job.wanted=false;
        }
        private void ScheduleImages()
        {
            if(root==null||!root.gameObject.activeInHierarchy)return;
            imageOffset=Offset;imageViewportHeight=scroll==null?root.rect.height:scroll.viewport.rect.height;imageScheduleDirty=false;
            float bottom=imageOffset+imageViewportHeight;
            pinned.Clear();imageQueue.Clear();
            foreach(var job in imageJobs.Values){
                job.wanted=false;job.priority=float.PositiveInfinity;
                foreach(var binding in job.targets){
                    binding.active=binding.target!=null&&binding.target.gameObject.activeInHierarchy&&binding.bottom>=imageOffset-ImagePreload&&binding.top<=bottom+ImagePreload;
                    if(binding.active){job.wanted=true;job.priority=Mathf.Min(job.priority,Mathf.Max(0,Mathf.Max(imageOffset-binding.bottom,binding.top-bottom)));}
                    else Detach(binding.target);
                }
                if(job.wanted)pinned.Add(job.key);else job.failed=false;
            }
            foreach(var job in imageJobs.Values){
                if(!job.wanted){if(job.request!=null&&!job.cancelled){job.cancelled=true;job.request.Abort();}continue;}
                if(images.TryGetValue(job.key,out var cached)){cached.used=++accessOrder;AssignTargets(job,cached.texture);}
                else if(job.request==null&&!job.failed)imageQueue.Enqueue(job);
            }
            foreach(var key in imageJobs.Where(pair=>pair.Value.targets.Count==0).Select(pair=>pair.Key).ToArray())imageJobs.Remove(key);
            if(imageQueue.Count>1){var ordered=imageQueue.OrderBy(job=>job.priority).ToArray();imageQueue.Clear();foreach(var job in ordered)imageQueue.Enqueue(job);}
            while(imageWorkerCount<3&&imageQueue.Count>0){imageWorkerCount++;StartCoroutine(ImageWorker(imageGeneration));}
        }
        private static void Detach(RawImage image)
        {
            if(image==null)return;image.texture=null;image.color=Color.clear;ImagePlaceholder(image,true);
        }
        private static void Assign(RawImage image,Texture2D texture)
        {
            if(image==null||texture==null)return;image.texture=texture;image.color=Color.white;ImagePlaceholder(image,false);var fit=image.GetComponent<AspectRatioFitter>();if(fit!=null)fit.aspectRatio=texture.width/(float)texture.height;
        }
        private static void ImagePlaceholder(RawImage image,bool visible){var text=image.transform.parent?.Find("Text");if(text!=null)text.gameObject.SetActive(visible);}
        private IEnumerator ImageWorker(int generation)
        {
            try{
                while(generation==imageGeneration&&imageQueue.Count>0){
                    var job=imageQueue.Dequeue();if(!job.wanted||job.failed||job.request!=null||job.generation!=generation||job.targets.Count==0)continue;
                    if(images.TryGetValue(job.key,out var existing)){AssignTargets(job,existing.texture);continue;}
                    var download=new LimitedImageDownload(MaxImageBytes);var request=new UnityWebRequest(job.url,"GET",download,null);request.timeout=12;request.redirectLimit=0;request.SetRequestHeader("X-Emby-Token",job.token??"");requests.Add(request);job.request=request;job.cancelled=false;
                    try{
                        yield return request.SendWebRequest();
                        if(generation==imageGeneration&&job.wanted&&!job.cancelled){
                            job.failed=true;
                            if(request.result==UnityWebRequest.Result.Success){
                                // Three downloads can finish together. Decode at most one
                                // per frame and avoid sharing a frame with card construction.
                                while(generation==imageGeneration&&job.wanted&&!job.cancelled&&(imageDecodeFrame==Time.frameCount||cardBuilds.Count>0))yield return null;
                                if(generation!=imageGeneration||!job.wanted||job.cancelled)continue;
                                imageDecodeFrame=Time.frameCount;
                                byte[] bytes=download.Payload();
                                if(ImageDimensions(bytes,out int width,out int height)&&width<=1024&&height<=1200&&(long)width*height<=786432){
                                    var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                                    bool loaded;using(ImageMarker.Auto())loaded=texture.LoadImage(bytes,true);int cost=loaded?texture.width*texture.height*4:0;
                                    if(loaded&&ReserveTexture(cost)){
                                        texture.wrapMode=TextureWrapMode.Clamp;images[job.key]=new CachedImage{texture=texture,bytes=cost,used=++accessOrder};textureBytes+=cost;job.failed=false;AssignTargets(job,texture);
                                    }else Destroy(texture);
                                }
                            }
                        }
                    }finally{
                        requests.Remove(request);if(job.request==request)job.request=null;request.Dispose();
                    }
                    if(generation==imageGeneration)imageScheduleDirty=true;
                }
            }finally{if(generation==imageGeneration)imageWorkerCount--;}
        }
        private static void AssignTargets(ImageJob job,Texture2D texture)
        {
            foreach(var binding in job.targets)if(binding.active)Assign(binding.target,texture);
        }
        private bool ReserveTexture(int bytes)
        {
            while(images.Count>=MaxTextures||textureBytes+bytes>MaxTextureBytes){
                var oldest=images.Where(pair=>!pinned.Contains(pair.Key)).OrderBy(pair=>pair.Value.used).FirstOrDefault();
                if(oldest.Value==null)return false;
                if(imageJobs.TryGetValue(oldest.Key,out var job))foreach(var binding in job.targets)Detach(binding.target);
                textureBytes-=oldest.Value.bytes;Destroy(oldest.Value.texture);images.Remove(oldest.Key);
            }
            return true;
        }
        private void CancelImages()
        {
            imageGeneration++;foreach(var request in requests){request.Abort();request.Dispose();}requests.Clear();StopAllCoroutines();imageQueue.Clear();imageJobs.Clear();pinned.Clear();imageWorkerCount=0;imageOffset=float.NaN;imageScheduleDirty=false;
        }
        private void OnDestroy()
        {
            CancelImages();ClearPosterWall();ClearCast();pageOffsets.Clear();foreach(var cached in images.Values)if(cached.texture!=null)Destroy(cached.texture);images.Clear();textureBytes=0;password="";
        }
        // Reject large responses before decoding, then validate JPEG/PNG dimensions.
        private sealed class LimitedImageDownload : DownloadHandlerScript
        {
            private readonly byte[] bytes;private int length;private bool rejected;
            public LimitedImageDownload(int limit):base(new byte[32*1024]){bytes=new byte[limit];}
            protected override void ReceiveContentLengthHeader(ulong contentLength){if(contentLength>(ulong)bytes.Length)rejected=true;}
            protected override bool ReceiveData(byte[] data,int count){if(rejected||data==null||count<0||count>bytes.Length-length)return false;Buffer.BlockCopy(data,0,bytes,length,count);length+=count;return true;}
            public byte[] Payload(){if(rejected)return Array.Empty<byte>();var result=new byte[length];Buffer.BlockCopy(bytes,0,result,0,length);return result;}
        }
        private static bool ImageDimensions(byte[] bytes,out int width,out int height)
        {
            width=height=0;if(bytes==null||bytes.Length<24)return false;
            if(bytes[0]==137&&bytes[1]==80&&bytes[2]==78&&bytes[3]==71){width=ReadInt(bytes,16);height=ReadInt(bytes,20);return width>0&&height>0;}
            if(bytes[0]!=255||bytes[1]!=216)return false;
            int position=2;while(position+8<bytes.Length){
                if(bytes[position++]!=255)continue;byte marker=bytes[position++];while(marker==255&&position<bytes.Length)marker=bytes[position++];
                if(marker==216||marker==217||marker==1||marker>=208&&marker<=215)continue;
                if(position+2>bytes.Length)return false;int length=bytes[position]*256+bytes[position+1];if(length<2||position+length>bytes.Length)return false;
                if(marker>=192&&marker<=195||marker>=197&&marker<=199||marker>=201&&marker<=203||marker>=205&&marker<=207){if(length<8)return false;height=bytes[position+3]*256+bytes[position+4];width=bytes[position+5]*256+bytes[position+6];return width>0&&height>0;}
                position+=length;
            }
            return false;
        }
        private static int ReadInt(byte[] bytes,int offset)=>bytes[offset]<<24|bytes[offset+1]<<16|bytes[offset+2]<<8|bytes[offset+3];
        private static string AccountName(EmbyAccount account)=>string.IsNullOrWhiteSpace(account?.name)?account?.username??"Emby":account.name;
        private static string SafeServer(string serverUrl)=>Uri.TryCreate(serverUrl,UriKind.Absolute,out var uri)?uri.GetLeftPart(UriPartial.Authority):L.T("Emby 服务器");
        private static string DisplayName(EmbyItem item)=>item.Type=="Episode"&&!string.IsNullOrEmpty(item.SeriesName)?item.SeriesName+" · "+(item.IndexNumber>0?L.T("第 ")+item.IndexNumber+L.T(" 集 · "):"")+item.Name:item.Name??L.T("未命名影片");
        private static string ItemMeta(EmbyItem item)=>item.IsBrowsable?item.Type=="Season"?L.T("季 / 剧集"):item.Type=="Series"?L.T("剧集"):L.T("媒体库"):item.ProductionYear>0?item.ProductionYear.ToString():item.RunTimeTicks>0?TicksClock(item.RunTimeTicks):L.T("视频");
        private static string DetailsMeta(EmbyItem item)
        {
            var parts=new List<string>();if(item.ProductionYear>0)parts.Add(item.ProductionYear.ToString());if(item.RunTimeTicks>0)parts.Add(((long)TimeSpan.FromTicks(item.RunTimeTicks).TotalMinutes)+L.T(" 分钟"));if(!string.IsNullOrEmpty(item.OfficialRating))parts.Add(item.OfficialRating);if(item.CommunityRating>0)parts.Add(L.T("评分 ")+item.CommunityRating.ToString("0.0"));return string.Join("   ·   ",parts);
        }
        private static float Progress(EmbyItem item)=>item.RunTimeTicks>0?Mathf.Clamp01((item.UserData?.PlaybackPositionTicks??0)/(float)item.RunTimeTicks):Mathf.Clamp01((float)(item.UserData?.PlayedPercentage??0)/100);
        private static string PlaybackProgress(EmbyItem item)=>item.UserData?.Played==true?L.T("已看"):item.UserData?.PlaybackPositionTicks>0?TicksClock(item.UserData.PlaybackPositionTicks):L.T("未播放");
        private static string TicksClock(long ticks){var value=TimeSpan.FromTicks(Math.Max(0,ticks));return value.TotalHours>=1?$"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}":$"{value.Minutes:00}:{value.Seconds:00}";}
        private static string PlainOverview(string text)=>System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(text,"<[^>]*>",""));
    }
}
