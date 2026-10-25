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
