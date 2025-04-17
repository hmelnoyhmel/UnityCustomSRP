using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public partial class CameraRenderer
{
    public const float renderScaleMin = 0.1f, renderScaleMax = 2f;

    private static readonly ShaderTagId
        unlitShaderTagId = new("SRPDefaultUnlit"),
        litShaderTagId = new("CustomLit");

    public static readonly int
        bufferSizeId = Shader.PropertyToID("_CameraBufferSize"),
        colorAttachmentId = Shader.PropertyToID("_CameraColorAttachment"),
        depthAttachmentId = Shader.PropertyToID("_CameraDepthAttachment"),
        colorTextureId = Shader.PropertyToID("_CameraColorTexture"),
        depthTextureId = Shader.PropertyToID("_CameraDepthTexture"),
        sourceTextureId = Shader.PropertyToID("_SourceTexture"),
        srcBlendId = Shader.PropertyToID("_CameraSrcBlend"),
        dstBlendId = Shader.PropertyToID("_CameraDstBlend");

    private static readonly CameraSettings defaultCameraSettings = new();

    private static readonly bool copyTextureSupported =
        SystemInfo.copyTextureSupport > CopyTextureSupport.None;

    private static readonly Rect fullViewRect = new(0f, 0f, 1f, 1f);
    
    CommandBuffer buffer;

    private readonly Lighting lighting = new();

    private readonly Material material;

    private readonly Texture2D missingTexture;

    private readonly PostFXStack postFXStack = new();

    private Vector2Int bufferSize;

    public Camera camera;

    private ScriptableRenderContext context;

    private CullingResults cullingResults;

    public bool useColorTexture, useDepthTexture, useIntermediateBuffer;

    private bool useHDR, useScaledRendering;

    public CameraRenderer(Shader shader)
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

    public void Dispose()
    {
        CoreUtils.Destroy(material);
        CoreUtils.Destroy(missingTexture);
    }

    public void Render(
        RenderGraph renderGraph, ScriptableRenderContext context, Camera camera,
        CameraBufferSettings bufferSettings,
        bool useLightsPerObject, ShadowSettings shadowSettings, PostFXSettings postFXSettings,
        int colorLUTResolution)
    {
        this.context = context;
        this.camera = camera;

        ProfilingSampler cameraSampler;
        CameraSettings cameraSettings;
        if (camera.TryGetComponent(out CustomRenderPipelineCamera crpCamera))
        {
            cameraSampler = crpCamera.Sampler;
            cameraSettings = crpCamera.Settings;
        }
        else
        {
            cameraSampler = ProfilingSampler.Get(camera.cameraType);
            cameraSettings = defaultCameraSettings;
        }
        
        
        if (camera.cameraType == CameraType.Reflection)
        {
            useColorTexture = bufferSettings.copyColorReflection;
            useDepthTexture = bufferSettings.copyDepthReflection;
        }
        else
        {
            useColorTexture = bufferSettings.copyColor && cameraSettings.copyColor;
            useDepthTexture = bufferSettings.copyDepth && cameraSettings.copyDepth;
        }

        if (cameraSettings.overridePostFX) postFXSettings = cameraSettings.postFXSettings;

        var renderScale = cameraSettings.GetRenderScale(bufferSettings.renderScale);
        useScaledRendering = renderScale < 0.99f || renderScale > 1.01f;
        PrepareForSceneWindow();
        if (!Cull(shadowSettings.maxDistance)) return;

        useHDR = bufferSettings.allowHDR && camera.allowHDR;
        if (useScaledRendering)
        {
            renderScale = Mathf.Clamp(renderScale, renderScaleMin, renderScaleMax);
            bufferSize.x = (int)(camera.pixelWidth * renderScale);
            bufferSize.y = (int)(camera.pixelHeight * renderScale);
        }
        else
        {
            bufferSize.x = camera.pixelWidth;
            bufferSize.y = camera.pixelHeight;
        }
        
        bufferSettings.fxaa.enabled &= cameraSettings.allowFXAA;
        postFXStack.Setup(
            camera, bufferSize, postFXSettings, cameraSettings.keepAlpha, useHDR,
            colorLUTResolution, cameraSettings.finalBlendMode,
            bufferSettings.bicubicRescaling, bufferSettings.fxaa);
        
        useIntermediateBuffer = useScaledRendering ||
                                useColorTexture || useDepthTexture || postFXStack.IsActive;
        
        var renderGraphParameters = new RenderGraphParameters
        {
            commandBuffer = CommandBufferPool.Get(),
            currentFrameIndex = Time.frameCount,
            executionName = cameraSampler.name,
            rendererListCulling = true,
            scriptableRenderContext = context
        };
        buffer = renderGraphParameters.commandBuffer;
        
        renderGraph.BeginRecording(renderGraphParameters);
        using (new RenderGraphProfilingScope(renderGraph, cameraSampler))
        {
            // Add passes here.
            
            LightingPass.Record(
                renderGraph, lighting,
                cullingResults, shadowSettings, useLightsPerObject,
                cameraSettings.maskLights ? cameraSettings.renderingLayerMask : -1);

            SetupPass.Record(renderGraph, useIntermediateBuffer, useHDR, bufferSize, camera);
            
            // opaque pass
            GeometryPass.Record(
                renderGraph, camera, cullingResults,
                useLightsPerObject, cameraSettings.renderingLayerMask, true);

            SkyboxPass.Record(renderGraph, camera);

            if (useColorTexture || useDepthTexture)
            {
                CopyAttachmentsPass.Record(renderGraph, this);
            }

            // transparent pass
            GeometryPass.Record(
                renderGraph, camera, cullingResults,
                useLightsPerObject, cameraSettings.renderingLayerMask, false);
            
            UnsupportedShadersPass.Record(renderGraph, camera, cullingResults);
            
            if (postFXStack.IsActive)
            {
                PostFXPass.Record(renderGraph, postFXStack);
            }
            else if (useIntermediateBuffer)
            {
                FinalPass.Record(renderGraph, this, cameraSettings.finalBlendMode);
            }
            
            GizmosPass.Record(renderGraph, this);
        }
        renderGraph.EndRecordingAndExecute();

        context.ExecuteCommandBuffer(renderGraphParameters.commandBuffer);
        context.Submit();
        CommandBufferPool.Release(renderGraphParameters.commandBuffer);
    }

    private bool Cull(float maxShadowDistance)
    {
        if (camera.TryGetCullingParameters(out var p))
        {
            p.shadowDistance = Mathf.Min(maxShadowDistance, camera.farClipPlane);
            cullingResults = context.Cull(ref p);
            return true;
        }

        return false;
    }

    private void Cleanup()
    {
        lighting.Cleanup();
        if (useIntermediateBuffer)
        {
            if (useColorTexture) buffer.ReleaseTemporaryRT(colorTextureId);
            if (useDepthTexture) buffer.ReleaseTemporaryRT(depthTextureId);
        }
    }

    private void Submit()
    {
        ExecuteBuffer();
        context.Submit();
    }

    public void ExecuteBuffer()
    {
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    public void CopyAttachments()
    {
        ExecuteBuffer();
        if (useColorTexture)
        {
            buffer.GetTemporaryRT(
                colorTextureId, bufferSize.x, bufferSize.y,
                0, FilterMode.Bilinear, useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default);
            if (copyTextureSupported)
                buffer.CopyTexture(colorAttachmentId, colorTextureId);
            else
                Draw(colorAttachmentId, colorTextureId);
        }

        if (useDepthTexture)
        {
            buffer.GetTemporaryRT(
                depthTextureId, bufferSize.x, bufferSize.y,
                32, FilterMode.Point, RenderTextureFormat.Depth);
            if (copyTextureSupported)
                buffer.CopyTexture(depthAttachmentId, depthTextureId);
            else
                Draw(depthAttachmentId, depthTextureId, true);
        }

        if (!copyTextureSupported)
            buffer.SetRenderTarget(
                colorAttachmentId,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                depthAttachmentId,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
        ExecuteBuffer();
    }

    public void Draw(
        RenderTargetIdentifier from, RenderTargetIdentifier to, bool isDepth = false)
    {
        buffer.SetGlobalTexture(sourceTextureId, from);
        buffer.SetRenderTarget(
            to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        buffer.SetViewport(camera.pixelRect);
        buffer.DrawProcedural(
            Matrix4x4.identity, material, isDepth ? 1 : 0, MeshTopology.Triangles, 3);
    }

    public void DrawFinal(CameraSettings.FinalBlendMode finalBlendMode)
    {
        buffer.SetGlobalFloat(srcBlendId, (float)finalBlendMode.source);
        buffer.SetGlobalFloat(dstBlendId, (float)finalBlendMode.destination);
        buffer.SetGlobalTexture(sourceTextureId, colorAttachmentId);
        buffer.SetRenderTarget(
            BuiltinRenderTextureType.CameraTarget,
            finalBlendMode.destination == BlendMode.Zero && camera.rect == fullViewRect
                ? RenderBufferLoadAction.DontCare
                : RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store);
        buffer.SetViewport(camera.pixelRect);
        buffer.DrawProcedural(
            Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
        buffer.SetGlobalFloat(srcBlendId, 1f);
        buffer.SetGlobalFloat(dstBlendId, 0f);
    }
}