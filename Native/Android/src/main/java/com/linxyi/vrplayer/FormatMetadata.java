package com.linxyi.vrplayer;

import java.io.*;
import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.regex.*;

/** Bounded, seek-based metadata inspection. Never walks compressed video payloads. */
final class FormatMetadata {
    interface Reader extends AutoCloseable { long length() throws IOException; byte[] read(long offset,int count) throws IOException; default void close() throws Exception {} }
    static final class Hints {
        int projection=-1,stereo=-1,half=-1,swapEyes=-1,blockedFields;
        String unsupported="",reason="容器元数据";
        boolean video;int trackId=-1;String legacyXml="";
        void block(int fields,String why){blockedFields|=fields;unsupported=why;if((fields&1)!=0)projection=-1;if((fields&2)!=0)stereo=-1;}
    }
    static final class Bounded implements Reader {
        final Reader source;final long deadline;int remaining=2*1024*1024,operations;
        Bounded(Reader source,long timeoutMillis){this.source=source;deadline=System.nanoTime()+timeoutMillis*1000000;}
        public long length() throws IOException{return source.length();}
        public byte[] read(long offset,int count) throws IOException {
            if(Thread.currentThread().isInterrupted()||System.nanoTime()>deadline)throw new InterruptedIOException("Metadata deadline");
            if(offset<0||count<0||count>remaining||++operations>10000)throw new IOException("Metadata budget");
            remaining-=count;byte[] bytes=source.read(offset,count);if(bytes.length!=count)throw new EOFException();return bytes;
        }
    }
    static Hints inspect(Reader source) throws IOException {
        Bounded reader=new Bounded(source,3000);long length=reader.length();if(length<8)throw new EOFException();
        byte[] header=reader.read(0,8);List<Hints> tracks=new ArrayList<>();
        if(u32(header,0)==0x1a45dfa3L)ebml(reader,0,length,null,tracks,0);
        else mp4(reader,0,length,null,tracks,0);
        List<Hints> videos=new ArrayList<>();for(Hints track:tracks)if(track.video)videos.add(track);
        if(videos.size()==1)return videos.get(0);
        Hints hints=new Hints();if(videos.size()>1)hints.block(3,"多视频轨道，使用当前轨道标签或手动选择");return hints;
    }
    private static void mp4(Bounded reader,long start,long end,Hints track,List<Hints> tracks,int depth) throws IOException {
        if(depth>12)throw new IOException("Box nesting");long p=start;
        while(p<end){
            if(end-p<8)throw new EOFException();byte[] header=reader.read(p,8);long size=u32(header,0);String type=fourcc(header,4);int prefix=8;
            if(size==1){size=i64(reader.read(p+8,8),0);prefix=16;}else if(size==0)size=end-p;
            if(size<prefix||size>end-p)throw new IOException("Box bounds");long data=p+prefix,next=p+size;
            if(type.equals("trak")){Hints child=new Hints();mp4(reader,data,next,child,tracks,depth+1);if(!child.legacyXml.isEmpty())legacy(child,child.legacyXml);tracks.add(child);}
            else if(Arrays.asList("moov","mdia","minf","stbl","sv3d","proj").contains(type))mp4(reader,data,next,track,tracks,depth+1);
            else if(type.equals("stsd")){if(next-data<8)throw new EOFException();mp4(reader,data+8,next,track,tracks,depth+1);}
            else if(Arrays.asList("avc1","avc3","hvc1","hev1","vp09","av01","encv","dvh1","dvhe","mp4v").contains(type)){
                if(next-data<78)throw new EOFException();mp4(reader,data+78,next,track,tracks,depth+1);
            }else if(track!=null){
                int required=type.equals("st3d")?5:type.equals("prhd")?16:type.equals("equi")?20:type.equals("cbmp")?12:0;
                if(next-data<required)throw new EOFException();
                if(type.equals("hdlr")){if(next-data<12)throw new EOFException();track.video=fourcc(reader.read(data,12),8).equals("vide");}
                else if(type.equals("tkhd")){byte[] value=reader.read(data,(int)Math.min(24,next-data));int at=value[0]==1?20:12;if(value.length<at+4)throw new EOFException();track.trackId=(int)u32(value,at);}
                else if(type.equals("st3d")){byte[] value=reader.read(data,5);if(value[0]!=0)track.block(2,"未知立体元数据版本");else stereo(track,value[4]&255,false);}
                else if(type.equals("prhd")){byte[] value=reader.read(data,16);if(u32(value,0)!=0||u32(value,4)!=0||u32(value,8)!=0||u32(value,12)!=0)track.block(1,"带方向变换的投影暂需手动选择");}
                else if(type.equals("equi"))equirect(track,reader.read(data,20));
                else if(type.equals("cbmp"))cubemap(track,reader.read(data,12));
                else if(type.equals("mshp"))track.block(3,"VR 网格投影暂不支持，不能直接当作 VR180");
                else if(type.equals("uuid")&&next-data>=16){byte[] uuid=reader.read(data,16);if(hex(uuid).equals("ffcc8263f8554a938814587a02521fdd"))track.legacyXml=new String(reader.read(data+16,(int)Math.min(Integer.MAX_VALUE,next-data-16)),StandardCharsets.UTF_8);}
            }
            if(type.equals("moov"))return; // The complete track table is sufficient; do not scan fragments.
            p=next;
        }
    }
    static Hints projectionData(byte[] data) throws IOException {
        Hints hints=new Hints();if(data==null||data.length<8)return hints;
        Reader array=new Reader(){public long length(){return data.length;}public byte[] read(long at,int count)throws IOException{if(at<0||at+count>data.length)throw new EOFException();return Arrays.copyOfRange(data,(int)at,(int)at+count);}};
        mp4(new Bounded(array,100),0,data.length,hints,new ArrayList<>(),0);return hints;
    }
    private static void equirect(Hints hints,byte[] data) throws IOException {
        if(data.length<20)throw new EOFException();if(u32(data,0)!=0){hints.block(1,"未知等距柱状版本");return;}
        long top=u32(data,4),bottom=u32(data,8),left=u32(data,12),right=u32(data,16);
        if(top==0&&bottom==0&&left==0&&right==0)hints.projection=2;
        else if(top==0&&bottom==0&&left==0x40000000L&&right==0x40000000L)hints.projection=1;
        else hints.block(1,"该等距柱状裁剪范围暂不支持");
    }
    private static void cubemap(Hints hints,byte[] data) throws IOException {
        if(data.length<12)throw new EOFException();if(u32(data,0)==0&&u32(data,4)==0&&u32(data,8)==0)hints.projection=4;else hints.block(1,"该 Cubemap 面排列或边距暂不支持");
    }
    private static void stereo(Hints hints,int mode,boolean matroska){
        if(mode==0)hints.stereo=0;
        else if(mode==(matroska?1:2))hints.stereo=1;
        else if(mode==(matroska?3:1))hints.stereo=2;
        else hints.block(2,"该立体排列暂不支持");
    }
    private static String tag(String xml,String name){Matcher match=Pattern.compile("<(?:[A-Za-z0-9_]+:)?"+name+">\\s*([^<]+)",Pattern.CASE_INSENSITIVE).matcher(xml);return match.find()?match.group(1).trim():"";}
    private static void legacy(Hints hints,String xml){
        if(!tag(xml,"Spherical").equalsIgnoreCase("true")||tag(xml,"Stitched").equalsIgnoreCase("false")||!tag(xml,"ProjectionType").equalsIgnoreCase("equirectangular"))return;
        // V2 takes precedence over V1 when both are present.
        if(hints.projection<0&&(hints.blockedFields&1)==0){
            if(!tag(xml,"CroppedAreaImageWidthPixels").isEmpty()&&(!tag(xml,"CroppedAreaImageWidthPixels").equals(tag(xml,"FullPanoWidthPixels"))||!tag(xml,"CroppedAreaImageHeightPixels").equals(tag(xml,"FullPanoHeightPixels"))))hints.block(1,"旧版球面裁剪暂不支持");else hints.projection=2;
        }
        if(hints.stereo<0){String layout=tag(xml,"StereoMode");if(layout.equals("left-right"))hints.stereo=1;else if(layout.equals("top-bottom"))hints.stereo=2;else if(layout.equals("mono"))hints.stereo=0;}
        if(!tag(xml,"InitialViewHeadingDegrees").isEmpty()&&!tag(xml,"InitialViewHeadingDegrees").matches("0(?:\\.0+)?"))hints.block(1,"旧版球面方向暂需手动选择");
    }
    private static final class Vint {long value;int length;boolean unknown;}
    private static Vint vint(Bounded reader,long offset,boolean id)throws IOException{
        int first=reader.read(offset,1)[0]&255,mask=128,length=1;while(length<=8&&(first&mask)==0){mask>>=1;length++;}
        if(length>8||(id&&length>4))throw new IOException("EBML integer");
        Vint value=new Vint();value.length=length;value.value=id?first:first&(mask-1);boolean unknown=!id&&value.value==mask-1;
        if(length>1)for(byte b:reader.read(offset+1,length-1)){value.value=(value.value<<8)|(b&255);unknown&=(b&255)==255;}value.unknown=unknown;return value;
    }
    private static void ebml(Bounded reader,long start,long end,Hints track,List<Hints> tracks,int depth)throws IOException{
        if(depth>10)throw new IOException("EBML nesting");long p=start;
        while(p<end){Vint id=vint(reader,p,true),size=vint(reader,p+id.length,false);long data=p+id.length+size.length;
            if(size.unknown&&id.value!=0x18538067L)throw new IOException("Unknown EBML size");long next=size.unknown?end:data+size.value;if(next<data||next>end)throw new EOFException();
            long type=id.value;
            if(type==0xae){Hints child=new Hints();ebml(reader,data,next,child,tracks,depth+1);tracks.add(child);}
            else if(type==0x1654ae6bL){ebml(reader,data,next,track,tracks,depth+1);return;}
            else if(type==0x18538067L||type==0xe0)ebml(reader,data,next,track,tracks,depth+1);
            else if(track!=null){
                if(type==0x83)track.video=integer(reader,data,next)==1;
                else if(type==0xd7)track.trackId=(int)integer(reader,data,next);
                else if(type==0x53b8)stereo(track,(int)integer(reader,data,next),true);
                else if(type==0x7670)projectionEbml(reader,data,next,track);
            }
            p=next;
        }
    }
    private static void projectionEbml(Bounded reader,long start,long end,Hints track)throws IOException{
        long p=start;int type=-1;byte[] data=null;boolean rotated=false;
        while(p<end){Vint id=vint(reader,p,true),size=vint(reader,p+id.length,false);long at=p+id.length+size.length,next=at+size.value;if(size.unknown||next<at||next>end)throw new EOFException();
            if(id.value==0x7671)type=(int)integer(reader,at,next);
            else if(id.value==0x7672){if(size.value>2*1024*1024)throw new IOException("Projection size");data=reader.read(at,(int)size.value);}
            else if(id.value>=0x7673&&id.value<=0x7675){byte[] number=reader.read(at,(int)size.value);double degrees=number.length==4?ByteBuffer.wrap(number).getFloat():number.length==8?ByteBuffer.wrap(number).getDouble():Double.NaN;rotated|=degrees!=0;}
            p=next;
        }
        if(type==0)track.projection=0;
        else if(type==1)equirect(track,data==null?new byte[20]:data);
        else if(type==2){if(data==null)throw new IOException("Cubemap metadata missing");cubemap(track,data);}
        else if(type==3)track.block(3,"VR 网格投影暂不支持");
        else track.block(1,"未知投影类型");
        if(rotated)track.block(1,"带方向变换的投影暂需手动选择");
    }
    private static long integer(Bounded reader,long start,long end)throws IOException{if(end-start<1||end-start>8)throw new IOException("Integer size");long result=0;for(byte b:reader.read(start,(int)(end-start)))result=(result<<8)|(b&255);return result;}
    private static long u32(byte[] data,int at){return ((long)(data[at]&255)<<24)|((long)(data[at+1]&255)<<16)|((long)(data[at+2]&255)<<8)|(data[at+3]&255);}
    private static long i64(byte[] data,int at){return ByteBuffer.wrap(data,at,8).getLong();}
    private static String fourcc(byte[] data,int at){return new String(data,at,4,StandardCharsets.US_ASCII);}
    private static String hex(byte[] data){StringBuilder text=new StringBuilder();for(byte b:data)text.append(String.format(Locale.ROOT,"%02x",b&255));return text.toString();}
}
