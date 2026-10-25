using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRPlayer
{
    public enum CinemaBackgroundMode { Black = 0, NeutralGray = 1, SoftSurround = 2, QuietTheater = 3, MatteWall = 4 }

    [Serializable]
    public sealed class CinemaBackgroundSettings
    {
        public CinemaBackgroundMode mode;
        // SDR/sRGB code value, not a percentage of physical display luminance.
        public float grayLevel = 16;
        public float featherDegrees = 12;

        public void Normalize()
        {
            if (!Enum.IsDefined(typeof(CinemaBackgroundMode), mode)) mode = CinemaBackgroundMode.Black;
            grayLevel = Mathf.Clamp(float.IsNaN(grayLevel) || float.IsInfinity(grayLevel) ? 16 : grayLevel, 0, 48);
            featherDegrees = Mathf.Clamp(float.IsNaN(featherDegrees) || float.IsInfinity(featherDegrees) ? 12 : featherDegrees, 5, 25);
        }

        public static string Name(CinemaBackgroundMode mode) => mode switch
        {
            CinemaBackgroundMode.NeutralGray => "中性暗灰",
            CinemaBackgroundMode.SoftSurround => "屏幕柔光",
            CinemaBackgroundMode.QuietTheater => "暗色影院",
            CinemaBackgroundMode.MatteWall => "哑光幕墙",
            _ => "纯黑"
        };
    }

    // Owned by VideoPresenter. No video sampling, lights, colliders or per-frame allocations.
    [ExecuteAlways]
    public sealed class CinemaBackground : MonoBehaviour
    {
        private Camera head;
        private Material material;
        private Mesh mesh;
        private MeshRenderer surface;
        private CameraClearFlags originalClearFlags;
        private Color originalClearColor;
        private Vector3 roomOrigin;
        private Vector4 screenBounds;
        private float lastWidth = -1, lastHeight, lastDistance, lastOffset, lastCurve;
        public bool Visible => surface != null && surface.enabled;

        public void Initialize(Camera camera)
        {
            head = camera;
            originalClearFlags = head.clearFlags;
            originalClearColor = head.backgroundColor;
            head.clearFlags = CameraClearFlags.SolidColor;
            head.backgroundColor = Color.black;
            roomOrigin = head.transform.position;
            var shader = Shader.Find("VRPlayer/Cinema Background");
            if (shader == null) { Debug.LogError("VRPlayer cinema background shader is missing."); return; }
            material = new Material(shader) { name = "Cinema background material" };
            mesh = new Mesh { name = "Cinema background enclosure" };
            mesh.vertices = new[] { new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(-1,1,-1), new Vector3(-1,-1,1), new Vector3(1,-1,1), new Vector3(1,1,1), new Vector3(-1,1,1) };
            mesh.triangles = new[] { 0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,0,1,5,0,5,4,3,7,6,3,6,2 };
            mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            surface = gameObject.AddComponent<MeshRenderer>();
            surface.sharedMaterial = material;
            surface.shadowCastingMode = ShadowCastingMode.Off;
            surface.receiveShadows = false;
            surface.lightProbeUsage = LightProbeUsage.Off;
            surface.reflectionProbeUsage = ReflectionProbeUsage.Off;
            surface.enabled = false;
            // Keep every cube corner inside the camera's far plane.
            transform.localScale = Vector3.one * Mathf.Min(40, head.farClipPlane * .45f);
        }

        public void Apply(CinemaBackgroundSettings settings, ViewSettings view, float aspect)
        {
            if (surface == null) return;
            surface.enabled = view.projection == Projection.Flat && settings.mode != CinemaBackgroundMode.Black && settings.grayLevel > 0;
            if (!surface.enabled) return;
            float height = view.width / aspect;
            if (lastWidth != view.width || lastHeight != height || lastDistance != view.distance || lastOffset != view.heightOffset || lastCurve != view.curvature)
            {
                screenBounds = ScreenBounds(view, aspect);
                lastWidth = view.width; lastHeight = height; lastDistance = view.distance; lastOffset = view.heightOffset; lastCurve = view.curvature;
                material.SetVector("_ScreenBounds", screenBounds);
            }
            material.SetFloat("_Mode", (int)settings.mode);
            material.SetFloat("_GrayLevel", settings.grayLevel / 255f);
            material.SetFloat("_Feather", settings.featherDegrees * Mathf.Deg2Rad);
            // The room stays anchored in the initial viewing frame during head translation.
            material.SetVector("_RoomOffset", Quaternion.Inverse(transform.rotation) * (head.transform.position - roomOrigin));
        }

        public static Vector4 ScreenBounds(ViewSettings view, float aspect)
        {
            float halfHeight = view.width / aspect * .5f;
            float yaw = 0, low = Mathf.PI, high = -Mathf.PI;
            // Match the curved screen's 64 segments and contain every edge in angular space.
            for (int i = 0; i <= 64; i++)
            {
                float u = i / 64f - .5f, x = u * view.width, z = view.distance;
                if (view.curvature > .01f)
                {
                    float radius = view.width / Mathf.Deg2Rad / view.curvature, angle = u * view.curvature * Mathf.Deg2Rad;
                    x = Mathf.Sin(angle) * radius; z += (Mathf.Cos(angle) - 1) * radius;
                }
                yaw = Mathf.Max(yaw, Mathf.Abs(Mathf.Atan2(x, z)));
                float horizontal = Mathf.Sqrt(x * x + z * z);
                low = Mathf.Min(low, Mathf.Atan2(view.heightOffset - halfHeight, horizontal));
                high = Mathf.Max(high, Mathf.Atan2(view.heightOffset + halfHeight, horizontal));
            }
            return new Vector4(yaw, (high - low) * .5f, (high + low) * .5f, 0);
        }

        private void OnDestroy()
        {
            if (head != null) { head.clearFlags = originalClearFlags; head.backgroundColor = originalClearColor; }
            Release(material); Release(mesh);
        }
        private static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
