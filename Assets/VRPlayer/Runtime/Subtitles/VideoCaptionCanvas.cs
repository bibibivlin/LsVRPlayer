using UnityEngine;

namespace VRPlayer
{
    internal sealed class VideoCaptionCanvas : MonoBehaviour
    {
        private const float CanvasHeight = 1000;
        private const float CaptionHeight = CanvasHeight * .92f;
        internal static int Layer => LayerMask.NameToLayer("Video Subtitle Raster");

        private Camera rasterCamera;
        private RectTransform frame, bounds;
        private CaptionPresenter caption;
        private RenderTexture target;
        private float verticalPosition = -1;

        public void Initialize()
        {
            rasterCamera = gameObject.AddComponent<Camera>();
            rasterCamera.enabled = false;
            rasterCamera.stereoTargetEye = StereoTargetEyeMask.None;
            rasterCamera.cullingMask = 1 << Layer;
            rasterCamera.clearFlags = CameraClearFlags.SolidColor;
            rasterCamera.backgroundColor = Color.clear;
            rasterCamera.orthographic = true;
            rasterCamera.orthographicSize = .5f;
            rasterCamera.nearClipPlane = .01f;
            rasterCamera.farClipPlane = 2;
            rasterCamera.useOcclusionCulling = false;
            rasterCamera.allowHDR = false;
            rasterCamera.allowMSAA = false;

            var root = new GameObject("Video subtitle canvas", typeof(RectTransform), typeof(Canvas)) { layer = Layer };
            frame = (RectTransform)root.transform;
            frame.SetParent(transform, false);
            frame.localPosition = Vector3.forward;
            frame.localScale = Vector3.one / CanvasHeight;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = rasterCamera;

            bounds = UiFactory.Rect(frame, "Caption bounds", 0, 0, CanvasHeight, CaptionHeight);
            bounds.gameObject.layer = Layer;
            bounds.anchorMin = bounds.anchorMax = bounds.pivot = new Vector2(.5f, .5f);
            caption = bounds.gameObject.AddComponent<CaptionPresenter>();
            caption.Initialize(CanvasHeight, CaptionHeight, overlay: true, opticalCenter: true, fitToCanvas: true);
            foreach (Transform child in bounds) child.gameObject.layer = Layer;
        }

        public Texture Render(string text, FlatSubtitleSettings settings, Vector2Int resolution)
        {
            bool dirty = false;
            if (target == null || target.width != resolution.x || target.height != resolution.y)
            {
                ReleaseTarget();
                target = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "Video text subtitles",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                rasterCamera.targetTexture = target;
                rasterCamera.aspect = resolution.x / (float)resolution.y;
                frame.sizeDelta = new Vector2(CanvasHeight * rasterCamera.aspect, CanvasHeight);
                float width = frame.sizeDelta.x * .92f;
                bounds.sizeDelta = new Vector2(width, CaptionHeight);
                caption.Resize(width, CaptionHeight);
                dirty = true;
            }

            bool needsTarget = !target.IsCreated();
            if (needsTarget && !target.Create()) return null;
            bool changed = caption.SetText(text, settings.size, settings.style);
            if (dirty || needsTarget || changed || !Mathf.Approximately(settings.verticalPosition, verticalPosition))
            {
                verticalPosition = settings.verticalPosition;
                float travel = (CaptionHeight - caption.ContentHeight) * .5f;
                bounds.anchoredPosition = new Vector2(0, Mathf.Lerp(-travel, travel, verticalPosition));
                Canvas.ForceUpdateCanvases();
                rasterCamera.Render();
            }
            return target;
        }

        private void ReleaseTarget()
        {
            if (target == null) return;
            if (rasterCamera != null) rasterCamera.targetTexture = null;
            target.Release();
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
            target = null;
        }

        private void OnDestroy() => ReleaseTarget();
    }
}
