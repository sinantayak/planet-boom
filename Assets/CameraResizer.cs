using UnityEngine;

// Width-locked orthographic camera fit. The play field (boundary ring, orbits,
// sling) is authored against a 1080x1920 (9:16) portrait view: at that aspect
// the camera's designed orthographic size shows the whole board with margin.
// Orthographic size is HALF-HEIGHT, so on longer phones (19.5:9, 20:9) the
// same size yields a NARROWER view and the boundary gets cropped at the sides.
//
// Fix: treat the designed size as "size at the reference aspect" and grow it
// on narrower screens so the visible half-WIDTH never shrinks below designed.
// Extra screen goes to the top/bottom as empty space — nothing is ever
// stretched, so circles stay circles and 2D physics is untouched (the world
// never changes, only how much of it the camera frames).
//
// Lives on the Main Camera GameObject. Zero-config: it captures the size you
// authored in the Inspector at Awake and scales from there.
[RequireComponent(typeof(Camera))]
public class CameraResizer : MonoBehaviour
{
    [Header("Reference Layout")]
    // The aspect the scene was designed at — matches the Canvas Scaler's
    // reference resolution so world and UI agree on what "designed" means.
    [SerializeField] private float referenceWidth = 1080f;
    [SerializeField] private float referenceHeight = 1920f;

    private Camera cam;

    // The Inspector-authored orthographic size, i.e. the intended view at the
    // reference aspect. Captured once so repeated Apply() calls don't compound.
    private float designedOrthographicSize;

    private int lastScreenWidth;
    private int lastScreenHeight;

    void Awake()
    {
        cam = GetComponent<Camera>();
        designedOrthographicSize = cam.orthographicSize;
        Apply();
    }

    // Cheap two-int comparison per frame; covers orientation changes,
    // foldables, split-screen resizes, and editor Game view fiddling.
    void Update()
    {
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
        {
            Apply();
        }
    }

    private void Apply()
    {
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        if (Screen.height == 0)
            return;

        float referenceAspect = referenceWidth / referenceHeight;
        float currentAspect = (float)Screen.width / Screen.height;

        // Narrower than designed (taller phone): scale the size up by exactly
        // the aspect deficit, which restores the designed half-width.
        // Wider than designed (tablets, landscape): the designed size already
        // shows MORE width than needed — keep it, gaining side margin instead
        // of cropping top/bottom.
        cam.orthographicSize = currentAspect < referenceAspect
            ? designedOrthographicSize * (referenceAspect / currentAspect)
            : designedOrthographicSize;

        Debug.Log($"CameraResizer: {Screen.width}x{Screen.height} (aspect {currentAspect:F3}) -> " +
                  $"orthographicSize {cam.orthographicSize:F3} (designed {designedOrthographicSize:F2} " +
                  $"at {referenceAspect:F3}).");
    }
}
