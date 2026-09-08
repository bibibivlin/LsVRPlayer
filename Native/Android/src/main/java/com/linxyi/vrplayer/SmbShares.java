package com.linxyi.vrplayer;

import com.hierynomus.smbj.SMBClient;
import com.hierynomus.smbj.SmbConfig;
import com.hierynomus.smbj.auth.AuthenticationContext;
import com.hierynomus.smbj.connection.Connection;
import com.hierynomus.smbj.session.Session;
import com.rapid7.client.dcerpc.transport.SMBTransportFactories;
import com.rapid7.client.dcerpc.mssrvs.ServerService;
import com.rapid7.client.dcerpc.mssrvs.dto.NetShareInfo1;
import java.util.concurrent.TimeUnit;
import org.json.*;

final class SmbShares implements AutoCloseable {
    private final JSONObject server;
    private final NetworkSockets sockets=new NetworkSockets(null,5000);
    SmbShares(String json)throws JSONException{server=new JSONObject(json);}
    JSONObject run()throws Exception {
        SmbConfig config=SmbConfig.builder().withSocketFactory(sockets).withTimeout(5,TimeUnit.SECONDS).withSoTimeout(5000).withEncryptData(server.optBoolean("encrypt")).build();
        try(SMBClient client=new SMBClient(config);Connection connection=client.connect(server.getString("host"),server.optInt("port",445))){
            String user=server.optString("username");
            try(Session session=connection.authenticate(user.isEmpty()?AuthenticationContext.anonymous():new AuthenticationContext(user,server.optString("password").toCharArray(),server.optString("domain")))){
                ServerService service=new ServerService(SMBTransportFactories.SRVSVC.getTransport(session));JSONArray shares=new JSONArray();
                for(NetShareInfo1 share:service.getShares1()){
                    if(Thread.currentThread().isInterrupted())throw new InterruptedException();
                    // Disk shares, including administratively hidden disk shares; never IPC/printers.
                    if((share.getType()&0xffff)==0&&!"IPC$".equalsIgnoreCase(share.getNetName()))shares.put(new JSONObject().put("name",share.getNetName()).put("comment",share.getRemark()));
                }
                return new JSONObject().put("shares",shares);
            }
        }finally{close();}
    }
    @Override public void close(){sockets.close();}
}
