using System;
using UnityEngine;

[DisallowMultipleComponent]
public class PerObjectMaterialProperties : MonoBehaviour
{
    private static int baseColorId = Shader.PropertyToID("_BaseColor");
    private static int cutoffId = Shader.PropertyToID("_Cutoff");
    private static int metallicId = Shader.PropertyToID("_Metallic");
    private static int smoothnessId = Shader.PropertyToID("_Smoothness");
    private static int emissionColorId = Shader.PropertyToID("_EmissionColor");

    [SerializeField] private Color baseColor = Color.white;
    
    [SerializeField, ColorUsage(false, true)] Color emissionColor = Color.black;

    [SerializeField, Range(0f, 1f)] private float cutoff = 0.15f;
    [SerializeField, Range(0f, 1f)] private float metallic = 0.0f;
    [SerializeField, Range(0f, 1f)] private float smoothness = 0.5f;

    private static MaterialPropertyBlock block;

    void Awake() 
    {
        OnValidate();
    }
    
    private void OnValidate()
    {
        // if (block == null)
        block ??= new MaterialPropertyBlock();
        
        block.SetColor(baseColorId, baseColor);
        block.SetFloat(cutoffId, cutoff);
        block.SetFloat(metallicId, metallic);
        block.SetFloat(smoothnessId, smoothness);
        block.SetColor(emissionColorId, emissionColor);
        
        GetComponent<MeshRenderer>().SetPropertyBlock(block);
        
    }
}
