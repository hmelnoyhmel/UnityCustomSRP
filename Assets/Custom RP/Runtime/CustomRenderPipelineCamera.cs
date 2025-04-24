using UnityEngine;
using UnityEngine.Rendering;

namespace Custom_RP.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class CustomRenderPipelineCamera : MonoBehaviour
    {
        [SerializeField] private CameraSettings settings;
    
        ProfilingSampler sampler;
    
        public ProfilingSampler Sampler => sampler ??= new(GetComponent<Camera>().name);

        public CameraSettings Settings => settings ??= new CameraSettings();
    }
}