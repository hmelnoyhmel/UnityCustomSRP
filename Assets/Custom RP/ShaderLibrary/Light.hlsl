#ifndef CUSTOM_LIGHT_INCLUDED
#define CUSTOM_LIGHT_INCLUDED

#define MAX_DIRECTIONAL_LIGHT_COUNT 4

CBUFFER_START(_CustomLight)
    int _DirectionalLightCount;
    int _OtherLightCount;
CBUFFER_END

struct Light
{
    float3 color;
    float3 direction;
    float attenuation;
    uint renderingLayerMask;
};

struct OtherLightData
{
    float4 color;
    float4 position;
    float4 directionAndMask;
    float4 spotAngle;
    float4 shadowData;
};

StructuredBuffer<OtherLightData> _OtherLightData;

struct DirectionalLightData
{
    float4 color;
    float4 directionAndMask;
    float4 shadowData;
};

StructuredBuffer<DirectionalLightData> _DirectionalLightData;

int GetDirectionalLightCount()
{
    return _DirectionalLightCount;
}

DirectionalShadowData GetDirectionalShadowData(float4 lightShadowData, ShadowData shadowData)
{
    DirectionalShadowData data;
    data.strength = lightShadowData.x;
    data.tileIndex = lightShadowData.y + shadowData.cascadeIndex;
    data.normalBias = lightShadowData.z;
    data.shadowMaskChannel = lightShadowData.w;
    return data;
}

Light GetDirectionalLight(int index, Surface surfaceWS, ShadowData shadowData)
{
    DirectionalLightData data = _DirectionalLightData[index];
    Light light;
    light.color = data.color.rgb;
    light.direction = data.directionAndMask.xyz;
    light.renderingLayerMask = asuint(data.directionAndMask.w);
    DirectionalShadowData dirShadowData = GetDirectionalShadowData(data.shadowData, shadowData);
    light.attenuation = GetDirectionalShadowAttenuation(dirShadowData, shadowData, surfaceWS);
    return light;
}

int GetOtherLightCount()
{
    return _OtherLightCount;
}

OtherShadowData GetOtherShadowData(float4 lightShadowData)
{
    OtherShadowData data;
    data.strength = lightShadowData.x;
    data.tileIndex = lightShadowData.y;
    data.shadowMaskChannel = lightShadowData.w;
    data.isPoint = lightShadowData.z == 1.0;
    data.lightPositionWS = 0.0;
    data.lightDirectionWS = 0.0;
    data.spotDirectionWS = 0.0;
    return data;
}

Light GetOtherLight(int index, Surface surfaceWS, ShadowData shadowData)
{
    OtherLightData data = _OtherLightData[index];
    Light light;
    light.color = data.color.rgb;
    float3 position = data.position.xyz;
    float3 ray = position - surfaceWS.position;
    light.direction = normalize(ray);
    float distanceSqr = max(dot(ray, ray), 0.00001);
    float rangeAttenuation = Square(saturate(1.0 - Square(distanceSqr * data.position.w)));
    float3 spotDirection = data.directionAndMask.xyz;
    light.renderingLayerMask = asuint(data.directionAndMask.w);
    float spotAttenuation = Square(
        saturate(dot(spotDirection, light.direction) *
            data.spotAngle.x + data.spotAngle.y)
    );
    OtherShadowData otherShadowData = GetOtherShadowData(index);
    otherShadowData.lightPositionWS = position;
    otherShadowData.lightDirectionWS = light.direction;
    otherShadowData.spotDirectionWS = spotDirection;
    light.attenuation =
        GetOtherShadowAttenuation(otherShadowData, shadowData, surfaceWS) *
        spotAttenuation * rangeAttenuation / distanceSqr;
    return light;
}

#endif
