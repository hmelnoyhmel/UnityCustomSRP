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
            None = -1,
            ACES,
            Neutral,
            Reinhard
        }

        public Mode mode;
    }
}