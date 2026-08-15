using UnityEngine;
using TMPro;

public class TowerLabelController : MonoBehaviour
{
    [Header("UI要素参照")]
    [SerializeField] private TMP_Text titleText;       // 鉄塔名 / 路線名 (tower_name)
    [SerializeField] private TMP_Text towerNumText;    // 鉄塔番号 (tower_num)
    [SerializeField] private TMP_Text distanceText;   // 距離

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
            titleText.text = towerName;
        }

        if (towerNumText != null)
        {
            towerNumText.text = string.IsNullOrEmpty(towerNum) || towerNum == "-" ? "" : $"{towerNum}";
        }
        else if (titleText != null && !string.IsNullOrEmpty(towerNum) && towerNum != "-")
        {
            // towerNumTextが未割り当ての場合のフォールバック
            titleText.text = $"{towerName}\nNo.{towerNum}";
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