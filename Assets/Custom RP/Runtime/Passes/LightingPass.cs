using Custom_RP.Runtime.Passes.Lighting;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime.Passes
{
    public class LightingPass
    {
        private const int MaxDirectionalLightCount = 4;
        private const int MaxOtherLightCount = 128;
        private static readonly ProfilingSampler Sampler = new("Lighting");

        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int DirectionalLightDataId = Shader.PropertyToID("_DirectionalLightData");
        private static readonly int OtherLightCountId = Shader.PropertyToID("_OtherLightCount");
        private static readonly int OtherLightDataId = Shader.PropertyToID("_OtherLightData");
        private static readonly int TilesId = Shader.PropertyToID("_ForwardPlusTiles");
        private static readonly int TileSettingsId = Shader.PropertyToID("_ForwardPlusTileSettings");

        private static readonly DirectionalLightData[] DirectionalLightData =
            new DirectionalLightData[MaxDirectionalLightCount];

        private static readonly OtherLightData[] OtherLightData =
            new OtherLightData[MaxOtherLightCount];

        private readonly Shadows shadows = new();

        private CullingResults cullingResults;

        private int directionalLightCount;

        private BufferHandle directionalLightDataBuffer;
        private JobHandle forwardPlusJobHandle;

        private NativeArray<float4> lightBounds;

        private int maxLightsPerTile;
        private int maxTileDataSize;
        private int otherLightCount;
        private BufferHandle otherLightDataBuffer;

        private Vector2 screenUVToTileCoordinates;
        private Vector2Int tileCount;
        private NativeArray<int> tileData;
        private int tileDataSize;
        private BufferHandle tilesBuffer;

        private int TileCount => tileCount.x * tileCount.y;

        public void Setup(
            CullingResults cullingResults,
            Vector2Int attachmentSize,
            ForwardPlusSettings forwardPlusSettings,
            ShadowSettings shadowSettings,
            int renderingLayerMask)
        {
            this.cullingResults = cullingResults;
            shadows.Setup(cullingResults, shadowSettings);


            maxLightsPerTile = forwardPlusSettings.maxLightsPerTile <= 0 ? 31 : forwardPlusSettings.maxLightsPerTile;
            maxTileDataSize = maxLightsPerTile + 1;

            lightBounds = new NativeArray<float4>(
                MaxOtherLightCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            var tileScreenPixelSize = forwardPlusSettings.tileSize <= 0 ? 64f : (float)forwardPlusSettings.tileSize;

            screenUVToTileCoordinates.x = attachmentSize.x / tileScreenPixelSize;
            screenUVToTileCoordinates.y = attachmentSize.y / tileScreenPixelSize;
            tileCount.x = Mathf.CeilToInt(screenUVToTileCoordinates.x);
            tileCount.y = Mathf.CeilToInt(screenUVToTileCoordinates.y);

            SetupLights(renderingLayerMask);
        }

        private void SetupForwardPlus(int lightIndex, ref VisibleLight visibleLight)
        {
            var r = visibleLight.screenRect;
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
                    switch (visibleLight.lightType)
                    {
                        case LightType.Directional:
                            if (directionalLightCount < MaxDirectionalLightCount)
                                DirectionalLightData[directionalLightCount++] =
                                    new DirectionalLightData(
                                        ref visibleLight, light,
                                        shadows.ReserveDirectionalShadows(light, i));
                            break;

                        case LightType.Point:
                            if (otherLightCount < MaxOtherLightCount)
                            {
                                SetupForwardPlus(otherLightCount, ref visibleLight);
                                OtherLightData[otherLightCount++] =
                                    Lighting.OtherLightData.CreatePointLight(
                                        ref visibleLight, light,
                                        shadows.ReserveOtherShadows(light, i));
                            }

                            break;

                        case LightType.Spot:
                            if (otherLightCount < MaxOtherLightCount)
                            {
                                SetupForwardPlus(otherLightCount, ref visibleLight);
                                OtherLightData[otherLightCount++] =
                                    Lighting.OtherLightData.CreateSpotLight(
                                        ref visibleLight, light,
                                        shadows.ReserveOtherShadows(light, i));
                            }

                            break;
                    }
            }


            tileData = new NativeArray<int>(TileCount * tileDataSize, Allocator.TempJob);
            forwardPlusJobHandle = new ForwardPlusTilesJob
            {
                LightBounds = lightBounds,
                TileData = tileData,
                OtherLightCount = otherLightCount,
                TileScreenUVSize = math.float2(
                    1f / screenUVToTileCoordinates.x,
                    1f / screenUVToTileCoordinates.y),
                MaxLightsPerTile = requiredMaxLightsPerTile,
                TilesPerRow = tileCount.x,
                TileDataSize = tileDataSize
            }.ScheduleParallel(TileCount, tileCount.x, default);
        }

        private void Render(RenderGraphContext context)
        {
            var buffer = context.cmd;

            buffer.SetGlobalInt(DirectionalLightCountId, directionalLightCount);
            buffer.SetBufferData(
                directionalLightDataBuffer,
                DirectionalLightData,
                0,
                0,
                directionalLightCount);
            buffer.SetGlobalBuffer(DirectionalLightDataId, directionalLightDataBuffer);

            buffer.SetGlobalInt(OtherLightCountId, otherLightCount);
            buffer.SetBufferData(
                otherLightDataBuffer,
                OtherLightData,
                0,
                0,
                otherLightCount);
            buffer.SetGlobalBuffer(OtherLightDataId, otherLightDataBuffer);

            shadows.Render(context);

            forwardPlusJobHandle.Complete();
            buffer.SetBufferData(
                tilesBuffer,
                tileData,
                0,
                0,
                tileData.Length);

            buffer.SetGlobalBuffer(TilesId, tilesBuffer);
            buffer.SetGlobalVector(TileSettingsId,
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
            using var builder =
                renderGraph.AddRenderPass(
                    Sampler.name,
                    out LightingPass pass,
                    Sampler);

            pass.Setup(cullingResults, attachmentSize, forwardPlusSettings,
                shadowSettings, renderingLayerMask);

            pass.directionalLightDataBuffer = builder.WriteBuffer(
                renderGraph.CreateBuffer(
                    new BufferDesc(MaxDirectionalLightCount, Lighting.DirectionalLightData.Stride)
                    {
                        name = "Directional Light Data"
                    }));

            pass.otherLightDataBuffer = builder.WriteBuffer(
                renderGraph.CreateBuffer(
                    new BufferDesc(MaxOtherLightCount, Lighting.OtherLightData.Stride)
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
}