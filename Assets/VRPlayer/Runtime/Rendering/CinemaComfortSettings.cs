using System;
using UnityEngine;

namespace VRPlayer
{
    public enum HighlightCompression { Off = 0, Gentle = 1, Medium = 2, Strong = 3 }

    [Serializable]
    public sealed class CinemaComfortSettings
    {
        public HighlightCompression highlights;
        // Multiplies emitted subtitle RGB in linear space, never glyph opacity.
        public float subtitleBrightness = 1;

        public void Normalize()
        {
            if (!Enum.IsDefined(typeof(HighlightCompression), highlights)) highlights = HighlightCompression.Off;
            subtitleBrightness = NormalizeSubtitleBrightness(subtitleBrightness);
        }

        public static float NormalizeSubtitleBrightness(float value) => Mathf.Clamp(float.IsNaN(value) || float.IsInfinity(value) ? 1 : value, .3f, 1);

        public static Vector4 HighlightParameters(HighlightCompression mode)
        {
            const float knee = .35f;
            float white = mode switch { HighlightCompression.Gentle => .85f, HighlightCompression.Medium => .75f, HighlightCompression.Strong => .65f, _ => 1 };
            float a = (1 - white) / ((white - knee) * (1 - knee));
            float slope = (white - knee) / (1 - knee);
            return new Vector4(knee, white, a, slope * slope);
        }
    }
}
