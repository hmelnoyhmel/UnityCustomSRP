using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Color = System.Drawing.Color;

public class CameraRenderer 
{
    ScriptableRenderContext renderContext;
    Camera activeCamera;
    const string bufferName = "Render Camera";
    CullingResults cullingResults;
    
    CommandBuffer buffer = new CommandBuffer 
    {
        name = bufferName
    };
    
    Lighting lighting = new Lighting();
    PostFXStack postFXStack = new PostFXStack();
    
    static int frameBufferId = Shader.PropertyToID("_CameraFrameBuffer");
    
    bool useHDR;
    
    public void Render(ScriptableRenderContext context, Camera camera, bool allowHDR,
        bool useDynamicBatching, bool useGPUInstancing, bool useLightsPerObject, 
        ShadowSettings shadowSettings, PostFXSettings postFXSettings, int colorLUTResolution) 
    {
        renderContext = context;
        activeCamera = camera;

        RenderUtils.PrepareBuffer(buffer, activeCamera);
        RenderUtils.PrepareForSceneWindow(activeCamera);
        if (!Cull(shadowSettings.maxDistance)) return;
        
        useHDR = allowHDR && camera.allowHDR;
        
        // lighting & shadows
        buffer.BeginSample(RenderUtils.SampleName);
        ExecuteBuffer();
        
        lighting.Setup(context, cullingResults, shadowSettings, useLightsPerObject); 
        postFXStack.Setup(context, camera, postFXSettings, useHDR, colorLUTResolution);
        
        buffer.EndSample(RenderUtils.SampleName);
        
        Setup();
        DrawVisibleGeometry(useDynamicBatching, useGPUInstancing, useLightsPerObject);
        RenderUtils.DrawUnsupportedShaders(renderContext, activeCamera, cullingResults);
        
        RenderUtils.DrawGizmosBeforeFX(renderContext, activeCamera);
        
        if (postFXStack.IsActive) 
            postFXStack.Render(frameBufferId);
        
        RenderUtils.DrawGizmosAfterFX(renderContext, activeCamera);
        
        Cleanup();
        Submit();
    }
    
    void Setup () 
    {
        renderContext.SetupCameraProperties(activeCamera);
        
        // clear flags are set in the editor
        // inside camera component
        var flags = activeCamera.clearFlags;
        
        if (postFXStack.IsActive) 
        {
            if (flags > CameraClearFlags.Color) 
                flags = CameraClearFlags.Color;
            
            buffer.GetTemporaryRT(
                frameBufferId, activeCamera.pixelWidth, activeCamera.pixelHeight,
                32, FilterMode.Bilinear, 
                useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default
            );
            
            buffer.SetRenderTarget(
                frameBufferId,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
            );
        }
        
        buffer.ClearRenderTarget(
            flags <= CameraClearFlags.Depth, 
            flags <= CameraClearFlags.Color, 
            flags == CameraClearFlags.Color ? activeCamera.backgroundColor.linear : UnityEngine.Color.clear);
        
        buffer.BeginSample(RenderUtils.SampleName); 
        ExecuteBuffer();
    }
    
    void DrawVisibleGeometry(bool useDynamicBatching, bool useGPUInstancing, bool useLightsPerObject)
    {
        PerObjectData lightsPerObjectFlags = useLightsPerObject ?
            PerObjectData.LightData | PerObjectData.LightIndices :
            PerObjectData.None;
        
        var sortingSettings = new SortingSettings(activeCamera)
        {
            criteria = SortingCriteria.CommonOpaque
        };
        var drawingSettings = new DrawingSettings(RenderUtils.UnlitShaderTagId, sortingSettings)
        {
            enableDynamicBatching = useDynamicBatching,
            enableInstancing = useGPUInstancing,
            perObjectData = 
                PerObjectData.ReflectionProbes |
                PerObjectData.Lightmaps | 
                PerObjectData.ShadowMask | 
                PerObjectData.LightProbe | 
                PerObjectData.OcclusionProbe |
                PerObjectData.LightProbeProxyVolume |
                PerObjectData.OcclusionProbeProxyVolume |
                lightsPerObjectFlags
        };
        drawingSettings.SetShaderPassName(1, RenderUtils.LitShaderTagId);
        
        var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
        
        renderContext.DrawRenderers(cullingResults, 
            ref drawingSettings,
            ref filteringSettings);
        
        renderContext.DrawSkybox(activeCamera);
        
        sortingSettings.criteria = SortingCriteria.CommonTransparent;
        drawingSettings.sortingSettings = sortingSettings;
        filteringSettings.renderQueueRange = RenderQueueRange.transparent;
        
        renderContext.DrawRenderers(cullingResults, 
            ref drawingSettings,
            ref filteringSettings);
    }
    
    void Submit() 
    {
        buffer.EndSample(RenderUtils.SampleName);
        ExecuteBuffer();
        renderContext.Submit();
    }
    
    void ExecuteBuffer () 
    {
        renderContext.ExecuteCommandBuffer(buffer);
        
        // commands are copied, but buffer doesn't clear itself, have to do it manually
        buffer.Clear();
    }

    bool Cull(float maxShadowDistance) 
    {
        if (activeCamera.TryGetCullingParameters(out var cullingParams))
        {
            cullingParams.shadowDistance = Mathf.Min(maxShadowDistance, activeCamera.farClipPlane);
            cullingResults = renderContext.Cull(ref cullingParams);
            return true;
        }

        return false;
    }
    
    void Cleanup () 
    {
        lighting.Cleanup();
        
        if (postFXStack.IsActive) 
            buffer.ReleaseTemporaryRT(frameBufferId);
    }
}