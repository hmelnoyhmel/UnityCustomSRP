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
    BloomSettings bloom = default;

    public BloomSettings Bloom => bloom;
    
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
        
        [Min(0f)]
        public float threshold;

        [Range(0f, 1f)]
        public float thresholdKnee;
        
        [Min(0f)]
        public float intensity;
        
        public bool bicubicUpsampling;
    }
}