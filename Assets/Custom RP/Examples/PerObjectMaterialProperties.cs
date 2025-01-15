using System;
using UnityEngine;

[DisallowMultipleComponent]
public class PerObjectMaterialProperties : MonoBehaviour
{
    private static int baseColorId = Shader.PropertyToID("_BaseColor");
    private static int cutoffId = Shader.PropertyToID("_Cutoff");

    [SerializeField] private Color baseColor = Color.white;

    [SerializeField, Range(0f, 1f)] private float cutoff = 0.15f;

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
        GetComponent<Renderer>().SetPropertyBlock(block);
        
    }
}
