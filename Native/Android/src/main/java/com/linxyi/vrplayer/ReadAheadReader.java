package com.linxyi.vrplayer;

import java.io.EOFException;
import java.io.IOException;
import java.io.InterruptedIOException;

/** A bounded, forward-only range. Only the loader thread reads; cancellation may come from any thread. */
final class ReadAheadReader {
    interface Source { int read(byte[] target,long position,int offset,int length)throws IOException; }
    private final Source source;
    private final byte[] cache;
    private final boolean enabled;
    private long position,remaining;
    private int cursor,limit;
    private volatile boolean cancelled;

    ReadAheadReader(Source source,byte[] cache,long position,long length,boolean enabled){
        if(position<0||length<0||length>Long.MAX_VALUE-position||cache.length==0)throw new IllegalArgumentException("Invalid read range");
        this.source=source;this.cache=cache;this.position=position;remaining=length;this.enabled=enabled;
    }
    long remaining(){return remaining;}
    void cancel(){cancelled=true;}
    private void check()throws InterruptedIOException{
        if(cancelled||Thread.currentThread().isInterrupted())throw new InterruptedIOException("Read cancelled");
    }
    int read(byte[] target,int offset,int length)throws IOException{
        if(offset<0||length<0||offset>target.length-length)throw new IndexOutOfBoundsException();
        check();if(length==0)return 0;if(remaining==0)return -1;
        int count;
        if(!enabled){
            count=source.read(target,position,offset,(int)Math.min(length,remaining));
            check();validate(count,(int)Math.min(length,remaining));
        }else{
            if(cursor==limit){
                int requested=(int)Math.min(cache.length,remaining);
                int received=source.read(cache,position,0,requested);
                check();validate(received,requested);cursor=0;limit=received;
            }
            count=Math.min(length,limit-cursor);
            check();System.arraycopy(cache,cursor,target,offset,count);cursor+=count;
        }
        position+=count;remaining-=count;return count;
    }
    private static void validate(int received,int requested)throws IOException{
        if(received<0)throw new EOFException("SMB file ended before the declared range");
        if(received==0||received>requested)throw new IOException("Invalid SMB read length");
    }
}
