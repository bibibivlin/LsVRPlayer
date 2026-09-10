package com.linxyi.vrplayer;

import java.io.*;
import java.util.Arrays;
import java.util.concurrent.*;
import org.junit.Test;
import static org.junit.Assert.*;

public class ReadAheadReaderTest {
    private static final class Source implements ReadAheadReader.Source {
        int calls,maxChunk=Integer.MAX_VALUE,maxRequest;long lastEnd;
        public int read(byte[] data,long position,int offset,int length){
            calls++;maxRequest=Math.max(maxRequest,length);int count=Math.min(length,maxChunk);lastEnd=position+count;
            for(int i=0;i<count;i++)data[offset+i]=(byte)((position+i)%251);return count;
        }
    }
    @Test public void consecutiveNalLengthReadsUseOneNetworkRequest()throws Exception{
        Source source=new Source();int size=1024*1024;ReadAheadReader reader=new ReadAheadReader(source,new byte[size],0,size,true);byte[] out=new byte[4];
        for(int i=0;i<size/4;i++){assertEquals(4,reader.read(out,0,4));assertEquals((byte)((i*4)%251),out[0]);}
        assertEquals(1,source.calls);assertEquals(size,source.maxRequest);assertEquals(-1,reader.read(out,0,4));assertEquals(0,reader.read(out,0,0));
    }
    @Test public void blockCrossingAndServerShortReadsPreserveEveryByte()throws Exception{
        Source source=new Source();source.maxChunk=17;ReadAheadReader reader=new ReadAheadReader(source,new byte[64],30,137,true);
        ByteArrayOutputStream output=new ByteArrayOutputStream();byte[] buffer=new byte[31];int count;
        while((count=reader.read(buffer,3,25))!=-1)output.write(buffer,3,count);
        byte[] bytes=output.toByteArray();assertEquals(137,bytes.length);for(int i=0;i<bytes.length;i++)assertEquals((byte)((30+i)%251),bytes[i]);
        assertEquals(167,source.lastEnd);assertEquals(0,reader.remaining());
    }
    @Test public void rangeAboveFourGiBNeverReadsPastItsEnd()throws Exception{
        Source source=new Source();long start=(1L<<32)+53;ReadAheadReader reader=new ReadAheadReader(source,new byte[1024],start,11,true);byte[] data=new byte[40];
        assertEquals(11,reader.read(data,0,40));assertEquals((byte)(start%251),data[0]);assertEquals(start+11,source.lastEnd);assertEquals(11,source.maxRequest);
    }
    @Test public void reopeningSameBufferDiscardsPreviousRange()throws Exception{
        Source source=new Source();byte[] cache=new byte[64],target=new byte[4];ReadAheadReader first=new ReadAheadReader(source,cache,0,100,true);first.read(target,0,4);first.cancel();
        ReadAheadReader second=new ReadAheadReader(source,cache,1000,100,true);second.read(target,0,4);assertEquals((byte)(1000%251),target[0]);assertEquals(2,source.calls);
        try{first.read(target,0,4);fail("Cancelled cached bytes must not escape");}catch(InterruptedIOException expected){}
    }
    @Test public void cancellationDiscardsAnInflightRead()throws Exception{
        CountDownLatch entered=new CountDownLatch(1),release=new CountDownLatch(1);byte[] target=new byte[4];Arrays.fill(target,(byte)99);
        ReadAheadReader reader=new ReadAheadReader((data,position,offset,length)->{entered.countDown();try{if(!release.await(2,TimeUnit.SECONDS))throw new IOException("Timed out");}catch(InterruptedException ex){throw new InterruptedIOException();}Arrays.fill(data,(byte)7);return length;},new byte[16],0,100,true);
        ExecutorService worker=Executors.newSingleThreadExecutor();
        try{Future<?> pending=worker.submit(()->{try{reader.read(target,0,4);throw new AssertionError("Expected cancellation");}catch(InterruptedIOException expected){}catch(IOException ex){throw new AssertionError(ex);}});
            assertTrue(entered.await(1,TimeUnit.SECONDS));reader.cancel();release.countDown();pending.get(2,TimeUnit.SECONDS);assertArrayEquals(new byte[]{99,99,99,99},target);
        }finally{release.countDown();worker.shutdownNow();}
    }
    @Test public void baselineSwitchPreservesSmallReadRequests()throws Exception{
        Source source=new Source();ReadAheadReader reader=new ReadAheadReader(source,new byte[64],0,100,false);byte[] out=new byte[4];
        reader.read(out,0,4);reader.read(out,0,4);assertEquals(2,source.calls);assertEquals(4,source.maxRequest);assertEquals((byte)4,out[0]);
    }
    @Test public void prematureEofAndZeroReadAreErrors()throws Exception{
        for(int result:new int[]{-1,0,65}){
            ReadAheadReader reader=new ReadAheadReader((a,p,o,n)->result,new byte[64],0,100,true);
            try{reader.read(new byte[4],0,4);fail("Expected invalid upstream length");}catch(IOException expected){}
        }
    }
    @Test public void emptyRangeDoesNotContactServer()throws Exception{
        Source source=new Source();ReadAheadReader reader=new ReadAheadReader(source,new byte[64],100,0,true);
        assertEquals(-1,reader.read(new byte[4],0,4));assertEquals(0,source.calls);
    }
}
