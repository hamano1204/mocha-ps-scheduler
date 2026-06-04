# 設計書: Mocha PS Scheduler

## 1. アプリ概要

### 目的
Windows のシステムトレイに常駐し、設定されたスケジュール（Cron）に従って、PowerShell スクリプトをはじめとする様々なジョブをバックグラウンドで自動実行する軽量なスケジューラアプリを提供する。

### 技術スタック
* **ランタイム**: .NET 10 (C# 13)
* **アプリ種別**: Windows Forms / Worker Service ハイブリッド (システムトレイ常駐型 GUI アプリ)
* **UI**: システムトレイアイコンおよびコンテキストメニュー（ジョブ管理用の Windows Forms 画面 `JobListForm`, `JobEditForm`）

### 特徴
* **コンソールの完全非表示**: ウィンドウを表示せずに、バックグラウンド（`CreateNoWindow = true`, `UseShellExecute = false`）でジョブを起動。
* **PowerShell対応**: PowerShell (`.ps1`) スクリプトの実行に特化しつつ、拡張可能な構造。
* **簡単常駐・自動起動**: レジストリへの自動起動登録を UI から切り替え可能。
* **ホットリロード**: 設定ファイル（JSON）の変更をリアルタイムに監視し、アプリの再起動なしでスケジュールを動的更新。
* **独立したログ管理**: スクリプトの標準出力（Stdout）および標準エラー（Stderr）をキャプチャし、ジョブごとに整理されたファイルログを出力・管理。
* **通知機能**: ジョブの実行結果（成功・失敗）に応じて Windows のネイティブトースト通知を送信。

---

## 2. 全体アーキテクチャ

```
[MochaScheduler.exe (.NET 10)]
├── SystemTrayContext (トレイアイコン・メニューUI管理)
├── JobListForm / JobEditForm (ジョブのリスト・編集用UI画面)
├── SchedulerEngine (NCrontab を用いた Cron スケジュール監視・実行ループ)
├── JobManager (ジョブのライフサイクル・同時実行制御・リトライ制御)
│     └── JobRunner / PowerShellRunner (ジョブ実行エンジン)
├── ConfigManager (JSON設定ファイルの読み込み・監視・スレッドセーフな保存)
├── LogManager (出力のキャプチャ・アプリログの管理)
└── NotificationManager (トレイ通知の配信)
```

---

## 3. モジュール詳細設計

### 3.1 SystemTrayContext (GUI / トレイUI)
* **機能**: 
  * Windows システムトレイにアイコンを表示。
  * 右クリックメニューからジョブの編集画面、ログフォルダの表示、自動起動設定、終了などのアクションを提供。

### 3.2 ConfigManager (設定管理)
* **スレッドセーフ性**:
  * 設定オブジェクト `AppConfig` は読み取り専用の `Clone()` を介して取得・返却されます。
  * `SaveConfig` 処理中に `FileSystemWatcher` による多重イベントを防ぐための 150ms のデバウンス機能を持ちます。
  * デッドロックを回避するため、`ConfigChanged` イベントの発火は `_lock` の排他制御ブロックの**外側**で行われます。

### 3.3 SchedulerEngine (スケジュール管理)
* **機能**:
  * ジョブごとの Cron 式（**秒を含む6フィールド形式**: `second minute hour day month day-of-week`）を `NCrontab` ライブラリを利用して解析。
  * `RunSchedulerLoopAsync` ループ内で予定時刻（`_nextRunTimes`）に達したジョブを非同期にトリガー。
  * 次回の実行予定時刻を計算する際、現在時刻ではなく前回の予定時刻を基準に算出することで**時間の累積ドリフト**を防止。

### 3.4 JobManager & JobRunner (ジョブ実行)
* **重複起動防止**:
  * 同一ジョブの重複実行を抑止するため、`ConcurrentDictionary<string, CancellationTokenSource>` を用いて実行状態を管理。
  * すでに実行中のジョブはトリガーをスキップ。
* **タイムアウトとキャンセルの判別**:
  * ジョブの実行ごとに手動キャンセル用の `CancellationTokenSource` と、設定時間に基づく `CancelAfter` タイムアウト用トークンをリンクさせた `CreateLinkedTokenSource(cts.Token)` を生成。
  * `JobRunner` の返却値やキャンセルトークンの状態を検証し、「手動によるキャンセル」と「タイムアウトによる強制終了」を正確に識別して、不必要なリトライ処理を防ぎます。
* **リトライ機構**:
  * 設定された `RetryCount` と `RetryDelaySeconds` に基づきリトライを試行。ただし、手動キャンセルまたはタイムアウトが発生した場合は、無駄なハングを避けるためにリトライを即座に打ち切ります。

### 3.5 LogManager (ログ管理)
* **機能**:
  * ジョブプロセスの `StandardOutput` および `StandardError` を非同期で読み取り、リアルタイムにログファイル（`logs/job-{jobId}.log`）へ書き出し。
  * ログの書き込みハングやデッドロックを防ぐため、バックグラウンドスレッドでストリーム読み込みを行います。

---

## 4. 実行フロー

### アプリケーション起動
```
[アプリ起動]
  │
  ├── 1. 重複起動チェック (Single Instance check via Mutex)
  ├── 2. ConfigManager の初期化 (config.json の読み込み & 監視開始)
  ├── 3. SystemTrayContext の初期化 (トレイアイコンを生成してタスクトレイに格納)
  ├── 4. SchedulerEngine の起動 (ジョブのスケジュールタイマーをセット)
  └── 5. 待機ループ開始
```

### ジョブ実行サイクル
```
[タイマー発火 / 手動実行トリガー]
  │
  ├── 1. JobManager が実行要求を受信
  ├── 2. 重複実行のチェック (該当ジョブがすでに実行中であればスキップ)
  ├── 3. JobManager が PowerShellRunner を生成して実行を開始
  │      ├── LogManager がプロセスの出力をリアルタイムにログファイルへ保存
  │      └── タイムアウト時間（timeoutSeconds）を監視
  ├── 4. プロセス終了
  │      ├── 成功(ExitCode 0): 終了処理へ (必要に応じて成功通知)
  │      └── 失敗(ExitCode 非0 / タイムアウト / キャンセル):
  │            ├── ユーザーキャンセル / タイムアウト: 即座に失敗として処理終了
  │            └── 通常エラー: リトライ回数上限まで遅延を挟んで再試行
  └── 5. 結果の記録 & 必要に応じてトースト通知を発行
```
