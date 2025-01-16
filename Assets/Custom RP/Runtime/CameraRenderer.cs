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
    
    public void Render(ScriptableRenderContext context, Camera camera, bool useDynamicBatching, bool useGPUInstancing) 
    {
        renderContext = context;
        activeCamera = camera;

        RenderUtils.PrepareBuffer(buffer, activeCamera);
        RenderUtils.PrepareForSceneWindow(activeCamera);
        if (!Cull()) return;

        Setup();
        lighting.Setup(context, cullingResults);
        DrawVisibleGeometry(useDynamicBatching, useGPUInstancing);
        RenderUtils.DrawUnsupportedShaders(renderContext, activeCamera, cullingResults);
        RenderUtils.DrawGizmos(renderContext, activeCamera);
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
            enableInstancing = useGPUInstancing
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

    bool Cull() 
    {
        if (activeCamera.TryGetCullingParameters(out var cullingParams))
        {
            cullingResults = renderContext.Cull(ref cullingParams);
            return true;
        }

        return false;
    }
    
}