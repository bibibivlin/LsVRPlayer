package com.linxyi.vrplayer;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import java.security.KeyStore;
import java.util.Iterator;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import org.json.JSONArray;
import org.json.JSONObject;

/** Only remembered access tokens are persisted, encrypted in app-private preferences. */
final class EmbyAccountVault {
    private static final String ALIAS="LsVRPlayer.EmbyAccounts";
    private static Context context;
    static synchronized void initialize(Context value){context=value.getApplicationContext();}
    static synchronized String load(){
        if(context==null)return "";
        try {
            String envelope=context.getSharedPreferences("emby_accounts",Context.MODE_PRIVATE).getString("vault","");
            return envelope.isEmpty()?"":EmbySecretEnvelope.decrypt(envelope,key());
        }catch(Exception ignored){return "";}
    }
    static synchronized boolean save(String json){
        if(context==null||json==null||json.length()>1024*1024)return false;
        try {
            JSONObject payload=new JSONObject(json);rejectPasswords(payload);
            String envelope=EmbySecretEnvelope.encrypt(payload.toString(),key());
            return context.getSharedPreferences("emby_accounts",Context.MODE_PRIVATE).edit().putString("vault",envelope).commit();
        }catch(Exception ignored){return false;}
    }
    private static void rejectPasswords(Object value)throws Exception {
        if(value instanceof JSONObject){
            JSONObject object=(JSONObject)value;
            for(Iterator<String> keys=object.keys();keys.hasNext();){String name=keys.next();if(name.toLowerCase(java.util.Locale.ROOT).contains("password")||name.equalsIgnoreCase("pw"))throw new IllegalArgumentException("Passwords cannot be persisted");rejectPasswords(object.get(name));}
        }else if(value instanceof JSONArray){JSONArray values=(JSONArray)value;for(int i=0;i<values.length();i++)rejectPasswords(values.get(i));}
    }
    private static SecretKey key()throws Exception {
        KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
        if(!store.containsAlias(ALIAS)){
            KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
            generator.init(new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setRandomizedEncryptionRequired(true).build());generator.generateKey();
        }
        return (SecretKey)store.getKey(ALIAS,null);
    }
}
