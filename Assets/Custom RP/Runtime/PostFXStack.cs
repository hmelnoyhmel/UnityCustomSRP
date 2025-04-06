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
        Final
    }

    ScriptableRenderContext context;
    Camera camera;
    PostFXSettings settings;
    
    private const int maxBloomPyramidLevels = 16;
    private int bloomPyramidId;
    
    private int bloomBucibicUpsamplingId = Shader.PropertyToID("_BloomBicubicUpsampling");
    private int bloomIntensityId = Shader.PropertyToID("_BloomIntensity");
    private int bloomPrefilterId = Shader.PropertyToID("_BloomPrefilter");
    private int bloomResultId = Shader.PropertyToID("_BloomResult");
    private int bloomThresholdId = Shader.PropertyToID("_BloomThreshold"); 
    private int fxSourceId = Shader.PropertyToID("_PostFXSource");
    private int fxSource2Id = Shader.PropertyToID("_PostFXSource2");
    private int colorAdjustmentsId = Shader.PropertyToID("_ColorAdjustments");
    private int colorFilterId = Shader.PropertyToID("_ColorFilter");
    private int whiteBalanceId = Shader.PropertyToID("_WhiteBalance");
    private int splitToningShadowsId = Shader.PropertyToID("_SplitToningShadows");
    private int splitToningHighlightsId = Shader.PropertyToID("_SplitToningHighlights");
    private int channelMixerRedId = Shader.PropertyToID("_ChannelMixerRed");
    private int channelMixerGreenId = Shader.PropertyToID("_ChannelMixerGreen");
    private int channelMixerBlueId = Shader.PropertyToID("_ChannelMixerBlue");
    private int smhShadowsId = Shader.PropertyToID("_SMHShadows");
    private int smhMidtonesId = Shader.PropertyToID("_SMHMidtones");
    private int smhHighlightsId = Shader.PropertyToID("_SMHHighlights");
    private int smhRangeId = Shader.PropertyToID("_SMHRange");
    private int colorGradingLUTId = Shader.PropertyToID("_ColorGradingLUT");
    private int colorGradingLUTParametersId = Shader.PropertyToID("_ColorGradingLUTParameters");
    private int colorGradingLUTInLogId = Shader.PropertyToID("_ColorGradingLUTInLogC");
    
    private bool useHDR;
    private int colorLUTResolution;
    
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
    
    public void Setup (ScriptableRenderContext context, Camera camera, PostFXSettings settings, 
        bool useHDR, int colorLUTResolution) 
    {
        this.colorLUTResolution = colorLUTResolution;
        this.context = context;
        this.camera = camera;
        this.settings = camera.cameraType <= CameraType.SceneView ? settings : null;
        this.useHDR = useHDR;
        ApplySceneViewState();
    }
    
    public void Render (int sourceId) 
    {
        if (DoBloom(sourceId))
        {
            DoColorGradingAndToneMapping(bloomResultId);
            buffer.ReleaseTemporaryRT(bloomResultId);
        }
        else 
        {
            DoColorGradingAndToneMapping(sourceId);
        }
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
    
    bool DoBloom (int sourceId) 
    {
        PostFXSettings.BloomSettings bloom = settings.Bloom;
        
        int width = camera.pixelWidth / 2; 
        int height = camera.pixelHeight / 2;
        
        if (
            bloom.maxIterations == 0 ||
            bloom.intensity <= 0f ||
            height < bloom.downscaleLimit * 2 || 
            width < bloom.downscaleLimit * 2) 
        {
            return false;
        }
        
        buffer.BeginSample("Bloom");
        
        Vector4 threshold;
        threshold.x = Mathf.GammaToLinearSpace(bloom.threshold);
        threshold.y = threshold.x * bloom.thresholdKnee;
        threshold.z = 2f * threshold.y;
        threshold.w = 0.25f / (threshold.y + 0.00001f);
        threshold.y -= threshold.x;
        buffer.SetGlobalVector(bloomThresholdId, threshold);
        
        RenderTextureFormat format = useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
        buffer.GetTemporaryRT(bloomPrefilterId, width, height, 0, FilterMode.Bilinear, format);
        Draw(sourceId, bloomPrefilterId, 
            bloom.fadeFireflies ? Pass.BloomPrefilterFireflies : Pass.BloomPrefilter);
        
        width /= 2;
        height /= 2;
        
        int fromId = bloomPrefilterId; 
        int toId = bloomPyramidId + 1;
        
        
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
        
        Pass combinePass;
        Pass finalPass;
        float finalIntensity;
        if (bloom.mode == PostFXSettings.BloomSettings.Mode.Additive) 
        {
            combinePass = Pass.BloomAdd;
            finalPass = Pass.BloomAdd;
            buffer.SetGlobalFloat(bloomIntensityId, 1f);
            finalIntensity = bloom.intensity;
        }
        else 
        {
            combinePass = Pass.BloomScatter;
            finalPass = Pass.BloomScatterFinal;
            buffer.SetGlobalFloat(bloomIntensityId, bloom.scatter);
            finalIntensity = Mathf.Min(bloom.intensity, 0.95f);
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
            bloomResultId, camera.pixelWidth, camera.pixelHeight, 
            0, FilterMode.Bilinear, format
        );
        Draw(fromId, bloomResultId, finalPass);
        buffer.ReleaseTemporaryRT(fromId);
        buffer.EndSample("Bloom");
        return true;
    }
    
    void ConfigureColorAdjustments () 
    {
        PostFXSettings.ColorAdjustmentsSettings colorAdjustments = settings.ColorAdjustments;
        buffer.SetGlobalVector(colorAdjustmentsId, new Vector4(
            Mathf.Pow(2f, colorAdjustments.postExposure),
            colorAdjustments.contrast * 0.01f + 1f,
            colorAdjustments.hueShift * (1f / 360f),
            colorAdjustments.saturation * 0.01f + 1f
        ));
        buffer.SetGlobalColor(colorFilterId, colorAdjustments.colorFilter.linear);
        
    }
    
    void ConfigureWhiteBalance () {
        PostFXSettings.WhiteBalanceSettings whiteBalance = settings.WhiteBalance;
        buffer.SetGlobalVector(whiteBalanceId, ColorUtils.ColorBalanceToLMSCoeffs(
            whiteBalance.temperature, whiteBalance.tint
        ));
    }
    
    void ConfigureSplitToning () {
        PostFXSettings.SplitToningSettings splitToning = settings.SplitToning;
        Color splitColor = splitToning.shadows;
        splitColor.a = splitToning.balance * 0.01f;
        buffer.SetGlobalColor(splitToningShadowsId, splitColor);
        buffer.SetGlobalColor(splitToningHighlightsId, splitToning.highlights);
    }
    
    void DoColorGradingAndToneMapping(int sourceId) 
    {
        ConfigureColorAdjustments();
        ConfigureWhiteBalance();
        ConfigureSplitToning();
        ConfigureChannelMixer();
        ConfigureShadowsMidtonesHighlights();
        
        int lutHeight = colorLUTResolution;
        int lutWidth = lutHeight * lutHeight;
        buffer.GetTemporaryRT(
            colorGradingLUTId, lutWidth, lutHeight, 0,
            FilterMode.Bilinear, RenderTextureFormat.DefaultHDR
        );
        buffer.SetGlobalVector(colorGradingLUTParametersId, new Vector4(
            lutHeight, 0.5f / lutWidth, 0.5f / lutHeight, lutHeight / (lutHeight - 1f)
        ));
        
        PostFXSettings.ToneMappingSettings.Mode mode = settings.ToneMapping.mode;
        Pass pass = Pass.ColorGradingNone + (int)mode;
        buffer.SetGlobalFloat(
            colorGradingLUTInLogId, useHDR && pass != Pass.ColorGradingNone ? 1f : 0f
        );
        Draw(sourceId, colorGradingLUTId, pass);
        
        buffer.SetGlobalVector(colorGradingLUTParametersId,
            new Vector4(1f / lutWidth, 1f / lutHeight, lutHeight - 1f)
        );
        Draw(sourceId, BuiltinRenderTextureType.CameraTarget, Pass.Final);
        buffer.ReleaseTemporaryRT(colorGradingLUTId);
    }
    
    void ConfigureChannelMixer () 
    {
        PostFXSettings.ChannelMixerSettings channelMixer = settings.ChannelMixer;
        buffer.SetGlobalVector(channelMixerRedId, channelMixer.red);
        buffer.SetGlobalVector(channelMixerGreenId, channelMixer.green);
        buffer.SetGlobalVector(channelMixerBlueId, channelMixer.blue);
    }

    void ConfigureShadowsMidtonesHighlights () 
    {
        PostFXSettings.ShadowsMidtonesHighlightsSettings smh = settings.ShadowsMidtonesHighlights;
        buffer.SetGlobalColor(smhShadowsId, smh.shadows.linear);
        buffer.SetGlobalColor(smhMidtonesId, smh.midtones.linear);
        buffer.SetGlobalColor(smhHighlightsId, smh.highlights.linear);
        buffer.SetGlobalVector(smhRangeId, new Vector4(
            smh.shadowsStart, smh.shadowsEnd, smh.highlightsStart, smh.highLightsEnd
        ));
    }
    
}