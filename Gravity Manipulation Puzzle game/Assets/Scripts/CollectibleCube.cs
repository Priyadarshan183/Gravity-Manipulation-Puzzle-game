using UnityEngine;


/*  Destroys itself and notifies the GameManager when the player enters its trigger collider.
    Requires a Collider component set to "Is Trigger" and the player GameObject tagged "Player".*/
public class CollectibleCube : MonoBehaviour {
    #region Trigger Handling

    private void OnTriggerEnter(Collider other) {
        if (!other.CompareTag("Player")) return;

        GameManager.Instance.CollectCube();
        Destroy(gameObject);
    }

    #endregion
}
