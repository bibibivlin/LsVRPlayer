package com.linxyi.vrplayer;

import androidx.media3.common.C;
import androidx.media3.common.Format;
import androidx.media3.common.MimeTypes;
import androidx.media3.decoder.DecoderInputBuffer;
import androidx.media3.exoplayer.BaseRenderer;
import androidx.media3.exoplayer.FormatHolder;
import androidx.media3.exoplayer.RendererCapabilities;
import androidx.media3.exoplayer.source.MediaSource;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Locale;
import java.util.concurrent.atomic.AtomicInteger;

/** Preserve Matroska ASS packets for libass instead of flattening them to text.
 * The extractor supplies sample-relative Dialogue lines and the codec-private
 * script header. Only timestamps are remuxed; override tags remain untouched.
 * All mutable queue work is on the playback thread; State is immutable. */
final class AssTextRenderer extends BaseRenderer {
    static final class State {
        final int stream,revision;
        final String script;
        State(int stream,int revision,String script){this.stream=stream;this.revision=revision;this.script=script;}
    }
    private static final AtomicInteger streams=new AtomicInteger();
    private final DecoderInputBuffer input=new DecoderInputBuffer(DecoderInputBuffer.BUFFER_REPLACEMENT_MODE_NORMAL);
    private final ArrayList<AssPacket.Event> events=new ArrayList<>();
    private volatile State published=new State(0,0,"");
    private String header="",format="";
    private boolean inputEnded,pending;
    private int stream,revision,bytes;
    State state(){return published;}
    AssTextRenderer(){super(C.TRACK_TYPE_TEXT);}
    @Override public String getName(){return "VRPlayerLibass";}
    @Override public int supportsFormat(Format value){return RendererCapabilities.create(MimeTypes.TEXT_SSA.equals(value.sampleMimeType)?C.FORMAT_HANDLED:C.FORMAT_UNSUPPORTED_TYPE);}
    @Override public boolean isReady(){return true;}
    @Override public boolean isEnded(){return inputEnded;}
    @Override protected void onStreamChanged(Format[] formats,long startPositionUs,long offsetUs,MediaSource.MediaPeriodId period){
        // Reenabling an existing SampleStream need not emit FORMAT_READ again.
        // Codec-private styles are available here on every track attachment.
        if(formats.length>0)configure(formats[0]);
    }
    @Override protected void onPositionReset(long positionUs,boolean joining,boolean keyframe){clear();}
    @Override protected void onDisabled(){clear();header=format="";published=new State(0,++revision,"");}
    private void clear(){events.clear();bytes=0;inputEnded=pending=false;input.clear();stream=streams.incrementAndGet();publish();}
    private void configure(Format value){
        header=format="";
        if(value.initializationData.size()>=2){
            format=new String(value.initializationData.get(0),StandardCharsets.UTF_8);
            header=new String(value.initializationData.get(1),StandardCharsets.UTF_8);
            int section=header.toLowerCase(Locale.ROOT).indexOf("[events]");
            if(section>=0)header=header.substring(0,section);
            if(header.length()>1024*1024)header="";
        }
        clear();
    }
    private void publish(){
        StringBuilder script=new StringBuilder(header).append("\n[Events]\n")
            .append("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");
        for(AssPacket.Event event:events)script.append(event.dialogue).append('\n');
        published=new State(stream,++revision,events.isEmpty()?"":script.toString());
    }
    @Override public void render(long positionUs,long elapsedRealtimeUs){
        long positionMs=(positionUs-getStreamOffsetUs())/1000;
        boolean changed=false;
        for(int i=events.size()-1;i>=0;i--)if(events.get(i).endMs<positionMs-12000){bytes-=events.remove(i).dialogue.length();changed=true;}
        for(int count=0;count<16&&!inputEnded;count++){
            if(!pending){
                input.clear();FormatHolder holder=getFormatHolder();int result=readSource(holder,input,0);
                if(result==C.RESULT_NOTHING_READ)break;
                if(result==C.RESULT_FORMAT_READ){configure(holder.format);continue;}
                if(input.isEndOfStream()){inputEnded=true;break;}
                input.flip();pending=true;
            }
            if(input.timeUs-getStreamOffsetUs()>positionUs-getStreamOffsetUs()+20000000L)break;
            if(input.data!=null&&input.data.remaining()<=256*1024&&!header.isEmpty()){
                byte[] packet=new byte[input.data.remaining()];input.data.get(packet);
                AssPacket.Event event=AssPacket.parse(new String(packet,StandardCharsets.UTF_8),format,(input.timeUs-getStreamOffsetUs())/1000);
                if(event!=null&&event.endMs>=positionMs-12000&&events.size()<512&&bytes+event.dialogue.length()<=2*1024*1024){events.add(event);bytes+=event.dialogue.length();changed=true;}
            }
            pending=false;
        }
        if(changed)publish();
    }
}
