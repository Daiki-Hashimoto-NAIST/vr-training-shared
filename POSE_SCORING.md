> 開発時点の設計資料です。現在の実装と異なる記述を含むため、動作の確認にはソースコードを参照してください。公開版での動作検証は行っていません。

# 3点トラッキング・ポーズ採点

## 現在の対象

現在は次の3ポーズを順番に採点する。

1. `FRONT DOUBLE BICEPS`
2. `SINGLE BICEPS`
3. `MOST MUSCULAR`

- HMD
- 左Touchコントローラー
- 右Touchコントローラー

上記3点だけを入力に使う。Movement SDKが推定した肘、肩、腰、脚や、表示中のアバター骨格は採点入力に含めない。FBX差し替えやBody Trackingの推定差で点数が変わらない構成にしている。

## データの流れ

`ThreePointPosingScorer` が毎フレームの最後にTouchコントローラーの生姿勢と追跡有効フラグを取得し、`PosingScoreCalculator` へ渡す。

手の位置は次の順に正規化する。

1. HMD位置を原点にする
2. `StageArrivalPoint` の水平Yawを基準方向にする
3. HMD由来の校正身長で割る

これにより、プレイヤーの立ち位置、ステージのworld位置・向き、身長差の影響を減らす。現在の手とHMDの距離を毎フレーム正規化には使わないため、腕を伸ばす距離の違いは採点に残る。

## 0〜100点の内訳

- Position: 左右コントローラーのHMD相対位置
- Rotation: HMDと左右コントローラーのステージ相対回転
- Symmetry: 左右の手位置関係
- Hold: 成立姿勢を連続して維持した時間

Position / Rotation / Symmetry の加重平均が成立閾値以上の間だけHoldが進む。短い姿勢崩れにはgrace時間があり、それを超えるとHoldを0へ戻す。最終点は4項目の加重平均で、成立しないまま時間切れになった場合はラウンド中の最高点を表示する。

計算本体はScene、`Time`、Meta SDKに依存しない純粋計算として分離している。

## 調整場所

基準値は次の3つのProfile assetにある。

- `Assets/IVRC/Config/FrontDoubleBicepsProfile.asset`
- `Assets/IVRC/Config/SingleBicepsProfile.asset`
- `Assets/IVRC/Config/MostMuscularProfile.asset`

- Target Pose: HMD相対・身長正規化後の左右手位置と3点の向き
- Perfect / Miss Tolerances: 満点範囲と0点になる誤差
- Score Weights: 位置、回転、左右対称、保持の配点
- Required Seconds: 必要な連続保持時間
- Minimum Spatial Score: Holdを進める成立閾値
- Break Grace Seconds: 一時的な姿勢崩れの猶予
- Countdown Seconds: ステージ到着後の準備時間

現在値は実機調整前の仮値。Touchコントローラーの持ち方による回転差を考慮してRotationの許容範囲は広くしているが、回転が完全に外れたまま成立しない配点と閾値にしている。

## HUD

ステージの既存HUDを再利用し、次を表示する。

- 各ポーズの3秒カウントダウンとルーチン進捗
- 対象ポーズ名、簡単な指示、正面から見た基準ポーズ図
- 最初のポーズの準備中だけ、Pose Monitor上部へ
  `大きい人影＝あなた / 小さい人影＝お手本`、`足はその場・腕だけ合わせる`、
  `合ったら、そのまま2秒`を表示。採点開始、2ポーズ目、追跡喪失時には隠す
- 各ポーズの採点開始後、保持成立値の一致度70未満が約3秒続いた時は、そのポーズで一度だけ
  小さい3Dお手本を短く拡大・濃色化し、`お手本の腕を見る → 自分の腕を同じ位置へ
  → そのまま2秒`を約2.5秒再表示する。一致度が改善した時は即座に隠す
- 現在の空間的一致度
- Position / Rotation / Symmetry の個別点
- 成立状態と残り保持秒数
- 各ポーズ確定後、約0.9秒だけ `成功！ / 次のポーズを準備`、または
  `時間です / 次のポーズへ` を表示
- Resultsで最終ポーズ点、成立可否、運動回数、筋肉ゲージ

各ポーズの確定結果は `PoseCompleted` の不変スナップショットで一度だけ通知する。
次ポーズのCountdownが始まっても直前結果を復元せず、Tracking lossでは通知しない。
最後のポーズ成立時はResultsへ早く進む。制限時間終了時はLateUpdateの最後の採点を
1フレーム待ってからResultsへ進む。

適応ヒントは表示だけを補助し、採点値、Hold積算、Pose制限時間、Profileの閾値を
変更しない。Countdown、Holding、追跡喪失、Pose確定、次Pose、Retryでは停止し、
追跡がない時間を「迷っている時間」へ含めない。復帰後は約0.35秒だけ有効な
頭・両手を確認し、採点、Hold、Pose制限時間、全体Phase時間を止めたまま
`認識が戻りました / そのまま待つ`を表示してから同じポーズへ戻る。
復帰途中で再び追跡が外れた場合は安定待ちを最初からやり直す。新しい操作や
音素材は要求しない。

基準ポーズ図は `PoseReferenceGuide` が
`FrontDoubleBicepsProfile.asset` の左右手ターゲット位置から生成する。
頭、左右手、肩から手までのライン、胴体を軽量なuGUI図形で表示し、
追加カメラ、RenderTexture、外部画像は使わない。ターゲット位置を調整すると
採点設定と見本図が同時に更新される。見本図はPosing中だけ表示し、Resultsでは
非表示になる。

Pose Monitor内の本人アバターを主、現在Profileから作る半透明3D見本を
横の小型 `お手本` とする。`PoseGhostGuideController` の3D見本が表示できる間は
2D基準ポーズ図を隠し、生成失敗や無効化で3D見本が利用できない場合だけ
`PoseReferenceGuide` が2D見本へ自動フォールバックする。これにより、本番中に
強い見本を二つ同時表示しない。Profile調整時は `AlwaysShow`、完全に隠す場合は
`Hidden` を選べる。詳細は
[体験・演出統一方針](EXPERIENCE_DIRECTION.md)を正とする。

### 見本図のUI調整

コンポーネントは
`Phase_Posing/PoseHUD/StageFlowHUD` の `Pose Reference Guide` にある。
現在のシーン保存値は次のとおり。

- Display Mode: `AutomaticFallback`
- Primary Guide: `PoseGuideRoot` の `PoseGhostGuideController`
- Posing Content Width: `716`
- Other Content Width: `716`
- Head Position: `(0, 45)`
- Pixels Per Normalized Unit: `200`
- Shoulder Half Width Normalized: `0.12`
- Shoulder Drop Normalized: `0.13`
- Arm Line Thickness: `8`

左右手の見本位置は、ポーズプロファイルのHMD相対座標X/Yへ
`Pixels Per Normalized Unit`を掛け、Head Positionへ加算して決める。
肩位置はHead Position、Shoulder Half Width、Shoulder Dropから作り、
肩から手までのラインの長さと角度を自動更新する。プロファイルのZは
正面図には投影しないが、実際のPosition採点には引き続き含まれる。

`PoseReferenceVisual/Silhouette` 配下のTorsoとShoulderLine、各マーカーの
大きさと色は、それぞれのRectTransform / Imageで調整する。Torsoと
ShoulderLineは固定図形で、プロファイルに合わせて動くのは左右手と
肩から手までのラインである。

プロファイルを編集した結果は、Play開始またはコンポーネント再有効化時に
反映される。Edit Modeですぐ確認する場合はコンポーネントのContext Menuから
`Refresh Pose Reference Guide` を実行する。見本図のためだけにTarget Poseを
変更すると採点基準も変わるため、見た目だけの大きさ調整には上記UI項目を使う。

## 自動テスト

`PosingScoreCalculatorTests` で以下を検証する。

- 正解姿勢は100点、大きく外れた姿勢は低得点
- world平行移動、ステージYaw、体格スケールに対する不変性
- HMD身長と腕長のfallback
- 左右対称性
- Holdの積算、grace、追跡喪失後のreset
- すべての点数が0〜100に収まること

現在は採点計算のEditMode 13件に加え、`PoseReferenceGuideTests` 11件、
初見向け適応ヒントの状態テスト、実シーン配線テストを含む全EditMode 465件が
成功している。

`PoseReferenceGuideTests` では、プロファイルからの投影、腕ラインの長さと角度、
不正値の無害化、実際のRectTransformへの反映、3D見本の有無に応じた
2Dフォールバックを検証する。

## Quest実機で確認・調整する項目

1. ステージ到着後、HMDと左右Touchがすべて有効になりカウントダウンが進む
2. 両手を頭の横に上げた正解姿勢でPositionとSymmetryが十分高くなる
3. 両手を下げた姿勢、片手だけずらした姿勢で明確に点が下がる
4. 2秒保持で成立し、途中で崩すとgrace後にHoldが戻る
5. コントローラーを追跡範囲外へ出すと時間と採点が一時停止する
   復帰方法と同時に `痛みがあれば腕を下ろして止まる` が残る
6. 身長の異なるメンバーでも同じ姿勢の点が大きく変わらない
7. コントローラー回転点が持ち方だけで極端に変化しない
8. HUDが視界に収まり、文字が小さすぎない
9. Posingのカウントダウンから採点中だけ見本図が表示され、Resultsでは
   非表示になって文字領域が元の幅へ戻る
10. 見本図の頭・左右手・腕ラインがパネル内に収まり、採点文字や
    成長ゲージと重ならない
11. 左右手の図が正面向きの基準として誤解なく読め、実際のTarget Positionと
    同じ高さ・広がりを示している
12. Target Position調整後に見本図も更新され、X/Yの変更は図へ、Zの変更は
    採点だけへ反映される
13. 最初の3秒で自分とお手本、足を動かさないこと、2秒保持を読める。案内が
    観客に隠れず、採点開始後と2ポーズ目以降は本人と見本を遮らない
14. 各ポーズで腕を合わせず約3秒待つと再案内が一度だけ出て、小さいお手本が
    見つけやすくなる。腕を合わせる、追跡を失う、保持へ入る、または次ポーズへ
    進むと即座に通常表示へ戻り、同じポーズでは繰り返さない

実機確認後は、まずTarget PositionとPosition toleranceを調整し、その後にRotation targetと重みを詰める。
