package com.linxyi.vrplayer;

import android.content.Context;
import android.content.res.AssetFileDescriptor;
import android.net.Uri;
import java.io.*;
import java.nio.ByteBuffer;
import java.nio.channels.FileChannel;
import java.util.concurrent.atomic.AtomicBoolean;

/** Each probe owns its descriptor/SMB handle; cancellation never touches the playback source. */
final class FormatProbe implements AutoCloseable {
    private final Context context;private final String location,accessToken;
    private final AtomicBoolean cancelled=new AtomicBoolean();
    private volatile FormatMetadata.Reader reader;
    FormatProbe(Context context,String location){this(context,location,"");}
    FormatProbe(Context context,String location,String accessToken){this.context=context;this.location=location;this.accessToken=accessToken;}
    FormatMetadata.Hints run() throws Exception {
        Uri uri=Uri.parse(location);FormatMetadata.Reader opened;
        if("http".equals(uri.getScheme())||"https".equals(uri.getScheme())){
            opened=new HttpFormatReader(location,accessToken);PlayerBridge.attachResource(opened);
        }else if("smb".equals(uri.getScheme())){
            SmbStore.Handle handle=SmbStore.connect(uri);PlayerBridge.attachResource(handle);
            com.hierynomus.smbj.share.File file=handle.open();long length=file.getFileInformation().getStandardInformation().getEndOfFile();
            opened=new FormatMetadata.Reader(){
                public long length(){return length;}
                public byte[] read(long offset,int count)throws IOException{byte[] bytes=new byte[count];int done=0;while(done<count){check();int n=file.read(bytes,offset+done,done,count-done);if(n<=0)throw new EOFException();done+=n;}return bytes;}
                public void close(){try{file.close();}catch(Exception ignored){}handle.close();}
            };
        }else{
            AssetFileDescriptor descriptor="content".equals(uri.getScheme())?context.getContentResolver().openAssetFileDescriptor(uri,"r"):null;
            if("content".equals(uri.getScheme())&&descriptor==null)throw new FileNotFoundException();
            FileInputStream input=descriptor!=null?new FileInputStream(descriptor.getFileDescriptor()):new FileInputStream("file".equals(uri.getScheme())?uri.getPath():location);
            FileChannel channel=input.getChannel();long base=descriptor==null?0:descriptor.getStartOffset();
            opened=new FormatMetadata.Reader(){
                public long length()throws IOException{return descriptor!=null&&descriptor.getDeclaredLength()>=0?descriptor.getDeclaredLength():channel.size()-base;}
                public byte[] read(long offset,int count)throws IOException{check();byte[] bytes=new byte[count];ByteBuffer buffer=ByteBuffer.wrap(bytes);channel.position(base+offset);while(buffer.hasRemaining()){check();if(channel.read(buffer)<=0)throw new EOFException();}return bytes;}
                public void close()throws IOException{try{input.close();}finally{if(descriptor!=null)descriptor.close();}}
            };
        }
        reader=opened;try{check();return FormatMetadata.inspect(opened);}finally{close();}
    }
    private void check()throws InterruptedIOException{if(cancelled.get()||Thread.currentThread().isInterrupted())throw new InterruptedIOException();}
    public void close(){cancelled.set(true);FormatMetadata.Reader current=reader;reader=null;if(current!=null)try{current.close();}catch(Exception ignored){}}
}
