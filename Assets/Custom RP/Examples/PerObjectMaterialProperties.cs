using UnityEngine;

[DisallowMultipleComponent]
public class PerObjectMaterialProperties : MonoBehaviour
{
    private static readonly int
        baseColorId = Shader.PropertyToID("_BaseColor"),
        cutoffId = Shader.PropertyToID("_Cutoff"),
        metallicId = Shader.PropertyToID("_Metallic"),
        smoothnessId = Shader.PropertyToID("_Smoothness"),
        emissionColorId = Shader.PropertyToID("_EmissionColor");

    private static MaterialPropertyBlock block;

    [SerializeField] private Color baseColor = Color.white;

    [SerializeField] [Range(0f, 1f)] private float alphaCutoff = 0.5f, metallic, smoothness = 0.5f;

    [SerializeField] [ColorUsage(false, true)]
    private Color emissionColor = Color.black;

    private void Awake()
    {
        OnValidate();
    }

    private void OnValidate()
    {
        block ??= new MaterialPropertyBlock();
        block.SetColor(baseColorId, baseColor);
        block.SetFloat(cutoffId, alphaCutoff);
        block.SetFloat(metallicId, metallic);
        block.SetFloat(smoothnessId, smoothness);
        block.SetColor(emissionColorId, emissionColor);
        GetComponent<Renderer>().SetPropertyBlock(block);
    }
}