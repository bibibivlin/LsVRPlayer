using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VRPlayer.Tests
{
    public sealed class PlayerExperienceTests
    {
        [TestCase(1.5f,84,-1.5f)]
        [TestCase(3,42,-.75f)]
        [TestCase(6,21,-.375f)]
        public void LegacySubtitlesKeepTheirApparentSize(float distance,float expectedSize,float expectedOffset)
        {
            var settings=PlayerStore.ParseSettings("{\"version\":1,\"subtitleDistance\":"+distance.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"subtitleSize\":42,\"subtitleOffset\":-0.75}");
            Assert.That(settings.version,Is.EqualTo(2));Assert.That(settings.subtitleSize,Is.EqualTo(expectedSize));Assert.That(settings.subtitleOffset,Is.EqualTo(expectedOffset));
            Assert.That(settings.renderQuality,Is.EqualTo(RenderQuality.High));
            var twice=PlayerStore.ParseSettings(JsonUtility.ToJson(settings));Assert.That(twice.subtitleSize,Is.EqualTo(settings.subtitleSize));
        }
        [TestCase(.1f)][TestCase(.3f)][TestCase(1f)][TestCase(3f)][TestCase(6f)]
        public void ChangingDistanceKeepsAngularScale(float distance)=>Assert.That(CaptionPresenter.DistanceScale(distance)/distance,Is.EqualTo(1f/3).Within(.00001f));
        [Test] public void SettingsRoundTripPreservesStyleHistoryAndFavorites()
        {
            var settings=new UserSettings{subtitleDistance=.1f,renderQuality=RenderQuality.Ultra,subtitleStyle=SubtitleStyle.Preset(3)};
            settings.history.Add(new PlaybackRecord{uri="smb://stable-id/movie.mp4",position=12345});settings.favorites.Add(new Favorite{name="电影",uri="smb://stable-id/"});
            var read=PlayerStore.ParseSettings(JsonUtility.ToJson(settings));Assert.That(read.subtitleDistance,Is.EqualTo(.1f));Assert.That(read.subtitleStyle.background,Is.EqualTo(SubtitleBackground.TextBox));Assert.That(read.history[0].position,Is.EqualTo(12345));Assert.That(read.favorites[0].uri,Is.EqualTo(settings.favorites[0].uri));
        }
        [Test] public void FavoriteRemovalAndUndoPreserveOrderAndPersist()
        {
            string path=Path.Combine(Path.GetTempPath(),"vrplayer-"+Guid.NewGuid()+".json");
            try{
                var store=new PlayerStore(path);foreach(string name in new[]{"a","b","c"})store.Settings.favorites.Add(new Favorite{name=name,uri="file:///"+name});
                var removed=store.RemoveFavorite("file:///b",out int index);Assert.That(index,Is.EqualTo(1));Assert.That(new PlayerStore(path).Settings.favorites.Count,Is.EqualTo(2));
                Assert.That(store.RemoveFavorite("file:///b",out _),Is.Null);Assert.That(store.RestoreFavorite(removed,index),Is.True);Assert.That(store.RestoreFavorite(removed,index),Is.False);
                Assert.That(new PlayerStore(path).Settings.favorites.Select(f=>f.name),Is.EqualTo(new[]{"a","b","c"}));
            }finally{if(File.Exists(path))File.Delete(path);}
        }
        [TestCase(0)][TestCase(1)][TestCase(1000)]
        public void DirectorySizeDoesNotIncreaseRowPool(int count)
        {
            var root=new GameObject("List test",typeof(RectTransform));
            try{var list=root.AddComponent<VirtualFileList>();list.Initialize(1376,352);list.SetRows(Enumerable.Range(0,count).Select(i=>new BrowserRow{key=i.ToString(),label="文件 "+i}).ToArray());Assert.That(list.PoolCount,Is.LessThanOrEqualTo(8));list.SetOffset(float.MaxValue);Assert.That(list.Offset,Is.EqualTo(Mathf.Max(0,count*64-352)));list.SetRows(Array.Empty<BrowserRow>());Assert.That(list.Offset,Is.Zero);}
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void RecycledRowDoesNotActivateANewItemFromAnOldPress()
        {
            var root=new GameObject("Binding test");try{bool activated=false;var row=root.AddComponent<RowBinding>();row.Item=new BrowserRow{key="a"};row.Press();row.Item=new BrowserRow{key="b",open=()=>activated=true};row.Open();Assert.That(activated,Is.False);row.Press();row.Open();Assert.That(activated,Is.True);}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void EmptyCaptionHidesItsBackgroundWithoutChangingMenuMaterial()
        {
            var root=new GameObject("Caption test",typeof(RectTransform),typeof(Canvas));
            try{
                var original=UiFactory.Font.material;var color=original.GetColor(TMPro.ShaderUtilities.ID_OutlineColor);
                var presenter=root.AddComponent<CaptionPresenter>();presenter.Initialize();
                presenter.SetText("两行字幕\nSubtitle",42,SubtitleStyle.Preset(3));Assert.That(presenter.BackgroundVisible,Is.True);
                presenter.SetText("",42,SubtitleStyle.Preset(3));Assert.That(presenter.BackgroundVisible,Is.False);
                Assert.That(UiFactory.Font.material,Is.SameAs(original));Assert.That(original.GetColor(TMPro.ShaderUtilities.ID_OutlineColor),Is.EqualTo(color));
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void SecondControllerCannotEndTheFirstControllersDrag()
        {
            var events=new GameObject("Events",typeof(EventSystem));var root=new GameObject("Scroll",typeof(RectTransform));
            try{
                var scroll=root.AddComponent<ListScrollRect>();scroll.content=root.GetComponent<RectTransform>();
                var first=new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-100};var second=new PointerEventData(events.GetComponent<EventSystem>()){pointerId=-101};
                scroll.OnBeginDrag(first);scroll.OnBeginDrag(second);scroll.OnEndDrag(second);Assert.That(scroll.HasDragOwner,Is.True);
                scroll.OnEndDrag(first);Assert.That(scroll.HasDragOwner,Is.False);
            }finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(events);}
        }
        [TestCase(.1f)][TestCase(6f)]
        public void CaptionUsesCurrentHeadPositionAndPreservesVisualHeight(float distance)
        {
            var root=new GameObject("Caption position",typeof(RectTransform));var head=new GameObject("Head",typeof(Camera));var screen=new GameObject("Screen");
            try{
                head.transform.SetPositionAndRotation(new Vector3(2,3,4),Quaternion.Euler(15,65,0));
                var settings=new UserSettings{subtitleDistance=distance,subtitleOffset=-.75f,subtitleFollow=true};
                var presenter=root.AddComponent<CaptionPresenter>();presenter.Position(head.GetComponent<Camera>(),screen.transform,settings);
                Vector3 local=head.transform.InverseTransformPoint(root.transform.position);
                Assert.That(local.z,Is.EqualTo(distance).Within(.00001f));Assert.That(local.y/distance,Is.EqualTo(-.25f).Within(.00001f));
            }finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(head);UnityEngine.Object.DestroyImmediate(screen);}
        }
    }
}
