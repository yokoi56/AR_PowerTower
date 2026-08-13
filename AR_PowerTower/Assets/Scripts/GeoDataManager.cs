using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

public class GeoDataManager : MonoBehaviour
{
    [SerializeField] private string towerResourceFileName = "power_tower_kanto";

    public class TowerPoint
    {
        public string Id;
        public string TowerName; // est_tower_name
        public string TowerNum;  // est_tower_num
        public double Latitude;
        public double Longitude;
    }

    public List<TowerPoint> LoadedTowers { get; private set; } = new List<TowerPoint>();

    void Start()
    {
        LoadTowerGeoJson();
    }

    public void LoadTowerGeoJson()
    {
        LoadedTowers.Clear();
        TextAsset geoJsonAsset = Resources.Load<TextAsset>(towerResourceFileName);

        if (geoJsonAsset == null)
        {
            Debug.LogError($"【エラー】Resources フォルダ内に '{towerResourceFileName}.json' が見つかりません。");
            return;
        }

        try
        {
            JObject geoJson = JObject.Parse(geoJsonAsset.text);
            JArray features = geoJson["features"] as JArray;

            if (features != null)
            {
                int index = 0;
                foreach (var feature in features)
                {
                    string geomType = feature["geometry"]?["type"]?.ToString();
                    if (geomType == "Point")
                    {
                        JArray coordinates = feature["geometry"]?["coordinates"] as JArray;
                        if (coordinates != null && coordinates.Count >= 2)
                        {
                            double lng = (double)coordinates[0];
                            double lat = (double)coordinates[1];

                            var props = feature["properties"];
                            string towerName = props?["est_tower_name"]?.ToString() ?? "名称未設定";
                            string towerNum = props?["est_tower_num"]?.ToString() ?? "-";

                            LoadedTowers.Add(new TowerPoint
                            {
                                Id = $"Tower_{index++}",
                                TowerName = towerName,
                                TowerNum = towerNum,
                                Latitude = lat,
                                Longitude = lng
                            });
                        }
                    }
                }
            }

            Debug.Log($"<color=green>【鉄塔GeoJSON】{LoadedTowers.Count} 件の鉄塔データを正常ロード</color>");
        }
        catch (Exception e)
        {
            Debug.LogError($"【GeoJSON解析エラー】: {e.Message}");
        }
    }
}