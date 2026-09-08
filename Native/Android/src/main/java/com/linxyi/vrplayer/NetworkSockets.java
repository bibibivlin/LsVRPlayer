package com.linxyi.vrplayer;

import android.net.Network;
import java.io.IOException;
import java.net.*;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import javax.net.SocketFactory;

/** Closing the group interrupts connect/read as well as a Future's Java thread. */
final class NetworkSockets extends SocketFactory implements AutoCloseable {
    private final Network network;
    private final int timeout;
    private final Set<Socket> sockets=ConcurrentHashMap.newKeySet();
    private volatile boolean closed;
    NetworkSockets(Network network,int timeout){this.network=network;this.timeout=timeout;}
    @Override public Socket createSocket()throws IOException {
        Socket socket=network==null?new Socket():network.getSocketFactory().createSocket();
        sockets.add(socket);
        if(closed){socket.close();throw new IOException("Request cancelled");}
        socket.setSoTimeout(timeout);return socket;
    }
    @Override public Socket createSocket(String host,int port)throws IOException{return connect(new InetSocketAddress(host,port),null);}
    @Override public Socket createSocket(InetAddress host,int port)throws IOException{return connect(new InetSocketAddress(host,port),null);}
    @Override public Socket createSocket(String host,int port,InetAddress local,int localPort)throws IOException{return connect(new InetSocketAddress(host,port),new InetSocketAddress(local,localPort));}
    @Override public Socket createSocket(InetAddress host,int port,InetAddress local,int localPort)throws IOException{return connect(new InetSocketAddress(host,port),new InetSocketAddress(local,localPort));}
    private Socket connect(SocketAddress remote,SocketAddress local)throws IOException {
        Socket socket=createSocket();try{if(local!=null)socket.bind(local);socket.connect(remote,timeout);return socket;}catch(IOException ex){socket.close();sockets.remove(socket);throw ex;}
    }
    @Override public void close(){closed=true;for(Socket socket:sockets)try{socket.close();}catch(IOException ignored){}sockets.clear();}
}
