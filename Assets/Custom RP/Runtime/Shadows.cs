using Custom_RP.Runtime.Passes.Lighting;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime
{
    public class Shadows
    {
        private const int MaxShadowedDirLightCount = 4, MaxShadowedOtherLightCount = 16;
        private const int MaxCascades = 4;

        private static readonly GlobalKeyword[] FilterQualityKeywords =
        {
            GlobalKeyword.Create("_SHADOW_FILTER_MEDIUM"),
            GlobalKeyword.Create("_SHADOW_FILTER_HIGH"),
        };

        private static readonly GlobalKeyword SoftCascadeBlendKeyword =
            GlobalKeyword.Create("_SOFT_CASCADE_BLEND");

        private static readonly GlobalKeyword[] ShadowMaskKeywords =
        {
            GlobalKeyword.Create("_SHADOW_MASK_ALWAYS"),
            GlobalKeyword.Create("_SHADOW_MASK_DISTANCE")
        };

        private static readonly int
            DirectionalShadowAtlasId = Shader.PropertyToID("_DirectionalShadowAtlas"),
            DirectionalShadowCascadesId = Shader.PropertyToID("_DirectionalShadowCascades"),
            DirectionalShadowMatricesId = Shader.PropertyToID("_DirectionalShadowMatrices"),
            OtherShadowAtlasId = Shader.PropertyToID("_OtherShadowAtlas"),
            OtherShadowDataId = Shader.PropertyToID("_OtherShadowData"),
            CascadeCountId = Shader.PropertyToID("_CascadeCount"),
            ShadowAtlastSizeId = Shader.PropertyToID("_ShadowAtlasSize"),
            ShadowDistanceFadeId = Shader.PropertyToID("_ShadowDistanceFade"),
            ShadowPancakingId = Shader.PropertyToID("_ShadowPancaking");

        static readonly DirectionalShadowCascade[] DirectionalShadowCascades =
            new DirectionalShadowCascade[MaxCascades];

        private static readonly Matrix4x4[] DirectionalShadowMatrices = 
            new Matrix4x4[MaxShadowedDirLightCount * MaxCascades];
    
        static readonly OtherShadowData[] OtherShadowData =
            new OtherShadowData[MaxShadowedOtherLightCount];
    
        CommandBuffer buffer;

        private readonly ShadowedDirectionalLight[] shadowedDirectionalLights =
            new ShadowedDirectionalLight[MaxShadowedDirLightCount];

        private readonly ShadowedOtherLight[] shadowedOtherLights =
            new ShadowedOtherLight[MaxShadowedOtherLightCount];

        private Vector4 atlasSizes;

        private CullingResults cullingResults;

        private ShadowSettings settings;

        private int shadowedDirectionalLightCount, shadowedOtherLightCount;

        private bool useShadowMask;

        private TextureHandle directionalAtlas;
        private TextureHandle otherAtlas;

        private BufferHandle directionalShadowCascadesBuffer;
        private BufferHandle directionalShadowMatricesBuffer;
        private BufferHandle otherShadowDataBuffer;

        private NativeArray<LightShadowCasterCullingInfo> cullingInfoPerLight;
        private NativeArray<ShadowSplitData> shadowSplitDataPerLight;

        private const int MaxTilesPerLight = 6;
    
        struct RenderInfo
        {
            public RendererListHandle Handle;
            public Matrix4x4 View;
            public Matrix4x4 Projection;
        }

        private int directionalSplit, directionalTileSize;
        private int otherSplit, otherTileSize;

        private RenderInfo[] directionalRenderInfo =
            new RenderInfo[MaxShadowedDirLightCount * MaxCascades];

        private RenderInfo[] otherRenderInfo =
            new RenderInfo[MaxShadowedOtherLightCount * MaxTilesPerLight];
    
        public void Setup(
            CullingResults cullingResults,
            ShadowSettings settings)
        {
            this.cullingResults = cullingResults;
            this.settings = settings;
            shadowedDirectionalLightCount = shadowedOtherLightCount = 0;
            useShadowMask = false;
            cullingInfoPerLight = new NativeArray<LightShadowCasterCullingInfo>(
                cullingResults.visibleLights.Length, Allocator.Temp);
        
            shadowSplitDataPerLight = new NativeArray<ShadowSplitData>(
                cullingInfoPerLight.Length * MaxTilesPerLight,
                Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        }

        public ShadowResources GetResources(
            RenderGraph renderGraph,
            RenderGraphBuilder builder,
            ScriptableRenderContext context)
        {
            int atlasSize = (int)settings.directional.atlasSize;
            var desc = new TextureDesc(atlasSize, atlasSize)
            {
                depthBufferBits = DepthBits.Depth32,
                isShadowMap = true,
                name = "Directional Shadow Atlas"
            };
            directionalAtlas = shadowedDirectionalLightCount > 0 ?
                builder.WriteTexture(renderGraph.CreateTexture(desc)) :
                renderGraph.defaultResources.defaultShadowTexture;

            directionalShadowCascadesBuffer = builder.WriteBuffer(
                renderGraph.CreateBuffer(
                    new BufferDesc(MaxCascades, DirectionalShadowCascade.Stride)
                    {
                        name = "Shadow Cascades"
                    }));

            directionalShadowMatricesBuffer = builder.WriteBuffer(
                renderGraph.CreateBuffer(
                    new BufferDesc(MaxShadowedDirLightCount * MaxCascades, 4 * 16)
                    {
                        name = "Directional Shadow Matrices"
                    }));
        
            otherShadowDataBuffer = builder.WriteBuffer(
                renderGraph.CreateBuffer(
                    new BufferDesc(MaxShadowedOtherLightCount, Passes.Lighting.OtherShadowData.Stride)
                    {
                        name = "Other Shadow Data"
                    }));
        
            atlasSize = (int)settings.other.atlasSize;
            desc.width = desc.height = atlasSize;
            desc.name = "Other Shadow Atlas";
            otherAtlas = shadowedOtherLightCount > 0 ?
                builder.WriteTexture(renderGraph.CreateTexture(desc)) :
                renderGraph.defaultResources.defaultShadowTexture;
        
            BuildRendererLists(renderGraph, builder, context);
        
            return new ShadowResources(
                directionalAtlas, 
                otherAtlas,
                directionalShadowCascadesBuffer,
                directionalShadowMatricesBuffer,
                otherShadowDataBuffer);
        }

        void BuildRendererLists(
            RenderGraph renderGraph,
            RenderGraphBuilder builder,
            ScriptableRenderContext context)
        {
            if (shadowedDirectionalLightCount > 0)
            {
                int atlasSize = (int)settings.directional.atlasSize;
                int tiles =
                    shadowedDirectionalLightCount * settings.directional.cascadeCount;
                directionalSplit = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
                directionalTileSize = atlasSize / directionalSplit;

                for (int i = 0; i < shadowedDirectionalLightCount; i++)
                {
                    BuildDirectionalRendererList(i, renderGraph, builder);
                }
            }

            if (shadowedOtherLightCount > 0)
            {
                int atlasSize = (int)settings.other.atlasSize;
                int tiles = shadowedOtherLightCount;
                otherSplit = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
                otherTileSize = atlasSize / otherSplit;

                for (int i = 0; i < shadowedOtherLightCount;)
                {
                    if (shadowedOtherLights[i].IsPoint)
                    {
                        BuildPointShadowsRendererList(i, renderGraph, builder);
                        i += 6;
                    }
                    else
                    {
                        BuildSpotShadowsRendererList(i, renderGraph, builder);
                        i += 1;
                    }
                }
            }

            if (shadowedDirectionalLightCount + shadowedOtherLightCount > 0)
            {
                context.CullShadowCasters(
                    cullingResults,
                    new ShadowCastersCullingInfos
                    {
                        perLightInfos = cullingInfoPerLight,
                        splitBuffer = shadowSplitDataPerLight
                    });
            }
        }
    
        void BuildSpotShadowsRendererList(
            int index,
            RenderGraph renderGraph,
            RenderGraphBuilder builder)
        {
            ShadowedOtherLight light = shadowedOtherLights[index];
        
            var shadowSettings = new ShadowDrawingSettings(
                cullingResults, light.VisibleLightIndex)
            {
                useRenderingLayerMaskTest = true
            };
            ref RenderInfo info = ref otherRenderInfo[index * MaxTilesPerLight];
        
            cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(
                light.VisibleLightIndex, out info.View, out info.Projection,
                out ShadowSplitData splitData);

            int splitOffset = light.VisibleLightIndex * MaxTilesPerLight;
            shadowSplitDataPerLight[splitOffset] = splitData;
        
            info.Handle = builder.UseRendererList(
                renderGraph.CreateShadowRendererList(ref shadowSettings));
        
            cullingInfoPerLight[light.VisibleLightIndex] =
                new LightShadowCasterCullingInfo
                {
                    projectionType = BatchCullingProjectionType.Perspective,
                    splitRange = new RangeInt(splitOffset, 1)
                };
        }
    
        void BuildPointShadowsRendererList(
            int index, 
            RenderGraph renderGraph, 
            RenderGraphBuilder builder)
        {
            ShadowedOtherLight light = shadowedOtherLights[index];
            var shadowSettings = new ShadowDrawingSettings(
                cullingResults, light.VisibleLightIndex)
            {
                useRenderingLayerMaskTest = true
            };
        
            float texelSize = 2f / otherTileSize;
            float filterSize = texelSize * settings.OtherFilterSize;
            float bias = light.NormalBias * filterSize * 1.4142136f;
            float fovBias =
                Mathf.Atan(1f + bias + filterSize) * Mathf.Rad2Deg * 2f - 90f;
            int splitOffset = light.VisibleLightIndex * MaxTilesPerLight;
            for (int i = 0; i < 6; i++)
            {
                ref RenderInfo info =
                    ref otherRenderInfo[index * MaxTilesPerLight + i];
                cullingResults.ComputePointShadowMatricesAndCullingPrimitives(
                    light.VisibleLightIndex, (CubemapFace)i, fovBias,
                    out info.View, out info.Projection,
                    out ShadowSplitData splitData);
                shadowSplitDataPerLight[splitOffset + i] = splitData;
                info.Handle = builder.UseRendererList(
                    renderGraph.CreateShadowRendererList(ref shadowSettings));
            }

            cullingInfoPerLight[light.VisibleLightIndex] =
                new LightShadowCasterCullingInfo
                {
                    projectionType = BatchCullingProjectionType.Perspective,
                    splitRange = new RangeInt(splitOffset, 6)
                };
        }
    
        void BuildDirectionalRendererList(
            int index,
            RenderGraph renderGraph,
            RenderGraphBuilder builder)
        {
            ShadowedDirectionalLight light = shadowedDirectionalLights[index];
            var shadowSettings = new ShadowDrawingSettings(
                cullingResults, light.VisibleLightIndex)
            {
                useRenderingLayerMaskTest = true
            };

            int cascadeCount = settings.directional.cascadeCount;
            Vector3 ratios = settings.directional.CascadeRatios;
            float cullingFactor =
                Mathf.Max(0f, 0.8f - settings.directional.cascadeFade);
            int splitOffset = light.VisibleLightIndex * MaxTilesPerLight;
            for (int i = 0; i < cascadeCount; i++)
            {
                ref RenderInfo info =
                    ref directionalRenderInfo[index * MaxCascades + i];
                cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                    light.VisibleLightIndex, i, cascadeCount, ratios,
                    directionalTileSize, light.NearPlaneOffset, out info.View,
                    out info.Projection, out ShadowSplitData splitData);
                splitData.shadowCascadeBlendCullingFactor = cullingFactor;
                shadowSplitDataPerLight[splitOffset + i] = splitData;
                if (index == 0)
                {
                    DirectionalShadowCascades[i] = new DirectionalShadowCascade(
                        splitData.cullingSphere,
                        directionalTileSize, settings.DirectionalFilterSize);
                }
                info.Handle =
                    builder.UseRendererList(renderGraph.CreateShadowRendererList(
                        ref shadowSettings));
            }

            cullingInfoPerLight[light.VisibleLightIndex] =
                new LightShadowCasterCullingInfo
                {
                    projectionType = BatchCullingProjectionType.Orthographic,
                    splitRange = new RangeInt(splitOffset, cascadeCount)
                };
        }
    
        public Vector4 ReserveDirectionalShadows(Light light, int visibleLightIndex)
        {
            if (
                shadowedDirectionalLightCount < MaxShadowedDirLightCount &&
                light.shadows != LightShadows.None && light.shadowStrength > 0f)
            {
                float maskChannel = -1;
                var lightBaking = light.bakingOutput;
                if (
                    lightBaking.lightmapBakeType == LightmapBakeType.Mixed &&
                    lightBaking.mixedLightingMode == MixedLightingMode.Shadowmask)
                {
                    useShadowMask = true;
                    maskChannel = lightBaking.occlusionMaskChannel;
                }

                if (!cullingResults.GetShadowCasterBounds(visibleLightIndex, out _))
                    return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);

                shadowedDirectionalLights[shadowedDirectionalLightCount] =
                    new ShadowedDirectionalLight
                    {
                        VisibleLightIndex = visibleLightIndex,
                        SlopeScaleBias = light.shadowBias,
                        NearPlaneOffset = light.shadowNearPlane
                    };
                return new Vector4(
                    light.shadowStrength,
                    settings.directional.cascadeCount * shadowedDirectionalLightCount++,
                    light.shadowNormalBias, maskChannel);
            }

            return new Vector4(0f, 0f, 0f, -1f);
        }

        public Vector4 ReserveOtherShadows(Light light, int visibleLightIndex)
        {
            if (light.shadows == LightShadows.None || light.shadowStrength <= 0f) return new Vector4(0f, 0f, 0f, -1f);

            var maskChannel = -1f;
            var lightBaking = light.bakingOutput;
            if (
                lightBaking.lightmapBakeType == LightmapBakeType.Mixed &&
                lightBaking.mixedLightingMode == MixedLightingMode.Shadowmask)
            {
                useShadowMask = true;
                maskChannel = lightBaking.occlusionMaskChannel;
            }

            var isPoint = light.type == LightType.Point;
            var newLightCount = shadowedOtherLightCount + (isPoint ? 6 : 1);
            if (
                newLightCount > MaxShadowedOtherLightCount ||
                !cullingResults.GetShadowCasterBounds(visibleLightIndex, out _))
                return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);

            shadowedOtherLights[shadowedOtherLightCount] = new ShadowedOtherLight
            {
                VisibleLightIndex = visibleLightIndex,
                SlopeScaleBias = light.shadowBias,
                NormalBias = light.shadowNormalBias,
                IsPoint = isPoint
            };

            var data = new Vector4(
                light.shadowStrength, shadowedOtherLightCount,
                isPoint ? 1f : 0f, maskChannel);
            shadowedOtherLightCount = newLightCount;
            return data;
        }

        public void Render(RenderGraphContext context)
        {
            buffer = context.cmd;
        
            if (shadowedDirectionalLightCount > 0)
                RenderDirectionalShadows();
        
            if (shadowedOtherLightCount > 0)
                RenderOtherShadows();
        
            SetKeywords(FilterQualityKeywords, (int)settings.filterQuality - 1);
        
            buffer.SetGlobalDepthBias(0f, 0f);
            buffer.SetGlobalBuffer(
                DirectionalShadowCascadesId, directionalShadowCascadesBuffer);
            buffer.SetGlobalBuffer(
                DirectionalShadowMatricesId, directionalShadowMatricesBuffer);
            buffer.SetGlobalBuffer(OtherShadowDataId, otherShadowDataBuffer);
            buffer.SetGlobalTexture(DirectionalShadowAtlasId, directionalAtlas);
            buffer.SetGlobalTexture(OtherShadowAtlasId, otherAtlas);
        
            SetKeywords(ShadowMaskKeywords,
                useShadowMask ? QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask ? 0 : 1 : -1);
            buffer.SetGlobalInt(CascadeCountId, shadowedDirectionalLightCount > 0 ? settings.directional.cascadeCount : 0);
            var f = 1f - settings.directional.cascadeFade;
            buffer.SetGlobalVector(ShadowDistanceFadeId, new Vector4(
                1f / settings.maxDistance, 1f / settings.distanceFade,
                1f / (1f - f * f)));
            buffer.SetGlobalVector(ShadowAtlastSizeId, atlasSizes);
            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        private void RenderDirectionalShadows()
        {
            var atlasSize = (int)settings.directional.atlasSize;
            atlasSizes.x = atlasSize;
            atlasSizes.y = 1f / atlasSize;
            buffer.BeginSample("Directional Shadows");
            buffer.SetRenderTarget(
                directionalAtlas,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            buffer.ClearRenderTarget(true, false, Color.clear);
            buffer.SetGlobalFloat(ShadowPancakingId, 1f);

            for (var i = 0; i < shadowedDirectionalLightCount; i++) 
                RenderDirectionalShadows(i);

            buffer.SetBufferData(
                directionalShadowCascadesBuffer, 
                DirectionalShadowCascades,
                0, 
                0, 
                settings.directional.cascadeCount);
        
            buffer.SetBufferData(
                directionalShadowMatricesBuffer, 
                DirectionalShadowMatrices,
                0, 
                0, 
                shadowedDirectionalLightCount * settings.directional.cascadeCount);
        
            buffer.SetKeyword(SoftCascadeBlendKeyword, settings.directional.softCascadeBlend);
            buffer.EndSample("Directional Shadows");
        }

        private void RenderDirectionalShadows(int index)
        {
            var cascadeCount = settings.directional.cascadeCount;
            var tileOffset = index * cascadeCount;
            var tileScale = 1f / directionalSplit;
            buffer.SetGlobalDepthBias(0f, shadowedDirectionalLights[index].SlopeScaleBias);
        
            for (var i = 0; i < cascadeCount; i++)
            {
                RenderInfo info = directionalRenderInfo[index * MaxCascades + i];
            
                var tileIndex = tileOffset + i;
                DirectionalShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
                    info.Projection * info.View,
                    SetTileViewport(tileIndex, directionalSplit, directionalTileSize), 
                    tileScale);
                buffer.SetViewProjectionMatrices(info.View, info.Projection);
                buffer.DrawRendererList(info.Handle);
            }
        }

        private void RenderOtherShadows()
        {
            var atlasSize = (int)settings.other.atlasSize;
            atlasSizes.z = atlasSize;
            atlasSizes.w = 1f / atlasSize;
            buffer.BeginSample("Other Shadows");
        
            buffer.SetRenderTarget(
                otherAtlas,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            buffer.ClearRenderTarget(true, false, Color.clear);
            buffer.SetGlobalFloat(ShadowPancakingId, 0f);

            for (var i = 0; i < shadowedOtherLightCount;)
                if (shadowedOtherLights[i].IsPoint)
                {
                    RenderPointShadows(i);
                    i += 6;
                }
                else
                {
                    RenderSpotShadows(i);
                    i += 1;
                }

            buffer.SetBufferData(
                otherShadowDataBuffer, 
                OtherShadowData,
                0, 
                0, 
                shadowedOtherLightCount);
        
            buffer.EndSample("Other Shadows");
        }

        private void RenderSpotShadows(int index)
        {
            var light = shadowedOtherLights[index];
            RenderInfo info = otherRenderInfo[index * MaxTilesPerLight];
            var texelSize = 2f / (otherTileSize * info.Projection.m00);
            var filterSize = texelSize * settings.OtherFilterSize;
            var bias = light.NormalBias * filterSize * 1.4142136f;
            var offset = SetTileViewport(index, otherSplit, otherTileSize);
            var tileScale = 1f / otherSplit;
        
            OtherShadowData[index] = new OtherShadowData(
                offset, 
                tileScale, 
                bias, 
                atlasSizes.w * 0.5f,
                ConvertToAtlasMatrix(info.Projection * info.View, offset, tileScale));

            buffer.SetViewProjectionMatrices(info.View, info.Projection);
            buffer.SetGlobalDepthBias(0f, light.SlopeScaleBias);
            buffer.DrawRendererList(info.Handle);
        }

        private void RenderPointShadows(int index)
        {
            var light = shadowedOtherLights[index];
            var texelSize = 2f / otherTileSize;
            var filterSize = texelSize * settings.OtherFilterSize;
            var bias = light.NormalBias * filterSize * 1.4142136f;
            var tileScale = 1f / otherSplit;
            buffer.SetGlobalDepthBias(0f, light.SlopeScaleBias);
            for (var i = 0; i < 6; i++)
            {
                RenderInfo info = otherRenderInfo[index * MaxTilesPerLight + i];
                info.View.m11 = -info.View.m11;
                info.View.m12 = -info.View.m12;
                info.View.m13 = -info.View.m13;
            
                var tileIndex = index + i;
                var offset = SetTileViewport(tileIndex, otherSplit, otherTileSize);
            
                OtherShadowData[tileIndex] = new OtherShadowData(
                    offset, 
                    tileScale, 
                    bias, 
                    atlasSizes.w * 0.5f,
                    ConvertToAtlasMatrix(info.Projection * info.View, offset, tileScale));

                buffer.SetViewProjectionMatrices(info.View, info.Projection);
                buffer.DrawRendererList(info.Handle);
            }
        }

        private Matrix4x4 ConvertToAtlasMatrix(Matrix4x4 m, Vector2 offset, float scale)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                m.m20 = -m.m20;
                m.m21 = -m.m21;
                m.m22 = -m.m22;
                m.m23 = -m.m23;
            }

            m.m00 = (0.5f * (m.m00 + m.m30) + offset.x * m.m30) * scale;
            m.m01 = (0.5f * (m.m01 + m.m31) + offset.x * m.m31) * scale;
            m.m02 = (0.5f * (m.m02 + m.m32) + offset.x * m.m32) * scale;
            m.m03 = (0.5f * (m.m03 + m.m33) + offset.x * m.m33) * scale;
            m.m10 = (0.5f * (m.m10 + m.m30) + offset.y * m.m30) * scale;
            m.m11 = (0.5f * (m.m11 + m.m31) + offset.y * m.m31) * scale;
            m.m12 = (0.5f * (m.m12 + m.m32) + offset.y * m.m32) * scale;
            m.m13 = (0.5f * (m.m13 + m.m33) + offset.y * m.m33) * scale;
            m.m20 = 0.5f * (m.m20 + m.m30);
            m.m21 = 0.5f * (m.m21 + m.m31);
            m.m22 = 0.5f * (m.m22 + m.m32);
            m.m23 = 0.5f * (m.m23 + m.m33);
            return m;
        }

        private Vector2 SetTileViewport(int index, int split, float tileSize)
        {
            var offset = new Vector2(index % split, index / split);
            buffer.SetViewport(new Rect(
                offset.x * tileSize, offset.y * tileSize, tileSize, tileSize));
            return offset;
        }

        private void SetKeywords(GlobalKeyword[] keywords, int enabledIndex)
        {
            for (var i = 0; i < keywords.Length; i++)
            {
                buffer.SetKeyword(keywords[i], i == enabledIndex);
            }
        }

        private struct ShadowedDirectionalLight
        {
            public int VisibleLightIndex;
            public float SlopeScaleBias;
            public float NearPlaneOffset;
        }

        private struct ShadowedOtherLight
        {
            public int VisibleLightIndex;
            public float SlopeScaleBias;
            public float NormalBias;
            public bool IsPoint;
        }
    }
}