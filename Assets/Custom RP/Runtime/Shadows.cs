using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public partial class Shadows
{
    private const int maxShadowedDirLightCount = 4, maxShadowedOtherLightCount = 16;
    private const int maxCascades = 4;

    private static readonly GlobalKeyword[] filterQualityKeywords =
    {
        GlobalKeyword.Create("_SHADOW_FILTER_MEDIUM"),
        GlobalKeyword.Create("_SHADOW_FILTER_HIGH"),
    };

    private static readonly GlobalKeyword softCascadeBlendKeyword =
        GlobalKeyword.Create("_SOFT_CASCADE_BLEND");

    private static readonly GlobalKeyword[] shadowMaskKeywords =
    {
        GlobalKeyword.Create("_SHADOW_MASK_ALWAYS"),
            GlobalKeyword.Create("_SHADOW_MASK_DISTANCE")
    };

    private static readonly int
        directionalShadowAtlasId = Shader.PropertyToID("_DirectionalShadowAtlas"),
        directionalShadowCascadesId = Shader.PropertyToID("_DirectionalShadowCascades"),
        directionalShadowMatricesId = Shader.PropertyToID("_DirectionalShadowMatrices"),
        otherShadowAtlasId = Shader.PropertyToID("_OtherShadowAtlas"),
        otherShadowDataId = Shader.PropertyToID("_OtherShadowData"),
        cascadeCountId = Shader.PropertyToID("_CascadeCount"),
        shadowAtlastSizeId = Shader.PropertyToID("_ShadowAtlasSize"),
        shadowDistanceFadeId = Shader.PropertyToID("_ShadowDistanceFade"),
        shadowPancakingId = Shader.PropertyToID("_ShadowPancaking");

    static readonly DirectionalShadowCascade[] directionalShadowCascades =
        new DirectionalShadowCascade[maxCascades];

    private static readonly Matrix4x4[] directionalShadowMatrices = 
        new Matrix4x4[maxShadowedDirLightCount * maxCascades];
    
    static readonly OtherShadowData[] otherShadowData =
        new OtherShadowData[maxShadowedOtherLightCount];
    
    CommandBuffer buffer;

    private readonly ShadowedDirectionalLight[] shadowedDirectionalLights =
        new ShadowedDirectionalLight[maxShadowedDirLightCount];

    private readonly ShadowedOtherLight[] shadowedOtherLights =
        new ShadowedOtherLight[maxShadowedOtherLightCount];

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

    private const int maxTilesPerLight = 6;
    
    struct RenderInfo
    {
        public RendererListHandle handle;
        public Matrix4x4 view;
        public Matrix4x4 projection;
    }

    private int directionalSplit, directionalTileSize;
    private int otherSplit, otherTileSize;

    private RenderInfo[] directionalRenderInfo =
        new RenderInfo[maxShadowedDirLightCount * maxCascades];

    private RenderInfo[] otherRenderInfo =
        new RenderInfo[maxShadowedOtherLightCount * maxTilesPerLight];
    
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
            cullingInfoPerLight.Length * maxTilesPerLight,
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
                new BufferDesc(maxCascades, DirectionalShadowCascade.stride)
            {
                name = "Shadow Cascades"
            }));

        directionalShadowMatricesBuffer = builder.WriteBuffer(
            renderGraph.CreateBuffer(
                new BufferDesc(maxShadowedDirLightCount * maxCascades, 4 * 16)
            {
                name = "Directional Shadow Matrices"
            }));
        
        otherShadowDataBuffer = builder.WriteBuffer(
            renderGraph.CreateBuffer(
                new BufferDesc(maxShadowedOtherLightCount, OtherShadowData.stride)
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
                if (shadowedOtherLights[i].isPoint)
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
            cullingResults, light.visibleLightIndex)
        {
            useRenderingLayerMaskTest = true
        };
        ref RenderInfo info = ref otherRenderInfo[index * maxTilesPerLight];
        
        cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(
            light.visibleLightIndex, out info.view, out info.projection,
            out ShadowSplitData splitData);

        int splitOffset = light.visibleLightIndex * maxTilesPerLight;
        shadowSplitDataPerLight[splitOffset] = splitData;
        
        info.handle = builder.UseRendererList(
            renderGraph.CreateShadowRendererList(ref shadowSettings));
        
        cullingInfoPerLight[light.visibleLightIndex] =
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
            cullingResults, light.visibleLightIndex)
        {
            useRenderingLayerMaskTest = true
        };
        
        float texelSize = 2f / otherTileSize;
        float filterSize = texelSize * settings.OtherFilterSize;
        float bias = light.normalBias * filterSize * 1.4142136f;
        float fovBias =
            Mathf.Atan(1f + bias + filterSize) * Mathf.Rad2Deg * 2f - 90f;
        int splitOffset = light.visibleLightIndex * maxTilesPerLight;
        for (int i = 0; i < 6; i++)
        {
            ref RenderInfo info =
                ref otherRenderInfo[index * maxTilesPerLight + i];
            cullingResults.ComputePointShadowMatricesAndCullingPrimitives(
                light.visibleLightIndex, (CubemapFace)i, fovBias,
                out info.view, out info.projection,
                out ShadowSplitData splitData);
            shadowSplitDataPerLight[splitOffset + i] = splitData;
            info.handle = builder.UseRendererList(
                renderGraph.CreateShadowRendererList(ref shadowSettings));
        }

        cullingInfoPerLight[light.visibleLightIndex] =
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
            cullingResults, light.visibleLightIndex)
        {
            useRenderingLayerMaskTest = true
        };

        int cascadeCount = settings.directional.cascadeCount;
        Vector3 ratios = settings.directional.CascadeRatios;
        float cullingFactor =
            Mathf.Max(0f, 0.8f - settings.directional.cascadeFade);
        int splitOffset = light.visibleLightIndex * maxTilesPerLight;
        for (int i = 0; i < cascadeCount; i++)
        {
            ref RenderInfo info =
                ref directionalRenderInfo[index * maxCascades + i];
            cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                light.visibleLightIndex, i, cascadeCount, ratios,
                directionalTileSize, light.nearPlaneOffset, out info.view,
                out info.projection, out ShadowSplitData splitData);
            splitData.shadowCascadeBlendCullingFactor = cullingFactor;
            shadowSplitDataPerLight[splitOffset + i] = splitData;
            if (index == 0)
            {
                directionalShadowCascades[i] = new DirectionalShadowCascade(
                    splitData.cullingSphere,
                    directionalTileSize, settings.DirectionalFilterSize);
            }
            info.handle =
                builder.UseRendererList(renderGraph.CreateShadowRendererList(
                    ref shadowSettings));
        }

        cullingInfoPerLight[light.visibleLightIndex] =
            new LightShadowCasterCullingInfo
            {
                projectionType = BatchCullingProjectionType.Orthographic,
                splitRange = new RangeInt(splitOffset, cascadeCount)
            };
    }
    
    public Vector4 ReserveDirectionalShadows(Light light, int visibleLightIndex)
    {
        if (
            shadowedDirectionalLightCount < maxShadowedDirLightCount &&
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
                    visibleLightIndex = visibleLightIndex,
                    slopeScaleBias = light.shadowBias,
                    nearPlaneOffset = light.shadowNearPlane
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
            newLightCount > maxShadowedOtherLightCount ||
            !cullingResults.GetShadowCasterBounds(visibleLightIndex, out _))
            return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);

        shadowedOtherLights[shadowedOtherLightCount] = new ShadowedOtherLight
        {
            visibleLightIndex = visibleLightIndex,
            slopeScaleBias = light.shadowBias,
            normalBias = light.shadowNormalBias,
            isPoint = isPoint
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
        
        SetKeywords(filterQualityKeywords, (int)settings.filterQuality - 1);
        
        buffer.SetGlobalDepthBias(0f, 0f);
        buffer.SetGlobalBuffer(
            directionalShadowCascadesId, directionalShadowCascadesBuffer);
        buffer.SetGlobalBuffer(
            directionalShadowMatricesId, directionalShadowMatricesBuffer);
        buffer.SetGlobalBuffer(otherShadowDataId, otherShadowDataBuffer);
        buffer.SetGlobalTexture(directionalShadowAtlasId, directionalAtlas);
        buffer.SetGlobalTexture(otherShadowAtlasId, otherAtlas);
        
        SetKeywords(shadowMaskKeywords,
            useShadowMask ? QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask ? 0 : 1 : -1);
        buffer.SetGlobalInt(cascadeCountId, shadowedDirectionalLightCount > 0 ? settings.directional.cascadeCount : 0);
        var f = 1f - settings.directional.cascadeFade;
        buffer.SetGlobalVector(shadowDistanceFadeId, new Vector4(
            1f / settings.maxDistance, 1f / settings.distanceFade,
            1f / (1f - f * f)));
        buffer.SetGlobalVector(shadowAtlastSizeId, atlasSizes);
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
        buffer.SetGlobalFloat(shadowPancakingId, 1f);

        for (var i = 0; i < shadowedDirectionalLightCount; i++) 
            RenderDirectionalShadows(i);

        buffer.SetBufferData(
            directionalShadowCascadesBuffer, 
            directionalShadowCascades,
            0, 
            0, 
            settings.directional.cascadeCount);
        
        buffer.SetBufferData(
            directionalShadowMatricesBuffer, 
            directionalShadowMatrices,
            0, 
            0, 
            shadowedDirectionalLightCount * settings.directional.cascadeCount);
        
        buffer.SetKeyword(softCascadeBlendKeyword, settings.directional.softCascadeBlend);
        buffer.EndSample("Directional Shadows");
    }

    private void RenderDirectionalShadows(int index)
    {
        var cascadeCount = settings.directional.cascadeCount;
        var tileOffset = index * cascadeCount;
        var tileScale = 1f / directionalSplit;
        buffer.SetGlobalDepthBias(0f, shadowedDirectionalLights[index].slopeScaleBias);
        
        for (var i = 0; i < cascadeCount; i++)
        {
            RenderInfo info = directionalRenderInfo[index * maxCascades + i];
            
            var tileIndex = tileOffset + i;
            directionalShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
                info.projection * info.view,
                SetTileViewport(tileIndex, directionalSplit, directionalTileSize), 
                tileScale);
            buffer.SetViewProjectionMatrices(info.view, info.projection);
            buffer.DrawRendererList(info.handle);
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
        buffer.SetGlobalFloat(shadowPancakingId, 0f);

        for (var i = 0; i < shadowedOtherLightCount;)
            if (shadowedOtherLights[i].isPoint)
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
            otherShadowData,
            0, 
            0, 
            shadowedOtherLightCount);
        
        buffer.EndSample("Other Shadows");
    }

    private void RenderSpotShadows(int index)
    {
        var light = shadowedOtherLights[index];
        RenderInfo info = otherRenderInfo[index * maxTilesPerLight];
        var texelSize = 2f / (otherTileSize * info.projection.m00);
        var filterSize = texelSize * settings.OtherFilterSize;
        var bias = light.normalBias * filterSize * 1.4142136f;
        var offset = SetTileViewport(index, otherSplit, otherTileSize);
        var tileScale = 1f / otherSplit;
        
        otherShadowData[index] = new OtherShadowData(
            offset, 
            tileScale, 
            bias, 
            atlasSizes.w * 0.5f,
            ConvertToAtlasMatrix(info.projection * info.view, offset, tileScale));

        buffer.SetViewProjectionMatrices(info.view, info.projection);
        buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
        buffer.DrawRendererList(info.handle);
    }

    private void RenderPointShadows(int index)
    {
        var light = shadowedOtherLights[index];
        var texelSize = 2f / otherTileSize;
        var filterSize = texelSize * settings.OtherFilterSize;
        var bias = light.normalBias * filterSize * 1.4142136f;
        var tileScale = 1f / otherSplit;
        buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
        for (var i = 0; i < 6; i++)
        {
            RenderInfo info = otherRenderInfo[index * maxTilesPerLight + i];
            info.view.m11 = -info.view.m11;
            info.view.m12 = -info.view.m12;
            info.view.m13 = -info.view.m13;
            
            var tileIndex = index + i;
            var offset = SetTileViewport(tileIndex, otherSplit, otherTileSize);
            
            otherShadowData[tileIndex] = new OtherShadowData(
                offset, 
                tileScale, 
                bias, 
                atlasSizes.w * 0.5f,
                ConvertToAtlasMatrix(info.projection * info.view, offset, tileScale));

            buffer.SetViewProjectionMatrices(info.view, info.projection);
            buffer.DrawRendererList(info.handle);
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
        public int visibleLightIndex;
        public float slopeScaleBias;
        public float nearPlaneOffset;
    }

    private struct ShadowedOtherLight
    {
        public int visibleLightIndex;
        public float slopeScaleBias;
        public float normalBias;
        public bool isPoint;
    }
}