using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class CustomRenderPipeline : RenderPipeline
{
    CameraRenderer rendererTest = new CameraRenderer();
    
    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
    }
    
    protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
        foreach (var camera in cameras)
        {
            rendererTest.Render(context, camera);
        }
    }
}
