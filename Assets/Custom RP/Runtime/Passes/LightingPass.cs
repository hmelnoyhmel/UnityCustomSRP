using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class LightingPass
{
    static readonly ProfilingSampler sampler = new("Lighting");
    
    private const int maxDirLightCount = 4, maxOtherLightCount = 64;

    private static readonly GlobalKeyword lightsPerObjectKeyword =
        GlobalKeyword.Create("_LIGHTS_PER_OBJECT");

    private static readonly int
        dirLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
        dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors"),
        dirLightDirectionsAndMasksId =
            Shader.PropertyToID("_DirectionalLightDirectionsAndMasks"),
        dirLightShadowDataId =
            Shader.PropertyToID("_DirectionalLightShadowData");

    private static readonly Vector4[]
        dirLightColors = new Vector4[maxDirLightCount],
        dirLightDirectionsAndMasks = new Vector4[maxDirLightCount],
        dirLightShadowData = new Vector4[maxDirLightCount];

    private static readonly int
        otherLightCountId = Shader.PropertyToID("_OtherLightCount"),
        otherLightColorsId = Shader.PropertyToID("_OtherLightColors"),
        otherLightPositionsId = Shader.PropertyToID("_OtherLightPositions"),
        otherLightDirectionsAndMasksId =
            Shader.PropertyToID("_OtherLightDirectionsAndMasks"),
        otherLightSpotAnglesId = Shader.PropertyToID("_OtherLightSpotAngles"),
        otherLightShadowDataId = Shader.PropertyToID("_OtherLightShadowData");

    private static readonly Vector4[]
        otherLightColors = new Vector4[maxOtherLightCount],
        otherLightPositions = new Vector4[maxOtherLightCount],
        otherLightDirectionsAndMasks = new Vector4[maxOtherLightCount],
        otherLightSpotAngles = new Vector4[maxOtherLightCount],
        otherLightShadowData = new Vector4[maxOtherLightCount];

    CommandBuffer buffer;
    
    private readonly Shadows shadows = new();

    private CullingResults cullingResults;
    
    int dirLightCount, otherLightCount;

    bool useLightsPerObject;
    
    public void Setup(
        CullingResults cullingResults,
        ShadowSettings shadowSettings, bool useLightsPerObject, int renderingLayerMask)
    {
        this.cullingResults = cullingResults;
        this.useLightsPerObject = useLightsPerObject;
        shadows.Setup(cullingResults, shadowSettings);
        SetupLights(renderingLayerMask);
    }

    private void SetupLights(int renderingLayerMask)
    {
        var indexMap = useLightsPerObject ? cullingResults.GetLightIndexMap(Allocator.Temp) : default;
        var visibleLights = cullingResults.visibleLights;
        dirLightCount = 0;
        otherLightCount = 0;
        int i;
        for (i = 0; i < visibleLights.Length; i++)
        {
            var newIndex = -1;
            var visibleLight = visibleLights[i];
            var light = visibleLight.light;
            if ((light.renderingLayerMask & renderingLayerMask) != 0)
                switch (visibleLight.lightType)
                {
                    case LightType.Directional:
                        if (dirLightCount < maxDirLightCount)
                            SetupDirectionalLight(
                                dirLightCount++, i, ref visibleLight, light);
                        break;
                    case LightType.Point:
                        if (otherLightCount < maxOtherLightCount)
                        {
                            newIndex = otherLightCount;
                            SetupPointLight(
                                otherLightCount++, i, ref visibleLight, light);
                        }

                        break;
                    case LightType.Spot:
                        if (otherLightCount < maxOtherLightCount)
                        {
                            newIndex = otherLightCount;
                            SetupSpotLight(otherLightCount++, i, ref visibleLight, light);
                        }

                        break;
                }

            if (useLightsPerObject) indexMap[i] = newIndex;
        }

        if (useLightsPerObject)
        {
            for (; i < indexMap.Length; i++) indexMap[i] = -1;
            cullingResults.SetLightIndexMap(indexMap);
            indexMap.Dispose();
        }
    }

    private void Render(RenderGraphContext context)
    {
        CommandBuffer buffer = context.cmd;
        buffer.SetKeyword(lightsPerObjectKeyword, useLightsPerObject);

        buffer.SetGlobalInt(dirLightCountId, dirLightCount);
        if (dirLightCount > 0)
        {
            buffer.SetGlobalVectorArray(dirLightColorsId, dirLightColors);
            buffer.SetGlobalVectorArray(
                dirLightDirectionsAndMasksId, dirLightDirectionsAndMasks);
            buffer.SetGlobalVectorArray(dirLightShadowDataId, dirLightShadowData);
        }

        buffer.SetGlobalInt(otherLightCountId, otherLightCount);
        if (otherLightCount > 0)
        {
            buffer.SetGlobalVectorArray(otherLightColorsId, otherLightColors);
            buffer.SetGlobalVectorArray(otherLightPositionsId, otherLightPositions);
            buffer.SetGlobalVectorArray(
                otherLightDirectionsAndMasksId, otherLightDirectionsAndMasks);
            buffer.SetGlobalVectorArray(otherLightSpotAnglesId, otherLightSpotAngles);
            buffer.SetGlobalVectorArray(otherLightShadowDataId, otherLightShadowData);
        }
        
        shadows.Render(context);
        context.renderContext.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    private void SetupDirectionalLight(
        int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
    {
        dirLightColors[index] = visibleLight.finalColor;
        var dirAndMask = -visibleLight.localToWorldMatrix.GetColumn(2);
        dirAndMask.w = light.renderingLayerMask.ReinterpretAsFloat();
        dirLightDirectionsAndMasks[index] = dirAndMask;
        dirLightShadowData[index] =
            shadows.ReserveDirectionalShadows(light, visibleIndex);
    }

    private void SetupPointLight(
        int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
    {
        otherLightColors[index] = visibleLight.finalColor;
        var position = visibleLight.localToWorldMatrix.GetColumn(3);
        position.w = 1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
        otherLightPositions[index] = position;
        otherLightSpotAngles[index] = new Vector4(0f, 1f);
        var dirAndmask = Vector4.zero;
        dirAndmask.w = light.renderingLayerMask.ReinterpretAsFloat();
        otherLightDirectionsAndMasks[index] = dirAndmask;
        otherLightShadowData[index] = shadows.ReserveOtherShadows(light, visibleIndex);
    }

    private void SetupSpotLight(
        int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
    {
        otherLightColors[index] = visibleLight.finalColor;
        var position = visibleLight.localToWorldMatrix.GetColumn(3);
        position.w = 1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
        otherLightPositions[index] = position;
        var dirAndMask = -visibleLight.localToWorldMatrix.GetColumn(2);
        dirAndMask.w = light.renderingLayerMask.ReinterpretAsFloat();
        otherLightDirectionsAndMasks[index] = dirAndMask;

        var innerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * light.innerSpotAngle);
        var outerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * visibleLight.spotAngle);
        var angleRangeInv = 1f / Mathf.Max(innerCos - outerCos, 0.001f);
        otherLightSpotAngles[index] = new Vector4(
            angleRangeInv, -outerCos * angleRangeInv
        );
        otherLightShadowData[index] = shadows.ReserveOtherShadows(light, visibleIndex);
    }

    public static ShadowTextures Record(
        RenderGraph renderGraph, CullingResults cullingResults, 
        ShadowSettings shadowSettings,
        bool useLightsPerObject, int renderingLayerMask)
    {
        using RenderGraphBuilder builder =
            renderGraph.AddRenderPass(
                sampler.name,
                out LightingPass pass,
                sampler);
        pass.Setup(cullingResults, shadowSettings,
            useLightsPerObject, renderingLayerMask);
        builder.SetRenderFunc<LightingPass>((pass, context) => pass.Render(context));
        builder.AllowPassCulling(false);
        return pass.shadows.GetRenderTextures(renderGraph, builder);
    }
}