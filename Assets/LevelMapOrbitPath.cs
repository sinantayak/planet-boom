using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Lightweight dotted UI path. Control points are the persistent node RectTransforms.
[ExecuteAlways, RequireComponent(typeof(CanvasRenderer))]
public sealed class LevelMapOrbitPath : MaskableGraphic
{
    [SerializeField] private List<RectTransform> controlPoints = new();
    [SerializeField, Min(2)] private int samplesPerSegment = 12;
    [SerializeField, Min(1f)] private float dotSize = 9f;
    [SerializeField, Min(1f)] private float dotSpacing = 24f;
    [SerializeField] private Color lockedColor = new(1f, 1f, 1f, .18f);
    [SerializeField] private Color completedColor = new(1f, .86f, .25f, .9f);
    [SerializeField] private int completedSegments;
    private readonly List<Vector3> cachedControlPointPositions = new();

    private void LateUpdate()
    {
        bool changed = cachedControlPointPositions.Count != controlPoints.Count;
        if (!changed)
        {
            for (int i = 0; i < controlPoints.Count; i++)
            {
                Vector3 position = controlPoints[i] != null ? controlPoints[i].position : Vector3.zero;
                if (cachedControlPointPositions[i] != position) { changed = true; break; }
            }
        }
        if (!changed) return;
        cachedControlPointPositions.Clear();
        for (int i = 0; i < controlPoints.Count; i++)
            cachedControlPointPositions.Add(controlPoints[i] != null ? controlPoints[i].position : Vector3.zero);
        SetVerticesDirty();
    }

    public void SetCompletedSegments(int count)
    {
        completedSegments = Mathf.Max(0, count);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (controlPoints == null || controlPoints.Count < 2) return;
        for (int segment = 0; segment < controlPoints.Count - 1; segment++)
        {
            if (controlPoints[segment] == null || controlPoints[segment + 1] == null) continue;
            Vector2 start = transform.InverseTransformPoint(controlPoints[segment].position);
            Vector2 end = transform.InverseTransformPoint(controlPoints[segment + 1].position);
            float bend = (segment % 2 == 0 ? 1f : -1f) * Mathf.Min(90f, Vector2.Distance(start, end) * .22f);
            Vector2 middle = (start + end) * .5f + Vector2.right * bend;
            Vector2 previous = start;
            float carried = 0f;
            int samples = Mathf.Max(2, samplesPerSegment);
            for (int sample = 1; sample <= samples; sample++)
            {
                float t = sample / (float)samples;
                Vector2 point = Quadratic(start, middle, end, t);
                float distance = Vector2.Distance(previous, point);
                Vector2 direction = distance > 0f ? (point - previous) / distance : Vector2.zero;
                while (carried + distance >= dotSpacing)
                {
                    float step = dotSpacing - carried;
                    previous += direction * step;
                    distance -= step;
                    AddDot(vh, previous, segment < completedSegments ? completedColor : lockedColor);
                    carried = 0f;
                }
                carried += distance;
                previous = point;
            }
        }
    }

    private static Vector2 Quadratic(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    private void AddDot(VertexHelper vh, Vector2 center, Color tint)
    {
        float half = dotSize * .5f;
        int index = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = tint;
        vertex.position = center + new Vector2(-half, -half); vh.AddVert(vertex);
        vertex.position = center + new Vector2(-half, half); vh.AddVert(vertex);
        vertex.position = center + new Vector2(half, half); vh.AddVert(vertex);
        vertex.position = center + new Vector2(half, -half); vh.AddVert(vertex);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }
}
