package com.linxyi.vrplayer;

import java.util.Locale;

/** Timestamp-only remux of Media3's Matroska SSA envelope. */
final class AssPacket {
    static final class Event {
        final long endMs;
        final String dialogue;
        Event(long endMs,String dialogue){this.endMs=endMs;this.dialogue=dialogue;}
    }
    static Event parse(String packet,String format,long sampleMs){
        if(!packet.startsWith("Dialogue:")||!format.startsWith("Format:"))return null;
        String[] keys=format.substring(7).trim().split(","),values=packet.substring(9).trim().replace("\u0000","").split(",",keys.length);
        if(keys.length!=values.length)return null;
        String layer="0",style="Default",name="",left="0",right="0",vertical="0",effect="",text="";
        long start=0,end=-1;
        try{
            for(int i=0;i<keys.length;i++)switch(keys[i].trim().toLowerCase(Locale.ROOT)){
                case "start":start=time(values[i]);break;
                case "end":end=time(values[i]);break;
                case "layer":layer=values[i];break;
                case "style":style=values[i];break;
                case "name":name=values[i];break;
                case "marginl":left=values[i];break;
                case "marginr":right=values[i];break;
                case "marginv":vertical=values[i];break;
                case "effect":effect=values[i];break;
                case "text":text=values[i];break;
            }
            if(end<=start||sampleMs<0||sampleMs>Long.MAX_VALUE-end)return null;
            return new Event(sampleMs+end,"Dialogue: "+layer+","+stamp(sampleMs+start)+","+stamp(sampleMs+end)+","+style+","+name+","+left+","+right+","+vertical+","+effect+","+text);
        }catch(IllegalArgumentException ex){return null;}
    }
    private static long time(String value){
        String[] parts=value.trim().replace('.',':').split(":");
        if(parts.length!=4)throw new IllegalArgumentException();
        long h=Long.parseLong(parts[0]),m=Long.parseLong(parts[1]),s=Long.parseLong(parts[2]),cs=Long.parseLong(parts[3]);
        if(h<0||h>100000||m<0||m>59||s<0||s>59||cs<0||cs>99)throw new IllegalArgumentException();
        return ((h*60+m)*60+s)*1000+cs*10;
    }
    private static String stamp(long ms){return String.format(Locale.ROOT,"%d:%02d:%02d.%02d",ms/3600000,ms/60000%60,ms/1000%60,ms/10%100);}
}
