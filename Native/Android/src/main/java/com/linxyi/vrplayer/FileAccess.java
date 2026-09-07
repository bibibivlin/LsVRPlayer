package com.linxyi.vrplayer;

import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.provider.DocumentsContract;
import org.json.*;
import java.io.*;
import java.nio.ByteBuffer;
import java.nio.charset.*;
import java.util.*;
import com.hierynomus.msfscc.fileinformation.FileIdBothDirectoryInformation;

final class FileAccess {
    static JSONObject entry(String name,String uri,boolean directory,long size,long modified)throws JSONException{
        return new JSONObject().put("name",name).put("uri",uri).put("directory",directory).put("size",size).put("modified",modified);
    }
    static String documentId(Uri uri){return DocumentsContract.isDocumentUri(PlayerBridge.activity,uri)?DocumentsContract.getDocumentId(uri):DocumentsContract.getTreeDocumentId(uri);}
    static JSONObject list(Context context,String location)throws Exception{
        JSONArray entries=new JSONArray();Uri uri=Uri.parse(location);
        if("smb".equals(uri.getScheme())){
            try(SmbStore.Handle handle=SmbStore.connect(uri)){
                for(FileIdBothDirectoryInformation file:handle.share.list(handle.path)){
                    if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();String name=file.getFileName();if(name.equals(".")||name.equals(".."))continue;
                    entries.put(entry(name,SmbStore.child(location,name),(file.getFileAttributes()&16)!=0,file.getEndOfFile(),file.getLastWriteTime().toEpochMillis()));
                }
            }
        }else if("content".equals(uri.getScheme())){
            Uri children=DocumentsContract.buildChildDocumentsUriUsingTree(uri,documentId(uri));
            String[] projection={DocumentsContract.Document.COLUMN_DOCUMENT_ID,DocumentsContract.Document.COLUMN_DISPLAY_NAME,DocumentsContract.Document.COLUMN_MIME_TYPE,DocumentsContract.Document.COLUMN_SIZE,DocumentsContract.Document.COLUMN_LAST_MODIFIED};
            try(Cursor cursor=context.getContentResolver().query(children,projection,null,null,null)){
                if(cursor==null)throw new IOException("无法读取授权目录");
                while(cursor.moveToNext()){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();entries.put(entry(cursor.getString(1),DocumentsContract.buildDocumentUriUsingTree(uri,cursor.getString(0)).toString(),DocumentsContract.Document.MIME_TYPE_DIR.equals(cursor.getString(2)),cursor.getLong(3),cursor.getLong(4)));}
            }
        }else{
            File directory=new File("file".equals(uri.getScheme())?uri.getPath():location);File[] files=directory.listFiles();if(files==null)throw new IOException("无法读取目录，请先授予存储权限或添加授权目录");
            for(File file:files){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();if(file.getName().startsWith("."))continue;entries.put(entry(file.getName(),Uri.fromFile(file).toString(),file.isDirectory(),file.length(),file.lastModified()));}
        }
        return new JSONObject().put("entries",entries);
    }
    static String readText(Context context,String location,String encoding)throws Exception{
        Uri uri=Uri.parse(location);byte[] bytes;
        if("smb".equals(uri.getScheme())){try(SmbStore.Handle handle=SmbStore.connect(uri);com.hierynomus.smbj.share.File file=handle.open()){
            long length=file.getFileInformation().getStandardInformation().getEndOfFile();
            if(length<0||length>4*1024*1024)throw new IOException("字幕超过 4 MB 限制");
            bytes=new byte[(int)length];int offset=0;
            // Bound reads by the advertised size: some SMB servers return malformed EOF packets.
            while(offset<bytes.length){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();int count=file.read(bytes,offset,offset,Math.min(8192,bytes.length-offset));if(count<=0)throw new EOFException("字幕读取不完整");offset+=count;}
        }}
        else if("content".equals(uri.getScheme())){try(InputStream stream=context.getContentResolver().openInputStream(uri)){bytes=readBounded(stream);}}
        else {try(InputStream stream=new FileInputStream("file".equals(uri.getScheme())?uri.getPath():location)){bytes=readBounded(stream);}}
        if(encoding!=null&&!encoding.isEmpty()&&!encoding.equals("Auto"))return new String(bytes,Charset.forName(encoding));
        if(bytes.length>=2&&((bytes[0]&255)==255&&(bytes[1]&255)==254))return new String(bytes,2,bytes.length-2,StandardCharsets.UTF_16LE);
        if(bytes.length>=2&&((bytes[0]&255)==254&&(bytes[1]&255)==255))return new String(bytes,2,bytes.length-2,StandardCharsets.UTF_16BE);
        try{return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString();}
        catch(CharacterCodingException ex){return new String(bytes,Charset.forName("GB18030"));}
    }
    private static byte[] readBounded(InputStream input)throws IOException{
        if(input==null)throw new IOException("无法打开字幕");ByteArrayOutputStream output=new ByteArrayOutputStream();byte[] chunk=new byte[8192];int length;
        while((length=input.read(chunk))!=-1){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();if(output.size()+length>4*1024*1024)throw new IOException("字幕超过 4 MB 限制");output.write(chunk,0,length);}return output.toByteArray();
    }
}
