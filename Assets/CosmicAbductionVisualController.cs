using System.Collections;
using UnityEngine;

// Presentation-only bridge between GameManager's reservation and completion.
// The reserved planet already has physics/collisions disabled, so this class
// can animate its Transform without competing with merge or orbit systems.
public sealed class CosmicAbductionVisualController : MonoBehaviour
{
    public static CosmicAbductionVisualController Instance { get; private set; }

    [Header("UFO Visual")]
    [SerializeField] private Sprite ufoSprite;
    [SerializeField] private Sprite beamSprite;
    [SerializeField] private float ufoScale = 1f;
    [SerializeField] private Vector2 entryViewportPosition = new Vector2(1.15f, 1.1f);
    [SerializeField] private float rotationOffset;

    [Header("Sequence Timing")]
    [SerializeField] [Min(0.05f)] private float ufoTravelDuration = 0.65f;
    [SerializeField] private float hoverHeightAboveTarget = 2f;
    [SerializeField] [Min(0f)] private float beamDuration = 0.25f;
    [SerializeField] [Min(0.05f)] private float planetPullDuration = 0.55f;
    [SerializeField] [Min(0.05f)] private float exitDuration = 0.55f;

    [Header("Beam Fallback")]
    [SerializeField] private Color beamColor = new Color(0.35f, 0.95f, 1f, 0.7f);
    [SerializeField] [Min(0.01f)] private float fallbackBeamWidth = 0.35f;

    private Coroutine sequenceRoutine;
    private GameObject ufoObject;
    private GameObject beamObject;
    private SpriteRenderer beamRenderer;
    private LineRenderer fallbackBeam;
    private Material fallbackBeamMaterial;
    private Planet reservedTarget;

    public bool CanBegin => sequenceRoutine == null && isActiveAndEnabled;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDisable()
    {
        if (sequenceRoutine != null)
            StopCoroutine(sequenceRoutine);
        sequenceRoutine = null;
        CompleteReservedTargetIfNeeded();
        CleanupVisuals();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool TryPlay(Planet target)
    {
        if (!CanBegin || target == null || !IsGameplayPlaying())
            return false;

        reservedTarget = target;
        sequenceRoutine = StartCoroutine(PlaySequence());
        return true;
    }

    private IEnumerator PlaySequence()
    {
        bool completed = false;
        try
        {
            CreateUfo();
            AudioManager.Instance?.PlayCosmicAbductionSequence();

            Vector3 entry = EntryWorldPosition(reservedTarget);
            Vector3 hover = HoverPosition(reservedTarget);
            ufoObject.transform.position = entry;
            yield return MoveUfo(entry, hover, ufoTravelDuration);
            if (!CanContinue()) yield break;

            CreateBeam();
            float elapsed = 0f;
            while (elapsed < beamDuration && CanContinue())
            {
                elapsed += Time.deltaTime;
                UpdateBeam(Mathf.Clamp01(elapsed / Mathf.Max(0.01f, beamDuration)));
                yield return null;
            }
            if (!CanContinue()) yield break;

            Vector3 planetStart = reservedTarget.transform.position;
            Vector3 scaleStart = reservedTarget.transform.localScale;
            SpriteRenderer planetRenderer = reservedTarget.GetComponent<SpriteRenderer>();
            Color colorStart = planetRenderer != null ? planetRenderer.color : Color.white;
            elapsed = 0f;
            while (elapsed < planetPullDuration && CanContinue())
            {
                elapsed += Time.deltaTime;
                float t = Smooth01(elapsed / Mathf.Max(0.05f, planetPullDuration));
                reservedTarget.transform.position = Vector3.Lerp(planetStart, ufoObject.transform.position, t);
                reservedTarget.transform.localScale = Vector3.Lerp(scaleStart, scaleStart * 0.2f, t);
                if (planetRenderer != null)
                    planetRenderer.color = new Color(colorStart.r, colorStart.g, colorStart.b, 1f - t);
                UpdateBeam(1f - t);
                yield return null;
            }

            if (reservedTarget != null)
            {
                GameManager.Instance?.CompleteCosmicAbduction(reservedTarget);
                reservedTarget = null;
                completed = true;
            }
            DestroyBeam();

            if (ufoObject != null && IsGameplayPlaying())
                yield return MoveUfo(ufoObject.transform.position, entry, exitDuration);
        }
        finally
        {
            if (!completed)
                CompleteReservedTargetIfNeeded();
            CleanupVisuals();
            sequenceRoutine = null;
        }
    }

    private IEnumerator MoveUfo(Vector3 start, Vector3 end, float duration)
    {
        float elapsed = 0f;
        duration = Mathf.Max(0.05f, duration);
        while (elapsed < duration && ufoObject != null && IsGameplayPlaying())
        {
            elapsed += Time.deltaTime;
            ufoObject.transform.position = Vector3.Lerp(start, end, Smooth01(elapsed / duration));
            yield return null;
        }
        if (ufoObject != null && IsGameplayPlaying())
            ufoObject.transform.position = end;
    }

    private void CreateUfo()
    {
        ufoObject = new GameObject("CosmicAbduction_UFO");
        ufoObject.transform.SetParent(transform, false);
        ufoObject.transform.localScale = Vector3.one * Mathf.Max(0.001f, ufoScale);
        ufoObject.transform.rotation = Quaternion.Euler(0f, 0f, rotationOffset);
        SpriteRenderer renderer = ufoObject.AddComponent<SpriteRenderer>();
        renderer.sprite = ufoSprite;
        renderer.sortingOrder = 250;
    }

    private void CreateBeam()
    {
        beamObject = new GameObject("CosmicAbduction_Beam");
        beamObject.transform.SetParent(transform, false);
        if (beamSprite != null)
        {
            beamRenderer = beamObject.AddComponent<SpriteRenderer>();
            beamRenderer.sprite = beamSprite;
            beamRenderer.color = beamColor;
            beamRenderer.sortingOrder = 240;
        }
        else
        {
            fallbackBeam = beamObject.AddComponent<LineRenderer>();
            fallbackBeam.useWorldSpace = true;
            fallbackBeam.positionCount = 2;
            fallbackBeamMaterial = new Material(Shader.Find("Sprites/Default"));
            fallbackBeam.material = fallbackBeamMaterial;
            fallbackBeam.startColor = beamColor;
            fallbackBeam.endColor = new Color(beamColor.r, beamColor.g, beamColor.b, 0.08f);
            fallbackBeam.numCapVertices = 6;
        }
        UpdateBeam(0f);
    }

    private void UpdateBeam(float intensity)
    {
        if (beamObject == null || ufoObject == null || reservedTarget == null)
            return;
        Vector3 top = ufoObject.transform.position;
        Vector3 bottom = reservedTarget.transform.position;
        intensity = Mathf.Clamp01(intensity);
        if (beamRenderer != null)
        {
            Vector3 delta = bottom - top;
            beamObject.transform.position = (top + bottom) * 0.5f;
            beamObject.transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f);
            float spriteHeight = Mathf.Max(0.001f, beamRenderer.sprite.bounds.size.y);
            beamObject.transform.localScale = new Vector3(intensity, delta.magnitude / spriteHeight, 1f);
            beamRenderer.color = new Color(beamColor.r, beamColor.g, beamColor.b, beamColor.a * intensity);
        }
        else if (fallbackBeam != null)
        {
            fallbackBeam.SetPosition(0, top);
            fallbackBeam.SetPosition(1, bottom);
            fallbackBeam.startWidth = fallbackBeamWidth * intensity;
            fallbackBeam.endWidth = fallbackBeamWidth * 1.8f * intensity;
        }
    }

    private Vector3 EntryWorldPosition(Planet target)
    {
        Camera camera = Camera.main;
        Vector3 targetPosition = target.transform.position;
        if (camera == null)
            return targetPosition + new Vector3(8f, 6f, 0f);
        float depth = Mathf.Abs(targetPosition.z - camera.transform.position.z);
        Vector3 world = camera.ViewportToWorldPoint(
            new Vector3(entryViewportPosition.x, entryViewportPosition.y, depth));
        world.z = targetPosition.z;
        return world;
    }

    private Vector3 HoverPosition(Planet target) =>
        target.transform.position + Vector3.up * hoverHeightAboveTarget;

    private bool CanContinue() => reservedTarget != null && ufoObject != null && IsGameplayPlaying();
    private static bool IsGameplayPlaying() => GameManager.Instance != null &&
        GameManager.Instance.State == GameManager.GameState.Playing;
    private static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    private void CompleteReservedTargetIfNeeded()
    {
        if (reservedTarget != null)
            GameManager.Instance?.CompleteCosmicAbduction(reservedTarget);
        reservedTarget = null;
    }

    private void DestroyBeam()
    {
        if (beamObject != null)
            Destroy(beamObject);
        beamObject = null;
        beamRenderer = null;
        fallbackBeam = null;
        if (fallbackBeamMaterial != null)
            Destroy(fallbackBeamMaterial);
        fallbackBeamMaterial = null;
    }

    private void CleanupVisuals()
    {
        DestroyBeam();
        if (ufoObject != null)
            Destroy(ufoObject);
        ufoObject = null;
    }
}
