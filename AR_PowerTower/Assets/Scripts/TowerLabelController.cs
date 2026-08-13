using UnityEngine;
using TMPro;

public class TowerLabelController : MonoBehaviour
{
    [Header("UI要素参照")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text distanceText;

    private Transform cameraTransform;

    void Start()
    {
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    public void SetInfo(string towerName, string towerNum)
    {
        if (titleText != null)
        {
            titleText.text = $"{towerName}\nNo. {towerNum}";
        }
    }

    public void UpdateDistance(float distanceMeters)
    {
        if (distanceText != null)
        {
            distanceText.text = $"{distanceMeters:F0} m";
        }
    }

    void LateUpdate()
    {
        if (cameraTransform == null)
        {
            if (Camera.main != null) cameraTransform = Camera.main.transform;
            return;
        }

        // カメラ正面を向かせる（LookAt + 反転）
        Vector3 targetPosition = transform.position + (transform.position - cameraTransform.position);
        transform.LookAt(targetPosition, Vector3.up);
    }
}