using System.Drawing.Drawing2D;

namespace MemoryCleaner;

internal sealed class MainForm : Form
{
    private readonly MemoryService _memoryService = new();
    private readonly Label _totalValue = CreateValueLabel();
    private readonly Label _usedValue = CreateValueLabel();
    private readonly Label _availableValue = CreateValueLabel();
    private readonly ProgressBar _usageBar = new();
    private readonly Label _usageText = new();
    private readonly Label _resultText = new();
    private readonly Label _statusText = new();
    private readonly TextBox _logBox = new();
    private readonly Button _cleanButton = new();
    private readonly Button _refreshButton = new();
    private readonly Button _floatingButton = new();
    private readonly NotifyIcon _trayIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly Icon? _applicationIcon;
    private FloatingForm? _floatingForm;
    private bool _exitRequested;
    private bool _trayTipShown;

    internal MainForm()
    {
        Text = "内存整理工具";
        _applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (_applicationIcon is not null)
        {
            Icon = _applicationIcon;
        }
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 590);
        Size = new Size(780, 640);
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildLayout();
        InitializeTrayIcon();
        Shown += (_, _) => RefreshMemory(showStatus: true);
        FormClosing += MainForm_FormClosing;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(26, 22, 26, 22),
            ColumnCount = 1,
            RowCount = 7
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        Controls.Add(root);

        var titlePanel = new Panel { Dock = DockStyle.Fill };
        titlePanel.Controls.Add(new Label
        {
            Text = "内存整理工具",
            Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59),
            AutoSize = true,
            Location = new Point(0, 0)
        });
        titlePanel.Controls.Add(new Label
        {
            Text = "安全整理当前用户进程的工作集，不结束进程",
            ForeColor = Color.FromArgb(100, 116, 139),
            AutoSize = true,
            Location = new Point(3, 39)
        });
        root.Controls.Add(titlePanel, 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 6, 0, 8) };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));
        cards.Controls.Add(CreateCard("总内存", _totalValue, Color.FromArgb(59, 130, 246)), 0, 0);
        cards.Controls.Add(CreateCard("已使用", _usedValue, Color.FromArgb(249, 115, 22)), 1, 0);
        cards.Controls.Add(CreateCard("可用内存", _availableValue, Color.FromArgb(16, 185, 129)), 2, 0);
        root.Controls.Add(cards, 0, 1);

        var progressPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 12, 4, 8) };
        _usageBar.Dock = DockStyle.Bottom;
        _usageBar.Height = 13;
        _usageBar.Style = ProgressBarStyle.Continuous;
        _usageText.AutoSize = true;
        _usageText.ForeColor = Color.FromArgb(71, 85, 105);
        _usageText.Location = new Point(1, 4);
        progressPanel.Controls.Add(_usageBar);
        progressPanel.Controls.Add(_usageText);
        root.Controls.Add(progressPanel, 0, 2);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(0, 8, 0, 8) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        _floatingButton.Text = "悬浮窗";
        _floatingButton.Dock = DockStyle.Fill;
        _floatingButton.Margin = new Padding(0, 0, 10, 0);
        StyleButton(_floatingButton, Color.White, Color.FromArgb(51, 65, 85));
        _floatingButton.Click += (_, _) => ShowFloatingWindow();
        _refreshButton.Text = "刷新";
        _refreshButton.Dock = DockStyle.Fill;
        _refreshButton.Margin = new Padding(0, 0, 10, 0);
        StyleButton(_refreshButton, Color.White, Color.FromArgb(51, 65, 85));
        _refreshButton.Click += (_, _) => RefreshMemory(showStatus: true);
        _cleanButton.Text = "一键释放";
        _cleanButton.Dock = DockStyle.Fill;
        _cleanButton.Margin = new Padding(0);
        StyleButton(_cleanButton, Color.FromArgb(37, 99, 235), Color.White);
        _cleanButton.Click += CleanButton_Click;
        actions.Controls.Add(_floatingButton, 1, 0);
        actions.Controls.Add(_refreshButton, 2, 0);
        actions.Controls.Add(_cleanButton, 3, 0);
        root.Controls.Add(actions, 0, 3);

        _resultText.Dock = DockStyle.Fill;
        _resultText.TextAlign = ContentAlignment.MiddleLeft;
        _resultText.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        _resultText.ForeColor = Color.FromArgb(30, 64, 175);
        _resultText.Text = "尚未执行内存整理";
        root.Controls.Add(_resultText, 0, 4);

        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.BackColor = Color.White;
        _logBox.ForeColor = Color.FromArgb(51, 65, 85);
        _logBox.BorderStyle = BorderStyle.FixedSingle;
        _logBox.Font = new Font("Microsoft YaHei UI", 9F);
        root.Controls.Add(_logBox, 0, 5);

        _statusText.Dock = DockStyle.Fill;
        _statusText.TextAlign = ContentAlignment.MiddleLeft;
        _statusText.ForeColor = Color.FromArgb(100, 116, 139);
        _statusText.Text = "就绪";
        root.Controls.Add(_statusText, 0, 6);

        AppendLog("工具已启动。建议仅在内存紧张或排查占用时手动使用。", "提示");
    }

    private async void CleanButton_Click(object? sender, EventArgs e)
    {
        SetBusy(true);
        MemorySnapshot before;

        try
        {
            before = _memoryService.ReadMemory();
            AppendLog($"开始整理。释放前：已用 {FormatBytes(before.UsedBytes)}，可用 {FormatBytes(before.AvailableBytes)}");

            CleanupResult cleanup = await Task.Run(_memoryService.TrimUserProcesses);
            await Task.Delay(600);
            MemorySnapshot after = _memoryService.ReadMemory();

            ApplyCleanupResult(before, after, cleanup);
        }
        catch (Exception ex)
        {
            _statusText.Text = "整理未完成";
            AppendLog(ex.Message, "错误");
            MessageBox.Show(this, ex.Message, "内存整理工具", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RefreshMemory(bool showStatus)
    {
        try
        {
            UpdateMemoryDisplay(_memoryService.ReadMemory());
            if (showStatus)
            {
                _statusText.Text = $"已刷新 · {DateTime.Now:HH:mm:ss}";
            }
        }
        catch (Exception ex)
        {
            _statusText.Text = ex.Message;
        }
    }

    private void ShowFloatingWindow()
    {
        if (_floatingForm is not null && !_floatingForm.IsDisposed)
        {
            _floatingForm.Show();
            _floatingForm.Activate();
            return;
        }

        _floatingForm = new FloatingForm(_memoryService, ApplyCleanupResult, RestoreMainWindow);
        _floatingForm.FormClosed += (_, _) => _floatingForm = null;
        // 悬浮窗不设置 Owner，主界面隐藏到托盘后它仍可独立显示。
        _floatingForm.Show();
        AppendLog("已打开悬浮窗；小窗仅刷新显示，不会自动清理。", "提示");
    }

    private void InitializeTrayIcon()
    {
        var openMainItem = new ToolStripMenuItem("打开主界面");
        openMainItem.Click += (_, _) => RestoreMainWindow();

        var showFloatingItem = new ToolStripMenuItem("显示悬浮窗");
        showFloatingItem.Click += (_, _) => ShowFloatingWindow();

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitFromTray();

        _trayMenu.Items.Add(openMainItem);
        _trayMenu.Items.Add(showFloatingItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);

        _trayIcon.Icon = _applicationIcon ?? SystemIcons.Application;
        _trayIcon.Text = "内存整理工具 - 后台运行";
        _trayIcon.ContextMenuStrip = _trayMenu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => RestoreMainWindow();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideMainWindowToTray();
            return;
        }

        _exitRequested = true;
        _trayIcon.Visible = false;

        if (_floatingForm is not null && !_floatingForm.IsDisposed)
        {
            _floatingForm.Close();
        }
    }

    private void HideMainWindowToTray()
    {
        ShowInTaskbar = false;
        Hide();

        if (!_trayTipShown)
        {
            _trayIcon.ShowBalloonTip(
                2500,
                "内存整理工具仍在运行",
                "双击托盘图标可恢复主界面；右键选择“退出”才会结束程序。",
                ToolTipIcon.Info);
            _trayTipShown = true;
        }
    }

    private void RestoreMainWindow()
    {
        ShowInTaskbar = true;

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        Activate();
        BringToFront();
        RefreshMemory(showStatus: true);
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        _trayIcon.Visible = false;

        if (_floatingForm is not null && !_floatingForm.IsDisposed)
        {
            _floatingForm.Close();
        }

        Close();
    }

    private void ApplyCleanupResult(MemorySnapshot before, MemorySnapshot after, CleanupResult cleanup)
    {
        UpdateMemoryDisplay(after);
        long availableChange = unchecked((long)after.AvailableBytes - (long)before.AvailableBytes);
        ulong released = availableChange > 0 ? (ulong)availableChange : 0;
        _resultText.Text = $"释放前已用 {FormatBytes(before.UsedBytes)}  →  释放后已用 {FormatBytes(after.UsedBytes)}  ｜  本次释放 {FormatBytes(released)}";

        string summary = $"完成：成功 {cleanup.Succeeded}，跳过 {cleanup.Skipped}，权限不足 {cleanup.AccessDenied}，已退出 {cleanup.Exited}，其他失败 {cleanup.Failed}。";
        AppendLog(summary, "结果");

        if (availableChange < 0)
        {
            AppendLog($"整理期间系统可用内存变化 {FormatSignedBytes(availableChange)}；后台活动会影响前后数值。", "说明");
        }

        foreach (string detail in cleanup.FailureDetails)
        {
            AppendLog(detail, "跳过");
        }

        _statusText.Text = "整理完成。Windows 会按需重新载入被移出的内存页。";
    }

    private void UpdateMemoryDisplay(MemorySnapshot snapshot)
    {
        _totalValue.Text = FormatBytes(snapshot.TotalBytes);
        _usedValue.Text = FormatBytes(snapshot.UsedBytes);
        _availableValue.Text = FormatBytes(snapshot.AvailableBytes);
        _usageBar.Value = Math.Clamp((int)snapshot.LoadPercent, 0, 100);
        _usageText.Text = $"物理内存使用率 {snapshot.LoadPercent}%";
    }

    private void SetBusy(bool busy)
    {
        _cleanButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _floatingButton.Enabled = !busy;
        _cleanButton.Text = busy ? "正在整理…" : "一键释放";
        UseWaitCursor = busy;
        _statusText.Text = busy ? "正在安全整理当前用户进程，请稍候…" : _statusText.Text;
    }

    private void AppendLog(string message, string kind = "信息")
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] [{kind}] {message}{Environment.NewLine}");
    }

    private static Panel CreateCard(string title, Label value, Color accent)
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(4),
            BackColor = Color.White,
            AccentColor = accent
        };
        card.Controls.Add(new Label
        {
            Text = title,
            ForeColor = Color.FromArgb(100, 116, 139),
            AutoSize = true,
            Location = new Point(18, 17)
        });
        value.Location = new Point(16, 48);
        card.Controls.Add(value);
        return card;
    }

    private static Label CreateValueLabel() => new()
    {
        Text = "--",
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
        ForeColor = Color.FromArgb(30, 41, 59)
    };

    private static void StyleButton(Button button, Color backColor, Color foreColor)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        button.FlatAppearance.BorderSize = backColor == Color.White ? 1 : 0;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    internal static string FormatBytes(ulong bytes)
    {
        const double gib = 1024d * 1024d * 1024d;
        const double mib = 1024d * 1024d;
        return bytes >= gib ? $"{bytes / gib:F2} GB" : $"{bytes / mib:F0} MB";
    }

    private static string FormatSignedBytes(long bytes)
    {
        string sign = bytes >= 0 ? "+" : "−";
        ulong magnitude = bytes == long.MinValue ? ((ulong)long.MaxValue + 1) : (ulong)Math.Abs(bytes);
        return sign + FormatBytes(magnitude);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            _applicationIcon?.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class RoundedPanel : Panel
{
    internal Color AccentColor { get; set; } = Color.DodgerBlue;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var accent = new SolidBrush(AccentColor);
        e.Graphics.FillRectangle(accent, 0, 0, 5, Height);
        using var border = new Pen(Color.FromArgb(226, 232, 240));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}
