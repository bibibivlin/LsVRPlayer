using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace VRPlayer.Tests
{
    public sealed class SeekThumbnailTests
    {
        private sealed class Source : ISeekThumbnailSource
        {
            public Func<long,CancellationToken,Task<Texture2D>> Read;
            public Task<Texture2D> ReadThumbnail(long position,int generation,CancellationToken cancellation)=>Read(position,cancellation);
        }
        [TestCase(-1,120000,0)][TestCase(0,120000,0)][TestCase(.5f,120000,60000)]
        [TestCase(1,120000,119999)][TestCase(2,120000,119999)][TestCase(.5f,0,0)]
        public void PreviewPositionStaysInsidePlayableDuration(float value,long duration,long expected)
        {
            Assert.That(SeekThumbnailCache.Position(value,duration),Is.EqualTo(expected));
        }
        [TestCase(504)][TestCase(1440)]
        public void LongFilmTremorAcrossTimeBucketsKeepsThePreviewTarget(float width)
        {
            var target=new SeekThumbnailTarget();Assert.That(target.Update(.5f,width,0));
            float jitter=SeekThumbnailTarget.DeadZone(width)*.9f/width;
            for(int i=1;i<=200;i++)Assert.That(target.Update(.5f+(i%2==0?jitter:-jitter),width,i*.02),Is.False);
            Assert.That(target.Value,Is.EqualTo(.5f));
            Assert.That(SeekThumbnailCache.Key(SeekThumbnailCache.Position(.5f+jitter,7200000)),Is.Not.EqualTo(3600000));
        }
        [TestCase(504)][TestCase(1440)]
        public void SmallIntentionalMovementSettlesWithoutNeedingAnotherPointerEvent(float width)
        {
            var target=new SeekThumbnailTarget();target.Update(.5f,width,0);float next=.5f+SeekThumbnailTarget.DeadZone(width)*1.5f/width;
            Assert.That(target.Update(next,width,.1),Is.False);Assert.That(target.Update(next,width,.27),Is.False);
            Assert.That(target.Update(next,width,.29));Assert.That(target.Value,Is.EqualTo(next));
            Assert.That(target.Update(next-SeekThumbnailTarget.DeadZone(width)*.4f/width,width,.5),Is.False,"Hysteresis must follow the accepted position");
        }
        [Test] public void BriefMotionOutsideTheDeadZoneDoesNotChangeThePreview()
        {
            var target=new SeekThumbnailTarget();target.Update(.5f,1000,0);
            Assert.That(target.Update(.507f,1000,.01),Is.False);Assert.That(target.Update(.5f,1000,.1),Is.False);
            Assert.That(target.Update(.5f,1000,1),Is.False);Assert.That(target.Value,Is.EqualTo(.5f));
        }
        [Test] public void MovingOutsideTheDeadZoneHasABoundedDebounceWait()
        {
            var target=new SeekThumbnailTarget();target.Update(.5f,1000,0);
            Assert.That(target.Update(.506f,1000,.01),Is.False);
            Assert.That(target.Update(.512f,1000,.11),Is.False);Assert.That(target.Update(.506f,1000,.21),Is.False);
            Assert.That(target.Update(.512f,1000,.31),Is.False);Assert.That(target.Update(.506f,1000,.37));
            Assert.That(target.Value,Is.EqualTo(.506f));
        }
        [Test] public void LargeMovementUpdatesImmediatelyAndResetForgetsThePreviousAnchor()
        {
            var target=new SeekThumbnailTarget();target.Update(.2f,504,0);Assert.That(target.Update(.8f,504,.001));
            target.Reset();Assert.That(target.Update(.801f,504,.002));Assert.That(target.Value,Is.EqualTo(.801f));
        }
        [Test] public void SlowIntentionalMovementAccumulatesBeyondTheDeadZone()
        {
            var target=new SeekThumbnailTarget();target.Update(.3f,1000,0);bool changed=false;
            for(int i=1;i<=30;i++)changed|=target.Update(.3f+i*.001f,1000,i*.04);
            Assert.That(changed);Assert.That(target.Value,Is.GreaterThan(.32f));
        }
        [Test] public async Task NearbyPositionsReuseFrameAndCacheEvictsLeastRecentlyViewedTexture()
        {
            int reads=0;using var cache=new SeekThumbnailCache();cache.SetGeneration(1);
            var source=new Source{Read=(_,__)=>{reads++;return Task.FromResult(new Texture2D(2,2));}};
            var first=await cache.Get(source,0,1,default);Assert.That(await cache.Get(source,1999,1,default),Is.SameAs(first));Assert.That(reads,Is.EqualTo(1));
            var second=await cache.Get(source,2000,1,default);
            for(int i=2;i<SeekThumbnailCache.Capacity;i++)await cache.Get(source,i*2000,1,default);
            await cache.Get(source,0,1,default);await cache.Get(source,48000,1,default);
            Assert.That(cache.Count,Is.EqualTo(SeekThumbnailCache.Capacity));Assert.That(first!=null);Assert.That(second==null);
            cache.SetGeneration(2);Assert.That(first==null);Assert.That(cache.Count,Is.Zero);
        }
        [Test] public async Task SwitchingFilmCancelsRequestAndDestroysLateFrame()
        {
            using var cache=new SeekThumbnailCache();cache.SetGeneration(1);var completion=new TaskCompletionSource<Texture2D>();CancellationToken token=default;
            var source=new Source{Read=(_,cancellation)=>{token=cancellation;return completion.Task;}};
            var request=cache.Get(source,4000,1,default);cache.SetGeneration(2);Assert.That(token.IsCancellationRequested);
            var frame=new Texture2D(2,2);completion.SetResult(frame);
            Assert.That(await request,Is.Null);Assert.That(frame==null);Assert.That(cache.Count,Is.Zero);
            Assert.That(await cache.Get(source,4000,1,default),Is.Null);
        }
        [Test] public async Task LeavingTimelineDiscardsLateFrameWithoutCachingIt()
        {
            using var cache=new SeekThumbnailCache();using var cancellation=new CancellationTokenSource();cache.SetGeneration(3);
            var completion=new TaskCompletionSource<Texture2D>();var source=new Source{Read=(_,__)=>completion.Task};
            var request=cache.Get(source,0,3,cancellation.Token);cancellation.Cancel();var frame=new Texture2D(2,2);completion.SetResult(frame);
            Assert.That(await request,Is.Null);Assert.That(frame==null);Assert.That(cache.Count,Is.Zero);
        }
        [TestCase(StereoLayout.Mono,false,0,0,1,1)]
        [TestCase(StereoLayout.SideBySide,false,0,0,.5f,1)][TestCase(StereoLayout.SideBySide,true,.5f,0,.5f,1)]
        [TestCase(StereoLayout.TopBottom,false,0,.5f,1,.5f)][TestCase(StereoLayout.TopBottom,true,0,0,1,.5f)]
        public void PreviewUsesOneEyeAndRespectsEyeSwap(StereoLayout stereo,bool swapped,float x,float y,float width,float height)
        {
            Assert.That(SeekThumbnailPreview.EyeRect(new ViewSettings{stereo=stereo,swapEyes=swapped}),Is.EqualTo(new Rect(x,y,width,height)));
        }
    }
}
