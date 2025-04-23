using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using Unity.Jobs;
using Unity.Mathematics;

public partial class LightingPass
{
    private static readonly ProfilingSampler sampler = new("Lighting");

    private const int maxDirectionalLightCount = 4;
    private const int maxOtherLightCount = 128;
    
    private int maxLightsPerTile;
    private int tileDataSize;
    private int maxTileDataSize;

    private Vector2 screenUVToTileCoordinates;
    private Vector2Int tileCount;

    private NativeArray<float4> lightBounds;
    private NativeArray<int> tileData;
    private JobHandle forwardPlusJobHandle;
    
    private int TileCount => tileCount.x * tileCount.y;

    private static readonly int directionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
    private static readonly int directionalLightDataId = Shader.PropertyToID("_DirectionalLightData");
    private static readonly int otherLightCountId = Shader.PropertyToID("_OtherLightCount");
    private static readonly int otherLightDataId = Shader.PropertyToID("_OtherLightData");
    private static readonly int tilesId = Shader.PropertyToID("_ForwardPlusTiles");
    private static readonly int tileSettingsId = Shader.PropertyToID("_ForwardPlusTileSettings");

    private static readonly DirectionalLightData[] directionalLightData =
        new DirectionalLightData[maxDirectionalLightCount];

    private static readonly OtherLightData[] otherLightData = 
        new OtherLightData[maxOtherLightCount];

    private BufferHandle directionalLightDataBuffer;
    private BufferHandle otherLightDataBuffer;
    private BufferHandle tilesBuffer;
    
    private readonly Shadows shadows = new();

    private CullingResults cullingResults;

    private int directionalLightCount;
    private int otherLightCount;
    
    public void Setup(
        CullingResults cullingResults,
        Vector2Int attachmentSize,
        ForwardPlusSettings forwardPlusSettings,
        ShadowSettings shadowSettings,
        int renderingLayerMask)
    {
        this.cullingResults = cullingResults;
        shadows.Setup(cullingResults, shadowSettings);
        
        
            maxLightsPerTile = forwardPlusSettings.maxLightsPerTile <= 0 ?
                31 : forwardPlusSettings.maxLightsPerTile;
            maxTileDataSize = maxLightsPerTile + 1;
            
            lightBounds = new NativeArray<float4>(
                maxOtherLightCount, 
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            
            float tileScreenPixelSize = forwardPlusSettings.tileSize <= 0 ?
                64f : (float)forwardPlusSettings.tileSize;
            
            screenUVToTileCoordinates.x = attachmentSize.x / tileScreenPixelSize;
            screenUVToTileCoordinates.y = attachmentSize.y / tileScreenPixelSize;
            tileCount.x = Mathf.CeilToInt(screenUVToTileCoordinates.x);
            tileCount.y = Mathf.CeilToInt(screenUVToTileCoordinates.y);
        
        SetupLights(renderingLayerMask);
    }
    
    void SetupForwardPlus(int lightIndex, ref VisibleLight visibleLight)
    {
        
            Rect r = visibleLight.screenRect;
            lightBounds[lightIndex] = math.float4(r.xMin, r.yMin, r.xMax, r.yMax);
        
    }

    private void SetupLights(int renderingLayerMask)
    {
        var visibleLights = cullingResults.visibleLights;
        var requiredMaxLightsPerTile = Mathf.Min(maxLightsPerTile, visibleLights.Length);
        tileDataSize = requiredMaxLightsPerTile + 1;
        
        directionalLightCount = 0;
        otherLightCount = 0;
        int i;
        for (i = 0; i < visibleLights.Length; i++)
        {
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
                            SetupForwardPlus(otherLightCount, ref visibleLight);
                            otherLightData[otherLightCount++] =
                                OtherLightData.CreatePointLight(
                                    ref visibleLight, light,
                                    shadows.ReserveOtherShadows(light, i));
                        }
                        break;

                    case LightType.Spot:
                        if (otherLightCount < maxOtherLightCount)
                        {
                            SetupForwardPlus(otherLightCount, ref visibleLight);
                            otherLightData[otherLightCount++] =
                                OtherLightData.CreateSpotLight(
                                    ref visibleLight, light,
                                    shadows.ReserveOtherShadows(light, i));
                        }
                        break;

                }
            }
        }

        
        tileData = new NativeArray<int>(TileCount * tileDataSize, Allocator.TempJob);
        forwardPlusJobHandle = new ForwardPlusTilesJob
        {
            lightBounds = lightBounds,
            tileData = tileData,
            otherLightCount = otherLightCount,
            tileScreenUVSize = math.float2(
                1f / screenUVToTileCoordinates.x,
                1f / screenUVToTileCoordinates.y),
            maxLightsPerTile = requiredMaxLightsPerTile,
            tilesPerRow = tileCount.x,
            tileDataSize = tileDataSize
        }.ScheduleParallel(TileCount, tileCount.x, default);
        
    }

    private void Render(RenderGraphContext context)
    {
        CommandBuffer buffer = context.cmd;

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
        
        forwardPlusJobHandle.Complete();
        buffer.SetBufferData(
            tilesBuffer, 
            tileData, 
            0, 
            0, 
            tileData.Length);
        
        buffer.SetGlobalBuffer(tilesId, tilesBuffer);
        buffer.SetGlobalVector(tileSettingsId, 
            new Vector4(
            screenUVToTileCoordinates.x,
            screenUVToTileCoordinates.y,
            tileCount.x.ReinterpretAsFloat(),
            tileDataSize.ReinterpretAsFloat()));
        
        context.renderContext.ExecuteCommandBuffer(buffer);
        buffer.Clear();
        lightBounds.Dispose();
        tileData.Dispose();
    }

    public static LightResources Record(
        RenderGraph renderGraph, 
        CullingResults cullingResults, 
        Vector2Int attachmentSize,
        ForwardPlusSettings forwardPlusSettings,
        ShadowSettings shadowSettings,
        int renderingLayerMask,
        ScriptableRenderContext context)
    {
        using RenderGraphBuilder builder =
            renderGraph.AddRenderPass(
                sampler.name,
                out LightingPass pass,
                sampler);
        
        pass.Setup(cullingResults, attachmentSize, forwardPlusSettings, 
            shadowSettings, renderingLayerMask);
        
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
        
        
            pass.tilesBuffer = builder.WriteBuffer(
                renderGraph.CreateBuffer(
                    new BufferDesc(pass.TileCount * pass.maxTileDataSize, 4)
                {
                    name = "Forward+ Tiles"
                }));
        
        
        builder.SetRenderFunc<LightingPass>(static (pass, context) => pass.Render(context));
        builder.AllowPassCulling(false);
        
        return new LightResources(
            pass.directionalLightDataBuffer,
            pass.otherLightDataBuffer,
            pass.tilesBuffer,
            pass.shadows.GetResources(renderGraph, builder, context));
    }
}