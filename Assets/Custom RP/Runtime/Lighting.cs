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

    private static Vector4[] dirLightColors = new Vector4[maxDirLightCount];
    private static Vector4[] dirLightDirections = new Vector4[maxDirLightCount];
    private static Vector4[] dirLightShadowData = new Vector4[maxDirLightCount];

    private static Vector4[] otherLightColors = new Vector4[maxOtherLightCount];
    private static Vector4[] otherLightPositions = new Vector4[maxOtherLightCount];
    
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
        otherLightPositions[index] = visibleLight.localToWorldMatrix.GetColumn(3);
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
            buffer.SetGlobalVectorArray(
                otherLightPositionsId, otherLightPositions
            );
        }
    }
    
    public void Cleanup() 
    {
        shadows.Cleanup();
    }
    
}