using Volvox.Sim;

namespace Volvox.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        int seed = Environment.TickCount & 0xFFFF;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--seed" && int.TryParse(args[i + 1], out int s)) seed = s;

        // Optional tuning file next to the exe (same format the test runner uses).
        var config = new SimConfig();
        string path = Path.Combine(AppContext.BaseDirectory, "volvox.config.json");
        if (File.Exists(path))
        {
            try { config = SimConfig.Load(path); }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read volvox.config.json — using defaults.\n\n{ex.Message}", "Volvox",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        Application.Run(new MainForm(config, seed));
    }
}
