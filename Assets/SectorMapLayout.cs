using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class SectorMapNodeLayout
{
    [Tooltip("LevelNode root visual center normalized inside LevelNodes (0..1).")]
    public Vector2 normalizedRootCenter = new(.5f, .5f);
    [Tooltip("PathNode center normalized inside the shared LevelNodes rect (0..1).")]
    public Vector2 normalizedPathPosition = new(.5f, .5f);
    public Vector2 pathNodeSize = new(92f, 92f);
    public Vector2 islandPosition;
    public Vector2 islandSize = new(240f, 150f);
    public float islandRotation;
    public Vector3 islandScale = Vector3.one;
    public Vector2 starsPosition;
    public Vector2 rewardBadgePosition;
    [Tooltip("Legacy/default value only. Full layout snapshots preserve the authored RectTransform size exactly.")]
    public Vector2 rewardBadgeSize = new(90f, 90f);
    public RectTransformLayout islandRect;
    public RectTransformLayout starsRect;
    public RectTransformLayout rewardBadgeRect;
    public RectTransformLayout rootRect;
    public RectTransformLayout pathRect;
}

[Serializable]
public sealed class RectTransformLayout
{
    public Vector2 anchorMin = new(.5f, .5f);
    public Vector2 anchorMax = new(.5f, .5f);
    public Vector2 pivot = new(.5f, .5f);
    public Vector2 anchoredPosition;
    public Vector2 sizeDelta;
    public float rotation;
    public Vector3 scale = Vector3.one;
}

[CreateAssetMenu(menuName = "Planet Boom/Level Map/Sector Map Layout", fileName = "Sector01_MapLayout")]
public sealed class SectorMapLayout : ScriptableObject
{
    [Range(1, 10)] public int sectorNumber = 1;
    [Tooltip("Informational reference canvas used while authoring.")]
    public Vector2 referenceResolution = new(1080f, 1920f);
    public List<SectorMapNodeLayout> nodes = new();
    [Tooltip("True only after the complete LevelNode root/child hierarchy has passed Capture -> Apply round-trip validation.")]
    public bool completeHierarchyCaptured;

    [Header("Background Crop")]
    public Vector2 backgroundPosition;
    public Vector2 backgroundSize = new(2048f, 2048f);
    public Vector3 backgroundScale = Vector3.one;

    public bool HasCompleteNodeLayout => nodes != null && nodes.Count == 7;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (nodes == null) return;
        for (int i = 0; i < nodes.Count; i++)
        {
            SectorMapNodeLayout node = nodes[i];
            if (node == null) continue;
            Vector2 position = node.normalizedPathPosition;
            if (position.x < 0f || position.x > 1f || position.y < 0f || position.y > 1f)
                Debug.LogWarning($"{name}: node {i + 1} normalized PathNode center {position} is outside 0..1. Runtime will clamp it; restore and recapture this sector.", this);
            Vector2 root = node.normalizedRootCenter;
            if (root.x < 0f || root.x > 1f || root.y < 0f || root.y > 1f)
                Debug.LogWarning($"{name}: node {i + 1} normalized LevelNode root center {root} is outside 0..1. Runtime will clamp it; restore and recapture this sector.", this);
        }
    }
#endif
}
