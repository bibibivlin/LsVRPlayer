package com.linxyi.vrplayer;

import java.util.ArrayList;
import java.util.List;

/** Event-driven intervals; periodic snapshots alone miss short stalls. Times are monotonic milliseconds. */
final class BufferingTracker {
    static final class Event {
        final String type,reason;final long timeMs,durationMs;
        Event(String type,String reason,long timeMs,long durationMs){this.type=type;this.reason=reason;this.timeMs=timeMs;this.durationMs=durationMs;}
    }
    private final List<Event> events=new ArrayList<>();
    private boolean prepared;
    private String pending="";
    private String reason="";
    private long startedMs,rebufferMs;
    private int rebufferCount;
    synchronized void seek(long nowMs){finish(nowMs);pending="seek";events.add(new Event("seek","user",nowMs,0));}
    synchronized void transition(long nowMs){finish(nowMs);pending="transition";events.add(new Event("transition","automatic",nowMs,0));}
    synchronized void playIntent(boolean playing,long nowMs){events.add(new Event("play-intent",playing?"play":"pause",nowMs,0));}
    synchronized void update(int state,boolean playWhenReady,boolean suppressed,long nowMs){
        String next=state!=2?"":!prepared?"initial":!pending.isEmpty()?pending:!playWhenReady?"paused":suppressed?"suppressed":"rebuffer";
        if(!next.equals(reason)){
            finish(nowMs);reason=next;
            if(!reason.isEmpty()){startedMs=nowMs;if(reason.equals("rebuffer"))rebufferCount++;events.add(new Event("buffer-start",reason,nowMs,0));}
        }
        if(state==3){prepared=true;pending="";}
    }
    synchronized void finish(long nowMs){
        if(reason.isEmpty())return;long duration=Math.max(0,nowMs-startedMs);
        if(reason.equals("rebuffer"))rebufferMs+=duration;
        events.add(new Event("buffer-end",reason,nowMs,duration));reason="";
    }
    synchronized List<Event> drain(){List<Event> result=new ArrayList<>(events);events.clear();return result;}
    synchronized String reason(){return reason;}
    synchronized int count(){return rebufferCount;}
    synchronized long duration(long nowMs){return rebufferMs+(reason.equals("rebuffer")?Math.max(0,nowMs-startedMs):0);}
}
