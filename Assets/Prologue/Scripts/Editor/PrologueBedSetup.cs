// Menu: Tools > Prologue > Make Bed Interactable
// Adds PrologueBed (+ a BoxCollider and a SleepPoint) to the selected object,
// or to the object called "Bed" in the open scene.
// Also makes sure Prologue and FERI_Map are in Build Settings, so the bed can load the FERI map.
// This file must stay inside an "Editor" folder.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Prologue.EditorTools
{
    [InitializeOnLoad]
    public static class PrologueBedSetup
    {
        static readonly string[] Scenes = { "Assets/Scenes/Prologue.unity", "Assets/FERI_Map/FERI_Map.unity" };

        static PrologueBedSetup() { EditorApplication.delayCall += EnsureBuildScenes; }

        static void EnsureBuildScenes()
        {
            var list = EditorBuildSettings.scenes.ToList();
            bool changed = false;
            foreach (var path in Scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) continue;
                if (list.Any(s => s.path == path)) continue;
                list.Add(new EditorBuildSettingsScene(path, true));
                changed = true;
                Debug.Log("Prologue: added " + path + " to Build Settings.");
            }
            if (changed) EditorBuildSettings.scenes = list.ToArray();
        }

        [MenuItem("Tools/Prologue/Make Bed Interactable")]
        public static void MakeBedInteractable()
        {
            EnsureBuildScenes();
            GameObject bed = Selection.activeGameObject;
            if (bed == null || bed.GetComponentInChildren<Renderer>() == null || !bed.scene.IsValid())
                bed = FindInScene("Bed");
            if (bed == null)
            {
                EditorUtility.DisplayDialog("Prologue", "Couldn't find the bed. Select it in the Hierarchy and run this again.", "OK");
                return;
            }
            if (bed.transform.parent != null && bed.GetComponent<MeshFilter>() != null && bed.transform.parent.name.ToLower().Contains("bed"))
                bed = bed.transform.parent.gameObject;

            var comp = bed.GetComponent<PrologueBed>();
            if (comp == null) comp = Undo.AddComponent<PrologueBed>(bed);
            comp.EnsureCollider();
            comp.EnsureSleepPoint();
            EditorUtility.SetDirty(comp);
            EditorSceneManager.MarkSceneDirty(bed.scene);
            Selection.activeGameObject = bed;
            EditorGUIUtility.PingObject(bed);
            Debug.Log("Prologue: '" + bed.name + "' is now interactable. Save the scene (Ctrl+S).", bed);
        }

        const string VeilPath = "Assets/Soundtracks/Veil.wav";

        [MenuItem("Tools/Prologue/Add Veil Soundtrack")]
        public static void AddVeil()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(VeilPath);
            if (clip == null)
            {
                EditorUtility.DisplayDialog("Prologue", "Couldn't find " + VeilPath + ".", "OK");
                return;
            }

            // 9-minute track: stream it from disk instead of decompressing it all into memory, 2D
            var imp = AssetImporter.GetAtPath(VeilPath) as AudioImporter;
            if (imp != null)
            {
                var s = imp.defaultSampleSettings;
                if (s.loadType != AudioClipLoadType.Streaming || s.compressionFormat != AudioCompressionFormat.Vorbis)
                {
                    s.loadType = AudioClipLoadType.Streaming;
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = 0.7f;
                    imp.defaultSampleSettings = s;
                    imp.SaveAndReimport();
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(VeilPath);
                }
            }

            var music = FindInScene("Music");
            if (music == null)
            {
                music = new GameObject("Music");
                Undo.RegisterCreatedObjectUndo(music, "Add Veil Soundtrack");
            }
            var src = music.GetComponent<AudioSource>();
            if (src == null) src = Undo.AddComponent<AudioSource>(music);
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            if (music.GetComponent<PrologueMusic>() == null) Undo.AddComponent<PrologueMusic>(music);

            EditorSceneManager.MarkSceneDirty(music.scene);
            Selection.activeGameObject = music;
            Debug.Log("Prologue: Veil soundtrack added. Save the scene (Ctrl+S).", music);
        }

        static GameObject FindInScene(string name)
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t.gameObject;
            return null;
        }
    }
}
