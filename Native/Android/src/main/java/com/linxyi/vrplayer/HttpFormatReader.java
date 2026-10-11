package com.linxyi.vrplayer;

import java.io.*;
import java.net.*;
import java.util.Collections;
import java.util.concurrent.atomic.AtomicBoolean;

/** Independent small range requests; cancellation never closes the playback connection. */
final class HttpFormatReader implements FormatMetadata.Reader {
    private final String location,token;private final URI origin;
    private final AtomicBoolean cancelled=new AtomicBoolean();
    private volatile HttpURLConnection connection;private long length=-1;
    HttpFormatReader(String location,String token)throws IOException {this.location=location;this.token=token;origin=EmbyHttpTransport.location(location);}
    private void check()throws InterruptedIOException {if(cancelled.get()||Thread.currentThread().isInterrupted())throw new InterruptedIOException("Metadata cancelled");}
    private EmbyHttpTransport.Response open(long offset,int count)throws IOException {
        check();return EmbyHttpTransport.open(origin,location,token,offset,count,Collections.emptyMap(),2500,value->{connection=value;if(cancelled.get()){value.disconnect();throw new InterruptedIOException("Metadata cancelled");}check();});
    }
    public long length()throws IOException {
        if(length>=0)return length;
        try(EmbyHttpTransport.Response response=open(0,1)){length=response.totalLength;if(length<0)throw new IOException("Unknown metadata length");return length;}finally{connection=null;}
    }
    public byte[] read(long offset,int count)throws IOException {
        if(count==0)return new byte[0];
        try(EmbyHttpTransport.Response response=open(offset,count)){
            byte[] bytes=new byte[count];int done=0;
            while(done<count){check();int size=response.input.read(bytes,done,count-done);if(size<0)throw new EOFException("Incomplete metadata range");done+=size;}
            return bytes;
        }finally{connection=null;}
    }
    public void close(){cancelled.set(true);HttpURLConnection current=connection;connection=null;if(current!=null)current.disconnect();}
}
