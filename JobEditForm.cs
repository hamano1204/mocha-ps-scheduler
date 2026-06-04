using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NCrontab;

namespace MochaScheduler
{
    public class JobEditForm : Form
    {
        private readonly List<string> _existingIds;
        
        private TextBox _txtId = null!;
        private TextBox _txtName = null!;
        private CheckBox _chkEnabled = null!;
        private TextBox _txtScriptPath = null!;
        private Button _btnBrowse = null!;
        private TextBox _txtArguments = null!;

        // スケジュールビルダー用コントロール群
        private RadioButton _rdoInterval = null!;
        private NumericUpDown _numIntervalValue = null!;
        private ComboBox _cmbIntervalUnit = null!;

        private RadioButton _rdoDaily = null!;
        private DateTimePicker _dtpDailyTime = null!;

        private RadioButton _rdoWeekly = null!;
        private CheckBox[] _chkDays = null!;
        private DateTimePicker _dtpWeeklyTime = null!;

        private RadioButton _rdoCron = null!;
        private TextBox _txtCron = null!;

        private NumericUpDown _numTimeout = null!;
        private NumericUpDown _numRetry = null!;
        private NumericUpDown _numRetryDelay = null!;
        private Button _btnSave = null!;
        private Button _btnCancel = null!;

        public JobConfig? JobConfigResult { get; private set; }

        private readonly JobConfig? _editingJob;

        public JobEditForm(List<string> existingIds, JobConfig? editingJob = null)
        {
            _existingIds = existingIds;
            _editingJob = editingJob;
            InitializeComponent();
            LoadEditingJobData();
            UpdateScheduleUiState();
        }

        private void InitializeComponent()
        {
            this.Text = "ジョブの追加 - Mocha PS Scheduler";
            this.Size = new System.Drawing.Size(600, 728);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Icon = Program.AppIcon;

            // メインのテーブルレイアウト (縦並び)
            var mainTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
                Padding = new Padding(15)
            };
            mainTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            mainTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            // 行サイズ設定
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F)); // ID
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F)); // 名前
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F)); // 有効/無効
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F)); // パス
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F)); // 引数
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 290F)); // スケジュール (ビルダーUI)
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F)); // タイムアウト・リトライ
            mainTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // ボタン

            int currentRow = 0;

            // 1. ジョブID
            mainTable.Controls.Add(new Label { Text = "ジョブID (半角英数字):", Anchor = AnchorStyles.Left }, 0, currentRow);
            _txtId = new TextBox { Dock = DockStyle.Fill };
            mainTable.Controls.Add(_txtId, 1, currentRow);
            currentRow++;

            // 2. ジョブ名
            mainTable.Controls.Add(new Label { Text = "ジョブ名:", Anchor = AnchorStyles.Left }, 0, currentRow);
            _txtName = new TextBox { Dock = DockStyle.Fill };
            mainTable.Controls.Add(_txtName, 1, currentRow);
            currentRow++;

            // 2.5 有効状態
            mainTable.Controls.Add(new Label { Text = "有効状態:", Anchor = AnchorStyles.Left }, 0, currentRow);
            _chkEnabled = new CheckBox { Text = "このスケジュールを有効にする", Checked = true, Anchor = AnchorStyles.Left };
            mainTable.Controls.Add(_chkEnabled, 1, currentRow);
            currentRow++;

            // 3. スクリプトパス
            mainTable.Controls.Add(new Label { Text = "スクリプトパス:", Anchor = AnchorStyles.Left }, 0, currentRow);
            var pathPanel = CreatePathPanel();
            mainTable.Controls.Add(pathPanel, 1, currentRow);
            currentRow++;

            // 4. 実行引数
            mainTable.Controls.Add(new Label { Text = "実行引数:", Anchor = AnchorStyles.Left }, 0, currentRow);
            _txtArguments = new TextBox { Dock = DockStyle.Fill };
            mainTable.Controls.Add(_txtArguments, 1, currentRow);
            currentRow++;

            // 5. スケジュール設定ビルダー (GroupBox)
            mainTable.Controls.Add(new Label { Text = "スケジュール設定:", Anchor = AnchorStyles.Top | AnchorStyles.Left, Padding = new Padding(0, 5, 0, 0) }, 0, currentRow);
            var grpSchedule = CreateScheduleGroupBox();
            mainTable.Controls.Add(grpSchedule, 1, currentRow);
            currentRow++;

            // 6. タイムアウト・リトライ GroupBox
            mainTable.Controls.Add(new Label { Text = "高度な実行制御:", Anchor = AnchorStyles.Top | AnchorStyles.Left, Padding = new Padding(0, 5, 0, 0) }, 0, currentRow);
            var grpAdvanced = CreateAdvancedGroupBox();
            mainTable.Controls.Add(grpAdvanced, 1, currentRow);
            currentRow++;

            // 7. 下部ボタン
            var buttonPanel = CreateButtonPanel();
            mainTable.Controls.Add(buttonPanel, 1, currentRow);

            this.Controls.Add(mainTable);
            this.AcceptButton = _btnSave;
            this.CancelButton = _btnCancel;
        }

        private TableLayoutPanel CreatePathPanel()
        {
            var pathPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            
            _txtScriptPath = new TextBox { Dock = DockStyle.Fill };
            _btnBrowse = new Button { Text = "参照...", Dock = DockStyle.Fill };
            _btnBrowse.Click += BtnBrowse_Click;
            
            pathPanel.Controls.Add(_txtScriptPath, 0, 0);
            pathPanel.Controls.Add(_btnBrowse, 1, 0);
            return pathPanel;
        }

        private GroupBox CreateScheduleGroupBox()
        {
            var grpSchedule = new GroupBox
            {
                Text = "実行スケジュール",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            var schedLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4
            };
            schedLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            schedLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            schedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F)); // インターバル
            schedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F)); // 毎日
            schedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F)); // 毎週
            schedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55F)); // Cron

            // 5.1 インターバル
            _rdoInterval = new RadioButton { Text = "一定間隔", Checked = true, Anchor = AnchorStyles.Left };
            _rdoInterval.CheckedChanged += (s, e) => UpdateScheduleUiState();
            
            var pnlInterval = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 5, 0, 0) };
            _numIntervalValue = new NumericUpDown { Value = 10, Minimum = 1, Maximum = 3600, Width = 60 };
            _cmbIntervalUnit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
            _cmbIntervalUnit.Items.AddRange(new object[] { "秒", "分", "時間" });
            _cmbIntervalUnit.SelectedIndex = 1; // "分"
            pnlInterval.Controls.Add(_numIntervalValue);
            pnlInterval.Controls.Add(_cmbIntervalUnit);
            pnlInterval.Controls.Add(new Label { Text = "ごと", AutoSize = true, Margin = new Padding(3, 5, 0, 0) });

            schedLayout.Controls.Add(_rdoInterval, 0, 0);
            schedLayout.Controls.Add(pnlInterval, 1, 0);

            // 5.2 毎日
            _rdoDaily = new RadioButton { Text = "毎日指定時刻", Anchor = AnchorStyles.Left };
            _rdoDaily.CheckedChanged += (s, e) => UpdateScheduleUiState();
            
            var pnlDaily = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 5, 0, 0) };
            _dtpDailyTime = new DateTimePicker { Format = DateTimePickerFormat.Time, ShowUpDown = true, Width = 100 };
            pnlDaily.Controls.Add(_dtpDailyTime);

            schedLayout.Controls.Add(_rdoDaily, 0, 1);
            schedLayout.Controls.Add(pnlDaily, 1, 1);

            // 5.3 毎週
            _rdoWeekly = new RadioButton { Text = "毎週指定曜日", Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = new Padding(3, 5, 0, 0) };
            _rdoWeekly.CheckedChanged += (s, e) => UpdateScheduleUiState();
            
            var pnlWeekly = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            pnlWeekly.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            pnlWeekly.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));

            var pnlDays = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            string[] dayNames = { "月", "火", "水", "木", "金", "土", "日" };
            _chkDays = new CheckBox[7];
            for (int i = 0; i < 7; i++)
            {
                _chkDays[i] = new CheckBox { Text = dayNames[i], Width = 40 };
                pnlDays.Controls.Add(_chkDays[i]);
            }

            var pnlWeeklyTime = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _dtpWeeklyTime = new DateTimePicker { Format = DateTimePickerFormat.Time, ShowUpDown = true, Width = 100 };
            pnlWeeklyTime.Controls.Add(new Label { Text = "時刻:", AutoSize = true, Margin = new Padding(0, 5, 5, 0) });
            pnlWeeklyTime.Controls.Add(_dtpWeeklyTime);

            pnlWeekly.Controls.Add(pnlDays, 0, 0);
            pnlWeekly.Controls.Add(pnlWeeklyTime, 0, 1);

            schedLayout.Controls.Add(_rdoWeekly, 0, 2);
            schedLayout.Controls.Add(pnlWeekly, 1, 2);

            // 5.4 Cron式
            _rdoCron = new RadioButton { Text = "高度な設定 (Cron)", Anchor = AnchorStyles.Left };
            _rdoCron.CheckedChanged += (s, e) => UpdateScheduleUiState();
            
            var pnlCron = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 5, 0, 0) };
            _txtCron = new TextBox { Width = 180, Text = "0 */10 * * * *" };
            pnlCron.Controls.Add(_txtCron);
            pnlCron.Controls.Add(new Label { Text = "(秒 分 時 日 月 曜日)", AutoSize = true, ForeColor = System.Drawing.Color.Gray, Margin = new Padding(5, 5, 0, 0) });

            schedLayout.Controls.Add(_rdoCron, 0, 3);
            schedLayout.Controls.Add(pnlCron, 1, 3);

            grpSchedule.Controls.Add(schedLayout);
            return grpSchedule;
        }

        private GroupBox CreateAdvancedGroupBox()
        {
            var grpAdvanced = new GroupBox { Text = "タイムアウト & リトライ", Dock = DockStyle.Fill, Padding = new Padding(5) };
            var advLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
            advLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            advLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            advLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            advLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            advLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            advLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            
            advLayout.Controls.Add(new Label { Text = "タイムアウト:", Anchor = AnchorStyles.Left }, 0, 0);
            _numTimeout = new NumericUpDown { Minimum = 0, Maximum = 86400, Value = 300, Width = 80 };
            var pnlTimeout = new FlowLayoutPanel { Dock = DockStyle.Fill };
            pnlTimeout.Controls.Add(_numTimeout);
            pnlTimeout.Controls.Add(new Label { Text = "秒 (0で無制限)", AutoSize = true, Margin = new Padding(3, 5, 0, 0) });
            advLayout.Controls.Add(pnlTimeout, 1, 0);

            advLayout.Controls.Add(new Label { Text = "リトライ回数:", Anchor = AnchorStyles.Left }, 2, 0);
            _numRetry = new NumericUpDown { Minimum = 0, Maximum = 10, Value = 0, Width = 50 };
            advLayout.Controls.Add(_numRetry, 3, 0);

            advLayout.Controls.Add(new Label { Text = "リトライ遅延:", Anchor = AnchorStyles.Left }, 2, 1);
            _numRetryDelay = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = 10, Width = 50 };
            var pnlDelay = new FlowLayoutPanel { Dock = DockStyle.Fill };
            pnlDelay.Controls.Add(_numRetryDelay);
            pnlDelay.Controls.Add(new Label { Text = "秒", AutoSize = true, Margin = new Padding(3, 5, 0, 0) });
            advLayout.Controls.Add(pnlDelay, 3, 1);

            grpAdvanced.Controls.Add(advLayout);
            return grpAdvanced;
        }

        private FlowLayoutPanel CreateButtonPanel()
        {
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 10, 0, 0)
            };

            _btnCancel = new Button { Text = "キャンセル", Size = new System.Drawing.Size(90, 32), DialogResult = DialogResult.Cancel };
            _btnSave = new Button { Text = "保存", Size = new System.Drawing.Size(90, 32) };
            _btnSave.Click += BtnSave_Click;

            buttonPanel.Controls.Add(_btnCancel);
            buttonPanel.Controls.Add(_btnSave);
            this.CancelButton = _btnCancel;
            return buttonPanel;
        }

        private void UpdateScheduleUiState()
        {
            // インターバル
            _numIntervalValue.Enabled = _rdoInterval.Checked;
            _cmbIntervalUnit.Enabled = _rdoInterval.Checked;

            // 毎日
            _dtpDailyTime.Enabled = _rdoDaily.Checked;

            // 毎週
            foreach (var chk in _chkDays)
            {
                chk.Enabled = _rdoWeekly.Checked;
            }
            _dtpWeeklyTime.Enabled = _rdoWeekly.Checked;

            // Cron
            _txtCron.Enabled = _rdoCron.Checked;
        }

        private void LoadEditingJobData()
        {
            if (_editingJob == null) return;

            this.Text = "ジョブの編集 - Mocha PS Scheduler";
            _txtId.Text = _editingJob.Id;
            _txtId.ReadOnly = true;
            _txtName.Text = _editingJob.Name;
            _chkEnabled.Checked = _editingJob.Enabled;
            _txtScriptPath.Text = _editingJob.ScriptPath;
            _txtArguments.Text = _editingJob.Arguments;
            _numTimeout.Value = _editingJob.TimeoutSeconds;
            _numRetry.Value = _editingJob.RetryCount;
            _numRetryDelay.Value = _editingJob.RetryDelaySeconds;

            string cron = _editingJob.Schedule.Trim();
            if (string.IsNullOrEmpty(cron)) return;

            // 1. Check if it matches Interval (seconds: */X * * * * *, minutes: 0 */X * * * *, hours: 0 0 */X * * *)
            if (cron.StartsWith("*/"))
            {
                var parts = cron.Split(' ');
                if (parts.Length >= 6 && int.TryParse(parts[0].Substring(2), out int val))
                {
                    _rdoInterval.Checked = true;
                    _numIntervalValue.Value = val;
                    _cmbIntervalUnit.SelectedItem = "秒";
                    return;
                }
            }
            else if (cron.Contains(" */"))
            {
                var parts = cron.Split(' ');
                if (parts.Length >= 6)
                {
                    if (parts[0] == "0" && parts[1].StartsWith("*/") && int.TryParse(parts[1].Substring(2), out int minVal))
                    {
                        _rdoInterval.Checked = true;
                        _numIntervalValue.Value = minVal;
                        _cmbIntervalUnit.SelectedItem = "分";
                        return;
                    }
                    if (parts[0] == "0" && parts[1] == "0" && parts[2].StartsWith("*/") && int.TryParse(parts[2].Substring(2), out int hrVal))
                    {
                        _rdoInterval.Checked = true;
                        _numIntervalValue.Value = hrVal;
                        _cmbIntervalUnit.SelectedItem = "時間";
                        return;
                    }
                }
            }

            // 2. Check if it matches Daily: 0 M H * * *
            var dailyParts = cron.Split(' ');
            if (dailyParts.Length == 6 && dailyParts[0] == "0" && dailyParts[3] == "*" && dailyParts[4] == "*" && dailyParts[5] == "*")
            {
                if (int.TryParse(dailyParts[1], out int min) && int.TryParse(dailyParts[2], out int hr))
                {
                    _rdoDaily.Checked = true;
                    _dtpDailyTime.Value = DateTime.Today.AddHours(hr).AddMinutes(min);
                    return;
                }
            }

            // 3. Check if it matches Weekly: 0 M H * * DayString
            if (dailyParts.Length == 6 && dailyParts[0] == "0" && dailyParts[3] == "*" && dailyParts[4] == "*" && dailyParts[5] != "*")
            {
                if (int.TryParse(dailyParts[1], out int min) && int.TryParse(dailyParts[2], out int hr))
                {
                    _rdoWeekly.Checked = true;
                    _dtpWeeklyTime.Value = DateTime.Today.AddHours(hr).AddMinutes(min);
                    
                    var days = dailyParts[5].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    string[] cronDaySymbols = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
                    for (int i = 0; i < 7; i++)
                    {
                        _chkDays[i].Checked = System.Linq.Enumerable.Contains(days, cronDaySymbols[i]);
                    }
                    return;
                }
            }

            // Default to Cron
            _rdoCron.Checked = true;
            _txtCron.Text = cron;
        }

        private void BtnBrowse_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog();
            ofd.Filter = "PowerShell スクリプト (*.ps1)|*.ps1|すべてのファイル (*.*)|*.*";

            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                _txtScriptPath.Text = ofd.FileName;
            }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            // 入力バリデーション
            string id = _txtId.Text.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                MessageBox.Show("ジョブIDは必須入力です。", "バリデーションエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _txtId.Focus();
                return;
            }

            if ((_editingJob == null || id != _editingJob.Id) && _existingIds.Contains(id))
            {
                MessageBox.Show($"ジョブID '{id}' はすでに登録されています。一意のIDを指定してください。", "バリデーションエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _txtId.Focus();
                return;
            }

            string scriptPath = _txtScriptPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                MessageBox.Show("スクリプトパスは必須入力です。", "バリデーションエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _txtScriptPath.Focus();
                return;
            }

            if (!File.Exists(scriptPath))
            {
                var confirm = MessageBox.Show(
                    "指定されたスクリプトファイルが存在しません。このまま保存しますか？",
                    "警告",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );
                if (confirm == DialogResult.No)
                {
                    _txtScriptPath.Focus();
                    return;
                }
            }

            // Cron式自動組み立て
            string cronExpression = string.Empty;

            if (_rdoInterval.Checked)
            {
                int val = (int)_numIntervalValue.Value;
                var unit = _cmbIntervalUnit.SelectedItem?.ToString();
                
                cronExpression = unit switch
                {
                    "秒" => $"*/{val} * * * * *",
                    "分" => $"0 */{val} * * * *",
                    "時間" => $"0 0 */{val} * * *",
                    _ => $"0 */{val} * * * *"
                };
            }
            else if (_rdoDaily.Checked)
            {
                var time = _dtpDailyTime.Value;
                cronExpression = $"0 {time.Minute} {time.Hour} * * *";
            }
            else if (_rdoWeekly.Checked)
            {
                // 選択された曜日のマッピング
                var selectedDays = new List<string>();
                string[] cronDaySymbols = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
                for (int i = 0; i < 7; i++)
                {
                    if (_chkDays[i].Checked)
                    {
                        selectedDays.Add(cronDaySymbols[i]);
                    }
                }

                if (selectedDays.Count == 0)
                {
                    MessageBox.Show("毎週実行する場合は、曜日を少なくとも1つ選択してください。", "バリデーションエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var time = _dtpWeeklyTime.Value;
                var dayString = string.Join(",", selectedDays);
                cronExpression = $"0 {time.Minute} {time.Hour} * * {dayString}";
            }
            else // _rdoCron.Checked
            {
                cronExpression = _txtCron.Text.Trim();
            }

            // スケジュールの検証
            try
            {
                var options = new CrontabSchedule.ParseOptions { IncludingSeconds = true };
                CrontabSchedule.Parse(cronExpression, options);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"スケジュールの組み立てに失敗したか、無効なCron式です。\nエラー: {ex.Message}", "検証エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 結果を構築
            JobConfigResult = new JobConfig
            {
                Id = id,
                Name = _txtName.Text.Trim(),
                Type = "PowerShell",
                ScriptPath = scriptPath,
                Arguments = _txtArguments.Text.Trim(),
                Schedule = cronExpression,
                TimeoutSeconds = (int)_numTimeout.Value,
                RetryCount = (int)_numRetry.Value,
                RetryDelaySeconds = (int)_numRetryDelay.Value,
                Enabled = _chkEnabled.Checked
            };

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
