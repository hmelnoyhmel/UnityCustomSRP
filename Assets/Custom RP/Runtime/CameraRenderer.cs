using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Color = UnityEngine.Color;

public class CameraRenderer 
{
    ScriptableRenderContext renderContext;
    Camera activeCamera;
    const string bufferName = "Render Camera";
    CullingResults cullingResults;
    Material material;
    Texture2D missingTexture;
    
    CommandBuffer buffer = new CommandBuffer 
    {
        name = bufferName
    };
    
    Lighting lighting = new Lighting();
    PostFXStack postFXStack = new PostFXStack();

    private static int colorAttachmentId = Shader.PropertyToID("_CameraColorAttachment");
    private static int depthAttachmentId = Shader.PropertyToID("_CameraDepthAttachment");
    private static int colorTextureId = Shader.PropertyToID("_CameraColorTexture");
    private static int depthTextureId = Shader.PropertyToID("_CameraDepthTexture");
    private static int sourceTextureId = Shader.PropertyToID("_SourceTexture");
    
    bool useHDR;
    bool useColorTexture;
    bool useDepthTexture;
    bool useIntermediateBuffer;
    
    static bool copyTextureSupported = SystemInfo.copyTextureSupport > CopyTextureSupport.None;
    
    public CameraRenderer (Shader shader) 
    {
        material = CoreUtils.CreateEngineMaterial(shader);
        missingTexture = new Texture2D(1, 1) 
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "Missing"
        };
        missingTexture.SetPixel(0, 0, Color.white * 0.5f);
        missingTexture.Apply(true, true);
    }
    
    public void Dispose () 
    {
        CoreUtils.Destroy(material);
        CoreUtils.Destroy(missingTexture);
    }
    
    public void Render(ScriptableRenderContext context, Camera camera, CameraBufferSettings bufferSettings,
        bool useDynamicBatching, bool useGPUInstancing, bool useLightsPerObject, 
        ShadowSettings shadowSettings, PostFXSettings postFXSettings, int colorLUTResolution) 
    {
        renderContext = context;
        activeCamera = camera;

        RenderUtils.PrepareBuffer(buffer, activeCamera);
        RenderUtils.PrepareForSceneWindow(activeCamera);
        if (!Cull(shadowSettings.maxDistance)) return;


        if (camera.cameraType == CameraType.Reflection)
        {
            useColorTexture = bufferSettings.copyColorReflection;
            useDepthTexture = bufferSettings.copyDepthReflection;
        }
        else
        {
            useColorTexture = bufferSettings.copyColor;
            useDepthTexture = bufferSettings.copyDepth;
        }

        useHDR = bufferSettings.allowHDR && camera.allowHDR;
        
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
        {
            postFXStack.Render(colorAttachmentId);
        }
        else if (useIntermediateBuffer) 
        {
            Draw(colorAttachmentId, BuiltinRenderTextureType.CameraTarget);
            ExecuteBuffer();
        }
        
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
        
        useIntermediateBuffer = useColorTexture || useDepthTexture || postFXStack.IsActive;
        if (useIntermediateBuffer) 
        {
            if (flags > CameraClearFlags.Color) 
                flags = CameraClearFlags.Color;
            
            buffer.GetTemporaryRT(
                colorAttachmentId, activeCamera.pixelWidth, activeCamera.pixelHeight,
                0, FilterMode.Bilinear, 
                useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default
            );
            buffer.GetTemporaryRT(
                depthAttachmentId, activeCamera.pixelWidth, activeCamera.pixelHeight,
                32, FilterMode.Point, RenderTextureFormat.Depth
            );
            
            buffer.SetRenderTarget(
                colorAttachmentId,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                depthAttachmentId,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
            );
        }
        
        buffer.ClearRenderTarget(
            flags <= CameraClearFlags.Depth, 
            flags <= CameraClearFlags.Color, 
            flags == CameraClearFlags.Color ? activeCamera.backgroundColor.linear : UnityEngine.Color.clear);
        
        buffer.BeginSample(RenderUtils.SampleName); 
        buffer.SetGlobalTexture(colorTextureId, missingTexture);
        buffer.SetGlobalTexture(depthTextureId, missingTexture);
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
        
        if (useColorTexture || useDepthTexture) 
            CopyAttachments();
        
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

        if (useIntermediateBuffer)
        {
            buffer.ReleaseTemporaryRT(colorAttachmentId);
            buffer.ReleaseTemporaryRT(depthAttachmentId);
            
            if (useColorTexture)
                buffer.ReleaseTemporaryRT(colorTextureId);

            if (useDepthTexture) 
                buffer.ReleaseTemporaryRT(depthTextureId);
        }
    }
    
    void CopyAttachments () 
    {
        if (useColorTexture) 
        {
            buffer.GetTemporaryRT(colorTextureId, activeCamera.pixelWidth, activeCamera.pixelHeight,
                0, FilterMode.Bilinear, 
                useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default);
            
            if (copyTextureSupported) 
            {
                buffer.CopyTexture(colorAttachmentId, colorTextureId);
            }
            else 
            {
                Draw(colorAttachmentId, colorTextureId);
            }
        }
        
        if (useDepthTexture) 
        {
            buffer.GetTemporaryRT(depthTextureId, activeCamera.pixelWidth, activeCamera.pixelHeight,
                32, FilterMode.Point, RenderTextureFormat.Depth);

            if (copyTextureSupported)
            {
                buffer.CopyTexture(depthAttachmentId, depthTextureId);
            }
            else 
            {
                Draw(depthAttachmentId, depthTextureId, true);
                
            }
            
        }
        
        if (!copyTextureSupported) 
        {
            buffer.SetRenderTarget(
                colorAttachmentId,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                depthAttachmentId,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store
            );
        }
        
        ExecuteBuffer();
    }
    
    void Draw (RenderTargetIdentifier from, RenderTargetIdentifier to, bool isDepth = false) 
    {
        buffer.SetGlobalTexture(sourceTextureId, from);
        buffer.SetRenderTarget(to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        buffer.DrawProcedural(Matrix4x4.identity, material, isDepth ? 1 : 0, MeshTopology.Triangles, 3);
    }
    
}