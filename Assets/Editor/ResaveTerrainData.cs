using System.IO;
using UnityEditor;
using UnityEngine;

// Writes the TerrainData currently loaded in the editor back to disk,
// so a good in-memory copy can replace a corrupted file.
static class ResaveTerrainData
{
    [MenuItem("Tools/Re-save Terrain Data")]
    static void Resave()
    {
        if (Terrain.activeTerrains.Length == 0) Debug.LogWarning("No active terrain in the open scenes.");
        foreach (var t in Terrain.activeTerrains)
        {
            var data = t.terrainData;
            if (data == null) { Debug.LogError($"'{t.name}' has no TerrainData loaded - nothing to save.", t); continue; }
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            string path = AssetDatabase.GetAssetPath(data);
            Debug.Log($"Re-saved {path} ({new FileInfo(path).Length} bytes)", data);
        }
    }
}
