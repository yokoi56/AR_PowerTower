using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

public class GeoDataManager : MonoBehaviour
{
    [Header("GeoJSON リソースファイル設定")]
    [Tooltip("Resources フォルダ内の鉄塔(Point)データファイル名 (.json除く)")]
    [SerializeField] private string towerResourceFileName = "power_tower_kanto";

    [Tooltip("Resources フォルダ内の送電線(LineString)データファイル名 (.json除く)")]
    [SerializeField] private string lineResourceFileName = "power_line_kanto";

    public class TowerPoint
    {
        public string Id;
        public string TowerName;
        public string TowerNum;
        public double Latitude;
        public double Longitude;
    }

    public class CircuitInfo
    {
        public string Voltage;
        public string LineName;

        public string GetDisplayText()
        {
            if (string.IsNullOrEmpty(Voltage))
            {
                return string.IsNullOrEmpty(LineName) ? "名称未設定" : LineName;
            }
            return $"{Voltage} {LineName}";
        }
    }

    public class PowerLineData
    {
        public string Id;
        public string OsmId;
        public string Name;
        public List<CircuitInfo> Circuits = new List<CircuitInfo>();
        public List<Vector2D> Coordinates = new List<Vector2D>();

        public struct Vector2D
        {
            public double Latitude;
            public double Longitude;
            public Vector2D(double lat, double lon) { Latitude = lat; Longitude = lon; }
        }

        public string GetFormattedLabelText()
        {
            if (Circuits != null && Circuits.Count > 0)
            {
                List<string> lines = new List<string>();
                foreach (var c in Circuits)
                {
                    lines.Add(c.GetDisplayText());
                }
                return string.Join("\n", lines);
            }
            return string.IsNullOrEmpty(Name) ? "送電線" : Name;
        }
    }

    public List<TowerPoint> LoadedTowers { get; private set; } = new List<TowerPoint>();
    public List<PowerLineData> LoadedLines { get; private set; } = new List<PowerLineData>();

    void Start()
    {
        LoadAllGeoJson();
    }

    public void LoadAllGeoJson()
    {
        LoadedTowers.Clear();
        LoadedLines.Clear();

        ParseTowerGeoJson(towerResourceFileName);
        ParseLineGeoJson(lineResourceFileName);

        Debug.Log($"<color=green>【GeoJSONロード完了】鉄塔: {LoadedTowers.Count} 件 / 送電線: {LoadedLines.Count} 件</color>");
    }

    private void ParseTowerGeoJson(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        TextAsset asset = Resources.Load<TextAsset>(fileName);
        if (asset == null)
        {
            Debug.LogWarning($"[GeoDataManager] 鉄塔データ '{fileName}.json' が Resources 内に見つかりません。");
            return;
        }

        try
        {
            JObject geoJson = JObject.Parse(asset.text);
            JArray features = geoJson["features"] as JArray;
            if (features == null) return;

            int index = 0;
            foreach (var feature in features)
            {
                string geomType = feature["geometry"]?["type"]?.ToString();
                if (geomType == "Point")
                {
                    JArray coords = feature["geometry"]?["coordinates"] as JArray;
                    if (coords != null && coords.Count >= 2)
                    {
                        var props = feature["properties"];
                        LoadedTowers.Add(new TowerPoint
                        {
                            Id = $"Tower_{index++}",
                            TowerName = props?["est_tower_name"]?.ToString() ?? "名称未設定",
                            TowerNum = props?["est_tower_num"]?.ToString() ?? "-",
                            Latitude = (double)coords[1],
                            Longitude = (double)coords[0]
                        });
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GeoDataManager] 鉄塔データパースエラー ({fileName}): {e.Message}");
        }
    }

    private void ParseLineGeoJson(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        TextAsset asset = Resources.Load<TextAsset>(fileName);
        if (asset == null)
        {
            Debug.LogWarning($"[GeoDataManager] 送電線データ '{fileName}.json' が Resources 内に見つかりません。");
            return;
        }

        try
        {
            JObject geoJson = JObject.Parse(asset.text);
            JArray features = geoJson["features"] as JArray;
            if (features == null) return;

            int index = 0;
            foreach (var feature in features)
            {
                string geomType = feature["geometry"]?["type"]?.ToString();
                if (geomType == "LineString")
                {
                    JArray coords = feature["geometry"]?["coordinates"] as JArray;
                    if (coords != null && coords.Count >= 2)
                    {
                        var props = feature["properties"];
                        var lineData = new PowerLineData
                        {
                            Id = $"Line_{index++}",
                            OsmId = props?["osm_id"]?.ToString() ?? "",
                            Name = props?["name"]?.ToString() ?? ""
                        };

                        JArray circuitsArray = props?["circuits"] as JArray;
                        if (circuitsArray != null)
                        {
                            foreach (var c in circuitsArray)
                            {
                                string voltage = c["voltage"]?.Type == JTokenType.Null ? null : c["voltage"]?.ToString();
                                string lineName = c["line_name"]?.Type == JTokenType.Null ? null : c["line_name"]?.ToString();
                                lineData.Circuits.Add(new CircuitInfo { Voltage = voltage, LineName = lineName });
                            }
                        }

                        foreach (var coord in coords)
                        {
                            JArray pt = coord as JArray;
                            if (pt != null && pt.Count >= 2)
                            {
                                lineData.Coordinates.Add(new PowerLineData.Vector2D((double)pt[1], (double)pt[0]));
                            }
                        }

                        LoadedLines.Add(lineData);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GeoDataManager] 送電線データパースエラー ({fileName}): {e.Message}");
        }
    }
}