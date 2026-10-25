#ifndef VRPLAYER_CINEMA_COMFORT_INCLUDED
#define VRPLAYER_CINEMA_COMFORT_INCLUDED

// Input is linear RGB after the existing film brightness gain. The shoulder is
// C1 at both knee and reference white; values above 1 approach 1 without clipping.
float3 CinemaCompressHighlights(float3 rgb, float4 parameters)
{
    if (parameters.y >= 1) return rgb;
    float peak = max(rgb.r, max(rgb.g, rgb.b));
    if (peak <= parameters.x) return rgb;
    float delta = peak - parameters.x;
    float mapped = parameters.x + delta / (1 + parameters.z * delta);
    if (peak > 1)
    {
        float room = 1 - parameters.y;
        mapped = 1 - room / (1 + (peak - 1) * parameters.w / room);
    }
    return rgb * (mapped / peak);
}

// The caller supplies straight RGB; keeping alpha separate also supports ASS
// and premultiplied SDF edge pixels without making the background show through.
float3 CinemaSubtitleColor(float3 rgb, float gain)
{
    if (gain >= 1) return rgb;
    #ifdef UNITY_COLORSPACE_GAMMA
        return LinearToGammaSpace(GammaToLinearSpace(rgb) * gain);
    #else
        return rgb * gain;
    #endif
}
#endif
