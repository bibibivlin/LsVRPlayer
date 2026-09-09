package com.linxyi.vrplayer;

import org.junit.Test;
import static org.junit.Assert.*;
import java.io.*;
import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

public class FormatMetadataTest {
    static byte[] join(byte[]... parts){ByteArrayOutputStream out=new ByteArrayOutputStream();for(byte[] part:parts)out.write(part,0,part.length);return out.toByteArray();}
    static byte[] box(String type,byte[]... payload){byte[] bytes=join(payload);return join(ByteBuffer.allocate(4).putInt(bytes.length+8).array(),type.getBytes(StandardCharsets.US_ASCII),bytes);}
    static byte[] equi(int left,int right){return box("equi",ByteBuffer.allocate(20).putInt(0).putInt(0).putInt(0).putInt(left).putInt(right).array());}
    static byte[] video(byte[]... children){return box("trak",box("mdia",box("hdlr",new byte[8],"vide".getBytes(StandardCharsets.US_ASCII)),box("minf",box("stbl",box("stsd",new byte[8],box("avc1",new byte[78],join(children)))))));}
    static FormatMetadata.Reader reader(byte[] bytes){return new FormatMetadata.Reader(){public long length(){return bytes.length;}public byte[] read(long at,int count)throws IOException{if(at<0||at+count>bytes.length)throw new EOFException();return Arrays.copyOfRange(bytes,(int)at,(int)at+count);}};}
    static FormatMetadata.Hints inspect(byte[] bytes)throws IOException{return FormatMetadata.inspect(reader(bytes));}
    @Test public void detectsIndependentStereoAndProjection()throws Exception{
        FormatMetadata.Hints hints=inspect(box("moov",video(box("st3d",new byte[]{0,0,0,0,2}),box("sv3d",box("proj",equi(0,0))))));
        assertEquals(2,hints.projection);assertEquals(1,hints.stereo);
    }
    @Test public void stereoAloneDoesNotImplyVr()throws Exception{FormatMetadata.Hints hints=inspect(box("moov",video(box("st3d",new byte[]{0,0,0,0,1}))));assertEquals(-1,hints.projection);assertEquals(2,hints.stereo);}
    @Test public void recognizesCentered180Crop()throws Exception{assertEquals(1,inspect(box("moov",video(box("sv3d",box("proj",equi(0x40000000,0x40000000)))))).projection);}
    @Test public void rejectsUnsupportedCrop()throws Exception{assertEquals(1,inspect(box("moov",video(box("sv3d",box("proj",equi(100,0)))))).blockedFields&1);}
    @Test public void meshIsNotTreatedAs180()throws Exception{FormatMetadata.Hints hints=inspect(box("moov",video(box("sv3d",box("proj",box("mshp",new byte[16]))))));assertEquals(-1,hints.projection);assertEquals(3,hints.blockedFields);}
    @Test public void orientationStillBlocksProjectionWhenHeaderComesFirst()throws Exception{assertEquals(1,inspect(box("moov",video(box("sv3d",box("proj",box("prhd",ByteBuffer.allocate(16).putInt(0).putInt(65536).putLong(0).array()),equi(0,0)))))).blockedFields&1);}
    @Test public void refusesMultipleVideoTracks()throws Exception{assertEquals(3,inspect(box("moov",video(),video())).blockedFields);}
    @Test(expected=IOException.class) public void truncatedBoxCannotBorrowTheNextBox()throws Exception{inspect(box("moov",video(box("st3d",new byte[2]),box("free",new byte[32]))));}
    @Test(expected=IOException.class) public void rejectsOversizedBox()throws Exception{inspect(join(ByteBuffer.allocate(4).putInt(10000).array(),"moov".getBytes(StandardCharsets.US_ASCII)));}
    @Test public void skipsLargeMediaPayloadByOffset()throws Exception{
        long offset=(1L<<32)+32;byte[] moov=box("moov",video(box("sv3d",box("proj",equi(0,0)))));byte[] header=join(ByteBuffer.allocate(4).putInt(1).array(),"mdat".getBytes(StandardCharsets.US_ASCII),ByteBuffer.allocate(8).putLong(offset).array());
        int[] bytes={0};FormatMetadata.Reader source=new FormatMetadata.Reader(){public long length(){return offset+moov.length;}public byte[] read(long at,int count)throws IOException{bytes[0]+=count;if(at<16&&at+count<=16)return Arrays.copyOfRange(header,(int)at,(int)at+count);if(at>=offset&&at+count<=offset+moov.length)return Arrays.copyOfRange(moov,(int)(at-offset),(int)(at-offset)+count);throw new IOException("Video payload read");}};
        assertEquals(2,FormatMetadata.inspect(source).projection);assertTrue(bytes[0]<1024);
    }
    @Test(expected=IOException.class) public void readBudgetIsEnforced()throws Exception{FormatMetadata.Bounded bounded=new FormatMetadata.Bounded(reader(new byte[3*1024*1024]),3000);bounded.read(0,2*1024*1024+1);}
    @Test(expected=InterruptedIOException.class) public void expiredProbeStopsReading()throws Exception{new FormatMetadata.Bounded(reader(new byte[16]),-1).read(0,1);}
    static byte[] element(byte[] id,byte[] value){return join(id,ByteBuffer.allocate(2).putShort((short)(0x4000|value.length)).array(),value);}
    static byte[] mkv(int stereo,int projection,boolean mesh){byte[] proj=element(new byte[]{0x76,0x70},join(element(new byte[]{0x76,0x71},new byte[]{(byte)projection}),mesh?element(new byte[]{0x76,0x72},new byte[20]):new byte[0]));byte[] track=element(new byte[]{(byte)0xae},join(element(new byte[]{(byte)0x83},new byte[]{1}),element(new byte[]{(byte)0xe0},join(element(new byte[]{0x53,(byte)0xb8},new byte[]{(byte)stereo}),proj))));return join(element(new byte[]{0x1a,0x45,(byte)0xdf,(byte)0xa3},new byte[0]),element(new byte[]{0x18,0x53,(byte)0x80,0x67},element(new byte[]{0x16,0x54,(byte)0xae,0x6b},track)));}
    @Test public void readsMatroskaProjectionWithoutPrivateData()throws Exception{FormatMetadata.Hints hints=inspect(mkv(3,1,false));assertEquals(2,hints.projection);assertEquals(2,hints.stereo);}
    @Test public void rejectsMatroskaMesh()throws Exception{assertEquals(3,inspect(mkv(1,3,true)).blockedFields);}
    @Test public void parsesMedia3ProjectionBytes()throws Exception{assertEquals(2,FormatMetadata.projectionData(box("proj",equi(0,0))).projection);}
    @Test public void matroskaStopsBeforeUnknownLengthCluster()throws Exception{
        byte[] base=mkv(3,1,false);byte[] cluster=new byte[]{0x1f,0x43,(byte)0xb6,0x75,(byte)0xff};
        // Extend the segment; the unknown cluster is legal streaming media, not metadata.
        byte[] bytes=join(base,cluster);int length=((bytes[10]&63)<<8)|(bytes[11]&255);length+=cluster.length;bytes[10]=(byte)(0x40|(length>>8));bytes[11]=(byte)length;
        assertEquals(2,inspect(bytes).projection);
    }
    static byte[] sphericalUuid(String xml){byte[] uuid=new byte[16];String hex="ffcc8263f8554a938814587a02521fdd";for(int i=0;i<16;i++)uuid[i]=(byte)Integer.parseInt(hex.substring(i*2,i*2+2),16);return box("uuid",uuid,xml.getBytes(StandardCharsets.UTF_8));}
    @Test public void legacySphericalTagsAreRecognized()throws Exception{
        byte[] uuid=sphericalUuid("<rdf><GSpherical:Spherical>true</GSpherical:Spherical><GSpherical:ProjectionType>equirectangular</GSpherical:ProjectionType><GSpherical:StereoMode>left-right</GSpherical:StereoMode></rdf>");
        FormatMetadata.Hints hints=inspect(box("moov",video(uuid)));assertEquals(2,hints.projection);assertEquals(1,hints.stereo);
    }
    @Test public void versionTwoOverridesLegacyRegardlessOfBoxOrder()throws Exception{
        byte[] uuid=sphericalUuid("<Spherical>true</Spherical><ProjectionType>equirectangular</ProjectionType><CroppedAreaImageWidthPixels>100</CroppedAreaImageWidthPixels><FullPanoWidthPixels>400</FullPanoWidthPixels>");
        FormatMetadata.Hints hints=inspect(box("moov",video(uuid,box("sv3d",box("proj",equi(0,0))))));assertEquals(2,hints.projection);assertEquals(0,hints.blockedFields);
    }
}
