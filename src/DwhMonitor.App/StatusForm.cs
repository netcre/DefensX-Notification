using DwhMonitor.Core;

namespace DwhMonitor.App;

internal sealed class StatusForm : Form
{
    private readonly Dictionary<string, Label> _values = [];
    private bool _allowClose;

    public StatusForm()
    {
        Text = "盾网实时监控";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(520, 410);
        ClientSize = new Size(560, 440);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 10
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var _ in Enumerable.Range(0, 10))
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 10));
        }

        AddRow(layout, 0, "网吧", "name");
        AddRow(layout, 1, "GUID", "guid");
        AddRow(layout, 2, "网吧状态", "online");
        AddRow(layout, 3, "终端数据", "terminals");
        AddRow(layout, 4, "VIP终端额度", "vip");
        AddRow(layout, 5, "防御次数", "defense");
        AddRow(layout, 6, "账号会话", "session");
        AddRow(layout, 7, "连续接口失败", "failures");
        AddRow(layout, 8, "最近成功采集", "lastSuccess");
        AddRow(layout, 9, "当前告警", "alerts");
        Controls.Add(layout);
        FormClosing += (_, eventArgs) =>
        {
            if (!_allowClose)
            {
                eventArgs.Cancel = true;
                Hide();
            }
        };
        UpdateStatus(MonitorRuntimeStatus.Starting);
    }

    public void UpdateStatus(MonitorRuntimeStatus status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateStatus(status));
            return;
        }

        var snapshot = status.LastSnapshot;
        Set("name", snapshot?.NetbarName ?? "等待采集");
        Set("guid", MonitorDefaults.TargetGuid.ToString());
        Set("online", snapshot is null ? "未知" : snapshot.Online ? "在线" : "离线");
        Set("terminals", snapshot is null ? "未知" : $"{snapshot.TerminalCurrent} / {snapshot.TerminalTotal}");
        Set("vip", snapshot?.VipLimit.ToString() ?? "未知");
        Set("defense", snapshot?.DefenseCount.ToString() ?? "未知");
        Set("session", status.SessionValid ? "有效" : "失效或等待登录");
        Set("failures", status.ConsecutiveApiFailures.ToString());
        Set("lastSuccess", status.LastSuccessUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚无");
        Set("alerts", status.ActiveAlerts.Count == 0
            ? status.LastError is null ? "无" : status.LastError
            : string.Join("、", status.ActiveAlerts.Select(ToChinese)));
    }

    public void Shutdown()
    {
        _allowClose = true;
        Close();
    }

    private void AddRow(TableLayoutPanel layout, int row, string title, string key)
    {
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText
        };
        var valueLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        _values[key] = valueLabel;
        layout.Controls.Add(titleLabel, 0, row);
        layout.Controls.Add(valueLabel, 1, row);
    }

    private void Set(string key, string value) => _values[key].Text = value;

    private static string ToChinese(AlertKind kind) => kind switch
    {
        AlertKind.TerminalExceeded => "VIP终端超额",
        AlertKind.SessionExpired => "账号掉线",
        AlertKind.NetbarOffline => "云机房离线",
        AlertKind.ApiUnavailable => "接口不可达",
        AlertKind.AutoLoginFailed => "自动登录失败",
        _ => kind.ToString()
    };
}
