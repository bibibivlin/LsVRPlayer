package com.linxyi.vrplayer;
import org.junit.Test;
import static org.junit.Assert.*;

public class DiscoveryRangeTest {
    @Test public void slash23IncludesBothHalvesOfTheSubnet(){DiscoveryRange range=new DiscoveryRange(new byte[]{(byte)192,(byte)168,5,42},23);assertEquals(509,range.count());assertEquals("192.168.4.1",range.take());assertEquals("192.168.5.254",DiscoveryRange.address(range.last));assertTrue(range.contains(new byte[]{(byte)192,(byte)168,4,99}));}
    @Test public void skipsOwnAddressNetworkAndBroadcast(){DiscoveryRange range=new DiscoveryRange(new byte[]{10,0,0,1},30);assertEquals(1,range.count());assertEquals("10.0.0.2",range.take());assertFalse(range.hasNext());assertNull(range.take());}
    @Test public void pointToPointSubnetKeepsTheOtherEndpoint(){DiscoveryRange range=new DiscoveryRange(new byte[]{10,0,0,0},31);assertEquals(1,range.count());assertEquals("10.0.0.1",range.take());}
    @Test public void singleAddressSubnetHasNoTargets(){DiscoveryRange range=new DiscoveryRange(new byte[]{10,0,0,1},32);assertEquals(0,range.count());assertFalse(range.hasNext());}
    @Test public void largeSubnetsUseUnsignedArithmeticWithoutOverflow(){DiscoveryRange range=new DiscoveryRange(new byte[]{(byte)192,(byte)168,0,2},0);assertEquals(4294967293L,range.count());assertEquals("255.255.255.254",DiscoveryRange.address(range.last));}
}
