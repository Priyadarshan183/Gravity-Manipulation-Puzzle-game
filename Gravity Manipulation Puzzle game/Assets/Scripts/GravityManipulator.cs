using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;


//  Lets the player preview and commit a new gravity direction using arrow keys (preview)
//  and Enter (confirm) or Escape (cancel).

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(ThirdPersonController))]
public class GravityManipulator : MonoBehaviour {
    #region Inspector Fields

    [Header("References")]
    [Tooltip("Prefab used to visualise the gravity preview. Must have renderers set up for transparency.")]
    [SerializeField] private GameObject hologramPrefab;

    [Tooltip("The Cinemachine camera target transform (same one assigned in ThirdPersonController).")]
    [SerializeField] private Transform cameraTarget;

    [Header("Gravity")]
    [Tooltip("Magnitude applied to whichever gravity axis is selected.")]
    [SerializeField] private float gravityMagnitude = 9.81f;

    [Header("Hologram Placement")]
    [Tooltip("Distance from the character's head the hologram is placed along the selected gravity direction.")]
    [SerializeField] private float hologramDistance = 1f;

    [Tooltip("Extra clearance added beyond the capsule radius to prevent the hologram clipping the character.")]
    [SerializeField] private float hologramBodyClearance = 0.5f;

    [Header("Reorientation")]
    [Tooltip("Duration in seconds of the smooth character rotation when gravity is committed.")]
    [SerializeField] private float reorientDuration = 0.5f;

    [Tooltip("Max seconds to wait for the character to be grounded after reorientation before giving up.")]
    [SerializeField] private float reorientGroundedTimeout = 3f;

    [Tooltip("Settling delay (seconds) after landing before player control is restored.")]
    [SerializeField] private float reorientSettleDelay = 0.1f;

    #endregion

    #region Gravity Direction Tables

    // Six axis-aligned gravity directions and their display names.
    private static readonly Vector3[] GravityDirections =
    {
        Vector3.down, Vector3.up,
        Vector3.left, Vector3.right,
        Vector3.forward, Vector3.back
    };

    private static readonly string[] DirectionNames = { "Down", "Up", "Left", "Right", "Forward", "Back" };

    #endregion

    #region Private State

    private int  _selectedIndex     = 0;
    private bool _isPreviewActive   = false;
    private bool _isReorienting     = false;

    // World-space gravity direction chosen during preview; stored so CommitGravity
    // uses the player-relative direction rather than a raw world axis.
    private Vector3 _selectedGravityDir;

    // Component references
    private Rigidbody            _rb;
    private ThirdPersonController _controller;
    private GravityHologram      _hologram;
    private CinemachineBrain     _cinemachineBrain;
    private CapsuleCollider      _capsule;

    #endregion

    #region Unity Lifecycle

    private void Awake() {
        _rb         = GetComponent<Rigidbody>();
        _controller = GetComponent<ThirdPersonController>();
        _capsule    = GetComponent<CapsuleCollider>();

        // Freeze rotation so physics never tips the character over.
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        if (Camera.main != null) {
            _cinemachineBrain = Camera.main.GetComponent<CinemachineBrain>();
        }

        if (_cinemachineBrain == null) {
            Debug.LogWarning("[GravityManipulator] CinemachineBrain not found on Camera.main.");
        }

        // Instantiate and attach the hologram component at runtime.
        if (hologramPrefab != null) {
            GameObject go = Instantiate(hologramPrefab, transform);
            go.SetActive(false);
            _hologram = go.AddComponent<GravityHologram>();

            // ✅ Assign hologram layer to every child so only HologramCamera renders it
            int hologramLayer = LayerMask.NameToLayer("Hologram");
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) {
                t.gameObject.layer = hologramLayer;
            }
                
        }
        else {
            Debug.LogWarning("[GravityManipulator] hologramPrefab is not assigned.");
        }
    }

    private void Update() {
        // Block input during the reorientation animation.
        if (!_isReorienting) {
            HandleInput();
        }
    }

    #endregion

    #region Input Handling

    //  Polls the keyboard for gravity-selection, confirm, and cancel inputs.
    //  Arrow keys & Q select a direction and show the preview hologram.
    //  Enter commits; Escape cancels.
    private void HandleInput() {
        var kb = Keyboard.current;
        if (kb == null) return;

        int newIndex = -1;

        if (kb.upArrowKey.wasPressedThisFrame) newIndex = 5;   // Back → gravity pulls Back
        else if (kb.downArrowKey.wasPressedThisFrame) newIndex = 4;   // Forward → gravity pulls Forward
        else if (kb.leftArrowKey.wasPressedThisFrame) newIndex = 2;   // Left → gravity pulls Left
        else if (kb.rightArrowKey.wasPressedThisFrame) newIndex = 3;   // Right → gravity pulls Right
        else if (kb.qKey.wasPressedThisFrame) newIndex = 1;   // Q  → gravity flips Up (ceiling)

        if (newIndex != -1) {
            _selectedIndex = newIndex;
            ShowHologram();
        }

        bool confirmPressed = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;

        if (confirmPressed && _isPreviewActive) {
            CommitGravity();
        }
        if (kb.escapeKey.wasPressedThisFrame && _isPreviewActive) {
            CancelHologram();
        }
    }

    #endregion

    #region Hologram Preview


    //  Computes the hologram world position and orientation for the currently
    //  selected gravity direction, then hands off to GravityHologram.Show.

    private void ShowHologram() {
        if (_hologram == null) return;

        // Get the player-relative direction, then snap to nearest world axis
        // so we avoid diagonals while still respecting the player's facing.
        Vector3 rawDir = _selectedIndex switch {
            0 => -transform.up,
            1 => transform.up,
            2 => -transform.right,
            3 => transform.right,
            4 => transform.forward,
            5 => -transform.forward,
            _ => -transform.up
        };

        // Snap to the closest axis-aligned world direction.
        float bestDot = float.MinValue;
        _selectedGravityDir = Vector3.down;
        foreach (Vector3 axis in GravityDirections) {
            float dot = Vector3.Dot(rawDir, axis);
            if (dot > bestDot) {
                bestDot = dot; _selectedGravityDir = axis;
            }
        }

        Vector3 newUp       = -_selectedGravityDir;
        float   halfHeight  = GetHalfHeight();

        // Place the hologram above the head along the selected gravity direction.
        Vector3 headPosition   = transform.position + transform.up * halfHeight;
        float   capsuleRadius  = _capsule != null ? _capsule.radius * transform.lossyScale.x : 0.5f;
        float   totalOffset    = capsuleRadius + hologramBodyClearance + hologramDistance;
        Vector3 hologramPos    = headPosition + _selectedGravityDir * totalOffset;

        // Compute a forward that is perpendicular to newUp for a natural look.
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, newUp);
        if (fwd.sqrMagnitude < 0.001f) {
            fwd = Vector3.ProjectOnPlane(transform.right, newUp);
        }
        fwd.Normalize();

        _hologram.Show(hologramPos, Quaternion.LookRotation(fwd, newUp), DirectionNames[_selectedIndex]);
        _isPreviewActive = true;
    }

    //  Hides the hologram and clears the preview flag.
    private void HideHologram() {
        _hologram?.Hide();
        _isPreviewActive = false;
    }

    //  Cancels the preview without committing gravity.
    //  Resets the selected index to match the current active gravity.
    private void CancelHologram() {
        HideHologram();
        _selectedIndex = GetCurrentGravityIndex();
    }

    #endregion

    #region Gravity Commit & Reorientation

    //  Applies the previewed gravity direction and starts the reorientation coroutine.
    //  Zeroes velocity and sets the Rigidbody kinematic for the duration of the animation.
    private void CommitGravity() {
        if (_isReorienting) return;

        HideHologram();

        _rb.linearVelocity  = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.constraints     = RigidbodyConstraints.None;
        _rb.isKinematic     = true;

        Physics.gravity = _selectedGravityDir * gravityMagnitude;
        StartCoroutine(ReorientCoroutine(-_selectedGravityDir));
    }

    //  Smoothly rotates the character to align with newWorldUp,
    //  keeping them planted on the current surface throughout the animation.
    //  Releases kinematic once grounded on the new surface.
    private IEnumerator ReorientCoroutine(Vector3 newWorldUp) {
        _isReorienting = true;
        _controller.GravityReorienting = true;

        yield return new WaitForFixedUpdate();

        float halfHeight  = GetHalfHeight() * 2f;
        Vector3 oldUp       = transform.up;
        LayerMask groundMask  = _controller.GroundLayer;
        const float skin       = 0.05f;

        // Raycast downward from mid-body to find the exact floor contact point.
        Vector3 castOrigin   = transform.position + oldUp * halfHeight * 0.5f;
        Vector3 surfacePoint = transform.position  - oldUp * halfHeight;

        if (Physics.Raycast(castOrigin, -oldUp, out RaycastHit floorHit, halfHeight * 2f + 0.3f, groundMask, QueryTriggerInteraction.Ignore)) surfacePoint = floorHit.point;

        Vector3 plantedPosition = surfacePoint + oldUp * (halfHeight + skin);
        transform.position = plantedPosition;
        _rb.position       = plantedPosition;

        yield return new WaitForFixedUpdate();

        Quaternion startRot = transform.rotation;

        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, newWorldUp);
        if (fwd.sqrMagnitude < 0.001f) {
            fwd = Vector3.ProjectOnPlane(transform.right, newWorldUp);
        }
        fwd.Normalize();

        Quaternion targetRot = Quaternion.LookRotation(fwd, newWorldUp);

        // The character rotates on the spot (e.g. upside-down for Down→Up),
        // then gravity carries them to the new surface once kinematic releases.
        float elapsed = 0f;
        while (elapsed < reorientDuration){
            float t = Mathf.SmoothStep(0f, 1f, elapsed / reorientDuration);
            _rb.MoveRotation(Quaternion.Slerp(startRot, targetRot, t));
            _rb.MovePosition(plantedPosition);
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        _rb.MoveRotation(targetRot);
        _rb.MovePosition(plantedPosition);
        yield return new WaitForFixedUpdate();


        // After rotating, the collider contact normal has flipped. Releasing
        // kinematic while touching the floor causes one-frame depenetration
        // that pushes the character INTO the surface. A small nudge along the
        // old up-axis eliminates the overlap before physics takes over.
        const float depenetrationClearance = 0.1f;
        Vector3 clearedPosition = plantedPosition + oldUp * depenetrationClearance;
        transform.position = clearedPosition;
        _rb.position       = clearedPosition;

        yield return new WaitForFixedUpdate();

        // Release kinematic — gravity now pulls to the new surface.
        _rb.isKinematic = false;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        yield return new WaitForFixedUpdate();

        // Wait until grounded, with a safety timeout to avoid an infinite loop.
        float waited = 0f;
        while (!_controller.IsGrounded && waited < reorientGroundedTimeout){
            waited += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        // Brief settle delay before returning control to the player.
        yield return new WaitForSeconds(reorientSettleDelay);

        _controller.GravityReorienting = false;
        _isReorienting                 = false;

        // Sync camera to new orientation
        if (_cinemachineBrain != null) {
            _cinemachineBrain.WorldUpOverride = transform;
        }

        if (cameraTarget != null) {
            cameraTarget.rotation = targetRot;
        }

        _controller.SyncCameraAngles(targetRot);
    }

    #endregion

    #region Utility

    //  Returns the half-height of the character's collider along its local-up axis.
    private float GetHalfHeight() {
        if (_capsule != null) {
            return _capsule.height * 0.5f * transform.lossyScale.y;
        }

        Collider col = GetComponent<Collider>();
        if (col != null){
            // Project the bounds size onto local up to get the correct axis extent.
            float projected = Mathf.Abs(Vector3.Dot(col.bounds.size, transform.up));
            return projected * 0.5f;
        }

        return 1f;   // Last-resort fallback.
    }

    //  Returns the index intoGravityDirections that best matches the current Physics.gravity direction.
    private int GetCurrentGravityIndex() {
        Vector3 dir  = Physics.gravity.normalized;
        float   best = float.MinValue;
        int     idx  = 0;

        for (int i = 0; i < GravityDirections.Length; i++){
            float dot = Vector3.Dot(dir, GravityDirections[i]);
            if (dot > best){
                best = dot;
                idx  = i;
            }
        }

        return idx;
    }

    #endregion
}
