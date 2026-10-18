package com.linxyi.vrplayer;

import androidx.media3.common.C;
import androidx.media3.common.Format;
import androidx.media3.common.MimeTypes;
import androidx.media3.decoder.DecoderInputBuffer;
import androidx.media3.exoplayer.FormatHolder;
import androidx.media3.exoplayer.RendererConfiguration;
import androidx.media3.exoplayer.source.MediaSource;
import androidx.media3.exoplayer.source.SampleStream;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import org.junit.Test;
import static org.junit.Assert.*;

public final class AssTextRendererTest {
    private static final String FIELDS="Format: Start, End, ReadOrder, Layer, Style, Name, MarginL, MarginR, MarginV, Effect, Text";
    private static final Format FORMAT=new Format.Builder().setSampleMimeType(MimeTypes.TEXT_SSA)
        .setInitializationData(Arrays.asList(FIELDS.getBytes(StandardCharsets.UTF_8),
            "[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\n".getBytes(StandardCharsets.UTF_8))).build();
    private static final class PacketStream implements SampleStream {
        private boolean formatPending,samplePending=true;
        private final String text;
        PacketStream(boolean emitFormat,String text){formatPending=emitFormat;this.text=text;}
        @Override public boolean isReady(){return true;}
        @Override public void maybeThrowError(){}
        @Override public int skipData(long positionUs){return 0;}
        @Override public int readData(FormatHolder holder,DecoderInputBuffer buffer,int flags){
            if(formatPending){formatPending=false;holder.format=FORMAT;return C.RESULT_FORMAT_READ;}
            if(!samplePending)return C.RESULT_NOTHING_READ;
            samplePending=false;
            byte[] bytes=("Dialogue: 0:00:00:00,0:00:08:00,1,2,Default,,0,0,0,,{\\pos(100,100)}"+text).getBytes(StandardCharsets.UTF_8);
            buffer.ensureSpaceForWrite(bytes.length);buffer.data.put(bytes);buffer.timeUs=1000000;return C.RESULT_BUFFER_READ;
        }
    }
    private static void enable(AssTextRenderer renderer,PacketStream stream) throws Exception {
        renderer.enable(RendererConfiguration.DEFAULT,new Format[]{FORMAT},stream,2000000,false,false,2000000,0,new MediaSource.MediaPeriodId("fixture"));
        renderer.render(2000000,0);
    }
    @Test public void reenablingWithoutFormatReadRestoresStylesAndEvents() throws Exception {
        AssTextRenderer renderer=new AssTextRenderer();enable(renderer,new PacketStream(true,"Original"));
        assertTrue(renderer.state().script.contains("Original"));renderer.disable();assertEquals("",renderer.state().script);
        enable(renderer,new PacketStream(false,"Restored"));
        assertTrue(renderer.state().script.contains("ScriptType: v4.00+"));
        assertTrue(renderer.state().script.contains("{\\pos(100,100)}Restored"));renderer.disable();
    }
    @Test public void replacementTrackCannotRetainPreviousEvents() throws Exception {
        AssTextRenderer renderer=new AssTextRenderer();enable(renderer,new PacketStream(true,"Old track"));int previous=renderer.state().stream;
        renderer.replaceStream(new Format[]{FORMAT},new PacketStream(false,"New track"),0,0,new MediaSource.MediaPeriodId("replacement"));renderer.render(2000000,0);
        assertNotEquals(previous,renderer.state().stream);assertFalse(renderer.state().script.contains("Old track"));assertTrue(renderer.state().script.contains("New track"));renderer.disable();
    }
}
