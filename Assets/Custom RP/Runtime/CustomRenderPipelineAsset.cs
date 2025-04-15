using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline")]
public partial class CustomRenderPipelineAsset : RenderPipelineAsset
{
    public enum ColorLUTResolution
    {
        _16 = 16,
        _32 = 32,
        _64 = 64
    }

    [SerializeField] private CameraBufferSettings cameraBuffer = new()
    {
        allowHDR = true,
        renderScale = 1f,
        fxaa = new CameraBufferSettings.FXAA
        {
            fixedThreshold = 0.0833f,
            relativeThreshold = 0.166f,
            subpixelBlending = 0.75f
        }
    };

    [SerializeField] private bool
        useDynamicBatching = true,
        useGPUInstancing = true,
        useSRPBatcher = true,
        useLightsPerObject = true;

    [SerializeField] private ShadowSettings shadows;

    [SerializeField] private PostFXSettings postFXSettings;

    [SerializeField] private ColorLUTResolution colorLUTResolution = ColorLUTResolution._32;

    [SerializeField] private Shader cameraRendererShader;

    protected override RenderPipeline CreatePipeline()
    {
        return new CustomRenderPipeline(
            cameraBuffer, useDynamicBatching, useGPUInstancing, useSRPBatcher,
            useLightsPerObject, shadows, postFXSettings, (int)colorLUTResolution,
            cameraRendererShader);
    }
}