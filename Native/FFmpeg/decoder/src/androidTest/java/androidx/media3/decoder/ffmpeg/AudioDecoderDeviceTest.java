package androidx.media3.decoder.ffmpeg;

import static org.junit.Assert.*;
import android.content.Context;
import androidx.media3.common.Format;
import androidx.media3.common.MimeTypes;
import androidx.media3.decoder.DecoderInputBuffer;
import androidx.media3.decoder.SimpleDecoderOutputBuffer;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import org.junit.Test;
import org.junit.runner.RunWith;

/** Generated sine packets only. This isolated test APK never reads the player's settings or media. */
@RunWith(AndroidJUnit4.class)
public final class AudioDecoderDeviceTest {
    // Media3 exposes LIBAVCODEC_IDENT, not the FFmpeg release number.
    @Test public void pinnedArm64LibraryLoads(){assertTrue(FfmpegLibrary.isAvailable());assertEquals("Lavc62.28.103",FfmpegLibrary.getVersion());}
    @Test public void commonAudioFallbacksRemainAvailable(){
        for(String mime:new String[]{MimeTypes.AUDIO_AC3,MimeTypes.AUDIO_E_AC3,MimeTypes.AUDIO_DTS,MimeTypes.AUDIO_TRUEHD,
                MimeTypes.AUDIO_FLAC,MimeTypes.AUDIO_OPUS,MimeTypes.AUDIO_VORBIS,MimeTypes.AUDIO_AAC,MimeTypes.AUDIO_MPEG})
            assertTrue(mime,FfmpegLibrary.supportsFormat(mime));
    }
    @Test public void softwareVideoRendererIsNotInstalled()throws Exception{
        try{Class.forName("androidx.media3.decoder.ffmpeg.ExperimentalFfmpegVideoRenderer");fail("Software video must stay disabled");}
        catch(ClassNotFoundException expected){}
    }
    @Test public void ac3PacketProducesPcmAndSurvivesReset()throws Exception{decode("ac3",MimeTypes.AUDIO_AC3);}
    @Test public void eac3PacketProducesPcmAndSurvivesReset()throws Exception{decode("eac3",MimeTypes.AUDIO_E_AC3);}
    @Test public void dtsPacketProducesPcmAndSurvivesReset()throws Exception{decode("dts",MimeTypes.AUDIO_DTS);}
    private static void decode(String fixture,String mime)throws Exception{
        Context context=InstrumentationRegistry.getInstrumentation().getContext();byte[] packet;
        try(java.io.InputStream source=context.getAssets().open("audio-"+fixture+".bin")){
            java.io.ByteArrayOutputStream bytes=new java.io.ByteArrayOutputStream();byte[] buffer=new byte[4096];int count;
            while((count=source.read(buffer))!=-1)bytes.write(buffer,0,count);packet=bytes.toByteArray();
        }
        Format format=new Format.Builder().setSampleMimeType(mime).setSampleRate(48000).setChannelCount(2).build();
        FfmpegAudioDecoder decoder=new FfmpegAudioDecoder(format,2,2,packet.length+64,false);
        try{
            for(boolean reset:new boolean[]{false,true}){
                long energy=0;
                for(int offset=0;offset<packet.length;){
                    // The DCA encoder emits two core frames per packet. Media3's extractor
                    // splits them; the JNI decoder expects one access unit per call.
                    int size=fixture.equals("dts")?(((packet[offset+5]&3)<<12)|((packet[offset+6]&255)<<4)|((packet[offset+7]&240)>>4))+1:packet.length;
                    assertTrue(offset+size<=packet.length);
                    DecoderInputBuffer input=new DecoderInputBuffer(DecoderInputBuffer.BUFFER_REPLACEMENT_MODE_DIRECT,FfmpegLibrary.getInputBufferPaddingSize());
                    input.ensureSpaceForWrite(size);input.data.put(packet,offset,size);input.flip();
                    SimpleDecoderOutputBuffer output=new SimpleDecoderOutputBuffer(value->{});
                    assertNull(decoder.decode(input,output,reset&&offset==0));assertFalse(output.shouldBeSkipped);
                    ByteBuffer pcm=output.data.order(ByteOrder.nativeOrder());assertTrue(pcm.remaining()>=1024);
                    while(pcm.remaining()>=2)energy+=Math.abs((int)pcm.getShort());
                    offset+=size;
                }
                assertTrue("Decoded "+fixture+" reset="+reset+" PCM energy="+energy,energy>100000);
                assertEquals(48000,decoder.getSampleRate());assertEquals(2,decoder.getChannelCount());
            }
        }finally{decoder.release();}
    }
}
