namespace MemoryCleaner;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var singleInstance = new SingleInstanceGuard(
            @"Local\MemoryCleaner_6F8A20E5_3A89_4D0D_AE1A_87D89A548B86");

        if (!singleInstance.IsPrimaryInstance)
        {
            MessageBox.Show(
                "内存整理工具已经在运行，可能位于右下角通知区域。\n\n请通过托盘图标打开主界面。",
                "软件已经在运行",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.Run(new MainForm());
    }
}
