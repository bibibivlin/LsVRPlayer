package com.linxyi.vrplayer;

import android.app.Activity;
import android.content.Intent;
import android.graphics.SurfaceTexture;
import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.os.Handler;
import android.os.Looper;
import android.view.Surface;
import android.net.Uri;
import android.util.Log;
import androidx.media3.common.*;
import androidx.media3.common.text.Cue;
import androidx.media3.common.text.CueGroup;
import androidx.media3.datasource.*;
import androidx.media3.exoplayer.*;
import androidx.media3.exoplayer.analytics.AnalyticsListener;
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory;
import org.json.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;

public final class PlayerBridge {
    public static Activity activity;
    public static volatile String pickerResult="";
    private static final Handler main=new Handler(Looper.getMainLooper());
    private static final ExecutorService io=Executors.newFixedThreadPool(3);
    private static final AtomicInteger requestSequence=new AtomicInteger();
    private static final ConcurrentHashMap<Integer,FutureTask<Void>> requests=new ConcurrentHashMap<>();
    private static final ConcurrentHashMap<Integer,String> results=new ConcurrentHashMap<>();
    private static final ConcurrentHashMap<Integer,AutoCloseable> requestResources=new ConcurrentHashMap<>();
    private static final ThreadLocal<Integer> currentRequest=new ThreadLocal<>();
    private static ExoPlayer player;
    private static Surface videoSurface;
    private static volatile String snapshot="{}";
    private static String error="",decoder="",audioDecoder="",embeddedSubtitle="";
    private static int dropped=0,generation=0;
    private static boolean running=false;
    private static volatile boolean formatProbeEnabled=true;
    private static int probeId,probeRun;
    private static boolean probeComplete;
    private static FormatMetadata.Hints probeHints=new FormatMetadata.Hints(),projectionHints=new FormatMetadata.Hints();
    private static Format lastProjectionFormat;
    private static final ScheduledExecutorService probeTimer=Executors.newSingleThreadScheduledExecutor();
    static {System.loadLibrary("vrplayerrender");nativeBootstrap();}
    private static native void nativeBootstrap();
    private static native void nativeFrameAvailable();

    public static void initialize(Activity host){
        activity=host;SmbStore.initialize(host);
        main.post(()->{
            if(player!=null)return;
            DefaultDataSource.Factory defaults=new DefaultDataSource.Factory(host);
            DataSource.Factory factory=()->new RoutingDataSource(defaults);
            DefaultLoadControl loadControl=new DefaultLoadControl.Builder().setBufferDurationsMs(12000,45000,800,2500).setTargetBufferBytes(64*1024*1024).setPrioritizeTimeOverSizeThresholds(false).build();
            player=new ExoPlayer.Builder(host,new DefaultRenderersFactory(host).setEnableDecoderFallback(true).setExtensionRendererMode(DefaultRenderersFactory.EXTENSION_RENDERER_MODE_ON)).setMediaSourceFactory(new DefaultMediaSourceFactory(host).setDataSourceFactory(factory)).setLoadControl(loadControl).build();
            player.setAudioAttributes(new AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MOVIE).build(),true);
            if(videoSurface!=null)player.setVideoSurface(videoSurface);
            player.addListener(new Player.Listener(){
                @Override public void onPlayerError(PlaybackException value){error=value.getErrorCodeName();Log.e("VRPlayer","Playback error "+error);}
                @Override public void onCues(CueGroup group){StringBuilder text=new StringBuilder();for(Cue cue:group.cues){if(cue.text!=null){if(text.length()>0)text.append('\n');text.append(cue.text);}}embeddedSubtitle=text.toString();}
            });
            player.addAnalyticsListener(new AnalyticsListener(){
                @Override public void onVideoDecoderInitialized(EventTime time,String name,long timestamp,long duration){decoder=name;Log.i("VRPlayer","Decoder: "+name);}
                @Override public void onAudioDecoderInitialized(EventTime time,String name,long timestamp,long duration){audioDecoder=name;Log.i("VRPlayer","Audio decoder: "+name);}
                @Override public void onVideoDecoderReleased(EventTime time,String name){if(name.equals(decoder))decoder="";}
                @Override public void onAudioDecoderReleased(EventTime time,String name){if(name.equals(audioDecoder))audioDecoder="";}
                @Override public void onDroppedVideoFrames(EventTime time,int count,long elapsed){dropped+=count;}
            });
            running=true;main.post(update);
        });
    }
    // Called on Unity's render thread. ExoPlayer and Surface listeners belong to the Android main looper.
    public static void setVideoSurface(Surface surface,SurfaceTexture texture){main.post(()->{videoSurface=surface;if(texture!=null)texture.setOnFrameAvailableListener(value->nativeFrameAvailable(),main);if(player!=null)player.setVideoSurface(surface);});}
    public static void useCompositionSurface(Surface surface){main.post(()->{videoSurface=surface;if(player!=null)player.setVideoSurface(surface);});}
    public static void open(String uri,long position,int token){main.post(()->{
        // Media3 may reuse the current decoder when opening a compatible movie.
        // Keep its name until the actual decoder-release event arrives.
        if(player==null)return;generation=token;error="";dropped=0;embeddedSubtitle="";
        cancelProbe();probeHints=new FormatMetadata.Hints();projectionHints=new FormatMetadata.Hints();lastProjectionFormat=null;probeComplete=false;
        player.setMediaItem(MediaItem.fromUri(uri),Math.max(0,position));player.prepare();player.play();if(formatProbeEnabled)startProbe(uri);
    });}
    public static void play(){main.post(()->{if(player!=null)player.play();});}
    public static void pause(){main.post(()->{if(player!=null)player.pause();});}
    public static void stop(){main.post(()->{if(player!=null){cancelProbe();player.stop();player.clearMediaItems();embeddedSubtitle="";error="";}});}
    public static void seek(long millis){main.post(()->{if(player!=null){embeddedSubtitle="";player.seekTo(Math.max(0,millis));}});}
    public static void volume(float value){main.post(()->{if(player!=null)player.setVolume(Math.max(0,Math.min(1,value)));});}
    public static void speed(float value){main.post(()->{if(player!=null)player.setPlaybackSpeed(Math.max(.25f,Math.min(3,value)));});}
    public static void repeat(boolean value){main.post(()->{if(player!=null)player.setRepeatMode(value?Player.REPEAT_MODE_ONE:Player.REPEAT_MODE_OFF);});}
    public static void selectTrack(String id,int type){main.post(()->{
        if(player==null)return;
        try{
            TrackSelectionParameters.Builder parameters=player.getTrackSelectionParameters().buildUpon().clearOverridesOfType(type).setTrackTypeDisabled(type,id.equals("off"));
            if(!id.equals("off")&&!id.equals("auto")){String[] parts=id.split(":");Tracks.Group group=player.getCurrentTracks().getGroups().get(Integer.parseInt(parts[0]));parameters.setOverrideForType(new TrackSelectionOverride(group.getMediaTrackGroup(),Integer.parseInt(parts[1])));}
            player.setTrackSelectionParameters(parameters.build());embeddedSubtitle="";
        }catch(Exception ex){error="无法选择音轨或字幕轨";}
    });}
    public static String getSnapshot(){return snapshot;}
    public static String getLaunchUri(){return activity.getIntent().getStringExtra("video");}
    public static String getLaunchMode(){return activity.getIntent().getStringExtra("projection");}
    public static void release(){SmbDiscovery.stopAll();main.post(()->{running=false;main.removeCallbacks(update);if(player!=null){cancelProbe();player.release();player=null;}videoSurface=null;});for(Integer id:requests.keySet())cancelRequest(id);}
    private static final Runnable update=new Runnable(){@Override public void run(){
        if(!running||player==null)return;
        try{
            VideoSize size=player.getVideoSize();Format format=player.getVideoFormat();
            if(probeId>0){String result=takeResult(probeId);if(!result.isEmpty()){probeId=0;probeComplete=true;JSONObject json=new JSONObject(result);if(json.optBoolean("ok"))probeHints=readHints(json);}}
            if(format!=lastProjectionFormat){lastProjectionFormat=format;projectionHints=new FormatMetadata.Hints();
                if(format!=null&&format.projectionData!=null)try{projectionHints=FormatMetadata.projectionData(format.projectionData);}catch(Exception ignored){}
            }
            JSONObject info=new JSONObject().put("generation",generation).put("state",player.getPlaybackState()).put("playing",player.isPlaying()).put("position",Math.max(0,player.getCurrentPosition())).put("duration",Math.max(0,player.getDuration())).put("buffered",Math.max(0,player.getBufferedPosition())).put("width",size.width).put("height",size.height).put("pixelRatio",size.pixelWidthHeightRatio).put("error",error).put("decoder",decoder).put("dropped",dropped).put("embeddedSubtitle",embeddedSubtitle);
            info.put("audioDecoder",audioDecoder).put("formatProbeComplete",probeComplete).put("formatHints",hintsJson(mergeHints(probeHints,projectionHints)));
            if(format!=null)info.put("codec",format.sampleMimeType).put("fps",format.frameRate).put("bitrate",format.bitrate).put("stereoMode",format.stereoMode);
            JSONArray tracks=new JSONArray();int groupIndex=0;
            for(Tracks.Group group:player.getCurrentTracks().getGroups()){
                if(group.getType()==C.TRACK_TYPE_AUDIO||group.getType()==C.TRACK_TYPE_TEXT){for(int i=0;i<group.length;i++){
                    Format track=group.getTrackFormat(i);tracks.put(new JSONObject().put("id",groupIndex+":"+i).put("type",group.getType()).put("name",(track.label==null?"":track.label+" ")+(track.language==null?"und":track.language)+" · "+(track.sampleMimeType==null?"":track.sampleMimeType)).put("selected",group.isTrackSelected(i)).put("supported",group.isTrackSupported(i)));
                }}groupIndex++;
            }
            snapshot=info.put("tracks",tracks).toString();
        }catch(Exception ex){Log.w("VRPlayer","Snapshot unavailable: "+ex.getClass().getSimpleName());}
        main.postDelayed(this,150);
    }};
    public static void setFormatProbeEnabled(boolean value){formatProbeEnabled=value;}
    public static void probeCurrentFormat(){main.post(()->{if(player!=null&&player.getCurrentMediaItem()!=null&&player.getCurrentMediaItem().localConfiguration!=null){cancelProbe();probeHints=new FormatMetadata.Hints();startProbe(player.getCurrentMediaItem().localConfiguration.uri.toString());}});}
    private static void cancelProbe(){int id=probeId;probeId=0;probeRun++;if(id>0)io.execute(()->cancelRequest(id));}
    private static void startProbe(String uri){
        int run=++probeRun;probeComplete=false;FormatProbe probe=new FormatProbe(activity,uri);
        int id=request(()->hintsJson(probe.run()),probe);probeId=id;
        probeTimer.schedule(()->{cancelRequest(id);main.post(()->{if(run==probeRun&&probeId==id){probeId=0;probeComplete=true;}});},3,TimeUnit.SECONDS);
    }
    private static JSONObject hintsJson(FormatMetadata.Hints hints)throws JSONException{return new JSONObject().put("projection",hints.projection).put("stereo",hints.stereo).put("half",hints.half).put("swapEyes",hints.swapEyes).put("blockedFields",hints.blockedFields).put("unsupported",hints.unsupported).put("reason",hints.reason);}
    private static FormatMetadata.Hints readHints(JSONObject json){FormatMetadata.Hints hints=new FormatMetadata.Hints();hints.projection=json.optInt("projection",-1);hints.stereo=json.optInt("stereo",-1);hints.half=json.optInt("half",-1);hints.swapEyes=json.optInt("swapEyes",-1);hints.blockedFields=json.optInt("blockedFields");hints.unsupported=json.optString("unsupported");return hints;}
    private static FormatMetadata.Hints mergeHints(FormatMetadata.Hints a,FormatMetadata.Hints b){
        FormatMetadata.Hints hints=new FormatMetadata.Hints();hints.projection=a.projection>=0?a.projection:b.projection;hints.stereo=a.stereo>=0?a.stereo:b.stereo;hints.half=a.half;hints.swapEyes=a.swapEyes;
        hints.blockedFields=a.blockedFields|b.blockedFields;hints.unsupported=a.unsupported.isEmpty()?b.unsupported:a.unsupported;
        if(a.projection>=0&&b.projection>=0&&a.projection!=b.projection)hints.block(1,"投影元数据冲突");
        if(a.stereo>=0&&b.stereo>=0&&a.stereo!=b.stereo)hints.block(2,"立体元数据冲突");return hints;
    }
    interface Work {JSONObject run()throws Exception;}
    private static int request(Work work){return request(work,null);}
    private static int request(Work work,AutoCloseable resource){
        int id=requestSequence.incrementAndGet();
        if(resource!=null)requestResources.put(id,resource);
        FutureTask<Void> task=new FutureTask<>(()->{
            currentRequest.set(id);
            JSONObject result;
            try{result=work.run().put("ok",true);}catch(Exception ex){result=new JSONObject();try{result.put("ok",false).put("error",safeError(ex)).put("code",ex.getClass().getSimpleName().contains("Authentication")?"authentication":"unavailable");}catch(Exception ignored){}}
            finally{closeRequest(id);currentRequest.remove();}
            if(!Thread.currentThread().isInterrupted()&&requests.containsKey(id))results.put(id,result.toString());return null;
        });requests.put(id,task);io.execute(task);return id;
    }
    public static int beginList(String location){return request(()->FileAccess.list(activity,location));}
    public static int beginText(String location,String encoding){return request(()->new JSONObject().put("text",FileAccess.readText(activity,location,encoding)));}
    public static String takeResult(int id){String value=results.remove(id);if(value!=null)requests.remove(id);return value==null?"":value;}
    public static void cancelRequest(int id){FutureTask<Void> task=requests.remove(id);if(task!=null)task.cancel(true);closeRequest(id);results.remove(id);}
    private static void closeRequest(int id){AutoCloseable resource=requestResources.remove(id);if(resource!=null)try{resource.close();}catch(Exception ignored){}}
    static void attachResource(AutoCloseable resource){Integer id=currentRequest.get();if(id==null)return;requestResources.compute(id,(key,previous)->()->{if(previous!=null)previous.close();resource.close();});if(!requests.containsKey(id)||Thread.currentThread().isInterrupted())closeRequest(id);}
    public static int beginDiscovery(){return SmbDiscovery.begin(activity);}
    public static String pollDiscovery(int id){return SmbDiscovery.poll(id);}
    public static void stopDiscovery(int id){SmbDiscovery.stop(id);}
    public static int beginShares(String json){try{SmbShares shares=new SmbShares(json);return request(shares::run,shares);}catch(Exception ex){return request(()->{throw new IllegalArgumentException("SMB settings");});}}
    public static String saveServer(String json){try{return new JSONObject().put("ok",true).put("id",SmbStore.save(json)).toString();}catch(Exception ex){return "{\"ok\":false,\"error\":\"无法保存服务器，请检查填写内容\"}";}}
    public static String listServers(){return "{\"servers\":"+SmbStore.list()+"}";}
    public static void removeServer(String id){try{SmbStore.remove(id);}catch(Exception ignored){}}
    public static void pickFolder(){pickerResult="";main.post(()->{try{activity.startActivity(new Intent(activity,FolderPickerActivity.class));}catch(Exception ex){pickerResult="ERROR:无法打开目录选择器";}});}
    public static String takeFolder(){String value=pickerResult;pickerResult="";return value;}
    public static String safeError(Exception error){
        String name=error.getClass().getSimpleName();
        if(name.contains("Authentication"))return "SMB 身份验证失败，请检查账号和密码";
        if(name.contains("Transport")||name.contains("Timeout")||name.contains("Connect"))return "网络连接失败或超时，请检查服务器和 Wi-Fi";
        if(name.contains("Security"))return "没有目录权限，请重新授权";
        if(name.contains("SMBApi"))return "SMB 拒绝访问，请检查共享名、权限和账号";
        if(error instanceof IllegalArgumentException)return "路径或参数无效";
        if(error instanceof java.io.FileNotFoundException)return "文件不存在或无法读取";
        return "无法读取文件（"+name+"），请检查权限、路径和网络";
    }
    public static String capabilities(){JSONArray output=new JSONArray();for(MediaCodecInfo codec:new MediaCodecList(MediaCodecList.REGULAR_CODECS).getCodecInfos()){
        if(codec.isEncoder())continue;for(String type:codec.getSupportedTypes()){if(!type.startsWith("video/"))continue;try{MediaCodecInfo.VideoCapabilities video=codec.getCapabilitiesForType(type).getVideoCapabilities();output.put(new JSONObject().put("name",codec.getName()).put("mime",type).put("hardware",codec.isHardwareAccelerated()).put("widths",video.getSupportedWidths().toString()).put("heights",video.getSupportedHeights().toString()).put("rates",video.getSupportedFrameRates().toString()));}catch(Exception ignored){}}
    }return "{\"decoders\":"+output+"}";}
    private static final class RoutingDataSource implements DataSource {
        private final DefaultDataSource.Factory defaults;private DataSource current;private final List<TransferListener> listeners=new ArrayList<>();
        RoutingDataSource(DefaultDataSource.Factory defaults){this.defaults=defaults;}
        public void addTransferListener(TransferListener listener){listeners.add(listener);if(current!=null)current.addTransferListener(listener);}
        public long open(DataSpec spec)throws java.io.IOException{current="smb".equals(spec.uri.getScheme())?new SmbDataSource():defaults.createDataSource();for(TransferListener listener:listeners)current.addTransferListener(listener);return current.open(spec);}
        public int read(byte[] buffer,int offset,int length)throws java.io.IOException{return current.read(buffer,offset,length);}
        public Uri getUri(){return current==null?null:current.getUri();}
        public void close()throws java.io.IOException{if(current!=null){try{current.close();}finally{current=null;}}}
    }
}
