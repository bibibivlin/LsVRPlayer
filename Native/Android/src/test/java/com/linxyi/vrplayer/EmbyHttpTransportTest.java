package com.linxyi.vrplayer;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.atomic.AtomicReference;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import org.junit.Test;
import static org.junit.Assert.*;

public class EmbyHttpTransportTest {
    // Android's unit-test boot classpath excludes the JDK HTTP server module.
    private interface Handler {void handle(Exchange exchange)throws Exception;}
    private static final class Headers extends HashMap<String,String> {
        String getFirst(String name){return get(name.toLowerCase(Locale.ROOT));}
        void set(String name,String value){put(name.toLowerCase(Locale.ROOT),value);}
    }
    private static final class Exchange {
        final Socket socket;final URI uri;final Headers request=new Headers(),response=new Headers();final ByteArrayOutputStream body=new ByteArrayOutputStream();int code=200;long length;
        Exchange(Socket socket)throws Exception {
            this.socket=socket;BufferedReader reader=new BufferedReader(new InputStreamReader(socket.getInputStream(),StandardCharsets.US_ASCII));String first=reader.readLine();uri=new URI(first.split(" ")[1]);
            String line;while((line=reader.readLine())!=null&&!line.isEmpty()){int colon=line.indexOf(':');request.set(line.substring(0,colon),line.substring(colon+1).trim());}
        }
        Headers getRequestHeaders(){return request;}Headers getResponseHeaders(){return response;}URI getRequestURI(){return uri;}OutputStream getResponseBody(){return body;}
        void sendResponseHeaders(int code,long length){this.code=code;this.length=length;}
        void close()throws IOException {
            OutputStream output=socket.getOutputStream();StringBuilder headers=new StringBuilder("HTTP/1.1 "+code+" Response\r\nConnection: close\r\nContent-Length: "+Math.max(0,length)+"\r\n");
            for(Map.Entry<String,String> entry:response.entrySet())headers.append(entry.getKey()).append(": ").append(entry.getValue()).append("\r\n");headers.append("\r\n");output.write(headers.toString().getBytes(StandardCharsets.US_ASCII));body.writeTo(output);socket.close();
        }
    }
    private static final class HttpServer {
        final ServerSocket listener;final Map<String,Handler> handlers=new HashMap<>();final ExecutorService worker=Executors.newSingleThreadExecutor();volatile boolean stopped;volatile Throwable failure;
        private HttpServer()throws IOException {listener=new ServerSocket();listener.bind(new InetSocketAddress("127.0.0.1",0));}
        static HttpServer create(InetSocketAddress ignored,int backlog)throws IOException {return new HttpServer();}
        InetSocketAddress getAddress(){return (InetSocketAddress)listener.getLocalSocketAddress();}
        void createContext(String path,Handler handler){handlers.put(path,handler);}
        void start(){worker.submit(()->{while(!stopped){try(Socket socket=listener.accept()){Exchange exchange=new Exchange(socket);handlers.get(exchange.uri.getPath()).handle(exchange);}catch(Throwable ex){if(!stopped)failure=ex;}}});}
        void stop(int delay){stopped=true;try{listener.close();}catch(IOException ignored){}worker.shutdownNow();if(failure!=null)throw new AssertionError("Local HTTP fixture failed",failure);}
    }
    private static String address(HttpServer server){return "http://127.0.0.1:"+server.getAddress().getPort();}
    private static EmbyHttpTransport.Response open(String uri,String token,long offset,long count)throws Exception {return EmbyHttpTransport.open(EmbyHttpTransport.location(uri),uri,token,offset,count,Collections.emptyMap(),2000,value->{});}
    @Test public void tokenUsesHeaderAndEachOpenCapturesItsOwnSession()throws Exception {
        HttpServer server=HttpServer.create(new InetSocketAddress(InetAddress.getLoopbackAddress(),0),0);List<String> tokens=new ArrayList<>();List<String> queries=new ArrayList<>();
        server.createContext("/video",exchange->{tokens.add(exchange.getRequestHeaders().getFirst("X-Emby-Token"));queries.add(exchange.getRequestURI().toString());exchange.sendResponseHeaders(200,3);exchange.getResponseBody().write(new byte[]{1,2,3});exchange.close();});server.start();
        try {String uri=address(server)+"/video?static=true";try(EmbyHttpTransport.Response response=open(uri,"first-session",0,-1)){assertEquals(3,response.input.readAllBytes().length);}try(EmbyHttpTransport.Response response=open(uri,"second-session",0,-1)){assertEquals(3,response.input.readAllBytes().length);}assertEquals(Arrays.asList("first-session","second-session"),tokens);assertEquals(Arrays.asList("/video?static=true","/video?static=true"),queries);}
        finally{server.stop(0);}
    }
    @Test public void redirectedRequestCannotForwardTokenToAnotherOrigin()throws Exception {
        HttpServer first=HttpServer.create(new InetSocketAddress(InetAddress.getLoopbackAddress(),0),0),other=HttpServer.create(new InetSocketAddress(InetAddress.getLoopbackAddress(),0),0);AtomicReference<String> stolen=new AtomicReference<>();
        other.createContext("/video",exchange->{stolen.set(exchange.getRequestHeaders().getFirst("X-Emby-Token"));exchange.sendResponseHeaders(200,1);exchange.close();});other.start();
        first.createContext("/video",exchange->{exchange.getResponseHeaders().set("Location",address(other)+"/video");exchange.sendResponseHeaders(302,-1);exchange.close();});first.start();
        try{try(EmbyHttpTransport.Response ignored=open(address(first)+"/video","private-session",0,-1)){fail("Cross-origin redirect must fail");}catch(IOException expected){assertFalse(expected.getMessage().contains("private-session"));}assertNull(stolen.get());}
        finally{first.stop(0);other.stop(0);}
    }
    @Test public void metadataReadsRequestedRangeWithoutDownloadingPayload()throws Exception {
        byte[] bytes=FormatMetadataTest.box("moov",FormatMetadataTest.video(FormatMetadataTest.box("st3d",new byte[]{0,0,0,0,2}),FormatMetadataTest.box("sv3d",FormatMetadataTest.box("proj",FormatMetadataTest.equi(0,0)))));
        HttpServer server=HttpServer.create(new InetSocketAddress(InetAddress.getLoopbackAddress(),0),0);int[] transferred={0};
        server.createContext("/video",exchange->{assertEquals("session",exchange.getRequestHeaders().getFirst("X-Emby-Token"));String range=exchange.getRequestHeaders().getFirst("Range");String[] parts=range.substring(6).split("-");int start=Integer.parseInt(parts[0]),end=Integer.parseInt(parts[1]);int count=end-start+1;transferred[0]+=count;exchange.getResponseHeaders().set("Content-Range","bytes "+start+"-"+end+"/"+bytes.length);exchange.sendResponseHeaders(206,count);exchange.getResponseBody().write(bytes,start,count);exchange.close();});server.start();
        try(HttpFormatReader reader=new HttpFormatReader(address(server)+"/video","session")){FormatMetadata.Hints hints=FormatMetadata.inspect(reader);assertEquals(1,hints.stereo);assertEquals(2,hints.projection);assertTrue(transferred[0]<bytes.length);}
        finally{server.stop(0);}
    }
    @Test public void cancelledMetadataReaderRejectsFurtherRanges()throws Exception {
        HttpFormatReader reader=new HttpFormatReader("http://127.0.0.1:1234/video","session");reader.close();try{reader.read(0,8);fail("Cancelled probe must not connect");}catch(InterruptedIOException expected){}
    }
    @Test public void rangeIgnoredByServerDoesNotDownloadBeforeSeekOffset()throws Exception {
        HttpServer server=HttpServer.create(new InetSocketAddress(InetAddress.getLoopbackAddress(),0),0);server.createContext("/video",exchange->{exchange.sendResponseHeaders(200,3);exchange.getResponseBody().write(new byte[]{1,2,3});exchange.close();});server.start();
        try {try(EmbyHttpTransport.Response ignored=open(address(server)+"/video","session",2,1)){fail("Ignoring nonzero range must fail");}catch(IOException expected){assertTrue(expected.getMessage().contains("seeking"));}}
        finally{server.stop(0);}
    }
    @Test public void defaultPortsAndCaseAreSameOriginButDowngradeIsNot()throws Exception {
        assertTrue(EmbyHttpTransport.sameOrigin(new URI("http://EMBY.local/video"),new URI("http://emby.local:80/other")));
        assertFalse(EmbyHttpTransport.sameOrigin(new URI("https://emby.local/video"),new URI("http://emby.local/other")));
    }
    @Test public void invalidSessionIsRejectedBeforeConnectionAndErrorDoesNotContainToken()throws Exception {
        try(EmbyHttpTransport.Response ignored=open("http://127.0.0.1:1234/video","private\r\nsession",0,-1)){fail("Header injection must fail");}catch(IOException expected){assertFalse(expected.getMessage().contains("private"));}
    }
}
