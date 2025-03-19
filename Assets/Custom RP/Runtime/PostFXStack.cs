using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class PostFXStack 
{
    const string bufferName = "Post FX";
    CommandBuffer buffer = new CommandBuffer 
    {
        name = bufferName
    };
    
    enum Pass 
    {
        Copy
    }

    ScriptableRenderContext context;
    Camera camera;
    PostFXSettings settings;
    
    private int fxSourceId = Shader.PropertyToID("_PostFXSource");

    public bool IsActive => settings != null;

#if UNITY_EDITOR
    void ApplySceneViewState () 
    {
        if (camera.cameraType == CameraType.SceneView &&
            !SceneView.currentDrawingSceneView.sceneViewState.showImageEffects) 
        {
            settings = null;
        }
    }
#else
void ApplySceneViewState() {};
#endif
    
    public void Setup (ScriptableRenderContext context, Camera camera, PostFXSettings settings) 
    {
        this.context = context;
        this.camera = camera;
        this.settings = camera.cameraType <= CameraType.SceneView ? settings : null;
        ApplySceneViewState();
    }
    
    public void Render (int sourceId) 
    {
        Draw(sourceId, BuiltinRenderTextureType.CameraTarget, Pass.Copy);
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }
    
    void Draw (RenderTargetIdentifier from, RenderTargetIdentifier to, Pass pass) 
    {
        buffer.SetGlobalTexture(fxSourceId, from);
        buffer.SetRenderTarget(to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        
        buffer.DrawProcedural(
            Matrix4x4.identity, settings.Material, (int)pass,
            MeshTopology.Triangles, 3
        );
    }
    

}