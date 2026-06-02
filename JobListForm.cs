using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace MochaScheduler
{
    public class JobListForm : Form
    {
        private readonly ConfigManager _configManager;
        private readonly JobManager _jobManager;
        private DataGridView _dataGridView = null!;
        private Button _btnRun = null!;
        private Button _btnOpenLog = null!;
        private Button _btnAddJob = null!;
        private Button _btnEditJob = null!;
        private Button _btnDeleteJob = null!;
        private Button _btnClose = null!;

        public JobListForm(ConfigManager configManager, JobManager jobManager)
        {
            _configManager = configManager;
            _jobManager = jobManager;

            InitializeComponent();
            LoadJobData();

            // イベントの購読
            _jobManager.JobStateChanged += OnJobStateChanged;
            _configManager.ConfigChanged += OnConfigChanged;
        }

        private void InitializeComponent()
        {
            this.Text = "ジョブスケジュール一覧 - Mocha PS Scheduler";
            this.Size = new System.Drawing.Size(950, 500);
            this.MinimumSize = new System.Drawing.Size(750, 400);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = System.Drawing.SystemIcons.Application;

            // レイアウト用のメインパネル (TableLayoutPanel)
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // DataGridViewが最大化
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));  // ボタンエリア

            // DataGridView の設定
            _dataGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = System.Drawing.SystemColors.Window,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false
            };

            // ボタン配置用のフローレイアウトパネル
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(10, 12, 10, 10),
                BackColor = System.Drawing.SystemColors.Control
            };

            _btnRun = new Button
            {
                Text = "今すぐ実行",
                Size = new System.Drawing.Size(120, 32),
                UseVisualStyleBackColor = true
            };
            _btnRun.Click += BtnRun_Click;

            _btnOpenLog = new Button
            {
                Text = "ログを開く",
                Size = new System.Drawing.Size(120, 32),
                UseVisualStyleBackColor = true
            };
            _btnOpenLog.Click += BtnOpenLog_Click;

            _btnAddJob = new Button
            {
                Text = "ジョブを追加",
                Size = new System.Drawing.Size(120, 32),
                UseVisualStyleBackColor = true
            };
            _btnAddJob.Click += BtnAddJob_Click;

            _btnEditJob = new Button
            {
                Text = "ジョブを編集",
                Size = new System.Drawing.Size(120, 32),
                UseVisualStyleBackColor = true
            };
            _btnEditJob.Click += BtnEditJob_Click;

            _btnDeleteJob = new Button
            {
                Text = "ジョブを削除",
                Size = new System.Drawing.Size(120, 32),
                UseVisualStyleBackColor = true
            };
            _btnDeleteJob.Click += BtnDeleteJob_Click;

            _btnClose = new Button
            {
                Text = "閉じる",
                Size = new System.Drawing.Size(100, 32),
                UseVisualStyleBackColor = true
            };
            _btnClose.Click += (s, e) => this.Hide();

            buttonPanel.Controls.Add(_btnRun);
            buttonPanel.Controls.Add(_btnOpenLog);
            buttonPanel.Controls.Add(_btnAddJob);
            buttonPanel.Controls.Add(_btnEditJob);
            buttonPanel.Controls.Add(_btnDeleteJob);
            buttonPanel.Controls.Add(_btnClose);

            mainPanel.Controls.Add(_dataGridView, 0, 0);
            mainPanel.Controls.Add(buttonPanel, 0, 1);

            this.Controls.Add(mainPanel);

            // 閉じるボタンが押された時の常駐化処理 (非表示にする)
            this.FormClosing += JobListForm_FormClosing;
        }

        private void LoadJobData()
        {
            var rows = new List<JobDisplayRow>();
            foreach (var job in _configManager.Config.Jobs)
            {
                if (string.IsNullOrEmpty(job.Id)) continue;

                var isRunning = _jobManager.IsJobRunning(job.Id);
                rows.Add(new JobDisplayRow
                {
                    Id = job.Id,
                    Name = string.IsNullOrEmpty(job.Name) ? job.Id : job.Name,
                    Schedule = job.Schedule,
                    ScriptPath = job.ScriptPath,
                    Status = isRunning ? "実行中" : "待機中"
                });
            }

            _dataGridView.DataSource = null;
            _dataGridView.DataSource = rows;

            // ヘッダーテキストの調整
            if (_dataGridView.Columns.Count > 0)
            {
                if (_dataGridView.Columns["Id"] is { } idCol) idCol.HeaderText = "ジョブID";
                if (_dataGridView.Columns["Name"] is { } nameCol) nameCol.HeaderText = "ジョブ名";
                if (_dataGridView.Columns["Schedule"] is { } schedCol) schedCol.HeaderText = "スケジュール";
                if (_dataGridView.Columns["ScriptPath"] is { } pathCol) pathCol.HeaderText = "スクリプトパス";
                if (_dataGridView.Columns["Status"] is { } statusCol) statusCol.HeaderText = "ステータス";
                
                // パス列などを少し広めに取る調整
                if (_dataGridView.Columns["ScriptPath"] is { } scriptPathCol) scriptPathCol.FillWeight = 180F;
            }
        }

        private void BtnRun_Click(object? sender, EventArgs e)
        {
            var selectedJob = GetSelectedJobConfig();
            if (selectedJob != null)
            {
                if (_jobManager.IsJobRunning(selectedJob.Id))
                {
                    MessageBox.Show("選択したジョブはすでに実行中です。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _ = _jobManager.TriggerJobAsync(selectedJob, isManual: true);
            }
        }

        private void BtnOpenLog_Click(object? sender, EventArgs e)
        {
            var selectedJob = GetSelectedJobConfig();
            if (selectedJob != null)
            {
                var logPath = Path.Combine(LogManager.GetLogDirectory(), $"job-{selectedJob.Id}.log");
                if (File.Exists(logPath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "notepad.exe",
                            Arguments = $"\"{logPath}\"",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"ログファイルを開けませんでした:\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    MessageBox.Show("このジョブのログファイルはまだ作成されていません。\n（一度も実行されていない可能性があります）", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void BtnAddJob_Click(object? sender, EventArgs e)
        {
            // 重複チェック用の既存ID一覧の取得
            var existingIds = _configManager.Config.Jobs.ConvertAll(j => j.Id);
            
            using var editForm = new JobEditForm(existingIds);
            if (editForm.ShowDialog(this) == DialogResult.OK && editForm.JobConfigResult != null)
            {
                var newJob = editForm.JobConfigResult;
                var currentConfig = _configManager.Config;
                currentConfig.Jobs.Add(newJob);
                
                // 設定を保存 (ホットリロード監視により、タイマー再構成・グリッド更新が自動誘発されます)
                _configManager.SaveConfig(currentConfig);
            }
        }

        private void BtnEditJob_Click(object? sender, EventArgs e)
        {
            var selectedJob = GetSelectedJobConfig();
            if (selectedJob != null)
            {
                if (_jobManager.IsJobRunning(selectedJob.Id))
                {
                    MessageBox.Show("実行中のジョブは編集できません。停止してから編集してください。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var existingIds = _configManager.Config.Jobs.ConvertAll(j => j.Id);

                using var editForm = new JobEditForm(existingIds, selectedJob);
                if (editForm.ShowDialog(this) == DialogResult.OK && editForm.JobConfigResult != null)
                {
                    var updatedJob = editForm.JobConfigResult;
                    var currentConfig = _configManager.Config;
                    
                    int idx = currentConfig.Jobs.FindIndex(j => j.Id == selectedJob.Id);
                    if (idx >= 0)
                    {
                        currentConfig.Jobs[idx] = updatedJob;
                        _configManager.SaveConfig(currentConfig);
                    }
                }
            }
            else
            {
                MessageBox.Show("編集するジョブを選択してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void BtnDeleteJob_Click(object? sender, EventArgs e)
        {
            var selectedJob = GetSelectedJobConfig();
            if (selectedJob != null)
            {
                if (_jobManager.IsJobRunning(selectedJob.Id))
                {
                    MessageBox.Show("実行中のジョブは削除できません。停止してから削除してください。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var confirm = MessageBox.Show(
                    $"ジョブ '{selectedJob.Name}' (ID: {selectedJob.Id}) を削除しますか？",
                    "削除の確認",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (confirm == DialogResult.Yes)
                {
                    var currentConfig = _configManager.Config;
                    currentConfig.Jobs.RemoveAll(j => j.Id == selectedJob.Id);
                    _configManager.SaveConfig(currentConfig);
                }
            }
            else
            {
                MessageBox.Show("削除するジョブを選択してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private JobConfig? GetSelectedJobConfig()
        {
            if (_dataGridView.SelectedRows.Count > 0)
            {
                var row = _dataGridView.SelectedRows[0];
                var displayRow = row.DataBoundItem as JobDisplayRow;
                if (displayRow != null)
                {
                    return _configManager.Config.Jobs.Find(j => j.Id == displayRow.Id);
                }
            }
            return null;
        }

        private void JobListForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // ユーザーによるクローズ操作の場合、終了せず非表示にするだけにする
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        private void OnJobStateChanged(object? sender, EventArgs e)
        {
            SafeInvoke(LoadJobData);
        }

        private void OnConfigChanged(object? sender, AppConfig e)
        {
            SafeInvoke(LoadJobData);
        }

        private void SafeInvoke(Action action)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _jobManager.JobStateChanged -= OnJobStateChanged;
                _configManager.ConfigChanged -= OnConfigChanged;
            }
            base.Dispose(disposing);
        }

        // DataGridView バインディング用のヘルパークラス
        private class JobDisplayRow
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Schedule { get; set; } = string.Empty;
            public string ScriptPath { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }
    }
}
