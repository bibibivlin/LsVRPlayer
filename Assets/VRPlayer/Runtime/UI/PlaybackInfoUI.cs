using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

using L = VRPlayer.Localization;

namespace VRPlayer
{
    // One scrollable page. Every diagnostic remains directly visible in its own group.
    public sealed class PlaybackInfoUI
    {
        private readonly Dictionary<string,TextMeshProUGUI> values=new Dictionary<string,TextMeshProUGUI>();
        private readonly TextMeshProUGUI title,error;
        public const float Height=1474;
        public PlaybackInfoUI(RectTransform root)
        {
            title=UiFactory.Label(root,"",24,0,1272,52,30);
            var video=Card(root,L.T("视频"),0,70,1320,134);
            Summary(video,L.T("分辨率"),"resolution",190,228);Summary(video,L.T("编码格式"),"codec",438,216);
            Summary(video,L.T("帧率"),"fps",674,192);Summary(video,L.T("视觉格式"),"format",886,410);
            var playback=Card(root,L.T("播放状态"),0,228,1320,134);
            Summary(playback,L.T("当前状态"),"playing",190,280);Summary(playback,L.T("剩余缓冲"),"ahead",494,240);Summary(playback,L.T("播放速度"),"speed",758,230);
            var quality=Card(root,L.T("画质"),0,386,1320,104);values["quality"]=UiFactory.Label(quality,"—",190,28,1106,48,27);
            UiFactory.Label(root,L.T("诊断信息"),0,514,1320,48,30);
            Diagnostic(Card(root,L.T("解码与渲染"),0,586,648,352),new[]{(L.T("视频内核"),"decoder"),(L.T("音频内核"),"audio"),(L.T("码率"),"bitrate"),(L.T("解码丢帧"),"dropped"),(L.T("GPU 更新帧"),"frames")});
            Diagnostic(Card(root,L.T("缓冲"),672,586,648,352),new[]{(L.T("已缓冲到"),"buffered"),(L.T("正在加载"),"loading"),(L.T("缓冲内存"),"memory"),(L.T("意外缓冲次数"),"rebufferCount"),(L.T("意外缓冲时长"),"rebufferTime")});
            Diagnostic(Card(root,L.T("网络 · SMB"),0,962,648,448),new[]{(L.T("读取状态"),"networkState"),(L.T("网络吞吐"),"networkSpeed"),(L.T("请求数"),"requests"),(L.T("平均请求"),"requestSize"),(L.T("平均读取"),"readAverage"),(L.T("P95 读取"),"readP95"),(L.T("网络错误"),"networkErrors")});
            var app=Card(root,L.T("应用"),672,962,648,448);
            Diagnostic(app,new[]{(L.T("后端状态"),"state"),(L.T("版本"),"version"),(L.T("图形 API"),"api"),(L.T("眼缓冲 / 眼"),"eyes"),(L.T("倍率 / MSAA"),"scale"),(L.T("应用帧率"),"appFps")});
            error=UiFactory.Label(app,"",24,384,600,40,22);error.textWrappingMode=TextWrappingModes.Normal;
            UiFactory.Label(root,L.T("诊断来自当前播放会话；格式支持以设备解码器与样片实测为准。"),0,1434,1320,40,23).color=UiTheme.Muted;
        }
        private static RectTransform Card(RectTransform root,string name,float x,float y,float width,float height)
        {
            var card=UiFactory.RoundedBox(root,name,x,y,width,height,UiTheme.Surface,false,18,SurfaceTreatment.Inset);
            UiFactory.Label(card,name,24,24,width-48,48,27).color=UiTheme.Accent;return card;
        }
        private void Summary(RectTransform parent,string caption,string key,float x,float width,float y=24)
        {
            UiFactory.Label(parent,caption,x,y,width,38,23).color=UiTheme.Muted;
            values[key]=UiFactory.Label(parent,"—",x,y+38,width,48,27);
        }
        private void Diagnostic(RectTransform card,(string label,string key)[] rows)
        {
            for(int i=0;i<rows.Length;i++){
                UiFactory.Label(card,rows[i].label,24,88+i*48,226,42,23).color=UiTheme.Muted;
                values[rows[i].key]=UiFactory.Label(card,"—",254,88+i*48,card.rect.width-278,42,25,TextAlignmentOptions.Right);
            }
        }
        private void Value(string key,string text){if(values[key].text!=text)values[key].text=text;}
        private static string Known(string text)=>string.IsNullOrEmpty(text)?"—":text;
        private static string Clock(long value){var t=TimeSpan.FromMilliseconds(Math.Max(0,value));return t.TotalHours>=1?$"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}":$"{t.Minutes:00}:{t.Seconds:00}";}
        public void Update(PlayerApp app,MediaSnapshot snapshot)
        {
            title.text=app.Current?.name??L.T("尚未打开影片");
            Value("version",Application.version);Value("api",SystemInfo.graphicsDeviceType.ToString());Value("eyes",XRSettings.eyeTextureWidth>0?$"{XRSettings.eyeTextureWidth} × {XRSettings.eyeTextureHeight}":"—");Value("scale",$"{app.RenderQuality.EffectiveScale:0.##}× / {QualitySettings.antiAliasing}×");Value("appFps",L.F("{0:0.0} fps · 目标 72 Hz", app.RenderQuality.Fps));
            if(snapshot==null||app.Current==null||snapshot.generation!=app.Generation){
                foreach(var key in new[]{"resolution","codec","fps","ahead","decoder","audio","bitrate","dropped","frames","buffered","loading","memory","rebufferCount","rebufferTime","networkSpeed","requests","requestSize","readAverage","readP95","networkErrors","state"})Value(key,"—");
                Value("playing",app.Current==null?L.T("未打开影片"):L.T("正在打开影片"));Value("networkState",L.T("暂无会话样本"));error.text=L.T("错误：无");return;
            }
            Value("resolution",snapshot.width>0&&snapshot.height>0?$"{snapshot.width} × {snapshot.height}":"—");Value("codec",Known(snapshot.codec));Value("fps",snapshot.fps>0?$"{snapshot.fps:0.##} fps":"—");
            var view=app.Presenter.Settings;string[] projections={L.T("平面 / 曲面"),"VR180","VR360",L.T("鱼眼"),"Cubemap 3×2","EAC 3×2"};string[] stereo={"2D",L.T("左右 SBS"),L.T("上下 OU")};
            Value("format",stereo[(int)view.stereo]+" · "+projections[(int)view.projection]);
            Value("playing",app.Current==null?L.T("未打开影片"):snapshot.HasError?L.T("播放异常"):snapshot.loading?L.T("正在缓冲"):snapshot.playing?L.T("正在播放"):L.T("已暂停"));Value("ahead",L.F("{0:0.0} 秒", snapshot.bufferAheadMs/1000d));Value("speed",$"{app.Store.Settings.speed:0.##}×");
            string[] qualities={L.T("均衡"),L.T("高清"),L.T("超清")};Value("quality",qualities[(int)app.Store.Settings.renderQuality]+$" · {RenderQualityController.Scale(app.Store.Settings.renderQuality):0.##}×");
            Value("decoder",Known(snapshot.decoder));Value("audio",Known(snapshot.audioDecoder));Value("bitrate",snapshot.bitrate>0?$"{snapshot.bitrate/1000000d:0.##} Mbps":L.T("未知"));Value("dropped",snapshot.dropped.ToString());Value("frames",app.Backend.RenderedFrames.ToString());
            Value("buffered",Clock(snapshot.buffered));Value("loading",snapshot.loading?L.T("是"):L.T("否"));Value("memory",$"{snapshot.allocatedBufferBytes/1048576d:0.0} MiB");Value("rebufferCount",L.F("{0} 次", snapshot.rebufferCount));Value("rebufferTime",L.F("{0:0.00} 秒", snapshot.rebufferMs/1000d));
            var reads=snapshot.network;bool valid=reads!=null&&reads.generation==snapshot.generation&&reads.requests>0;
            Value("networkState",valid?reads.readAhead?L.T("预读"):L.T("直接读取"):L.T("暂无会话样本"));Value("networkSpeed",valid?$"{reads.mbps:0.00} Mbps":"—");Value("requests",valid?reads.requests.ToString():"—");Value("requestSize",valid?$"{reads.averageRequestBytes/1024:0.0} KiB":"—");Value("readAverage",valid?$"{reads.averageReadMs:0.0} ms":"—");Value("readP95",valid?$"{reads.readP95Ms:0.0} ms":"—");Value("networkErrors",valid?reads.errors.ToString():"—");
            Value("state",snapshot.state.ToString());Value("version",Application.version);Value("api",SystemInfo.graphicsDeviceType.ToString());Value("eyes",XRSettings.eyeTextureWidth>0?$"{XRSettings.eyeTextureWidth} × {XRSettings.eyeTextureHeight}":"—");Value("scale",$"{app.RenderQuality.EffectiveScale:0.##}× / {QualitySettings.antiAliasing}×");Value("appFps",L.F("{0:0.0} fps · 目标 72 Hz", app.RenderQuality.Fps));error.text=string.IsNullOrEmpty(snapshot.error)?L.T("错误：无"):L.T("错误：")+L.Message(snapshot.error);
        }
    }
}
