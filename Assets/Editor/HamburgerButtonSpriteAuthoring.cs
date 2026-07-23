#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Swaps the GameScene HamburgerButton's Image sprite to the new
// Assets/Buttons/EvolutionButton.png art. Additive and re-run-safe: only the
// sprite reference changes — the GameObject name, RectTransform, Button
// wiring, sounds and every other authored value stay untouched. The scene is
// marked dirty but never saved; if the texture has not been imported as a
// Sprite yet, its importer is fixed first.
public static class HamburgerButtonSpriteAuthoring
{
    private const string TexturePath = "Assets/Buttons/EvolutionButton.png";
    private const string ButtonName = "HamburgerButton";

    [MenuItem("Tools/Planet Boom/UI/Use Evolution Sprite On Hamburger Button")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "GameScene")
        {
            Debug.LogError($"Open GameScene first — the {ButtonName} lives there (active scene is '{scene.name}').");
            return;
        }

        Sprite sprite = LoadEvolutionSprite();
        if (sprite == null)
            return;

        Image target = FindButtonImage(scene);
        if (target == null)
        {
            Debug.LogError($"No '{ButtonName}' GameObject with an Image was found in GameScene.");
            return;
        }

        if (target.sprite == sprite)
        {
            Debug.Log($"{ButtonName} already uses {sprite.name} — nothing to change.", target);
            return;
        }

        Undo.RecordObject(target, "Use Evolution Sprite On Hamburger Button");
        target.sprite = sprite;
        EditorUtility.SetDirty(target);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"{ButtonName} sprite swapped to {sprite.name}. Review and save the scene yourself.", target);
    }

    private static Image FindButtonImage(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.gameObject.name == ButtonName)
                    return image;
            }
        }
        return null;
    }

    private static Sprite LoadEvolutionSprite()
    {
        var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"Texture not found at {TexturePath} — make sure Unity has imported the file.");
            return null;
        }

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
        if (sprite == null)
            Debug.LogError($"{TexturePath} imported, but no Sprite sub-asset was produced.");
        return sprite;
    }
}
#endif
