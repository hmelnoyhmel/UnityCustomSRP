using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Rendering/Custom Post FX Settings")]
public class PostFXSettings : ScriptableObject
{
    [SerializeField]
    Shader shader = default;
    
    [NonSerialized]
    Material material;
    
    [SerializeField]
    BloomSettings bloom = new BloomSettings 
    {
        scatter = 0.7f
    };

    public BloomSettings Bloom => bloom;
    
    [SerializeField]
    ToneMappingSettings toneMapping = default;

    public ToneMappingSettings ToneMapping => toneMapping;
    
    [SerializeField]
    ColorAdjustmentsSettings colorAdjustments = new ColorAdjustmentsSettings 
    {
        colorFilter = Color.white
    };

    public ColorAdjustmentsSettings ColorAdjustments => colorAdjustments;
    
    [SerializeField]
    WhiteBalanceSettings whiteBalance = default;

    public WhiteBalanceSettings WhiteBalance => whiteBalance;
    
    [SerializeField]
    SplitToningSettings splitToning = new SplitToningSettings 
    {
        shadows = Color.gray,
        highlights = Color.gray
    };

    public SplitToningSettings SplitToning => splitToning;
    
    [SerializeField]
    ChannelMixerSettings channelMixer = new ChannelMixerSettings 
    {
        red = Vector3.right,
        green = Vector3.up,
        blue = Vector3.forward
    };

    public ChannelMixerSettings ChannelMixer => channelMixer;
    
    [SerializeField]
    ShadowsMidtonesHighlightsSettings shadowsMidtonesHighlights = new ShadowsMidtonesHighlightsSettings 
    {
            shadows = Color.white,
            midtones = Color.white,
            highlights = Color.white,
            shadowsEnd = 0.3f,
            highlightsStart = 0.55f,
            highLightsEnd = 1f
    };

    public ShadowsMidtonesHighlightsSettings ShadowsMidtonesHighlights => shadowsMidtonesHighlights;
    
    public Material Material 
    {
        get 
        {
            if (material == null && shader != null) 
            {
                material = new Material(shader);
                material.hideFlags = HideFlags.HideAndDontSave;
            }
            
            return material;
        }
    }
    
    [Serializable]
    public struct BloomSettings 
    {
        [Range(0f, 16f)]
        public int maxIterations;

        [Min(1f)]
        public int downscaleLimit;
        
        //[Range(0f, 1f)] - correct limits if not using HDR
        [Min(0f)]
        public float threshold;

        [Range(0f, 1f)]
        public float thresholdKnee;
        
        [Min(0f)]
        public float intensity;
        
        public bool bicubicUpsampling;
        
        public bool fadeFireflies;
        
        public enum Mode { Additive, Scattering }
        
        public Mode mode;

        [Range(0.05f, 0.95f)]
        public float scatter;
    }
    
    [Serializable]
    public struct ToneMappingSettings 
    {
        public enum Mode
        {
            None,
            ACES,
            Neutral,
            Reinhard
        }

        public Mode mode;
    }

    [Serializable]
    public struct ColorAdjustmentsSettings
    {
        public float postExposure;

        [Range(-100f, 100f)]
        public float contrast;

        [ColorUsage(false, true)]
        public Color colorFilter;

        [Range(-180f, 180f)]
        public float hueShift;

        [Range(-100f, 100f)]
        public float saturation;
        
    }
    
    [Serializable]
    public struct WhiteBalanceSettings
    {
        [Range(-100f, 100f)] 
        public float temperature;
        
        [Range(-100f, 100f)] 
        public float tint;
    }
    
    [Serializable]
    public struct SplitToningSettings
    {
        [ColorUsage(false)] 
        public Color shadows; 
        
        [ColorUsage(false)] 
        public Color highlights;

        [Range(-100f, 100f)]
        public float balance;
    }
    
    [Serializable]
    public struct ChannelMixerSettings
    {

        public Vector3 red;
        public Vector3 green;
        public Vector3 blue;
    }

    [Serializable]
    public struct ShadowsMidtonesHighlightsSettings
    {

        [ColorUsage(false, true)] 
        public Color shadows;

        [ColorUsage(false, true)] 
        public Color midtones;
            
        [ColorUsage(false, true)]
        public Color highlights;

        [Range(0f, 2f)]
        public float shadowsStart;
        
        [Range(0f, 2f)]
        public float shadowsEnd;
            
        [Range(0f, 2f)]
        public float highlightsStart;
            
        [Range(0f, 2f)]
        public float highLightsEnd;
        
    }
    
}