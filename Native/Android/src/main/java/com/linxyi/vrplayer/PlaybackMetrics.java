package com.linxyi.vrplayer;

/** Primitive counters on the loader thread; the diagnostics worker calculates rates once a second. */
final class PlaybackMetrics {
    private long requests,networkBytes,deliveredBytes,requestedBytes,readNanos,errors,cancelledReads,smallReads,opens;
    private long lastBytes,lastSampleMs;
    private double lastMbps;
    private final long[] latencies=new long[256];
    private int latencyIndex,latencyCount;
    synchronized void opened(){opens++;}
    synchronized void read(int requested,int received,long elapsedNanos,boolean failed){
        read(requested,received,elapsedNanos,failed,false);
    }
    synchronized void read(int requested,int received,long elapsedNanos,boolean failed,boolean cancelled){
        requests++;requestedBytes+=requested;networkBytes+=Math.max(0,received);readNanos+=Math.max(0,elapsedNanos);
        if(requested<=4096)smallReads++;if(cancelled)cancelledReads++;else if(failed)errors++;
        latencies[latencyIndex]=Math.max(0,elapsedNanos);latencyIndex=(latencyIndex+1)%latencies.length;latencyCount=Math.min(latencies.length,latencyCount+1);
    }
    synchronized void delivered(int count){deliveredBytes+=count;}
    synchronized Sample sample(long nowMs){
        if(lastSampleMs!=0&&nowMs>lastSampleMs)lastMbps=(networkBytes-lastBytes)*.008/(nowMs-lastSampleMs);
        lastSampleMs=nowMs;lastBytes=networkBytes;
        long[] sorted=java.util.Arrays.copyOf(latencies,latencyCount);java.util.Arrays.sort(sorted);
        double p95=latencyCount==0?0:sorted[(int)Math.ceil(latencyCount*.95)-1]/1e6;
        return new Sample(requests,networkBytes,deliveredBytes,errors,cancelledReads,smallReads,opens,lastMbps,
            requests==0?0:requestedBytes/(double)requests,requests==0?0:networkBytes/(double)requests,
            requests==0?0:readNanos/1e6/requests,p95);
    }
    static final class Sample {
        final long requests,networkBytes,deliveredBytes,errors,cancelledReads,smallReads,opens;
        final double mbps,averageRequestBytes,averageReadBytes,averageReadMs,readP95Ms;
        Sample(long requests,long networkBytes,long deliveredBytes,long errors,long cancelledReads,long smallReads,long opens,double mbps,double requestBytes,double readBytes,double readMs,double p95){
            this.requests=requests;this.networkBytes=networkBytes;this.deliveredBytes=deliveredBytes;this.errors=errors;this.cancelledReads=cancelledReads;this.smallReads=smallReads;this.opens=opens;
            this.mbps=mbps;averageRequestBytes=requestBytes;averageReadBytes=readBytes;averageReadMs=readMs;readP95Ms=p95;
        }
    }
}
