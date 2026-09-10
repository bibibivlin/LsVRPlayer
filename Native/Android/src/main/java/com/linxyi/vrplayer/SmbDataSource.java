package com.linxyi.vrplayer;

import android.net.Uri;
import androidx.media3.common.C;
import androidx.media3.datasource.BaseDataSource;
import androidx.media3.datasource.DataSpec;
import java.io.IOException;

final class SmbDataSource extends BaseDataSource {
    private final PlaybackSession session;
    private final byte[] cache=new byte[1024*1024];
    private SmbStore.Handle connection;
    private com.hierynomus.smbj.share.File file;
    private NetworkSockets sockets;
    private ReadAheadReader reader;
    private Uri uri;
    private boolean opened;
    SmbDataSource(PlaybackSession session){super(true);this.session=session;}
    @Override public long open(DataSpec spec)throws IOException{
        close();session.check();
        transferInitializing(spec);uri=spec.uri;
        try{
            sockets=new NetworkSockets(null,15000);session.attach(sockets);
            connection=SmbStore.connect(uri,sockets);session.check();file=connection.open();
            long length=file.getFileInformation().getStandardInformation().getEndOfFile();
            if(spec.position>length)throw new IOException("SMB 读取位置超过文件长度");
            long remaining=spec.length==C.LENGTH_UNSET?length-spec.position:Math.min(spec.length,length-spec.position);
            reader=new ReadAheadReader(this::readNetwork,cache,spec.position,remaining,session.readAhead);
            session.check();session.metrics.opened();opened=true;transferStarted(spec);return remaining;
        }catch(Exception ex){close();throw new IOException("SMB 打开失败："+PlayerBridge.safeError(ex),ex);}
    }
    @Override public int read(byte[] buffer,int offset,int length)throws IOException{
        session.check();if(reader==null)throw new IOException("SMB 数据源未打开");
        try{
            int read=reader.read(buffer,offset,length);session.check();
            if(read>0)session.metrics.delivered(read);return read;
        }catch(Exception ex){throw new IOException("SMB 读取中断，可重试播放",ex);}
    }
    private int readNetwork(byte[] target,long position,int offset,int length)throws IOException{
        length=Math.min(length,connection.connection.getNegotiatedProtocol().getMaxReadSize());
        session.check();long started=System.nanoTime();int count=0;boolean failed=true,cancelled=false;
        try{
            count=file.read(target,position,offset,length);session.check();failed=count<=0;
            if(count>0)bytesTransferred(count); // Actual network transfer, never count cache hits twice.
            return count;
        }catch(Exception ex){cancelled=isCancellation(ex);throw new IOException("SMB 网络读取失败",ex);}
        finally{session.metrics.read(length,count,System.nanoTime()-started,failed,cancelled||session.isCancelled()||Thread.currentThread().isInterrupted());}
    }
    private static boolean isCancellation(Throwable error){
        for(Throwable cause=error;cause!=null;cause=cause.getCause())
            if(cause instanceof InterruptedException||(cause instanceof java.io.InterruptedIOException&&!(cause instanceof java.net.SocketTimeoutException)))return true;
        return false;
    }
    @Override public Uri getUri(){return uri;}
    @Override public void close(){
        if(reader!=null)reader.cancel();reader=null;
        // Closing sockets first also unblocks an in-flight SMB operation during cancellation.
        if(sockets!=null){sockets.close();session.detach(sockets);}sockets=null;
        try{if(file!=null)file.close();}catch(Exception ignored){}file=null;
        if(connection!=null)connection.close();connection=null;uri=null;
        if(opened){opened=false;transferEnded();}
    }
}
