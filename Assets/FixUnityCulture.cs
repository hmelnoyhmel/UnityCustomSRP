using System.Globalization;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityPatches
{
#if UNITY_EDITOR
	[InitializeOnLoad]
	internal static class FixCultureEditor
	{
		static FixCultureEditor()
		{
			Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
		}
	}
#endif

	internal static class FixCultureRuntime
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void FixCulture()
		{
			Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
		}
	}
}