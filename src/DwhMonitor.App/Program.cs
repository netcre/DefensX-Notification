namespace DwhMonitor.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var singleInstance = new Mutex(initiallyOwned: true, "Local\\DwhMonitor", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("盾网监控已经在运行。", "盾网监控", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (args is [var acceptanceMode, var countText] &&
            acceptanceMode is "--ocr-acceptance" or "--ocr-acceptance-capture" &&
            int.TryParse(countText, out var count) &&
            count is >= 1 and <= 100)
        {
            Application.Run(new OcrAcceptanceApplicationContext(
                count,
                captureSamples: acceptanceMode == "--ocr-acceptance-capture"));
            return;
        }

        Application.Run(new TrayApplicationContext());
    }
}
