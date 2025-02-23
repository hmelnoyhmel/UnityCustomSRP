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
    
    public void Render(ScriptableRenderContext context, Camera camera, bool useDynamicBatching, bool useGPUInstancing, ShadowSettings shadowSettings) 
    {
        renderContext = context;
        activeCamera = camera;

        RenderUtils.PrepareBuffer(buffer, activeCamera);
        RenderUtils.PrepareForSceneWindow(activeCamera);
        if (!Cull(shadowSettings.maxDistance)) return;
        
        // lighting & shadows
        buffer.BeginSample(RenderUtils.SampleName);
        ExecuteBuffer();
        lighting.Setup(context, cullingResults, shadowSettings);
        buffer.EndSample(RenderUtils.SampleName);
        
        Setup();
        DrawVisibleGeometry(useDynamicBatching, useGPUInstancing);
        RenderUtils.DrawUnsupportedShaders(renderContext, activeCamera, cullingResults);
        RenderUtils.DrawGizmos(renderContext, activeCamera);
        lighting.Cleanup();
        Submit();
    }
    
    void Setup () 
    {
        renderContext.SetupCameraProperties(activeCamera);
        
        // viewport rect adjust cam render position
        // tweak the numbers if experiencing artifacts
        
        // clear flags are set in the editor
        // inside camera component
        var flags = activeCamera.clearFlags;
        buffer.ClearRenderTarget(
            flags <= CameraClearFlags.Depth, 
            flags <= CameraClearFlags.Color, 
            flags == CameraClearFlags.Color ? activeCamera.backgroundColor.linear : UnityEngine.Color.clear);
        
        
        
        buffer.BeginSample(RenderUtils.SampleName); // Samples are used for profiling purposes
        ExecuteBuffer();
    }
    
    void DrawVisibleGeometry(bool useDynamicBatching, bool useGPUInstancing)
    {
        var sortingSettings = new SortingSettings(activeCamera)
        {
            criteria = SortingCriteria.CommonOpaque
        };
        var drawingSettings = new DrawingSettings(RenderUtils.UnlitShaderTagId, sortingSettings)
        {
            enableDynamicBatching = useDynamicBatching,
            enableInstancing = useGPUInstancing,
            perObjectData = 
                PerObjectData.Lightmaps | 
                PerObjectData.ShadowMask | 
                PerObjectData.LightProbe | 
                PerObjectData.OcclusionProbe |
                PerObjectData.LightProbeProxyVolume |
                PerObjectData.OcclusionProbeProxyVolume
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
    
}