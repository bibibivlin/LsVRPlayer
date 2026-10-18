package com.linxyi.vrplayer;

import org.junit.Test;
import static org.junit.Assert.*;

public final class AssPacketTest {
    private static final String FORMAT="Format: Start, End, ReadOrder, Layer, Style, Name, MarginL, MarginR, MarginV, Effect, Text";
    @Test public void remuxPreservesEffectsLayerStyleAndCommas(){
        AssPacket.Event event=AssPacket.parse("Dialogue: 0:00:00:00,0:00:03:25,42,2,Sign,actor,10,20,30,,{\\move(0,0,100,100)}Hello, world",FORMAT,62000);
        assertNotNull(event);assertEquals(65250,event.endMs);
        assertEquals("Dialogue: 2,0:01:02.00,0:01:05.25,Sign,actor,10,20,30,,{\\move(0,0,100,100)}Hello, world",event.dialogue);
    }
    @Test public void relativeStartIsAddedToSampleTime(){
        AssPacket.Event event=AssPacket.parse("Dialogue: 0:00:01.50,0:00:04.00,0,0,Default,,0,0,0,,Text",FORMAT,10000);
        assertEquals("Dialogue: 0,0:00:11.50,0:00:14.00,Default,,0,0,0,,Text",event.dialogue);
    }
    @Test public void malformedOrReversedTimesAreDiscarded(){
        assertNull(AssPacket.parse("Dialogue: 0:00:05:00,0:00:03:00,0,0,Default,,0,0,0,,Bad",FORMAT,0));
        assertNull(AssPacket.parse("Dialogue: 0:90:00:00,0:00:03:00,0,0,Default,,0,0,0,,Bad",FORMAT,0));
        assertNull(AssPacket.parse("not a packet",FORMAT,0));
    }
}
