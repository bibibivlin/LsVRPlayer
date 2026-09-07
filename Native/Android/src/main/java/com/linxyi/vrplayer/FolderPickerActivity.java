package com.linxyi.vrplayer;
import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;

public final class FolderPickerActivity extends Activity {
    @Override public void onCreate(Bundle saved){super.onCreate(saved);if(saved==null){try{Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION|Intent.FLAG_GRANT_PREFIX_URI_PERMISSION);startActivityForResult(intent,10);}catch(Exception ex){PlayerBridge.pickerResult="ERROR:系统目录选择器不可用，请使用本地目录权限";finish();}}}
    @Override protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);
        if(result==RESULT_OK&&data!=null&&data.getData()!=null){try{getContentResolver().takePersistableUriPermission(data.getData(),Intent.FLAG_GRANT_READ_URI_PERMISSION);PlayerBridge.pickerResult=data.getData().toString();}catch(Exception ex){PlayerBridge.pickerResult="ERROR:未能保存目录授权";}}
        else PlayerBridge.pickerResult="CANCELLED";finish();
    }
}
