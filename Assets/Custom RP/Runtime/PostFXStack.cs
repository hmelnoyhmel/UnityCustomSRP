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
        BloomPrefilter,
        BloomCombine,
        BloomVertical,
        BloomHorizontal,
        Copy
    }

    ScriptableRenderContext context;
    Camera camera;
    PostFXSettings settings;
    
    private const int maxBloomPyramidLevels = 16;
    private int bloomPyramidId;
    
    private int bloomBucibicUpsamplingId = Shader.PropertyToID("_BloomBicubicUpsampling");
    private int bloomIntensityId = Shader.PropertyToID("_BloomIntensity");
    private int bloomPrefilterId = Shader.PropertyToID("_BloomPrefilter");
    private int bloomThresholdId = Shader.PropertyToID("_BloomThreshold"); 
    private int fxSourceId = Shader.PropertyToID("_PostFXSource");
    private int fxSource2Id = Shader.PropertyToID("_PostFXSource2");
    
    
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
    
    public PostFXStack () 
    {
        bloomPyramidId = Shader.PropertyToID("_BloomPyramid0");
        
        for (var i = 1; i < maxBloomPyramidLevels * 2; i++)
            Shader.PropertyToID("_BloomPyramid" + i);
    }
    
    public void Setup (ScriptableRenderContext context, Camera camera, PostFXSettings settings) 
    {
        this.context = context;
        this.camera = camera;
        this.settings = camera.cameraType <= CameraType.SceneView ? settings : null;
        ApplySceneViewState();
    }
    
    public void Render (int sourceId) 
    {
        DoBloom(sourceId);
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
    
    void DoBloom (int sourceId) 
    {
        buffer.BeginSample("Bloom");

        PostFXSettings.BloomSettings bloom = settings.Bloom;
        
        var width = camera.pixelWidth / 2; 
        var height = camera.pixelHeight / 2;
        
        if (
            bloom.maxIterations == 0 ||
            bloom.intensity <= 0f ||
            height < bloom.downscaleLimit * 2 || 
            width < bloom.downscaleLimit * 2) 
        {
            Draw(sourceId, BuiltinRenderTextureType.CameraTarget, Pass.Copy);
            buffer.EndSample("Bloom");
            return;
        }
        
        Vector4 threshold;
        threshold.x = Mathf.GammaToLinearSpace(bloom.threshold);
        threshold.y = threshold.x * bloom.thresholdKnee;
        threshold.z = 2f * threshold.y;
        threshold.w = 0.25f / (threshold.y + 0.00001f);
        threshold.y -= threshold.x;
        buffer.SetGlobalVector(bloomThresholdId, threshold);
        
        var format = RenderTextureFormat.Default;
        buffer.GetTemporaryRT(bloomPrefilterId, width, height, 0, FilterMode.Bilinear, format);
        Draw(sourceId, bloomPrefilterId, Pass.BloomPrefilter);
        width /= 2;
        height /= 2;
        
        var fromId = bloomPrefilterId; 
        var toId = bloomPyramidId + 1;
        
        
        int i;
        for (i = 0; i < bloom.maxIterations; i++) 
        {
            if (height < bloom.downscaleLimit || width < bloom.downscaleLimit) 
            {
                break;
            }
            
            int midId = toId - 1;
            buffer.GetTemporaryRT(
                midId, width, height,
                0, FilterMode.Bilinear, format);
            
            buffer.GetTemporaryRT(
                toId, width, height, 
                0, FilterMode.Bilinear, format);
            
            Draw(fromId, midId, Pass.BloomHorizontal);
            Draw(midId, toId, Pass.BloomVertical);
            
            fromId = toId;
            toId += 2;
            width /= 2;
            height /= 2;
        }
        
        buffer.ReleaseTemporaryRT(bloomPrefilterId);

        buffer.SetGlobalFloat(
            bloomBucibicUpsamplingId, bloom.bicubicUpsampling ? 1f : 0f
        );
        
        buffer.SetGlobalFloat(bloomIntensityId, 1f);
        
        if (i > 1)
        {
            buffer.ReleaseTemporaryRT(fromId - 1);
            toId -= 5;

            for (i -= 1; i > 0; i--)
            {
                buffer.SetGlobalTexture(fxSource2Id, toId + 1);
                Draw(fromId, toId, Pass.BloomCombine);
                buffer.ReleaseTemporaryRT(fromId);
                buffer.ReleaseTemporaryRT(toId + 1);
                fromId -= toId;
                toId -= 2;
            }
            
            buffer.SetGlobalTexture(fxSource2Id, sourceId);
            Draw(bloomPyramidId, BuiltinRenderTextureType.CameraTarget, Pass.BloomCombine);
            buffer.ReleaseTemporaryRT(fromId);
            
            buffer.EndSample("Bloom");
        }
        else 
        {
            buffer.ReleaseTemporaryRT(bloomPyramidId);
        }
        
        buffer.SetGlobalFloat(bloomIntensityId, bloom.intensity);
        
    }

}