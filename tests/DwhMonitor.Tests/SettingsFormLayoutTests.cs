using DwhMonitor.App;
using DwhMonitor.Core;

namespace DwhMonitor.Tests;

public sealed class SettingsFormLayoutTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DwhMonitorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void AccountEditorShowsSeparateUsernameAndPasswordRows()
    {
        Directory.CreateDirectory(_directory);
        var store = new AppSettingsStore(Path.Combine(_directory, "config.json"));
        using var form = new SettingsForm(
            store,
            (_, _) => Task.FromResult(new NotificationResult(true)));

        form.CreateControl();
        form.PerformLayout();
        var labels = Descendants(form).OfType<Label>().ToArray();
        var usernameLabel = Assert.Single(labels, label => label.Text == "账号");
        var passwordLabel = Assert.Single(labels, label => label.Text == "密码");
        var fields = Assert.IsType<TableLayoutPanel>(usernameLabel.Parent);
        fields.PerformLayout();
        var username = Assert.IsType<TextBox>(fields.GetControlFromPosition(1, 0));
        var password = Assert.IsType<TextBox>(fields.GetControlFromPosition(1, 1));

        Assert.Same(fields, passwordLabel.Parent);
        Assert.True(username.Width > 0 && username.Height > 0);
        Assert.True(password.Width > 0 && password.Height > 0);
        Assert.True(password.Top >= username.Bottom);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
