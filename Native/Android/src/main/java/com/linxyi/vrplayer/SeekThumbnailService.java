package com.linxyi.vrplayer;

import android.content.Context;
import android.graphics.Bitmap;
import android.media.MediaMetadataRetriever;
import android.util.Base64;
import java.io.ByteArrayOutputStream;
import java.util.concurrent.*;
import org.json.JSONObject;

/** One decoder and one replaceable queued request; the current index is reused briefly while scrubbing. */
final class SeekThumbnailService implements AutoCloseable {
    private final ThreadPoolExecutor worker=new ThreadPoolExecutor(1,1,0,TimeUnit.MILLISECONDS,new ArrayBlockingQueue<>(1));
    private final ScheduledThreadPoolExecutor timer=new ScheduledThreadPoolExecutor(1);
    private final ExecutorService cleanup=Executors.newSingleThreadExecutor();
    private Context context;private String location="",token="";private int generation,sequence;
    private Request current;private Session session;private ScheduledFuture<?> idle;
    SeekThumbnailService(){timer.setRemoveOnCancelPolicy(true);}

    synchronized void setSource(Context context,String location,String token,int generation){
        clear();this.context=context;this.location=location;this.token=token;this.generation=generation;
    }
    synchronized void clear(){cancelCurrent();retireSession();context=null;location=token="";}
    @Override public synchronized void close(){clear();worker.shutdownNow();timer.shutdownNow();cleanup.shutdown();}
    synchronized int begin(long position,int expectedGeneration){
        if(context==null||location.isEmpty()||expectedGeneration!=generation)return 0;
        cancelCurrent();if(idle!=null){idle.cancel(false);idle=null;}
        if(session==null)session=new Session(context,location,token);
        Request request=new Request(++sequence,generation,Math.max(0,position),session);current=request;
        // Include queue time: a retired decoder must not leave a new request loading indefinitely.
        request.timeout=timer.schedule(()->expire(request),20,TimeUnit.SECONDS);
        worker.getQueue().clear();worker.execute(()->decode(request));return request.id;
    }
    synchronized String take(int id){
        if(current==null||current.id!=id)return "{\"ok\":false}";
        String value=current.result;if(!value.isEmpty())current=null;return value;
    }
    synchronized void cancel(int id){if(current!=null&&current.id==id)cancelCurrent();}
    private void cancelCurrent(){
        if(current==null)return;Request request=current;current=null;request.cancelled=true;
        if(request.timeout!=null)request.timeout.cancel(false);
        // Completed reads may share the same index; an interrupted read must never be reused.
        if(request.result.isEmpty()&&session==request.session)retireSession();
        worker.getQueue().clear();
    }
    private void retireSession(){
        if(idle!=null){idle.cancel(false);idle=null;}
        Session retired=session;session=null;if(retired==null)return;
        retired.source.cancel();
        cleanup.execute(()->{retired.source.abortNetwork();retired.close();});
    }
    private void decode(Request request){
        String result="";boolean ok=false;Bitmap bitmap=null;
        try{
            if(request.cancelled)return;
            synchronized(request.session){
                if(request.cancelled)return;request.session.prepare(request);
                if(request.cancelled)return;request.stage="frame";long start=System.nanoTime();
                bitmap=request.session.retriever.getScaledFrameAtTime(request.position*1000,MediaMetadataRetriever.OPTION_CLOSEST_SYNC,320,180);
                request.frameMs=(System.nanoTime()-start)/1000000;
                if(bitmap==null||request.cancelled)return;
                ByteArrayOutputStream bytes=new ByteArrayOutputStream();bitmap.compress(Bitmap.CompressFormat.JPEG,78,bytes);
                result=metrics(request,true).put("generation",request.generation).put("position",request.position)
                    .put("image",Base64.encodeToString(bytes.toByteArray(),Base64.NO_WRAP)).toString();ok=true;
            }
        }catch(Exception|OutOfMemoryError ignored){
            request.reason=ignored instanceof OutOfMemoryError?"memory":"decode_or_io";
            // Never expose exception messages: they can contain media URLs or credentials.
        }finally{
            if(bitmap!=null)bitmap.recycle();
            synchronized(this){
                request.timeout.cancel(false);
                if(current==request&&!request.cancelled){
                    request.result=ok?result:failure(request);
                    if(!ok)retireSession();
                    else idle=timer.schedule(()->expireIdle(request.session),5,TimeUnit.SECONDS);
                }
            }
        }
    }
    private JSONObject metrics(Request request,boolean ok)throws Exception {
        ThumbnailDataSource source=request.session.source;
        return new JSONObject().put("ok",ok).put("stage",request.stage).put("reason",ok?"":source.failure.isEmpty()?request.reason:source.failure)
            .put("readBytes",source.readBytes).put("readCalls",source.readCalls).put("prepareMs",request.prepareMs).put("frameMs",request.frameMs);
    }
    private String failure(Request request){try{return metrics(request,false).toString();}catch(Exception ignored){return "{\"ok\":false}";}}
    private synchronized void expire(Request request){
        if(current!=request)return;request.cancelled=true;request.reason="timeout";request.result=failure(request);
        if(session==request.session)retireSession();
    }
    private synchronized void expireIdle(Session expected){if(session==expected&&(current==null||!current.result.isEmpty()))retireSession();}
    private static final class Session {
        final Context context;final String location,token;final ThumbnailDataSource source=new ThumbnailDataSource();
        MediaMetadataRetriever retriever;
        Session(Context context,String location,String token){this.context=context;this.location=location;this.token=token;}
        void prepare(Request request)throws Exception {
            long start=System.nanoTime();
            if(retriever==null){
                source.open(context,location,token);request.stage="index";
                retriever=new MediaMetadataRetriever();retriever.setDataSource(source);
            }else source.beginRequest();
            request.prepareMs=(System.nanoTime()-start)/1000000;
        }
        synchronized void close(){
            if(retriever!=null)try{retriever.release();}catch(Exception ignored){}retriever=null;source.close();
        }
    }
    private static final class Request {
        final int id,generation;final long position;final Session session;
        volatile boolean cancelled;String result="";volatile String stage="open",reason="no_frame";
        long prepareMs,frameMs;ScheduledFuture<?> timeout;
        Request(int id,int generation,long position,Session session){this.id=id;this.generation=generation;this.position=position;this.session=session;}
    }
}