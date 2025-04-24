using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime
{
    public readonly ref struct ShadowResources
    {
        public readonly TextureHandle DirectionalAtlas, OtherAtlas;

        public readonly BufferHandle
            DirectionalShadowCascadesBuffer,
            DirectionalShadowMatricesBuffer,
            OtherShadowDataBuffer;
    
        public ShadowResources(
            TextureHandle directionalAtlas,
            TextureHandle otherAtlas,
            BufferHandle directionalShadowCascadesBuffer,
            BufferHandle directionalShadowMatricesBuffer,
            BufferHandle otherShadowDataBuffer)
        {
            this.DirectionalAtlas = directionalAtlas;
            this.OtherAtlas = otherAtlas;
            this.DirectionalShadowCascadesBuffer = directionalShadowCascadesBuffer;
            this.DirectionalShadowMatricesBuffer = directionalShadowMatricesBuffer;
            this.OtherShadowDataBuffer = otherShadowDataBuffer;
        }
    }
}