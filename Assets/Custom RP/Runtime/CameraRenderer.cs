using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public class CameraRenderer 
{
    ScriptableRenderContext context;
    Camera camera;

    public void Render(ScriptableRenderContext context, Camera camera) 
    {
        this.context = context;
        this.camera = camera;

        Setup();
        DrawVisibleGeometry();
        Submit();
    }
    
    void DrawVisibleGeometry() 
    {
        context.DrawSkybox(camera);
    }
    
    void Submit() 
    {
        context.Submit();
    }
    
    void Setup () {
        context.SetupCameraProperties(camera);
    }
}