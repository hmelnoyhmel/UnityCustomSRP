using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using static PostFXSettings;

public partial class PostFXStack
{
    private const string
        fxaaQualityLowKeyword = "FXAA_QUALITY_LOW",
        fxaaQualityMediumKeyword = "FXAA_QUALITY_MEDIUM";

    private const int maxBloomPyramidLevels = 16;

    private static readonly Rect fullViewRect = new(0f, 0f, 1f, 1f);

    private readonly int
        bloomBicubicUpsamplingId = Shader.PropertyToID("_BloomBicubicUpsampling"),
        bloomIntensityId = Shader.PropertyToID("_BloomIntensity"),
        bloomPrefilterId = Shader.PropertyToID("_BloomPrefilter"),
        bloomResultId = Shader.PropertyToID("_BloomResult"),
        bloomThresholdId = Shader.PropertyToID("_BloomThreshold"),
        fxSourceId = Shader.PropertyToID("_PostFXSource"),
        fxSource2Id = Shader.PropertyToID("_PostFXSource2");

    private readonly int bloomPyramidId;

    CommandBuffer buffer;

    private readonly int
        colorGradingLUTId = Shader.PropertyToID("_ColorGradingLUT"),
        colorGradingLUTParametersId = Shader.PropertyToID("_ColorGradingLUTParameters"),
        colorGradingLUTInLogId = Shader.PropertyToID("_ColorGradingLUTInLogC"),
        colorAdjustmentsId = Shader.PropertyToID("_ColorAdjustments"),
        colorFilterId = Shader.PropertyToID("_ColorFilter"),
        whiteBalanceId = Shader.PropertyToID("_WhiteBalance"),
        splitToningShadowsId = Shader.PropertyToID("_SplitToningShadows"),
        splitToningHighlightsId = Shader.PropertyToID("_SplitToningHighlights"),
        channelMixerRedId = Shader.PropertyToID("_ChannelMixerRed"),
        channelMixerGreenId = Shader.PropertyToID("_ChannelMixerGreen"),
        channelMixerBlueId = Shader.PropertyToID("_ChannelMixerBlue"),
        smhShadowsId = Shader.PropertyToID("_SMHShadows"),
        smhMidtonesId = Shader.PropertyToID("_SMHMidtones"),
        smhHighlightsId = Shader.PropertyToID("_SMHHighlights"),
        smhRangeId = Shader.PropertyToID("_SMHRange");

    private readonly int
        copyBicubicId = Shader.PropertyToID("_CopyBicubic"),
        colorGradingResultId = Shader.PropertyToID("_ColorGradingResult"),
        finalResultId = Shader.PropertyToID("_FinalResult"),
        finalSrcBlendId = Shader.PropertyToID("_FinalSrcBlend"),
        finalDstBlendId = Shader.PropertyToID("_FinalDstBlend");

    private readonly int fxaaConfigId = Shader.PropertyToID("_FXAAConfig");

    private CameraBufferSettings.BicubicRescalingMode bicubicRescaling;

    private Vector2Int bufferSize;

    private Camera camera;

    private int colorLUTResolution;

    private ScriptableRenderContext context;

    private CameraSettings.FinalBlendMode finalBlendMode;

    private CameraBufferSettings.FXAA fxaa;

    private bool keepAlpha, useHDR;

    private PostFXSettings settings;

    public PostFXStack()
    {
        bloomPyramidId = Shader.PropertyToID("_BloomPyramid0");
        for (var i = 1; i < maxBloomPyramidLevels * 2; i++) Shader.PropertyToID("_BloomPyramid" + i);
    }

    public bool IsActive => settings != null;

    public void Setup(
        Camera camera, Vector2Int bufferSize,
        PostFXSettings settings, bool keepAlpha, bool useHDR, int colorLUTResolution,
        CameraSettings.FinalBlendMode finalBlendMode,
        CameraBufferSettings.BicubicRescalingMode bicubicRescaling,
        CameraBufferSettings.FXAA fxaa)
    {
        this.fxaa = fxaa;
        this.bicubicRescaling = bicubicRescaling;
        this.bufferSize = bufferSize;
        this.finalBlendMode = finalBlendMode;
        this.colorLUTResolution = colorLUTResolution;
        this.keepAlpha = keepAlpha;
        this.useHDR = useHDR;
        this.camera = camera;
        this.settings = camera.cameraType <= CameraType.SceneView ? settings : null;
        ApplySceneViewState();
    }

    public void Render(RenderGraphContext context, int sourceId)
    {
        buffer = context.cmd;
        
        if (DoBloom(sourceId))
        {
            DoFinal(bloomResultId);
            buffer.ReleaseTemporaryRT(bloomResultId);
        }
        else
        {
            DoFinal(sourceId);
        }

        context.renderContext.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    private bool DoBloom(int sourceId)
    {
        var bloom = settings.Bloom;
        int width, height;
        if (bloom.ignoreRenderScale)
        {
            width = camera.pixelWidth / 2;
            height = camera.pixelHeight / 2;
        }
        else
        {
            width = bufferSize.x / 2;
            height = bufferSize.y / 2;
        }

        if (
            bloom.maxIterations == 0 || bloom.intensity <= 0f ||
            height < bloom.downscaleLimit * 2 || width < bloom.downscaleLimit * 2)
            return false;

        buffer.BeginSample("Bloom");
        Vector4 threshold;
        threshold.x = Mathf.GammaToLinearSpace(bloom.threshold);
        threshold.y = threshold.x * bloom.thresholdKnee;
        threshold.z = 2f * threshold.y;
        threshold.w = 0.25f / (threshold.y + 0.00001f);
        threshold.y -= threshold.x;
        buffer.SetGlobalVector(bloomThresholdId, threshold);

        var format = useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
        buffer.GetTemporaryRT(
            bloomPrefilterId, width, height, 0, FilterMode.Bilinear, format);
        Draw(
            sourceId, bloomPrefilterId, bloom.fadeFireflies ? Pass.BloomPrefilterFireflies : Pass.BloomPrefilter);
        width /= 2;
        height /= 2;

        int fromId = bloomPrefilterId, toId = bloomPyramidId + 1;
        int i;
        for (i = 0; i < bloom.maxIterations; i++)
        {
            if (height < bloom.downscaleLimit || width < bloom.downscaleLimit) break;
            var midId = toId - 1;
            buffer.GetTemporaryRT(midId, width, height, 0, FilterMode.Bilinear, format);
            buffer.GetTemporaryRT(toId, width, height, 0, FilterMode.Bilinear, format);
            Draw(fromId, midId, Pass.BloomHorizontal);
            Draw(midId, toId, Pass.BloomVertical);
            fromId = toId;
            toId += 2;
            width /= 2;
            height /= 2;
        }

        buffer.ReleaseTemporaryRT(bloomPrefilterId);
        buffer.SetGlobalFloat(
            bloomBicubicUpsamplingId, bloom.bicubicUpsampling ? 1f : 0f);

        Pass combinePass, finalPass;
        float finalIntensity;
        if (bloom.mode == BloomSettings.Mode.Additive)
        {
            combinePass = finalPass = Pass.BloomAdd;
            buffer.SetGlobalFloat(bloomIntensityId, 1f);
            finalIntensity = bloom.intensity;
        }
        else
        {
            combinePass = Pass.BloomScatter;
            finalPass = Pass.BloomScatterFinal;
            buffer.SetGlobalFloat(bloomIntensityId, bloom.scatter);
            finalIntensity = Mathf.Min(bloom.intensity, 1f);
        }

        if (i > 1)
        {
            buffer.ReleaseTemporaryRT(fromId - 1);
            toId -= 5;
            for (i -= 1; i > 0; i--)
            {
                buffer.SetGlobalTexture(fxSource2Id, toId + 1);
                Draw(fromId, toId, combinePass);
                buffer.ReleaseTemporaryRT(fromId);
                buffer.ReleaseTemporaryRT(toId + 1);
                fromId = toId;
                toId -= 2;
            }
        }
        else
        {
            buffer.ReleaseTemporaryRT(bloomPyramidId);
        }

        buffer.SetGlobalFloat(bloomIntensityId, finalIntensity);
        buffer.SetGlobalTexture(fxSource2Id, sourceId);
        buffer.GetTemporaryRT(
            bloomResultId, bufferSize.x, bufferSize.y, 0,
            FilterMode.Bilinear, format);
        Draw(fromId, bloomResultId, finalPass);
        buffer.ReleaseTemporaryRT(fromId);
        buffer.EndSample("Bloom");
        return true;
    }

    private void ConfigureColorAdjustments()
    {
        var colorAdjustments = settings.ColorAdjustments;
        buffer.SetGlobalVector(colorAdjustmentsId, new Vector4(
            Mathf.Pow(2f, colorAdjustments.postExposure),
            colorAdjustments.contrast * 0.01f + 1f,
            colorAdjustments.hueShift * (1f / 360f),
            colorAdjustments.saturation * 0.01f + 1f));
        buffer.SetGlobalColor(colorFilterId, colorAdjustments.colorFilter.linear);
    }

    private void ConfigureWhiteBalance()
    {
        var whiteBalance = settings.WhiteBalance;
        buffer.SetGlobalVector(whiteBalanceId, ColorUtils.ColorBalanceToLMSCoeffs(
            whiteBalance.temperature, whiteBalance.tint));
    }

    private void ConfigureSplitToning()
    {
        var splitToning = settings.SplitToning;
        var splitColor = splitToning.shadows;
        splitColor.a = splitToning.balance * 0.01f;
        buffer.SetGlobalColor(splitToningShadowsId, splitColor);
        buffer.SetGlobalColor(splitToningHighlightsId, splitToning.highlights);
    }

    private void ConfigureChannelMixer()
    {
        var channelMixer = settings.ChannelMixer;
        buffer.SetGlobalVector(channelMixerRedId, channelMixer.red);
        buffer.SetGlobalVector(channelMixerGreenId, channelMixer.green);
        buffer.SetGlobalVector(channelMixerBlueId, channelMixer.blue);
    }

    private void ConfigureShadowsMidtonesHighlights()
    {
        var smh = settings.ShadowsMidtonesHighlights;
        buffer.SetGlobalColor(smhShadowsId, smh.shadows.linear);
        buffer.SetGlobalColor(smhMidtonesId, smh.midtones.linear);
        buffer.SetGlobalColor(smhHighlightsId, smh.highlights.linear);
        buffer.SetGlobalVector(smhRangeId, new Vector4(
            smh.shadowsStart, smh.shadowsEnd, smh.highlightsStart, smh.highLightsEnd));
    }

    private void ConfigureFXAA()
    {
        if (fxaa.quality == CameraBufferSettings.FXAA.Quality.Low)
        {
            buffer.EnableShaderKeyword(fxaaQualityLowKeyword);
            buffer.DisableShaderKeyword(fxaaQualityMediumKeyword);
        }
        else if (fxaa.quality == CameraBufferSettings.FXAA.Quality.Medium)
        {
            buffer.DisableShaderKeyword(fxaaQualityLowKeyword);
            buffer.EnableShaderKeyword(fxaaQualityMediumKeyword);
        }
        else
        {
            buffer.DisableShaderKeyword(fxaaQualityLowKeyword);
            buffer.DisableShaderKeyword(fxaaQualityMediumKeyword);
        }

        buffer.SetGlobalVector(fxaaConfigId, new Vector4(
            fxaa.fixedThreshold, fxaa.relativeThreshold, fxaa.subpixelBlending));
    }

    private void DoFinal(int sourceId)
    {
        ConfigureColorAdjustments();
        ConfigureWhiteBalance();
        ConfigureSplitToning();
        ConfigureChannelMixer();
        ConfigureShadowsMidtonesHighlights();

        var lutHeight = colorLUTResolution;
        var lutWidth = lutHeight * lutHeight;
        buffer.GetTemporaryRT(
            colorGradingLUTId, lutWidth, lutHeight, 0,
            FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
        buffer.SetGlobalVector(colorGradingLUTParametersId, new Vector4(
            lutHeight, 0.5f / lutWidth, 0.5f / lutHeight, lutHeight / (lutHeight - 1f)));

        var mode = settings.ToneMapping.mode;
        var pass = Pass.ColorGradingNone + (int)mode;
        buffer.SetGlobalFloat(
            colorGradingLUTInLogId, useHDR && pass != Pass.ColorGradingNone ? 1f : 0f);
        Draw(sourceId, colorGradingLUTId, pass);

        buffer.SetGlobalVector(colorGradingLUTParametersId,
            new Vector4(1f / lutWidth, 1f / lutHeight, lutHeight - 1f));

        buffer.SetGlobalFloat(finalSrcBlendId, 1f);
        buffer.SetGlobalFloat(finalDstBlendId, 0f);
        if (fxaa.enabled)
        {
            ConfigureFXAA();
            buffer.GetTemporaryRT(
                colorGradingResultId, bufferSize.x, bufferSize.y, 0,
                FilterMode.Bilinear, RenderTextureFormat.Default);
            Draw(
                sourceId, colorGradingResultId,
                keepAlpha ? Pass.ApplyColorGrading : Pass.ApplyColorGradingWithLuma);
        }

        if (bufferSize.x == camera.pixelWidth)
        {
            if (fxaa.enabled)
            {
                DrawFinal(
                    colorGradingResultId, keepAlpha ? Pass.FXAA : Pass.FXAAWithLuma);
                buffer.ReleaseTemporaryRT(colorGradingResultId);
            }
            else
            {
                DrawFinal(sourceId, Pass.ApplyColorGrading);
            }
        }
        else
        {
            buffer.GetTemporaryRT(
                finalResultId, bufferSize.x, bufferSize.y, 0,
                FilterMode.Bilinear, RenderTextureFormat.Default);

            if (fxaa.enabled)
            {
                Draw(
                    colorGradingResultId, finalResultId,
                    keepAlpha ? Pass.FXAA : Pass.FXAAWithLuma);
                buffer.ReleaseTemporaryRT(colorGradingResultId);
            }
            else
            {
                Draw(sourceId, finalResultId, Pass.ApplyColorGrading);
            }

            var bicubicSampling =
                bicubicRescaling == CameraBufferSettings.BicubicRescalingMode.UpAndDown ||
                (bicubicRescaling == CameraBufferSettings.BicubicRescalingMode.UpOnly &&
                 bufferSize.x < camera.pixelWidth);
            buffer.SetGlobalFloat(copyBicubicId, bicubicSampling ? 1f : 0f);
            DrawFinal(finalResultId, Pass.FinalRescale);
            buffer.ReleaseTemporaryRT(finalResultId);
        }

        buffer.ReleaseTemporaryRT(colorGradingLUTId);
    }

    private void Draw(RenderTargetIdentifier from, RenderTargetIdentifier to, Pass pass)
    {
        buffer.SetGlobalTexture(fxSourceId, from);
        buffer.SetRenderTarget(
            to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        buffer.DrawProcedural(
            Matrix4x4.identity, settings.Material, (int)pass, MeshTopology.Triangles, 3);
    }

    private void DrawFinal(RenderTargetIdentifier from, Pass pass)
    {
        buffer.SetGlobalFloat(finalSrcBlendId, (float)finalBlendMode.source);
        buffer.SetGlobalFloat(finalDstBlendId, (float)finalBlendMode.destination);
        buffer.SetGlobalTexture(fxSourceId, from);
        buffer.SetRenderTarget(
            BuiltinRenderTextureType.CameraTarget,
            finalBlendMode.destination == BlendMode.Zero && camera.rect == fullViewRect
                ? RenderBufferLoadAction.DontCare
                : RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store);
        buffer.SetViewport(camera.pixelRect);
        buffer.DrawProcedural(
            Matrix4x4.identity, settings.Material, (int)pass, MeshTopology.Triangles, 3);
    }

    private enum Pass
    {
        BloomAdd,
        BloomHorizontal,
        BloomPrefilter,
        BloomPrefilterFireflies,
        BloomScatter,
        BloomScatterFinal,
        BloomVertical,
        Copy,
        ColorGradingNone,
        ColorGradingACES,
        ColorGradingNeutral,
        ColorGradingReinhard,
        ApplyColorGrading,
        ApplyColorGradingWithLuma,
        FinalRescale,
        FXAA,
        FXAAWithLuma
    }
}