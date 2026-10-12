using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace VRPlayer
{
    // Uses the player's Canvas and input paths; only the media browser is replaced.
    public sealed class EmbyBrowserUI : MonoBehaviour
    {
        private static readonly Color Background=new Color(.12f,.12f,.12f,1),Card=new Color(.17f,.17f,.17f,1),Green=new Color(.32f,.71f,.29f,1),Muted=new Color(.67f,.69f,.68f,1);
        private const int MaxImageBytes=2*1024*1024,MaxTextures=48,MaxTextureBytes=24*1024*1024;
        private const float ImagePreload=180;
        private PlayerApp app;
        private RectTransform root,body;
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
            CaptureLogin();RememberOffset();CancelImages();ClearPosterWall();root=null;scroll=null;body=null;addressField=portField=userField=passwordField=null;
        }
        public void Suspend()=>CancelImages();
        public bool TryBack()
        {
            if(app==null)return false;
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
            if(scroll==null)return;scroll.enabled=!blocked;if(blocked)scroll.StopMovement();
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
            if(root==null||app==null)return;var state=app.EmbyState;
            if(!force&&revision==state.revision)return;
            CaptureLogin();RememberOffset();string key=string.Concat(new[]{state.page.ToString(),login.ToString(),state.parent?.Id,state.selected?.Id,state.filterKind,state.filterValue,state.filterId,state.search,app.ActiveEmbyAccount?.id}.Select(value=>(value?.Length??0)+":"+(value??"")));
            float offset=pageOffsets.TryGetValue(key,out var remembered)?remembered:0;viewKey=key;revision=state.revision;
            CancelImages();ClearPosterWall();UiFactory.Clear(root);scroll=null;body=null;addressField=portField=userField=passwordField=null;surfaceChanged?.Invoke();
            UiFactory.Box(root,"Emby background",0,0,1376,root.rect.height,Background,true);
            if(login||state.page==EmbyPage.Accounts){if(login)DrawLogin();else DrawAccounts();}
            else{
                DrawHeader(state.title,state.page!=EmbyPage.Details);
                if(state.loading){UiFactory.Label(root,"正在读取媒体库…",28,160,1320,64,32);UiFactory.Label(root,"请稍候",28,226,1320,44,24).color=Muted;}
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
            float height=scroll==null?root.rect.height:scroll.viewport.rect.height;
            if(imageScheduleDirty||float.IsNaN(imageOffset)||Mathf.Abs(Offset-imageOffset)>1||Mathf.Abs(height-imageViewportHeight)>1){UpdatePosterWall();ScheduleImages();}
        }
        private void DrawHeader(string title,bool showTitle=true)
        {
            Button(root,"返回",14,10,100,48,()=>{closeKeyboard?.Invoke();app.EmbyBack();});
            if(showTitle){var label=UiFactory.Label(root,string.IsNullOrEmpty(title)?"Emby · "+AccountName(app.ActiveEmbyAccount):title,132,8,700,50,30);label.color=Color.white;}
            Button(root,"首页",852,10,120,48,()=>{closeKeyboard?.Invoke();app.EmbyHome();});
            Button(root,"账户与服务器",988,10,220,48,()=>{closeKeyboard?.Invoke();login=false;removing="";app.ShowEmbyAccounts();});
            Button(root,"刷新",1224,10,138,48,()=>app.RefreshEmby());
        }
        private void DrawError(string message)
        {
            var text=UiFactory.Label(root,message,28,150,1300,120,28);text.textWrappingMode=TextWrappingModes.Normal;text.alignment=TextAlignmentOptions.TopLeft;
            Button(root,"重试",28,298,240,56,()=>app.RefreshEmby(),true);
            Button(root,"更换账户",286,298,280,56,()=>{closeKeyboard?.Invoke();login=false;app.ShowEmbyAccounts();});
        }
        private void DrawAccounts()
        {
            UiFactory.Label(root,"Emby · 服务器与账户",24,8,1010,52,32);
            if(!string.IsNullOrEmpty(app.ActiveEmbyAccount?.accessToken))Button(root,"返回媒体库",1120,10,242,48,()=>{login=false;app.EmbyHome();});
            BeginScroll(76,root.rect.height-158);
            var saved=app.EmbyAccounts??Array.Empty<EmbyAccount>();
            float errorHeight=string.IsNullOrEmpty(app.EmbyState.error)?0:68;
            if(errorHeight>0){var error=UiFactory.Label(body,app.EmbyState.error,18,0,1300,58,24);error.textWrappingMode=TextWrappingModes.Normal;}
            if(saved.Count==0){
                UiFactory.Label(body,"连接你的 Emby 媒体库",18,55,1300,66,36);
                UiFactory.Label(body,"使用已有的 Emby 账户登录，可添加多个服务器和账户。",18,131,1300,60,25).color=Muted;
                FinishScroll(260);
            }else{
                int index=0;foreach(var account in saved){
                    var selected=account;float y=errorHeight+index++*108;
                    var card=UiFactory.Box(body,"Server account",10,y,1320,96,Card);
                    bool active=selected.id==app.ActiveEmbyAccount?.id;
                    if(active)UiFactory.Box(card,"Active",0,0,6,96,Green);
                    var open=Button(card,"",14,0,1000,96,()=>{
                        removing="";
                        if(string.IsNullOrEmpty(selected.accessToken)){login=true;EmbyServerAddress.Parse(selected.serverUrl,out useHttps,out serverAddress,out serverPort);username=selected.username;password="";loginError="";remember=selected.remember;Render(true);}
                        else{login=false;app.SelectEmbyAccount(selected.id);}
                    });
                    UiFactory.Clear(open.transform);
                    UiFactory.Label(open.transform,AccountName(selected)+(active?"  ·  当前":""),16,7,958,40,28);
                    UiFactory.Label(open.transform,(selected.username??"")+"  ·  "+SafeServer(selected.serverUrl),16,49,958,36,22).color=Muted;
                    Button(card,removing==selected.id?"确认移除":"移除",1040,24,256,48,()=>{
                        if(removing!=selected.id){removing=selected.id;Render(true);return;}
                        removing="";app.RemoveEmbyAccount(selected.id);Render(true);
                    });
                }
                FinishScroll(errorHeight+index*108);
            }
            Button(root,"添加服务器 / 账户",24,root.rect.height-64,600,54,()=>{login=true;useHttps=false;serverPort="8096";serverAddress=username=password=loginError="";remember=true;Render(true);},true);
            UiFactory.Label(root,"切换账户后显示该账户可访问的媒体库",644,root.rect.height-64,704,54,23,TextAlignmentOptions.Right).color=Muted;
        }
        private void DrawLogin()
        {
            UiFactory.Label(root,"添加 Emby 服务器 / 账户",24,8,1030,54,32);
            Button(root,busy?"取消登录":"返回",1180,10,182,48,ReturnFromLogin);
            UiFactory.Label(root,"输入服务器地址，并使用该服务器已有的账户登录",24,73,1320,44,24).color=Muted;
            UiFactory.Label(root,"连接协议",24,130,224,52,26);
            var protocol=Button(root,useHttps?"HTTPS":"HTTP",258,130,190,52,()=>{CaptureLogin();closeKeyboard?.Invoke();useHttps=!useHttps;Render(true);},true);protocol.interactable=!busy;
            UiFactory.Label(root,"点击按钮切换 HTTP / HTTPS",468,130,894,52,24).color=Muted;
            UiFactory.Label(root,"地址 / IP",24,194,224,52,26);
            addressField=UiFactory.Input(root,serverAddress,"IP 地址或域名",258,194,788,52,keyboard);addressField.name="Emby server address";
            UiFactory.Label(root,"端口",1062,194,86,52,26);
            portField=UiFactory.Input(root,serverPort,"8096",1164,194,198,52,keyboard);portField.name="Emby server port";portField.characterLimit=5;
            UiFactory.Label(root,"用户名",24,258,224,52,26);
            userField=UiFactory.Input(root,username,"Emby 用户名",258,258,1104,52,keyboard);
            UiFactory.Label(root,"密码",24,322,224,52,26);
            passwordField=UiFactory.Input(root,password,"账户密码",258,322,1104,52,keyboard,true);
            addressField.interactable=portField.interactable=userField.interactable=passwordField.interactable=!busy;
            var keep=Button(root,remember?"记住登录：开":"记住登录：关",24,390,350,48,null,remember);keep.interactable=!busy;
            keep.onClick.AddListener(()=>{remember=!remember;keep.GetComponentInChildren<TextMeshProUGUI>().text=remember?"记住登录：开":"记住登录：关";Paint(keep,remember);});
            UiFactory.Label(root,"可在账户列表切换或移除登录",398,390,948,48,23).color=Muted;
            string error=string.IsNullOrEmpty(loginError)?app.EmbyState.error:loginError;
            var errorText=UiFactory.Label(root,busy?"正在登录…":error??"",24,442,1328,36,22);errorText.textWrappingMode=TextWrappingModes.Normal;
            var submit=Button(root,busy?"正在连接…":"登录并打开媒体库",24,486,1338,54,()=>{CaptureLogin();closeKeyboard?.Invoke();_ = Login();},true);submit.interactable=!busy;
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
            if(string.IsNullOrWhiteSpace(username)){loginError="请填写 Emby 用户名";Render(true);return;}
            int capturedGeneration=++loginGeneration;
            busy=true;loginError="";Render(true);
            try{
                await app.LoginEmby(server,username,password,remember);
                if(this==null||capturedGeneration!=loginGeneration||!login)return;
                if(app.EmbyState.page!=EmbyPage.Accounts&&app.ActiveEmbyAccount!=null){login=false;passwordField?.SetTextWithoutNotify("");password="";}
            }catch(OperationCanceledException){}catch(Exception){if(this!=null&&capturedGeneration==loginGeneration&&login)loginError="登录失败，请检查服务器地址与账户";}
            finally{if(this!=null&&capturedGeneration==loginGeneration){busy=false;Render(true);}}
        }
        private void DrawHome()
        {
            BeginScroll(76,root.rect.height-90);var state=app.EmbyState;float y=0;
            y=SectionTitle("我的媒体",y);
            var views=state.views??Array.Empty<EmbyItem>();
            if(views.Length==0){UiFactory.Label(body,"此账户没有可访问的媒体库",18,y,1300,58,26).color=Muted;y+=78;}
            else{
                for(int i=0;i<views.Length;i++)LibraryCard(views[i],14+(i%4)*332,y+(i/4)*182);
                y+=Mathf.CeilToInt(views.Length/4f)*182+16;
            }
            y=HomePosters("继续播放",state.resume,y,"暂无未看完的影片，播放进度会同步到 Emby");
            y=HomePosters("最新加入",state.latest,y,"媒体库中暂无可显示的影片");
            FinishScroll(y+12);
        }
        private float SectionTitle(string title,float y)
        {
            UiFactory.Box(body,"Section accent",14,y+9,4,28,Green);
            UiFactory.Label(body,title,30,y,1288,48,30);return y+58;
        }
        private float HomePosters(string title,EmbyItem[] items,float y,string empty)
        {
            y=SectionTitle(title,y);items=items??Array.Empty<EmbyItem>();
            if(items.Length==0){UiFactory.Label(body,empty,18,y,1300,48,24).color=Muted;return y+78;}
            int count=Math.Min(12,items.Length);for(int i=0;i<count;i++)PosterCard(items[i],14+(i%6)*222,y+(i/6)*344);
            return y+Mathf.CeilToInt(count/6f)*344+18;
        }
        private void LibraryCard(EmbyItem item,float x,float y)
        {
            var captured=item;var card=Button(body,"",x,y,314,166,()=>app.OpenEmbyLibrary(captured));UiFactory.Clear(card.transform);
            Image(card.transform,item.Id,0,0,314,122,!string.IsNullOrEmpty(item.ImageTags?.Primary),item);
            UiFactory.Label(card.transform,item.Name??"媒体库",12,126,290,40,25);
        }
        private void DrawLibrary()
        {
            var state=app.EmbyState;
            var search=UiFactory.Input(root,state.search??"","搜索当前影片列表",14,78,1040,48,keyboard);
            Button(root,"搜索",1070,78,138,48,()=>{closeKeyboard?.Invoke();app.SearchEmbyLibrary(search.text);},true);
            Button(root,"清空",1224,78,138,48,()=>{closeKeyboard?.Invoke();app.SearchEmbyLibrary("");});
            BeginScroll(142,root.rect.height-156);var items=state.items??Array.Empty<EmbyItem>();
            if(items.Length==0){UiFactory.Label(body,string.IsNullOrEmpty(state.search)?"此媒体库暂无影片":"未找到匹配的影片",18,80,1290,68,30).color=Muted;FinishScroll(260);}
            else{BuildPosterWall(items,0);FinishScroll(Mathf.CeilToInt(items.Length/6f)*344);}
        }
        private void PosterCard(EmbyItem item,float x,float y)
        {
            var captured=item;var card=Button(body,"",x,y,204,326,()=>app.OpenEmbyItem(captured));UiFactory.Clear(card.transform);
            Image(card.transform,item.Id,0,0,204,246,!string.IsNullOrEmpty(item.ImageTags?.Primary),item);
            var name=UiFactory.Label(card.transform,DisplayName(item),8,250,188,42,24);name.alignment=TextAlignmentOptions.Left;
            UiFactory.Label(card.transform,ItemMeta(item),8,292,188,34,20).color=Muted;
            float progress=Progress(item);if(progress>0){UiFactory.Box(card.transform,"Progress rail",0,240,204,6,new Color(0,0,0,.6f));UiFactory.Box(card.transform,"Progress",0,240,204*progress,6,Green);}
            if(item.UserData?.Played==true){UiFactory.Box(card.transform,"Watched",150,8,44,28,Green);UiFactory.Label(card.transform,"已看",150,8,44,28,17,TextAlignmentOptions.Center);}
        }
        private float BuildPosterWall(EmbyItem[] items,float top)
        {
            posterItems=items??Array.Empty<EmbyItem>();posterTop=top;posterFirstRow=int.MinValue;
            int count=Math.Min(posterItems.Length,(Mathf.CeilToInt((scroll.viewport.rect.height+ImagePreload*2)/344)+1)*6);
            for(int i=0;i<count;i++){
                var slot=new PosterSlot();var card=Button(body,"",14,top,204,326,null);UiFactory.Clear(card.transform);slot.root=(RectTransform)card.transform;
                slot.binding=slot.root.gameObject.AddComponent<RowBinding>();slot.root.gameObject.AddComponent<RowPress>().Bind(slot.binding);card.onClick.AddListener(slot.binding.Open);
                slot.artwork=UiFactory.Box(slot.root,"Artwork",0,0,204,246,new Color(.09f,.1f,.095f,1));
                slot.placeholder=UiFactory.Label(slot.artwork,"",8,0,188,246,22,TextAlignmentOptions.Center);slot.placeholder.color=Muted;
                var imageRect=UiFactory.Rect(slot.artwork,"Image",0,0,204,246);imageRect.pivot=new Vector2(.5f,.5f);slot.image=imageRect.gameObject.AddComponent<RawImage>();slot.image.raycastTarget=false;slot.image.color=Color.clear;
                var fit=imageRect.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=204f/246;
                slot.label=UiFactory.Label(slot.root,"",8,250,188,42,24);slot.meta=UiFactory.Label(slot.root,"",8,292,188,34,20);slot.meta.color=Muted;
                slot.progressRail=UiFactory.Box(slot.root,"Progress rail",0,240,204,6,new Color(0,0,0,.6f));slot.progress=UiFactory.Box(slot.root,"Progress",0,240,0,6,Green);
                slot.watched=UiFactory.Box(slot.root,"Watched",150,8,44,28,Green);UiFactory.Label(slot.watched,"已看",0,0,44,28,17,TextAlignmentOptions.Center);
                posterPool.Add(slot);
            }
            return top+Mathf.CeilToInt(posterItems.Length/6f)*344;
        }
        private void UpdatePosterWall()
        {
            if(posterPool.Count==0||body==null)return;
            int first=Mathf.Clamp(Mathf.FloorToInt((Offset-posterTop-ImagePreload)/344),0,Mathf.Max(0,Mathf.CeilToInt(posterItems.Length/6f)-1));
            if(first==posterFirstRow)return;posterFirstRow=first;
            for(int i=0;i<posterPool.Count;i++){
                var slot=posterPool[i];UnbindImage(slot.imageBinding);slot.imageBinding=null;
                int index=first*6+i;var item=index<posterItems.Length?posterItems[index]:null;
                slot.root.gameObject.SetActive(item!=null);if(item==null){slot.binding.Item=null;continue;}
                slot.root.anchoredPosition=new Vector2(14+(i%6)*222,-(posterTop+(index/6)*344));
                slot.binding.Item=new BrowserRow{key=(app.ActiveEmbyAccount?.id??"")+":"+item.Id,open=()=>app.OpenEmbyItem(item)};
                slot.root.GetComponent<Button>().interactable=!string.IsNullOrEmpty(item.Id);
                slot.label.text=DisplayName(item);slot.meta.text=ItemMeta(item);
                bool available=!string.IsNullOrEmpty(item.ImageTags?.Primary);slot.placeholder.text=available?"Emby":item.IsBrowsable?"媒体库":"暂无海报";
                slot.imageBinding=RegisterImage(slot.image,slot.artwork,item.Id,246,available,item,"","Primary");
                float progress=Progress(item);slot.progressRail.gameObject.SetActive(progress>0);slot.progress.gameObject.SetActive(progress>0);slot.progress.sizeDelta=new Vector2(204*progress,6);slot.watched.gameObject.SetActive(item.UserData?.Played==true);
            }
            imageScheduleDirty=true;
        }
        private void ClearPosterWall(){posterPool.Clear();posterItems=Array.Empty<EmbyItem>();posterFirstRow=int.MinValue;}
        private void DrawDetails()
        {
            var item=app.EmbyState.selected;if(item==null){UiFactory.Label(root,"无法读取影片详情",24,170,1300,66,30);return;}
            BeginScroll(76,root.rect.height-90);
            Image(body,item.Id,14,8,246,369,!string.IsNullOrEmpty(item.ImageTags?.Primary),item);
            var name=UiFactory.Label(body,DisplayName(item),290,8,1026,78,36);name.textWrappingMode=TextWrappingModes.Normal;name.alignment=TextAlignmentOptions.TopLeft;
            name.overflowMode=TextOverflowModes.Overflow;
            float nameHeight=Mathf.Max(78,name.GetPreferredValues(name.text,1026,float.PositiveInfinity).y+12);name.rectTransform.sizeDelta=new Vector2(1026,nameHeight);
            float metaY=nameHeight+20;
            UiFactory.Label(body,DetailsMeta(item),290,metaY,1026,42,25).color=Muted;
            float playY=metaY+60;
            float actionsBottom=playY+56;
            if(item.IsBrowsable){Button(body,"浏览季 / 剧集",290,playY,416,56,()=>app.OpenEmbyLibrary(item),true);}
            else{
                var last=app.EmbyMovieResume;
                long currentTicks=app.EmbyPartProgress(item)?.PositionTicks??item.UserData?.PlaybackPositionTicks??0;
                if(last!=null){
                    int cd=Array.FindIndex(app.EmbyState.parts,part=>part.Id==last.Item.Id)+1;
                    var label=UiFactory.Label(body,(last.HasLastPlayed?"上次播放":"已保存进度")+"：CD"+cd+" · "+DisplayName(last.Item),290,playY,1026,42,25);label.color=Muted;
                    float primaryY=playY+48;
                    Button(body,(last.PositionTicks>0?"继续播放":"播放上次部分")+" · CD"+cd+(last.PositionTicks>0?" · "+TicksClock(last.PositionTicks):""),290,primaryY,650,56,()=>app.ContinueEmbyMovie(),true);
                    actionsBottom=primaryY+56;
                    if(last.Item.Id!=item.Id){
                        float secondaryY=primaryY+68;
                        if(currentTicks>0)Button(body,"仅继续 CD1 · "+TicksClock(currentTicks),290,secondaryY,440,48,()=>app.PlayEmby(item,true));
                        Button(body,"CD1 从头播放",currentTicks>0?750:290,secondaryY,360,48,()=>app.PlayEmby(item,false));
                        actionsBottom=secondaryY+48;
                    }else Button(body,"从头播放",962,primaryY,350,56,()=>app.PlayEmby(item,false));
                }else{
                    Button(body,currentTicks>0?"继续播放 · "+TicksClock(currentTicks):"播放",290,playY,416,56,()=>app.PlayEmby(item,true),true);
                    if(currentTicks>0)Button(body,"从头播放",728,playY,260,56,()=>app.PlayEmby(item,false));
                }
            }
            string overview=string.IsNullOrWhiteSpace(item.Overview)?"暂无影片简介":PlainOverview(item.Overview);
            if(overview.Length>10000)overview=overview.Substring(0,10000)+"…";
            float overviewY=actionsBottom+22;
            UiFactory.Label(body,"简介",290,overviewY,1026,46,28);
            var description=UiFactory.Label(body,overview,290,overviewY+52,1026,100,24);description.textWrappingMode=TextWrappingModes.Normal;description.alignment=TextAlignmentOptions.TopLeft;
            float overviewHeight=Mathf.Max(100,description.GetPreferredValues(overview,1026,float.PositiveInfinity).y+12);description.rectTransform.sizeDelta=new Vector2(1026,overviewHeight);
            float categoryY=overviewY+64+overviewHeight;
            categoryY=CategoryRow("类型","Genre",Categories(item.GenreItems,item.Genres),categoryY);
            categoryY=CategoryRow("标签","Tag",Categories(item.TagItems,item.Tags),categoryY);
            categoryY=CategoryRow("制片厂","Studio",Categories(item.Studios,null),categoryY);
            float y=Mathf.Max(410,categoryY);
            y=DetailVideos("额外部分",app.EmbyState.parts,y,true);
            var people=(item.People??Array.Empty<EmbyPerson>()).Where(person=>person!=null&&!string.IsNullOrWhiteSpace(person.Name)).Take(24).ToArray();
            if(people.Length>0){
                y=SectionTitle("演员与主创",y+16);
                for(int i=0;i<people.Length;i++){
                    var person=people[i];float x=14+(i%6)*222,py=y+(i/6)*258;
                    var card=Button(body,"",x,py,204,248,()=>{closeKeyboard?.Invoke();app.OpenEmbyPerson(person);});card.name="Cast";card.interactable=!string.IsNullOrEmpty(person.Id);UiFactory.Clear(card.transform);
                    Image(card.transform,person.Id,32,8,140,170,!string.IsNullOrEmpty(person.PrimaryImageTag),null,person.PrimaryImageTag);
                    UiFactory.Label(card.transform,person.Name,8,179,188,34,23);
                    UiFactory.Label(card.transform,string.IsNullOrEmpty(person.Role)?person.Type:person.Role,8,213,188,34,20).color=Muted;
                }
                y+=Mathf.CeilToInt(people.Length/6f)*258;
            }
            y=DetailVideos("附加篇",app.EmbyState.extras,y,false);
            FinishScroll(y+20);
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
                if(x+width>1316){x=410;rowY+=54;}
                var button=Button(body,value.Key,x,rowY,width,44,()=>{closeKeyboard?.Invoke();app.OpenEmbyCategory(kind,value.Key,value.Value);});button.GetComponentInChildren<TextMeshProUGUI>().fontSize=23;
                x+=width+12;
            }
            return rowY+54;
        }
        private void DrawPerson()
        {
            var item=app.EmbyState.selected;if(item==null){UiFactory.Label(root,"无法读取人物详情",24,170,1300,66,30);return;}
            BeginScroll(76,root.rect.height-90);Image(body,item.Id,14,8,246,369,!string.IsNullOrEmpty(item.ImageTags?.Primary),item);
            var name=UiFactory.Label(body,item.Name??"未命名人物",290,8,1026,78,36);name.textWrappingMode=TextWrappingModes.Normal;name.alignment=TextAlignmentOptions.TopLeft;
            float nameHeight=Mathf.Max(78,name.GetPreferredValues(name.text,1026,float.PositiveInfinity).y+12);name.rectTransform.sizeDelta=new Vector2(1026,nameHeight);
            float y=PersonLine("演员与主创",nameHeight+28);
            if(!string.IsNullOrWhiteSpace(item.PremiereDate))y=PersonLine("出生日期 · "+DateText(item.PremiereDate),y);
            var locations=(item.ProductionLocations??Array.Empty<string>()).Where(location=>!string.IsNullOrWhiteSpace(location)).Distinct().ToArray();
            if(locations.Length>0)y=PersonLine("出生地 · "+string.Join(" / ",locations),y);
            if(!string.IsNullOrWhiteSpace(item.EndDate))y=PersonLine("逝世日期 · "+DateText(item.EndDate),y);
            y+=16;UiFactory.Label(body,"简介",290,y,1026,46,28);y+=52;
            string overview=string.IsNullOrWhiteSpace(item.Overview)?"暂无人物简介":PlainOverview(item.Overview);if(overview.Length>10000)overview=overview.Substring(0,10000)+"…";
            var description=UiFactory.Label(body,overview,290,y,1026,100,24);description.textWrappingMode=TextWrappingModes.Normal;description.alignment=TextAlignmentOptions.TopLeft;
            float height=Mathf.Max(100,description.GetPreferredValues(overview,1026,float.PositiveInfinity).y+12);description.rectTransform.sizeDelta=new Vector2(1026,height);
            y=Mathf.Max(410,y+height+24);var items=app.EmbyState.items??Array.Empty<EmbyItem>();y=SectionTitle($"出演作品 · {items.Length}",y+16);
            if(items.Length==0){UiFactory.Label(body,"暂无可显示的出演作品",18,y,1300,58,26).color=Muted;y+=78;}
            else y=BuildPosterWall(items,y);
            FinishScroll(y+20);
        }
        private float PersonLine(string text,float y)
        {
            var label=UiFactory.Label(body,text,290,y,1026,40,24);label.color=Muted;label.textWrappingMode=TextWrappingModes.Normal;label.alignment=TextAlignmentOptions.TopLeft;
            float height=Mathf.Max(40,label.GetPreferredValues(text,1026,float.PositiveInfinity).y+8);label.rectTransform.sizeDelta=new Vector2(1026,height);return y+height+8;
        }
        private static string DateText(string value)=>DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces,out var date)?date.ToString("yyyy年M月d日",CultureInfo.InvariantCulture):value;
        private float DetailVideos(string title,EmbyItem[] items,float y,bool parts)
        {
            var videos=(items??Array.Empty<EmbyItem>()).Where(item=>item!=null&&!string.IsNullOrEmpty(item.Id)).ToArray();
            if(videos.Length==0)return y;
            var last=parts?app.EmbyMovieResume:null;
            y=SectionTitle(title,y+16);
            for(int i=0;i<videos.Length;i++){
                var video=videos[i];float x=14+(i%3)*444,py=y+(i/3)*354;
                var card=Button(body,"",x,py,428,334,()=>{closeKeyboard?.Invoke();if(parts)app.PlayEmbyPart(video);else app.PlayEmbyExtra(video);});UiFactory.Clear(card.transform);
                string backdropTag=video.BackdropImageTags?.FirstOrDefault();
                string imageType=!string.IsNullOrEmpty(video.ImageTags?.Thumb)?"Thumb":!string.IsNullOrEmpty(backdropTag)?"Backdrop":"Primary";
                string imageTag=imageType=="Thumb"?video.ImageTags.Thumb:imageType=="Backdrop"?backdropTag:video.ImageTags?.Primary;
                Image(card.transform,video.Id,0,0,428,241,!string.IsNullOrEmpty(imageTag),video,imageTag,imageType);
                string name=(parts?"CD"+(i+1)+" · ":"")+(string.IsNullOrWhiteSpace(video.Name)?parts?"影片部分":"附加篇":video.Name);
                UiFactory.Label(card.transform,name,12,245,404,42,25);
                long ticks=video.RunTimeTicks>0?video.RunTimeTicks:(video.MediaSources?.FirstOrDefault()?.RunTimeTicks??0);
                string duration=ticks>0?TicksClock(ticks):"时长未知";
                var progress=parts?app.EmbyPartProgress(video):null;
                string resume=progress?.PositionTicks>0?" · 进度 "+TicksClock(progress.PositionTicks):"";
                var subtitle=UiFactory.Label(card.transform,(last?.Item.Id==video.Id?last.HasLastPlayed?"上次播放 · ":"已存进度 · ":parts&&i==0?"主片 · ":"")+duration+resume,12,289,404,40,22);
                subtitle.color=last?.Item.Id==video.Id?Green:Muted;
            }
            return y+Mathf.CeilToInt(videos.Length/3f)*354;
        }
        private void BeginScroll(float y,float height)
        {
            var container=UiFactory.Rect(root,"Emby scrolling content",14,y,1348,height);
            scroll=container.gameObject.AddComponent<ListScrollRect>();scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=48;scroll.decelerationRate=.12f;
            var viewport=UiFactory.Box(container,"Viewport",0,0,1344,height,Color.clear,true);viewport.gameObject.AddComponent<RectMask2D>();scroll.viewport=viewport;
            body=UiFactory.Rect(viewport,"Cards",0,0,1344,height);scroll.content=body;
            var barRect=UiFactory.Box(container,"Scrollbar",1348-16,0,16,height,Card,true);var bar=barRect.gameObject.AddComponent<Scrollbar>();
            var handle=UiFactory.Box(barRect,"Handle",0,0,16,height,Green,true);handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;
            bar.handleRect=handle;bar.targetGraphic=handle.GetComponent<Image>();bar.direction=Scrollbar.Direction.BottomToTop;scroll.verticalScrollbar=bar;
        }
        private void FinishScroll(float height){body.sizeDelta=new Vector2(1344,Mathf.Max(scroll.viewport.rect.height,height));}
        private static Button Button(Transform parent,string text,float x,float y,float width,float height,Action clicked,bool selected=false)
        {
            var button=UiFactory.Button(parent,text,x,y,width,height,clicked);Paint(button,selected);return button;
        }
        private static void Paint(Button button,bool selected)
        {
            button.targetGraphic.color=selected?new Color(.22f,.46f,.20f,1):Card;var colors=button.colors;colors.pressedColor=Green;button.colors=colors;
        }
        private void Image(Transform parent,string itemId,float x,float y,float width,float height,bool available,EmbyItem item=null,string imageTag="",string imageType="Primary")
        {
            var holder=UiFactory.Box(parent,"Artwork",x,y,width,height,new Color(.09f,.1f,.095f,1));
            var placeholder=UiFactory.Label(holder,available?"Emby":item?.IsBrowsable==true?"媒体库":width>height?"暂无缩略图":"暂无海报",8,0,width-16,height,22,TextAlignmentOptions.Center);placeholder.color=Muted;
            if(!available||string.IsNullOrEmpty(itemId)||app.ActiveEmbyAccount==null)return;
            var rect=UiFactory.Rect(holder,"Image",0,0,width,height);rect.pivot=new Vector2(.5f,.5f);var image=rect.gameObject.AddComponent<RawImage>();image.raycastTarget=false;
            var fit=rect.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=width/height;
            RegisterImage(image,holder,itemId,height,available,item,imageTag,imageType);
        }
        private ImageBinding RegisterImage(RawImage image,RectTransform holder,string itemId,float height,bool available,EmbyItem item,string imageTag,string imageType)
        {
            Detach(image);if(!available||string.IsNullOrEmpty(itemId)||app.ActiveEmbyAccount==null)return null;
            var account=app.ActiveEmbyAccount;string tag=string.IsNullOrEmpty(imageTag)?item?.ImageTags?.Primary??"":imageTag,key=account.id+":"+itemId+":"+imageType+":"+tag;
            if(!imageJobs.TryGetValue(key,out var job)){
                string url=app.EmbyApi.ImageUrl(account,itemId,imageType,imageType=="Primary"?256:428);
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
                    binding.active=binding.target!=null&&binding.bottom>=imageOffset-ImagePreload&&binding.top<=bottom+ImagePreload;
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
            if(image==null)return;image.texture=null;image.color=Color.clear;
        }
        private static void Assign(RawImage image,Texture2D texture)
        {
            if(image==null||texture==null)return;image.texture=texture;image.color=Color.white;var fit=image.GetComponent<AspectRatioFitter>();if(fit!=null)fit.aspectRatio=texture.width/(float)texture.height;
        }
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
                                byte[] bytes=download.Payload();
                                if(ImageDimensions(bytes,out int width,out int height)&&width<=1024&&height<=1200&&(long)width*height<=786432){
                                    var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                                    bool loaded=texture.LoadImage(bytes,true);int cost=loaded?texture.width*texture.height*4:0;
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
            CancelImages();ClearPosterWall();pageOffsets.Clear();foreach(var cached in images.Values)if(cached.texture!=null)Destroy(cached.texture);images.Clear();textureBytes=0;password="";
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
        private static string SafeServer(string serverUrl)=>Uri.TryCreate(serverUrl,UriKind.Absolute,out var uri)?uri.GetLeftPart(UriPartial.Authority):"Emby 服务器";
        private static string DisplayName(EmbyItem item)=>item.Type=="Episode"&&!string.IsNullOrEmpty(item.SeriesName)?item.SeriesName+" · "+(item.IndexNumber>0?"第 "+item.IndexNumber+" 集 · ":"")+item.Name:item.Name??"未命名影片";
        private static string ItemMeta(EmbyItem item)=>item.IsBrowsable?item.Type=="Season"?"季 / 剧集":item.Type=="Series"?"剧集":"媒体库":item.ProductionYear>0?item.ProductionYear.ToString():item.RunTimeTicks>0?TicksClock(item.RunTimeTicks):"视频";
        private static string DetailsMeta(EmbyItem item)
        {
            var parts=new List<string>();if(item.ProductionYear>0)parts.Add(item.ProductionYear.ToString());if(item.RunTimeTicks>0)parts.Add(((long)TimeSpan.FromTicks(item.RunTimeTicks).TotalMinutes)+" 分钟");if(!string.IsNullOrEmpty(item.OfficialRating))parts.Add(item.OfficialRating);if(item.CommunityRating>0)parts.Add("评分 "+item.CommunityRating.ToString("0.0"));return string.Join("   ·   ",parts);
        }
        private static float Progress(EmbyItem item)=>item.RunTimeTicks>0?Mathf.Clamp01((item.UserData?.PlaybackPositionTicks??0)/(float)item.RunTimeTicks):Mathf.Clamp01((float)(item.UserData?.PlayedPercentage??0)/100);
        private static string TicksClock(long ticks){var value=TimeSpan.FromTicks(Math.Max(0,ticks));return value.TotalHours>=1?$"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}":$"{value.Minutes:00}:{value.Seconds:00}";}
        private static string PlainOverview(string text)=>System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(text,"<[^>]*>",""));
    }
}
