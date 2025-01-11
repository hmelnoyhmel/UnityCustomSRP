#ifndef CUSTOM_UNLIT_PASS_INCLUDED
#define CUSTOM_UNLIT_PASS_INCLUDED

#include "../ShaderLibrary/Common.hlsl"

float4 _BaseColor;

// The main job of the vertex function is to convert the original vertex position to the correct space.
float4 UnlitPassVertex(float3 positionOS : POSITION) : SV_POSITION
{
    const float3 positionWS = TransformObjectToWorld(positionOS.xyz);
    return TransformWorldToHClip(positionWS);
}

float4 UnlitPassFragment() : SV_TARGET
{
    return _BaseColor;
}

#endif

