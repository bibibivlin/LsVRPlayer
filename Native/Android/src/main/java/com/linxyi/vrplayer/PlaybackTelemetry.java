package com.linxyi.vrplayer;

import android.os.SystemClock;
import android.util.Log;
import org.json.JSONObject;
import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicLong;

/** Bounded diagnostics, no URI/credentials, and no disk or logcat writes on the playback threads. */
final class PlaybackTelemetry {
    private final File output;
    private final ArrayBlockingQueue<String> events=new ArrayBlockingQueue<>(256);
    private final AtomicLong droppedEvents=new AtomicLong();
    private final ScheduledExecutorService worker=Executors.newSingleThreadScheduledExecutor();
    private volatile PlaybackSession session;
    private volatile String state="{}";
    PlaybackTelemetry(File directory){output=new File(directory,"playback-events.jsonl");worker.scheduleAtFixedRate(this::flush,0,1,TimeUnit.SECONDS);}
    void session(PlaybackSession value){session=value;state="{}";}
    void state(JSONObject value){state=value.toString();}
    void events(PlaybackSession value){
        if(value==null)return;
        for(BufferingTracker.Event event:value.buffering.drain())try{
            String json=new JSONObject().put("type",event.type).put("reason",event.reason).put("generation",value.generation)
                .put("monotonicMs",event.timeMs).put("durationMs",event.durationMs).toString();
            if(!events.offer(json))droppedEvents.incrementAndGet();
        }catch(Exception ignored){}
    }
    void close(){worker.execute(this::flush);worker.shutdown();}
    private void flush(){
        try{
            PlaybackSession current=session;long now=SystemClock.elapsedRealtime();
            JSONObject tick=new JSONObject(state);
            if(current!=null){
                PlaybackMetrics.Sample sample=current.metrics.sample(now);
                JSONObject network=new JSONObject().put("generation",current.generation).put("monotonicMs",now).put("readAhead",current.readAhead)
                    .put("requests",sample.requests).put("networkBytes",sample.networkBytes).put("deliveredBytes",sample.deliveredBytes)
                    .put("errors",sample.errors).put("cancelledReads",sample.cancelledReads).put("smallReads",sample.smallReads).put("opens",sample.opens).put("mbps",sample.mbps)
                    .put("averageRequestBytes",sample.averageRequestBytes).put("averageReadBytes",sample.averageReadBytes)
                    .put("averageReadMs",sample.averageReadMs).put("readP95Ms",sample.readP95Ms);
                current.networkSnapshot=network.toString();
                if(tick.optInt("generation",-1)==current.generation&&(tick.optBoolean("playing")||tick.optInt("state")==2)){
                    tick.put("network",network).put("type","sample").put("droppedDiagnosticEvents",droppedEvents.get());
                }else tick=null;
            }else tick=null;
            StringBuilder lines=new StringBuilder();String event;
            while((event=events.poll())!=null){lines.append(event).append('\n');Log.i("VRPlayerPlayback",event);}
            if(tick!=null)lines.append(tick).append('\n');
            if(lines.length()==0)return;
            if(output.length()>8*1024*1024){File previous=new File(output.getParentFile(),"playback-events.previous.jsonl");
                if(previous.exists()&&!previous.delete())throw new java.io.IOException("Cannot rotate diagnostics");
                if(!output.renameTo(previous))throw new java.io.IOException("Cannot rotate diagnostics");}
            try(FileOutputStream stream=new FileOutputStream(output,true)){stream.write(lines.toString().getBytes(StandardCharsets.UTF_8));}
        }catch(Exception ex){Log.w("VRPlayerPlayback","Diagnostics unavailable: "+ex.getClass().getSimpleName());}
    }
}
