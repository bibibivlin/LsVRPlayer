using System;
using System.Collections;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRPlayer.Tests
{
    public sealed class SeekThumbnailPlaybackTests
    {
        [UnityTest] public IEnumerator EditorPreviewDecodesAnotherTimeWithoutSeekingMainPlayback()
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../TestMedia/Thumbnails/seek-preview.mp4"));
            if(!File.Exists(path))Assert.Ignore("Run python Tools/Generate-ThumbnailFixture.py first.");
            var owner=new GameObject("Thumbnail playback regression");var backend=new UnityMediaBackend(owner);Texture2D frame=null;
            using var cancellation=new CancellationTokenSource();
            try{
                backend.Open(new Uri(path).AbsoluteUri,0,7);double deadline=Time.realtimeSinceStartupAsDouble+15;
                while(backend.Snapshot.duration==0&&Time.realtimeSinceStartupAsDouble<deadline){backend.Tick();yield return null;}
                Assert.That(backend.Snapshot.duration,Is.GreaterThan(10000));backend.Pause();backend.Tick();long before=backend.Snapshot.position;
                var request=backend.ReadThumbnail(9000,7,cancellation.Token);
                while(!request.IsCompleted&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                Assert.That(request.IsCompleted);Assert.That(request.IsFaulted,Is.False,request.Exception?.ToString());frame=request.GetAwaiter().GetResult();Assert.That(frame,Is.Not.Null);
                backend.Tick();Assert.That(backend.Snapshot.playing,Is.False);Assert.That(backend.Snapshot.position,Is.EqualTo(before).Within(100));
                var target=RenderTexture.GetTemporary(frame.width,frame.height,0);var previous=RenderTexture.active;Texture2D sample=null;
                try{
                    Graphics.Blit(frame,target);RenderTexture.active=target;sample=new Texture2D(frame.width,frame.height,TextureFormat.RGB24,false);
                    sample.ReadPixels(new Rect(0,0,frame.width,frame.height),0,0);sample.Apply();var pixel=sample.GetPixel(frame.width/2,frame.height/2);
                    Assert.That(pixel.b,Is.GreaterThan(.7f));Assert.That(pixel.r,Is.LessThan(.25f));
                }finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);if(sample!=null)UnityEngine.Object.Destroy(sample);}
            }finally{cancellation.Cancel();if(frame!=null)UnityEngine.Object.Destroy(frame);backend.Dispose();UnityEngine.Object.Destroy(owner);}
        }
    }
}
