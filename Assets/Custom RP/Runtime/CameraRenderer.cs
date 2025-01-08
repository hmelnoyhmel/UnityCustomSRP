using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public class CameraRenderer 
{
    ScriptableRenderContext context;
    Camera camera;
    const string bufferName = "Render Camera";
    
    CommandBuffer buffer = new CommandBuffer 
    {
        name = bufferName
    };
    
    public void Render(ScriptableRenderContext context, Camera camera) 
    {
        this.context = context;
        this.camera = camera;

        Setup();
        DrawVisibleGeometry();
        Submit();
    }
    
    void Setup () 
    {
        buffer.BeginSample(bufferName);
        context.SetupCameraProperties(camera);
    }
    
    void DrawVisibleGeometry() 
    {
        context.DrawSkybox(camera);
    }
    
    void Submit() 
    {
        buffer.EndSample(bufferName);
        context.Submit();
    }
    

}