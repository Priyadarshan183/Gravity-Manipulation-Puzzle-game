using UnityEngine;
using TMPro;


//  Singleton that owns all runtime UI updates: the HUD timer, cube counter,and the game-over panel.
public class UIManager : MonoBehaviour {
    #region Singleton

    public static UIManager Instance { get; private set; }

    #endregion

    #region Inspector Fields

    [Header("HUD")]
    [Tooltip("TextMeshPro label that displays the countdown timer in MM:SS format.")]
    [SerializeField] private TextMeshProUGUI timerText;

    [Tooltip("TextMeshPro label that displays collected / total cubes.")]
    [SerializeField] private TextMeshProUGUI cubeText;

    [Header("Game Over UI")]
    [Tooltip("Root panel GameObject that is shown when the game ends.")]
    [SerializeField] private GameObject gameOverPanel;

    [Tooltip("TextMeshPro label inside the game-over panel for the result message.")]
    [SerializeField] private TextMeshProUGUI resultText;

    #endregion

    #region Unity Lifecycle

    private void Awake() {
        // Singleton guard — destroy duplicates that appear after scene reloads.
        if (Instance != null && Instance != this){
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start() {
        // Hide game-over panel at startup; null-safe in case it is unassigned.
        gameOverPanel?.SetActive(false);
    }

    private void OnDestroy() {
        // Clear the static reference so the next scene's instance can register cleanly.
        if (Instance == this) Instance = null;
    }

    #endregion

    #region Public HUD API

    //  Refreshes the timer label. Called every frame by GameManager.
    public void UpdateTimer(float timeRemaining) {
        timeRemaining = Mathf.Max(0f, timeRemaining);
        int minutes = Mathf.FloorToInt(timeRemaining / 60f);
        int seconds = Mathf.FloorToInt(timeRemaining % 60f);
        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    
    //  Refreshes the cube-count label. Call whenever the collected count changes.
    public void UpdateCubeCount(int collected, int total) {
        cubeText.text = $"Cubes: {collected}/{total}";
    }

    #endregion

    #region Public Game-Over API

    //  Activates the game-over panel and displays messange.
    public void ShowGameOver(string message) {
        gameOverPanel?.SetActive(true);
        if (resultText != null) {
            resultText.text = message;
        }
            
    }

    #endregion
}
