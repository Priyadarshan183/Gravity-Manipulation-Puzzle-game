using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/*  Third-person character controller using Unity's Input System and Rigidbody physics.
    Handles movement, jumping, camera look, and animator parameter updates.*/
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Animator))]
public class ThirdPersonController : MonoBehaviour {
    #region Animator Parameter Constants

    private const string ANIM_SPEED = "Speed";
    private const string ANIM_JUMP = "Jump";
    private const string ANIM_GROUNDED = "Grounded";

    #endregion

    #region Inspector Fields

    [Header("Cinemachine")]
    [Tooltip("The target transform that the Cinemachine camera follows and rotates around.")]
    [SerializeField] private Transform cameraTarget;

    [Tooltip("Maximum upward camera pitch angle in degrees.")]
    [SerializeField] private float topClamp = 70.0f;

    [Tooltip("Maximum downward camera pitch angle in degrees.")]
    [SerializeField] private float bottomClamp = -30.0f;

    [Header("Movement")]
    [Tooltip("Base movement speed in units per second.")]
    [SerializeField] private float movementSpeed = 3f;

    [Tooltip("Mouse/stick look sensitivity multiplier.")]
    [SerializeField] private float lookSpeed = 10.0f;

    [Header("Jump")]
    [Tooltip("Impulse force applied upward when jumping.")]
    [SerializeField] private float jumpStrength = 7f;

    [Tooltip("Minimum time (seconds) after landing before the player can jump again.")]
    [SerializeField] private float jumpDowntime = 0.1f;

    [Tooltip("How much horizontal momentum is retained on jump (0 = stop, 1 = full speed).")]
    [SerializeField][Range(0f, 1f)] private float jumpMomentumRetention = 0.3f;

    [Tooltip("How quickly the character accelerates and decelerates. Higher values feel snappier, lower values feel floatier.")]
    [SerializeField] private float speedLerpRate = 8f;

    [Tooltip("How fast the character rotates to face the movement direction. Higher values feel more responsive, lower values feel smoother.")]
    [SerializeField] private float rotationSlerpRate = 10f;

    [Tooltip("Brief delay (seconds) after jumping before the ground check registers as airborne. Prevents instant re-grounding on the same frame.")]
    [SerializeField] private float jumpAirborneDelay = 0.25f;

    [Header("Ground Check")]
    [Tooltip("Transform positioned at the character's feet used as the ground check origin.")]
    [SerializeField] private Transform groundCheckPoint;

    [Tooltip("Radius of the ground detection sphere.")]
    [SerializeField] private float groundCheckRadius = 0.2f;

    [Tooltip("Layer(s) considered as ground. Must NOT include the player's own layer.")]
    [SerializeField] private LayerMask groundLayer;

    public bool GravityReorienting { get; set; } = false;

    // Expose groundLayer so GravityManipulator can raycast against walkable surfaces only.
    public LayerMask GroundLayer => groundLayer;
    // Exposes the IsGrounded to GravityManipulator to check if the player is grounded.
    public bool IsGrounded => _isGrounded;

    #endregion

    #region Private State

    // Input values
    private Vector2 _moveInput;
    private Vector2 _lookInput;

    // Movement state
    private float _currentSpeed;
    private bool _isRunning;

    // Camera rotation state
    private float _yaw;
    private float _pitch;

    // Jump state
    private bool _isGrounded = true;
    private bool _canJump = true;

    // Component references
    private Rigidbody _rb;
    private Animator _animator;

    // Minimum look input magnitude squared to avoid floating-point noise
    private const float LOOK_THRESHOLD = 0.01f;

    #endregion

    #region Unity Lifecycle

    private void Awake() {
        _rb = GetComponent<Rigidbody>();
        _animator = GetComponent<Animator>();

        // Initialise camera yaw/pitch from its current scene rotation
        // so the camera doesn't snap to world-forward on Play.
        _yaw = cameraTarget.rotation.eulerAngles.y;
        _pitch = cameraTarget.rotation.eulerAngles.x;
    }

    //Ground check runs every frame for responsive state updates.
    private void Update() {
        CheckGrounded();
    }

    //Camera look runs in LateUpdate so it applies after all movement.
    private void LateUpdate() {
        /*  GUARD: skip while GravityManipulator owns the cameraTarget rotation.
            Without this, Quaternion.Euler(_pitch, _yaw, 0) snaps cameraTarget back to world-upright every frame, undoing the gravity reorientation.*/
        if (GravityReorienting) return;
        UpdateCameraLook();
    }

    //Physics-based movement runs in FixedUpdate.
    private void FixedUpdate() {

        if (GravityReorienting) return;
        // Skip movement processing while airborne — jump momentum handles air travel.
        if (!_isGrounded) return;

        ApplyMovement();
    }

    #endregion

    #region Ground Check

    /*  Casts a sphere at the feet to determine whether the character is grounded.
        Updates the Animator's Grounded parameter accordingly.*/
    private void CheckGrounded() {
        _isGrounded = Physics.CheckSphere(groundCheckPoint.position, groundCheckRadius, groundLayer);

        _animator.SetBool(ANIM_GROUNDED, _isGrounded);
    }


    #endregion

    #region Movement
    // Safe gravity up — falls back to Vector3.up if gravity magnitude is near zero
    private Vector3 GravityUp =>
        Physics.gravity.sqrMagnitude > 0.001f ? -Physics.gravity.normalized : Vector3.up;

    /*  Calculates and applies horizontal Rigidbody velocity based on input,
        camera orientation, and current speed. Also updates Animator parameters.*/
    private void ApplyMovement() {
        // Determine target speed: zero when no input, walk or run otherwise.
        float targetSpeed = (_isRunning ? movementSpeed * 2f : movementSpeed) * _moveInput.magnitude;

        // Smoothly interpolate toward the target speed.
        _currentSpeed = Mathf.Lerp(_currentSpeed, targetSpeed, Time.deltaTime * speedLerpRate);

        Vector3 gravityUp = GravityUp;

        // Stable forward
        Vector3 camForward = Vector3.ProjectOnPlane(cameraTarget.forward, gravityUp).normalized;

        // Fallback
        if (camForward.sqrMagnitude < 0.001f)
            camForward = Vector3.ProjectOnPlane(transform.forward, gravityUp).normalized;

        // TRUE right vector (fixes A/D completely)
        Vector3 camRight = Vector3.Cross(gravityUp, camForward).normalized;

        Vector3 moveDirection =
            (camForward * _moveInput.y + camRight * _moveInput.x).normalized;

        if (moveDirection.sqrMagnitude > 0.1f) {
            // Rotate character to match camera yaw, not just movement direction
            // Rotate toward movement direction, not camera yaw
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, gravityUp);
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRotation, Time.fixedDeltaTime * rotationSlerpRate));

            // Apply movement velocity + preserve gravity axis (fall/jump speed)
            float gravityAxisVel = Vector3.Dot(_rb.linearVelocity, gravityUp);
            _rb.linearVelocity = moveDirection * _currentSpeed + gravityUp * gravityAxisVel;
        } else {
            // Preserve gravity axis velocity, zero movement plane velocity
            float gravityAxisVel = Vector3.Dot(_rb.linearVelocity, gravityUp);
            _rb.linearVelocity = gravityUp * gravityAxisVel;
        }

        // Normalise speed to [0, 1] range for the Animator.
        float normalizedSpeed = _currentSpeed / (movementSpeed * 2f);
        _animator.SetFloat(ANIM_SPEED, normalizedSpeed);
    }
    #endregion

    #region Jump

    /*  Applies an upward impulse force and reduces horizontal momentum.
        Guarded by grounded and cooldown checks.*/
    private void Jump() {
        if (!_isGrounded || !_canJump) return;

        // Retain a fraction of horizontal momentum for a natural arc.
        Vector3 gravityUp = GravityUp;
        Vector3 verticalVel = Vector3.Project(_rb.linearVelocity, gravityUp);
        Vector3 horizontalVel = _rb.linearVelocity - verticalVel;
        _rb.linearVelocity = horizontalVel * jumpMomentumRetention + verticalVel;

        // Reset speed and animator so transitions (Running -> Idle) fire immediately.
        _currentSpeed = _currentSpeed * jumpMomentumRetention;
        _animator.SetFloat(ANIM_SPEED, 0f);

        // Jump opposes gravity direction
        _rb.AddForce(GravityUp * jumpStrength, ForceMode.Impulse);

        _canJump = false;
        StartCoroutine(JumpCooldownRoutine());

        _animator.SetTrigger(ANIM_JUMP);
    }

    /*  Waits for the character to leave the ground, land again,
        then enforces the minimum re-jump delay before re-enabling jumping.*/
    private IEnumerator JumpCooldownRoutine() {
        // Brief delay so the ground check sphere has time to register as airborne.
        yield return new WaitForSeconds(jumpAirborneDelay);

        // Wait until grounded again.
        yield return new WaitUntil(() => _isGrounded);

        // Minimum delay after landing before next jump is allowed.
        yield return new WaitForSeconds(jumpDowntime);

        _canJump = true;
    }

    #endregion

    #region Camera Look
    // Rotates the "cameraTarget" based on look input, clamping pitch to avoid flipping.
    private void UpdateCameraLook() {

        // _yaw rotates around gravityUp — the surface-relative "vertical" axis.
        // _pitch tilts around the surface-relative "right" axis that gravityUp defines.
        // We derive the pitch axis directly from gravityUp so it is always
        Vector3 gravityUp = GravityUp;

        if (_lookInput.sqrMagnitude >= LOOK_THRESHOLD) {
            float delta = Time.deltaTime * lookSpeed;
            _yaw += _lookInput.x * delta;
            _pitch -= _lookInput.y * delta;
        }

        _pitch = Mathf.Clamp(_pitch, bottomClamp, topClamp);

        // perpendicular to whatever surface the character stands on.

        // Step 1: pick a stable world reference that is never parallel to gravityUp.
        Vector3 worldRef = (Mathf.Abs(Vector3.Dot(gravityUp, Vector3.forward)) < 0.99f) ? Vector3.forward : Vector3.up;

        // Step 2: build a surface-relative forward from worldRef and gravityUp.
        Vector3 surfaceForward = Vector3.Cross(Vector3.Cross(gravityUp, worldRef).normalized, gravityUp).normalized;

        // Step 3: yaw — orbit horizontally around gravityUp.
        Quaternion yawRot = Quaternion.AngleAxis(_yaw, gravityUp);

        // Step 4: pitch axis is always perpendicular to both gravityUp and
        //         the yawed forward, so it correctly represents surface-right.
        Vector3 yawedForward = yawRot * surfaceForward;
        Vector3 pitchAxis = Vector3.Cross(gravityUp, yawedForward).normalized;

        // Step 5: pitch — tilt up/down relative to the surface.
        Quaternion pitchRot = Quaternion.AngleAxis(_pitch, pitchAxis);

        cameraTarget.rotation = pitchRot * yawRot * Quaternion.LookRotation(surfaceForward, gravityUp);
    }
    // Mirrors the camera yaw onto the player outside of FixedUpdate for tighter feel.
   

    #endregion

    #region Public API for GravityManipulator

    public void SyncCameraAngles(Quaternion newOrientation) {
        Vector3 gravityUp = GravityUp;

        // Pick the same stable world reference as UpdateCameraLook uses.
        Vector3 worldRef = (Mathf.Abs(Vector3.Dot(gravityUp, Vector3.forward)) < 0.99f) ? Vector3.forward : Vector3.up;

        Vector3 surfaceForward = Vector3.Cross(Vector3.Cross(gravityUp, worldRef).normalized, gravityUp).normalized;

        // Project camera's current forward onto the gravity plane.
        Vector3 camForward = cameraTarget.forward;
        Vector3 flatForward = Vector3.ProjectOnPlane(camForward, gravityUp);

        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = Vector3.ProjectOnPlane(transform.forward, gravityUp);

        flatForward.Normalize();

        // Yaw = signed angle from surfaceForward to flatForward, around gravityUp.
        // Using the same surfaceForward reference as UpdateCameraLook ensures
        // _yaw=0 always means "facing surface-forward" on any gravity surface.
        _yaw = Vector3.SignedAngle(surfaceForward, flatForward, gravityUp);

        // Pitch = how far the camera is tilted above/below the gravity plane.
        float angleFromFlat = Vector3.Angle(flatForward, camForward);
        _pitch = Vector3.Dot(camForward, gravityUp) > 0f ? -angleFromFlat : angleFromFlat;

        _pitch = Mathf.Clamp(_pitch, bottomClamp, topClamp);
    }

    #endregion

    #region Input Callbacks

    // Called by Unity's Player Input component via Send Messages / Broadcast Messages.

    private void OnMove(InputValue value) => _moveInput = value.Get<Vector2>();
    private void OnLook(InputValue value) => _lookInput = value.Get<Vector2>();
    private void OnRun(InputValue value) => _isRunning = value.isPressed;
    private void OnJump(InputValue value) => Jump();

    #endregion
}