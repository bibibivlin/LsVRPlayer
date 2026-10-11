package com.linxyi.vrplayer;

import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import java.security.GeneralSecurityException;
import org.junit.Test;
import static org.junit.Assert.*;

public class EmbySecretEnvelopeTest {
    private static SecretKey key()throws Exception {KeyGenerator generator=KeyGenerator.getInstance("AES");generator.init(256);return generator.generateKey();}
    @Test public void rememberedTokenIsEncryptedAndRoundTrips()throws Exception {
        SecretKey key=key();String payload="{\"tokens\":[{\"id\":\"account\",\"accessToken\":\"private-session-token\"}]}";
        String saved=EmbySecretEnvelope.encrypt(payload,key);assertFalse(saved.contains("private-session-token"));assertEquals(payload,EmbySecretEnvelope.decrypt(saved,key));
    }
    @Test public void savingSameTokenGeneratesDistinctNonce()throws Exception {SecretKey key=key();assertNotEquals(EmbySecretEnvelope.encrypt("same-token",key),EmbySecretEnvelope.encrypt("same-token",key));}
    @Test(expected=GeneralSecurityException.class) public void corruptedCiphertextCannotReturnAToken()throws Exception {
        SecretKey key=key();String saved=EmbySecretEnvelope.encrypt("session",key);String[] parts=saved.split(":");byte[] ciphertext=java.util.Base64.getDecoder().decode(parts[2]);ciphertext[0]^=1;EmbySecretEnvelope.decrypt(parts[0]+":"+parts[1]+":"+java.util.Base64.getEncoder().encodeToString(ciphertext),key);
    }
    @Test(expected=GeneralSecurityException.class) public void anotherDeviceKeyCannotReadRememberedToken()throws Exception {EmbySecretEnvelope.decrypt(EmbySecretEnvelope.encrypt("session",key()),key());}
    @Test(expected=GeneralSecurityException.class) public void unsupportedVersionIsRejected()throws Exception {EmbySecretEnvelope.decrypt("2:bad:bad",key());}
}
