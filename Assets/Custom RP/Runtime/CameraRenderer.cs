using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public class CameraRenderer 
{
    ScriptableRenderContext context;
    Camera camera;
    const string bufferName = "Render Camera";
    CullingResults cullingResults;
    
    CommandBuffer buffer = new CommandBuffer 
    {
        name = bufferName
    };
    
    static ShaderTagId unlitShaderTagId = new ShaderTagId("SRPDefaultUnlit");
    
    public void Render(ScriptableRenderContext context, Camera camera) 
    {
        this.context = context;
        this.camera = camera;

        if (!Cull()) return;
        
        Setup();
        DrawVisibleGeometry();
        Submit();
    }
    
    // Samples are used for profiling purposes
    void Setup () 
    {
        context.SetupCameraProperties(camera);
        buffer.ClearRenderTarget(true, true, Color.clear);
        buffer.BeginSample(bufferName);
        ExecuteBuffer();
    }
    
    void DrawVisibleGeometry()
    {
        var sortingSettings = new SortingSettings(camera)
        {
            criteria = SortingCriteria.CommonOpaque
        };
        var drawingSettings = new DrawingSettings(unlitShaderTagId, sortingSettings);
        var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
        
        context.DrawRenderers(cullingResults, 
            ref drawingSettings,
            ref filteringSettings);
        
        context.DrawSkybox(camera);

        // code duplication - bad!
        sortingSettings.criteria = SortingCriteria.CommonTransparent;
        drawingSettings.sortingSettings = sortingSettings;
        filteringSettings.renderQueueRange = RenderQueueRange.transparent;
        
        context.DrawRenderers(cullingResults, 
            ref drawingSettings,
            ref filteringSettings);
    }
    
    void Submit() 
    {
        buffer.EndSample(bufferName);
        ExecuteBuffer();
        context.Submit();
    }
    
    void ExecuteBuffer () {
        // commands are copied, but buffer doesn't clear itself
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    bool Cull() 
    {
        if (camera.TryGetCullingParameters(out var cullingParams))
        {
            cullingResults = context.Cull(ref cullingParams);
            return true;
        }

        return false;
    }
    
}