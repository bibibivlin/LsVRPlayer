package com.linxyi.vrplayer;

import android.content.Context;
import android.net.Uri;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import com.hierynomus.msdtyp.AccessMask;
import com.hierynomus.mssmb2.SMB2CreateDisposition;
import com.hierynomus.mssmb2.SMB2CreateOptions;
import com.hierynomus.mssmb2.SMB2ShareAccess;
import com.hierynomus.msfscc.FileAttributes;
import com.hierynomus.smbj.SMBClient;
import com.hierynomus.smbj.SmbConfig;
import com.hierynomus.smbj.auth.AuthenticationContext;
import com.hierynomus.smbj.connection.Connection;
import com.hierynomus.smbj.session.Session;
import com.hierynomus.smbj.share.DiskShare;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.TimeUnit;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;
import org.json.*;

final class SmbStore {
    private static final Map<String,JSONObject> servers=new ConcurrentHashMap<>();
    private static Context context;
    static void initialize(Context value){
        context=value.getApplicationContext();
        try {JSONArray records=new JSONArray(context.getSharedPreferences("smb",0).getString("servers","[]"));
            for(int i=0;i<records.length();i++){JSONObject record=records.getJSONObject(i);String secret=record.optString("secret");
                if(!secret.isEmpty()){try{record.put("password",decrypt(secret));}catch(Exception ex){record.put("password","");}}
                servers.put(record.getString("id"),record);
            }
        }catch(Exception ignored){}
    }
    static synchronized String save(String json) throws Exception {
        JSONObject item=new JSONObject(json);
        if(item.optString("host").trim().isEmpty()||item.optString("share").trim().isEmpty())throw new IllegalArgumentException("请填写服务器地址和共享名");
        String id=item.optString("id");if(id.isEmpty())id=UUID.randomUUID().toString();item.put("id",id);
        JSONObject previous=servers.get(id);if(previous!=null&&item.optString("password").isEmpty()&&!item.optString("username").isEmpty())item.put("password",previous.optString("password"));
        item.put("host",item.getString("host").trim());item.put("share",item.getString("share").trim());
        int port=item.optInt("port",445);if(port<1||port>65535)throw new IllegalArgumentException("端口范围为 1–65535");
        servers.put(id,item);persist();return id;
    }
    static synchronized void remove(String id)throws Exception{servers.remove(id);persist();}
    static String list(){JSONArray result=new JSONArray();for(JSONObject record:servers.values()){try{JSONObject safe=new JSONObject(record.toString());safe.remove("password");safe.remove("secret");result.put(safe);}catch(Exception ignored){}}return result.toString();}
    private static void persist()throws Exception{
        JSONArray result=new JSONArray();
        for(JSONObject record:servers.values()){
            JSONObject saved=new JSONObject(record.toString());String password=saved.optString("password");saved.remove("password");saved.remove("secret");
            if(saved.optBoolean("remember")&&!password.isEmpty())saved.put("secret",encrypt(password));
            result.put(saved);
        }
        if(!context.getSharedPreferences("smb",0).edit().putString("servers",result.toString()).commit())throw new java.io.IOException("无法保存服务器设置");
    }
    static String child(String directory,String name){return Uri.parse(directory).buildUpon().appendPath(name).build().toString();}
    static Handle connect(Uri uri)throws Exception{
        JSONObject server=servers.get(uri.getHost());if(server==null)throw new java.io.IOException("找不到 SMB 服务器配置");
        return new Handle(server,path(uri));
    }
    static String path(Uri uri){
        List<String> segments=uri.getPathSegments();
        for(String segment:segments)if(segment.equals("..")||segment.equals(".")||segment.contains("\\")||segment.indexOf('\0')>=0)throw new IllegalArgumentException("无效的共享路径");
        return String.join("\\",segments);
    }
    static final class Handle implements AutoCloseable {
        final NetworkSockets sockets=new NetworkSockets(null,15000);
        final SMBClient client;Connection connection;Session session;DiskShare share;final String path;
        Handle(JSONObject server,String path)throws Exception{
            this.path=path;
            PlayerBridge.attachResource(sockets);
            client=new SMBClient(SmbConfig.builder().withSocketFactory(sockets).withTimeout(15,TimeUnit.SECONDS).withSoTimeout(15000).withEncryptData(server.optBoolean("encrypt")).build());
            try{
                connection=client.connect(server.getString("host"),server.optInt("port",445));String user=server.optString("username");
                session=connection.authenticate(user.isEmpty()?AuthenticationContext.anonymous():new AuthenticationContext(user,server.optString("password").toCharArray(),server.optString("domain")));
                share=(DiskShare)session.connectShare(server.getString("share"));
            }catch(Exception ex){close();throw ex;}
        }
        com.hierynomus.smbj.share.File open(){return share.openFile(path,EnumSet.of(AccessMask.GENERIC_READ),EnumSet.of(FileAttributes.FILE_ATTRIBUTE_NORMAL),SMB2ShareAccess.ALL,SMB2CreateDisposition.FILE_OPEN,EnumSet.of(SMB2CreateOptions.FILE_NON_DIRECTORY_FILE));}
        public void close(){try{if(share!=null)share.close();}catch(Exception ignored){}try{if(session!=null)session.close();}catch(Exception ignored){}try{if(connection!=null)connection.close();}catch(Exception ignored){}try{client.close();}catch(Exception ignored){}sockets.close();}
    }
    private static SecretKey key()throws Exception{
        KeyStore ks=KeyStore.getInstance("AndroidKeyStore");ks.load(null);
        if(!ks.containsAlias("LsVRPlayer.SMB")){KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");generator.init(new KeyGenParameterSpec.Builder("LsVRPlayer.SMB",KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());generator.generateKey();}
        return (SecretKey)ks.getKey("LsVRPlayer.SMB",null);
    }
    private static String encrypt(String value)throws Exception{Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key());return Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP)+":"+Base64.encodeToString(cipher.doFinal(value.getBytes(java.nio.charset.StandardCharsets.UTF_8)),Base64.NO_WRAP);}
    private static String decrypt(String value)throws Exception{String[] parts=value.split(":",2);Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key(),new GCMParameterSpec(128,Base64.decode(parts[0],Base64.NO_WRAP)));return new String(cipher.doFinal(Base64.decode(parts[1],Base64.NO_WRAP)),java.nio.charset.StandardCharsets.UTF_8);}
}
