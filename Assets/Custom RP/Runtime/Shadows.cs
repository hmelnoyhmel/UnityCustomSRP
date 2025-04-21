using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public partial class Shadows
{
    private const int maxShadowedDirLightCount = 4, maxShadowedOtherLightCount = 16;
    private const int maxCascades = 4;

    private static readonly GlobalKeyword[] directionalFilterKeywords =
    {
        GlobalKeyword.Create("_DIRECTIONAL_PCF3"),
            GlobalKeyword.Create("_DIRECTIONAL_PCF5"),
                GlobalKeyword.Create("_DIRECTIONAL_PCF7")
    };

    private static readonly GlobalKeyword[] otherFilterKeywords =
    {
        GlobalKeyword.Create("_OTHER_PCF3"),
            GlobalKeyword.Create("_OTHER_PCF5"),
                GlobalKeyword.Create("_OTHER_PCF7")
    };

    private static readonly GlobalKeyword[] cascadeBlendKeywords =
    {
        GlobalKeyword.Create("_CASCADE_BLEND_SOFT"),
            GlobalKeyword.Create("_CASCADE_BLEND_DITHER")
    };

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

    private ScriptableRenderContext context;

    private CullingResults cullingResults;

    private ShadowSettings settings;

    private int shadowedDirectionalLightCount, shadowedOtherLightCount;

    private bool useShadowMask;

    private TextureHandle directionalAtlas;
    private TextureHandle otherAtlas;

    private BufferHandle directionalShadowCascadesBuffer;
    private BufferHandle directionalShadowMatricesBuffer;
    private BufferHandle otherShadowDataBuffer;
    
    public void Setup(
        CullingResults cullingResults,
        ShadowSettings settings)
    {
        this.cullingResults = cullingResults;
        this.settings = settings;
        shadowedDirectionalLightCount = shadowedOtherLightCount = 0;
        useShadowMask = false;
    }

    public ShadowResources GetResources(
        RenderGraph renderGraph,
        RenderGraphBuilder builder)
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
        
        return new ShadowResources(
            directionalAtlas, 
            otherAtlas,
            directionalShadowCascadesBuffer,
            directionalShadowMatricesBuffer,
            otherShadowDataBuffer);
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
        this.context = context.renderContext;
        
        if (shadowedDirectionalLightCount > 0)
            RenderDirectionalShadows();
        
        if (shadowedOtherLightCount > 0)
            RenderOtherShadows();
        
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
        ExecuteBuffer();
    }

    private void RenderDirectionalShadows()
    {
        var atlasSize = (int)settings.directional.atlasSize;
        atlasSizes.x = atlasSize;
        atlasSizes.y = 1f / atlasSize;
        buffer.SetRenderTarget(
            directionalAtlas,
            RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        buffer.ClearRenderTarget(true, false, Color.clear);
        buffer.SetGlobalFloat(shadowPancakingId, 1f);
        buffer.BeginSample("Directional Shadows");
        ExecuteBuffer();

        var tiles = shadowedDirectionalLightCount * settings.directional.cascadeCount;
        var split = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
        var tileSize = atlasSize / split;

        for (var i = 0; i < shadowedDirectionalLightCount; i++) RenderDirectionalShadows(i, split, tileSize);

        buffer.SetBufferData(
            directionalShadowCascadesBuffer, directionalShadowCascades,
            0, 0, settings.directional.cascadeCount);
        buffer.SetGlobalBuffer(
            directionalShadowCascadesId, directionalShadowCascadesBuffer);
        buffer.SetBufferData(
            directionalShadowMatricesBuffer, directionalShadowMatrices,
            0, 0, shadowedDirectionalLightCount * settings.directional.cascadeCount);
        buffer.SetGlobalBuffer(
            directionalShadowMatricesId, directionalShadowMatricesBuffer);
        
        SetKeywords(
            directionalFilterKeywords, (int)settings.directional.filter - 1);
        SetKeywords(
            cascadeBlendKeywords, (int)settings.directional.cascadeBlend - 1);
        buffer.EndSample("Directional Shadows");
        ExecuteBuffer();
    }

    private void RenderDirectionalShadows(int index, int split, int tileSize)
    {
        var light = shadowedDirectionalLights[index];
        var shadowSettings = new ShadowDrawingSettings(
            cullingResults, light.visibleLightIndex,
            BatchCullingProjectionType.Orthographic)
        {
            useRenderingLayerMaskTest = true
        };
        
        var cascadeCount = settings.directional.cascadeCount;
        var tileOffset = index * cascadeCount;
        var ratios = settings.directional.CascadeRatios;
        var cullingFactor = Mathf.Max(0f, 0.8f - settings.directional.cascadeFade);
        var tileScale = 1f / split;
        for (var i = 0; i < cascadeCount; i++)
        {
            cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                light.visibleLightIndex, i, cascadeCount, ratios, tileSize,
                light.nearPlaneOffset, out var viewMatrix,
                out var projectionMatrix, out var splitData);
            splitData.shadowCascadeBlendCullingFactor = cullingFactor;
            shadowSettings.splitData = splitData;

            if (index == 0)
            {
                directionalShadowCascades[i] = new DirectionalShadowCascade(
                    splitData.cullingSphere,
                    tileSize, settings.directional.filter);
            }
            
            var tileIndex = tileOffset + i;
            directionalShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
                projectionMatrix * viewMatrix,
                SetTileViewport(tileIndex, split, tileSize), tileScale);
            buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
            ExecuteBuffer();
            context.DrawShadows(ref shadowSettings);
            buffer.SetGlobalDepthBias(0f, 0f);
        }
    }

    private void RenderOtherShadows()
    {
        var atlasSize = (int)settings.other.atlasSize;
        atlasSizes.z = atlasSize;
        atlasSizes.w = 1f / atlasSize;
        buffer.SetRenderTarget(
            otherAtlas,
            RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        buffer.ClearRenderTarget(true, false, Color.clear);
        buffer.SetGlobalFloat(shadowPancakingId, 0f);
        buffer.BeginSample("Other Shadows");
        ExecuteBuffer();

        var tiles = shadowedOtherLightCount;
        var split = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
        var tileSize = atlasSize / split;

        for (var i = 0; i < shadowedOtherLightCount;)
            if (shadowedOtherLights[i].isPoint)
            {
                RenderPointShadows(i, split, tileSize);
                i += 6;
            }
            else
            {
                RenderSpotShadows(i, split, tileSize);
                i += 1;
            }

        buffer.SetBufferData(
            otherShadowDataBuffer, 
            otherShadowData,
            0, 
            0, 
            shadowedOtherLightCount);
        buffer.SetGlobalBuffer(otherShadowDataId, otherShadowDataBuffer);
        
        SetKeywords(otherFilterKeywords, (int)settings.other.filter - 1);
        buffer.EndSample("Other Shadows");
        ExecuteBuffer();
    }

    private void RenderSpotShadows(int index, int split, int tileSize)
    {
        var light = shadowedOtherLights[index];
        var shadowSettings = new ShadowDrawingSettings(
            cullingResults, light.visibleLightIndex,
            BatchCullingProjectionType.Perspective)
        {
            useRenderingLayerMaskTest = true
        };
        cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(
            light.visibleLightIndex, out var viewMatrix,
            out var projectionMatrix, out var splitData);
        shadowSettings.splitData = splitData;
        var texelSize = 2f / (tileSize * projectionMatrix.m00);
        var filterSize = texelSize * ((float)settings.other.filter + 1f);
        var bias = light.normalBias * filterSize * 1.4142136f;
        var offset = SetTileViewport(index, split, tileSize);
        var tileScale = 1f / split;
        
        otherShadowData[index] = new OtherShadowData(
            offset, 
            tileScale, 
            bias, 
            atlasSizes.w * 0.5f,
            ConvertToAtlasMatrix(projectionMatrix * viewMatrix, offset, tileScale));

        buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
        buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
        ExecuteBuffer();
        context.DrawShadows(ref shadowSettings);
        buffer.SetGlobalDepthBias(0f, 0f);
    }

    private void RenderPointShadows(int index, int split, int tileSize)
    {
        var light = shadowedOtherLights[index];
        var shadowSettings = new ShadowDrawingSettings(
            cullingResults, light.visibleLightIndex,
            BatchCullingProjectionType.Perspective)
        {
            useRenderingLayerMaskTest = true
        };
        var texelSize = 2f / tileSize;
        var filterSize = texelSize * ((float)settings.other.filter + 1f);
        var bias = light.normalBias * filterSize * 1.4142136f;
        var tileScale = 1f / split;
        var fovBias = Mathf.Atan(1f + bias + filterSize) * Mathf.Rad2Deg * 2f - 90f;
        for (var i = 0; i < 6; i++)
        {
            cullingResults.ComputePointShadowMatricesAndCullingPrimitives(
                light.visibleLightIndex, (CubemapFace)i, fovBias,
                out var viewMatrix, out var projectionMatrix,
                out var splitData);
            viewMatrix.m11 = -viewMatrix.m11;
            viewMatrix.m12 = -viewMatrix.m12;
            viewMatrix.m13 = -viewMatrix.m13;

            shadowSettings.splitData = splitData;
            var tileIndex = index + i;
            var offset = SetTileViewport(tileIndex, split, tileSize);
            
            otherShadowData[tileIndex] = new OtherShadowData(
                offset, 
                tileScale, 
                bias, 
                atlasSizes.w * 0.5f,
                ConvertToAtlasMatrix(projectionMatrix * viewMatrix, offset, tileScale));

            buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
            ExecuteBuffer();
            context.DrawShadows(ref shadowSettings);
            buffer.SetGlobalDepthBias(0f, 0f);
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

    private void ExecuteBuffer()
    {
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
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