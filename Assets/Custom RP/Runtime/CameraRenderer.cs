using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public class CameraRenderer
{
    public const float renderScaleMin = 0.1f, renderScaleMax = 2f;

    private static readonly CameraSettings defaultCameraSettings = new();
    
    private readonly Material material;

    private readonly PostFXStack postFXStack = new();
    
    public CameraRenderer(Shader shader) => material = CoreUtils.CreateEngineMaterial(shader);

    public void Dispose() => CoreUtils.Destroy(material);

    public void Render(
        RenderGraph renderGraph, ScriptableRenderContext context, Camera camera,
        CameraBufferSettings bufferSettings,
        bool useLightsPerObject, ShadowSettings shadowSettings, PostFXSettings postFXSettings,
        int colorLUTResolution)
    {
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
        
#if UNITY_EDITOR
#pragma warning disable 0618
        if (cameraSettings.renderingLayerMask != 0)
        {
            // Migrate camera settings to new rendering layer mask.
            cameraSettings.newRenderingLayerMask =
                (uint)cameraSettings.renderingLayerMask;
            cameraSettings.renderingLayerMask = 0;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
            );
        }
#pragma warning restore 0618
#endif
        
        bool useColorTexture, useDepthTexture;
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
        var useScaledRendering = renderScale < 0.99f || renderScale > 1.01f;
        
        #if UNITY_EDITOR
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
            useScaledRendering = false;
        }
        #endif
        
        if (!camera.TryGetCullingParameters(
                out ScriptableCullingParameters scriptableCullingParameters))
        {
            return;
        }
        scriptableCullingParameters.shadowDistance =
            Mathf.Min(shadowSettings.maxDistance, camera.farClipPlane);
        CullingResults cullingResults = context.Cull(ref scriptableCullingParameters);

        var useHDR = bufferSettings.allowHDR && camera.allowHDR;
        Vector2Int bufferSize = default;
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
        
        var useIntermediateBuffer = useScaledRendering ||
                                useColorTexture || useDepthTexture || postFXStack.IsActive;
        
        var renderGraphParameters = new RenderGraphParameters
        {
            commandBuffer = CommandBufferPool.Get(),
            currentFrameIndex = Time.frameCount,
            executionName = cameraSampler.name,
            rendererListCulling = true,
            scriptableRenderContext = context
        };
        
        renderGraph.BeginRecording(renderGraphParameters);
        using (new RenderGraphProfilingScope(renderGraph, cameraSampler))
        {
            // Add passes here.
            
            ShadowTextures shadowTextures = LightingPass.Record(
                renderGraph, cullingResults, shadowSettings, useLightsPerObject,
                cameraSettings.maskLights ? cameraSettings.renderingLayerMask : -1);

            CameraRendererTextures textures = 
                SetupPass.Record(renderGraph, useIntermediateBuffer, useColorTexture, 
                    useDepthTexture, useHDR, bufferSize, camera);
            
            // opaque pass
            GeometryPass.Record(
                renderGraph, camera, cullingResults,
                useLightsPerObject, cameraSettings.renderingLayerMask, 
                true, textures, shadowTextures);

            SkyboxPass.Record(renderGraph, camera, textures);

            var copier = new CameraRendererCopier(
                material, camera, cameraSettings.finalBlendMode);
            CopyAttachmentsPass.Record(
                renderGraph, useColorTexture, useDepthTexture, 
                copier, textures);

            // transparent pass
            GeometryPass.Record(
                renderGraph, camera, cullingResults,
                useLightsPerObject, cameraSettings.renderingLayerMask, 
                false, textures, shadowTextures);
            
            UnsupportedShadersPass.Record(renderGraph, camera, cullingResults);
            
            if (postFXStack.IsActive)
            {
                PostFXPass.Record(renderGraph, postFXStack, textures);
            }
            else if (useIntermediateBuffer)
            {
                FinalPass.Record(renderGraph, copier, textures);
            }
            
            GizmosPass.Record(renderGraph, useIntermediateBuffer, copier, textures);
        }
        renderGraph.EndRecordingAndExecute();

        context.ExecuteCommandBuffer(renderGraphParameters.commandBuffer);
        context.Submit();
        CommandBufferPool.Release(renderGraphParameters.commandBuffer);
    }

}