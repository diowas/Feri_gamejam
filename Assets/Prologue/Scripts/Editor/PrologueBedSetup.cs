// Menu: Tools > Prologue > Make Bed Interactable
// Adds PrologueBed (+ a BoxCollider and a SleepPoint) to the selected object,
// or to the object called "Bed" in the open scene if nothing suitable is selected.
// You can also just drag PrologueBed onto the bed yourself - it sets itself up the same way.
// This file must stay inside an "Editor" folder.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Prologue.EditorTools
{
    public static class PrologueBedSetup
    {
        [MenuItem("Tools/Prologue/Make Bed Interactable")]
        public static void MakeBedInteractable()
        {
            GameObject bed = Selection.activeGameObject;
            if (bed == null || bed.GetComponentInChildren<Renderer>() == null || !bed.scene.IsValid())
                bed = FindInScene("Bed");
            if (bed == null)
            {
                EditorUtility.DisplayDialog("Prologue", "Couldn't find the bed. Select it in the Hierarchy and run this again.", "OK");
                return;
            }
            // a click on a child mesh (frame / bedding) -> use the bed root
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
