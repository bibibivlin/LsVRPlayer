package com.linxyi.vrplayer;

import static org.junit.Assert.*;
import java.io.*;
import org.junit.Test;

public final class ThumbnailRangeCacheTest {
    private static final class Reader implements FormatMetadata.Reader {
        int calls;long transferred;final long length;
        Reader(long length){this.length=length;}
        public long length(){return length;}
        public byte[] read(long position,int count){
            assertTrue(position>=0&&position+count<=length);calls++;transferred+=count;
            byte[] bytes=new byte[count];for(int i=0;i<count;i++)bytes[i]=(byte)((position+i)%251);return bytes;
        }
    }
    @Test public void alternatingHeaderAndDistantFrameReadsReuseRangesBeyondFourGiB()throws Exception {
        Reader source=new Reader(20L*1024*1024*1024);ThumbnailRangeCache cache=new ThumbnailRangeCache(source,source.length());
        byte[] bytes=new byte[8192];long distant=9L*1024*1024*1024+123;
        for(int i=0;i<50;i++){
            assertEquals(bytes.length,cache.read(distant,bytes,0,bytes.length));
            for(int n=0;n<bytes.length;n++)assertEquals((byte)((distant+n)%251),bytes[n]);
            cache.read(0,bytes,0,64);
        }
        assertEquals(2,source.calls);assertEquals(2L*ThumbnailRangeCache.BLOCK,source.transferred);
    }
    @Test public void cacheHitsDoNotConsumeTransferBudgetAndUncachedReadsFailAtTheBound()throws Exception {
        Reader source=new Reader(32L*ThumbnailRangeCache.BLOCK);
        ThumbnailRangeCache cache=new ThumbnailRangeCache(source,source.length(),2L*ThumbnailRangeCache.BLOCK);byte[] bytes=new byte[64];
        for(int i=0;i<100;i++)cache.read(0,bytes,0,bytes.length);
        cache.read(ThumbnailRangeCache.BLOCK,bytes,0,bytes.length);
        try{cache.read(2L*ThumbnailRangeCache.BLOCK,bytes,0,bytes.length);fail("Transfer must remain bounded");}
        catch(ThumbnailRangeCache.BudgetExceeded expected){}
        assertEquals(2,source.calls);
    }
    @Test public void longHighBitrateIndexCanExceedOldBudgetWithoutKeepingAllTransferredBytes()throws Exception {
        Reader source=new Reader(256L*1024*1024);ThumbnailRangeCache cache=new ThumbnailRangeCache(source,source.length());byte[] bytes=new byte[1];
        for(long at=0;at<32L*1024*1024;at+=ThumbnailRangeCache.BLOCK)cache.read(at,bytes,0,1);
        assertEquals(32L*1024*1024,source.transferred);
        int before=source.calls;cache.read(31L*1024*1024,bytes,0,1);assertEquals(before,source.calls);
        cache.read(0,bytes,0,1);assertEquals(before+1,source.calls);
    }
    @Test public void tailReadAndBlockBoundaryReturnOnlyAvailableBytes()throws Exception {
        Reader source=new Reader(ThumbnailRangeCache.BLOCK+3);ThumbnailRangeCache cache=new ThumbnailRangeCache(source,source.length());
        byte[] bytes=new byte[16];assertEquals(2,cache.read(ThumbnailRangeCache.BLOCK-2,bytes,1,8));
        assertEquals(3,cache.read(ThumbnailRangeCache.BLOCK,bytes,1,8));assertEquals(-1,cache.read(source.length(),bytes,0,8));
        assertEquals(0,cache.read(source.length(),bytes,0,0));assertEquals(source.length(),source.transferred);
        try{cache.read(0,bytes,12,8);fail("Invalid destination range");}catch(IOException expected){}
    }
    @Test public void nextTargetGetsAFreshBudgetWithoutLosingCachedIndexBytes()throws Exception {
        Reader source=new Reader(8L*ThumbnailRangeCache.BLOCK);ThumbnailRangeCache cache=new ThumbnailRangeCache(source,source.length(),ThumbnailRangeCache.BLOCK);
        byte[] bytes=new byte[1];cache.read(0,bytes,0,1);cache.beginRequest();cache.read(0,bytes,0,1);
        assertEquals(0,cache.readBytes);assertEquals(0,cache.readCalls);
        cache.read(ThumbnailRangeCache.BLOCK,bytes,0,1);assertEquals(2,source.calls);
    }
}
