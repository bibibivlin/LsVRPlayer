using UnityEngine;
using UnityEngine.UI;

namespace VRPlayer
{
    // Clip the image geometry itself so its corners do not depend on a stencil mask's alpha threshold.
    public sealed class RoundedRawImage : RawImage
    {
        [SerializeField] private float radius=18;
        public float Radius
        {
            get=>radius;
            set{value=Mathf.Max(0,value);if(Mathf.Approximately(radius,value))return;radius=value;SetVerticesDirty();}
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            if(radius<=0){base.OnPopulateMesh(mesh);return;}
            mesh.Clear();var texture=mainTexture;Rect rect=GetPixelAdjustedRect();
            if(texture==null||rect.width<=0||rect.height<=0)return;
            float curve=Mathf.Min(radius,Mathf.Min(rect.width,rect.height)*.5f);
            Vector2 scale=new Vector2(texture.width*texture.texelSize.x,texture.height*texture.texelSize.y);
            void Vertex(Vector2 point)
            {
                var uv=new Vector2((uvRect.x+(point.x-rect.xMin)/rect.width*uvRect.width)*scale.x,
                    (uvRect.y+(point.y-rect.yMin)/rect.height*uvRect.height)*scale.y);
                mesh.AddVert(point,color,uv);
            }
            Vertex(rect.center);
            const int segments=8;
            for(int corner=0;corner<4;corner++){
                var center=new Vector2(corner==0||corner==3?rect.xMax-curve:rect.xMin+curve,
                    corner<2?rect.yMax-curve:rect.yMin+curve);
                for(int step=0;step<=segments;step++){
                    float angle=(corner*90+step*90f/segments)*Mathf.Deg2Rad;
                    Vertex(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*curve);
                }
            }
            int perimeter=4*(segments+1);
            for(int i=1;i<=perimeter;i++)mesh.AddTriangle(0,i,i==perimeter?1:i+1);
        }
    }
}
