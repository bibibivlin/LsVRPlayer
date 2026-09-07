using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRPlayer
{
    public sealed class PlayerUI : MonoBehaviour
    {
        public Canvas Canvas{get;private set;}
        public bool Visible=>Canvas!=null&&Canvas.gameObject.activeSelf;
        public float LastInteraction{get;set;}
        private PlayerApp app;private Camera head;
        private RectTransform content,keyboard;
        private TextMeshProUGUI title,status,time,playLabel,detail,keyboardPreview;
        private Slider seek;private SeekGesture seekGesture;
        private TMP_InputField address,filter,keyboardTarget;
        private string tab="文件",query="";
        private int page,sort;
        private bool subtitleBrowse,shift;
        private const int PageSize=7;
        public void Initialize(PlayerApp owner,Camera camera)
        {
            app=owner;head=camera;
            var root=new GameObject("Player UI",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));Canvas=root.GetComponent<Canvas>();Canvas.renderMode=RenderMode.WorldSpace;Canvas.worldCamera=camera;Canvas.sortingOrder=10;
            var rect=(RectTransform)root.transform;rect.sizeDelta=new Vector2(1440,1040);rect.localScale=Vector3.one*.00145f;
            UiFactory.Box(rect,"Background",0,0,1440,1040,UiFactory.Background,true);
            UiFactory.Label(rect,"L’s VRPlayer",32,20,490,65,42);UiFactory.Label(rect,"PICO 4  /  私人影院",1000,25,400,50,24,TextAlignmentOptions.Right).color=UiFactory.Muted;
            title=UiFactory.Label(rect,"选择影片开始观看",32,85,1376,45,26);
            string[] tabs={"文件","投影","字幕","设置","信息"};for(int i=0;i<tabs.Length;i++){string name=tabs[i];UiFactory.Button(rect,name,32+i*158,145,142,50,()=>{tab=name;Draw();},i==0);}
            UiFactory.Button(rect,"正前方",1060,145,160,50,()=>app.Recenter(false));UiFactory.Button(rect,"隐藏面板",1236,145,172,50,Hide);
            content=UiFactory.Rect(rect,"Page",32,212,1376,610);
            status=UiFactory.Label(rect,"",32,827,1376,40,23);status.color=UiFactory.Muted;
            seek=UiFactory.Slider(rect,32,885,1050,0,1,0,_=>{});seekGesture=seek.gameObject.AddComponent<SeekGesture>();seekGesture.Commit=value=>app.Seek((long)(value*(app.Backend?.Snapshot.duration??0)));
            time=UiFactory.Label(rect,"00:00 / 00:00",1102,881,310,45,23,TextAlignmentOptions.Right);
            UiFactory.Button(rect,"上一部",32,953,112,54,()=>app.Next(-1));
            var play=UiFactory.Button(rect,"播放",156,953,128,54,()=>app.TogglePlay(),true);playLabel=play.GetComponentInChildren<TextMeshProUGUI>();
            UiFactory.Button(rect,"下一部",296,953,112,54,()=>app.Next(1));UiFactory.Button(rect,"−10 秒",424,953,112,54,()=>app.Skip(-10));UiFactory.Button(rect,"+10 秒",548,953,112,54,()=>app.Skip(10));
            UiFactory.Label(rect,"音量",690,953,68,54,23);UiFactory.Slider(rect,765,960,225,0,1,app.Store.Settings.volume,app.SetVolume);
            UiFactory.Button(rect,"重试",1014,953,112,54,app.Retry);UiFactory.Button(rect,"停止",1138,953,112,54,app.Stop);UiFactory.Button(rect,"躺卧",1262,953,146,54,()=>app.Recenter(true));
            keyboard=UiFactory.Box(rect,"Virtual keyboard",30,340,1380,485,new Color(.025f,.065f,.09f,1),true);keyboard.gameObject.SetActive(false);
            app.Changed+=OnAppChanged;Draw();Show();
        }
        private void OnAppChanged(){if(tab=="文件"||tab=="字幕")Draw();}
        private void Update()
        {
            if(Canvas==null)return;var snapshot=app.Backend?.Snapshot;
            title.text=app.Current?.name??"选择影片开始观看";status.text=app.Status;
            if(snapshot!=null){playLabel.text=snapshot.playing?"暂停":"播放";if(!seekGesture.Dragging)seek.SetValueWithoutNotify(snapshot.duration>0?snapshot.position/(float)snapshot.duration:0);time.text=Clock(snapshot.position)+" / "+Clock(snapshot.duration);}
            if(detail!=null&&tab=="信息")detail.text=snapshot==null?"播放内核未就绪":$"播放内核：{snapshot.decoder}\n视频：{snapshot.codec}  {snapshot.width} × {snapshot.height}  {snapshot.fps:0.##} fps\n码率：{(snapshot.bitrate>0?snapshot.bitrate/1000000f:0):0.##} Mbps\n已缓冲到：{Clock(snapshot.buffered)}\n解码丢帧：{snapshot.dropped}    GPU 更新帧：{app.Backend.RenderedFrames}\n投影：{app.Presenter.Settings.projection}    立体：{app.Presenter.Settings.stereo}\n状态：{snapshot.state}    错误：{snapshot.error}\n版本：{Application.version}    图形 API：{SystemInfo.graphicsDeviceType}\n\n格式支持以当前设备解码器与样片实测为准。\nEAC / Cubemap 布局：上排 +X −X +Y，下排 −Y +Z −Z。";
            if(Visible&&app.Store.Settings.autoHide&&snapshot!=null&&snapshot.playing&&!keyboard.gameObject.activeSelf&&Time.unscaledTime-LastInteraction>18)Hide();
        }
        private static string Clock(long milliseconds){var duration=TimeSpan.FromMilliseconds(Math.Max(0,milliseconds));return duration.TotalHours>=1?$"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}":$"{duration.Minutes:00}:{duration.Seconds:00}";}
        public void Show(){if(Canvas==null)return;Canvas.gameObject.SetActive(true);LastInteraction=Time.unscaledTime;Canvas.transform.position=head.transform.position+head.transform.rotation*new Vector3(0,-.05f,2);Canvas.transform.rotation=head.transform.rotation;}
        public void Hide(){keyboardTarget=null;keyboard.gameObject.SetActive(false);Canvas.gameObject.SetActive(false);app.Store.Save();}
        public void Toggle(){if(Visible)Hide();else Show();}
        private void Draw()
        {
            if(content==null)return;UiFactory.Clear(content);detail=null;address=filter=null;
            if(tab=="文件")DrawFiles();else if(tab=="投影")DrawProjection();else if(tab=="字幕")DrawSubtitles();else if(tab=="设置")DrawSettings();else DrawInfo();
        }
        private void DrawFiles()
        {
            UiFactory.Button(content,"本地",0,0,116,44,()=>{page=0;subtitleBrowse=false;app.Home();});UiFactory.Button(content,"SMB",128,0,116,44,()=>{page=0;app.ShowServers();});
            UiFactory.Button(content,"收藏",256,0,116,44,()=>{page=0;app.ShowFavorites();});UiFactory.Button(content,"最近",384,0,116,44,()=>{page=0;app.ShowRecent();});
            UiFactory.Button(content,"存储权限",512,0,160,44,app.RequestStorage);UiFactory.Button(content,"授权目录",684,0,160,44,app.PickFolder);UiFactory.Button(content,"添加 SMB",856,0,176,44,DrawServerForm);
            UiFactory.Button(content,"收藏此目录",1044,0,200,44,()=>{if(!string.IsNullOrEmpty(app.DirectoryUri))app.AddFavorite(Uri.UnescapeDataString(app.DirectoryUri.Split('/').LastOrDefault(x=>x.Length>0)??"目录"),app.DirectoryUri);});
            UiFactory.Button(content,"取消",1256,0,120,44,()=>{app.CancelListing();app.SetStatus("已取消读取");});
            address=UiFactory.Input(content,app.DirectoryUri,"输入本地目录或选择一个来源",0,60,1034,48,ShowKeyboard);
            UiFactory.Button(content,"打开",1046,60,102,48,()=>{page=0;query="";_ = app.Browse(address.text);});UiFactory.Button(content,"上级",1160,60,102,48,()=>{page=0;app.Up();});UiFactory.Button(content,"刷新",1274,60,102,48,app.Refresh);
            filter=UiFactory.Input(content,query,"搜索当前目录",0,126,864,44,ShowKeyboard);filter.onValueChanged.AddListener(value=>query=value);
            UiFactory.Button(content,"搜索",876,126,100,44,()=>{query=filter.text;page=0;Draw();});UiFactory.Button(content,new[]{"名称排序","日期排序","大小排序"}[sort],988,126,184,44,()=>{sort=(sort+1)%3;page=0;Draw();});UiFactory.Button(content,subtitleBrowse?"字幕文件":"视频文件",1184,126,192,44,()=>{subtitleBrowse=!subtitleBrowse;page=0;Draw();});
            IEnumerable<FileEntry> items=app.Files.Where(e=>(e.directory||(subtitleBrowse?e.IsSubtitle:e.IsVideo))&&(string.IsNullOrEmpty(query)||e.name.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0));
            items=sort==1?items.OrderByDescending(e=>e.directory).ThenByDescending(e=>e.modified):sort==2?items.OrderByDescending(e=>e.directory).ThenByDescending(e=>e.size):items.OrderByDescending(e=>e.directory).ThenBy(e=>e.name,StringComparer.OrdinalIgnoreCase);
            var list=items.ToList();int pages=Math.Max(1,(list.Count+PageSize-1)/PageSize);page=Math.Clamp(page,0,pages-1);
            for(int i=0;i<Math.Min(PageSize,list.Count-page*PageSize);i++){
                FileEntry item=list[page*PageSize+i];float y=192+i*49;
                string prefix=item.directory?"目录  ":item.IsSubtitle?"SRT   ":"视频  ";
                var button=UiFactory.Button(content,prefix+item.name,0,y,1172,43,()=>{page=0;query="";if(item.directory)_ = app.Browse(item.uri);else if(item.IsSubtitle){_ = app.LoadSubtitle(item.uri);tab="字幕";Draw();}else app.OpenRecentOrFile(item);});
                button.GetComponentInChildren<TextMeshProUGUI>().alignment=TextAlignmentOptions.Left;
                if(item.directory&&string.IsNullOrEmpty(app.DirectoryUri)&&item.uri.StartsWith("smb://"))UiFactory.Button(content,"编辑",1190,y,184,43,()=>DrawServerForm(app.FindServer(item.uri)));
                else UiFactory.Label(content,item.directory?"打开":Size(item.size),1190,y,184,43,22,TextAlignmentOptions.Right);
            }
            if(list.Count==0)UiFactory.Label(content,app.Loading?"正在读取…":"此处没有匹配文件。可切换来源、授予目录权限或清空搜索。",12,210,1330,90,25);
            UiFactory.Button(content,"上一页",0,556,160,44,()=>{page--;Draw();});UiFactory.Label(content,$"第 {page+1} / {pages} 页   ·   {list.Count} 项",184,556,600,44,23);UiFactory.Button(content,"下一页",1216,556,160,44,()=>{page++;Draw();});
        }
        private static string Size(long bytes)=>bytes>=1024L*1024*1024?$"{bytes/(1024d*1024*1024):0.00} GB":bytes>=1024*1024?$"{bytes/(1024d*1024):0.0} MB":$"{bytes/1024d:0} KB";
        private void DrawProjection()
        {
            var view=app.Presenter.Settings;string[] names={"平面 / 曲面","VR 180°","VR 360°","鱼眼","Cubemap 3×2","EAC 3×2"};
            for(int i=0;i<names.Length;i++){int index=i;UiFactory.Button(content,names[i],i*230,0,216,52,()=>{view.projection=(Projection)index;Draw();},(int)view.projection==i);}
            string[] stereo={"普通 2D","左右 SBS","上下 OU"};for(int i=0;i<3;i++){int index=i;UiFactory.Button(content,stereo[i],i*230,72,216,52,()=>{view.stereo=(StereoLayout)index;Draw();},(int)view.stereo==i);}
            UiFactory.Button(content,view.half?"Half 半宽/半高":"Full 全宽/全高",690,72,216,52,()=>{view.half=!view.half;Draw();},view.half);
            UiFactory.Button(content,view.swapEyes?"已交换左右眼":"交换左右眼",920,72,216,52,()=>{view.swapEyes=!view.swapEyes;Draw();},view.swapEyes);
            UiFactory.Button(content,view.monoEye?"单眼 2D 已开":"单眼 2D",1150,72,216,52,()=>{view.monoEye=!view.monoEye;Draw();},view.monoEye);
            Setting("屏幕宽度",155,1,10,view.width,v=>view.width=v,"米");Setting("屏幕距离",223,1.2f,10,view.distance,v=>view.distance=v,"米");Setting("屏幕高度",291,-3,3,view.heightOffset,v=>view.heightOffset=v,"米");Setting("屏幕曲率",359,0,150,view.curvature,v=>view.curvature=v,"°");Setting("鱼眼角度",427,120,240,view.fisheyeFov,v=>view.fisheyeFov=v,"°");
            UiFactory.Button(content,"对齐正前方",0,518,255,52,()=>app.Recenter(false));UiFactory.Button(content,"对齐当前视线 / 躺卧",274,518,344,52,()=>app.Recenter(true));UiFactory.Button(content,"按文件名重新识别",638,518,310,52,()=>{if(app.Current!=null){app.Current.view=ViewSettings.Detect(app.Current.name);app.Presenter.Settings=app.Current.view;Draw();}});
        }
        private void Setting(string name,float y,float min,float max,float value,Action<float> change,string unit="")
        {
            UiFactory.Label(content,name,0,y,225,45,26);var number=UiFactory.Label(content,$"{value:0.##} {unit}",1180,y,196,45,24,TextAlignmentOptions.Right);
            UiFactory.Slider(content,240,y+2,910,min,max,value,v=>{change(v);number.text=$"{v:0.##} {unit}";LastInteraction=Time.unscaledTime;});
        }
        private void DrawSubtitles()
        {
            UiFactory.Button(content,"关闭字幕",0,0,175,48,app.DisableSubtitles);UiFactory.Button(content,"手动选择 SRT",190,0,260,48,()=>{tab="文件";subtitleBrowse=true;page=0;Draw();});
            string[] encodings={"Auto","UTF-8","GB18030","UTF-16LE"};int encoding=Array.IndexOf(encodings,app.SubtitleEncoding);UiFactory.Button(content,"编码："+app.SubtitleEncoding,468,0,310,48,()=>{app.SubtitleEncoding=encodings[(encoding+1)%encodings.Length];if(app.Current!=null&&!string.IsNullOrEmpty(app.Current.subtitle)&&!app.Current.subtitle.StartsWith("track:"))_ = app.LoadSubtitle(app.Current.subtitle);Draw();});
            UiFactory.Label(content,"同目录匹配字幕",0,70,900,40,28);
            var candidates=app.SubtitleCandidates;for(int i=0;i<Math.Min(candidates.Count,4);i++){var item=candidates[i];UiFactory.Button(content,item.name,0,118+i*48,1376,42,()=>_ = app.LoadSubtitle(item.uri),app.Current?.subtitle==item.uri);}
            if(candidates.Count==0)UiFactory.Label(content,"未发现匹配字幕，可手动选择。支持 chs、zh、zh-CN 等后缀。",0,118,1376,50,24);
            if(candidates.Count>4)UiFactory.Label(content,$"另有 {candidates.Count-4} 个候选，请在文件页切换“字幕文件”查看全部。",0,311,1376,35,22);
            float delay=app.Current?.subtitleDelay??0;Setting("字幕延后",350,-10,10,delay,v=>{if(app.Current!=null)app.Current.subtitleDelay=v;},"秒");
            Setting("字幕大小",415,24,68,app.Store.Settings.subtitleSize,v=>app.Store.Settings.subtitleSize=v);
            Setting("字幕高度",480,-2,1,app.Store.Settings.subtitleOffset,v=>app.Store.Settings.subtitleOffset=v,"米");
            UiFactory.Button(content,app.Store.Settings.subtitleFollow?"字幕跟随视线":"字幕固定方向",0,554,310,46,()=>{app.Store.Settings.subtitleFollow=!app.Store.Settings.subtitleFollow;Draw();});UiFactory.Button(content,"内嵌字幕与音轨…",330,554,340,46,DrawTracks);
        }
        private void DrawSettings()
        {
            Setting("播放速度",0,.25f,3,app.Store.Settings.speed,app.SetSpeed,"倍");Setting("视频亮度",70,.3f,1.8f,app.Presenter.Settings.brightness,v=>app.Presenter.Settings.brightness=v);
            Setting("字幕距离",140,1.5f,6,app.Store.Settings.subtitleDistance,v=>app.Store.Settings.subtitleDistance=v,"米");
            UiFactory.Button(content,app.Store.Settings.loop?"单片循环：开":"单片循环：关",0,225,310,52,()=>{app.Store.Settings.loop=!app.Store.Settings.loop;app.Backend?.SetRepeat(app.Store.Settings.loop);Draw();});
            UiFactory.Button(content,app.Store.Settings.sequential?"目录连播：开":"目录连播：关",330,225,310,52,()=>{app.Store.Settings.sequential=!app.Store.Settings.sequential;Draw();});
            UiFactory.Button(content,app.Store.Settings.autoHide?"自动隐藏：开":"自动隐藏：关",660,225,310,52,()=>{app.Store.Settings.autoHide=!app.Store.Settings.autoHide;Draw();});
            UiFactory.Button(content,"选择音轨 / 内嵌字幕",0,305,440,52,DrawTracks);UiFactory.Button(content,"恢复正常速度",465,305,340,52,()=>{app.SetSpeed(1);Draw();});
            UiFactory.Button(content,"清空播放记录",0,391,310,52,()=>{app.Store.Settings.history.Clear();app.Store.Save();app.SetStatus("播放记录已清空");});
            var instructions=UiFactory.Label(content,"手柄扳机选择，摇杆滚动；A / X 或菜单键呼出面板。\n键盘：空格播放暂停，← / → 快退快进，Tab 显示面板，R 居中。\n摘下头显或切换到后台时自动暂停。",0,478,1376,122,24);instructions.textWrappingMode=TextWrappingModes.Normal;
        }
        private void DrawTracks()
        {
            UiFactory.Clear(content);UiFactory.Label(content,"音轨与内嵌字幕",0,0,1000,50,32);UiFactory.Button(content,"返回",1190,0,186,48,Draw);
            UiFactory.Button(content,"自动音轨",0,68,285,48,()=>app.Backend?.SelectTrack("auto",1));UiFactory.Button(content,"关闭内嵌字幕",304,68,320,48,app.DisableSubtitles);
            var tracks=app.Backend?.Snapshot.tracks??Array.Empty<MediaTrack>();
            for(int i=0;i<Math.Min(tracks.Length,8);i++){var track=tracks[i];UiFactory.Button(content,(track.type==1?"音轨  ":"字幕  ")+track.name+(track.supported?"":"  [解码器不支持]"),0,138+i*54,1376,48,()=>{if(track.type==1){app.Backend?.SelectTrack(track.id,1);if(app.Current!=null)app.Current.audio=track.id;}else app.EmbeddedSubtitle(track.id);},track.selected);}
            if(tracks.Length==0)UiFactory.Label(content,"请先打开视频；轨道信息将在媒体准备完成后出现。",0,150,1376,60,25);
        }
        private void DrawInfo(){detail=UiFactory.Label(content,"",0,0,1376,590,25);detail.textWrappingMode=TextWrappingModes.Normal;detail.alignment=TextAlignmentOptions.TopLeft;}
        private void DrawServerForm()=>DrawServerForm(null);
        private void DrawServerForm(SmbServer existing)
        {
            UiFactory.Clear(content);UiFactory.Label(content,existing==null?"添加 SMB 服务器":"编辑 SMB 服务器",0,0,900,55,32);UiFactory.Button(content,"取消",1180,0,196,48,Draw);
            if(existing!=null)UiFactory.Button(content,"删除配置",920,0,240,48,()=>{app.RemoveServer(existing.id);Draw();});
            string[] labels={"显示名称","主机 / IP","共享名","用户名（空为匿名）",existing==null?"密码":"密码（空则保留）","域（可留空）"};var fields=new List<TMP_InputField>();var initial=existing??new SmbServer();string[] values={initial.name,initial.host,initial.share,initial.username,"",initial.domain};
            for(int i=0;i<labels.Length;i++){float y=75+i*67;UiFactory.Label(content,labels[i],0,y,345,50,25);fields.Add(UiFactory.Input(content,values[i],labels[i],370,y,1006,50,ShowKeyboard,i==4));}
            bool remember=initial.remember,encrypt=initial.encrypt;var rememberButton=UiFactory.Button(content,remember?"保存密码：开":"保存密码：关",0,493,296,48,null);rememberButton.onClick.AddListener(()=>{remember=!remember;rememberButton.GetComponentInChildren<TextMeshProUGUI>().text=remember?"保存密码：开":"保存密码：关";});
            var encryptionButton=UiFactory.Button(content,encrypt?"SMB 加密：开":"SMB 加密：关",314,493,296,48,null);encryptionButton.onClick.AddListener(()=>{encrypt=!encrypt;encryptionButton.GetComponentInChildren<TextMeshProUGUI>().text=encrypt?"SMB 加密：开":"SMB 加密：关";});
            var port=UiFactory.Input(content,initial.port.ToString(),"端口",632,493,186,48,ShowKeyboard);UiFactory.Label(content,"端口",834,493,150,48,24);
            UiFactory.Button(content,"保存并返回服务器列表",0,560,1376,48,()=>{if(!int.TryParse(port.text,out int parsed)||parsed<1||parsed>65535){app.SetStatus("请输入 1–65535 之间的端口");return;}
                var server=new SmbServer{id=initial.id,name=fields[0].text,host=fields[1].text,share=fields[2].text,username=fields[3].text,password=fields[4].text,domain=fields[5].text,port=parsed,remember=remember,encrypt=encrypt};if(app.SaveServer(server)){keyboard.gameObject.SetActive(false);Draw();}},true);
        }
        private void ShowKeyboard(TMP_InputField target){keyboardTarget=target;LastInteraction=Time.unscaledTime;keyboard.gameObject.SetActive(true);DrawKeyboard();}
        public void ScrollPage(float direction){if(tab=="文件"&&!keyboard.gameObject.activeSelf){page+=direction>0?-1:1;Draw();}}
        private void UpdateKeyboardPreview(){if(keyboardPreview!=null&&keyboardTarget!=null)keyboardPreview.text=keyboardTarget.contentType==TMP_InputField.ContentType.Password?new string('•',keyboardTarget.text.Length):keyboardTarget.text;}
        private void DrawKeyboard()
        {
            UiFactory.Clear(keyboard);keyboardPreview=UiFactory.Label(keyboard,"",20,12,1050,44,25);UpdateKeyboardPreview();UiFactory.Button(keyboard,"完成",1140,10,215,46,()=>{bool search=keyboardTarget==filter;keyboardTarget=null;keyboard.gameObject.SetActive(false);if(search){page=0;Draw();}});
            string[] rows={"1234567890-_=+","qwertyuiop[]\\","asdfghjkl;:'\"","zxcvbnm,./@:#"};
            for(int row=0;row<rows.Length;row++)for(int col=0;col<rows[row].Length;col++){char c=rows[row][col];string key=shift?c.ToString().ToUpperInvariant():c.ToString();UiFactory.Button(keyboard,key,20+col*100,80+row*72,91,60,()=>Insert(key));}
            UiFactory.Button(keyboard,shift?"小写":"大写",20,389,195,64,()=>{shift=!shift;DrawKeyboard();});UiFactory.Button(keyboard,"空格",235,389,580,64,()=>Insert(" "));UiFactory.Button(keyboard,"退格",835,389,245,64,()=>{if(keyboardTarget!=null&&keyboardTarget.text.Length>0)keyboardTarget.text=keyboardTarget.text.Substring(0,keyboardTarget.text.Length-1);UpdateKeyboardPreview();});UiFactory.Button(keyboard,"清空",1100,389,245,64,()=>{if(keyboardTarget!=null)keyboardTarget.text="";UpdateKeyboardPreview();});
        }
        private void Insert(string text){if(keyboardTarget==null)return;keyboardTarget.text+=text;keyboardTarget.caretPosition=keyboardTarget.text.Length;UpdateKeyboardPreview();}
        private void OnDestroy(){if(app!=null)app.Changed-=OnAppChanged;if(Canvas!=null)Destroy(Canvas.gameObject);}
    }
}
