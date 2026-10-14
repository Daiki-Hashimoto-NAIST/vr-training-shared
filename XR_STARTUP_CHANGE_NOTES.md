> 開発時点の設計資料です。現在の実装と異なる記述を含むため、動作の確認にはソースコードを参照してください。公開版での動作検証は行っていません。

# GitHubコミット用メモ

## タイトル

fix(xr): Quest Linkの再Play時クラッシュを回避する設定に変更

## 本文

- Standalone（PC）の Initialize XR on Startup を有効化。
- 既存の XrStartupController の自動管理対応分岐を利用し、PC VRの開始・終了をUnity XR Managementに任せる。
- Standalone（PC）の `Meta Quest: Boundary Visibility` を無効化。
- 手動管理を前提にしていた設定テストを、自動管理を検証する内容に更新。
- Android側の `Meta Quest: Boundary Visibility` は有効のまま維持。
- ゲーム進行、判定、スコア、音量は変更しない。

目的は、Quest LinkでPlayを終了した後、初期化済みのローダーだけが残り、次のPlayでVR表示が開始されない状態を避けること。
ローダーとはVR機能を読み込んで開始・終了する仕組みのこと。
実行時コードやSDKの変更は行わず、既存の自動管理対応を設定で選択する最小限の変更。

自動管理に変更した状態でQuest Linkを使い、Play、停止、2回目のPlayを行うとUnity Editorがクラッシュした。
使用中の Unity OpenXR: Meta 2.4.0 には、Boundary Visibilityが有効な状態で同じ操作を行うと異常終了する既知不具合があるため、PC側だけ同機能を無効化して回避を試す。
Boundary Visibilityは境界表示をアプリから制御するための機能であり、無効化してもQuest本体の安全用境界線やゲームのRep・ポーズ判定は無効にならない。

## 変更後の使用方法

### Quest 2をLinkで使用する場合

1. 作業を保存し、Unity Editorを一度終了して開き直す。変更前のVR接続状態を残さないために必要。
2. Meta Quest Linkを接続する。
3. `Edit > Project Settings > XR Plug-in Management` のPCタブで `Initialize XR on Startup` がオンであることを確認する。
4. `OpenXR` のPC設定で `Meta Quest: Boundary Visibility` がオフであることを確認する。
5. `IVRC > Desktop Mode > Force VR` を選び、Playする。
6. Play・停止を3〜5回繰り返し、映像とコントローラー入力が毎回復帰し、Unityがクラッシュしないことを確認する。

### ヘッドセットを使わずPC操作のみでテストする場合

1. Playを停止する。
2. 同じPCタブの `Initialize XR on Startup` をオフにする。
3. `IVRC > Desktop Mode > Force Desktop` を選ぶ。
4. VRテストへ戻すときは、自動起動をオンに戻し、Force VRを選ぶ。

Force Desktopだけでは、Unityが先に実行するXR自動起動を止められない。
PC用設定はGit管理対象なので、PC操作テスト後にオフのままコミットしないこと。
Quest単体向けAndroidビルドの設定には影響しない。

## 検証上の注意

設定を確認するEditModeテスト `StandaloneXrIsConfiguredForAutomaticStartup` は成功（1件中1件）。`git diff --check` も問題なし。

自動起動のみを有効にした状態では、初回Playと終了は成功したが、2回目のPlayでUnity Editorがクラッシュした。
Boundary Visibilityを無効化した後のQuest Link繰り返しPlayは、Unity再起動後に実機で確認する必要がある。
SDK内部の問題まで解決したことを保証する変更ではない。
