package com.linxyi.vrplayer;

import android.content.Context;
import android.content.res.AssetFileDescriptor;
import android.media.MediaDataSource;
import android.net.Uri;
import java.io.*;
import java.nio.ByteBuffer;
import java.nio.channels.FileChannel;

/** Random access for the preview decoder only. Never borrows the playback loader or socket. */
final class ThumbnailDataSource extends MediaDataSource {
    private static final int BLOCK=64*1024;
    private static final long MAX_BYTES=24L*1024*1024;
    private final NetworkSockets sockets=new NetworkSockets(null,2500);
    private volatile boolean cancelled;
    private volatile HttpFormatReader http;
    private FormatMetadata.Reader reader;
    private long length,readBytes,blockOffset=-1;
    private byte[] block;

    void open(Context context,String location,String token)throws Exception {
        check();Uri uri=Uri.parse(location);String scheme=uri.getScheme();
        if("http".equals(scheme)||"https".equals(scheme)){
            http=new HttpFormatReader(location,token);check();reader=http;
        }else if("smb".equals(scheme)){
            SmbStore.Handle handle=SmbStore.connect(uri,sockets,2500);
            try{
                check();com.hierynomus.smbj.share.File file=handle.open();
                reader=new FormatMetadata.Reader(){
                    public long length(){return file.getFileInformation().getStandardInformation().getEndOfFile();}
                    public byte[] read(long offset,int count)throws IOException {
                        byte[] bytes=new byte[count];int done=0;
                        while(done<count){check();int n=file.read(bytes,offset+done,done,count-done);if(n<=0)throw new EOFException();done+=n;}return bytes;
                    }
                    public void close(){try{file.close();}catch(Exception ignored){}handle.close();}
                };
            }catch(Exception ex){handle.close();throw ex;}
        }else{
            AssetFileDescriptor descriptor="content".equals(scheme)?context.getContentResolver().openAssetFileDescriptor(uri,"r"):null;
            if("content".equals(scheme)&&descriptor==null)throw new FileNotFoundException();
            try{
                FileInputStream input=descriptor!=null?new FileInputStream(descriptor.getFileDescriptor()):new FileInputStream("file".equals(scheme)?uri.getPath():location);
                FileChannel channel=input.getChannel();long base=descriptor==null?0:descriptor.getStartOffset();
                reader=new FormatMetadata.Reader(){
                    public long length()throws IOException{return descriptor!=null&&descriptor.getDeclaredLength()>=0?descriptor.getDeclaredLength():channel.size()-base;}
                    public byte[] read(long offset,int count)throws IOException {
                        byte[] bytes=new byte[count];ByteBuffer buffer=ByteBuffer.wrap(bytes);channel.position(base+offset);
                        while(buffer.hasRemaining()){check();if(channel.read(buffer)<=0)throw new EOFException();}return bytes;
                    }
                    public void close()throws IOException{try{input.close();}finally{if(descriptor!=null)descriptor.close();}}
                };
            }catch(Exception ex){if(descriptor!=null)descriptor.close();throw ex;}
        }
        check();length=reader.length();if(length<=0)throw new IOException("Preview length unavailable");
    }
    private void check()throws InterruptedIOException {if(cancelled||Thread.currentThread().isInterrupted())throw new InterruptedIOException("Preview cancelled");}
    @Override public long getSize()throws IOException {check();return length;}
    @Override public synchronized int readAt(long position,byte[] buffer,int offset,int size)throws IOException {
        check();if(position<0||offset<0||size<0||offset>buffer.length-size)throw new IOException("Invalid preview range");
        if(size==0)return 0;if(position>=length)return -1;
        if(block==null||position<blockOffset||position>=blockOffset+block.length){
            long start=position/BLOCK*BLOCK;int count=(int)Math.min(BLOCK,length-start);
            if(readBytes+count>MAX_BYTES)throw new IOException("Preview read budget exhausted");
            readBytes+=count;block=reader.read(start,count);blockOffset=start;check();
        }
        int index=(int)(position-blockOffset),count=Math.min(size,block.length-index);
        System.arraycopy(block,index,buffer,offset,count);return count;
    }
    // Mark cancellation synchronously; interrupt network I/O on the preview cleanup thread.
    void cancel(){cancelled=true;}
    void abortNetwork(){sockets.close();HttpFormatReader value=http;if(value!=null)value.close();}
    @Override public void close(){cancel();abortNetwork();if(reader!=null)try{reader.close();}catch(Exception ignored){}reader=null;block=null;}
}
