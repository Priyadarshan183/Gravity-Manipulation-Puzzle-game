using UnityEngine;
using UnityEngine.SceneManagement;


//  Central game-state singleton. Owns the countdown timer, air-time penalty,
//  cube collection tracking, and win / lose conditions.

public class GameManager : MonoBehaviour {
    #region Singleton

    public static GameManager Instance { get; private set; }

    #endregion

    #region Inspector Fields

    [Header("Game Settings")]
    [Tooltip("Total time allowed per round in seconds (default 120 = 2 minutes).")]
    [SerializeField] private float gameTime = 120f;

    [Tooltip("Maximum continuous airborne time before a 'fell too long' game-over triggers.")]
    [SerializeField] private float maxAirTime = 5f;

    #endregion

    #region Private State

    private float _timeRemaining;
    private float _airTimer;

    private int _totalCubes;
    private int _collectedCubes;

    private bool _isGameOver;

    private ThirdPersonController _player;

    #endregion

    #region Unity Lifecycle

    private void Awake() {
        // Singleton guard — destroy duplicates created after scene reloads.
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start() {
        // Ensure time runs normally (may have been paused in a previous round).
        Time.timeScale = 1f;

        // Reset gravity to default in case GravityManipulator left it altered.
        Physics.gravity = Vector3.down * 9.81f;

        _timeRemaining = gameTime;
        _isGameOver    = false;
        _airTimer      = 0f;

        // Auto-discover the player and all collectibles at scene start.
        _player     = FindAnyObjectByType<ThirdPersonController>();
        _totalCubes = FindObjectsByType<CollectibleCube>(FindObjectsSortMode.None).Length;

        // Initialise the cube counter label once so it is never blank.
        UIManager.Instance.UpdateCubeCount(_collectedCubes, _totalCubes);
    }

    private void Update() {
        if (_isGameOver) return;

        HandleTimer();
        HandleAirTime();

        // Timer updates every frame; cube count is pushed only on collection (see CollectCube).
        UIManager.Instance.UpdateTimer(_timeRemaining);
    }

    private void OnDestroy() {
        // Clear the static reference so the next scene's instance can register cleanly.
        if (Instance == this) Instance = null;
    }

    #endregion

    #region Private Game Logic

    //  Counts down the round timer and triggers a game-over when it reaches zero.
    
    private void HandleTimer() {
        _timeRemaining -= Time.deltaTime;

        if (_timeRemaining <= 0f) {
            GameOver("Time Up!");
        }

    }

    //  Tracks how long the player has been continuously airborne (outside of a gravity reorientation). 
    //  Triggers a game-over if the limit is exceeded.

    private void HandleAirTime() {
        if (_player == null) return;

        // GravityReorienting is intentional airtime — do not penalise it.
        if (!_player.IsGrounded && !_player.GravityReorienting) {
            _airTimer += Time.deltaTime;
            if (_airTimer >= maxAirTime) {
                GameOver("Fell too long!");
            } 
        }
        else{
            _airTimer = 0f;
        }
    }

    //  Ends the game, freezes time, and displays the supplied reason in the UI.
    
    private void GameOver(string reason) {
        if (_isGameOver) return;   // Guard against double-trigger on the same frame.

        _isGameOver    = true;
        Time.timeScale = 0f;

        Debug.Log($"[GameManager] Game Over — {reason}");
        UIManager.Instance.ShowGameOver("You Lose");
    }

    #endregion

    #region Public API

    //  Called by CollectibleCube each time the player picks up a cube.
    //  Triggers the win condition when all cubes are collected.
    public void CollectCube() {
        _collectedCubes++;

        Debug.Log($"[GameManager] Collected: {_collectedCubes}/{_totalCubes}");

        // Push updated count to UI immediately — not deferred to Update.
        UIManager.Instance.UpdateCubeCount(_collectedCubes, _totalCubes);

        if (_collectedCubes >= _totalCubes){
            _isGameOver    = true;
            Time.timeScale = 0f;
            UIManager.Instance.ShowGameOver("You Win!");
        }
    }

    //  Resets the game by reloading the active scene.
    //  Restores time scale so the scene starts unpaused.
    public void ResetGame() {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    //  Quits the application. In the Unity Editor, stops Play mode instead.
    public void ExitGame() {
        Debug.Log("[GameManager] Exiting application.");
        Application.Quit();

        #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }

    #endregion
}
