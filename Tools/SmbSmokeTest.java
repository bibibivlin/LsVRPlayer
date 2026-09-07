import com.hierynomus.smbj.SMBClient;
import com.hierynomus.smbj.auth.AuthenticationContext;
import com.hierynomus.smbj.connection.Connection;
import com.hierynomus.smbj.session.Session;
import com.hierynomus.smbj.share.DiskShare;
import com.hierynomus.msdtyp.AccessMask;
import com.hierynomus.msfscc.FileAttributes;
import com.hierynomus.mssmb2.*;
import java.nio.file.*;
import java.io.RandomAccessFile;
import java.util.*;

public final class SmbSmokeTest {
    public static void main(String[] args)throws Exception{
        Path root=Path.of(args[0]);
        try(SMBClient client=new SMBClient();Connection connection=client.connect("127.0.0.1",1445);Session session=connection.authenticate(AuthenticationContext.anonymous());DiskShare share=(DiskShare)session.connectShare("TestMedia")){
            System.out.println("SMB directory entries: "+share.list("").size());
            for(String name:List.of("Baseline.2D.mp4","Baseline.HEVC.mkv","Large-offset.bin")){
                try(com.hierynomus.smbj.share.File file=share.openFile(name,EnumSet.of(AccessMask.GENERIC_READ),EnumSet.noneOf(FileAttributes.class),SMB2ShareAccess.ALL,SMB2CreateDisposition.FILE_OPEN,EnumSet.of(SMB2CreateOptions.FILE_NON_DIRECTORY_FILE));RandomAccessFile reference=new RandomAccessFile(root.resolve(name).toFile(),"r")){
                    long length=file.getFileInformation().getStandardInformation().getEndOfFile();if(length!=reference.length())throw new AssertionError("Length mismatch");
                    for(long offset:new long[]{0,length/2,length-1024}){byte[] actual=new byte[1024],expected=new byte[1024];readFully(file,actual,offset);reference.seek(offset);reference.readFully(expected);if(!Arrays.equals(actual,expected))throw new AssertionError(name+" mismatch at "+offset);}
                    System.out.println("PASS head/middle/tail: "+name+" ("+length+" bytes)");
                }
            }
            try(com.hierynomus.smbj.share.File file=share.openFile("Baseline.2D.zh.srt",EnumSet.of(AccessMask.GENERIC_READ),EnumSet.noneOf(FileAttributes.class),SMB2ShareAccess.ALL,SMB2CreateDisposition.FILE_OPEN,EnumSet.of(SMB2CreateOptions.FILE_NON_DIRECTORY_FILE))){byte[] bytes=new byte[Math.toIntExact(file.getFileInformation().getStandardInformation().getEndOfFile())];readFully(file,bytes,0);if(!new String(bytes,"GB18030").contains("你好"))throw new AssertionError("Subtitle encoding");System.out.println("PASS SMB Chinese subtitle bytes");}
        }
    }
    private static void readFully(com.hierynomus.smbj.share.File file,byte[] data,long position){int offset=0;while(offset<data.length){int read=file.read(data,position+offset,offset,data.length-offset);if(read<=0)throw new AssertionError("Unexpected EOF");offset+=read;}}
}
