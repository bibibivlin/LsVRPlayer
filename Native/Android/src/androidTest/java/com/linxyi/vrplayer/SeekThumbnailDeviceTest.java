package com.linxyi.vrplayer;

import static org.junit.Assert.*;
import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.net.Uri;
import android.os.SystemClock;
import android.util.Base64;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;
import org.json.JSONObject;
import org.junit.*;
import org.junit.runner.RunWith;

/** Uses only a generated clip in the isolated test package, never installed-player settings. */
@RunWith(AndroidJUnit4.class)
public final class SeekThumbnailDeviceTest {
    private Context context;private File fixture;private SeekThumbnailService service;
    @Before public void setup()throws Exception {
        context=InstrumentationRegistry.getInstrumentation().getContext();fixture=File.createTempFile("seek-preview-",".mp4",context.getCacheDir());
        try(InputStream input=context.getAssets().open("seek-preview.mp4");OutputStream output=new FileOutputStream(fixture)){
            byte[] buffer=new byte[8192];int count;while((count=input.read(buffer))>=0)output.write(buffer,0,count);
        }
        service=new SeekThumbnailService();service.setSource(context,Uri.fromFile(fixture).toString(),"",7);
    }
    @After public void cleanup(){if(service!=null)service.close();if(fixture!=null)assertTrue("Owned fixture cleanup",fixture.delete());}
    private JSONObject waitFor(int request)throws Exception {
        long deadline=SystemClock.elapsedRealtime()+10000;
        while(SystemClock.elapsedRealtime()<deadline){String result=service.take(request);if(!result.isEmpty())return new JSONObject(result);Thread.sleep(30);}
        throw new AssertionError("Thumbnail timed out");
    }
    private void assertFrame(JSONObject result,int channel)throws Exception {
        assertTrue(result.toString(),result.optBoolean("ok"));assertEquals(7,result.getInt("generation"));
        byte[] bytes=Base64.decode(result.getString("image"),Base64.NO_WRAP);Bitmap bitmap=BitmapFactory.decodeByteArray(bytes,0,bytes.length);
        try{
            assertNotNull(bitmap);assertTrue(bitmap.getWidth()<=320);assertTrue(bitmap.getHeight()<=180);
            int color=bitmap.getPixel(bitmap.getWidth()/2,bitmap.getHeight()/2);
            int[] rgb={Color.red(color),Color.green(color),Color.blue(color)};
            assertTrue("Expected time's color",rgb[channel]>180);assertTrue(rgb[(channel+1)%3]<60);assertTrue(rgb[(channel+2)%3]<60);
        }finally{if(bitmap!=null)bitmap.recycle();}
    }
    @Test public void arbitraryTimelinePositionsDecodeDistinctSmallFrames()throws Exception {
        assertFrame(waitFor(service.begin(1000,7)),0);assertFrame(waitFor(service.begin(5000,7)),1);assertFrame(waitFor(service.begin(9000,7)),2);
    }
    @Test public void previewDecoderDoesNotSeekOrPauseConcurrentMedia3Playback()throws Exception {
        androidx.media3.exoplayer.ExoPlayer[] player=new androidx.media3.exoplayer.ExoPlayer[1];
        var instrumentation=InstrumentationRegistry.getInstrumentation();long[] position=new long[1];boolean[] playing=new boolean[1];int[] state=new int[1];
        instrumentation.runOnMainSync(()->{
            player[0]=new androidx.media3.exoplayer.ExoPlayer.Builder(context).build();player[0].setVolume(0);
            player[0].setMediaItem(androidx.media3.common.MediaItem.fromUri(Uri.fromFile(fixture)));player[0].prepare();player[0].play();
        });
        try{
            long deadline=SystemClock.elapsedRealtime()+8000;
            do{
                instrumentation.runOnMainSync(()->{state[0]=player[0].getPlaybackState();playing[0]=player[0].isPlaying();assertNull(player[0].getPlayerError());});
                if(playing[0])break;Thread.sleep(40);
            }while(SystemClock.elapsedRealtime()<deadline);
            assertTrue("Media3 must be playing before preview",playing[0]);
            instrumentation.runOnMainSync(()->position[0]=player[0].getCurrentPosition());long before=position[0],started=SystemClock.elapsedRealtime();
            assertFrame(waitFor(service.begin(9000,7)),2);
            instrumentation.runOnMainSync(()->{position[0]=player[0].getCurrentPosition();playing[0]=player[0].isPlaying();assertNull(player[0].getPlayerError());});
            assertTrue(playing[0]);assertTrue(position[0]>=before);assertTrue("Preview must not seek main playback",position[0]-before<=SystemClock.elapsedRealtime()-started+500);
        }finally{instrumentation.runOnMainSync(()->player[0].release());}
    }
    @Test public void replacementCancellationAndWrongGenerationNeverReturnOldFrames()throws Exception {
        int old=service.begin(1000,7);int latest=service.begin(9000,7);
        assertFalse(waitFor(old).optBoolean("ok"));assertFrame(waitFor(latest),2);
        int cancelled=service.begin(5000,7);service.cancel(cancelled);assertFalse(waitFor(cancelled).optBoolean("ok"));
        service.setSource(context,Uri.fromFile(fixture).toString(),"",8);assertEquals(0,service.begin(5000,7));
        service.clear();assertEquals(0,service.begin(1000,8));
    }
    @Test public void authenticatedRangePreviewUsesHeadersAndRejectsCrossOriginRedirect()throws Exception {
        byte[] media=Files.readAllBytes(fixture.toPath());AtomicInteger authenticated=new AtomicInteger();
        try(RangeServer target=new RangeServer(media,authenticated,null);RangeServer redirect=new RangeServer(media,new AtomicInteger(),target.url())){
            service.setSource(context,target.url(),"fixture-token",7);assertFrame(waitFor(service.begin(9000,7)),2);assertTrue(authenticated.get()>0);
            int before=authenticated.get();service.setSource(context,redirect.url(),"fixture-token",7);
            assertFalse(waitFor(service.begin(1000,7)).optBoolean("ok"));assertEquals("Redirect must not receive the token",before,authenticated.get());
        }
    }
    @Test public void cancelledNetworkReadAllowsNextLocalPreview()throws Exception {
        try(ServerSocket stall=new ServerSocket(0,1,InetAddress.getByName("127.0.0.1"))){
            service.setSource(context,"http://127.0.0.1:"+stall.getLocalPort()+"/video","fixture-token",7);
            int old=service.begin(1000,7);stall.setSoTimeout(5000);
            try(Socket ignored=stall.accept()){
                service.cancel(old);assertFalse(waitFor(old).optBoolean("ok"));
                service.setSource(context,Uri.fromFile(fixture).toString(),"",7);assertFrame(waitFor(service.begin(5000,7)),1);
            }
        }
    }
    @Test public void smbPreviewReadsThroughItsOwnConnection()throws Exception {
        String port=InstrumentationRegistry.getArguments().getString("thumbnailSmbPort","");
        Assume.assumeTrue("Optional loopback SMB fixture requires thumbnailSmbPort",!port.isEmpty());
        SmbStore.initialize(context);String id="thumbnail-fixture-"+java.util.UUID.randomUUID();
        try{
            SmbStore.save(new JSONObject().put("id",id).put("host","127.0.0.1").put("port",Integer.parseInt(port)).put("share","TestMedia").toString());
            String uri="smb://"+id+"/Thumbnails/seek-preview.mp4";service.setSource(context,uri,"",7);
            // A separate reader remains usable after the preview decoder releases its handle.
            try(SmbStore.Handle playback=SmbStore.connect(Uri.parse(uri));com.hierynomus.smbj.share.File file=playback.open()){
                byte[] before=new byte[32],after=new byte[32];assertEquals(32,file.read(before,0,0,32));
                assertFrame(waitFor(service.begin(9000,7)),2);assertEquals(32,file.read(after,0,0,32));assertArrayEquals(before,after);
            }
        }finally{service.clear();SmbStore.remove(id);}
    }
    private static final class RangeServer implements AutoCloseable {
        final ServerSocket socket;final ExecutorService thread=Executors.newSingleThreadExecutor();volatile boolean closed;volatile Throwable failure;
        RangeServer(byte[] media,AtomicInteger requests,String redirect)throws Exception {
            socket=new ServerSocket(0,8,InetAddress.getByName("127.0.0.1"));
            thread.submit(()->{while(!closed)try(Socket client=socket.accept()){
                client.setSoTimeout(3000);BufferedReader input=new BufferedReader(new InputStreamReader(client.getInputStream(),StandardCharsets.US_ASCII));
                String line=input.readLine();if(line==null)continue;assertFalse("Token in request URL",line.contains("fixture-token"));
                String range="",token="";while((line=input.readLine())!=null&&!line.isEmpty()){
                    if(line.toLowerCase().startsWith("range:"))range=line.substring(6).trim();
                    if(line.toLowerCase().startsWith("x-emby-token:"))token=line.substring(13).trim();
                }
                assertEquals("fixture-token",token);requests.incrementAndGet();OutputStream output=client.getOutputStream();
                if(redirect!=null){output.write(("HTTP/1.1 302 Found\r\nLocation: "+redirect+"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.US_ASCII));continue;}
                String[] offsets=range.substring(6).split("-",-1);int start=Integer.parseInt(offsets[0]),end=offsets[1].isEmpty()?media.length-1:Math.min(media.length-1,Integer.parseInt(offsets[1]));
                output.write(("HTTP/1.1 206 Partial Content\r\nContent-Type: video/mp4\r\nContent-Range: bytes "+start+"-"+end+"/"+media.length+"\r\nContent-Length: "+(end-start+1)+"\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.US_ASCII));output.write(media,start,end-start+1);
            }catch(Throwable error){if(!closed)failure=error;}});
        }
        String url(){return "http://127.0.0.1:"+socket.getLocalPort()+"/video";}
        public void close()throws Exception {closed=true;socket.close();thread.shutdownNow();assertTrue(thread.awaitTermination(4,TimeUnit.SECONDS));if(failure!=null)throw new AssertionError(failure);}
    }
}
