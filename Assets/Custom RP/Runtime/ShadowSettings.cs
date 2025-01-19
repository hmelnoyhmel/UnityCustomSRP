using System;
using UnityEngine;

// Container for configuration options
[Serializable]
public class ShadowSettings
{
    [Min(0f)] public float maxDistance = 100f;

    public enum MapSize
    {
        _256 = 256, 
        _512 = 512, 
        _1024 = 1024,
        _2048 = 2048, 
        _4096 = 4096,
        _8192 = 8192
    }
    
    [Serializable]
    public struct Directional {

        public MapSize atlasSize;
    }
    
    public Directional directional = new Directional 
    {
        atlasSize = MapSize._1024
    };
    
}