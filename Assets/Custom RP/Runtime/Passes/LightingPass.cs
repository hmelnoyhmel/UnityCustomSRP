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
    private const int maxLightsPerTile = 31;
    private const int tileDataSize = maxLightsPerTile + 1;
    private const int tileScreenPixelSize = 64;

    private Vector2 screenUVToTileCoordinates;
    private Vector2Int tileCount;

    private NativeArray<float4> lightBounds;
    private NativeArray<int> tileData;
    private JobHandle forwardPlusJobHandle;
    
    private int TileCount => tileCount.x * tileCount.y;

    private static readonly GlobalKeyword lightsPerObjectKeyword =
        GlobalKeyword.Create("_LIGHTS_PER_OBJECT");

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

    private bool useLightsPerObject;
    
    public void Setup(
        CullingResults cullingResults,
        Vector2Int attachmentSize,
        ShadowSettings shadowSettings, 
        bool useLightsPerObject, 
        int renderingLayerMask)
    {
        this.cullingResults = cullingResults;
        this.useLightsPerObject = useLightsPerObject;
        shadows.Setup(cullingResults, shadowSettings);
        
        if (!useLightsPerObject)
        {
            lightBounds = new NativeArray<float4>(
                maxOtherLightCount, 
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            
            screenUVToTileCoordinates.x = attachmentSize.x / (float)tileScreenPixelSize;
            screenUVToTileCoordinates.y = attachmentSize.y / (float)tileScreenPixelSize;
            tileCount.x = Mathf.CeilToInt(screenUVToTileCoordinates.x);
            tileCount.y = Mathf.CeilToInt(screenUVToTileCoordinates.y);
        }
        
        SetupLights(renderingLayerMask);
    }
    
    void SetupForwardPlus(int lightIndex, ref VisibleLight visibleLight)
    {
        if (!useLightsPerObject)
        {
            Rect r = visibleLight.screenRect;
            lightBounds[lightIndex] = math.float4(r.xMin, r.yMin, r.xMax, r.yMax);
        }
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
                            newIndex = otherLightCount;
                            SetupForwardPlus(otherLightCount, ref visibleLight);
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
        else
        {
            tileData = new NativeArray<int>(TileCount * tileDataSize, Allocator.TempJob);
            forwardPlusJobHandle = new ForwardPlusTilesJob
            {
                lightBounds = lightBounds,
                tileData = tileData,
                otherLightCount = otherLightCount,
                tileScreenUVSize = math.float2(
                    1f / screenUVToTileCoordinates.x,
                    1f / screenUVToTileCoordinates.y),
                maxLightsPerTile = maxLightsPerTile,
                tilesPerRow = tileCount.x,
                tileDataSize = tileDataSize
            }.ScheduleParallel(TileCount, tileCount.x, default);
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
        
        if (useLightsPerObject)
        {
            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
            return;
        }
        
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
        ShadowSettings shadowSettings,
        bool useLightsPerObject, 
        int renderingLayerMask)
    {
        using RenderGraphBuilder builder =
            renderGraph.AddRenderPass(
                sampler.name,
                out LightingPass pass,
                sampler);
        
        pass.Setup(cullingResults, attachmentSize, shadowSettings,
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
        
        if (!useLightsPerObject)
        {
            pass.tilesBuffer = builder.WriteBuffer(
                renderGraph.CreateBuffer(
                    new BufferDesc(pass.TileCount * tileDataSize, 4)
                {
                    name = "Forward+ Tiles"
                }));
        }
        
        builder.SetRenderFunc<LightingPass>(static (pass, context) => pass.Render(context));
        builder.AllowPassCulling(false);
        
        return new LightResources(
            pass.directionalLightDataBuffer,
            pass.otherLightDataBuffer,
            pass.tilesBuffer,
            pass.shadows.GetResources(renderGraph, builder));
    }
}