using UnityEngine;
using UnityEngine.Rendering;

public class Shadows
{
    private const string bufferName = "Shadows";
    private const int maxShadowedDirectionalLightCount = 4;
    private const int maxCascades = 4;
    
    private CommandBuffer buffer = new CommandBuffer
    {
        name = bufferName
    };
    
    private ScriptableRenderContext context;
    private CullingResults cullingResults;
    private ShadowSettings settings;

    private int ShadowedDirectionalLightCount;

    private static int dirShadowAtlasId = Shader.PropertyToID("_DirectionalShadowAtlas");
    private static int dirShadowMatricesId = Shader.PropertyToID("_DirectionalShadowMatrices");
    private static int cascadeCountId = Shader.PropertyToID("_CascadeCount");
    private static int cascadeCullingSpheresId = Shader.PropertyToID("_CascadeCullingSpheres");
    private static int cascadeDataId = Shader.PropertyToID("_CascadeData");
    private static int shadowAtlasSizeId = Shader.PropertyToID("_ShadowAtlasSize");
    private static int shadowDistanceFadeId = Shader.PropertyToID("_ShadowDistanceFade");

    private static Matrix4x4[] dirShadowMatrices = new Matrix4x4[maxShadowedDirectionalLightCount * maxCascades];
    private static Vector4[] cascadeCullingSpheres = new Vector4[maxCascades];
    private static Vector4[] cascadeData = new Vector4[maxCascades];
    
    private static string[] directionalFilterKeywords = 
    {
        "_DIRECTIONAL_PCF3",
        "_DIRECTIONAL_PCF5",
        "_DIRECTIONAL_PCF7",
    };

    private static string[] cascadeBlendKeywords =
    {
        "_CASCADE_BLEND_SOFT",
        "_CASCADE_BLEND_DITHER"
    };
    
    private static string[] shadowMaskKeywords = 
    {
        "_SHADOW_MASK_ALWAYS",
        "_SHADOW_MASK_DISTANCE"
    };
    
    bool useShadowMask;
    
    public void Setup(ScriptableRenderContext context, CullingResults cullingResults, ShadowSettings settings) 
    {
        this.context = context;
        this.cullingResults = cullingResults;
        this.settings = settings;
        ShadowedDirectionalLightCount = 0;
        useShadowMask = false;
    }
    
    void ExecuteBuffer()
    {
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }
    
    struct ShadowedDirectionalLight
    {
        public int visibleLightIndex;
        public float slopeScaleBias;
        public float nearPlaneOffset;
    }

    private ShadowedDirectionalLight[] ShadowedDirectionalLights =
        new ShadowedDirectionalLight[maxShadowedDirectionalLightCount];

    
    // Reserve space in the shadow atlas for the light's shadow map
    // and store the information needed to render them
    public Vector4 ReserveDirectionalShadows(Light light, int visibleLightIndex)
    {
        if (ShadowedDirectionalLightCount >= maxShadowedDirectionalLightCount) return new Vector4(0f, 0f, 0f, -1f);
        if (light.shadows == LightShadows.None) return new Vector4(0f, 0f, 0f, -1f);
        if (light.shadowStrength <= 0f) return new Vector4(0f, 0f, 0f, -1f);
        
        float maskChannel = -1;
        LightBakingOutput lightBaking = light.bakingOutput;
        
        if (lightBaking.lightmapBakeType == LightmapBakeType.Mixed &&
            lightBaking.mixedLightingMode == MixedLightingMode.Shadowmask) 
        {
            useShadowMask = true;
            maskChannel = lightBaking.occlusionMaskChannel;
        }
        
        if (!cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds b)) 
        {
            return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);
        }
        
        ShadowedDirectionalLights[ShadowedDirectionalLightCount] =
            new ShadowedDirectionalLight
            {
                visibleLightIndex = visibleLightIndex,
                slopeScaleBias = light.shadowBias,
                nearPlaneOffset = light.shadowNearPlane
            };
            
        return new Vector4(
            light.shadowStrength, 
            settings.directional.cascadeCount * ShadowedDirectionalLightCount++,
            light.shadowNormalBias, 
            maskChannel);
        
    }
    
    public Vector4 ReserveOtherShadows (Light light, int visibleLightIndex) 
    {
        if (light.shadows == LightShadows.None) return new Vector4(0f, 0f, 0f, -1f);
        if (light.shadowStrength <= 0f)  return new Vector4(0f, 0f, 0f, -1f);
        
        LightBakingOutput lightBaking = light.bakingOutput;
            
        if (lightBaking.lightmapBakeType != LightmapBakeType.Mixed) return new Vector4(0f, 0f, 0f, -1f);
        if (lightBaking.mixedLightingMode != MixedLightingMode.Shadowmask) return new Vector4(0f, 0f, 0f, -1f);
            
        useShadowMask = true;
        return new Vector4(light.shadowStrength, 0f, 0f, lightBaking.occlusionMaskChannel);
        
    }
    
    public void Render() 
    {
        if (ShadowedDirectionalLightCount > 0) 
        {
            RenderDirectionalShadows();
        }
        else 
        {
            buffer.GetTemporaryRT(dirShadowAtlasId, 1, 1,
                32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap);
        }
        buffer.BeginSample(bufferName);
        
        // disgusting 
        SetKeywords(shadowMaskKeywords, useShadowMask ? QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask ? 0 : 1 : -1);
        
        buffer.EndSample(bufferName);
        ExecuteBuffer();
    }

    private void RenderDirectionalShadows()
    {
        var atlasSize = (int)settings.directional.atlasSize;
        buffer.GetTemporaryRT(dirShadowAtlasId, atlasSize, atlasSize,
            32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap);

        buffer.SetRenderTarget(dirShadowAtlasId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        buffer.ClearRenderTarget(true, false, Color.clear);
        buffer.BeginSample(bufferName);
        
        var tiles = ShadowedDirectionalLightCount * settings.directional.cascadeCount;
        var split = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
        var tileSize = atlasSize / split;
        
        for (var i = 0; i < ShadowedDirectionalLightCount; i++)
            RenderDirectionalShadows(i, split, tileSize);
        
        
        buffer.SetGlobalInt(cascadeCountId, settings.directional.cascadeCount);
        buffer.SetGlobalVectorArray(cascadeCullingSpheresId, cascadeCullingSpheres);
        buffer.SetGlobalVectorArray(cascadeDataId, cascadeData);
        buffer.SetGlobalMatrixArray(dirShadowMatricesId, dirShadowMatrices);
        
        float f = 1f - settings.directional.cascadeFade;
        buffer.SetGlobalVector(
            shadowDistanceFadeId,
            new Vector4(
                1f / settings.maxDistance, 
                1f / settings.distanceFade,
                1f / (1f - f * f)
                ));
        
        SetKeywords(directionalFilterKeywords, (int)settings.directional.filter - 1);
        SetKeywords(cascadeBlendKeywords, (int)settings.directional.cascadeBlend - 1);
        
        buffer.SetGlobalVector(shadowAtlasSizeId, new Vector4(atlasSize, 1f / atlasSize));
        
        buffer.EndSample(bufferName);
        ExecuteBuffer();
    }

    void RenderDirectionalShadows(int index, int split, int tileSize)
    {
        ShadowedDirectionalLight light = ShadowedDirectionalLights[index];
        var shadowSettings = new ShadowDrawingSettings(cullingResults, light.visibleLightIndex);
        var cascadeCount = settings.directional.cascadeCount;
        var tileOffset = index * cascadeCount;
        var ratios = settings.directional.CascadeRatios;

        float cullingFactor = Mathf.Max(0f, 0.8f - settings.directional.cascadeFade);
        
        for (int i = 0; i < cascadeCount; i++)
        {
            cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                light.visibleLightIndex,
                i, cascadeCount, ratios, tileSize, light.nearPlaneOffset,
                out Matrix4x4 viewMatrix,
                out Matrix4x4 projectionMatrix,
                out ShadowSplitData splitData);
            
            splitData.shadowCascadeBlendCullingFactor = cullingFactor;
            shadowSettings.splitData = splitData;
            if (index == 0)
            {
                SetCascadeData(i, splitData.cullingSphere, tileSize);
            }

            var tileIndex = tileOffset + i;
            dirShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
                projectionMatrix * viewMatrix,
                SetTileViewport(tileIndex, split, tileSize), split
            );

            buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            
            buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
            ExecuteBuffer();
            context.DrawShadows(ref shadowSettings);
            buffer.SetGlobalDepthBias(0f, 0f);
        }
    }
    
    public void Cleanup() 
    {
        if (ShadowedDirectionalLightCount > 0)
        {
            buffer.ReleaseTemporaryRT(dirShadowAtlasId);
            ExecuteBuffer();
        }
    }
    
    Vector2 SetTileViewport(int index, int split, float tileSize) 
    {
        Vector2 offset = new Vector2(index % split, index / split);
        buffer.SetViewport(
            new Rect(offset.x * tileSize, offset.y * tileSize, tileSize, tileSize));

        return offset;
    }
    
    Matrix4x4 ConvertToAtlasMatrix(Matrix4x4 matrix, Vector2 offset, int split) 
    {
        if (SystemInfo.usesReversedZBuffer) 
        {
            matrix.m20 = -matrix.m20;
            matrix.m21 = -matrix.m21;
            matrix.m22 = -matrix.m22;
            matrix.m23 = -matrix.m23;
        }
        
        var scale = 1.0f / split;
        matrix.m00 = (0.5f * (matrix.m00 + matrix.m30) + offset.x * matrix.m30) * scale;
        matrix.m01 = (0.5f * (matrix.m01 + matrix.m31) + offset.x * matrix.m31) * scale;
        matrix.m02 = (0.5f * (matrix.m02 + matrix.m32) + offset.x * matrix.m32) * scale;
        matrix.m03 = (0.5f * (matrix.m03 + matrix.m33) + offset.x * matrix.m33) * scale;
        matrix.m10 = (0.5f * (matrix.m10 + matrix.m30) + offset.y * matrix.m30) * scale;
        matrix.m11 = (0.5f * (matrix.m11 + matrix.m31) + offset.y * matrix.m31) * scale;
        matrix.m12 = (0.5f * (matrix.m12 + matrix.m32) + offset.y * matrix.m32) * scale;
        matrix.m13 = (0.5f * (matrix.m13 + matrix.m33) + offset.y * matrix.m33) * scale;
        
        matrix.m20 = 0.5f * (matrix.m20 + matrix.m30);
        matrix.m21 = 0.5f * (matrix.m21 + matrix.m31);
        matrix.m22 = 0.5f * (matrix.m22 + matrix.m32);
        matrix.m23 = 0.5f * (matrix.m23 + matrix.m33);
        
        
        return matrix;
    }

    void SetCascadeData(int index, Vector4 cullingSphere, float tileSize)
    {
        float texelSize = 2f * cullingSphere.w / tileSize;
        float filterSize = texelSize * ((float)settings.directional.filter + 1f);
        cullingSphere.w -= filterSize;
        cullingSphere.w *= cullingSphere.w;
        cascadeCullingSpheres[index] = cullingSphere;
        cascadeData[index] = new Vector4(1f / cullingSphere.w, filterSize * 1.4142136f);
        
    }
    
    void SetKeywords (string[] keywords, int enabledIndex)
    {
        for (int i = 0; i < keywords.Length; i++) {
            if (i == enabledIndex) {
                buffer.EnableShaderKeyword(keywords[i]);
            }
            else {
                buffer.DisableShaderKeyword(keywords[i]);
            }
        }
    }
    
}