using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime.Passes
{
	public class ColorLutPass
	{
		static readonly ProfilingSampler Sampler = new("Color LUT");

		static readonly int
			ColorGradingLutId = Shader.PropertyToID("_ColorGradingLUT"),
			ColorGradingLutParametersId =
				Shader.PropertyToID("_ColorGradingLUTParameters"),
			ColorGradingLutInLogId = Shader.PropertyToID("_ColorGradingLUTInLogC"),
			ColorAdjustmentsId = Shader.PropertyToID("_ColorAdjustments"),
			ColorFilterId = Shader.PropertyToID("_ColorFilter"),
			WhiteBalanceId = Shader.PropertyToID("_WhiteBalance"),
			SplitToningShadowsId = Shader.PropertyToID("_SplitToningShadows"),
			SplitToningHighlightsId = Shader.PropertyToID("_SplitToningHighlights"),
			ChannelMixerRedId = Shader.PropertyToID("_ChannelMixerRed"),
			ChannelMixerGreenId = Shader.PropertyToID("_ChannelMixerGreen"),
			ChannelMixerBlueId = Shader.PropertyToID("_ChannelMixerBlue"),
			SmhShadowsId = Shader.PropertyToID("_SMHShadows"),
			SmhMidtonesId = Shader.PropertyToID("_SMHMidtones"),
			SmhHighlightsId = Shader.PropertyToID("_SMHHighlights"),
			SmhRangeId = Shader.PropertyToID("_SMHRange");

		static readonly GraphicsFormat ColorFormat =
			SystemInfo.GetGraphicsFormat(DefaultFormat.HDR);

		PostFXStack stack;

		int colorLutResolution;

		TextureHandle colorLut;

		void ConfigureColorAdjustments(CommandBuffer buffer, PostFXSettings settings)
		{
			PostFXSettings.ColorAdjustmentsSettings colorAdjustments = settings.ColorAdjustments;
			buffer.SetGlobalVector(ColorAdjustmentsId, new Vector4(
				Mathf.Pow(2f, colorAdjustments.postExposure),
				colorAdjustments.contrast * 0.01f + 1f,
				colorAdjustments.hueShift * (1f / 360f),
				colorAdjustments.saturation * 0.01f + 1f));
			buffer.SetGlobalColor(
				ColorFilterId, colorAdjustments.colorFilter.linear);
		}

		void ConfigureWhiteBalance(CommandBuffer buffer, PostFXSettings settings)
		{
			PostFXSettings.WhiteBalanceSettings whiteBalance = settings.WhiteBalance;
			buffer.SetGlobalVector(WhiteBalanceId,
				ColorUtils.ColorBalanceToLMSCoeffs(
					whiteBalance.temperature, whiteBalance.tint));
		}

		void ConfigureSplitToning(CommandBuffer buffer, PostFXSettings settings)
		{
			PostFXSettings.SplitToningSettings splitToning = settings.SplitToning;
			Color splitColor = splitToning.shadows;
			splitColor.a = splitToning.balance * 0.01f;
			buffer.SetGlobalColor(SplitToningShadowsId, splitColor);
			buffer.SetGlobalColor(SplitToningHighlightsId, splitToning.highlights);
		}

		void ConfigureChannelMixer(CommandBuffer buffer, PostFXSettings settings)
		{
			PostFXSettings.ChannelMixerSettings channelMixer = settings.ChannelMixer;
			buffer.SetGlobalVector(ChannelMixerRedId, channelMixer.red);
			buffer.SetGlobalVector(ChannelMixerGreenId, channelMixer.green);
			buffer.SetGlobalVector(ChannelMixerBlueId, channelMixer.blue);
		}

		void ConfigureShadowsMidtonesHighlights(
			CommandBuffer buffer, PostFXSettings settings)
		{
			PostFXSettings.ShadowsMidtonesHighlightsSettings smh =
				settings.ShadowsMidtonesHighlights;
			buffer.SetGlobalColor(SmhShadowsId, smh.shadows.linear);
			buffer.SetGlobalColor(SmhMidtonesId, smh.midtones.linear);
			buffer.SetGlobalColor(SmhHighlightsId, smh.highlights.linear);
			buffer.SetGlobalVector(SmhRangeId, new Vector4(
				smh.shadowsStart,
				smh.shadowsEnd,
				smh.highlightsStart,
				smh.highLightsEnd));
		}

		void Render(RenderGraphContext context)
		{
			PostFXSettings settings = stack.Settings;
			CommandBuffer buffer = context.cmd;
			ConfigureColorAdjustments(buffer, settings);
			ConfigureWhiteBalance(buffer, settings);
			ConfigureSplitToning(buffer, settings);
			ConfigureChannelMixer(buffer, settings);
			ConfigureShadowsMidtonesHighlights(buffer, settings);

			int lutHeight = colorLutResolution;
			int lutWidth = lutHeight * lutHeight;
			buffer.SetGlobalVector(ColorGradingLutParametersId, new Vector4(
				lutHeight,
				0.5f / lutWidth, 0.5f / lutHeight,
				lutHeight / (lutHeight - 1f)));

			PostFXSettings.ToneMappingSettings.Mode mode = settings.ToneMapping.mode;
			PostFXStack.Pass pass = PostFXStack.Pass.ColorGradingNone + (int)mode;
			buffer.SetGlobalFloat(ColorGradingLutInLogId,
				stack.BufferSettings.allowHDR && pass != PostFXStack.Pass.ColorGradingNone ?
					1f : 0f);
			stack.Draw(buffer, colorLut, pass);
			buffer.SetGlobalVector(ColorGradingLutParametersId,
				new Vector4(1f / lutWidth, 1f / lutHeight, lutHeight - 1f));
			buffer.SetGlobalTexture(ColorGradingLutId, colorLut);
		}

		public static TextureHandle Record(
			RenderGraph renderGraph,
			PostFXStack stack,
			int colorLutResolution)
		{
			using RenderGraphBuilder builder = renderGraph.AddRenderPass(
				Sampler.name, 
				out ColorLutPass pass, 
				Sampler);
		
			pass.stack = stack;
			pass.colorLutResolution = colorLutResolution;

			int lutHeight = colorLutResolution;
			int lutWidth = lutHeight * lutHeight;
			var desc = new TextureDesc(lutWidth, lutHeight)
			{
				colorFormat = ColorFormat,
				name = "Color LUT"
			};
			pass.colorLut = builder.WriteTexture(renderGraph.CreateTexture(desc));
			builder.SetRenderFunc<ColorLutPass>(static (pass, context) => pass.Render(context));
			return pass.colorLut;
		}
	}
}
