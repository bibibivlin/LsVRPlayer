using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VRPlayer.Tests
{
    public sealed class EmbyLibraryViewsTests
    {
        [Test] public void ViewOrderMatchesTheSevenOfficialChoices()
        {
            Assert.That(Enum.GetValues(typeof(EmbyLibraryView)).Cast<EmbyLibraryView>().Select(EmbyLibraryViews.Label),
                Is.EqualTo(new[]{"海报","横幅图","光盘封面","徽标","缩略图","列表","表格"}));
        }

        [TestCase(1)][TestCase(2)][TestCase(3)] public void ExistingSettingsDefaultToPosterAndKeepHistory(int version)
        {
            var settings=PlayerStore.ParseSettings("{\"version\":"+version+",\"history\":[{\"uri\":\"file:///movie.mp4\",\"position\":12000}],\"favorites\":[{\"name\":\"Video\",\"uri\":\"file:///Video\"}]}");
            Assert.That(settings.embyLibraryViews,Is.Empty);Assert.That(settings.version,Is.EqualTo(3));
            Assert.That(settings.history[0].position,Is.EqualTo(12000));Assert.That(settings.favorites[0].uri,Is.EqualTo("file:///Video"));
        }

        [TestCase(EmbyLibraryView.Poster)][TestCase(EmbyLibraryView.Banner)][TestCase(EmbyLibraryView.Disc)]
        [TestCase(EmbyLibraryView.Logo)][TestCase(EmbyLibraryView.Thumb)][TestCase(EmbyLibraryView.List)][TestCase(EmbyLibraryView.Table)]
        public void ReloadRetainsIndependentAccountAndLibraryChoices(EmbyLibraryView view)
        {
            string path=Path.Combine(Path.GetTempPath(),"VRPlayer-view-"+Guid.NewGuid().ToString("N")+".json");
            try{
                var store=new PlayerStore(path);Assert.That(store.GetEmbyLibraryView("a","movies"),Is.EqualTo(EmbyLibraryView.Poster));
                store.SetEmbyLibraryView("a","movies",view);store.SetEmbyLibraryView("a","shows",EmbyLibraryView.Table);store.SetEmbyLibraryView("b","movies",EmbyLibraryView.List);
                store.SetEmbyLibraryView("a","movies",view);
                var loaded=new PlayerStore(path);
                Assert.That(loaded.GetEmbyLibraryView("a","movies"),Is.EqualTo(view));Assert.That(loaded.GetEmbyLibraryView("a","shows"),Is.EqualTo(EmbyLibraryView.Table));
                Assert.That(loaded.GetEmbyLibraryView("b","movies"),Is.EqualTo(EmbyLibraryView.List));Assert.That(loaded.GetEmbyLibraryView("new","movies"),Is.EqualTo(EmbyLibraryView.Poster));
                Assert.That(loaded.Settings.embyLibraryViews.Count,Is.EqualTo(3));Assert.That(File.ReadAllText(path),Does.Not.Contain("accessToken"));
            }finally{if(File.Exists(path))File.Delete(path);}
        }

        [Test] public void MissingAndInvalidViewPreferencesUsePoster()
        {
            var settings=PlayerStore.ParseSettings("{\"version\":3,\"embyLibraryViews\":[null,{\"accountId\":\"a\",\"libraryId\":\"movies\",\"view\":999},{\"accountId\":\"\",\"libraryId\":\"movies\"}]}");
            Assert.That(settings.embyLibraryViews.Count,Is.EqualTo(1));Assert.That(settings.embyLibraryViews[0].view,Is.EqualTo(EmbyLibraryView.Poster));
            Assert.That(EmbyLibraryViews.Normalize((EmbyLibraryView)(-1)),Is.EqualTo(EmbyLibraryView.Poster));
            Assert.That(PlayerStore.ParseSettings("{\"version\":3,\"embyLibraryViews\":null}").embyLibraryViews,Is.Empty);
        }

        [TestCase(1)][TestCase(2)][TestCase(3)]
        public void ExistingSettingsKeepTheCurrentLargestArtworkAndPanelDistance(int version)
        {
            var settings=PlayerStore.ParseSettings("{\"version\":"+version+",\"embyLibraryViews\":[{\"accountId\":\"a\",\"libraryId\":\"movies\",\"view\":4}]}");
            Assert.That(settings.mainPanelDistance,Is.EqualTo(2));Assert.That(settings.embyLibraryViews.Single().size,Is.EqualTo(EmbyArtworkSize.Large));
            Assert.That(settings.embyLibraryViews.Single().view,Is.EqualTo(EmbyLibraryView.Thumb));
        }
        [TestCase(-1)][TestCase(999)] public void InvalidArtworkSizeFallsBackToLarge(int size)
        {
            var settings=PlayerStore.ParseSettings("{\"version\":3,\"embyLibraryViews\":[{\"accountId\":\"a\",\"libraryId\":\"movies\",\"size\":"+size+"}]}");
            Assert.That(settings.embyLibraryViews.Single().size,Is.EqualTo(EmbyArtworkSize.Large));
        }
        [TestCase(0,2)][TestCase(-1,2)][TestCase(.1f,1.2f)][TestCase(99,6)][TestCase(float.NaN,2)][TestCase(float.PositiveInfinity,2)]
        public void PanelDistanceNormalizesInvalidValues(float value,float expected)=>Assert.That(PlayerStore.NormalizeMainPanelDistance(value),Is.EqualTo(expected).Within(.0001));
        [Test] public void ArtworkSizeAndViewSurviveRestartWithoutOverwritingEachOther()
        {
            string path=Path.Combine(Path.GetTempPath(),"VRPlayer-size-"+Guid.NewGuid().ToString("N")+".json");
            try{
                var store=new PlayerStore(path);store.SetEmbyLibraryView("a","movies",EmbyLibraryView.Disc);store.SetEmbyArtworkSize("a","movies",EmbyArtworkSize.Small);
                store.SetEmbyArtworkSize("a","shows",EmbyArtworkSize.Medium);store.SetEmbyLibraryView("a","shows",EmbyLibraryView.Thumb);
                store.SetEmbyLibraryView("b","movies",EmbyLibraryView.Banner);store.SetEmbyArtworkSize("b","movies",EmbyArtworkSize.Medium);
                store.SetEmbyLibraryView("a","movies",EmbyLibraryView.Poster);
                var loaded=new PlayerStore(path);
                Assert.That(loaded.GetEmbyArtworkSize("a","movies"),Is.EqualTo(EmbyArtworkSize.Small));Assert.That(loaded.GetEmbyLibraryView("a","movies"),Is.EqualTo(EmbyLibraryView.Poster));
                Assert.That(loaded.GetEmbyArtworkSize("a","shows"),Is.EqualTo(EmbyArtworkSize.Medium));Assert.That(loaded.GetEmbyLibraryView("a","shows"),Is.EqualTo(EmbyLibraryView.Thumb));
                Assert.That(loaded.GetEmbyArtworkSize("b","movies"),Is.EqualTo(EmbyArtworkSize.Medium));Assert.That(loaded.GetEmbyLibraryView("b","movies"),Is.EqualTo(EmbyLibraryView.Banner));
                Assert.That(loaded.GetEmbyArtworkSize("new","movies"),Is.EqualTo(EmbyArtworkSize.Large));Assert.That(loaded.Settings.embyLibraryViews.Count,Is.EqualTo(3));
            }finally{if(File.Exists(path))File.Delete(path);}
        }

        [TestCase(1)][TestCase(2)][TestCase(3)] public void ExistingLibraryPreferencesDefaultToAscendingTitleSort(int version)
        {
            var settings=PlayerStore.ParseSettings("{\"version\":"+version+",\"embyLibraryViews\":[{\"accountId\":\"a\",\"libraryId\":\"movies\",\"view\":4,\"size\":2}]}");
            var item=settings.embyLibraryViews.Single();
            Assert.That(item.sort,Is.EqualTo(EmbyLibrarySort.Title));Assert.That(item.sortDescending,Is.False);
            Assert.That(item.view,Is.EqualTo(EmbyLibraryView.Thumb));Assert.That(item.size,Is.EqualTo(EmbyArtworkSize.Small));
        }

        [TestCase(-1)][TestCase(999)] public void InvalidLibrarySortFallsBackToTitle(int sort)
        {
            var settings=PlayerStore.ParseSettings("{\"version\":3,\"embyLibraryViews\":[{\"accountId\":\"a\",\"libraryId\":\"movies\",\"sort\":"+sort+"}]}");
            Assert.That(settings.embyLibraryViews.Single().sort,Is.EqualTo(EmbyLibrarySort.Title));
        }

        [TestCase(EmbyLibrarySort.Title,false)][TestCase(EmbyLibrarySort.Title,true)]
        [TestCase(EmbyLibrarySort.DateAdded,false)][TestCase(EmbyLibrarySort.DateAdded,true)]
        [TestCase(EmbyLibrarySort.ReleaseDate,false)][TestCase(EmbyLibrarySort.ReleaseDate,true)]
        public void LibrarySortSurvivesRestartAndRemainsIndependentOfOtherPreferences(EmbyLibrarySort sort,bool descending)
        {
            string path=Path.Combine(Path.GetTempPath(),"VRPlayer-sort-"+Guid.NewGuid().ToString("N")+".json");
            try{
                var store=new PlayerStore(path);
                store.SetEmbyLibraryView("a","movies",EmbyLibraryView.Disc);store.SetEmbyArtworkSize("a","movies",EmbyArtworkSize.Small);
                store.SetEmbyLibrarySort("a","movies",sort,descending);
                store.SetEmbyLibrarySort("a","shows",EmbyLibrarySort.ReleaseDate,true);store.SetEmbyLibrarySort("b","movies",EmbyLibrarySort.DateAdded,false);
                store.SetEmbyLibraryView("a","movies",EmbyLibraryView.Thumb);store.SetEmbyArtworkSize("a","movies",EmbyArtworkSize.Medium);
                var loaded=new PlayerStore(path);
                Assert.That(loaded.GetEmbyLibrarySort("a","movies"),Is.EqualTo(sort));Assert.That(loaded.GetEmbyLibrarySortDescending("a","movies"),Is.EqualTo(descending));
                Assert.That(loaded.GetEmbyLibraryView("a","movies"),Is.EqualTo(EmbyLibraryView.Thumb));Assert.That(loaded.GetEmbyArtworkSize("a","movies"),Is.EqualTo(EmbyArtworkSize.Medium));
                Assert.That(loaded.GetEmbyLibrarySort("a","shows"),Is.EqualTo(EmbyLibrarySort.ReleaseDate));Assert.That(loaded.GetEmbyLibrarySortDescending("a","shows"),Is.True);
                Assert.That(loaded.GetEmbyLibrarySort("b","movies"),Is.EqualTo(EmbyLibrarySort.DateAdded));Assert.That(loaded.GetEmbyLibrarySortDescending("b","movies"),Is.False);
                Assert.That(loaded.GetEmbyLibrarySort("new","movies"),Is.EqualTo(EmbyLibrarySort.Title));Assert.That(loaded.GetEmbyLibrarySortDescending("new","movies"),Is.False);
                Assert.That(loaded.Settings.embyLibraryViews.Count,Is.EqualTo(3));Assert.That(File.ReadAllText(path),Does.Not.Contain("accessToken"));
            }finally{if(File.Exists(path))File.Delete(path);}
        }
        [TestCase(EmbyLibraryView.Poster)][TestCase(EmbyLibraryView.Banner)][TestCase(EmbyLibraryView.Disc)]
        [TestCase(EmbyLibraryView.Logo)][TestCase(EmbyLibraryView.Thumb)][TestCase(EmbyLibraryView.List)][TestCase(EmbyLibraryView.Table)]
        public void ThreeArtworkSizesPreserveTheLargeLayoutAndKeepVirtualizationBounded(EmbyLibraryView view)
        {
            foreach(float viewport in new[]{1076f,1324f}){
                var original=EmbyLibraryViews.Layout(view,viewport);var large=EmbyLibraryViews.Layout(view,viewport,EmbyArtworkSize.Large);
                var medium=EmbyLibraryViews.Layout(view,viewport,EmbyArtworkSize.Medium);var small=EmbyLibraryViews.Layout(view,viewport,EmbyArtworkSize.Small);
                Assert.That(large,Is.EqualTo(original));Assert.That(large.ArtworkWidth,Is.GreaterThan(medium.ArtworkWidth));Assert.That(medium.ArtworkWidth,Is.GreaterThan(small.ArtworkWidth));
                foreach(var layout in new[]{large,medium,small}){
                    Assert.That(layout.ArtworkWidth/layout.ArtworkHeight,Is.EqualTo(original.ArtworkWidth/original.ArtworkHeight).Within(.001));
                    Assert.That(layout.Columns*layout.Width+(layout.Columns-1)*18,Is.EqualTo(viewport-28).Within(.01));
                    Assert.That(layout.PoolSize(500000,626,180),Is.EqualTo(layout.PoolSize(205,626,180)).And.LessThan(100));
                    Assert.That(layout.PoolSize(205,626,180)/(float)layout.Columns*layout.Pitch,Is.GreaterThanOrEqualTo(986));
                }
            }
        }

        [TestCase(EmbyLibraryView.Poster)][TestCase(EmbyLibraryView.Banner)][TestCase(EmbyLibraryView.Disc)]
        [TestCase(EmbyLibraryView.Logo)][TestCase(EmbyLibraryView.Thumb)][TestCase(EmbyLibraryView.List)][TestCase(EmbyLibraryView.Table)]
        public void LargeLibraryPoolsStayBoundedAndCoverTheViewport(EmbyLibraryView view)
        {
            var layout=EmbyLibraryViews.Layout(view);
            Assert.That(layout.PoolSize(500000,626,180),Is.EqualTo(layout.PoolSize(205,626,180)));
            Assert.That(layout.PoolSize(205,626,180)*layout.Pitch/layout.Columns,Is.GreaterThanOrEqualTo(626+360));
            Assert.That(layout.PoolSize(2,626,180),Is.EqualTo(2));Assert.That(layout.PoolSize(0,626,180),Is.Zero);
            Assert.That(layout.ContentHeight(205),Is.GreaterThanOrEqualTo(205f/layout.Columns*layout.Pitch));
            Assert.That(layout.Pitch,Is.GreaterThan(layout.CardHeight));
        }

        [Test] public void PosterDiscAndLandscapeArtUseDifferentProportions()
        {
            var poster=EmbyLibraryViews.Layout(EmbyLibraryView.Poster);var disc=EmbyLibraryViews.Layout(EmbyLibraryView.Disc);
            var thumb=EmbyLibraryViews.Layout(EmbyLibraryView.Thumb);var banner=EmbyLibraryViews.Layout(EmbyLibraryView.Banner);
            Assert.That(poster.ArtworkWidth/poster.ArtworkHeight,Is.EqualTo(2f/3).Within(.001));
            Assert.That(disc.ArtworkWidth/disc.ArtworkHeight,Is.EqualTo(1));Assert.That(thumb.ArtworkWidth/thumb.ArtworkHeight,Is.EqualTo(16f/9).Within(.001));
            Assert.That(banner.ArtworkWidth/banner.ArtworkHeight,Is.GreaterThan(4));
        }

        [TestCase(EmbyLibraryView.Poster,"Primary")][TestCase(EmbyLibraryView.Banner,"Banner")][TestCase(EmbyLibraryView.Disc,"Disc")]
        [TestCase(EmbyLibraryView.Logo,"Logo")][TestCase(EmbyLibraryView.Thumb,"Thumb")][TestCase(EmbyLibraryView.List,"Primary")][TestCase(EmbyLibraryView.Table,"Thumb")]
        public void ViewsChooseTheMatchingAdvertisedImage(EmbyLibraryView view,string type)
        {
            var item=EmbyClient.ParseItems("[{\"Id\":\"movie\",\"ImageTags\":{\"Primary\":\"poster\",\"Banner\":\"banner\",\"Disc\":\"disc\",\"Logo\":\"logo\",\"Thumb\":\"thumb\"}}]").Items[0];
            Assert.That(EmbyLibraryViews.ImageType(item,view),Is.EqualTo(type));
            Assert.That(EmbyLibraryViews.ImageTag(item,type),Is.Not.Empty);
        }

        [TestCase(EmbyLibraryView.Banner)][TestCase(EmbyLibraryView.Disc)][TestCase(EmbyLibraryView.Logo)][TestCase(EmbyLibraryView.Thumb)][TestCase(EmbyLibraryView.Table)]
        public void MissingSpecialArtworkFallsBackToPrimary(EmbyLibraryView view)
        {
            var item=new EmbyItem{ImageTags=new EmbyImageTags{Primary="poster"}};
            Assert.That(EmbyLibraryViews.ImageType(item,view),Is.EqualTo("Primary"));Assert.That(EmbyLibraryViews.ImageTag(item,EmbyLibraryViews.ImageType(item,view)),Is.EqualTo("poster"));
        }

        [TestCase(EmbyLibraryView.Thumb)][TestCase(EmbyLibraryView.Table)] public void LandscapeViewsPreferBackdropBeforePrimary(EmbyLibraryView view)
        {
            var item=new EmbyItem{ImageTags=new EmbyImageTags{Primary="poster"},BackdropImageTags=new[]{"backdrop"}};
            Assert.That(EmbyLibraryViews.ImageType(item,view),Is.EqualTo("Backdrop"));
        }

        [TestCase("Disc")][TestCase("Logo")] public void TransparentArtworkUsesPngAndHeaderAuthentication(string type)
        {
            var account=new EmbyAccount{serverUrl="http://example.test:8096",accessToken="fixture-token"};
            string url=new EmbyClient("fixture").ImageUrl(account,"movie",type,5000);
            Assert.That(url,Does.Contain("/Images/"+type).And.Contain("format=png").And.Contain("maxWidth=1024"));
            Assert.That(url,Does.Not.Contain(account.accessToken).And.Not.Contain("api_key"));
        }
    }
}
