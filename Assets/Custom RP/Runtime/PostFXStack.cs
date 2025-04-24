using UnityEngine;
using UnityEngine.Rendering;

namespace Custom_RP.Runtime
{
    public class PostFXStack
    {
        public enum Pass
        {
            BloomAdd,
            BloomHorizontal,
            BloomPrefilter,
            BloomPrefilterFireflies,
            BloomScatter,
            BloomScatterFinal,
            BloomVertical,
            Copy,
            ColorGradingNone,
            ColorGradingAces,
            ColorGradingNeutral,
            ColorGradingReinhard,
            ApplyColorGrading,
            ApplyColorGradingWithLuma,
            FinalRescale,
            Fxaa,
            FxaaWithLuma
        }
    
        public static readonly int
            FXSourceId = Shader.PropertyToID("_PostFXSource"),
            FXSource2Id = Shader.PropertyToID("_PostFXSource2"),
            FinalSrcBlendId = Shader.PropertyToID("_FinalSrcBlend"),
            FinalDstBlendId = Shader.PropertyToID("_FinalDstBlend");

        static readonly Rect FullViewRect = new(0f, 0f, 1f, 1f);

        public CameraBufferSettings BufferSettings
        { get; set; }

        public Vector2Int BufferSize
        { get; set; }

        public Camera Camera
        { get; set; }

        public CameraSettings.FinalBlendMode FinalBlendMode
        { get; set; }

        public PostFXSettings Settings
        { get; set; }

        public void Draw(CommandBuffer buffer, RenderTargetIdentifier to, Pass pass)
        {
            buffer.SetRenderTarget(
                to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            buffer.DrawProcedural(Matrix4x4.identity, Settings.Material, (int)pass,
                MeshTopology.Triangles, 3);
        }

        public void Draw(
            CommandBuffer buffer,
            RenderTargetIdentifier from,
            RenderTargetIdentifier to,
            Pass pass)
        {
            buffer.SetGlobalTexture(FXSourceId, from);
            buffer.SetRenderTarget(
                to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            buffer.DrawProcedural(Matrix4x4.identity, Settings.Material, (int)pass,
                MeshTopology.Triangles, 3);
        }

        public void DrawFinal(
            CommandBuffer buffer,
            RenderTargetIdentifier from,
            Pass pass)
        {
            buffer.SetGlobalFloat(FinalSrcBlendId, (float)FinalBlendMode.source);
            buffer.SetGlobalFloat(
                FinalDstBlendId, (float)FinalBlendMode.destination);
            buffer.SetGlobalTexture(FXSourceId, from);
            buffer.SetRenderTarget(
                BuiltinRenderTextureType.CameraTarget,
                FinalBlendMode.destination == BlendMode.Zero &&
                Camera.rect == FullViewRect ?
                    RenderBufferLoadAction.DontCare : RenderBufferLoadAction.Load,
                RenderBufferStoreAction.Store);
            buffer.SetViewport(Camera.pixelRect);
            buffer.DrawProcedural(Matrix4x4.identity, Settings.Material, (int)pass,
                MeshTopology.Triangles, 3);
        }
    }
}