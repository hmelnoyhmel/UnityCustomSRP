#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityPatches
{
	[InitializeOnLoad]
	internal static class SceneAutoSave
	{
		private static DateTime nextSaveTime;

		// Static constructor that gets called when unity fires up.
		static SceneAutoSave()
		{
			EditorApplication.playModeStateChanged -= OnEditorApplicationOnplayModeStateChanged;
			EditorApplication.playModeStateChanged += OnEditorApplicationOnplayModeStateChanged;

			// Also, every five minutes.
			nextSaveTime = DateTime.Now.AddMinutes(5);
			EditorApplication.update -= Update;
			EditorApplication.update += Update;
			Debug.Log("Added Auto Scene Save callback.");
		}

		private static void OnEditorApplicationOnplayModeStateChanged(PlayModeStateChange state)
		{
			// If we're about to run the scene...
			if (!EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying) return;

			// Save the scene and the assets.
			Debug.Log("Auto-saving all open scenes... " + state);
			EditorSceneManager.SaveOpenScenes();
			AssetDatabase.SaveAssets();
		}

		private static void Update()
		{
			if (nextSaveTime > DateTime.Now) return;

			nextSaveTime = nextSaveTime.AddMinutes(5);

			if (EditorApplication.isPlaying) return;

			Debug.Log("AutoSave Scenes: " + DateTime.Now.ToShortTimeString());
			EditorSceneManager.SaveOpenScenes();
			AssetDatabase.SaveAssets();
		}
	}
}
#endif