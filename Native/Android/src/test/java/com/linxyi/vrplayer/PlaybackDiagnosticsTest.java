package com.linxyi.vrplayer;

import java.io.InterruptedIOException;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.Test;
import static org.junit.Assert.*;

public class PlaybackDiagnosticsTest {
    @Test public void startupSeekAndPauseAreNotUnexpectedBuffering(){
        BufferingTracker tracker=new BufferingTracker();tracker.update(2,true,false,100);tracker.update(3,true,false,500);
        tracker.seek(600);tracker.update(2,true,false,610);tracker.update(3,true,false,800);
        tracker.update(2,false,false,900);tracker.update(3,false,false,1000);
        assertEquals(0,tracker.count());assertEquals(0,tracker.duration(1100));
        List<BufferingTracker.Event> events=tracker.drain();assertEquals("initial",events.get(0).reason);assertEquals("seek",events.get(3).reason);assertEquals("paused",events.get(5).reason);
    }
    @Test public void shortRebuffersAreCapturedBetweenSnapshots(){
        BufferingTracker tracker=new BufferingTracker();tracker.update(3,true,false,10);tracker.update(2,true,false,100);
        assertEquals(1,tracker.count());assertEquals(30,tracker.duration(130));tracker.update(3,true,false,140);
        assertEquals(40,tracker.duration(500));assertEquals(40,tracker.drain().get(1).durationMs);
    }
    @Test public void pausingOrSuppressionEndsUnexpectedBufferDuration(){
        BufferingTracker tracker=new BufferingTracker();tracker.update(3,true,false,10);tracker.update(2,true,false,100);
        tracker.update(2,false,false,200);assertEquals(100,tracker.duration(1000));
        tracker.update(2,true,true,1100);assertEquals("suppressed",tracker.reason());assertEquals(1,tracker.count());
    }
    @Test public void seekWithinReadyBufferDoesNotMaskNextRealStall(){
        BufferingTracker tracker=new BufferingTracker();tracker.update(3,true,false,10);tracker.seek(100);tracker.update(3,true,false,110);tracker.update(2,true,false,200);
        assertEquals("rebuffer",tracker.reason());assertEquals(1,tracker.count());
    }
    @Test public void automaticLoopBoundaryIsNotARebuffer(){
        BufferingTracker tracker=new BufferingTracker();tracker.update(3,true,false,10);tracker.transition(100);tracker.update(2,true,false,110);
        assertEquals("transition",tracker.reason());tracker.update(3,true,false,200);assertEquals(0,tracker.count());
        tracker.update(2,true,false,300);assertEquals(1,tracker.count());
    }
    @Test public void networkBytesAndDeliveredBytesAreIndependent(){
        PlaybackMetrics metrics=new PlaybackMetrics();metrics.sample(100);metrics.opened();metrics.read(1048576,524288,5000000,false);metrics.delivered(4);metrics.delivered(8);
        PlaybackMetrics.Sample sample=metrics.sample(1100);assertEquals(1,sample.requests);assertEquals(524288,sample.networkBytes);assertEquals(12,sample.deliveredBytes);
        assertEquals(4.194304,sample.mbps,.000001);assertEquals(5,sample.averageReadMs,0);assertEquals(0,sample.smallReads);assertEquals(1,sample.opens);
        assertEquals(0,metrics.sample(2100).mbps,0);
    }
    @Test public void failedRequestsAndRecentLatencyAreCounted(){
        PlaybackMetrics metrics=new PlaybackMetrics();for(int i=0;i<300;i++)metrics.read(4,0,1000000,true);
        PlaybackMetrics.Sample sample=metrics.sample(100);assertEquals(300,sample.errors);assertEquals(300,sample.smallReads);assertEquals(1,sample.readP95Ms,0);assertEquals(0,sample.networkBytes);
    }
    @Test public void cancelledReadIsNotReportedAsNetworkFailure(){
        PlaybackMetrics metrics=new PlaybackMetrics();metrics.read(1024,0,1000000,true,true);metrics.read(1024,0,1000000,true,false);
        PlaybackMetrics.Sample sample=metrics.sample(100);assertEquals(2,sample.requests);assertEquals(1,sample.errors);assertEquals(1,sample.cancelledReads);
    }
    @Test public void retiredSessionCancelsResourcesIncludingLateConnections()throws Exception{
        PlaybackSession previous=new PlaybackSession(1,true),current=new PlaybackSession(2,true);AtomicInteger closed=new AtomicInteger();
        previous.attach(closed::incrementAndGet);previous.cancel();assertEquals(1,closed.get());
        try{previous.attach(closed::incrementAndGet);fail("Expected cancellation");}catch(InterruptedIOException expected){}
        assertEquals(2,closed.get());current.check();previous.metrics.read(4,4,1,false);assertEquals(0,current.metrics.sample(100).requests);
    }
}
