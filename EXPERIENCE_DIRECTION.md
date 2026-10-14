> 開発時点の設計資料です。現在の実装と異なる記述を含むため、動作の確認にはソースコードを参照してください。公開版での動作検証は行っていません。

# VR Training 体験・演出統一方針

更新日: 2026-08-12  
対象シーン: `Assets/Scenes/VRTrainingPrototype.unity`
状態: 縦切り実装の基準と、Quest初見テスト前の受入条件

## 1. この文書の役割

この文書は、VR Trainingのプレイヤー体験を横断する判断基準を定める。
対象は次の4領域である。

- 運動・ポーズ・結果を通したフィードバックの統一
- Training後の変身演出
- ステージ入場と3種ポージング
- アバター、環境、UI、VFXを含むアート方向の統一

個々の判定式、BPM、許容角度、スコア配点などの調整値は、この文書へ
重複させない。実装コード、SceneのSerialize値、ScriptableObject、および
各機能資料を正とする。この文書は「何のために、どう見せ、何を壊しては
ならないか」を定める。

本文では次の区分を使う。

- **決定**: 実装の前提として固定する方針
- **初期案**: 最初の縦切り実装で採用し、実機検証で調整する方針
- **検証事項**: Quest実機または初見参加者でなければ確定できない事項
- **保留**: アセットまたは外部条件が揃うまで固定しない事項

本方針は製品仕様であり、作業者向けの永続ルールではないため、
`AGENTS.md` へ重複して記載しない。

## 2. 体験の中心

### 2.1 一文で表す体験

**リズムに合わせた自分の運動が、その場で自分の肉体を育て、その成果を
ステージで披露して観客に認められるVR体験。**

すべての追加演出は、次の因果を強める場合にだけ採用する。

`自分が動く → 認識されたと分かる → 肉体が変わる → 変身が完成する →
ステージで見せる → 評価される`

単独では派手でも、この因果を隠すUI、VFX、カメラ演出は採用しない。

### 2.2 感情の流れ

1. Setup / Ready: 「自分の体が正しく入った」という安心
2. Training: 「今の動きが通じた」という手応え
3. Transformation: 「自分が鍛え上がった」という驚き
4. Stage Entrance: 「この肉体を見せに行く」という期待
5. Posing: 「見本へ合わせて観客を沸かせる」という挑戦
6. Results: 「自分の運動が結果になった」という納得

Trainingを長い説明、Transformationを単なる待ち時間、Posingを別ゲームに
しない。この感情の勾配が一周を通して上がることを優先する。

### 2.3 変更してはならない土台

- 対象はQuestの立位体験で、装着と説明を含め約5分以内に一人が完了できる。
- HMDカメラへ揺れ、フリーズ、強制回転、見える状態での急な移動、
  カメラAnimationによる三人称移動を加えない。
- フェーズ間の場所変更だけは、両眼を完全に黒くした間にTrackingSpaceを
  絶対位置へ再配置してよい。移動前後で頭部追従と身体の相対位置を維持する。
- 追跡喪失は参加者の失敗として採点・表示しない。
- プレイヤー本人のアバターと肉体変化を常に主役にする。
- 同時に強く見せる主メッセージは一つにする。
- 本番表示とデバッグ表示を分離する。
- 実機での快適性、視認性、フレーム維持を、静止画上の豪華さより優先する。

### 2.4 現行タイムラインの基準

現行Sceneでは、Setup / ReadyとResultsを除く自動進行部分はおよそ2分9秒で
ある。これは今後の固定値ではなく、5分展示枠に対する現状確認用の基準である。

| 区間 | 現行の基準 | 備考 |
| --- | ---: | --- |
| Setup / Ready | 任意 | A、X、Bと追跡準備を待つ |
| Countdown | 3秒 | 運動開始の準備 |
| Training | 約74.75秒 | 120 BPM、148拍、完了猶予を含む現行譜面 |
| Transformation | 3秒 | 報酬の見せ場 |
| StageTransition to Judges | 約1.15秒 | 全黒中だけTrackingSpaceを整列 |
| StageEntrance | 6.5秒 | 審査員席から歩行アバターを見る |
| StageTransition to Stage | 約1.15秒 | ステージ上へ整列 |
| Posing | 最大約39秒 | 3ポーズ ×（3秒準備 + 最大10秒） |
| Results | 任意 | Bで次の参加者向けにリセット |

短縮する場合もTransformation、入場、各ポーズ成功の「意味が切り替わる間」は
削らず、Trainingの反復数や待ち時間から先に調整する。

## 3. フェーズ別の体験契約

各フェーズは、参加者がその瞬間に答えるべき質問を一つだけ持つ。

| フェーズ | 参加者の質問・行動 | 主表示 | 結果と次の案内 |
| --- | --- | --- | --- |
| Setup | 自分の位置と身長は合っているか | AとXの校正状態、追跡状態 | 両方整ったらBを案内 |
| Ready | いつ始めるか | Bで開始する一文 | Countdownへ |
| Countdown | 構える時間はあるか | 大きな3、2、1と最初の種目 | Trainingへ |
| Training | 次に何を、いつ上げ下げするか | 線画ガイド、UP/DOWN、短い確定判定 | 小さな成長と次の動作 |
| Transformation | どれだけ変わったか | 鏡の中の最終肉体と一つの見出し | ステージへ向かう合図 |
| StageTransition | 何が起きるか | 全黒フェードと短い音 | 新しい場所で自然に復帰 |
| StageEntrance | 鍛えた肉体がどう見えるか | 審査員席から歩く自分を直接見る | ステージへ立つ準備 |
| Posing | どのポーズへ合わせ、あと何秒保つか | 本人の映像、小さな見本、Match/Hold | 観客反応と次のポーズ |
| Results | 何が成果になったか | Rank、総合、Training、Pose、Growth | Bでリトライ |

### 3.1 情報量の上限

- Setup / Readyでは、校正と開始以外のゲーム数値を見せない。
- Trainingでは、次の動作とタイミングを第一、確定反応を第二、進捗を第三とする。
- Transformationでは通常HUDを弱めて肉体へ視線を戻す。StageEntranceでは
  Pose Monitorと見本を隠し、審査員席へ向けた短い受動案内だけを残す。
- Posingでは、見本、現在の一致、保持だけを本番情報にする。
- Resultsでは内部の角度、POS / ROT / SYM、ハードウェア状態を見せない。
- Body Metrics、個別角度、接続詳細、カフ内部状態はデバッグモードだけにする。

### 3.2 初見参加者についての現状判断

**実装上の主要な迷いは解消したが、スタッフ説明なしの初見合格はQuestで未検証。**
現在の本番表示は次のJust-in-time導線を持つ。

| 箇所 | 現在の本番導線 | 残る実機確認 |
| --- | --- | --- |
| Setup / Ready | `準備 0/3`から頭と両手の認識・立ち位置・身長をCheck list化し、`右手の下側 [A]を押す`、`左手の下側 [X]を押す`、`右手の上側 [B]を押す`を一つずつ表示する。A / X受理時は約0.9秒だけ緑の完了文を出し、次の案内へ進む。下部には`歩かない・周囲確認`と、気分不良や痛みの際の`腕を下ろしてスタッフへ伝える`を常時表示する | 手・上下・ボタン名の一文だけで実物のボタンを即座に見つけられるか、完了文だけで受理を十分理解できるか |
| Avatar変更 | `左Y アバター変更（任意）`を必須操作より小さく表示 | 初見参加者が校正より先に触らないか |
| Countdown / Training | 3、2、1の後と各種目切替で、未採点の線画を`下ろす → 上げる → 下ろす`と一往復だけ見せ、`次の「上げる」から採点`を表示する。採点中は種目名の隣へ`今：上げる / 下ろす`を常時出す。同じ種目でUP / DOWNを2点連続して取り逃した時は一度だけ線画を強調し、`線画を見て再開`を出す。見本は運動回数へ含めず、`足はその場`と痛み時の停止を同じPanelへ残す | 初見参加者が見本と採点開始を区別し、適応ヒント後にスタッフ説明なしで運動へ戻れるか |
| 1回の運動結果 | UP / DOWNの個別評価に加え、未完了なら`上げてから、下ろし切る`、動作完了・リズム未達なら`動きOK　次は拍に合わせる`、成功なら`リズム成功`を一度だけ表示する | 表示を待つために運動を止めないか、振動が強すぎないか |
| Tracking復帰 | 採点を止め、`プレイ位置に戻り、正面を向いて両手を前へ`と具体的に案内し、安全停止文も残す | Quest上で左右どちらを戻すべきか十分早く判断できるか |
| Transformation / 移動 | `成果を確定 → 鍛えた体をチェック → MUSCLE UP!`と切り替え、すべて`その場`または`歩かず`を明記 | 右側HUDではなく鏡の肉体へ自然に視線が戻るか |
| StageEntrance | `歩かず、その場でステージを見てください`と次の`3ポーズ`だけを、審査員席へ正対した案内に表示 | Android Questで文字サイズと入場アバターへの視線誘導を確認 |
| Posing | 最初の準備中だけPose Monitor上部へ`大きい人影＝あなた / 小さい人影＝お手本`、`足はその場・腕だけ合わせる`、`合ったら、そのまま2秒`を表示する。各ポーズで実際の保持基準未満が約3秒続いた時は一度だけ小さいお手本を強調し、腕の合わせ方を再提示する。その後は`POSE n/3`、合わせる・保持・成功・追跡復帰へ絞る | 見本の大きさと文字が実機距離で読めるか、再案内からスタッフ説明なしで保持へ戻れるか |
| Results | 確定した総合点を0.6秒だけ数え上げ、途中からRankを一度だけ表示する。Training、Pose、Growthの説明後、`体験終了・スタッフの案内でヘッドセットを外す`を主表示し、`右Bでもう一度`は小さな任意操作にする。低ランクも罰や危険に見える赤を使わない | Android Questで短い確定演出と終了案内を読み終えてから自然にスタッフへ注意を戻せるか |

したがって、次の受入判定はAndroid standalone版をプロジェクト未経験者へ渡し、
スタッフは装着と安全説明だけを担当して行う。まず3人で探索的に問題箇所を見つけ、
修正後は少なくとも5人中4人が追加のゲーム操作説明なしで完走し、同じ箇所で2人以上が
止まらないことを最初の合格目安とする。止まった場合はスタッフ説明を増やすのではなく、
直前のUI、見本動作、確定反応を修正する。

### 3.3 初見導線の共通契約

初見参加者へ長い説明画面や独立したTutorial Sceneを見せない。必要な操作を、
必要になる直前に一つずつ教え、実際の体験内で成功させる。

各フェーズの本番UIは、常に次の順で情報を持つ。

1. **現在地**: `準備 1/3`、`POSE 2/3`など、短いフェーズ名と進行
2. **今すること**: 一つの動詞から始まる主指示
3. **やり方**: 手、ボタン、姿勢、タイミングのうち必要なものだけ
4. **できた合図**: Check、確定音、短い振動、色変化のうち適切な組合せ
5. **次の一手**: 現在の成功後に初めて強調する

表示規則:

- 主指示は同時に一つだけ強調する。完了済み項目はCheck付きで小さく残す。
- ボタンは単独の文字だけにせず、左右の手とFace Buttonの上下位置を併記し、
  `右手の下側 [A]を押す`のように手・位置・ボタン・動作を一文で示す。実機の
  写真やSpriteを導入する場合も、この四情報は維持する。
- IVRC展示の初期案は日本語を主表示、種目・ポーズの英語名を小さな副表示とする。
  コピーは将来の言語切替に備えてPresenterへ直書きせず設定へ集約する。
- 一文はHMD内で一度に読める長さにし、詳細な理由より具体的な身体動作を優先する。
- 色だけで状態を伝えず、Check、Pause、Controller、矢印などの形を併用する。
- 自動演出中は不要なボタン案内を消し、見る対象だけを短く示す。
- 参加者が止まった場合は、最初に現在の操作を動きで再提示し、それでも進まない
  場合だけ補足文を一段増やす。正確な待ち時間は実機検証で設定する。
- 任意操作は必須操作と同じ強さにしない。Avatar切替はSetupの補助表示または
  スタッフ用表示へ下げ、校正・開始の導線を妨げない。

### 3.4 Setup / Readyの操作説明

Setupは一枚の説明文ではなく、同じパネル内の3段階Check listとして見せる。
一度に強調するのは現在の一段だけとする。

| 段階 | 主表示の初期コピー | 補助表現 | 完了表示 |
| ---: | --- | --- | --- |
| 追跡待ち | `プレイ位置で正面を向き、両手を前へ` | まず文章で案内。必要なら後からHMDと左右Controller図 | `✓ 頭と両手の認識` |
| 1 / 3 位置 | `正面を向いて、右手の下側 [A]を押す` | 鏡中央の足位置 | `✓ 正面合わせ` |
| 2 / 3 身長 | `背すじを伸ばして、左手の下側 [X]を押す` | 直立Silhouette | `✓ 身長合わせ` |
| 3 / 3 開始 | `準備OK。右手の上側 [B]を押す` | 位置・身長のCheckを残す | Countdownへ |

校正入力が受理された瞬間には、対象行をCheckへ変え、約0.9秒だけ
`✓ 位置合わせ完了`または`✓ 身長合わせ完了`を表示する。次のButton mapは同時に
残すため、受理確認のために同じボタンを連打させない。短い確認音または弱い一回振動は
Quest初見テストで視覚表示だけでは不足した場合に足す。押した後も同じ指示が残る、
Cameraだけが動いたように見える、成功したのに次へ進まない、という曖昧な状態を作らない。

校正失敗時は `FAILED` や内部理由だけを出さず、次に直す身体動作を示す。
例は `両手を見える位置へ戻す`、`正面を向いて立ち直す`である。再試行で
完了済みの別項目を不必要に消さない。

### 3.5 Trainingの教え方

Trainingでは文章を読みながら運動させない。現行Chartが持つ最初の4 Lead-in
beatsと各Block間の4 Transition beatsを、採点されない説明時間として使う。

最初のLead-in:

1. Countdownで正面を向き、腕を下ろす開始姿勢を表示する。
2. Trainingへ入ったら種目名と`線画を1回まねる`を表示する。
3. 採点外の線画を`下ろす → 上げる → 下ろす`の3拍で見せる。
4. `次の「上げる」から採点`を表示し、見本の一往復は運動回数へ含めない。
5. 最初の採点対象が始まる時点で説明文を消し、種目名の隣に
   `今：上げる / 下ろす`を一つだけ表示する。

種目切替:

- Transition beatsへ入ったら現在の成功表示を弱め、`次: サイドレイズ`のように
  次種目を最優先にする。
- 種目名だけでなく、採点外の一往復を最初と同じ線画で示す。
- 切替後の最初の採点前に、参加者が開始姿勢へ戻る時間を確保する。
- Ring modeとLine-art modeの説明コピーを分ける。Line-art modeで
  `MATCH THE RING`を表示しない。

運動中の本番表示は、次の3層以内に収める。

- 主: 現在の線画と `今：上げる`または`今：下ろす`
- 副: 確定した一回だけの `PERFECT / GOOD / OK`、`REP COMPLETE`
- 予告: 種目切替が近い時だけの次種目名

同じ種目でUP / DOWNを2点連続して取り逃した場合は、参加者が動きを
見失ったものとして、そのBlockで一度だけ既存線画を約2.5秒強調する。
表示は `線画を見て再開` と具体的な一文へ置き換え、正しい次動作を検出した時点で
即座に通常表示へ戻す。これは採点救済ではなく操作理解の救済であり、譜面時刻、
成長量、確定済みの評価は変更しない。追跡停止中には連続未入力として数えず、
復帰時に過去のヒントを再発火させない。

追跡喪失中は採点と譜面を止め、失敗音を出さない。`両手をHMDから見える位置へ`
のように復帰動作を示し、左右どちらが不足しているか取得できる場合は該当する
Controller iconだけを点灯する。

### 3.6 Transformation / Stage Entrance

Transformation、黒画面遷移、Stage Entranceは操作区間ではない。入力を促さず、
参加者が「待たされている」のではなく「結果を見ている」と理解できる最小限の
見出しだけを使う。

- Transformation開始: `トレーニング完了`から`変身中`へ短く切り替える。
- Reveal中: `鏡の上半身を見てください`だけを残し、鏡の肉体と照明へ視線を集める。
- Hero Hold: `MUSCLE UP!`と`次はステージで3ポーズ`を一度だけ示す。
- Entrance中: `変身後のあなた`、`歩かず、その場でステージを見てください`、
  `このあと お手本に合わせる3ポーズ`だけを残す。空の進捗Meterやボタン案内は出さない。
- 黒画面中: 新しい操作を要求しない。復帰後の最初の必要行動は、画面が戻って
  から案内する。

### 3.7 Posingの操作説明

Pose Monitorは、初見でも「どちらを見て、何を真似するか」が一目で分かる
構成にする。

- 大きいLive avatarへ `あなた`、小さい3D referenceへ `お手本`を固定表示する。
- 最初のポーズのCountdownだけは、観客に隠れないMonitor上部へ
  `大きい人影＝あなた / 小さい人影＝お手本`、`足はその場・腕だけ合わせる`、
  `合ったら、そのまま2秒`を示す。採点開始、2ポーズ目、Tracking loss、
  完了表示では消し、復帰案内や本人映像と競合させない。
- 上部に `POSE 1 / 3`、日本語の短い動作名、英語名を小さく表示する。
- 指示文は3点Trackingで実際に判定できる腕の形だけにする。胴体のひねり、脚、
  手首の厳密な向きは要求しない。
- Match percentageの常時表示より、`お手本に合わせる`、`そのまま2秒`、`成功!`
  という状態変化を優先する。
- 各ポーズの採点開始後、保持成立値の一致度70未満が約3秒続いた場合、そのポーズで一度だけ
  小さい3Dお手本を短く強調し、`小さいお手本の腕を見る / 自分の腕を同じ位置へ /
  合ったら、そのまま2秒`を再表示する。再案内の一致基準は現在のPose Profileが持つ保持開始基準から読み、一致改善、Holding、追跡喪失、Pose確定、
  次Pose、Retryでは即座に解除し、採点・Hold・制限時間は変えない。
- Hold中は残り時間をRingまたはBarで連続表示し、文字の秒数だけに依存しない。
- Tracking loss時は保持をPauseし、`両手を前へ戻す`とController iconを出す。
  復帰後は同じポーズから再開し、Tracking lossを失敗表示にしない。
- Timeoutは罰として大きく見せず、短い再案内の後に次のポーズへ進む。

3ポーズの初期コピーは次を基準にし、最終文言は実際のIK見本と同時に調整する。

| ポーズ | 主指示 |
| --- | --- |
| Front Double Biceps | `両こぶしを頭の横へ / ひじを高く広げる` |
| Single Biceps | `右こぶしを上へ / 左手を腰の前へ` |
| Most Muscular | `両こぶしをお腹の前へ / ひじを外へ` |

### 3.8 Resultsの説明

Resultsは数値を並べるだけでなく、体験中の行動との対応を短く示す。

- `TRAINING`: 動作完了とリズム精度
- `POSE`: お手本との一致と保持
- `GROWTH`: Trainingで獲得し、Avatarへ反映された変化量

Rankと総合点を最優先にし、上記3項目は一行の補助説明またはIcon付きLabelへ
まとめる。終了後の操作は、右Controllerの図とともに`右Bでもう一度`と示す。
展示運用でスタッフだけがResetする場合も、同じ画面をStaff modeで切り替え、
体験者へ不要な管理操作を見せない。

確定時は総合点を0.6秒だけ数え上げ、75%地点でRankを表示する。同じ
`ResultCompleted`が再通知されても巻き戻さず、音、振動、照明を追加して最終
ポーズの祝福と競合させない。

### 3.9 スタッフ説明との境界

スタッフが担当してよいのは、HMD装着、安全範囲、体調確認、緊急停止である。
次の内容を毎回口頭で補う必要があるなら、UIは未完成と判断する。

- A、X、Bを押す順番と左右の手
- 線画、UP / DOWN、判定の読み方
- 種目が切り替わること
- Posingで本人とお手本のどちらを見るか
- Tracking lossからの戻り方
- Resultsの主要項目の意味

ただし、参加者の安全やHMD装着不良に関する介入はUI失敗へ数えない。ゲーム操作の
ヒントを与えた回数はPlaytest記録へ残し、同じ箇所で複数人が止まった場合にUIを
修正する。

### 3.10 初見導線の受入条件

「迷わない」は設計レビューだけでは確定しない。Android standalone版を、
プロジェクトを知らない初見参加者で検証して初めて合格とする。

初期の合格基準:

- スタッフは装着と安全説明だけを行い、ゲーム操作を口頭で教えない。
- 参加者が手・上側 / 下側・Button名の一文だけでA、X、Bの位置を探し、順に自力で
  完了して各操作の成功を認識できる。
- 最初の採点対象までに、最初の種目、開始姿勢、UP / DOWNの意味を理解できる。
- 種目切替時に動作を止め続けたり、前種目を継続したりしない。
- Tracking lossから、失敗と誤解せず案内だけで復帰できる。
- 最初の3秒でPose Monitor上の`あなた`と`お手本`、腕だけ合わせること、2秒保持を
  理解し、その後の3ポーズで次の動作を見失わない。
- ResultsでTrainingとPoseが別評価であることを説明できる。
- 一周の所要時間が装着・安全説明を含め5分枠へ収まる。

少なくとも5人の初見参加者で、時間、操作ヒント回数、停止した箇所、誤った動作、
Tracking復帰時間を記録する。4人以上がゲーム操作の追加説明なしで完走し、同じ
箇所で2人以上が停止しないことを最初の目安とする。未達の場合は参加者へ説明を
増やすのではなく、該当する直前のUI、見本動作、確認反応を修正する。

## 4. フィードバック統一

### 4.1 基本の文法

すべてのプレイヤー向け反応を次の順序で設計する。

`行動 → システムによる確定 → 即時反応 → 成長またはスコアへの結果 → 次の指示`

「確定前の予測表示」と「確定後の判定表示」を同じ場所・同じ語で出さない。
過去に発生した `MISS → OK` や `OK → PERFECT` の見かけ上の反転を防ぐため、
評価語は `TrainingRhythmScorer.TimingFeedbackReady` の確定後に一度だけ出す。

### 4.2 意味の分類

色だけに依存せず、語、形、動きも同じ意味へ揃える。正確なRGB値はアート
パレット確定時に一つのStyle設定へ集約する。

| 意味 | 色の役割 | 動き | 用語例 |
| --- | --- | --- | --- |
| 次の指示・基準 | Cyan系 | 一定速度の収縮、点灯 | NEXT、UP、DOWN |
| 認識済み | White系 | 一度だけ短く広がる | REGISTERED、REP |
| 成功・獲得 | GoldまたはGreen系 | 小さな立ち上がりと収束 | PERFECT、CLEAR |
| 調整中・保持 | Amber系 | 連続するが点滅しない進捗 | HOLD、ALMOST |
| 追跡停止 | 低彩度Amber系 | ゆっくり静止 | TRACKING PAUSED |
| 装飾・興奮 | Magenta系 | 環境側だけで穏やかに増幅 | ステージ照明 |

通常のタイミングMISSへ強い赤や罰音を使わない。赤は安全上の停止や回復不能な
エラーへ予約する。色覚差を考え、成功にはチェック、保持にはリング進捗、停止には
一時停止記号を併用する。

### 4.3 「1回完了」と「リズム成功」を分ける

**決定:** 運動の完了とリズム上の成功は、別の事実として扱う。

- `REP COMPLETE`: 必要な左右が低→高→低を完了した身体動作の事実
- `RHYTHM CLEAR`: 同じ反復のUPとDOWNが両方ともOK以上だったリズム成果
- UP / DOWNの `PERFECT / GOOD / OK / MISS`: 各タイミング点の確定評価

これにより、運動自体はできたが拍を外した参加者へ、矛盾した「失敗」を返さない。
成長量は従来どおり各UP/DOWNの精度から決まり、`RHYTHM CLEAR` はその結果を
分かりやすくまとめる表示であって、別の加点を勝手に作らない。

`TrainingRhythmScorer.RepTimingResultReady` がUP/DOWNを同じClock Session・
Chart Note単位で集約し、身体完了とリズム成功を一度だけ確定する。集約前の
`BodyRepCounter.TimingPointReached` や連続更新される `ScoreChanged`、
`GrowthApplied` を成功演出の直接トリガーにしない。

新しい集約結果は `TrainingRhythmScorer` が所有し、
`RepTimingResultReady(TrainingRepTimingResult result)` として一度だけ通知する。
結果には最低限、次を含める。

- Clock Session IDとChart Note Index。一回性を保証する複合Key
- 運動種別とRequired Sides
- UP / DOWNそれぞれの全Required Sides解決状態、Accuracy、Grade
- `MotionCompleted`: 必要な低→高→低が完了したか
- `RhythmCleared`: UP / DOWNがともにOK以上か
- `FinalizationReason`: CompletedまたはTimedOut

同じ側の重複通知は既存Runtime Note内で無視し、集約結果も同じKeyで再発行しない。
Tracking loss中はNoteの期限を進めず、結果を確定しない。復帰後に通常継続する。
表示は次のように固定する。

| 最終状態 | 表示 | 強い成功反応 |
| --- | --- | --- |
| 動作完了、UP/DOWNともOK以上 | `REP COMPLETE` + `RHYTHM CLEAR` | あり |
| 動作完了、どちらかがOK未満 | `REP COMPLETE` | 弱い完了反応だけ |
| 必要な側が揃わずTime out | 短い次動作案内 | なし |
| Tracking loss | `TRACKING PAUSED` | なし、結果も未確定 |

### 4.4 反応の強さ

| レベル | 事象 | 主な反応 |
| ---: | --- | --- |
| 1 | 拍、次のUP/DOWN | 線画と短い通常振動 |
| 2 | UP/DOWN確定 | 小さな評価語と短い音 |
| 3 | REP COMPLETE / RHYTHM CLEAR | 1回だけの文字・輪郭・成功振動 |
| 4 | 種目ブロック完了 / 1ポーズ成功 | 環境、観客、音を含む短い祝福 |
| 5 | Transformation / 3ポーズ完遂 / Results | 一周で数回だけの最大演出 |

高いレベルの反応中は、低いレベルの見出しを一時的に抑える。ただし次の拍の
内部時刻を遅らせない。同じフレームで拍と成功振動が競合した場合は成功振動を
優先して拍を置換し、二つを加算して不快な強さにしない。

同時に許可するのは、原則として「主反応1個 + 補助反応2個」までとする。
例として、REP成功時は短い文字、短いハプティクス、アバターの成長で完結させ、
部屋全体の照明や大きなパーティクルまで毎回動かさない。

### 4.5 チャンネルごとの責務

| チャンネル | 担当 | 担当しないこと |
| --- | --- | --- |
| UI | 次の行動、確定評価、進捗 | 肉体変化の代替、内部値の常時表示 |
| アバター | 運動の結果である体型・表面変化 | 次のタイミングの説明 |
| ハプティクス | 拍、確定瞬間、節目 | 長時間の連続振動、罰 |
| Audio | 拍の補助、成功、変身、歓声 | 同じ意味の音を複数重ねること |
| Lighting | フェーズ、変身、ステージの盛り上がり | 毎回の細かな採点値の点滅表示 |
| Audience / Judges | 公開評価と舞台の反応 | Training中の内部判定 |

### 4.6 イベントの正と所有者

| 意味 | 正とする既存の送信元 | 実装上の扱い |
| --- | --- | --- |
| フェーズ開始・終了 | `GameFlowController.PhaseChanged` | 全Presenterの表示・reset基準 |
| 拍 | `RhythmClock.BeatOccurred` | 音、触覚、ガイドで同じDSP時計を共有 |
| UP/DOWN確定評価 | `TrainingRhythmScorer.TimingFeedbackReady` | 一度だけ表示 |
| 反復全体の結果 | `TrainingRhythmScorer.RepTimingResultReady` | REPとRHYTHMを同時に確定 |
| 適用済み肉体状態 | `MuscleGrowthController` | Overallと全Channelの表示値を読む |
| 現在のポーズ | `ThreePointPosingScorer.PoseChanged` | 見本と指示を同時に切り替える |
| ポーズ一致 | `ThreePointPosingScorer.ScoreChanged` | 連続反応。成功確定には使わない |
| ポーズ完了 | Scorerへ追加する一回性の完了通知 | ライト、観客、音、HUDを同期 |
| 最終結果 | `ExperienceResultController.ResultCompleted` | Results中は再計算しない |

Gameplay側をUI、音、ライトへ直接依存させない。既存イベントを薄い
Presentation Signal層で意味へ変換し、フェーズ別Presenterが表示する。
巨大な万能Managerへ全機能を集約するのではなく、イベントの優先度と一回性だけを
共通化する。

### 4.7 追跡喪失

- TrainingとPosingの進行、拍、採点、保持を停止する。
- 直前の確定済み回数とスコアは維持する。
- 未確定の動作や保持は安全に破棄または猶予後に戻す。
- `MISS`、ブザー、赤い失敗演出、成功振動を出さない。
- 本来見るべき位置に `TRACKING PAUSED` と復帰方法だけを表示する。
- 復帰後は約0.35秒の安定待ちを置き、採点、保持、個別Pose窓、全体Phase時間を
  止めたまま途中姿勢で誤カウントしない。認識が戻ったことと、そのまま待つことを
  失敗とは別の表示で伝える。

## 5. 変身演出

### 5.1 目的

Transformationは成長値を追加するフェーズではなく、Trainingで獲得済みの
変化を、参加者が明確に認識するための報酬フェーズである。

**決定:** Training終了時に確定したOverallと部位別Channelを凍結し、
Transformation、入場、Posing、Resultsの全表示コピーが同じSnapshotを使う。
演出はこの値を見せるだけで、Shape Keyへ加算しない。

`MuscleGrowthSnapshot` はOverall、ElbowFlexion L/R、ArmRaise L/R、Chest L/Rを
保持し、`GameFlowController` がTraining終了境界で一度だけ保存する実装になっている。
`MuscleGrowthController` はTraining中だけScorerのライブChannelを読み、
Transformation以降はこのSnapshotだけを読む。

### 5.2 造形と表面の役割

- Shape Key / BlendShape: 肩幅、上腕の太さ、胸郭など大きな輪郭と体積
- Normal / Detail Normal: 筋溝、筋繊維、細かな凹凸
- Roughness: 筋肉の面と稜線へ出るハイライトの差
- Optional Mask: 胸、肩、上腕など部位別の表面強調
- Lighting: 既に存在する形状と法線を読ませる手段
- VFX / UI: 「確定した」ことを伝える手段

Albedoへ強い筋溝の暗線を描いて立体を代用しない。大きな輪郭をNormalや
発光で偽装せず、細部テクスチャでシルエットを作ろうとしない。

ゲーム上の成長値は常に `0 = 通常体型、1 = 獲得した最大体型` と定義する。
Unity上のShape weightが100→0で筋肉化するアバターもあり得るため、weightの
増減方向をゲーム仕様にしない。各Avatar Definitionの明示Bindingが意味変換を
担当する。

### 5.3 3秒演出の初期案

長さを変更しても比率を維持できるよう、正規化時間で構成する。

1. **Lock（0〜15%）**  
   Trainingのガイド、拍、判定文字を止める。鏡と本人だけを残し、既存ライトを
   少し落として結果を見る間を作る。直前の成功振動と競合する開始パルスは出さない。
2. **Reveal（15〜70%）**  
   Earned Snapshotは変えず、既存のTraining Key / Fillの強度と色を滑らかに
   補間して上半身を読ませる。点滅、全身発光、画面フラッシュは使わない。
3. **Hero Hold（70〜100%）**  
   最終状態へ正確に収束させ、`MUSCLE UP!`、次のステージ導線、通常拍より
   明確な両手パルスを1回だけ出す。決め音はAudioClipが用意された時だけ鳴らす。

現行の `TransformationPresentationController` は、この照明Envelope、同じEnvelopeへ
追従する既存鏡枠の色、任意のReveal / Hero AudioClip、Hero両手パルスを所有する。
鏡枠は `MaterialPropertyBlock` だけで基準色からCyanへ滑らかに変わり、Hero Holdでは
弱い色を残す。終了、Retry、無効化でライトと鏡枠を記録済みの基準へ戻し、共有Material、
Avatar、Camera、OVRCameraRig、TrackingSpaceは変更しない。点滅、Emissionの有効化、
画面Flashは使わない。既存肌Normalの強度とSmoothnessは成長Snapshotへ追従させるが、
Detail Normal / Roughness texture自体の演出係数は対応Materialが揃うまで保留する。
Hero両手パルスは一度だけ、0.20秒以下へ強制し、設定ミスでも連続振動にしない。

Training中はShape変化を継続して見せ、表面Detailも成長へ追従させる。ただし
最終の見せ方をTransformationへ残すため、Training中の演出露出は控えめにする。
これはゲーム値を隠すことではなく、照明と表示係数を抑える表現上の差である。

### 5.4 実装境界

- `MuscleGrowthController` はゲーム値と全Channelの適用を所有する。
- 変身Presenterは一時的な照明、音、VFX、表面のPresentation係数だけを所有する。
- 共有Materialを書き換えず、必要な実行時係数は `MaterialPropertyBlock` を使う。
- `renderer.material` の毎フレーム生成を行わない。
- Tracked、Mirror、Cinematic、Pose Guideのコピーは、追跡側rendererに設定された
  全筋肉BlendShapeを同期する。単一のShape名をハードコードしない。
- 肌の表面係数は、Tracked、Mirror、Cinematic、Pose Monitor内のLive Avatarで
  同じSnapshotを使う。線画・半透明のお手本は専用マテリアルを維持し、肌用Detail
  Shaderをそのまま流用しない。
- Avatar Definitionの必須Shapeがコピー側に一つでもなければ、Editor/CIの構成
  テストを失敗させる。Runtimeでは一度だけ明示Errorを出して有効なAvatarへ
  fallbackし、欠落を毎フレーム黙ってskipしたりログ洪水にしたりしない。
- Retry、Avatar切替、途中中断で、ShapeとMaterial係数を基準値へ戻す。

### 5.5 変身の受入条件

- 0%、50%、100%の輪郭差を、UIを隠したQuest映像で判別できる。
- Normal/Detailを無効化しても大きな体型差はShapeだけで読める。
- Shapeを固定してDetailだけ変えた場合は、輪郭ではなく表面の筋肉感だけが変わる。
- Tracked、Mirror、Stage Entrance、Pose Monitorで同じ最終肉体になる。
- Training終了後にScorerのライブ値を変更しても、Overallと全部位が変化しない。
- 全必須Shapeの欠落をEditorテストが検出する。
- Transformation中にHMD、OVRCameraRig、TrackingSpaceを動かさない。
- 最終フレームでSnapshotへ正確に収束し、次フェーズで跳ねない。
- 5周Retryしても重み、Material、ライトの基準値が累積しない。

### 5.6 変身用AudioClip

現行Sceneの正式Audio slotは未設定である。プロトタイプでは、未設定時だけ安全な
生成仮音を使用し、Reveal開始とHero完成を一度ずつ区切る。低FPSなどで両境界を
一フレームに跨いだ場合はHeroだけを優先し、音を重ねない。正式Clipを割り当てると
対応する仮音は自動的に使われなくなる。Revealが残っている場合もHero直前に止め、
完成を示す音は一つだけにする。
本番では次の2つだけを追加し、拍、直前の運動成功音、入場音と重ねすぎない。

| Asset名 | 長さの目安 | 役割 |
| --- | ---: | --- |
| `Transformation_Reveal_Rise.wav` | 1.4〜1.6秒 | Reveal開始から持ち上がる、声なし・硬い初撃なしの短い上昇音 |
| `Transformation_Hero_Hit.wav` | 0.4〜0.7秒 | Hero Holdを一度だけ確定する、低域過多や鋭い高域を避けた決め音 |

48 kHz WAV、16または24 bitを基準とし、素材は自作、CC0、または再配布可能な
ライセンスに限る。出典、作者、ライセンスをAssetと同じThirdParty資料へ残す。

## 6. ステージ入場

### 6.1 視点

**決定:** プレイヤー向け入場は、現行どおり審査員席へ移動した実HMDから、
ステージへ歩くCinematic Avatarを直接見る。3カメラ映像やステージ内モニターを
プレイヤーの主視点へ戻さない。

この方式では頭部追従を保ったまま、自分の鍛えた全身を初めて外側から確認できる。
`StageEntranceCinematicController` の旧Cinematic Camera、Shot Anchor、
Screen Rendererはプレイヤー体験の必須依存にしない。現行Sceneの未設定状態は
この要件を満たしているため、フィールドが存在するという理由だけで再配線しない。

将来3カメラを使う場合は、外部観客向け録画・配信用だけに追加し、HMD表示や
ゲーム進行の必須依存にしない。

### 6.2 入場の役割分担

- `StageTransitionController`: 全黒、TrackingSpace整列、異常復旧
- `StageEntranceCinematicController`: 表示コピー、歩行、経路、表示状態の復元
- `GameFlowController`: フェーズ順序と時間
- Cinematic Avatar: 確定済みの全筋肉Shapeと表面状態を表示

歩行アニメーションはHumanoidとして再生し、Root Motionは使わず、移動は
WalkStart / Middle / EndのScene経路を正とする。HMD用カメラやTracked Avatarを
歩行アニメーションで動かさない。

### 6.3 入場の画づくり

- 袖から中央までのシルエットが、審査員席から最初の1秒以内に見つかる。
- 歩行経路と背景の明度差で輪郭を出し、常時発光する縁取りには頼らない。
- 上半身のShapeとDetailを読める斜めキーライトを置く。
- 審査員は前景のフレーミングと「見られている」感を担い、主役を隠さない。
- 見本とPose Monitorは入場中に隠す。共有するStageFlowHUDは審査員席へ一時的に
  向けて1.15倍表示し、ポージング開始時に向きと大きさを完全に戻す。
- 到着時に短い環境反応を入れるが、Transformationより強くしない。

## 7. ステージとポージング

### 7.1 ルーチン

現行順を初期ルーチンとして採用する。

1. Front Double Biceps
2. Single Biceps
3. Most Muscular

各ポーズは、準備カウントダウン → 一致探索 → HOLD → 成功または時間切れ →
次ポーズ、の同じ文法で進む。現行初期値は各3秒準備、最大10秒、成立後2秒保持。
数値はQuest実機で調整するが、3ポーズ間でルールを変えない。

### 7.2 採点可能範囲

採点入力はHMDと左右Touchの3点である。

- 手の位置、回転、左右関係、保持は採点できる。
- 肘位置は見本アバターを自然に見せるIK用で、採点しない。
- 胴体のひねり、肩、腰、脚を正解条件にしない。
- 見本は、採点していない身体部位まで厳密に一致させる印象を与えない。
- 3点だけで区別しにくいポーズは、演出上の見た目ではなく手ターゲットを先に見直す。

### 7.3 Pose Monitor

**決定:** Pose Monitorは「自分を確認する画面」を主とし、採点対象と同じProfileから
作る半透明3D見本を、横に一回り小さく `お手本` として表示する。

次の比率は **初期案** であり、Quest上の可読性を見てGate 3で調整する。

- Live Avatar: 画面面積の約65〜75%、不透明、現在の成長状態
- 3D Reference: 約20〜30%、半透明または線画、本人と重ねず横へ配置
- Pose name / instruction: 上部の一行
- Match / Hold: 下部の一つの進捗表現

`PoseGhostGuideController` を本番の主見本とする。`PoseReferenceGuide` の正面・
側面線画は、3D見本が利用できない時のFallback、またはProfile調整用Debugとし、
本番で同時に強く重ねない。奥行き指示が必要なら、小さな矢印や側面補助だけを出す。

見本の名前、指示、左右手ターゲット、肘ガイドは、必ず
`ThreePointPosingScorer` の現在Profileから読む。表示専用の別ポーズ値を持たない。
表示コピーも設定済みの全筋肉Shapeを同期し、単一Shapeだけの体型へ戻さない。

### 7.4 ポーズ中のフィードバック

1. Countdown: 見本とポーズ名を強くし、スコアは出さない。
2. Matching: 総合Matchだけを連続表示する。POS / ROT / SYMはDebugだけ。
3. Threshold到達: 表示を `HOLD` に固定し、少しの揺れは既存Graceで吸収する。
4. Hold完了: `POSE LOCKED`、ライト、観客、歓声を一度だけ同期して出す。
5. Timeout: 罰音や失敗の強調をせず、短い `NEXT POSE` で進む。
6. 3種終了: 各ポーズより一段強い観客・ライト反応を一度だけ出してResultsへ進む。

ライブのSpatial Scoreは観客と照明の滑らかな期待感に使い、Hold完了は成功の
確定に使う。閾値付近で歓声を連続再生しない。Tracking loss中はMatch、Hold、
観客、ライト反応を安全に下げ、復帰まで成功を確定しない。

### 7.5 観客と審査員

- 観客は55体・5段の現行密度を初期構成として扱う。
- 観客の個別精細さより、頭と胴体のシルエット、段差、反応の波を優先する。
- 全員を同位相で動かさず、反応量に小さな時間差を付ける。
- 審査員は最前列で観客と区別できる姿勢、卓、照明を持たせる。
- 審査員の個別採点ロジックは追加せず、当面は最終評価を示す演出役に留める。
- 観客、審査員、ライトの反応はPosing結果だけを読み、独自スコアを計算しない。

### 7.6 Results

Resultsは次の優先度で見せる。

1. Rank
2. Final Score
3. Training / Poseの二本柱
4. Growthの最終量と最終肉体
5. BでRetryの案内

3ポーズ別のPOS / ROT / SYM内訳は本番では表示せず、DebugまたはPlaytest記録へ
残す。Results中は `ExperienceResultController.ResultCompleted` の確定値だけを
使い、ライブ値で表示が変化しない。

確定値が揃ってから0.6秒の総合点表示を一度だけ開始し、75%地点でRankを出す。
再通知では開始時刻を更新せず、Setupへ戻った時だけ次周回向けにresetする。

## 8. アート方向

### 8.1 全体像

**決定:** ローポリ表現を最終目標にはしない。大きな形状は誇張して読みやすく、
表面は法線・粗さ・照明で筋肉を感じられる、スタイライズド・フィットネス
ショーの方向へ統一する。

写実度を全オブジェクトで揃えるのではなく、情報の階層を揃える。

- Avatar: 最も高い情報量。輪郭、表面、動きに注力
- Training room / Stage: Avatarを読ませる中程度の情報量
- Audience: 遠距離で読めるシルエット中心
- UI / Guide: 細い発光輪郭と小さな面。Avatarを覆わない
- VFX: 短時間で意味を伝え、常駐しない

### 8.2 共通の造形言語

- Training UIの収縮輪郭、Pose見本の線、Stageの発光アクセントに、同じ線幅比と
  角の処理を使う。
- 大きな半透明レーンや高Alphaの全身ゴーストを、本人アバターの前へ置かない。
- Cyanは導線、Magentaは舞台の興奮、獲得色は成功へ限定する。
- Training roomはCool Neutral、Transformationは同じ空間のコントラスト強調、
  StageはCyan/Magentaに暖色の肌ハイライトを足す。
- 部屋とStageを別作品のようにせず、同じ金属、発光、パネル形状を繰り返す。
- 肌色、性別、体型が変わっても、筋肉の明暗が潰れない照明範囲を持つ。

### 8.3 アバター用アセット契約

本番Avatarの受け入れ前に、男女それぞれ次を揃える。

- Humanoid RigとBody Trackingで破綻しないNeutral Pose
- ゲーム上の0%、50%、100%に対応する大形状
- 各Shapeの意味、対象部位、Unity weight方向の一覧
- Tracked / Mirror / Cinematicへ使える同一Mesh契約または明示的な対応表
- Base Color、Normal、Roughness
- 必要なら筋肉用Detail Normalと部位Mask
- 正しいUV、Tangent、Normal、Scale
- Quest用Texture解像度、圧縮、Mip Map設定

既存の男女Avatarでは、肌MaterialのBase NormalがURP Litの`_BumpMap`へ接続済みで
あることを確認した。Roughness textureファイルは存在するが、URP Litへそのまま
接続せず保留する。Smoothness向けの反転・packingとdata texture設定を確定する
必要があるためである。筋肉専用Detail / Height / Maskもまだ最終アセットとして
存在しない。到着前に仮の筋溝を本番仕様として固定しない。

特殊テクスチャの初期技術案は、URP Litを基礎にした肌用ShaderでDetail Normal、
Roughness、任意の部位Maskを扱う構成とする。Height / ParallaxはQuestの負荷、
斜め視点の破綻、Stereo差を実機評価するまで採用しない。

Gate 2の最初として、既存Normalを共有Material変更なしで利用し、成長0→1に対して
`_BumpScale` 0.72→1.08、`_Smoothness` 0.14→0.25の控えめな係数を
`MaterialPropertyBlock`から適用した。Tracked、Mirror、Stage Entrance、Pose Monitor内の
Live Avatarへ同じOverall Snapshotを使う。数値はQuest評価前の工学初期値であり、
最終テクスチャ納品後にDetail / Maskを接続して調整する。仮テクスチャの見た目を
最終Shader仕様へ固定しない。

### 8.4 Lightingと鏡

- Training roomの実体と鏡で、キーライトと影の向きが論理的に一致する。
- 筋肉を見せる斜めキー、影を潰さない弱いFill、輪郭用の控えめなRimを基本とする。
- Directional LightやPoint Lightを採点ごとに点滅させない。
- Reflection Probeは部屋・Stageの最終寸法が固まってから範囲を合わせる。
- ベイクはレイアウトとMaterialが固まるまで必須にしない。
- Stageの色ライトで肌のNormalとRoughnessが潰れる場合は、肌用のNeutral keyを残す。
- Mirror用だけに異なる成長、Material、影方向を作らない。

### 8.5 Quest向け制約

- 現行Mobile URP設定を基準に、設定されたリフレッシュレートを維持する。
- 最終の画質・性能判定はAndroid standaloneのQuest実機を正とする。Quest Linkの
  PC URP表示はレイアウトとタイミングの反復確認に使い、Standalone結果を上書きしない。
- full-resolutionの追加Camera / RenderTextureを安易に増やさない。現行Pose Monitorの
  1024×576を基準に、必要性を実機で評価する。
- 常時表示のTransparencyと重なりを最小化する。
- 観客ごとのMaterial instance、Realtime shadow、SkinnedMeshを増やさない。
  Avatarを読むための主ライト影は、Standaloneの予算内なら禁止しない。
- 実行中の `renderer.material` 生成と毎フレームのHierarchy検索を避ける。
- フェーズごとに必要なAvatarコピーだけを有効化し、同時に描画する高詳細体数を
  限定する。Training中のTracked / Mirrorまで一律に無効化しない。
- Post ProcessingやBloomは、文字と肌Detailがにじまない範囲で使う。

## 9. 実装アーキテクチャ

### 9.1 責務の境界

```text
Gameplay authorities
  GameFlow / RhythmClock / Scorer / MuscleGrowth / PoseScorer / Result
                         ↓ confirmed signals
Presentation signal layer
  semantic event / priority / one-shot / cancellation
                         ↓
Phase presenters
  Training UI+Haptics / Transformation / Entrance / Pose Stage / Results
                         ↓
UI / Avatar material / Audio / Haptics / Lights / Audience
```

- Gameplay authoritiesは演出都合で点数や成長値を書き換えない。
- Presentation signal layerは新しい採点を行わず、既存の確定イベントを意味へ変換する。
- Phase Presenterは、自分のフェーズ外で反応せず、DisableとRetryで基準状態へ戻す。
- `TrainingRhythmLaneHud`、`BeatHaptics`、`PoseStageFeedbackController` など既存の
  コンポーネントは段階的にこの契約へ揃え、一度に全面書き換えしない。
- `TrainingUiVisibilityController` をフェーズ表示の正として維持し、個別UIが
  勝手にSetActiveを競合させない。

### 9.2 設定の分け方と配置

巨大な一つの設定アセットを作らず、変更理由ごとに分ける。

- `Assets/IVRC/Config/ExperienceFeedbackStyle.asset`: 意味色、文字、共通の
  反応時間、音、ハプティクス優先度。Gate 1で作り、全Presenterが共有する
- `Assets/IVRC/Config/TransformationPresentationProfile.asset`: 時間曲線、
  表面係数、音、VFX asset。Gate 2で作る
- Stage Presentation: 現在の `PoseStageFeedbackController` のScene Serialize値を
  初期の正とする。別Sceneや複数Styleで再利用する時だけProfile assetへ分離する
- Gameplay Config: BPM、譜面、判定窓、成長量、ポーズProfile、スコア配点

GameObject、Light、Audience、AudioSourceなどScene固有参照はScene側に残す。
色や反応時間のような共通値だけをStyle/Profileから読む。存在しないProfileを
先に大量に作らず、各Gateで実装と同時に一つずつ導入する。

Gameplay ConfigとPresentation Configを同じクラスへ混ぜない。デザイナーが
見た目を調整して採点が変わる構造を避ける。

### 9.3 Reset契約

すべてのPresenterは次を満たす。

- 元のTransform、Light intensity、Material係数を一度だけCacheする。
- 毎フレームの差分加算ではなく、Cacheした基準から絶対値を計算する。
- Phase変更、Disable、Retry、Tracking lossで進行中のCoroutineとAudioを停止する。
- イベント購読は重複しない。
- Runtime clone、Material、AudioClipを所有者が破棄する。
- 一周目と五周目の初期状態が同じになる。

## 10. 実装順序とゲート

### 現行実装との差分

| 領域 | 現在 | この方針で次に変えること |
| --- | --- | --- |
| 初見導線 | 日本語の3段階Check、左右手と上側 / 下側のFace Button配置、A / X成功時の一回性完了表示・短音・押した側の弱い振動、失敗時の具体的な再試行、Aと暗転移動のHMD追跡ゲート、足元を動かさない安全文、Trainingの未採点見本、最初のPose説明を表示済み | Quest初見テストでボタン探索時間、完了表示と確認音の理解、3秒内の理解、文字サイズを調整 |
| Training説明 | 各種目の採点前に未採点の`下ろす → 上げる → 下ろす`を一往復見せ、`次の「上げる」から採点`を表示し、採点中は`今：上げる / 下ろす`を常時表示済み | Questで見本と採点開始を区別できるか、現在動作を迷わず理解できるかを確認 |
| REP反応 | Note単位で身体完了とリズム成功を分離し、Countdown、GO、反復結果、種目切替を一回音へ接続。既存の光・音・振動・BlendShape変化へ`二の腕 / 肩が成長`を重ね、動作完了は少し・リズム成功は強く因果を一度だけ明示済み | 正式SEへ差し替え、Questで表示・振動との強弱と、体型変化へ自然に視線が戻るかを調整 |
| Training BGM | 専用AudioSourceをDSP拍時計へ同期し、Tracking待ちの停止・復帰とTraining終了Fadeを実装。正式曲未設定時は4小節の静かな生成仮音を使用 | 120 BPM・16小節の正式ループへ差し替え、Questで短いSE・ハプティクス・スタッフの声を邪魔しないMixへ調整 |
| Transformation | Snapshotを変えないLock / Reveal / Hero Hold、既存照明、鏡枠の非点滅Color reveal、短いHUD、Hero両手振動を実装済み | 正式SEを追加し、Questで肉体への視線、明るさ、色、振動を調整 |
| 筋肉表面 | Base Normal接続を確認し、共有Materialを増やさないBump Scale / Smoothness追従を実装済み。Roughnessは未packing | Questで0/50/100と鏡・入場の一致を確認し、納品後にDetail / Maskを追加 |
| Stage Entrance | 審査員席の実HMDから直接見て、受動案内だけを残す | Android Questで可読性と視線誘導を調整 |
| Stage BGM | 審査員席への移動から入場・ポージングまで専用2D Sourceで継続し、Tracking待ちで30%へ減衰、Results前にFadeする生成仮音を実装 | 100 BPM前後の正式ループへ差し替え、成功音・歓声・スタッフの声を邪魔しないMixへ調整 |
| Pose Monitor | 3Dを本番の主見本とし、2D線画は3D生成不可時だけ自動表示する。最初のCountdownだけMonitor上部へ自分・お手本・腕・2秒保持を案内し、観客に隠れないことをScene視点で確認済み | Questで実距離可読性、3秒内の理解、3ポーズ切替時の視線移動を調整 |
| Pose成功 | 各確定結果を一回だけ通知し、中間成功は短いHUD・光・観客・生成仮SE・両手振動、Timeoutは非懲罰的に次へ。全Pose成功時の最終音はResults歓声へ譲る | Questで0.9秒表示、0.42振動、0.28秒仮SEの強さを調整し、正式な短い成功音へ差し替える |
| Results | 確定値、0.6秒の一回性スコア表示、遅れて出るRank、Training / Poseから選ぶ今回の強みと具体的な次の一手、主要な終了案内、任意Retryを実装済み | Questで実距離の可読性と、スタッフへ注意を戻すタイミングを調整 |

### Gate 0: アート入力と現状の意味を固定

実施内容:

- 男女Avatarの0%、50%、100%をUnityとQuestで撮影する。
- `Muscle`、`UpperArm.L/R`、`Shoulder.L/R`、`Chest.L/R` の見た目とweight方向を確認する。
- Tracked、Mirror、Cinematic、Pose GuideのBinding一覧を自動検証する。
- Detail Normal / Roughness / Maskの納品形式をアバター担当と合意する。
- Textureファイルの存在だけでなく、実RendererのMaterialとShader propertyへ
  Base Normalが実際に接続されていることを自動検証する。RoughnessはSmoothnessへ
  変換・packingしてから接続する。
- 男女FBXのBlendShape Normal / Tangent import設定を確認し、0/50/100%で
  法線の割れや陰影反転がないか比較する。

完了条件:

- ゲーム上の成長0→1が、男女で通常→筋肉へ一貫して変換される。
- Shape不足、名前違い、逆方向を黙って許容しない。
- Tracked / Mirror / Cinematic / Pose Guideの必須Shape集合が一致し、欠落時に
  自動テストが失敗する。

### Gate 1: 初見導線とフィードバック契約を統一

実施内容:

- Setupを左右手と上側 / 下側のFace Button配置付き3段階Check listへ変更する。
- 日本語の主指示、任意操作、Tracking復帰を一つのPlayer-facing copy設定へ集約する。
- Lead-in / Transition beatsへ、開始姿勢とLine-art guideの短い見本を追加する。
- Pose Monitorへ`あなた`、`お手本`、Pose進行、保持状態を同じ階層で表示する。
- REP COMPLETEとRHYTHM CLEARをNote単位で確定する。
- プレイヤー向け語、意味色、優先度を一つのStyleへ集約する。
- Training UI、ハプティクス、音の二重発火をなくす。
- Tracking pausedとMISSを分ける。

完了条件:

- 開発者以外が画面を見て、Setupから最初の採点対象まで追加説明なしで進める。
- RingとLine-artなど、無効な表示方式の説明文が本番UIへ残らない。
- Setup、Training、PosingのTracking lossで、具体的な復帰動作が表示される。
- 一つのUP/DOWNまたは反復結果に対し、各チャンネルの主反応が一度だけ出る。
- 確定後に評価語が別評価へ変わらない。
- 次の動作が、成功演出中も見失われない。

### Gate 2: Transformation縦切り

実施内容:

- Overallと全部位を含む不変 `MuscleGrowthSnapshot` をTraining終了時に保存する。
- 現行ShapeでLock / Reveal / Hero Holdを一周実装する。
- 仮Detailを使う場合も、最終Shader interfaceと役割だけを検証する。
- Mirror、Retry、Avatar切替を含める。

完了条件:

- 変身前後がQuestで明確に読め、HMD快適性を壊さない。
- 未獲得の成長や二重加算がない。
- Training後はScorerのライブChannel変更から完全に独立する。
- アセット差し替えがPresenterの作り直しを要求しない。

### Gate 3: Stage / Posing縦切り

実施内容:

- 審査員席からの直接入場をQuestで確認する。
- Pose MonitorはLive大、3Dお手本小へ整理済み。Questで実距離を確認する。
- `PoseReferenceGuide` は `AutomaticFallback` / `AlwaysShow` / `Hidden` を持ち、
  本番の `AutomaticFallback` では3D見本と2D線画を同時に主表示しない。
- 3Profileの表示、IK見本、採点、Hold成功を一致させる。
- 各ポーズ成功と3種完遂の反応を分ける。

完了条件:

- 頭を動かしても入場視点が拘束されない。
- 3ポーズすべてで名前、見本、採点、成功タイミングが一致する。
- 本番モードで有効な主見本が一方式だけであることをSceneテストで確認する。
- 本人の肉体がUI、見本、照明に隠れない。

### Gate 4: 本番アート統合

実施内容:

- 最終Shape、Normal、Detail、Roughness、Maskを男女へ適用する。
- 表面SnapshotからMaterialPropertyBlockへ渡すproperty契約と部位Mask変換を固定する。
- Training room、Transformation、StageのLightingを同じMaterial基準で調整する。
- 観客、審査員、Stage、UIの形状とPaletteを統一する。

完了条件:

- 男女、0/50/100%、鏡、入場、Pose Monitorで見た目が破綻しない。
- Textureが最終Materialの意図したShader propertyへ接続されている。
- Quest上で肌Detailが読め、UIと透過表現を含めても描画が安定する。

### Gate 5: 通し検証

自動検証:

- Phase表示とReset
- イベントの一回性と再購読
- 全筋肉Shapeの同期
- 変身Snapshotの保持
- 3Profileと見本の一致
- Pose成功、Timeout、Tracking loss
- Resultsの凍結とRetry

Quest実機検証:

- 一周と5周Retry
- 男性・女性Avatar切替
- 追跡喪失と復帰
- 装着位置と身長が異なる複数人
- Mirror / Entrance / Pose Monitorの肉体一致
- 頭部追従、酔い、光刺激、振動の不快感
- 実フレーム時間、RenderTexture、Transparency、Material数
- Linkとの見え方の差を記録し、最終合否はAndroid standaloneで判定

初見参加者検証:

- スタッフの追加説明なしでA、X、Bと次の身体動作を理解できる。
- 最初の採点前にLine-art、UP / DOWN、開始姿勢を理解できる。
- 種目切替とTracking復帰を案内だけで完了できる。
- REP完了とリズム評価を混同しない。
- Trainingが体型変化の原因だと説明できる。
- Transformationが一周の報酬に感じられる。
- 入場で自分の鍛えた姿を認識できる。
- 最初の案内だけで`あなた`、`お手本`、腕だけ合わせること、2秒保持を理解し、
  3ポーズで保持状態を見失わない。
- ResultsのRankと二本柱を説明できる。

## 11. 今回固定する決定

- 体験の主軸は、運動 → 肉体成長 → 変身 → 入場 → 3ポーズ → 結果。
- 運動完了とリズム成功を別の意味として表示する。
- アバターShapeを大形状、特殊テクスチャを細部へ使う。
- UnityのShape weight方向ではなく、ゲーム上の0→1を意味の正とする。
- Transformationは獲得済みSnapshotを見せ、成長値を追加しない。
- 入場は審査員席から頭部追従を保って直接見る。3カメ映像へ戻さない。
- Pose Monitorは本人を大きく、半透明3Dお手本を横に小さく表示する。
- 本番のPose見本は3Dを主、2D線画をFallback / Debugとする。
- 初見操作は独立した長い説明画面ではなく、各行動の直前に一つずつ教える。
- 必須操作は左右Controllerと成功状態を示し、任意操作と同じ強さで表示しない。
- 肘と胴体のひねりを3点採点へ含めない。
- 各ポーズ成功は中規模、3種完遂とResultsは大規模な反応にする。
- 画面揺れ、強制カメラ、ストロボ、長い連続振動を使わない。
- Setupは`0.12秒`以下、Training・Transformation・Posingは`0.20秒`以下へ
  一回振動を制限し、NaN / Infinityを含む設定ミスでも連続振動にしない。

## 12. アセットまたは実機まで保留する項目

- 筋肉専用Detail Normal / Roughness / Maskの最終枚数とpacking
- 最終ShaderをShader Graphにするか手書きURP Shaderにするか
- Height / Parallaxを採用するか
- TrainingのCountdown、GO、動作完了、リズム成功、種目切替に使う正式SE
- `Setup_Alignment_Complete.wav` / `Setup_Height_Complete.wav`: 各0.10〜0.16秒、
  成功だけを示す柔らかい単音。左右差は振動で示すため、極端なPanは使わない
- `Setup_Ready.wav`: 0.15〜0.25秒、A/X完了後にBへ進めると分かる短い上昇音
- `Training_Bed_120BPM.wav`: 120 BPM、4/4、16小節・32.000秒、先頭が第1拍、
  カウントインと末尾残響のない完全ループ
- `Stage_Bed.wav`: 100 BPM前後、4/4、24小節程度の完全ループ。入場の期待感を
  出しつつ、ポーズ成功音と観客歓声を邪魔しない声なし・低密度の曲
- Paletteの正確なRGB、Font、Icon、最終コピー
- 音楽、効果音、歓声の最終素材とMix
- 審査員の個別Animationをどこまで追加するか
- 各演出時間、スコア閾値、振動強度の最終値
- Reflection Probe、Realtime Light、Bakeの最終構成

これらはアーキテクチャを止めない。各項目は対応するGateで比較し、Questと
初見参加者の結果を残してから固定する。

## 13. 関連資料

- [トレーニング回数・リズム採点](TRAINING_RHYTHM.md)
- [筋肉成長出力](MUSCLE_GROWTH_INTEGRATION.md)
- [ステージ遷移](STAGE_TRANSITION.md)
- [ポージング採点](POSE_SCORING.md)
- [ステージ演出](POSE_STAGE_FEEDBACK.md)
- [結果スコア](RESULT_SCORING.md)
- [圧迫提示の安全境界](PNEUMATIC_CUFF_INTEGRATION.md)
