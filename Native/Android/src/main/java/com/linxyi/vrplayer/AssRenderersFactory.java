package com.linxyi.vrplayer;

import android.content.Context;
import android.os.Looper;
import androidx.media3.common.Format;
import androidx.media3.common.MimeTypes;
import androidx.media3.exoplayer.DefaultRenderersFactory;
import androidx.media3.exoplayer.Renderer;
import androidx.media3.exoplayer.text.TextOutput;
import androidx.media3.extractor.DefaultExtractorsFactory;
import androidx.media3.extractor.text.DefaultSubtitleParserFactory;
import androidx.media3.extractor.text.SubtitleParser;
import java.util.ArrayList;

final class AssRenderersFactory extends DefaultRenderersFactory {
    final AssTextRenderer ass=new AssTextRenderer();
    AssRenderersFactory(Context context){super(context);}
    @Override protected void buildTextRenderers(Context context,TextOutput output,Looper looper,int extensionMode,ArrayList<Renderer> out){
        out.add(ass);super.buildTextRenderers(context,output,looper,extensionMode,out);
    }
    static DefaultExtractorsFactory extractors(){
        SubtitleParser.Factory delegate=new DefaultSubtitleParserFactory();
        return new DefaultExtractorsFactory().setSubtitleParserFactory(new SubtitleParser.Factory(){
            @Override public boolean supportsFormat(Format format){return !MimeTypes.TEXT_SSA.equals(format.sampleMimeType)&&delegate.supportsFormat(format);}
            @Override public int getCueReplacementBehavior(Format format){return delegate.getCueReplacementBehavior(format);}
            @Override public SubtitleParser create(Format format){return delegate.create(format);}
        });
    }
}
