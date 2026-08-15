
---

# AR_PowerTower

※以下の文章は生成AIを利用して作成しました。

**ロケーションベースAR 送電線・鉄塔可視化アプリケーション**

`AR_PowerTower` は、屋外において遠方に存在する送電線や鉄塔をカメラ映像（AR空間）上にリアルタイムで可視化・識別するAndroid向けARアプリケーションです。OpenStreetMapのGeoJSONデータを基に、高精度な位置・方位トラッキングを用いて、鉄塔の路線名・番号・距離パネルや空中に描画される送電線を合成表示します。

---

## 1. プロジェクト概要

インフラ構造物（送電鉄塔・送電線）は上空や遠方に位置するため、従来の2D地図では位置関係や接続構造を直感的に把握することが困難でした。
本アプリは、スマホをかざすだけで「上空に見える鉄塔が何という系統で、どの番号か」「空中の送電線がどのように接続されているか」をAR空間上で直感的に識別できるようにすることを目指して開発されました。

### 主なユースケース
* **特定方向の観察**: 立ち止まって上空の鉄塔や送電線を仰ぎ見、名称や距離、接続状況を確認する。
* **環境非依存の利用**: ストリートビュー照合データが豊富な都市部（VPS）から、照合データのない郊外・山間部・夜間（GPS/コンパス）までシームレスに対応。

---

## 2. 主な機能

1. **ハイブリッド位置・方位トラッキング (VPS / GPS・コンパス モード切り替え)**
   * **VPSモード (Google ARCore Geospatial API)**: 周囲のカメラ映像とGoogleのストリートビュー特徴点を照合し、センチメートル級・度単位の高精度配置を実現。
   * **GPS / 磁気コンパスモード (適応型フォールバック)**: VPS利用不可環境（郊外、夜間、通信遮断時等）でも自律動作。平滑化アルゴリズムにより磁気ノイズを低減。
2. **ビルボード型 AR鉄塔テキストパネル**
   * 鉄塔の位置に「路線名」「鉄塔番号（No.）」「現在地からの直線距離（m）」を表示するUIパネルを3D空間へ生成。
   * パネルはカメラの仰角・旋回にかかわらず、常にカメラへ正対（LookAt制御）。
3. **送電線 (3D LineRenderer) の空中描画**
   * GeoJSON内の `LineString`（送電線座標列）をパースし、隣り合う鉄塔間を空中結線。
   * ユーザーに最も近い送電線ノードに、回線情報（電圧・路線名）パネルを動的配置。
4. **画角中心照準判定 (Center-Focused Targeting)**
   * スマホのカメラ画角中央（照準）で捉えている鉄塔をリアルタイム判定し、画面上部ヘ最優先でフォーカス表示。
5. **動的フィルタリング**
   * 現在地から指定半径（例: 500m〜2000m）内に存在する鉄塔および送電線のみを動的抽出・生成し、画面の混雑（クラッター）を防止。
6. **リアルタイム・デバッグオーバーレイ UI**
   * 現在のトラッキング状態、緯度経度、AR真北角度、保持データ数、最寄りスポット距離、判定理由をOnGUIで常時表示。
7. **PCエディタ用モック機能**
   * Unity Editor上ではダミー座標・ダミー方位を用いて室内テストが可能。

---

## 3. 技術的工夫・アルゴリズム

### ① 磁気ノイズと手ブレを相殺する「VIO × コンパス適応型平滑化」
従来のGPS/コンパスARアプリでは、電子コンパスの生（Raw）データで毎フレームAR空間を更新すると、地磁気ノイズやスマホ内部の電磁波によって画面上のオブジェクトが激しく暴れる問題（画面崩壊）が発生していました。

本アプリでは、ARCoreの **VIO (Visual Inertial Odometry: 6DoFカメラ追従)** と磁気コンパスを融合させた以下の公式で真北オフセット角度（`HeadingOffset`）を算出しています。

$$\text{TargetNorthAngle} = \text{CameraYaw} - \text{RawTrueHeading}$$

* **原理**: スマホを角度 $\theta$ 旋回させた際、`CameraYaw`（VIOによる回転）と `RawTrueHeading`（コンパスによる回転）の両方に同等の $\theta$ 変動が含まれるため、**引き算によりスマホ自身の回転運動成分 $\theta$ が完全に相殺消去**されます。
* **効果**: スマホを激しく振り回しても目標角度が揺れず、純粋な地磁気ノイズ成分のみを `Mathf.LerpAngle`（ローパスフィルター）で平滑化できます。VIOのガタつきゼロな追従性を一切破壊することなく、時間が経つと滑らかに統計的真北へ収束します。

### ② 3D高度（Y軸）計算による仰角破綻の防止
高所・遠所にある鉄塔を見上げる（カメラに仰角をつける）動作に対応するため、地表からの相対高度（例: 地表+25m〜35m）を加味した3D直交座標系 $(X, Y, Z)$ を構築しています。

1. **極座標 ➔ 平面近似メートル座標変換**:
   $$Z = (\text{TargetLat} - \text{UserLat}) \times 111,320\,\text{m}$$
   $$X = (\text{TargetLon} - \text{UserLon}) \times 111,320\,\text{m} \times \cos\left(\text{UserLat} \times \frac{\pi}{180}\right)$$
2. **ARカメラ基準の回転 ＆ ワールド配置**:
   $$V_{\text{rotated}} = \text{Quaternion.Euler}(0, \text{HeadingOffset}, 0) \times (X, Y_{\text{offset}}, Z)$$
   $$P_{\text{world}} = P_{\text{Camera}} + V_{\text{rotated}}$$

カメラを見上げたり見下ろしたりしても、3D空間上の幾何学的に正しい高所位置へARラベルが固定・保持されます。

### ③ 送電線 (LineString) の動的交差判定アルゴリズム
送電線データは複数の線分集合（折れ線）です。端点が指定半径外であっても、線分自体がユーザーの近くを通過しているかを判定するため、点と線分の最短距離（2D Segment Distance）を算出しています。

$$\vec{ab} = B - A, \quad t = \text{Clamp01}\left(\frac{\vec{ap} \cdot \vec{ab}}{|\vec{ab}|^2}\right)$$
$$\text{Distance} = |P - (A + t \cdot \vec{ab})|$$

これにより、現在地付近を通過する送電線のみを過不足なく動的にフィルタリング・描画します。

---

## 4. システムアーキテクチャ

本アプリは、トラッキング方式（GPS/VPS）を抽象化するインターフェース設計を採用しており、動的なモード切り替えに対応しています。

```text
                     ┌──────────────────────────────┐
                     │     ARPowerTowerManager      │
                     │(描画・検索・ライフサイクル制御) │
                     └──────────────┬───────────────┘
                                    │ ILocationProvider (利用)
                                    ▼
                     ┌──────────────────────────────┐
                     │     ILocationProvider        │ <--- (Interface)
                     └──────────────┬───────────────┘
                                    │
            ┌───────────────────────┴───────────────────────┐
            ▼                                               ▼
┌──────────────────────────────┐                ┌──────────────────────────────┐
│     GPSLocationManager       │                │  GeospatialLocationManager   │
│ (GPS + VIO×コンパス平滑化)   │                │   (ARCore Geospatial API)    │
└──────────────────────────────┘                └──────────────────────────────┘
```

### 主要クラス役割

| クラス名 | 役割・責務 |
| :--- | :--- |
| `ARPowerTowerManager` | 全体のメインコントローラー。描画範囲検索、照準判定、モード切り替え、OnGUIデバッグ表示。 |
| `ILocationProvider` | 位置情報（緯度・経度・高度）およびAR真北オフセット角を提供する抽象インターフェース。 |
| `GPSLocationManager` | Android生GPSと磁気コンパスを取得。VIO回転を考慮した適応型ローパスフィルターを実装。 |
| `GeospatialLocationManager` | Google ARCore Geospatial API (`AREarthManager`) を用いた高精度VPSトラッキング。 |
| `GeoDataManager` | `Resources` フォルダ内の GeoJSON（Point / LineString）を `Newtonsoft.Json` でパース・保持。 |
| `CoordinateConverter` | ハバーサイン大圏距離計算および極座標➔Unity 3D直交座標への変換ユーティリティ。 |
| `TowerLabelController` | 鉄塔パネルUIのデータ更新およびカメラ正対（LookAt）制御。 |
| `PowerLineController` | `LineRenderer` を用いた送電線の3D描画および最寄りノードへの回線パネル配置。 |

---

## 5. 技術スタック

* **開発環境**: Unity 6 (6000.3.x LTS)
* **レンダリングパイプライン**: Universal Render Pipeline (URP)
* **対応OS**: Android 10.0 (API Level 29) 以上
* **主要パッケージ**:
  * `com.unity.xr.arfoundation` (5.x / 6.x)
  * `com.unity.xr.arcore`
  * `com.google.ar.core.arcore_extensions` (ARCore Extensions / Geospatial API)
  * `com.unity.nuget.newtonsoft-json` (GeoJSONパース)
  * `com.unity.textmeshpro` (UI描画)

---

## 6. ビルド ＆ 設定上の重要ポイント（知見共有）

実機ビルド時に発生しやすいAR特有の不具合を防止するため、以下の設定が適用されています。

1. **Graphics API の固定 (Vulkan競合回避)**
   * Androidの `Graphics APIs` から `Vulkan` を除話し、**`OpenGLES3` に固定**（Vulkan使用時にARCoreのカメラストリームが真っ暗になる現象を防止）。
2. **URP AR Background Renderer Feature**
   * 使用中の `Mobile_Renderer` (UniversalRendererData) に **`AR Background Renderer Feature`** を追加（カメラ映像の背景描画に必須）。
3. **GeoJSONファイルの配置**
   * Android APK化後のファイル読み込みエラーを防ぐため、GeoJSONデータは `Assets/Resources/*.json` に配置し、`Resources.Load<TextAsset>()` 経由で取得。
4. **Android 実行時権限 (Runtime Permission)**
   * `AndroidManifest.xml` の記述に加え、C#コード側で `Permission.RequestUserPermission(Permission.FineLocation)` を明示的に呼び出し。

---

## 7. ディレクトリ構成

```text
AR_PowerTower/
├── Assets/
│   ├── Prefabs/
│   │   └── TowerLabelPrefab.prefab      # 鉄塔/送電線 ARテキストパネルUI
│   ├── Resources/
│   │   ├── power_tower_kanto.json       # 鉄塔 GeoJSON データ (Point)
│   │   └── power_line_kanto.json        # 送電線 GeoJSON データ (LineString)
│   ├── Scenes/
│   │   └── SampleScene.unity            # メインARシーン
│   └── Scripts/
│       ├── ARPowerTowerManager.cs       # 全体管理・UI制御
│       ├── CoordinateConverter.cs       # 座標変換ユーティリティ
│       ├── GeoDataManager.cs            # GeoJSONパース
│       ├── GeospatialLocationManager.cs # VPSプロバイダー
│       ├── GPSLocationManager.cs        # GPS/コンパスプロバイダー
│       ├── ILocationProvider.cs         # 位置情報インターフェース
│       ├── PowerLineController.cs       # 送電線3D描画
│       └── TowerLabelController.cs      # ビルボードUI制御
└── ProjectSettings/                     # Project各種設定
```

---

## 8. ライセンス表記


### ソースコード (Source Code)
本リポジトリのソースコードは [MIT License](LICENSE) のもとで公開されています。

### 地理・位置データ (Data Attribution)
本アプリで使用している鉄塔および送電線の位置データは、[OpenStreetMap](https://www.openstreetmap.org/) のデータを利用・加工しています。
* **データ出典**: © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors
* **データライセンス**: [Open Database License (ODbL)](https://opendatacommons.org/licenses/odbl/1.0/)

### サードパーティライブラリ (Third-party Libraries)
* Unity (Universal Render Pipeline, AR Foundation)
* Google ARCore Extensions (Apache License 2.0)
* Newtonsoft.Json (MIT License)

---
## 9. FAQ
Q：送電インフラの情報を提供することは安全保障上問題ありませんか？
A：地図データはOpenStreetMap上のデータを取得して利用しています。OSMには世界中の社会インフラの位置情報が記載され、公開されています。

Q：鉄塔/送電線の表示は正確ですか？
A：OSMのデータを基に独自の自動推定アルゴリズムを用いて情報を補完しています。そのため、正確でないデータとなっている部分があります。ご了承ください。
