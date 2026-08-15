using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class PowerLineController : MonoBehaviour
{
    private LineRenderer lineRenderer;
    private TowerLabelController labelController;
    private GeoDataManager.PowerLineData lineData;
    private float heightOffset;

    public void Init(GeoDataManager.PowerLineData data, GameObject labelPrefab, float heightOffsetMeters)
    {
        lineData = data;
        heightOffset = heightOffsetMeters;

        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = 1.2f; // 見やすい太さ
        lineRenderer.endWidth = 1.2f;
        lineRenderer.numCornerVertices = 4;
        lineRenderer.numCapVertices = 4;

        // 安全な標準シェーダー
        Shader lineShader = Shader.Find("Sprites/Default");
        if (lineShader == null) lineShader = Shader.Find("Unlit/Color");

        if (lineShader != null)
        {
            Material mat = new Material(lineShader);
            mat.color = new Color(0f, 1f, 1f, 0.9f); // シアン（水色）
            lineRenderer.material = mat;
        }

        if (labelPrefab != null)
        {
            GameObject labelObj = Instantiate(labelPrefab, transform);
            labelController = labelObj.GetComponent<TowerLabelController>();

            if (labelController != null)
            {
                string displayText = lineData.GetFormattedLabelText();
                labelController.SetInfo(displayText, "");
            }
        }
    }

    /// <summary>
    /// AR空間上の送電線頂点と、ユーザーに最も近い位置へラベルを更新配置
    /// </summary>
    public void UpdatePositions(double userLat, double userLon, float arNorthAngle, Vector3 cameraPos)
    {
        if (lineData == null || lineData.Coordinates == null || lineData.Coordinates.Count < 2) return;

        int nodeCount = lineData.Coordinates.Count;
        lineRenderer.positionCount = nodeCount;

        Vector3 closestNodeWorldPos = Vector3.zero;
        float minNodeDist = float.MaxValue;

        for (int i = 0; i < nodeCount; i++)
        {
            var coord = lineData.Coordinates[i];

            // 1. 相対平面座標の計算
            Vector3 basePos = CoordinateConverter.LatLonToUnityPosition(userLat, userLon, coord.Latitude, coord.Longitude);
            Vector3 rotatedPos = Quaternion.Euler(0, arNorthAngle, 0) * basePos;
            rotatedPos.y = heightOffset;

            Vector3 worldPos = cameraPos + rotatedPos;
            lineRenderer.SetPosition(i, worldPos);

            // 2. ユーザー（カメラ）に最も近いノードを検索
            float distToCam = Vector3.Distance(cameraPos, worldPos);
            if (distToCam < minNodeDist)
            {
                minNodeDist = distToCam;
                closestNodeWorldPos = worldPos;
            }
        }

        // 3. ユーザーの目の前（最も近いノード）にテキストラベルを配置
        if (labelController != null && minNodeDist < float.MaxValue)
        {
            labelController.transform.position = closestNodeWorldPos;
            labelController.UpdateDistance(minNodeDist);
        }
    }
}