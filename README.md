# Instant Muscle

Meta Questで筋トレとボディビルのポージングを体験する、IVRC向けのUnityプロトタイプです。HMDと左右コントローラーの入力から身体の動きを捉え、運動のタイミングを採点し、アバターの筋肉の成長やステージ演出へ反映します。

このリポジトリは、実装を紹介するための公開版です。ゲームのコード、テスト、シーン設定、設計資料を収録しています。アバター、アニメーション、音源などの素材を除外しているため、クローン直後に体験を実行できる構成ではありません。

## 体験の流れ

鏡の前で位置と身長を合わせ、チュートリアルを経てトレーニングを始めます。リズムに合わせた肘曲げ・腕上げの結果が筋肉の成長へ反映され、変身演出の後にステージへ移ります。ポージングでは頭と両手の位置・回転を採点し、最後にトレーニングとポージングを合わせた結果を表示します。

```text
セットアップ → チュートリアル → トレーニング → 変身
           → ステージ入場 → ポージング → 結果
```

## 実装の見どころ

| 機能 | 主なコード | 内容 |
| --- | --- | --- |
| 体験全体の進行 | [GameFlowController](Assets/IVRC/Scripts/GameFlowController.cs) | セットアップから結果表示までのフェーズを管理 |
| 運動の検出 | [BodyRepCounter](Assets/IVRC/Scripts/BodyRepCounter.cs) | 肘曲げ・腕上げを計測 |
| リズム採点 | [TrainingRhythmScorer](Assets/IVRC/Scripts/TrainingRhythmScorer.cs) | 拍に対する上下動作のタイミングを評価 |
| 筋肉の成長 | [MuscleGrowthController](Assets/IVRC/Scripts/MuscleGrowthController.cs) / [BlendShape出力](Assets/IVRC/Scripts/BlendShapeMuscleGrowthOutput.cs) | トレーニングの結果を部位別の体型変化へ反映 |
| ポージング採点 | [ThreePointPosingScorer](Assets/IVRC/Scripts/ThreePointPosingScorer.cs) | 頭と両手の追跡情報を使ってポーズを評価 |
| お手本の表示 | [RhythmPoseGuide](Assets/RhythmPoseGuide/Scripts) | ポーズデータを使った運動ガイドを表示 |
| 空気圧デバイスとの通信 | [PneumaticCuffController](Assets/IVRC/Scripts/PneumaticCuffController.cs) | ESP32に上腕・肩のバルブとポンプの指令をUDP送信 |

最初に `GameFlowController` で全体の流れを確認し、`TrainingRhythmScorer`、`MuscleGrowthController`、`ThreePointPosingScorer` の順に読むと、入力から評価・見た目への反映をたどれます。

## ディレクトリ構成

| パス | 内容 |
| --- | --- |
| `Assets/IVRC/Scripts/` | 体験の進行、入力、採点、演出、ハードウェア連携 |
| `Assets/IVRC/Tests/Editor/` | ゲームロジックなどのEditModeテスト |
| `Assets/RhythmPoseGuide/` | 運動ガイドのコード、シェーダー、ポーズ設定、テスト |
| `Assets/Editor/` | シーン設定やキャプチャ用のEditorツール |
| `Assets/Scenes/InstantMuscle_proto.unity` | 元プロジェクトのメインシーン。除外素材への参照を含む |
| `Assets/Plugins/Android/` | Android用マニフェストとUDP通信のJavaコード |
| `Packages/` / `ProjectSettings/` | Unityの依存パッケージとプロジェクト設定 |

## 開発環境

元プロジェクトの設定はUnity `6000.3.10f1`、Meta XR SDK `203.0.1`、Meta XR Interaction SDK OVR `203.0.0`、OpenXR `1.16.1`、Unity Meta OpenXR `2.4.0` です。Meta Movement SDKも使用しています。詳細な依存関係は [manifest.json](Packages/manifest.json) と [packages-lock.json](Packages/packages-lock.json) に記録しています。

```bash
git clone https://github.com/Daiki-Hashimoto-NAIST/InstantMuscle-Shared.git
```

コードを読むだけならUnityは不要です。Unityで復元する場合は、上記バージョンで開いてパッケージを取得したうえで、[除外した素材と復元時の注意](docs/PUBLIC_EXPORT.md)を確認してください。SDKサンプルや各素材の再取得、シーン・Prefabへの再割り当てが必要です。

## テストと検証範囲

元プロジェクトのEditModeテストを収録しています。公開版では素材とSDKサンプルを除外しており、Unityコンパイル、テスト実行、Quest実機での動作は未検証です。過去の開発資料にある成功件数を、この公開版の検証結果として扱わないでください。

公開用の整理では、ゲームのC#コードとテストを元の状態で保持しています。変更範囲と除外一覧は [PUBLIC_EXPORT.md](docs/PUBLIC_EXPORT.md) にまとめています。

## 設計資料

以下は開発時点の資料です。現在のコードと異なる説明も含まれます。

- [トレーニング回数・リズム採点](TRAINING_RHYTHM.md)
- [ポージング採点](POSE_SCORING.md)
- [筋肉成長の出力](MUSCLE_GROWTH_INTEGRATION.md)
- [ステージ遷移](STAGE_TRANSITION.md)
- [ステージ演出](POSE_STAGE_FEEDBACK.md)
- [結果スコア](RESULT_SCORING.md)

空気圧連携は現在のコードに実機向けUDP送信を含みます。旧資料の「モックのみ」「空気は出ない」という説明は現状に当てはまりません。公開版は人体装着時の安全性を検証したものではなく、シーン設定をそのまま実機接続に使うための配布物ではありません。

## 素材と利用条件

アバター・テクスチャ・入場アニメーションなど、取得した素材の元データは同梱していません。M PLUS 1pフォントには配布元の [SIL Open Font License](Assets/IVRC/ThirdParty/Fonts/MPlus1p/OFL.txt) を同梱しています。

この公開版にプロジェクト全体のオープンソースライセンスは付与していません。各SDKや第三者素材には、それぞれの提供元の利用条件が適用されます。
