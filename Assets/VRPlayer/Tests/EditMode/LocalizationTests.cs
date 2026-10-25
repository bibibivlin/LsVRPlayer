using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace VRPlayer.Tests
{
    public sealed class LocalizationTests
    {
        private AppLanguage previous;
        [SetUp] public void Setup(){previous=Localization.Preference;Localization.SetLanguage(AppLanguage.Chinese);}
        [TearDown] public void Cleanup()=>Localization.SetLanguage(previous);

        [TestCase(SystemLanguage.Chinese,AppLanguage.Chinese)]
        [TestCase(SystemLanguage.ChineseSimplified,AppLanguage.Chinese)]
        [TestCase(SystemLanguage.ChineseTraditional,AppLanguage.Chinese)]
        [TestCase(SystemLanguage.English,AppLanguage.English)]
        [TestCase(SystemLanguage.Japanese,AppLanguage.Japanese)]
        [TestCase(SystemLanguage.French,AppLanguage.English)]
        [TestCase(SystemLanguage.Unknown,AppLanguage.English)]
        public void SystemLanguageResolvesToSupportedLanguage(SystemLanguage system,AppLanguage expected)=>Assert.That(Localization.Resolve(AppLanguage.System,system),Is.EqualTo(expected));

        [TestCase(AppLanguage.Chinese)][TestCase(AppLanguage.English)][TestCase(AppLanguage.Japanese)]
        public void ExplicitLanguageOverridesSystem(AppLanguage language)=>Assert.That(Localization.Resolve(language,SystemLanguage.French),Is.EqualTo(language));

        [TestCase(1)][TestCase(2)][TestCase(3)]
        public void ExistingSettingsDefaultToSystemWithoutChangingMigration(int version)
        {
            var settings=PlayerStore.ParseSettings("{\"version\":"+version+",\"favorites\":[{\"name\":\"Keep\",\"uri\":\"smb://stable/Films\"}],\"history\":[{\"uri\":\"file:///movie.mp4\",\"subtitle\":\"file:///movie.srt\",\"position\":42000}]}");
            Assert.That(settings.language,Is.EqualTo(AppLanguage.System));Assert.That(settings.version,Is.EqualTo(3));
            Assert.That(settings.favorites.Single().uri,Is.EqualTo("smb://stable/Films"));Assert.That(settings.history.Single().position,Is.EqualTo(42000));
            if(version<3)Assert.That(settings.history.Single().subtitleSelection,Is.EqualTo(SubtitleSelection.Manual));
        }
        [TestCase(-1)][TestCase(999)]
        public void InvalidSavedLanguageFallsBackToSystem(int language)=>Assert.That(PlayerStore.ParseSettings("{\"version\":3,\"language\":"+language+"}").language,Is.EqualTo(AppLanguage.System));

        [TestCase(AppLanguage.System)][TestCase(AppLanguage.Chinese)][TestCase(AppLanguage.English)][TestCase(AppLanguage.Japanese)]
        public void LanguageChoiceSurvivesRestart(AppLanguage language)
        {
            string path=Path.Combine(Path.GetTempPath(),"VRPlayer-localization-"+Guid.NewGuid()+".json");
            try{
                var store=new PlayerStore(path);store.Settings.language=language;store.Settings.history.Add(new PlaybackRecord{uri="file:///keep.mp4",subtitleSelection=SubtitleSelection.Off,subtitleBeforeOff="file:///keep.srt",formatLocked=true,view=new ViewSettings{projection=Projection.Equirect180,panoramaZoom=1.4f}});store.Save();
                var restored=new PlayerStore(path).Settings;Assert.That(restored.language,Is.EqualTo(language));
                var film=restored.history.Single();Assert.That(film.subtitleSelection,Is.EqualTo(SubtitleSelection.Off));Assert.That(film.subtitleBeforeOff,Is.EqualTo("file:///keep.srt"));Assert.That(film.formatLocked);Assert.That(film.view.projection,Is.EqualTo(Projection.Equirect180));Assert.That(film.view.panoramaZoom,Is.EqualTo(1.4f));
            }finally{if(File.Exists(path))File.Delete(path);}
        }
        [Test] public void SystemPreferenceTracksChangesWithoutBecomingAnExplicitChoice()
        {
            Localization.SetLanguage(AppLanguage.System,SystemLanguage.Japanese);Assert.That(Localization.T("设置"),Is.EqualTo("設定"));
            Localization.SetLanguage(AppLanguage.System,SystemLanguage.English);Assert.That(Localization.T("设置"),Is.EqualTo("Settings"));Assert.That(Localization.Preference,Is.EqualTo(AppLanguage.System));
        }
        [TestCase(AppLanguage.English,"Opening: 字幕.mp4")][TestCase(AppLanguage.Japanese,"開いています：字幕.mp4")]
        public void StatusTranslationPreservesDynamicMediaNames(AppLanguage language,string expected)
        {
            Localization.SetLanguage(language);Assert.That(Localization.Message("正在打开：字幕.mp4"),Is.EqualTo(expected));
            Assert.That(Localization.Message("Unrecognized backend detail"),Is.EqualTo("Unrecognized backend detail"));
        }
        [Test] public void NestedNativeErrorsAreLocalizedWithoutChangingDiagnostics()
        {
            string diagnostic="播放失败：SMB 打开失败：文件不存在或无法读取。可点重试或选择其他文件。";
            Localization.SetLanguage(AppLanguage.English);
            Assert.That(Localization.Message(diagnostic),Is.EqualTo("Playback failed: Could not open SMB: File does not exist or cannot be read. Retry or choose another file."));
            Assert.That(diagnostic,Does.StartWith("播放失败："));
            Assert.That(Localization.Message("字幕已加载：42 条，跳过 2 个损坏块"),Is.EqualTo("Subtitles loaded: 42 cues, 2 invalid blocks skipped"));
        }
        [TestCase(AppLanguage.Chinese,"中文 / English")][TestCase(AppLanguage.English,"English / 中文")][TestCase(AppLanguage.Japanese,"English / 中文")]
        public void LegalCopyRemainsBilingualInPreferredOrder(AppLanguage language,string expected)
        {
            Localization.SetLanguage(language);Assert.That(Localization.Bilingual("中文","English"),Is.EqualTo(expected));
        }
        [Test] public void EveryCatalogTranslationHasMatchingPlaceholders()
        {
            var catalog=JsonUtility.FromJson<Localization.Catalog>(Resources.Load<TextAsset>("Localization/Strings").text);
            var keys=new HashSet<string>();
            string[] Fields(string text)=>Regex.Matches(text,@"\{\d+(?:[^{}]*?)\}").Cast<Match>().Select(match=>match.Value).OrderBy(value=>value).ToArray();
            foreach(var entry in catalog.entries){
                Assert.That(keys.Add(entry.zh),Is.True,"Duplicate key: "+entry.zh);
                Assert.That(entry.en,Is.Not.Null.And.Not.Empty,entry.zh);Assert.That(entry.ja,Is.Not.Null.And.Not.Empty,entry.zh);
                Assert.That(Fields(entry.en),Is.EqualTo(Fields(entry.zh)),entry.zh);Assert.That(Fields(entry.ja),Is.EqualTo(Fields(entry.zh)),entry.zh);
                Assert.DoesNotThrow(()=>string.Format(entry.en,1.25,2,3),entry.zh);Assert.DoesNotThrow(()=>string.Format(entry.ja,1.25,2,3),entry.zh);
            }
        }
        [Test] public void ShippedFontSupportsEnglishAndJapaneseCatalog()
        {
            var catalog=JsonUtility.FromJson<Localization.Catalog>(Resources.Load<TextAsset>("Localization/Strings").text);
            var font=TMP_FontAsset.CreateFontAsset(Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular"),36,4,GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,true);
            try{
                string alphabet=new string(("日本語 简体中文 English"+string.Concat(catalog.entries.Select(entry=>entry.en+entry.ja))).Where(c=>!char.IsControl(c)).Distinct().ToArray());
                font.TryAddCharacters(alphabet,out string missing);
                Assert.That(missing,Is.Empty,"Missing localized glyphs: "+missing);
            }finally{foreach(var texture in font.atlasTextures)UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(font.material);UnityEngine.Object.DestroyImmediate(font);}
        }
    }

    public sealed class LocalizedPlayerUITests
    {
        private GameObject root,head;private PlayerApp app;private PlayerUI ui;private string path;private AppLanguage previous;
        private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private void Set(string name,object value)=>typeof(PlayerApp).GetProperty(name).SetValue(app,value);
        private TextMeshProUGUI[] Labels=>ui.Canvas.GetComponentsInChildren<TextMeshProUGUI>();
        [SetUp] public void Setup()
        {
            previous=Localization.Preference;path=Path.Combine(Path.GetTempPath(),"VRPlayer-language-ui-"+Guid.NewGuid()+".json");
            root=new GameObject("Localized UI fixture");root.SetActive(false);app=root.AddComponent<PlayerApp>();
            var store=new PlayerStore(path);store.Settings.language=AppLanguage.Chinese;Set("Store",store);Set("Backend",new AndroidMediaBackend());Set("Presenter",root.AddComponent<VideoPresenter>());Set("RenderQuality",root.AddComponent<RenderQualityController>());
            Set("Generation",1);app.Backend.Snapshot.generation=1;
            Set("Current",store.Touch(new FileEntry{name="字幕.mp4",uri="file:///fixture.mp4"},""));
            head=new GameObject("Test head",typeof(Camera));ui=root.AddComponent<PlayerUI>();Set("UI",ui);ui.Initialize(app,head.GetComponent<Camera>());
        }
        [TearDown] public void Cleanup()
        {
            var quick=ui.Quick.Canvas;typeof(QuickControlsUI).GetProperty("Canvas").SetValue(ui.Quick,null);UnityEngine.Object.DestroyImmediate(quick.gameObject);
            var canvas=ui.Canvas;typeof(PlayerUI).GetProperty("Canvas").SetValue(ui,null);UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            UnityEngine.Object.DestroyImmediate(Field<RectTransform>(ui,"transportRoot").gameObject);Set("Presenter",null);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(head);
            if(File.Exists(path))File.Delete(path);Localization.SetLanguage(previous);
        }
        [TestCase(AppLanguage.English,"Settings")][TestCase(AppLanguage.Japanese,"設定")]
        public void SettingsLanguageChoiceUpdatesNavigationAndPersists(AppLanguage language,string tab)
        {
            ui.SelectTab("设置");var languageButton=Labels.Single(label=>label.text=="跟随系统"||label.text=="简体中文").GetComponentInParent<Button>();languageButton.onClick.Invoke();
            ui.Canvas.GetComponentsInChildren<Button>().Single(button=>button.name=="Language "+language).onClick.Invoke();
            Assert.That(ui.SelectedTab,Is.EqualTo("设置"));Assert.That(Labels.Any(label=>label.text==tab));Assert.That(new PlayerStore(path).Settings.language,Is.EqualTo(language));
            Assert.That(ui.Visible);Assert.That(ui.Quick.Visible,Is.False);Assert.That(app.Current.name,Is.EqualTo("字幕.mp4"));
        }
        [TestCase(AppLanguage.Chinese)][TestCase(AppLanguage.English)][TestCase(AppLanguage.Japanese)]
        public void BackgroundSelectionPersistsAndBackReturnsToSettings(AppLanguage language)
        {
            app.SetLanguage(language);ui.SelectTab("设置");
            ui.Canvas.GetComponentsInChildren<Button>().Single(button=>button.name=="Background settings").onClick.Invoke();
            foreach(CinemaBackgroundMode mode in Enum.GetValues(typeof(CinemaBackgroundMode)))
            {
                ui.Canvas.GetComponentsInChildren<Button>().Single(button=>button.name=="Background "+mode).onClick.Invoke();
                Assert.That(new PlayerStore(path).Settings.background.mode,Is.EqualTo(mode));
                Assert.That(app.Presenter.BackgroundSettings,Is.SameAs(app.Store.Settings.background));
            }
            var gray=ui.Canvas.GetComponentsInChildren<Slider>().Single(slider=>slider.name=="Background gray");gray.value=24;
            Assert.That(ui.Back(),Is.True);Assert.That(ui.SelectedTab,Is.EqualTo("设置"));Assert.That(ui.Visible,Is.True);
            Assert.That(new PlayerStore(path).Settings.background.grayLevel,Is.EqualTo(24));Assert.That(app.Current.name,Is.EqualTo("字幕.mp4"));
        }
        [TestCase(AppLanguage.Chinese)][TestCase(AppLanguage.English)][TestCase(AppLanguage.Japanese)]
        public void ComfortControlsPersistAndComparisonsPreserveFilmSettings(AppLanguage language)
        {
            app.SetLanguage(language);ui.SelectTab("设置");
            ui.Canvas.GetComponentsInChildren<Button>().Single(button=>button.name=="Cinema comfort settings").onClick.Invoke();
            app.Current.view.brightness=.9f;app.Current.view.distance=4;app.Current.subtitleSelection=SubtitleSelection.Off;app.Current.formatLocked=true;
            foreach(HighlightCompression mode in Enum.GetValues(typeof(HighlightCompression)))
            {
                ui.Canvas.GetComponentsInChildren<Button>().Single(button=>button.name=="Cinema highlights "+mode).onClick.Invoke();
                Assert.That(new PlayerStore(path).Settings.cinemaComfort.highlights,Is.EqualTo(mode));
            }
            for(int preset=0;preset<4;preset++)
            {
                ui.Canvas.GetComponentsInChildren<Button>().Single(button=>button.name=="Cinema comparison "+preset).onClick.Invoke();
                var saved=new PlayerStore(path).Settings;
                Assert.That(saved.background.mode,Is.EqualTo(CinemaBackgroundMode.Black));
                Assert.That(saved.cinemaComfort.highlights,Is.EqualTo(preset>=2?HighlightCompression.Medium:HighlightCompression.Off));
                Assert.That(saved.cinemaComfort.subtitleBrightness,Is.EqualTo(preset==1||preset==3?.7f:1));
                Assert.That(saved.history[0].view.brightness,Is.EqualTo(.9f));Assert.That(saved.history[0].view.distance,Is.EqualTo(4));
                Assert.That(saved.history[0].subtitleSelection,Is.EqualTo(SubtitleSelection.Off));Assert.That(saved.history[0].formatLocked,Is.True);
            }
            ui.Canvas.GetComponentsInChildren<Slider>().Single(slider=>slider.name=="Cinema subtitle brightness").value=.6f;
            Assert.That(ui.Back(),Is.True);Assert.That(ui.SelectedTab,Is.EqualTo("设置"));
            Assert.That(new PlayerStore(path).Settings.cinemaComfort.subtitleBrightness,Is.EqualTo(.6f).Within(.001f));
        }
        [Test] public void SwitchingLanguageKeepsDirectoryScrollAndActualNames()
        {
            Set("Source",FileSource.Directory);Set("DirectoryUri","smb://account/Films");
            Set("Files",Enumerable.Range(0,40).Select(i=>new FileEntry{name=i==0?"字幕":"動画 "+i,uri="smb://account/Films/"+i,directory=true}).ToList());ui.SelectTab("文件");
            Field<VirtualFileList>(ui,"fileList").SetOffset(600);app.SetLanguage(AppLanguage.English);
            Assert.That(Field<VirtualFileList>(ui,"fileList").Offset,Is.EqualTo(600).Within(.1));Assert.That(app.DirectoryUri,Is.EqualTo("smb://account/Films"));
            Field<VirtualFileList>(ui,"fileList").SetOffset(float.MaxValue);Assert.That(Labels.Any(label=>label.text=="字幕"));
        }
        [Test] public void QuickPanelRetainsPopupAndUsesNewLanguage()
        {
            ui.Hide();ui.ShowQuick();ui.Quick.TogglePopup("mode");app.SetLanguage(AppLanguage.Japanese);
            Assert.That(ui.Visible,Is.False);Assert.That(ui.Quick.Visible);Assert.That(ui.Quick.Popup,Is.EqualTo("mode"));
            Assert.That(ui.Quick.Canvas.GetComponentsInChildren<TextMeshProUGUI>().Any(label=>label.text=="表示モード"));
        }
        [TestCase("文件")][TestCase("投影")][TestCase("字幕")][TestCase("设置")][TestCase("信息")]
        public void DevelopmentLabelInspectionReadsActivePageWithoutInputValues(string tab)
        {
            ui.SelectTab(tab);
            var diagnostics=root.AddComponent<PlayerDiagnostics>();
            typeof(PlayerDiagnostics).GetField("app",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(diagnostics,app);
            var inspect=typeof(PlayerDiagnostics).GetMethod("InspectLabels",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.DoesNotThrow(()=>inspect.Invoke(diagnostics,null));
        }
        [TestCase(AppLanguage.Chinese)][TestCase(AppLanguage.English)][TestCase(AppLanguage.Japanese)]
        public void QuickSubtitleEmptyHintFitsVisibleRow(AppLanguage language)
        {
            app.SetLanguage(language);ui.Hide();ui.ShowQuick();ui.Quick.TogglePopup("subtitles");
            var label=ui.Quick.Canvas.GetComponentsInChildren<TextMeshProUGUI>().Single(item=>item.text==Localization.T("未发现字幕，请选择 SRT / ASS 文件"));
            label.ForceMeshUpdate();
            Assert.That(label.preferredWidth,Is.LessThanOrEqualTo(label.rectTransform.rect.width));
            Assert.That(label.isTextTruncated,Is.False);
        }
        [TestCase(AppLanguage.Chinese,"L's VRPlayer 隐私政策")][TestCase(AppLanguage.English,"L's VRPlayer Privacy")][TestCase(AppLanguage.Japanese,"L's VRPlayer Privacy")]
        public void LegalPagesPreferRequestedVersionAndKeepLicenseBodiesUnchanged(AppLanguage language,string first)
        {
            app.SetLanguage(language);ui.ShowLegalDocument("privacy");
            Assert.That(Field<RectTransform>(ui,"infoBody").GetComponentsInChildren<TextMeshProUGUI>().First().text,Does.StartWith(first));
            var buttons=Field<RectTransform>(ui,"content").GetComponentsInChildren<Button>().Where(button=>button.name=="English"||button.name=="简体中文").OrderBy(button=>((RectTransform)button.transform).anchoredPosition.x).ToArray();
            Assert.That(buttons.First().name,Is.EqualTo(language==AppLanguage.Chinese?"简体中文":"English"));
            buttons.Last().onClick.Invoke();Assert.That(Field<RectTransform>(ui,"infoBody").GetComponentsInChildren<TextMeshProUGUI>().First().text,Does.Not.StartWith(first));
            ui.ShowLegalDocument("notices");
            string[] original=Resources.Load<TextAsset>("Legal/notices").text.Replace("\r","").Split(new[]{"\n\n"},StringSplitOptions.RemoveEmptyEntries);
            string[] rendered=Field<RectTransform>(ui,"infoBody").GetComponentsInChildren<TextMeshProUGUI>().Select(label=>label.text).ToArray();
            Assert.That(rendered[0],Is.EqualTo("L's VRPlayer — "+Localization.Bilingual("第三方组件与许可","Third-party notices")));
            string[] pair=original[1].Split('\n');Assert.That(rendered[1],Is.EqualTo(Localization.Bilingual(pair[0],pair[1],"\n")));
            int originalStart=Array.IndexOf(original,"运行时组件 / Runtime components")+1,originalEnd=Array.IndexOf(original,"开发者与项目 / Developer and project");
            int renderedStart=Array.IndexOf(rendered,Localization.Bilingual("运行时组件","Runtime components"))+1,renderedEnd=Array.IndexOf(rendered,Localization.Bilingual("开发者与项目","Developer and project"));
            Assert.That(originalStart,Is.GreaterThan(0));Assert.That(renderedStart,Is.GreaterThan(0));
            Assert.That(rendered.Skip(renderedStart).Take(renderedEnd-renderedStart),Is.EqualTo(original.Skip(originalStart).Take(originalEnd-originalStart)));
        }
    }
}
