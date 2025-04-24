using UnityEngine;

namespace Custom_RP.Examples
{
    [DisallowMultipleComponent]
    public class PerObjectMaterialProperties : MonoBehaviour
    {
        private static readonly int
            BaseColorId = Shader.PropertyToID("_BaseColor"),
            CutoffId = Shader.PropertyToID("_Cutoff"),
            MetallicId = Shader.PropertyToID("_Metallic"),
            SmoothnessId = Shader.PropertyToID("_Smoothness"),
            EmissionColorId = Shader.PropertyToID("_EmissionColor");

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
            block.SetColor(BaseColorId, baseColor);
            block.SetFloat(CutoffId, alphaCutoff);
            block.SetFloat(MetallicId, metallic);
            block.SetFloat(SmoothnessId, smoothness);
            block.SetColor(EmissionColorId, emissionColor);
            GetComponent<Renderer>().SetPropertyBlock(block);
        }
    }
}