package com.linxyi.vrplayer;

import android.content.Context;
import android.graphics.Bitmap;
import android.media.MediaMetadataRetriever;
import android.util.Base64;
import java.io.ByteArrayOutputStream;
import java.util.concurrent.*;
import org.json.JSONObject;

/** One decoder, one replaceable queued request, and no persistent media/credential cache. */
final class SeekThumbnailService implements AutoCloseable {
    private final ThreadPoolExecutor worker=new ThreadPoolExecutor(1,1,0,TimeUnit.MILLISECONDS,new ArrayBlockingQueue<>(1));
    private final ScheduledThreadPoolExecutor timer=new ScheduledThreadPoolExecutor(1);
    private final ExecutorService cleanup=Executors.newSingleThreadExecutor();
    private Context context;private String location="",token="";private int generation,sequence;
    private Request current;
    SeekThumbnailService(){timer.setRemoveOnCancelPolicy(true);}

    synchronized void setSource(Context context,String location,String token,int generation){
        cancelCurrent();this.context=context;this.location=location;this.token=token;this.generation=generation;
    }
    synchronized void clear(){cancelCurrent();context=null;location=token="";}
    @Override public synchronized void close(){clear();worker.shutdownNow();timer.shutdownNow();cleanup.shutdown();}
    synchronized int begin(long position,int expectedGeneration){
        if(context==null||location.isEmpty()||expectedGeneration!=generation)return 0;
        cancelCurrent();Request request=new Request(++sequence,context,location,token,generation,Math.max(0,position));current=request;
        worker.getQueue().clear();worker.execute(()->decode(request));return request.id;
    }
    synchronized String take(int id){
        if(current==null||current.id!=id)return "{\"ok\":false}";
        String value=current.result;if(!value.isEmpty())current=null;return value;
    }
    synchronized void cancel(int id){if(current!=null&&current.id==id)cancelCurrent();}
    private void cancelCurrent(){
        if(current==null)return;Request request=current;current=null;request.cancelled=true;request.source.cancel();
        if(request.timeout!=null)request.timeout.cancel(false);
        // Pending requests own no resources. An active decode closes its descriptors in finally.
        if(request.started)cleanup.execute(request.source::abortNetwork);
        worker.getQueue().clear();
    }
    private void decode(Request request){
        synchronized(this){if(request.cancelled)return;request.started=true;
            request.timeout=timer.schedule(()->expire(request),6,TimeUnit.SECONDS);}
        MediaMetadataRetriever retriever=new MediaMetadataRetriever();Bitmap bitmap=null;
        try{
            request.source.open(request.context,request.location,request.token);
            if(request.cancelled)return;retriever.setDataSource(request.source);
            if(request.cancelled)return;
            bitmap=retriever.getScaledFrameAtTime(request.position*1000,MediaMetadataRetriever.OPTION_CLOSEST_SYNC,320,180);
            if(bitmap==null||request.cancelled)return;
            ByteArrayOutputStream bytes=new ByteArrayOutputStream();bitmap.compress(Bitmap.CompressFormat.JPEG,78,bytes);
            String result=new JSONObject().put("ok",true).put("generation",request.generation).put("position",request.position)
                .put("image",Base64.encodeToString(bytes.toByteArray(),Base64.NO_WRAP)).toString();
            synchronized(this){if(current==request&&!request.cancelled)request.result=result;}
        }catch(Exception|OutOfMemoryError ignored){
            // Preview failures must never become playback errors or expose media URLs.
        }finally{
            if(bitmap!=null)bitmap.recycle();try{retriever.release();}catch(Exception ignored){}request.source.close();
            synchronized(this){if(request.timeout!=null)request.timeout.cancel(false);if(current==request&&request.result.isEmpty())request.result="{\"ok\":false}";}
        }
    }
    private synchronized void expire(Request request){
        if(current!=request)return;request.cancelled=true;request.source.cancel();request.result="{\"ok\":false}";
        cleanup.execute(request.source::abortNetwork);
    }
    private static final class Request {
        final int id,generation;final Context context;final String location,token;final long position;
        final ThumbnailDataSource source=new ThumbnailDataSource();
        volatile boolean cancelled;boolean started;String result="";ScheduledFuture<?> timeout;
        Request(int id,Context context,String location,String token,int generation,long position){this.id=id;this.context=context;this.location=location;this.token=token;this.generation=generation;this.position=position;}
    }
}
