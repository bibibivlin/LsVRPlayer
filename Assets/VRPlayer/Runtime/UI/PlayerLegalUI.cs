using System;
using System.Threading.Tasks;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRPlayer
{
    public sealed partial class PlayerUI
    {
        private bool privacyGate,legalEnglish;
        private Action acceptPrivacy;
        private TaskCompletionSource<bool> permissionNotice;
        private string legalDocument="privacy";
        private const string DeveloperName="bibibivlin",DeveloperUrl="https://github.com/bibibivlin",ProjectUrl="https://github.com/bibibivlin/LsVRPlayer";
        private const string DeveloperLink="[bibibivlin](https://github.com/bibibivlin)";
        private readonly CancellationTokenSource legalOperations=new CancellationTokenSource();
        public bool PrivacyNoticeVisible=>privacyGate;

        public void ShowPrivacyConsent(Action accept)
        {
            privacyGate=true;acceptPrivacy=accept;Show();DrawPrivacyConsent();
        }
        private void DrawPrivacyConsent()
        {
            BeginModal("privacy-consent");
            UiFactory.Label(content,"隐私与使用说明 / Privacy",0,0,UiTheme.PageWidth,60,34);
            var notice=UiFactory.Label(content,
                "本地影片和观看设置保存在设备上。连接 SMB / Emby 时，必要数据直接发送给你选择的服务器。\n"+
                "应用会按需说明并申请媒体读取权限；不提供广告或开发者统计上传。\n\n"+
                "请阅读隐私政策和权限说明。同意后才初始化播放与服务器功能；不同意可退出。\n\n"+
                "Local media/settings stay here; network data goes to your chosen SMB/Emby server.\n"+
                "Read the policy and permissions. Agree to start playback/server features, or decline and exit.",
                24,90,1304,410,26,TextAlignmentOptions.TopLeft,false);
            notice.textWrappingMode=TextWrappingModes.Normal;notice.overflowMode=TextOverflowModes.Overflow;
            UiFactory.Button(content,"隐私政策 / Policy",24,532,416,60,()=>ShowLegalDocument("privacy"));
            UiFactory.Button(content,"权限 / Permissions",468,532,416,60,()=>ShowLegalDocument("permissions"));
            UiFactory.Button(content,"开源许可 / Licenses",912,532,416,60,()=>ShowLegalDocument("notices"));
            UiFactory.Button(content,"不同意并退出 / Decline",24,686,616,72,Application.Quit);
            UiFactory.Button(content,"同意并继续 / Agree",688,686,640,72,()=>{
                try{acceptPrivacy?.Invoke();privacyGate=false;acceptPrivacy=null;Draw();}
                catch(Exception){app.SetStatus("无法保存隐私确认，请检查应用存储后重试 / Could not save your choice");}
            },true);
            var footer=UiFactory.Rect(content,"Developer links",0,766,UiTheme.PageWidth,32);
            var developer=LegalLink(footer,DeveloperName,DeveloperUrl,0,0,20);
            float left=developer.rectTransform.sizeDelta.x;
            UiFactory.Label(footer,"·",left,0,28,32,20,TextAlignmentOptions.Center,false).color=UiTheme.Muted;
            var project=LegalLink(footer,ProjectUrl,ProjectUrl,left+28,0,20);
            float width=left+28+project.rectTransform.sizeDelta.x;
            footer.sizeDelta=new Vector2(width,32);footer.anchoredPosition=new Vector2((UiTheme.PageWidth-width)/2,-766);
        }
        public void ShowLegalDocument(string document)
        {
            legalDocument=document;BeginModal("legal");
            string title=document=="privacy"?"隐私政策 / Privacy":document=="permissions"?"权限说明 / Permissions":"第三方许可 / Licenses";
            UiFactory.Label(content,title,0,0,820,60,32);
            if(document=="notices")UiFactory.Button(content,"导出源码 / Sources",876,0,360,60,ExportLicenseSource);
            else UiFactory.Button(content,legalEnglish?"简体中文":"English",876,0,280,60,()=>{legalEnglish=!legalEnglish;ShowLegalDocument(legalDocument);});
            UiFactory.IconButton(content,"返回",QuickIcon.Back,1288,0,()=>{if(privacyGate)DrawPrivacyConsent();else ReturnFromModal();});
            string resource=document=="notices"?"notices":document+"-"+(legalEnglish?"en":"zh-CN");
            var asset=Resources.Load<TextAsset>("Legal/"+resource);
            string text=asset!=null?asset.text:"说明文件不可用 / Document unavailable.\n\n"+DeveloperLink+"\n\n"+ProjectUrl+"\n\n隐私联系 / Privacy: linyoc@outlook.com";
            var host=UiFactory.Rect(content,"Legal scroll",0,86,UiTheme.PageWidth,708);
            infoScroll=host.gameObject.AddComponent<ListScrollRect>();infoScroll.horizontal=false;infoScroll.movementType=ScrollRect.MovementType.Clamped;
            var viewport=UiFactory.Box(host,"Viewport",0,0,1320,708,Color.clear,true);viewport.gameObject.AddComponent<RectMask2D>();infoScroll.viewport=viewport;
            infoBody=UiFactory.Rect(viewport,"Legal document",0,0,1320,708);infoScroll.content=infoBody;
            float y=16;
            foreach(string paragraph in text.Replace("\r","").Split(new[]{"\n\n"},StringSplitOptions.RemoveEmptyEntries)){
                string profile=paragraph.Trim();
                if(profile==DeveloperLink||profile=="开发者："+DeveloperLink||profile=="Developer: "+DeveloperLink){
                    string prefix=profile.Substring(0,profile.Length-DeveloperLink.Length);float x=16;
                    if(prefix.Length>0){var heading=UiFactory.Label(infoBody,prefix,x,y,1288,48,25,TextAlignmentOptions.TopLeft,false);x+=heading.GetPreferredValues(prefix).x;}
                    LegalLink(infoBody,DeveloperName,DeveloperUrl,x,y,25);y+=66;continue;
                }
                var label=UiFactory.Label(infoBody,paragraph,16,y,1288,48,25,TextAlignmentOptions.TopLeft,false);
                label.textWrappingMode=TextWrappingModes.Normal;label.overflowMode=TextOverflowModes.Overflow;
                float height=Mathf.Max(48,label.GetPreferredValues(paragraph,1288,0).y+12);
                label.rectTransform.sizeDelta=new Vector2(1288,height);y+=height+18;
            }
            infoBody.sizeDelta=new Vector2(1320,Mathf.Max(708,y+16));
            var rail=UiFactory.Box(host,"Scrollbar",1336,0,10,708,UiTheme.Surface,true);var bar=rail.gameObject.AddComponent<Scrollbar>();
            var handle=UiFactory.Box(rail,"Handle",0,0,10,708,UiTheme.Muted,true);handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;
            bar.handleRect=handle;bar.targetGraphic=handle.GetComponent<Image>();bar.direction=Scrollbar.Direction.BottomToTop;infoScroll.verticalScrollbar=bar;
        }
        private static TextMeshProUGUI LegalLink(Transform parent,string text,string url,float x,float y,float size)
        {
            var label=UiFactory.Label(parent,text,x,y,1288,size*1.6f,size,TextAlignmentOptions.TopLeft,false);
            label.rectTransform.sizeDelta=new Vector2(label.GetPreferredValues(text).x+4,size*1.6f);
            label.text="<u>"+text+"</u>";label.richText=true;label.color=UiTheme.Accent;label.raycastTarget=true;
            var button=label.gameObject.AddComponent<Button>();button.targetGraphic=label;button.colors=UiTheme.ButtonColors();
            button.onClick.AddListener(()=>Application.OpenURL(url));return label;
        }
        public Task<bool> ExplainStoragePermission(string permission)
        {
            Show();BeginModal("storage-permission");permissionNotice=new TaskCompletionSource<bool>();var result=permissionNotice.Task;
            UiFactory.Label(content,"本地媒体访问 / Local media access",0,0,1280,64,32);
            string message="为读取你选择的本地影片，需要媒体读取权限。应用不会修改或上传本地文件。\n\n"+
                "拒绝后仍可使用 SMB、Emby 和离线说明；你可以稍后在系统设置中修改授权。\n\n"+
                "Read access is needed for selected local media. Files are not modified or uploaded.\n"+
                "You may refuse and continue using network sources and offline legal pages.\n\n"+permission;
            var label=UiFactory.Label(content,message,24,112,1304,400,28,TextAlignmentOptions.TopLeft,false);label.textWrappingMode=TextWrappingModes.Normal;label.overflowMode=TextOverflowModes.Overflow;
            UiFactory.Button(content,"暂不允许 / Not now",24,642,616,72,()=>CompletePermissionNotice(false));
            UiFactory.Button(content,"继续系统授权 / Continue",688,642,640,72,()=>CompletePermissionNotice(true),true);
            return result;
        }
        private void CompletePermissionNotice(bool allow)
        {
            var pending=permissionNotice;permissionNotice=null;ReturnFromModal();pending?.TrySetResult(allow);
        }
        private void CancelPermissionNotice(){var pending=permissionNotice;permissionNotice=null;pending?.TrySetResult(false);}
        private async void ExportLicenseSource()
        {
            app.SetStatus("正在导出源码 / Exporting source…");
            try{var result=await FileLibrary.ExportLicenseSource(legalOperations.Token);if(app!=null)app.SetStatus(result.ok?result.text:result.error);}
            catch(OperationCanceledException){}
        }
    }
}
