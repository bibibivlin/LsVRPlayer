package com.linxyi.vrplayer;

import java.io.*;
import java.util.LinkedHashMap;

/** Preview-only random read cache: bounded memory and transfer, including for multi-GB originals. */
final class ThumbnailRangeCache {
    static final int BLOCK=512*1024,CAPACITY=16;
    static final long MAX_BYTES=128L*1024*1024;
    private final FormatMetadata.Reader reader;
    private final long length,budget;
    private final LinkedHashMap<Long,byte[]> blocks=new LinkedHashMap<>(CAPACITY,.75f,true);
    long readBytes,readCalls;
    static final class BudgetExceeded extends IOException {BudgetExceeded(){super("Preview read budget exhausted");}}
    ThumbnailRangeCache(FormatMetadata.Reader reader,long length){this(reader,length,MAX_BYTES);}
    ThumbnailRangeCache(FormatMetadata.Reader reader,long length,long budget){this.reader=reader;this.length=length;this.budget=budget;}
    int read(long position,byte[] buffer,int offset,int size)throws IOException {
        if(position<0||offset<0||size<0||offset>buffer.length-size)throw new IOException("Invalid preview range");
        if(size==0)return 0;if(position>=length)return -1;
        long start=position/BLOCK*BLOCK;byte[] block=blocks.get(start);
        if(block==null){
            int count=(int)Math.min(BLOCK,length-start);
            if(count>budget-readBytes)throw new BudgetExceeded();
            readCalls++;block=reader.read(start,count);readBytes+=block.length;
            if(block.length!=count)throw new EOFException("Incomplete preview range");
            if(blocks.size()>=CAPACITY)blocks.remove(blocks.keySet().iterator().next());
            blocks.put(start,block);
        }
        int index=(int)(position-start),count=Math.min(size,block.length-index);
        System.arraycopy(block,index,buffer,offset,count);return count;
    }
    void clear(){blocks.clear();}
    void beginRequest(){readBytes=readCalls=0;}
}
