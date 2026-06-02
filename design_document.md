# 設計書: Mocha PS Scheduler

## 1. アプリ概要

### 目的
Windows のシステムトレイに常駐し、設定されたスケジュール（Cron / Interval）に従って、PowerShell スクリプトをはじめとする様々なジョブをバックグラウンドで自動実行する軽量なスケジューラアプリを提供する。

### 技術スタック
* **ランタイム**: .NET 10 (C#)
* **アプリ種別**: Windows Windows Forms / Worker Service ハイブリッド (システムトレイ常駐型 GUI アプリ)
* **UI**: システムトレイアイコンおよびコンテキストメニュー（必要に応じて最小限の設定/ログビューア画面）

### 特徴
* **コンソールの完全非表示**: ウィンドウを一瞬たりとも表示せずに、バックグラウンド（`CreateNoWindow = true`）でジョブを起動。
* **PowerShell専用**: PowerShell (`.ps1`) スクリプトの実行に特化したシンプルで軽量な設計。
* **簡単常駐・自動起動**: Windows サービスや WinSW 等の登録不要。レジストリやスタートアップフォルダへの自動登録を UI からトグル可能。
* **ホットリロード**: 設定ファイル（JSON）の変更をリアルタイムに監視し、アプリの再起動なしでスケジュールを動的更新。
* **独立したログ管理**: スクリプトの標準出力（Stdout）および標準エラー（Stderr）をキャプチャし、ジョブごとに整理されたファイルログを出力・ローテーション管理。

---

## 2. 全体アーキテクチャ

```
[MochaScheduler.exe (.NET 10)]
├── SystemTrayContext (トレイアイコン・メニューUI管理)
├── SchedulerEngine (Cron / Interval スケジュール管理)
├── JobManager (ジョブのライフサイクル・同時実行制御)
│     └── PowerShellRunner (PowerShell実行用)
├── ConfigManager (JSON設定ファイルの読み込み・監視)
├── LogManager (出力のキャプチャ・ログローテーション)
└── NotificationManager (トレイ通知)
```

---

## 3. モジュール詳細設計

### 3.1 SystemTrayContext (GUI / トレイUI)
* **機能**: 
  * Windows システムトレイにアイコンを表示。
  * 常駐アプリとして動作し、メインウィンドウを持たない（または最小限の設定・履歴画面のみ）。
* **トレイメニュー構成**:
  * **ステータス**: 現在の稼働状態（待機中 / 実行中のジョブ数）の表示。
  * **今すぐ実行**: 登録されているジョブの一覧を表示し、任意のジョブを即座に手動トリガー。
  * **ログフォルダを開く**: `LogManager` が出力するログフォルダをエクスプローラーで開く。
  * **設定ファイルを開く**: テキストエディタで `config.json` を開く。
  * **スタートアップ起動**: スタートアップ登録の有効/無効を切り替え。
  * **終了**: アプリケーションを完全にシャットダウン。

### 3.2 ConfigManager (設定管理)
* **機能**:
  * JSON 形式の設定ファイル（`config.json`）の読み込み。
  * `FileSystemWatcher` を用いたファイルの変更監視。
  * 変更検知時に設定をリロードし、`SchedulerEngine` に再登録を行う（ホットリロード）。
* **設定スキーマ例 (`config.json`)**:
  ```json
  {
    "startup": true,
    "notification": {
      "onSuccess": false,
      "onFailure": true
    },
    "jobs": [
      {
        "id": "sync-files",
        "name": "ファイル同期ジョブ",
        "type": "PowerShell",
        "scriptPath": "C:\\scripts\\sync.ps1",
        "arguments": "-Source 'A' -Destination 'B'",
        "schedule": "0 */10 * * * *",
        "timeoutSeconds": 300,
        "retryCount": 2,
        "retryDelaySeconds": 10
      },
      {
        "id": "cleanup-temp",
        "name": "一時ファイル削除",
        "type": "Batch",
        "scriptPath": "C:\\scripts\\cleanup.bat",
        "schedule": "0 0 2 * * *",
        "timeoutSeconds": 60,
        "retryCount": 0
      }
    ]
  }
  ```

### 3.3 SchedulerEngine (スケジュール管理)
* **機能**:
  * ジョブごとの Cron 式（`NCronTab` ライブラリ等を利用）またはインターバル時間を解析。
  * 各ジョブの次回実行予定時刻（DateTime）を管理。
  * 精密なタイマーを用いて、予定時刻に `JobManager` へ実行要求を送る。
  * `ConfigManager` からのホットリロード通知を受け取り、実行キューを再構築。

### 3.4 JobManager & JobRunner (ジョブ実行)
* **機能**:
  * 同時実行ジョブ数の制限、および同一ジョブの重複実行抑止（排他制御）の管理。
  * ジョブのタイムアウト監視と強制終了処理。
  * ジョブ失敗時のリトライロジック（バックオフ遅延対応）。
* **PowerShell実行エンジン**:
  * **PowerShellRunner の内部動作**:
    * 実行ファイル: `powershell.exe` もしくは `pwsh.exe`
    * 引数構成: `-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{scriptPath}" {arguments}`
    * `ProcessStartInfo` を使用し、`CreateNoWindow = true`, `UseShellExecute = false` で実行。

### 3.5 LogManager (ログ管理)
* **機能**:
  * 実行したジョブプロセスの `StandardOutput` および `StandardError` を非同期にキャプチャ。
  * ログをファイル (`logs/job-{jobId}.log`) にリアルタイム書き出し。
  * アプリ自体の動作ログは `logs/app.log` に出力。
  * ログサイズ上限や保持日数に応じた自動ローテーション機能の提供。

### 3.6 NotificationManager (通知機能)
* **機能**:
  * Windows のネイティブ通知（トースト通知）を利用。
  * ジョブの実行失敗や、リトライの上限到達時にユーザーに通知。
  * 設定（`config.json`）に基づいて、成功時の通知や通知自体の無効化を切り替え。

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
  ├── 2. 重複実行のチェック (該当ジョブがすでに実行中であればスキップまたはキューイング)
  ├── 3. JobManager が PowerShellRunner を生成して実行を開始
  │      ├── LogManager がプロセスの出力をリアルタイムにログファイルへ保存
  │      └── タイムアウト時間（timeoutSeconds）を監視
  ├── 5. プロセス終了
  │      ├── 成功(ExitCode 0): 終了処理へ
  │      └── 失敗(ExitCode 非0 / タイムアウト): 
  │            └── 設定に基づきリトライ処理をスケジュール
  └── 6. 結果の記録 & 必要に応じてトースト通知を発行
```
