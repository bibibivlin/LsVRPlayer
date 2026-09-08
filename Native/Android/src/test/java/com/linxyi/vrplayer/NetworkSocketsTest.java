package com.linxyi.vrplayer;
import java.net.*;
import java.io.*;
import java.util.concurrent.*;
import org.junit.Test;
import static org.junit.Assert.*;

public class NetworkSocketsTest {
    @Test public void cancellationClosesAnEstablishedSocketAndRejectsNewOnes()throws Exception {
        try(ServerSocket listener=new ServerSocket(0);NetworkSockets group=new NetworkSockets(null,5000)){
            Socket client=group.createSocket(InetAddress.getLoopbackAddress(),listener.getLocalPort());
            try(Socket peer=listener.accept()){
                ExecutorService worker=Executors.newSingleThreadExecutor();
                try{Future<Boolean> read=worker.submit(()->{try{client.getInputStream().read();return false;}catch(IOException ex){return true;}});group.close();assertTrue(read.get(1,TimeUnit.SECONDS));assertTrue(client.isClosed());}
                finally{worker.shutdownNow();}
            }
            try{group.createSocket();fail("Cancelled groups must not create sockets");}catch(IOException expected){}
        }
    }
}
