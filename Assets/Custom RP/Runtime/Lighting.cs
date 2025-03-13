using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class Lighting
{
    private const string bufferName = "Lighting";

    private CommandBuffer buffer = new CommandBuffer
    {
        name = bufferName
    };

    private CullingResults cullingResults;

    private const int maxDirLightCount = 4;
    private const int maxOtherLightCount = 64;

    private static int dirLightCountId = Shader.PropertyToID("_DirectionalLightCount");
    private static int dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors");
    private static int dirLightDirectionsId = Shader.PropertyToID("_DirectionalLightDirections");
    private static int dirLightShadowDataId = Shader.PropertyToID("_DirectionalLightShadowData");

    private static int otherLightCountId = Shader.PropertyToID("_OtherLightCount");
    private static int otherLightColorsId = Shader.PropertyToID("_OtherLightColors");
    private static int otherLightPositionsId = Shader.PropertyToID("_OtherLightPositions");
    private static int otherLightDirectionsId = Shader.PropertyToID("_OtherLightDirections");
    private static int otherLightSpotAnglesId = Shader.PropertyToID("_OtherLightSpotAngles");

    private static Vector4[] dirLightColors = new Vector4[maxDirLightCount];
    private static Vector4[] dirLightDirections = new Vector4[maxDirLightCount];
    private static Vector4[] dirLightShadowData = new Vector4[maxDirLightCount];

    private static Vector4[] otherLightColors = new Vector4[maxOtherLightCount];
    private static Vector4[] otherLightPositions = new Vector4[maxOtherLightCount];
    private static Vector4[] otherLightDirections = new Vector4[maxOtherLightCount];
    private static Vector4[] otherLightSpotAngles = new Vector4[maxOtherLightCount];
    
    Shadows shadows = new Shadows();
    
    public void Setup(ScriptableRenderContext context, CullingResults cullingResultsArgs, ShadowSettings shadowSettings)
    {
        this.cullingResults = cullingResultsArgs;
        buffer.BeginSample(bufferName);
        shadows.Setup(context, cullingResults, shadowSettings);
        SetupLights();
        shadows.Render();
        buffer.EndSample(bufferName);
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    void SetupDirectionalLight(int index, in VisibleLight visibleLight)
    {
        dirLightColors[index] = visibleLight.finalColor;
        dirLightDirections[index] = -visibleLight.localToWorldMatrix.GetColumn(2);
        dirLightShadowData[index] = shadows.ReserveDirectionalShadows(visibleLight.light, index);
    }
    
    void SetupPointLight (int index, in VisibleLight visibleLight) 
    {
        otherLightColors[index] = visibleLight.finalColor;
        Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
        position.w = 1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
        otherLightPositions[index] = position;
        otherLightSpotAngles[index] = new Vector4(0f, 1f);
    }
    
    void SetupSpotLight (int index, ref VisibleLight visibleLight) 
    {
        otherLightColors[index] = visibleLight.finalColor;
        Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
        position.w = 1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
        otherLightPositions[index] = position;
        
        otherLightDirections[index] = -visibleLight.localToWorldMatrix.GetColumn(2);
        Light light = visibleLight.light;
        float innerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * light.innerSpotAngle);
        float outerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * visibleLight.spotAngle);
        float angleRangeInv = 1f / Mathf.Max(innerCos - outerCos, 0.001f);
        otherLightSpotAngles[index] = new Vector4(angleRangeInv, -outerCos * angleRangeInv);
    }

    void SetupLights()
    {
        NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
        
        var dirLightCount = 0;
        var otherLightCount = 0;
        for (int i = 0; i < visibleLights.Length; i++) 
        {
            VisibleLight visibleLight = visibleLights[i];
            switch (visibleLight.lightType) 
            {
                case LightType.Directional:
                    if (dirLightCount < maxDirLightCount) 
                    {
                        SetupDirectionalLight(dirLightCount++, visibleLight);
                    }
                    break;
                case LightType.Point:
                    if (otherLightCount < maxOtherLightCount) 
                    {
                        SetupPointLight(otherLightCount++, visibleLight);
                    }
                    break;
                case LightType.Spot:
                    if (otherLightCount < maxOtherLightCount) 
                    {
                        SetupSpotLight(otherLightCount++, ref visibleLight);
                    }
                    break;
            }
        }
        
        buffer.SetGlobalInt(dirLightCountId, dirLightCount);
        if (dirLightCount > 0)
        {
            buffer.SetGlobalVectorArray(dirLightColorsId, dirLightColors);
            buffer.SetGlobalVectorArray(dirLightDirectionsId, dirLightDirections);
            buffer.SetGlobalVectorArray(dirLightShadowDataId, dirLightShadowData);
        }
        
        buffer.SetGlobalInt(otherLightCountId, otherLightCount);
        if (otherLightCount > 0) 
        {
            buffer.SetGlobalVectorArray(otherLightColorsId, otherLightColors);
            buffer.SetGlobalVectorArray(otherLightPositionsId, otherLightPositions);
            buffer.SetGlobalVectorArray(otherLightDirectionsId, otherLightDirections);
            buffer.SetGlobalVectorArray(otherLightSpotAnglesId, otherLightSpotAngles);
        }
    }
    
    public void Cleanup() 
    {
        shadows.Cleanup();
    }
    
}