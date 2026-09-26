namespace MemoryCleaner;

internal sealed class FloatingForm : Form
{
    private readonly MemoryService _memoryService;
    private readonly Action<MemorySnapshot, MemorySnapshot, CleanupResult> _onCleanupCompleted;
    private readonly Action _showMainWindow;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _zOrderTimer = new() { Interval = 700 };
    private readonly Label _usageValue = new();
    private readonly Label _availableValue = new();
    private readonly Label _statusText = new();
    private readonly Panel _barTrack = new();
    private readonly Panel _barFill = new();
    private readonly Button _cleanButton = new();
    private readonly Button _autoLayerButton = new();
    private readonly Button _topLayerButton = new();
    private readonly Button _bottomLayerButton = new();
    private Point _dragCursorStart;
    private Point _dragWindowStart;
    private bool _dragging;
    private bool _isLowered;
    private FloatingLayerMode _layerMode = FloatingLayerMode.Auto;
    private uint _lastUsagePercent;

    internal FloatingForm(
        MemoryService memoryService,
        Action<MemorySnapshot, MemorySnapshot, CleanupResult> onCleanupCompleted,
        Action showMainWindow)
    {
        _memoryService = memoryService;
        _onCleanupCompleted = onCleanupCompleted;
        _showMainWindow = showMainWindow;

        Text = "内存悬浮窗";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(330, 205);
        MinimumSize = Size;
        MaximumSize = Size;
        BackColor = Color.FromArgb(22, 31, 48);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Opacity = 0.97;

        BuildLayout();
        _refreshTimer.Tick += (_, _) => RefreshMemory();
        _zOrderTimer.Tick += (_, _) => UpdateFloatingLayer();
        Shown += FloatingForm_Shown;
        FormClosed += (_, _) =>
        {
            _refreshTimer.Dispose();
            _zOrderTimer.Dispose();
        };
    }

    private void BuildLayout()
    {
        var title = new Label
        {
            Text = "内存状态",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(226, 232, 240),
            Location = new Point(16, 12)
        };
        Controls.Add(title);

        var mainButton = CreateHeaderButton("▢", 270);
        mainButton.AccessibleName = "打开主界面";
        mainButton.Click += (_, _) => OpenMainWindow();
        Controls.Add(mainButton);

        var closeButton = CreateHeaderButton("×", 300);
        closeButton.AccessibleName = "关闭悬浮窗";
        closeButton.Click += (_, _) => Close();
        Controls.Add(closeButton);

        _usageValue.Text = "--%";
        _usageValue.AutoSize = true;
        _usageValue.Font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold);
        _usageValue.ForeColor = Color.FromArgb(96, 165, 250);
        _usageValue.Location = new Point(15, 44);
        Controls.Add(_usageValue);

        var availableTitle = new Label
        {
            Text = "可用",
            AutoSize = true,
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(126, 50)
        };
        Controls.Add(availableTitle);

        _availableValue.Text = "--";
        _availableValue.AutoSize = true;
        _availableValue.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        _availableValue.ForeColor = Color.FromArgb(226, 232, 240);
        _availableValue.Location = new Point(124, 70);
        Controls.Add(_availableValue);

        _barTrack.Location = new Point(17, 99);
        _barTrack.Size = new Size(296, 7);
        _barTrack.BackColor = Color.FromArgb(51, 65, 85);
        _barTrack.Controls.Add(_barFill);
        _barFill.Dock = DockStyle.Left;
        _barFill.BackColor = Color.FromArgb(59, 130, 246);
        Controls.Add(_barTrack);

        _statusText.Text = "每 2 秒刷新";
        _statusText.AutoEllipsis = true;
        _statusText.ForeColor = Color.FromArgb(148, 163, 184);
        _statusText.Location = new Point(17, 126);
        _statusText.Size = new Size(194, 25);
        _statusText.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(_statusText);

        _cleanButton.Text = "释放";
        _cleanButton.Location = new Point(226, 119);
        _cleanButton.Size = new Size(87, 34);
        _cleanButton.FlatStyle = FlatStyle.Flat;
        _cleanButton.FlatAppearance.BorderSize = 0;
        _cleanButton.BackColor = Color.FromArgb(37, 99, 235);
        _cleanButton.ForeColor = Color.White;
        _cleanButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _cleanButton.Cursor = Cursors.Hand;
        _cleanButton.Click += CleanButton_Click;
        Controls.Add(_cleanButton);

        ConfigureLayerButton(_autoLayerButton, "自动", new Point(17, 164), new Size(88, 28));
        ConfigureLayerButton(_topLayerButton, "置于顶层", new Point(111, 164), new Size(94, 28));
        ConfigureLayerButton(_bottomLayerButton, "置于底层", new Point(211, 164), new Size(102, 28));
        _autoLayerButton.Click += (_, _) => SetLayerMode(FloatingLayerMode.Auto);
        _topLayerButton.Click += (_, _) => SetLayerMode(FloatingLayerMode.AlwaysTop);
        _bottomLayerButton.Click += (_, _) => SetLayerMode(FloatingLayerMode.AlwaysBottom);
        Controls.Add(_autoLayerButton);
        Controls.Add(_topLayerButton);
        Controls.Add(_bottomLayerButton);
        UpdateLayerButtonStyles();

        AttachDrag(title);
        AttachDrag(this);

        var toolTip = new ToolTip();
        toolTip.SetToolTip(mainButton, "打开主界面");
        toolTip.SetToolTip(closeButton, "关闭悬浮窗");
        toolTip.SetToolTip(_cleanButton, "手动整理当前用户进程工作集");
        toolTip.SetToolTip(_autoLayerButton, "根据当前前台软件自动切换层级");
        toolTip.SetToolTip(_topLayerButton, "始终显示在其他窗口上方");
        toolTip.SetToolTip(_bottomLayerButton, "始终显示在其他窗口后方");
    }

    private void FloatingForm_Shown(object? sender, EventArgs e)
    {
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 20, area.Top + 20);
        RefreshMemory();
        _refreshTimer.Start();
        _zOrderTimer.Start();
        UpdateFloatingLayer();
    }

    private async void CleanButton_Click(object? sender, EventArgs e)
    {
        _cleanButton.Enabled = false;
        _cleanButton.Text = "整理中";
        _statusText.Text = "正在安全整理…";
        _refreshTimer.Stop();

        try
        {
            MemorySnapshot before = _memoryService.ReadMemory();
            CleanupResult cleanup = await Task.Run(_memoryService.TrimUserProcesses);
            await Task.Delay(600);
            MemorySnapshot after = _memoryService.ReadMemory();

            if (IsDisposed)
            {
                return;
            }

            RefreshMemory(after);
            long change = unchecked((long)after.AvailableBytes - (long)before.AvailableBytes);
            ulong released = change > 0 ? (ulong)change : 0;
            _statusText.Text = $"本次释放 {MainForm.FormatBytes(released)}";
            _onCleanupCompleted(before, after, cleanup);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _statusText.Text = ex.Message;
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _cleanButton.Enabled = true;
                _cleanButton.Text = "释放";
                _refreshTimer.Start();
            }
        }
    }

    private void RefreshMemory()
    {
        try
        {
            RefreshMemory(_memoryService.ReadMemory());
        }
        catch (Exception ex)
        {
            _statusText.Text = ex.Message;
        }
    }

    private void RefreshMemory(MemorySnapshot snapshot)
    {
        _lastUsagePercent = snapshot.LoadPercent;
        _usageValue.Text = $"{snapshot.LoadPercent}%";
        _availableValue.Text = MainForm.FormatBytes(snapshot.AvailableBytes);
        int width = (int)Math.Round(_barTrack.ClientSize.Width * snapshot.LoadPercent / 100d);
        _barFill.Width = Math.Clamp(width, 0, _barTrack.ClientSize.Width);
        _barFill.BackColor = _lastUsagePercent >= 85
            ? Color.FromArgb(239, 68, 68)
            : _lastUsagePercent >= 70
                ? Color.FromArgb(249, 115, 22)
                : Color.FromArgb(59, 130, 246);
    }

    private void OpenMainWindow()
    {
        _showMainWindow();
    }

    private void UpdateFloatingLayer()
    {
        if (_layerMode == FloatingLayerMode.AlwaysTop)
        {
            ApplyFloatingLayer(shouldFloat: true);
            return;
        }

        if (_layerMode == FloatingLayerMode.AlwaysBottom)
        {
            ApplyFloatingLayer(shouldFloat: false);
            return;
        }

        IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetWindowThreadProcessId(foregroundWindow, out uint foregroundProcessId);
        bool shouldFloat = foregroundProcessId == (uint)Environment.ProcessId ||
            NativeMethods.IsDesktopOrTaskbarWindow(foregroundWindow);

        ApplyFloatingLayer(shouldFloat);
    }

    private void ApplyFloatingLayer(bool shouldFloat)
    {
        if (shouldFloat)
        {
            if (!TopMost)
            {
                TopMost = true;
            }

            _isLowered = false;
        }
        else
        {
            if (TopMost)
            {
                TopMost = false;
            }

            if (!_isLowered)
            {
                // 外部游戏或软件位于前台时，不遮挡其内容。
                SendToBack();
            }

            _isLowered = true;
        }
    }

    private void SetLayerMode(FloatingLayerMode mode)
    {
        _layerMode = mode;
        UpdateLayerButtonStyles();
        _statusText.Text = mode switch
        {
            FloatingLayerMode.AlwaysTop => "模式：强制置顶",
            FloatingLayerMode.AlwaysBottom => "模式：强制置底",
            _ => "模式：自动切换"
        };
        UpdateFloatingLayer();
    }

    private void UpdateLayerButtonStyles()
    {
        StyleLayerButton(_autoLayerButton, _layerMode == FloatingLayerMode.Auto);
        StyleLayerButton(_topLayerButton, _layerMode == FloatingLayerMode.AlwaysTop);
        StyleLayerButton(_bottomLayerButton, _layerMode == FloatingLayerMode.AlwaysBottom);
    }

    private static void ConfigureLayerButton(Button button, string text, Point location, Size size)
    {
        button.Text = text;
        button.Location = location;
        button.Size = size;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.TabStop = false;
    }

    private static void StyleLayerButton(Button button, bool active)
    {
        button.BackColor = active ? Color.FromArgb(37, 99, 235) : Color.FromArgb(30, 41, 59);
        button.ForeColor = active ? Color.White : Color.FromArgb(203, 213, 225);
        button.FlatAppearance.BorderColor = active
            ? Color.FromArgb(96, 165, 250)
            : Color.FromArgb(71, 85, 105);
        button.FlatAppearance.BorderSize = 1;
    }

    private void AttachDrag(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _dragging = true;
            _dragCursorStart = Cursor.Position;
            _dragWindowStart = Location;
        };
        control.MouseMove += (_, e) =>
        {
            if (!_dragging || e.Button != MouseButtons.Left)
            {
                return;
            }

            Point offset = new(Cursor.Position.X - _dragCursorStart.X, Cursor.Position.Y - _dragCursorStart.Y);
            Location = new Point(_dragWindowStart.X + offset.X, _dragWindowStart.Y + offset.Y);
        };
        control.MouseUp += (_, _) => _dragging = false;
    }

    private static Button CreateHeaderButton(string text, int left) => new()
    {
        Text = text,
        Location = new Point(left, 6),
        Size = new Size(28, 28),
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.Transparent,
        ForeColor = Color.FromArgb(203, 213, 225),
        Font = new Font("Microsoft YaHei UI", 11F),
        Cursor = Cursors.Hand,
        TabStop = false
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Color.FromArgb(71, 85, 105));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}

internal enum FloatingLayerMode
{
    Auto,
    AlwaysTop,
    AlwaysBottom
}
