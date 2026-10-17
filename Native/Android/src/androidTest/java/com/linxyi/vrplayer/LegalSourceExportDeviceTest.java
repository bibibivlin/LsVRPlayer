package com.linxyi.vrplayer;

import static org.junit.Assert.*;
import android.app.Activity;
import android.content.Context;
import android.content.ContentResolver;
import android.content.res.AssetManager;
import android.net.Uri;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import java.io.InputStream;
import java.security.MessageDigest;
import org.json.JSONObject;
import org.junit.Test;
import org.junit.runner.RunWith;

/** Isolated test package: no player initialization, media permission, activity launch or real app data. */
@RunWith(AndroidJUnit4.class)
public final class LegalSourceExportDeviceTest {
    @Test public void correspondingSourceExportsWithoutMediaPermissionAndCanBeRemoved()throws Exception{
        Context context=InstrumentationRegistry.getInstrumentation().getContext();
        assertEquals(android.content.pm.PackageManager.PERMISSION_DENIED,context.checkSelfPermission("android.permission.READ_EXTERNAL_STORAGE"));
        Activity[] host=new Activity[1];
        InstrumentationRegistry.getInstrumentation().runOnMainSync(()->host[0]=new Activity(){
            @Override public ContentResolver getContentResolver(){return context.getContentResolver();}
            @Override public AssetManager getAssets(){return context.getAssets();}
        });
        int request=PlayerBridge.beginExportLicenseSource(host[0]);Uri owned=null;
        try{
            long deadline=android.os.SystemClock.elapsedRealtime()+30000;String text="";
            while(text.isEmpty()&&android.os.SystemClock.elapsedRealtime()<deadline){text=PlayerBridge.takeResult(request);if(text.isEmpty())Thread.sleep(40);}
            assertFalse("Source export timed out",text.isEmpty());JSONObject result=new JSONObject(text);
            assertTrue(result.toString(),result.getBoolean("ok"));owned=Uri.parse(result.getString("uri"));
            byte[] original,exported;
            try(InputStream stream=context.getAssets().open("Legal/ffmpeg-lgpl-corresponding-source.zip")){original=hash(stream);}
            try(InputStream stream=context.getContentResolver().openInputStream(owned)){exported=hash(stream);}
            assertArrayEquals(original,exported);
            try(android.database.Cursor row=context.getContentResolver().query(owned,new String[]{"is_pending"},null,null,null)){
                assertNotNull(row);assertTrue(row.moveToFirst());assertEquals(0,row.getInt(0));
            }
        }finally{
            PlayerBridge.cancelRequest(request);
            // Delete only the URI returned by this test's own successful insert.
            if(owned!=null){assertEquals(1,context.getContentResolver().delete(owned,null,null));
                try(android.database.Cursor row=context.getContentResolver().query(owned,new String[]{"_id"},null,null,null)){assertTrue(row==null||row.getCount()==0);}}
        }
        assertNull("Export must not initialize playback or remembered credentials",PlayerBridge.activity);
    }
    private static byte[] hash(InputStream stream)throws Exception{
        MessageDigest digest=MessageDigest.getInstance("SHA-256");byte[] buffer=new byte[65536];int count;
        while((count=stream.read(buffer))!=-1)digest.update(buffer,0,count);return digest.digest();
    }
}
