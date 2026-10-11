package com.linxyi.vrplayer;

import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.util.Base64;
import javax.crypto.Cipher;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Versioned authenticated encryption; the AndroidKeyStore key never leaves the device. */
final class EmbySecretEnvelope {
    private static final byte[] PURPOSE="LsVRPlayer.EmbyAccounts.v1".getBytes(StandardCharsets.UTF_8);
    static String encrypt(String value,SecretKey key)throws GeneralSecurityException {
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key);cipher.updateAAD(PURPOSE);
        return "1:"+Base64.getEncoder().encodeToString(cipher.getIV())+":"+Base64.getEncoder().encodeToString(cipher.doFinal(value.getBytes(StandardCharsets.UTF_8)));
    }
    static String decrypt(String value,SecretKey key)throws GeneralSecurityException {
        String[] parts=value.split(":",-1);
        if(parts.length!=3||!parts[0].equals("1"))throw new GeneralSecurityException("Invalid account envelope");
        try {
            byte[] iv=Base64.getDecoder().decode(parts[1]);if(iv.length!=12)throw new GeneralSecurityException("Invalid account nonce");
            Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key,new GCMParameterSpec(128,iv));cipher.updateAAD(PURPOSE);
            return new String(cipher.doFinal(Base64.getDecoder().decode(parts[2])),StandardCharsets.UTF_8);
        }catch(IllegalArgumentException ex){throw new GeneralSecurityException("Invalid account envelope");}
    }
}
