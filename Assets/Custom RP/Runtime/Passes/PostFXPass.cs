using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime.Passes
{
    public class PostFXPass
    {
        private static readonly ProfilingSampler GroupSampler = new("Post FX");
        private static readonly ProfilingSampler FinalSampler = new("Final Post FX");

        private static readonly int CopyBicubicId = Shader.PropertyToID("_CopyBicubic");
        private static readonly int FxaaConfigId = Shader.PropertyToID("_FXAAConfig");

        private static readonly GlobalKeyword FxaaLowKeyword = GlobalKeyword.Create("FXAA_QUALITY_LOW");
        private static readonly GlobalKeyword FxaaMediumKeyword = GlobalKeyword.Create("FXAA_QUALITY_MEDIUM");

        private static readonly GraphicsFormat ColorFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);

        private PostFXStack stack;
        private bool keepAlpha;
        private ScaleMode scaleMode;

        private TextureHandle colorSource;
        private TextureHandle colorGradingResult;
        private TextureHandle scaledResult;

        enum ScaleMode
        {
            None,
            Linear,
            Bicubic
        }
    
        void ConfigureFxaa(CommandBuffer buffer)
        {
            CameraBufferSettings.Fxaa fxaa = stack.BufferSettings.fxaa;
            buffer.SetKeyword(FxaaLowKeyword, fxaa.quality == CameraBufferSettings.Fxaa.Quality.Low);
            buffer.SetKeyword(FxaaMediumKeyword, fxaa.quality == CameraBufferSettings.Fxaa.Quality.Medium);
            buffer.SetGlobalVector(FxaaConfigId, new Vector4(
                fxaa.fixedThreshold,
                fxaa.relativeThreshold,
                fxaa.subpixelBlending));
        }
    
        void Render(RenderGraphContext context)
        {
            CommandBuffer buffer = context.cmd;
            buffer.SetGlobalFloat(PostFXStack.FinalSrcBlendId, 1f);
            buffer.SetGlobalFloat(PostFXStack.FinalDstBlendId, 0f);

            RenderTargetIdentifier finalSource;
            PostFXStack.Pass finalPass;
            if (stack.BufferSettings.fxaa.enabled)
            {
                finalSource = colorGradingResult;
                finalPass = keepAlpha ? PostFXStack.Pass.Fxaa : PostFXStack.Pass.FxaaWithLuma;
                ConfigureFxaa(buffer);
                stack.Draw(buffer, colorSource, finalSource, keepAlpha ?
                    PostFXStack.Pass.ApplyColorGrading : PostFXStack.Pass.ApplyColorGradingWithLuma);
            }
            else
            {
                finalSource = colorSource;
                finalPass = PostFXStack.Pass.ApplyColorGrading;
            }

            if (scaleMode == ScaleMode.None)
            {
                stack.DrawFinal(buffer, finalSource, finalPass);
            }
            else
            {
                stack.Draw(buffer, finalSource, scaledResult, finalPass);
                buffer.SetGlobalFloat(CopyBicubicId,
                    scaleMode == ScaleMode.Bicubic ? 1f : 0f);
                stack.DrawFinal(buffer, scaledResult, PostFXStack.Pass.FinalRescale);
            }
            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        public static void Record(
            RenderGraph renderGraph,
            PostFXStack stack,
            int colorLutResolution,
            bool keepAlpha,
            in CameraRendererTextures textures)
        {
            using var _ = new RenderGraphProfilingScope(renderGraph, GroupSampler);
        
            TextureHandle colorSource = BloomPass.Record(
                renderGraph, stack, textures);

            TextureHandle colorLut = ColorLutPass.Record(
                renderGraph, stack, colorLutResolution);
        
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                FinalSampler.name, out PostFXPass pass, FinalSampler);
            pass.keepAlpha = keepAlpha;
            pass.stack = stack;
            pass.colorSource = builder.ReadTexture(colorSource);
            builder.ReadTexture(colorLut);

            if (stack.BufferSize.x == stack.Camera.pixelWidth)
            {
                pass.scaleMode = ScaleMode.None;
            }
            else
            {
                pass.scaleMode =
                    stack.BufferSettings.bicubicRescaling ==
                    CameraBufferSettings.BicubicRescalingMode.UpAndDown ||
                    stack.BufferSettings.bicubicRescaling ==
                    CameraBufferSettings.BicubicRescalingMode.UpOnly &&
                    stack.BufferSize.x < stack.Camera.pixelWidth ?
                        ScaleMode.Bicubic : ScaleMode.Linear;
            }

            bool applyFxaa = stack.BufferSettings.fxaa.enabled;
            if (applyFxaa || pass.scaleMode != ScaleMode.None)
            {
                var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
                {
                    colorFormat = ColorFormat
                };
                if (applyFxaa)
                {
                    desc.name = "Color Grading Result";
                    pass.colorGradingResult = builder.CreateTransientTexture(desc);
                }
                if (pass.scaleMode != ScaleMode.None)
                {
                    desc.name = "Scaled Result";
                    pass.scaledResult = builder.CreateTransientTexture(desc);
                }
            }

            builder.SetRenderFunc<PostFXPass>(static (pass, context) => pass.Render(context));
        }
    }
}