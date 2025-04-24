using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Custom_RP.Runtime
{
    public static class CameraDebugger
    {
        private static readonly int OpacityID = Shader.PropertyToID("_DebugOpacity");

        private static Material material;

        private static bool showTiles;

        private static float opacity = 0.5f;

        public static bool IsActive => showTiles && opacity > 0f;

        private const string PanelName = "Forward+";

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Initialize(Shader shader)
        {
            material = CoreUtils.CreateEngineMaterial(shader);
            DebugManager.instance.GetPanel(PanelName, true).children.Add(
                new DebugUI.FloatField
                {
                    displayName = "Opacity",
                    tooltip = "Opacity of the debug overlay.",
                    min = static () => 0f,
                    max = static () => 1f,
                    getter = static () => opacity,
                    setter = static value => opacity = value
                },
                new DebugUI.BoolField
                {
                    displayName = "Show Tiles",
                    tooltip = "Whether the debug overlay is shown.",
                    getter = static () => showTiles,
                    setter = static value => showTiles = value
                }
            );
        }

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            DebugManager.instance.RemovePanel(PanelName);
        }

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Render(RenderGraphContext context)
        {
            CommandBuffer buffer = context.cmd;
            buffer.SetGlobalFloat(OpacityID, opacity);
            buffer.DrawProcedural(
                Matrix4x4.identity, 
                material, 
                0, 
                MeshTopology.Triangles, 
                3);
        
            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }
    }
}