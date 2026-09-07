package com.linxyi.vrplayer;

import android.net.Uri;
import androidx.media3.common.C;
import androidx.media3.datasource.BaseDataSource;
import androidx.media3.datasource.DataSpec;
import java.io.IOException;

final class SmbDataSource extends BaseDataSource {
    private SmbStore.Handle connection;
    private com.hierynomus.smbj.share.File file;
    private Uri uri;
    private long position,remaining;
    private boolean opened;
    SmbDataSource(){super(true);}
    @Override public long open(DataSpec spec)throws IOException{
        transferInitializing(spec);uri=spec.uri;
        try{
            connection=SmbStore.connect(uri);file=connection.open();
            long length=file.getFileInformation().getStandardInformation().getEndOfFile();
            if(spec.position>length)throw new IOException("SMB 读取位置超过文件长度");
            position=spec.position;remaining=spec.length==C.LENGTH_UNSET?length-position:Math.min(spec.length,length-position);
            opened=true;transferStarted(spec);return remaining;
        }catch(Exception ex){close();throw new IOException("SMB 打开失败："+PlayerBridge.safeError(ex),ex);}
    }
    @Override public int read(byte[] buffer,int offset,int length)throws IOException{
        if(length==0)return 0;if(remaining==0)return C.RESULT_END_OF_INPUT;
        try{
            int read=file.read(buffer,position,offset,(int)Math.min(length,remaining));
            if(read<0){remaining=0;return C.RESULT_END_OF_INPUT;}
            if(read==0)throw new IOException("SMB 服务端未返回预期数据");
            position+=read;remaining-=read;bytesTransferred(read);return read;
        }catch(Exception ex){throw new IOException("SMB 读取中断，可重试播放",ex);}
    }
    @Override public Uri getUri(){return uri;}
    @Override public void close(){
        try{if(file!=null)file.close();}catch(Exception ignored){}file=null;
        if(connection!=null)connection.close();connection=null;uri=null;
        if(opened){opened=false;transferEnded();}
    }
}
