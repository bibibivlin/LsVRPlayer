package com.linxyi.vrplayer;

import android.content.Context;
import android.net.*;
import android.net.nsd.*;
import android.net.wifi.WifiManager;
import android.os.SystemClock;
import com.hierynomus.smbj.SMBClient;
import com.hierynomus.smbj.SmbConfig;
import com.hierynomus.smbj.connection.Connection;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;
import org.json.*;

final class SmbDiscovery implements AutoCloseable {
    private static final AtomicInteger sequence=new AtomicInteger();
    private static final Map<Integer,SmbDiscovery> scans=new ConcurrentHashMap<>();
    private static final Object resolveLock=new Object();
    private static boolean resolveBusy;
    static synchronized int begin(Context context){for(SmbDiscovery scan:scans.values())scan.close();scans.clear();int id=sequence.incrementAndGet();SmbDiscovery scan=new SmbDiscovery(context);scans.put(id,scan);scan.coordinator.start();return id;}
    static String poll(int id){SmbDiscovery scan=scans.get(id);return scan==null?"{\"running\":false,\"complete\":false,\"hosts\":[]}":scan.snapshot();}
    static void stop(int id){SmbDiscovery scan=scans.remove(id);if(scan!=null)scan.close();}
    static void stopAll(){for(Integer id:scans.keySet())stop(id);}
    private static final class Candidate {final String host,name;final int port;Candidate(String h,int p,String n){host=h;port=p;name=n;}}
    private final Context context;
    private final Thread coordinator;
    private final ExecutorService workers=Executors.newFixedThreadPool(32);
    private final Set<NetworkSockets> sockets=ConcurrentHashMap.newKeySet();
    private final Set<String> scheduled=ConcurrentHashMap.newKeySet();
    private final Map<String,Candidate> found=new ConcurrentHashMap<>();
    private final Map<String,String> names=new ConcurrentHashMap<>();
    private final Queue<Candidate> announced=new ConcurrentLinkedQueue<>();
    private final Queue<NsdServiceInfo> resolveQueue=new ConcurrentLinkedQueue<>();
    private final AtomicInteger tested=new AtomicInteger();
    private final List<DiscoveryRange> ranges=new ArrayList<>();
    private volatile boolean running=true,complete,resolving,nsdStarted;
    private volatile String error="";
    private volatile int total;
    private volatile long nextResolveAt;
    private Network network;
    private NsdManager nsd;
    private WifiManager.MulticastLock multicastLock;
    private DatagramSocket wsd;
    private NsdManager.DiscoveryListener listener;
    private SmbDiscovery(Context context){this.context=context.getApplicationContext();coordinator=new Thread(this::run,"SMB discovery");}
    private void run(){
        long started=SystemClock.elapsedRealtime(),deadline=started+30000;
        try{
            ConnectivityManager manager=(ConnectivityManager)context.getSystemService(Context.CONNECTIVITY_SERVICE);
            for(Network candidate:manager.getAllNetworks()){
                NetworkCapabilities caps=manager.getNetworkCapabilities(candidate);
                if(caps!=null&&!caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)&&(caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)||caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET))){network=candidate;if(candidate.equals(manager.getActiveNetwork()))break;}
            }
            if(network==null){error="未连接 Wi-Fi 或有线局域网";return;}
            LinkProperties properties=manager.getLinkProperties(network);
            if(properties!=null)for(LinkAddress address:properties.getLinkAddresses())if(address.getAddress() instanceof Inet4Address&&!address.getAddress().isLoopbackAddress())ranges.add(new DiscoveryRange(address.getAddress().getAddress(),address.getPrefixLength()));
            long count=0;for(DiscoveryRange range:ranges)count+=range.count();total=(int)Math.min(Integer.MAX_VALUE,count);
            startNsd();startWsd();
            CompletionService<Void> completion=new ExecutorCompletionService<>(workers);int pending=0,rangeIndex=0;
            while(running&&SystemClock.elapsedRealtime()<deadline){
                resolveNext();
                NetworkCapabilities caps=manager.getNetworkCapabilities(network);if(caps==null){error="网络已断开，扫描已停止";break;}
                while(running&&pending<64){
                    Candidate candidate=announced.poll();
                    if(candidate==null){while(rangeIndex<ranges.size()&&!ranges.get(rangeIndex).hasNext())rangeIndex++;if(rangeIndex>=ranges.size())break;candidate=new Candidate(ranges.get(rangeIndex).take(),445,"");}
                    final Candidate target=candidate;String key=target.host+":"+target.port;
                    if(!scheduled.add(key))continue;
                    completion.submit(()->{probe(target);tested.incrementAndGet();return null;});pending++;
                }
                Future<Void> done=completion.poll(100,TimeUnit.MILLISECONDS);if(done!=null)pending--;
                if(pending==0&&rangeIndex>=ranges.size()&&announced.isEmpty()&&!resolving&&resolveQueue.isEmpty()&&SystemClock.elapsedRealtime()-started>=12000){complete=true;break;}
            }
            if(running&&!complete&&error.isEmpty())error="扫描时间已到，部分地址未完成；可重新扫描";
        }catch(InterruptedException ignored){}catch(Exception ex){if(running)error="无法完成局域网发现，请检查网络后重试";}
        finally{close();}
    }
    private void probe(Candidate target){
        if(!running)return;NetworkSockets group=new NetworkSockets(network,500);sockets.add(group);
        if(!running){group.close();sockets.remove(group);return;}
        try(SMBClient client=new SMBClient(SmbConfig.builder().withSocketFactory(group).withTimeout(500,TimeUnit.MILLISECONDS).withSoTimeout(500).build());Connection connection=client.connect(target.host,target.port)){
            // SMBClient.connect completes SMB2/3 negotiation; an open TCP port alone is insufficient.
            if(running)found.put(target.host+":"+target.port,target);
        }catch(Exception ignored){}finally{group.close();sockets.remove(group);}
    }
    private boolean onLan(InetAddress address){
        if(address==null||address.isLoopbackAddress()||address.isAnyLocalAddress()||address.isMulticastAddress())return false;
        if(address instanceof Inet4Address){for(DiscoveryRange range:ranges)if(range.contains(address.getAddress()))return true;return false;}
        return address.isLinkLocalAddress()||address.isSiteLocalAddress();
    }
    private void announce(InetAddress address,int port,String name){
        if(!running||port<1||port>65535||!onLan(address))return;
        String host=address.getHostAddress(),key=host+":"+port;
        if(name!=null&&!name.isEmpty())names.put(key,name);
        if(!scheduled.contains(key))announced.offer(new Candidate(host,port,name));
    }
    private synchronized void startNsd(){
        if(!running)return;
        try{
            WifiManager wifi=(WifiManager)context.getSystemService(Context.WIFI_SERVICE);
            if(wifi!=null){multicastLock=wifi.createMulticastLock("VRPlayer SMB discovery");multicastLock.setReferenceCounted(false);multicastLock.acquire();}
            nsd=(NsdManager)context.getSystemService(Context.NSD_SERVICE);
            listener=new NsdManager.DiscoveryListener(){
                public void onDiscoveryStarted(String type){}
                public void onDiscoveryStopped(String type){}
                public void onStartDiscoveryFailed(String type,int code){nsdStarted=false;}
                public void onStopDiscoveryFailed(String type,int code){}
                public void onServiceLost(NsdServiceInfo service){}
                public void onServiceFound(NsdServiceInfo service){if(running){resolveQueue.offer(service);resolveNext();}}
            };
            nsdStarted=true;nsd.discoverServices("_smb._tcp.",NsdManager.PROTOCOL_DNS_SD,listener);
        }catch(Exception ignored){} // Active subnet probing continues when multicast is unavailable.
    }
    private synchronized void resolveNext(){
        if(!running||resolving||nsd==null||SystemClock.elapsedRealtime()<nextResolveAt)return;
        NsdServiceInfo service;
        // Android 29 permits one resolve per process, including a previous cancelled scan.
        // Its callback must release the slot before a new scan starts resolving services.
        synchronized(resolveLock){if(resolveBusy)return;service=resolveQueue.poll();if(service==null)return;resolveBusy=true;resolving=true;}
        try{nsd.resolveService(service,new NsdManager.ResolveListener(){
            public void onResolveFailed(NsdServiceInfo info,int code){
                if(running&&code==NsdManager.FAILURE_ALREADY_ACTIVE){resolveQueue.offer(service);nextResolveAt=SystemClock.elapsedRealtime()+500;}
                releaseResolve();
            }
            public void onServiceResolved(NsdServiceInfo info){announce(info.getHost(),info.getPort(),info.getServiceName());releaseResolve();}
        });}catch(Exception ignored){releaseResolve();}
    }
    private void releaseResolve(){synchronized(resolveLock){resolveBusy=false;}resolving=false;}
    private void startWsd(){
        Thread receiver=new Thread(()->{
            try{
                DatagramSocket socket=new DatagramSocket(null);wsd=socket;if(!running){socket.close();return;}
                network.bindSocket(socket);socket.bind(new InetSocketAddress(0));socket.setSoTimeout(700);
                for(String namespace:new String[]{"http://schemas.xmlsoap.org/ws/2005/04/discovery","http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01"}){
                    String body="<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:a=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" xmlns:d=\""+namespace+"\"><s:Header><a:Action>"+namespace+"/Probe</a:Action><a:MessageID>urn:uuid:"+UUID.randomUUID()+"</a:MessageID><a:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To></s:Header><s:Body><d:Probe/></s:Body></s:Envelope>";
                    byte[] data=body.getBytes(StandardCharsets.UTF_8);socket.send(new DatagramPacket(data,data.length,InetAddress.getByName("239.255.255.250"),3702));
                }
                byte[] bytes=new byte[8192];while(running){try{DatagramPacket reply=new DatagramPacket(bytes,bytes.length);socket.receive(reply);String text=new String(bytes,0,reply.getLength(),StandardCharsets.UTF_8);if(text.contains("ProbeMatch"))announce(reply.getAddress(),445,"");}catch(SocketTimeoutException ignored){}}
            }catch(Exception ignored){}finally{if(wsd!=null)wsd.close();}
        },"SMB WS-Discovery");receiver.start();
    }
    private String snapshot(){
        try{JSONArray hosts=new JSONArray();List<String> keys=new ArrayList<>(found.keySet());Collections.sort(keys);
            for(String key:keys){Candidate host=found.get(key);hosts.put(new JSONObject().put("host",host.host).put("port",host.port).put("name",names.getOrDefault(key,host.name==null?"":host.name)));}
            return new JSONObject().put("running",running).put("complete",complete).put("tested",tested.get()).put("total",Math.max(total,scheduled.size())).put("error",error).put("hosts",hosts).toString();
        }catch(JSONException ex){return "{\"running\":false,\"hosts\":[],\"error\":\"发现结果不可用\"}";}
    }
    @Override public synchronized void close(){
        running=false;coordinator.interrupt();workers.shutdownNow();for(NetworkSockets group:sockets)group.close();
        if(wsd!=null)wsd.close();
        if(nsdStarted&&nsd!=null&&listener!=null){nsdStarted=false;try{nsd.stopServiceDiscovery(listener);}catch(Exception ignored){}}
        if(multicastLock!=null&&multicastLock.isHeld())multicastLock.release();resolveQueue.clear();announced.clear();
    }
}
