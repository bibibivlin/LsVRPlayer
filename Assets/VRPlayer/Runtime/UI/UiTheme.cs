using UnityEngine;
using UnityEngine.UI;

namespace VRPlayer
{
    // Canvas units, not pixels: keep the established world size and viewing distance.
    public static class UiTheme
    {
        public static readonly Color Background=new Color(.046f,.086f,.118f,.98f);
        public static readonly Color Surface=new Color(.044f,.085f,.115f,1);
        public static readonly Color Elevated=new Color(.090f,.147f,.191f,1);
        public static readonly Color Selected=new Color(.20f,.82f,.78f,1);
        public static readonly Color Accent=new Color(.23f,.88f,.82f,1);
        public static readonly Color Text=new Color(.93f,.96f,.98f,1);
        public static readonly Color Muted=new Color(.66f,.74f,.79f,1);
        public static readonly Color Rail=new Color(.15f,.23f,.29f,1);
        public static readonly Color Ink=new Color(.012f,.055f,.068f,1),SelectedMuted=new Color(.048f,.21f,.23f,1);
        public const float BodySize=28,MetaSize=23,TitleSize=34;
        public const float PageWidth=1352,PageHeight=794,CardPadding=24,CardGap=24,ControlGap=16,IconButtonSize=64,SliderHeight=48;

        public static ColorBlock ButtonColors()
        {
            var colors=ColorBlock.defaultColorBlock;
            colors.normalColor=Color.white;colors.highlightedColor=new Color(1.18f,1.18f,1.18f,1);
            colors.pressedColor=new Color(.78f,.92f,.94f,1);colors.selectedColor=colors.highlightedColor;
            colors.disabledColor=new Color(.55f,.55f,.55f,.7f);colors.fadeDuration=.08f;
            return colors;
        }
    }

    public sealed class UiNavState : MonoBehaviour
    {
        public bool sidebar;
    }

    // Smooth, inexpensive vertex gradients. Decorative graphics never receive input.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class VerticalFadeGraphic : MaskableGraphic
    {
        public Color top=Color.clear,bottom=new Color(.004f,.026f,.035f,.22f);
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var r=rectTransform.rect;
            for(int i=0;i<=12;i++){
                float t=i/12f;var c=Color.Lerp(top,bottom,Mathf.SmoothStep(0,1,t));
                float y=Mathf.Lerp(r.yMax,r.yMin,t);
                mesh.AddVert(new Vector2(r.xMin,y),c,Vector2.zero);mesh.AddVert(new Vector2(r.xMax,y),c,Vector2.zero);
                if(i>0){int n=i*2;mesh.AddTriangle(n-2,n,n+1);mesh.AddTriangle(n-2,n+1,n-1);}
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SeekBeamGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var r=rectTransform.rect;
            // Narrow luminous core plus a soft halo; the controller supplies the dot.
            float[] xs={-.5f,-.12f,-.045f,.045f,.12f,.5f};
            float[] alpha={0,.26f,.98f,.98f,.26f,0};
            for(int row=0;row<=10;row++){
                float t=row/10f;float fade=Mathf.Sin(t*Mathf.PI);fade*=fade;
                for(int col=0;col<xs.Length;col++){
                    Color c=col==2||col==3?Color.Lerp(color,Color.white,.75f):color;c.a*=alpha[col]*fade;
                    mesh.AddVert(new Vector2(r.center.x+xs[col]*r.width,Mathf.Lerp(r.yMin,r.yMax,t)),c,Vector2.zero);
                    if(row>0&&col>0){int n=row*xs.Length+col;mesh.AddTriangle(n-xs.Length-1,n-1,n);mesh.AddTriangle(n-xs.Length-1,n,n-xs.Length);}
                }
            }
        }
    }
}
