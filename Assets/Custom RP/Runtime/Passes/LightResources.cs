using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime.Passes
{
    public readonly ref struct LightResources
    {
        public readonly BufferHandle DirectionalLightDataBuffer;
        public readonly BufferHandle OtherLightDataBuffer;
        public readonly BufferHandle TilesBuffer;
	
        public readonly ShadowResources ShadowResources;

        public LightResources(
            BufferHandle directionalLightDataBuffer,
            BufferHandle otherLightDataBuffer,
            BufferHandle tilesBuffer,
            ShadowResources shadowResources)
        {
            this.DirectionalLightDataBuffer = directionalLightDataBuffer;
            this.OtherLightDataBuffer = otherLightDataBuffer;
            this.TilesBuffer = tilesBuffer;
            this.ShadowResources = shadowResources;
        }
    }
}