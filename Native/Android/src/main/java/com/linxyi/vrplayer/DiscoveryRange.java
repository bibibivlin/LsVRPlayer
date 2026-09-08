package com.linxyi.vrplayer;

/** Unsigned IPv4 arithmetic kept independent of Android for subnet regression tests. */
final class DiscoveryRange {
    final long first,last,self;
    long next;
    DiscoveryRange(byte[] address,int prefix){
        if(address.length!=4||prefix<0||prefix>32)throw new IllegalArgumentException("IPv4 prefix");
        self=number(address);long mask=prefix==0?0:(0xffffffffL<<(32-prefix))&0xffffffffL;
        long network=self&mask,broadcast=network|(~mask&0xffffffffL);
        first=prefix<31?network+1:network;last=prefix<31?broadcast-1:broadcast;next=first;
    }
    long count(){return Math.max(0,last-first+1-(self>=first&&self<=last?1:0));}
    boolean hasNext(){return next<=last&&(next!=self||next+1<=last);}
    String take(){if(next==self)next++;if(next>last)return null;return address(next++);}
    boolean contains(byte[] address){if(address.length!=4)return false;long n=number(address);return n>=first&&n<=last;}
    static long number(byte[] address){long n=0;for(byte b:address)n=(n<<8)|(b&255);return n;}
    static String address(long n){return ((n>>>24)&255)+"."+((n>>>16)&255)+"."+((n>>>8)&255)+"."+(n&255);}
}
