using NUnit.Framework;
using UnityEngine;
using UnityEngine.Video;

namespace VRPlayer.Tests
{
    public sealed class EmbyMediaBackendTests
    {
        [Test] public void AuthenticatedEditorOpenKeepsTokenOutOfVideoUrlAndErrors()
        {
            var host=new GameObject("Emby authenticated preview regression");
            try{
                var backend=new UnityMediaBackend(host);var video=host.GetComponent<VideoPlayer>();
                video.url="http://example.invalid/previous-local.mp4";
                const string token="private-emby-session-token";
                backend.OpenAuthenticated("http://example.invalid/Videos/item/stream?Static=true",token,32000,77);
                Assert.That(video.url,Is.Empty);
                Assert.That(backend.Snapshot.generation,Is.EqualTo(77));
                Assert.That(backend.Snapshot.state,Is.EqualTo(1));
                Assert.That(backend.Snapshot.error,Does.Contain("Android 头显"));
                Assert.That(backend.Snapshot.error,Does.Not.Contain(token).And.Not.Contain("api_key"));
                Assert.That(JsonUtility.ToJson(backend.Snapshot),Does.Not.Contain(token));
            }finally{Object.DestroyImmediate(host);}
        }
    }
}
