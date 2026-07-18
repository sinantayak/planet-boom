#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(LevelMapScreen))]
public sealed class LevelMapScreenEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        LevelMapScreen screen = (LevelMapScreen)target;
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Sector Layout Authoring", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Choose Editor Preview Sector above, Apply, arrange the persistent objects, then Capture.", MessageType.Info);
        if (GUILayout.Button("Apply Sector Layout"))
        {
            Undo.RegisterFullObjectHierarchyUndo(screen.gameObject, "Apply Sector Map Layout");
            screen.EditorApplyPreviewSector();
            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            SceneView.RepaintAll();
        }
        if (GUILayout.Button("Capture Current Layout To Sector"))
        {
            if (screen.EditorPreviewSector == 1)
                EditorUtility.DisplayDialog("Sector 1 Safety",
                    "Sector 1 is protected from the general capture action. Restore it manually, then use 'Recapture Sector 1 From Current Scene'.", "OK");
            else
            {
                LevelMapSectorVisual sector = screen.EditorFindSector(screen.EditorPreviewSector);
                if (sector?.layout == null)
                    EditorUtility.DisplayDialog("Sector Layout", "The selected sector has no layout asset assigned.", "OK");
                else
                {
                    Undo.RecordObject(sector.layout, "Capture Sector Map Layout");
                    LevelMapLayoutEditorUtility.Capture(screen, sector.layout);
                    sector.layout.completeHierarchyCaptured = true;
                    AssetDatabase.SaveAssets();
                    EditorUtility.DisplayDialog("Sector Layout", $"Sector {screen.EditorPreviewSector} layout captured.", "OK");
                }
            }
        }
        if (GUILayout.Button("Recapture Sector 1 From Current Scene"))
        {
            LevelMapSectorVisual sectorOne = screen.EditorFindSector(1);
            if (sectorOne?.layout == null)
                EditorUtility.DisplayDialog("Sector 1 Layout", "Sector 1 has no layout asset assigned.", "OK");
            else if (EditorUtility.DisplayDialog("Recapture Sector 1",
                "This writes the CURRENT scene arrangement into Sector01_MapLayout. Continue only after manually restoring Sector 1.",
                "Recapture", "Cancel"))
            {
                Undo.RecordObject(sectorOne.layout, "Recapture Sector 1 Map Layout");
                if (LevelMapLayoutEditorUtility.RecoverAndValidateSectorOne(screen, sectorOne.layout, out string report))
                    EditorUtility.DisplayDialog("Sector 1 Layout", "Sector 1 recovered successfully.\n\n" + report, "OK");
                else
                    EditorUtility.DisplayDialog("Sector 1 Layout", "Round-trip validation failed; the asset was not overwritten.\n\n" + report, "OK");
            }
        }
    }
}

public static class LevelMapLayoutEditorUtility
{
    public static void Capture(LevelMapScreen screen, SectorMapLayout layout)
        => CaptureInternal(screen, layout);

    private static void CaptureInternal(LevelMapScreen screen, SectorMapLayout layout)
    {
        RectTransform common = screen.EditorLayoutRoot;
        if (common == null) throw new System.InvalidOperationException("LevelNodes common layout root is missing.");
        layout.referenceResolution = new Vector2(1080f, 1920f);
        layout.nodes = new List<SectorMapNodeLayout>(7);
        IReadOnlyList<LevelMapNodeUI> nodes = screen.EditorNodes;
        for (int i = 0; i < 7; i++)
        {
            LevelMapNodeUI node = i < nodes.Count ? nodes[i] : null;
            if (node == null || node.PathNodeRect == null) throw new System.InvalidOperationException($"LevelNode{i + 1:00} is incomplete.");
            Vector3 worldCenter = node.PathNodeRect.TransformPoint(node.PathNodeRect.rect.center);
            Vector2 localCenter = common.InverseTransformPoint(worldCenter);
            Vector2 rootCenter = common.InverseTransformPoint(node.RootRect.TransformPoint(node.RootRect.rect.center));
            Rect bounds = common.rect;
            Vector2 normalized = new(
                bounds.width > 0f ? Mathf.InverseLerp(bounds.xMin, bounds.xMax, localCenter.x) : .5f,
                bounds.height > 0f ? Mathf.InverseLerp(bounds.yMin, bounds.yMax, localCenter.y) : .5f);
            if (normalized.x < 0f || normalized.x > 1f || normalized.y < 0f || normalized.y > 1f)
                Debug.LogWarning($"Captured PathNode {i + 1} center is outside LevelNodes: normalized={normalized}. Move it inside before final capture.", node);
            RectTransform island = node.IslandRect;
            RectTransform stars = node.StarsRect;
            RectTransform reward = node.RewardBadgeRect;
            layout.nodes.Add(new SectorMapNodeLayout
            {
                normalizedRootCenter = Normalize(bounds, rootCenter),
                normalizedPathPosition = normalized,
                pathNodeSize = node.PathNodeRect.sizeDelta,
                islandPosition = RelativeCenter(common, localCenter, island),
                islandSize = island != null ? island.sizeDelta : Vector2.zero,
                islandRotation = island != null ? island.localEulerAngles.z : 0f,
                islandScale = island != null ? island.localScale : Vector3.one,
                starsPosition = RelativeCenter(common, localCenter, stars),
                rewardBadgePosition = RelativeCenter(common, localCenter, reward),
                rewardBadgeSize = reward != null ? reward.sizeDelta : Vector2.zero,
                islandRect = Snapshot(island),
                starsRect = Snapshot(stars),
                rewardBadgeRect = Snapshot(reward),
                rootRect = Snapshot(node.RootRect),
                pathRect = Snapshot(node.PathNodeRect)
            });
        }
        if (screen.EditorSectorBackground != null)
        {
            RectTransform background = screen.EditorSectorBackground.rectTransform;
            layout.backgroundPosition = background.anchoredPosition;
            layout.backgroundSize = background.sizeDelta;
            layout.backgroundScale = background.localScale;
        }
        EditorUtility.SetDirty(layout);
    }

    public static bool RecoverAndValidateSectorOne(LevelMapScreen screen, SectorMapLayout layout, out string report)
    {
        string backup = EditorJsonUtility.ToJson(layout);
        Capture(screen, layout);
        layout.completeHierarchyCaptured = false;
        RectTransformLayout nodeOneBefore = layout.nodes[0].rootRect;
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Validate Sector 1 Layout Round Trip");
        Undo.RegisterFullObjectHierarchyUndo(screen.gameObject, "Validate Sector 1 Layout Round Trip");
        screen.EditorApplySectorForValidation(1);
        var roundTrip = ScriptableObject.CreateInstance<SectorMapLayout>();
        Capture(screen, roundTrip);
        bool matches = Compare(layout, roundTrip, 0.01f, out report);
        int correctIslandSprites = CountSectorIslandSprites(screen, 1);
        if (correctIslandSprites != 7)
        {
            matches = false;
            report += $"\nIsland sprite validation failed: {correctIslandSprites}/7 use Sector1-Land.";
        }
        else
        {
            RectTransformLayout nodeOneAfter = roundTrip.nodes[0].rootRect;
            report += $"\nLevelNode01 before: anchors {nodeOneBefore.anchorMin}/{nodeOneBefore.anchorMax}, pos {nodeOneBefore.anchoredPosition}." +
                      $"\nLevelNode01 after: anchors {nodeOneAfter.anchorMin}/{nodeOneAfter.anchorMax}, pos {nodeOneAfter.anchoredPosition}." +
                      "\nIsland sprites: 7/7 Sector1-Land.";
        }
        Object.DestroyImmediate(roundTrip);
        if (!matches)
        {
            EditorJsonUtility.FromJsonOverwrite(backup, layout);
            EditorUtility.SetDirty(layout);
            Undo.RevertAllDownToGroup(undoGroup);
            return false;
        }
        Undo.CollapseUndoOperations(undoGroup);
        layout.sectorNumber = 1;
        layout.completeHierarchyCaptured = true;
        EditorUtility.SetDirty(layout);
        AssetDatabase.SaveAssets();
        return true;
    }

    private static RectTransformLayout Snapshot(RectTransform rect)
    {
        if (rect == null) return null;
        return new RectTransformLayout
        {
            anchorMin = rect.anchorMin, anchorMax = rect.anchorMax, pivot = rect.pivot,
            anchoredPosition = rect.anchoredPosition, sizeDelta = rect.sizeDelta,
            rotation = rect.localEulerAngles.z, scale = rect.localScale
        };
    }

    private static bool Compare(SectorMapLayout expected, SectorMapLayout actual, float tolerance, out string report)
    {
        var differences = new List<string>();
        if (expected.nodes == null || actual.nodes == null || expected.nodes.Count != actual.nodes.Count)
            differences.Add("Node count differs.");
        else for (int i = 0; i < expected.nodes.Count; i++)
        {
            SectorMapNodeLayout a = expected.nodes[i], b = actual.nodes[i];
            if (!Near(a.normalizedRootCenter, b.normalizedRootCenter, tolerance)) differences.Add($"Node {i + 1} root center differs.");
            if (!Near(a.normalizedPathPosition, b.normalizedPathPosition, tolerance)) differences.Add($"Node {i + 1} PathNode center differs.");
            if (!Near(a.pathNodeSize, b.pathNodeSize, tolerance)) differences.Add($"Node {i + 1} PathNode size differs.");
            if (!Near(a.islandPosition, b.islandPosition, tolerance)) differences.Add($"Node {i + 1} Island visual offset differs.");
            if (!Near(a.starsPosition, b.starsPosition, tolerance)) differences.Add($"Node {i + 1} Stars visual offset differs.");
            if (!Near(a.rewardBadgePosition, b.rewardBadgePosition, tolerance)) differences.Add($"Node {i + 1} RewardBadge visual offset differs.");
            CompareRect(a.islandRect, b.islandRect, tolerance, $"Node {i + 1} Island", differences);
            CompareRect(a.starsRect, b.starsRect, tolerance, $"Node {i + 1} Stars", differences);
            CompareRect(a.rewardBadgeRect, b.rewardBadgeRect, tolerance, $"Node {i + 1} RewardBadge", differences);
            CompareRect(a.rootRect, b.rootRect, tolerance, $"Node {i + 1} Root", differences);
            CompareRect(a.pathRect, b.pathRect, tolerance, $"Node {i + 1} PathNode", differences);
        }
        if (!Near(expected.backgroundPosition, actual.backgroundPosition, tolerance) ||
            !Near(expected.backgroundSize, actual.backgroundSize, tolerance) ||
            !Near(expected.backgroundScale, actual.backgroundScale, tolerance)) differences.Add("Background crop differs.");
        report = differences.Count == 0 ? $"Round-trip passed for 7 nodes (tolerance {tolerance})." : string.Join("\n", differences);
        return differences.Count == 0;
    }

    private static void CompareRect(RectTransformLayout a, RectTransformLayout b, float tolerance, string label, List<string> differences)
    {
        if (a == null || b == null) { if (a != b) differences.Add(label + " snapshot missing."); return; }
        if (!Near(a.anchorMin,b.anchorMin,tolerance) || !Near(a.anchorMax,b.anchorMax,tolerance) ||
            !Near(a.pivot,b.pivot,tolerance) || !Near(a.anchoredPosition,b.anchoredPosition,tolerance) ||
            !Near(a.sizeDelta,b.sizeDelta,tolerance) || Mathf.Abs(Mathf.DeltaAngle(a.rotation,b.rotation)) > tolerance ||
            !Near(a.scale,b.scale,tolerance)) differences.Add(label + " differs.");
    }

    private static bool Near(Vector2 a, Vector2 b, float tolerance) => (a - b).sqrMagnitude <= tolerance * tolerance;
    private static bool Near(Vector3 a, Vector3 b, float tolerance) => (a - b).sqrMagnitude <= tolerance * tolerance;

    private static Vector2 Normalize(Rect bounds, Vector2 point) => new(
        bounds.width > 0f ? Mathf.InverseLerp(bounds.xMin, bounds.xMax, point.x) : .5f,
        bounds.height > 0f ? Mathf.InverseLerp(bounds.yMin, bounds.yMax, point.y) : .5f);

    private static int CountSectorIslandSprites(LevelMapScreen screen, int sectorNumber)
    {
        Sprite expected = screen.EditorFindSector(sectorNumber)?.island;
        if (expected == null) return 0;
        int count = 0;
        foreach (LevelMapNodeUI node in screen.EditorNodes)
            if (node?.IslandRect != null && node.IslandRect.GetComponent<UnityEngine.UI.Image>()?.sprite == expected) count++;
        return count;
    }

    private static Vector2 RelativeCenter(RectTransform common, Vector2 pathCenter, RectTransform item)
    {
        if (item == null) return Vector2.zero;
        Vector2 itemCenter = common.InverseTransformPoint(item.TransformPoint(item.rect.center));
        return itemCenter - pathCenter;
    }
}
#endif
