using UnityEngine;

//  Drives the visual preview of the player's new gravity orientation.
//  This component is added at runtime by GravityManipulator to the instantiated hologram prefab.
public class GravityHologram : MonoBehaviour {
    #region Inspector Fields

    [Tooltip("Angular frequency of the idle bob / alpha pulse animation.")]
    [SerializeField] private float pulseSpeed = 2.0f;

    [Tooltip("Amplitude of the vertical bob in world units.")]
    [SerializeField] private float bobAmplitude = 0.08f;

    [Tooltip("How quickly the hologram lerps / slreps to its target transform.")]
    [SerializeField] private float followSpeed = 12f;

    #endregion

    #region Private State

    private Vector3    _targetPosition;
    private Quaternion _targetRotation;
    private float      _phaseOffset;

    // Legacy TextMesh fallback for an optional direction label child.
    private TextMesh   _label;

    // Cached renderers and property block — avoids per-frame material allocations.
    private Renderer[]          _renderers;
    private MaterialPropertyBlock _mpb;

    // Shader property IDs — resolved once to avoid per-frame string lookups.
    private static readonly int StandardColorID = Shader.PropertyToID("_Color");
    private static readonly int URPColorID      = Shader.PropertyToID("_BaseColor");

    #endregion

    #region Unity Lifecycle

    private void Awake() {
        _phaseOffset = Random.Range(0f, Mathf.PI * 2f);   // Randomise phase per instance.
        _renderers   = GetComponentsInChildren<Renderer>();
        _mpb         = new MaterialPropertyBlock();

        // Try to find a legacy TextMesh child named "DirectionLabel".
        Transform labelTransform = transform.Find("DirectionLabel");
        if (labelTransform != null) {
            _label = labelTransform.GetComponent<TextMesh>();
        }
    }

    private void Update() {
        AnimatePulse();
        SmoothRotate();
    }

    #endregion

    #region Public API

    //  Activates the hologram at worldPosition with worldRotation.
    //  Snaps to position on first show to prevent the hologram gliding in from the world origin.
    public void Show(Vector3 worldPosition, Quaternion worldRotation, string directionLabel = "") {
        _targetPosition = worldPosition;
        _targetRotation = worldRotation;

        // Snap on first activation so there is no slide-in from origin.
        if (!gameObject.activeSelf) {
            transform.SetPositionAndRotation(worldPosition, worldRotation);
        }

        gameObject.SetActive(true);

        if (_label != null) {
            _label.text = directionLabel;
        }
    }

    //  Deactivates the hologram.
    public void Hide() {
        gameObject.SetActive(false);
    }

    #endregion

    #region Private Animation

    //  Bobs the hologram along its local-up axis and pulses renderer alpha to produce a living, holographic feel.
    private void AnimatePulse() {
        float phase    = Time.time * pulseSpeed + _phaseOffset;
        float sinValue = Mathf.Sin(phase);   // –1 … +1

        // Bob: absolute-value of sine keeps the hologram always above its base position.
        float bob             = Mathf.Abs(sinValue) * bobAmplitude;
        Vector3 bobgedTarget  = _targetPosition + transform.up * bob;

        transform.position = Vector3.Lerp(transform.position, bobgedTarget, Time.deltaTime * followSpeed);

        // Alpha pulse: map sine range [–1, 1] to alpha range [0.25, 0.55].
        float alpha = Mathf.Lerp(0.25f, 0.55f, (sinValue + 1f) * 0.5f);

        foreach (Renderer r in _renderers) {
            r.GetPropertyBlock(_mpb);

            // Support both standard (Built-in) and URP Lit shader property names.
            if (r.sharedMaterial.HasProperty(StandardColorID)) {
                Color c = _mpb.GetColor(StandardColorID);
                c.a = alpha;
                _mpb.SetColor(StandardColorID, c);
            }
            else if (r.sharedMaterial.HasProperty(URPColorID)) {
                Color c = _mpb.GetColor(URPColorID);
                c.a = alpha;
                _mpb.SetColor(URPColorID, c);
            }

            r.SetPropertyBlock(_mpb);
        }
    }

    //  Smoothly slews the hologram's rotation toward its target orientation.
    private void SmoothRotate() {
        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, Time.deltaTime * followSpeed);
    }

    #endregion
}
