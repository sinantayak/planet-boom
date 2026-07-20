#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class PreLevelSpriteImportOptimizer
{
    private const string CompletedKey = "PlanetBoom.PreLevelSpriteImportOptimized.v1";
    private static readonly string[] Paths =
    {
        "Assets/UI Elements/PreLevelBackground.png",
        "Assets/UI Elements/EmptyBacground2.png",
        "Assets/UI Elements/UseButton.png",
        "Assets/UI Elements/UsedButton.png",
        "Assets/UI Elements/ReadyButtonBackground.png",
        "Assets/UI Elements/BadgeBackground.png"
    };

    private static void OptimizeOnce()
    {
        if (EditorPrefs.GetBool(CompletedKey, false)) return;
        EditorApplication.delayCall += Apply;
    }

    [MenuItem("Tools/Planet Boom/Gameplay/Optimize Pre-Level UI Sprites")]
    public static void Apply()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Apply;
            return;
        }

        int changed = 0;
        foreach (string path in Paths)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
            bool dirty = importer.textureType != TextureImporterType.Sprite ||
                         importer.textureCompression != TextureImporterCompression.Uncompressed ||
                         importer.filterMode != FilterMode.Bilinear || importer.mipmapEnabled ||
                         importer.wrapMode != TextureWrapMode.Clamp;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 2048;
            if (dirty) { importer.SaveAndReimport(); changed++; }
        }
        EditorPrefs.SetBool(CompletedKey, true);
        Debug.Log($"Pre-Level UI sprite import optimized: {changed} asset(s) reimported as uncompressed bilinear UI sprites.");
    }
}
#endif
