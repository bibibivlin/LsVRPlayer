using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using L = VRPlayer.Localization;

namespace VRPlayer
{
    public sealed partial class PlayerUI
    {
        private void DrawBackgroundSelection()
        {
            BeginModal("background");
            var settings = app.Store.Settings.background;
            UiFactory.IconButton(content,"返回",QuickIcon.Back,0,0,ReturnFromModal);
            UiFactory.Label(content,L.T("观影背景"),80,0,900,64,32);
            UiFactory.Button(content,L.T("收起面板预览"),1072,0,280,56,Hide);
            string[] descriptions = { "完全黑暗，保留原有观影习惯。", "均匀、静止的低亮度中性灰。", "仅在屏幕外围柔和过渡，远处保持黑暗。", "微弱的墙面与地面层次，提供稳定空间参照。", "固定哑光墙面，无屏幕亮圈、条纹或倒影。" };
            for (int i = 0; i < descriptions.Length; i++)
            {
                var mode = (CinemaBackgroundMode)i;
                bool selected = settings.mode == mode;
                var button = UiFactory.Button(content,"",i%2*688,88+i/2*140,664,124,()=>{app.SetBackground(mode);DrawBackgroundSelection();},selected);
                button.name = "Background " + mode;
                UiFactory.Clear(button.transform);
                var thumbnail = UiFactory.Rect(button.transform,"Background preview",20,14,148,96).gameObject.AddComponent<BackgroundPreview>();
                thumbnail.Initialize(mode);
                UiFactory.RoundedBox(thumbnail.transform,"Preview screen",45,30,74,44,new Color(.32f,.34f,.35f),false,2);
                UiFactory.Label(button.transform,L.T(CinemaBackgroundSettings.Name(mode)),188,10,416,42,27).color = selected ? UiTheme.Ink : UiTheme.Text;
                var description = UiFactory.Label(button.transform,L.T(descriptions[i]),188,54,450,62,21);
                description.textWrappingMode = TextWrappingModes.Normal;
                description.color = selected ? UiTheme.Ink : UiTheme.Muted;
                UiFactory.Icon(button.transform,QuickIcon.Check,622,18,22,selected ? UiTheme.Ink : Color.clear);
            }
            UiFactory.Button(content,L.T("观影舒适"),688,368,664,124,DrawCinemaComfort).name="Cinema comfort settings";
            var controls = UiFactory.RoundedBox(content,"Background controls",0,512,1352,192,UiTheme.Surface,false,22,SurfaceTreatment.Inset);
            var gray = UiFactory.ValueSlider(controls,L.T("背景亮度"),24,24,1304,0,48,settings.grayLevel,app.SetBackgroundGray,v=>Mathf.RoundToInt(v).ToString()+" / 255",26,240,136);
            gray.name = "Background gray"; gray.wholeNumbers = true;
            var feather = UiFactory.ValueSlider(controls,L.T("柔光范围"),24,88,1304,5,25,settings.featherDegrees,app.SetBackgroundFeather,v=>v.ToString("0")+"°",26,240,136);
            feather.name = "Background feather"; feather.wholeNumbers = true;
            gray.interactable = settings.mode != CinemaBackgroundMode.Black;
            feather.interactable = settings.mode == CinemaBackgroundMode.SoftSurround || settings.mode == CinemaBackgroundMode.QuietTheater;
            foreach (var slider in new[] { gray, feather }) slider.gameObject.AddComponent<BackgroundSettingCommit>().Initialize(slider,app.Store.Save);
            UiFactory.Label(controls,L.T("从低亮度开始调节；视频亮度可在设置页单独调整。"),24,152,1304,32,22).color = UiTheme.Muted;
            string hint = app.Presenter.Settings.projection == Projection.Flat ? "用于平面观影；全景视频自动停用。背景可能减轻光晕的显眼程度，无法消除镜片反射。" : "当前为全景投影，背景暂不显示；返回平面后自动恢复所选背景。";
            var note = UiFactory.Label(content,L.T(hint),12,716,1328,78,23);
            note.textWrappingMode = TextWrappingModes.Normal; note.color = UiTheme.Muted;
        }

        private void DrawCinemaComfort()
        {
            BeginModal("cinema-comfort");
            var settings = app.Store.Settings.cinemaComfort;
            UiFactory.IconButton(content,"返回",QuickIcon.Back,0,0,ReturnFromModal);
            UiFactory.Label(content,L.T("观影舒适"),80,0,900,64,32);
            UiFactory.Button(content,L.T("收起面板预览"),1072,0,280,56,Hide);
            var film = UiFactory.RoundedBox(content,"Highlight controls",0,88,1352,210,UiTheme.Surface,false,22,SurfaceTreatment.Inset);
            UiFactory.Label(film,L.T("高亮柔和压缩"),24,16,1304,44,28);
            string[] levels = { "关闭", "轻度", "中度", "较强" };
            for (int i = 0; i < levels.Length; i++)
            {
                var mode = (HighlightCompression)i;
                UiFactory.Button(film,L.T(levels[i]),24+i*332,76,308,56,()=>{app.SetCinemaHighlights(mode);DrawCinemaComfort();},settings.highlights==mode).name="Cinema highlights "+mode;
            }
            var filmNote = UiFactory.Label(film,L.T("主要压低亮部，保留暗部；会改变高光表现。"),24,146,1304,56,23);
            filmNote.textWrappingMode = TextWrappingModes.Normal; filmNote.color = UiTheme.Muted;
            var subtitles = UiFactory.RoundedBox(content,"Cinema subtitle controls",0,322,1352,156,UiTheme.Surface,false,22,SurfaceTreatment.Inset);
            var gain = UiFactory.ValueSlider(subtitles,L.T("字幕亮度"),24,20,1304,.3f,1,settings.subtitleBrightness,app.SetCinemaSubtitleBrightness,v=>$"{v*100:0}%",26,260,100);
            gain.name="Cinema subtitle brightness";gain.gameObject.AddComponent<BackgroundSettingCommit>().Initialize(gain,app.Store.Save);
            var subtitleNote = UiFactory.Label(subtitles,L.T("独立调节外部与内嵌文字字幕；烧录字幕随影片处理。"),24,88,1304,60,23);
            subtitleNote.textWrappingMode=TextWrappingModes.Normal;subtitleNote.color=UiTheme.Muted;
            UiFactory.Label(content,L.T("纯黑背景对照"),12,502,1328,44,28);
            string[] presets = { "原片", "仅字幕", "仅高光", "组合" };
            for (int i = 0; i < presets.Length; i++)
            {
                int preset=i;
                UiFactory.Button(content,L.T(presets[i]),i*344,562,320,56,()=>{app.SetCinemaComparison(preset);DrawCinemaComfort();}).name="Cinema comparison "+preset;
            }
            var compareNote = UiFactory.Label(content,L.T("对照使用纯黑背景；原片恢复关闭压缩与原字幕亮度。"),12,632,1328,60,23);
            compareNote.textWrappingMode=TextWrappingModes.Normal;compareNote.color=UiTheme.Muted;
            UiFactory.Button(content,L.T("观影背景"),0,724,300,56,DrawBackgroundSelection).name="Background settings";
            string hint=app.Presenter.Settings.projection==Projection.Flat?"用于平面 SDR 观影；设置跨影片保留，可随时恢复原片。":"当前为全景投影，舒适调节暂不生效。";
            var note=UiFactory.Label(content,L.T(hint),324,718,1028,76,23);
            note.textWrappingMode=TextWrappingModes.Normal;note.color=UiTheme.Muted;
        }
    }

    public sealed class BackgroundPreview : RawImage
    {
        private Material ownedMaterial;
        public void Initialize(CinemaBackgroundMode mode)
        {
            raycastTarget = false;
            texture = Texture2D.whiteTexture;
            ownedMaterial = new Material(Shader.Find("VRPlayer/Cinema Background"));
            ownedMaterial.renderQueue = 3000;
            ownedMaterial.SetFloat("_Preview",1);
            ownedMaterial.SetFloat("_Mode",(int)mode);
            ownedMaterial.SetFloat("_GrayLevel",48/255f);
            material = ownedMaterial;
        }
        protected override void OnDestroy()
        {
            material = null;
            if (ownedMaterial != null) { if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial); }
            base.OnDestroy();
        }
    }

    // Live preview while dragging; write once on release or when leaving the page.
    [ExecuteAlways]
    public sealed class BackgroundSettingCommit : MonoBehaviour, IPointerUpHandler, IEndDragHandler
    {
        private Action save;
        private Slider slider;
        private bool dirty;
        public void Initialize(Slider source, Action commit) { slider = source; save = commit; slider.onValueChanged.AddListener(Changed); }
        private void Changed(float value) => dirty = true;
        private void Commit() { if (!dirty) return; dirty = false; save?.Invoke(); }
        public void OnPointerUp(PointerEventData data) => Commit();
        public void OnEndDrag(PointerEventData data) => Commit();
        private void OnDisable() => Commit();
        private void OnDestroy() { if (slider != null) slider.onValueChanged.RemoveListener(Changed); }
    }
}
