using System;
using UnityEngine;

namespace VRPlayer
{
    // Independent of XR polling so gesture timing and arbitration can be replayed in tests.
    public sealed class StickTransportGesture
    {
        private readonly int[] axes=new int[2]; // 0 neutral, 1 horizontal, 2 vertical, 3 suppressed until neutral.
        private int owner=-1,direction;
        private float repeatAt;
        private readonly Vector2[] values=new Vector2[2];
        private readonly bool[] valid=new bool[2];
        public int Owner=>owner;
        public float Scroll{get;private set;}
        public void Suppress(){owner=-1;axes[0]=axes[1]=3;Scroll=0;}
        public void SuppressActive(){owner=-1;for(int hand=0;hand<2;hand++)if(axes[hand]!=0)axes[hand]=3;Scroll=0;}
        public int Update(Vector2 left,bool leftValid,Vector2 right,bool rightValid,bool blocked,float now,bool blockHorizontal=false)
        {
            Scroll=0;values[0]=left;values[1]=right;valid[0]=leftValid;valid[1]=rightValid;
            for(int hand=0;hand<2;hand++){
                Vector2 axis=values[hand];
                if(!valid[hand]||blocked){axes[hand]=3;if(owner==hand)owner=-1;continue;}
                if(axis.magnitude<=.25f){axes[hand]=0;if(owner==hand)owner=-1;continue;}
                if(blockHorizontal&&(axes[hand]==1||owner==hand||Mathf.Abs(axis.x)>Mathf.Abs(axis.y))){axes[hand]=3;if(owner==hand)owner=-1;continue;}
                if(owner>=0&&owner!=hand){axes[hand]=3;continue;}
                if(axes[hand]==0){if(Mathf.Abs(axis.x)>=.65f&&Mathf.Abs(axis.x)>Mathf.Abs(axis.y)*1.35f)axes[hand]=1;else if(Mathf.Abs(axis.y)>.25f&&Mathf.Abs(axis.y)>=Mathf.Abs(axis.x))axes[hand]=2;}
                if(axes[hand]==2&&owner<0&&Mathf.Abs(axis.y)>Mathf.Abs(Scroll))Scroll=axis.y;
            }
            if(blocked)return 0;
            if(owner>=0){
                Vector2 axis=values[owner];
                if(Mathf.Abs(axis.x)<=.25f||Math.Sign(axis.x)!=direction){axes[owner]=3;owner=-1;return 0;}
                Scroll=0;if(now>=repeatAt){repeatAt=now+.3f;return direction*10;}return 0;
            }
            for(int hand=0;hand<2;hand++)if(axes[hand]==1){owner=hand;direction=Math.Sign(values[hand].x);repeatAt=now+.5f;Scroll=0;axes[1-hand]=values[1-hand].magnitude>.25f?3:axes[1-hand];return direction*10;}
            return 0;
        }
    }
    public sealed class RelativeSeekTarget
    {
        private long? pending;
        private float issuedAt;
        public void Reset()=>pending=null;
        public long Absolute(long position,long duration,float now){issuedAt=now;pending=Math.Clamp(position,0,Math.Max(0,duration-1));return pending.Value;}
        public long Relative(long observed,long delta,long duration,float now)
        {
            // Acknowledged snapshots resume from media time; otherwise accumulate rapid seeks.
            if(pending.HasValue&&(Math.Abs(observed-pending.Value)<1500||now-issuedAt>2))pending=null;
            return Absolute((pending??observed)+delta,duration,now);
        }
    }
}
