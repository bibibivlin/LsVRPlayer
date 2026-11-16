package com.linxyi.vrplayer;

import static org.junit.Assert.*;
import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.os.SystemClock;
import android.util.Base64;
import androidx.media3.common.C;
import androidx.media3.common.MediaItem;
import androidx.media3.exoplayer.ExoPlayer;
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory;
import androidx.media3.exoplayer.video.PlaceholderSurface;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import org.json.*;
import org.junit.*;
import org.junit.runner.RunWith;

/** Opt-in original-media reads only. Does not register an account or report playback progress. */
@RunWith(AndroidJUnit4.class)
public final class SeekThumbnailRealNetworkTest {
    @Test public void originalNetworkMediaProducesFramesAtDistantPositions()throws Exception {
        Context context=InstrumentationRegistry.getInstrumentation().getContext();
        File input=new File(context.getExternalFilesDir(null),"thumbnail-real.json");
        Assume.assumeTrue("Use Device-ThumbnailNetworkTest.py to supply an owned, temporary session",input.isFile());
        JSONObject config=new JSONObject(new String(Files.readAllBytes(input.toPath()),StandardCharsets.UTF_8));
        JSONArray cases=config.getJSONArray("cases"),results=new JSONArray();boolean passed=true;
        File output=new File(context.getExternalFilesDir(null),"thumbnail-real-result.json");
        try(SeekThumbnailService service=new SeekThumbnailService()){
            for(int i=0;i<cases.length();i++){
                JSONObject item=cases.getJSONObject(i);int generation=i+1;
                service.setSource(context,item.getString("url"),config.getString("token"),generation);
                JSONArray positions=item.getJSONArray("positions");
                try(ConcurrentPlayback playback=config.optBoolean("concurrentPlayback")?new ConcurrentPlayback(context,item.getString("url"),config.getString("token")):null){
                for(int p=0;p<positions.length();p++){
                    PlaybackState before=playback==null?null:playback.sample();boolean uninterrupted=true;
                    long position=positions.getLong(p),start=SystemClock.elapsedRealtime();int id=service.begin(position,generation);
                    JSONObject result=null;
                    while(SystemClock.elapsedRealtime()-start<35000){
                        String value=service.take(id);if(!value.isEmpty()){result=new JSONObject(value);break;}
                        if(playback!=null){PlaybackState state=playback.sample();uninterrupted&=state.playing&&!state.error;}
                        Thread.sleep(30);
                    }
                    long elapsed=SystemClock.elapsedRealtime()-start;
                    boolean ok=result!=null&&result.optBoolean("ok");int width=0,height=0;
                    if(ok){
                        byte[] bytes=Base64.decode(result.getString("image"),Base64.NO_WRAP);Bitmap frame=BitmapFactory.decodeByteArray(bytes,0,bytes.length);
                        if(frame!=null){width=frame.getWidth();height=frame.getHeight();frame.recycle();}else ok=false;
                    }
                    service.cancel(id);
                    JSONObject metric=new JSONObject().put("case",i).put("positionIndex",p).put("elapsedMs",elapsed)
                        .put("ok",ok).put("width",width).put("height",height);
                    if(playback!=null){
                        PlaybackState after=playback.sample();long advanced=after.position-before.position;
                        boolean valid=uninterrupted&&after.playing&&!after.error&&advanced>0&&advanced<=elapsed+1000&&after.decoded>before.decoded;
                        metric.put("playbackAdvancedMs",advanced).put("decodedFrames",after.decoded-before.decoded).put("playbackUninterrupted",valid);
                        passed&=valid;
                    }
                    passed&=ok;
                    if(result!=null){for(String field:new String[]{"reason","stage","readBytes","readCalls","prepareMs","frameMs"})if(result.has(field))metric.put(field,result.get(field));}
                    results.put(metric);Files.write(output.toPath(),results.toString(2).getBytes(StandardCharsets.UTF_8));
                }
                }
            }
        }
        assertTrue("Original network thumbnail failed; see the private run's sanitized metrics",passed);
    }
    private static final class PlaybackState {long position;int decoded;boolean playing,error;}
    private static final class ConcurrentPlayback implements AutoCloseable {
        private final PlaybackSession session=new PlaybackSession(1,true);
        private ExoPlayer player;private PlaceholderSurface surface;
        ConcurrentPlayback(Context context,String location,String token)throws Exception {
            try{
                InstrumentationRegistry.getInstrumentation().runOnMainSync(()->{
                    surface=PlaceholderSurface.newInstance(context,false);
                    player=new ExoPlayer.Builder(context).setMediaSourceFactory(new DefaultMediaSourceFactory(()->new EmbyDataSource(session,location,token))).build();
                    player.setVideoSurface(surface);player.setVolume(0);
                    player.setTrackSelectionParameters(player.getTrackSelectionParameters().buildUpon().setTrackTypeDisabled(C.TRACK_TYPE_AUDIO,true).build());
                    player.setMediaItem(MediaItem.fromUri(location));player.prepare();player.play();
                });
                long until=SystemClock.elapsedRealtime()+45000;
                while(SystemClock.elapsedRealtime()<until){PlaybackState state=sample();assertFalse("Concurrent original playback failed",state.error);if(state.playing&&state.decoded>30)return;Thread.sleep(100);}
                throw new AssertionError("Concurrent decoder did not become ready");
            }catch(Exception|AssertionError error){close();throw error;}
        }
        PlaybackState sample(){
            PlaybackState state=new PlaybackState();
            InstrumentationRegistry.getInstrumentation().runOnMainSync(()->{
                state.position=player.getCurrentPosition();state.playing=player.isPlaying();state.error=player.getPlayerError()!=null;
                var counters=player.getVideoDecoderCounters();if(counters!=null){counters.ensureUpdated();state.decoded=counters.renderedOutputBufferCount+counters.skippedOutputBufferCount;}
            });return state;
        }
        @Override public void close(){
            InstrumentationRegistry.getInstrumentation().runOnMainSync(()->{if(player!=null)player.release();player=null;if(surface!=null)surface.release();surface=null;});session.cancel();
        }
    }
}
