package com.linxyi.vrplayer;

import android.net.Uri;
import androidx.media3.common.C;
import androidx.media3.datasource.BaseDataSource;
import androidx.media3.datasource.DataSpec;
import java.io.*;
import java.net.*;
import java.util.*;

/** Each source captures one playback session and token; no mutable global HTTP headers. */
final class EmbyDataSource extends BaseDataSource implements AutoCloseable {
    private final PlaybackSession session;private final String token,location;
    private final AutoCloseable cancellation=this::cancelNetwork;
    private volatile HttpURLConnection connection;private volatile EmbyHttpTransport.Response response;
    private volatile boolean closed;private boolean opened;private long remaining;private Uri uri;
    EmbyDataSource(PlaybackSession session,String location,String token){super(true);this.session=session;this.token=token;this.location=location;}
    @Override public long open(DataSpec spec)throws IOException {
        close();closed=false;session.check();session.attach(cancellation);transferInitializing(spec);
        if(spec.httpMethod!=DataSpec.HTTP_METHOD_GET)throw new IOException("Unsupported media request");
        try {
            EmbyHttpTransport.Response current=EmbyHttpTransport.open(EmbyHttpTransport.location(location),spec.uri.toString(),token,spec.position,spec.length,spec.httpRequestHeaders,15000,value->{connection=value;if(closed){value.disconnect();throw new InterruptedIOException("Playback cancelled");}session.check();});
            response=current;if(closed){current.close();throw new InterruptedIOException("Playback cancelled");}
            remaining=current.remaining;uri=Uri.parse(current.uri.toString());opened=true;transferStarted(spec);return remaining;
        }catch(IOException|RuntimeException ex){close();throw ex;}
    }
    @Override public int read(byte[] buffer,int offset,int length)throws IOException {
        if(length==0)return 0;session.check();if(remaining==0)return C.RESULT_END_OF_INPUT;
        EmbyHttpTransport.Response current=response;if(current==null)throw new InterruptedIOException("Playback cancelled");
        int count=current.input.read(buffer,offset,remaining<0?length:(int)Math.min(length,remaining));
        if(count<0){if(remaining>0)throw new EOFException("Incomplete media response");return C.RESULT_END_OF_INPUT;}
        if(remaining>0)remaining-=count;bytesTransferred(count);return count;
    }
    @Override public Uri getUri(){return uri;}
    @Override public Map<String,List<String>> getResponseHeaders(){EmbyHttpTransport.Response current=response;return current==null?Collections.emptyMap():current.connection.getHeaderFields();}
    @Override public void close(){
        cancelNetwork();response=null;connection=null;session.detach(cancellation);
        if(opened){opened=false;transferEnded();}uri=null;
    }
    private void cancelNetwork(){
        closed=true;EmbyHttpTransport.Response current=response;HttpURLConnection network=connection;
        if(current!=null)current.close();else if(network!=null)network.disconnect();
    }
}
