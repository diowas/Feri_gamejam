using UnityEditor;
using UnityEngine;

namespace FeriMap.EditorTools
{

    public class FeriTextureImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            string a = assetPath.Replace('\\', '/');
            if (!a.Contains("/FERI_Map/Textures/") && !a.Contains("/FERI_Map/Props/Textures/")) return;
            Configure((TextureImporter)assetImporter);
        }

        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("/FERI_Map/Props/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.addCollider = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.weldVertices = false;
            mi.bakeAxisConversion = true;
        }

        public static bool Configure(TextureImporter ti)
        {
            bool changed = false;
            if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; changed = true; }
            if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
            if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; changed = true; }
            if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; changed = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
            if (ti.maxTextureSize < 1024) { ti.maxTextureSize = 1024; changed = true; }
            return changed;
        }
    }
}
