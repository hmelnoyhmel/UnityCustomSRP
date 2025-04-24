using UnityEngine;

namespace Custom_RP.Runtime
{
    [System.Serializable]
    public class CustomRenderPipelineSettings
    {
        public CameraBufferSettings cameraBuffer = new()
        {
            allowHDR = true,
            renderScale = 1f,
            fxaa = new()
            {
                fixedThreshold = 0.0833f,
                relativeThreshold = 0.166f,
                subpixelBlending = 0.75f
            }
        };

        public bool useSrpBatcher = true;
    
        public ForwardPlusSettings forwardPlus;

        public ShadowSettings shadows;

        public PostFXSettings postFXSettings;

        public enum ColorLutResolution
        {
            _16 = 16, 
            _32 = 32, 
            _64 = 64
        }

        public ColorLutResolution colorLutResolution = ColorLutResolution._32;

        public Shader cameraRendererShader;
        public Shader cameraDebuggerShader;
    }
}