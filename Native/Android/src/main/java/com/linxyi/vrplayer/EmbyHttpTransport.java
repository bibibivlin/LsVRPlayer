package com.linxyi.vrplayer;

import java.io.*;
import java.net.*;
import java.util.*;

/** Header-only authentication, bounded redirects and origin-bound token ownership. */
final class EmbyHttpTransport {
    interface ConnectionOwner {void attach(HttpURLConnection connection)throws IOException;}
    static boolean sameOrigin(URI a,URI b){return a.getScheme()!=null&&a.getScheme().equalsIgnoreCase(b.getScheme())&&a.getHost()!=null&&a.getHost().equalsIgnoreCase(b.getHost())&&port(a)==port(b);}
    private static int port(URI uri){return uri.getPort()>=0?uri.getPort():"https".equalsIgnoreCase(uri.getScheme())?443:80;}
    static URI location(String value)throws IOException {
        try {
            URI uri=new URI(value);
            if((!"http".equalsIgnoreCase(uri.getScheme())&&!"https".equalsIgnoreCase(uri.getScheme()))||uri.getHost()==null||uri.getUserInfo()!=null)throw new IOException("Invalid media server address");
            return uri;
        }catch(URISyntaxException ex){throw new IOException("Invalid media server address");}
    }
    static Response open(URI origin,String value,String token,long offset,long count,Map<String,String> headers,int timeout,ConnectionOwner owner)throws IOException {
        URI target=location(value);
        if(token==null||token.indexOf('\r')>=0||token.indexOf('\n')>=0)throw new IOException("Invalid media server session");
        if(offset<0||count< -1)throw new IOException("Invalid media range");
        for(int redirects=0;redirects<=5;redirects++){
            if(!sameOrigin(origin,target))throw new IOException("Media server redirect changed origin");
            HttpURLConnection connection=(HttpURLConnection)target.toURL().openConnection();
            owner.attach(connection);connection.setConnectTimeout(timeout);connection.setReadTimeout(timeout);connection.setInstanceFollowRedirects(false);
            connection.setRequestProperty("User-Agent","LsVRPlayer/1.0");connection.setRequestProperty("Accept-Encoding","identity");
            for(Map.Entry<String,String> header:headers.entrySet()){
                String name=header.getKey();if(!name.equalsIgnoreCase("X-Emby-Token")&&!name.equalsIgnoreCase("Authorization")&&!name.equalsIgnoreCase("Range")&&!name.equalsIgnoreCase("Accept-Encoding"))connection.setRequestProperty(name,header.getValue());
            }
            if(!token.isEmpty())connection.setRequestProperty("X-Emby-Token",token);
            if(offset>0||count>=0){String end=count>0?Long.toString(Math.addExact(offset,count-1)):"";connection.setRequestProperty("Range","bytes="+offset+"-"+end);}
            try {
                int code=connection.getResponseCode();
                if(code==301||code==302||code==303||code==307||code==308){
                    String next=connection.getHeaderField("Location");connection.disconnect();
                    if(next==null||redirects==5)throw new IOException("Invalid media server redirect");
                    try{target=location(target.resolve(next).toString());}catch(IllegalArgumentException ex){throw new IOException("Invalid media server redirect");}
                    continue;
                }
                if(code<200||code>299)throw new IOException(code==401||code==403?"Media server session expired":"Media server HTTP "+code);
                if(offset>0&&code!=206)throw new IOException("Media server does not support seeking");
                long total=totalLength(connection,code,offset),remaining=connection.getContentLengthLong();
                if(count>=0)remaining=remaining<0?count:Math.min(count,remaining);
                return new Response(connection,connection.getInputStream(),target,total,remaining);
            }catch(IOException|RuntimeException ex){connection.disconnect();throw ex;}
        }
        throw new IOException("Too many media server redirects");
    }
    private static long totalLength(HttpURLConnection connection,int code,long offset)throws IOException {
        if(code!=206)return connection.getContentLengthLong();
        String range=connection.getHeaderField("Content-Range");
        if(range==null||!range.startsWith("bytes "))throw new IOException("Invalid media range response");
        try {
            int dash=range.indexOf('-',6),slash=range.indexOf('/',dash+1);
            if(Long.parseLong(range.substring(6,dash))!=offset)throw new IOException("Unexpected media range response");
            return range.substring(slash+1).equals("*")?-1:Long.parseLong(range.substring(slash+1));
        }catch(IndexOutOfBoundsException|NumberFormatException ex){throw new IOException("Invalid media range response");}
    }
    static final class Response implements AutoCloseable {
        final HttpURLConnection connection;final InputStream input;final URI uri;final long totalLength,remaining;
        Response(HttpURLConnection connection,InputStream input,URI uri,long totalLength,long remaining){this.connection=connection;this.input=input;this.uri=uri;this.totalLength=totalLength;this.remaining=remaining;}
        public void close(){connection.disconnect();try{input.close();}catch(IOException ignored){}}
    }
}
