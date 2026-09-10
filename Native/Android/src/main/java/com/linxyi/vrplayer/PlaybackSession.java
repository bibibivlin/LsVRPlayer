package com.linxyi.vrplayer;

import java.io.IOException;
import java.io.InterruptedIOException;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/** Captured by a media source factory, never looked up from mutable global playback state. */
final class PlaybackSession {
    final int generation;
    final boolean readAhead;
    final PlaybackMetrics metrics=new PlaybackMetrics();
    final BufferingTracker buffering=new BufferingTracker();
    volatile String networkSnapshot="{}";
    private final Set<AutoCloseable> cancellations=ConcurrentHashMap.newKeySet();
    private volatile boolean cancelled;
    PlaybackSession(int generation,boolean readAhead){this.generation=generation;this.readAhead=readAhead;}
    void check()throws InterruptedIOException{if(cancelled||Thread.currentThread().isInterrupted())throw new InterruptedIOException("Playback cancelled");}
    boolean isCancelled(){return cancelled;}
    void attach(AutoCloseable resource)throws IOException{
        cancellations.add(resource);
        if(cancelled){detach(resource);close(resource);throw new InterruptedIOException("Playback cancelled");}
    }
    void detach(AutoCloseable resource){cancellations.remove(resource);}
    void cancel(){cancelled=true;for(AutoCloseable resource:cancellations)close(resource);cancellations.clear();}
    private static void close(AutoCloseable resource){try{resource.close();}catch(Exception ignored){}}
}
