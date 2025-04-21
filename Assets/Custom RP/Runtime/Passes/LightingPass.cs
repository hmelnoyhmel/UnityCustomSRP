using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public partial class LightingPass
{
    static readonly ProfilingSampler sampler = new("Lighting");
    
    private const int maxDirectionalLightCount = 4, maxOtherLightCount = 64;

    private static readonly GlobalKeyword lightsPerObjectKeyword =
        GlobalKeyword.Create("_LIGHTS_PER_OBJECT");
    
    private static readonly int
        directionalLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
        directionalLightDataId = Shader.PropertyToID("_DirectionalLightData"),
        otherLightCountId = Shader.PropertyToID("_OtherLightCount"),
        otherLightDataId = Shader.PropertyToID("_OtherLightData");

    static readonly DirectionalLightData[] directionalLightData = new DirectionalLightData[maxDirectionalLightCount];
    static readonly OtherLightData[] otherLightData = new OtherLightData[maxOtherLightCount];
    
    BufferHandle directionalLightDataBuffer;
    BufferHandle otherLightDataBuffer;
    
    private readonly Shadows shadows = new();

    private CullingResults cullingResults;
    
    int directionalLightCount, otherLightCount;

    bool useLightsPerObject;
    
    public void Setup(
        CullingResults cullingResults,
        ShadowSettings shadowSettings, 
        bool useLightsPerObject, 
        int renderingLayerMask)
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
        
        directionalLightCount = 0;
        otherLightCount = 0;
        int i;
        for (i = 0; i < visibleLights.Length; i++)
        {
            var newIndex = -1;
            var visibleLight = visibleLights[i];
            var light = visibleLight.light;
            if ((light.renderingLayerMask & renderingLayerMask) != 0)
            {
                switch (visibleLight.lightType)
                {
                    case LightType.Directional:
                        if (directionalLightCount < maxDirectionalLightCount)
                        {
                            directionalLightData[directionalLightCount++] =
                                new DirectionalLightData(
                                    ref visibleLight, light,
                                    shadows.ReserveDirectionalShadows(light, i));
                        }
                        break;

                    case LightType.Point:
                        if (otherLightCount < maxOtherLightCount)
                        {
                            newIndex = otherLightCount;
                            otherLightData[otherLightCount++] =
                                OtherLightData.CreatePointLight(
                                    ref visibleLight, light,
                                    shadows.ReserveOtherShadows(light, i));
                        }
                        break;

                    case LightType.Spot:
                        if (otherLightCount < maxOtherLightCount)
                        {
                            newIndex = otherLightCount;
                            otherLightData[otherLightCount++] =
                                OtherLightData.CreateSpotLight(
                                    ref visibleLight, light,
                                    shadows.ReserveOtherShadows(light, i));
                        }
                        break;

                }
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

        buffer.SetGlobalInt(directionalLightCountId, directionalLightCount);
        buffer.SetBufferData(
            directionalLightDataBuffer, 
            directionalLightData,
            0, 
            0, 
            directionalLightCount);
        buffer.SetGlobalBuffer(directionalLightDataId, directionalLightDataBuffer);

        buffer.SetGlobalInt(otherLightCountId, otherLightCount);
        buffer.SetBufferData(
            otherLightDataBuffer, 
            otherLightData, 
            0, 
            0, 
            otherLightCount);
        buffer.SetGlobalBuffer(otherLightDataId, otherLightDataBuffer);
        
        shadows.Render(context);
        context.renderContext.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    public static LightResources Record(
        RenderGraph renderGraph, 
        CullingResults cullingResults, 
        ShadowSettings shadowSettings,
        bool useLightsPerObject, 
        int renderingLayerMask)
    {
        using RenderGraphBuilder builder =
            renderGraph.AddRenderPass(
                sampler.name,
                out LightingPass pass,
                sampler);
        pass.Setup(cullingResults, shadowSettings,
            useLightsPerObject, renderingLayerMask);
        
        pass.directionalLightDataBuffer = builder.WriteBuffer(
            renderGraph.CreateBuffer(
                new BufferDesc(maxDirectionalLightCount, DirectionalLightData.stride)
            {
                name = "Directional Light Data"
            }));
        
        pass.otherLightDataBuffer = builder.WriteBuffer(
            renderGraph.CreateBuffer(
                new BufferDesc(maxOtherLightCount, OtherLightData.stride)
            {
                name = "Other Light Data"
            }));
        
        builder.SetRenderFunc<LightingPass>(static (pass, context) => pass.Render(context));
        builder.AllowPassCulling(false);
        
        return new LightResources(
            pass.directionalLightDataBuffer,
            pass.otherLightDataBuffer,
            pass.shadows.GetResources(renderGraph, builder));
    }
}