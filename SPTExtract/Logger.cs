using System;
using System.IO;

public static class Logger
{
    private static readonly object _sync = new object();
    private static readonly string _logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sptextract.log");

    public static void Log(Exception ex, string context = null)
    {
        try
        {
            lock (_sync)
            {
                using (var sw = File.AppendText(_logFile))
                {
                    sw.WriteLine("[{0}] {1}", DateTime.Now.ToString("s"), context ?? "Exception");
                    if (ex != null)
                    {
                        sw.WriteLine(ex.ToString());
                    }
                    sw.WriteLine();
                    sw.Flush();
                }
            }
        }
        catch
        {
            // Swallow any logging exceptions to avoid secondary faults
        }
    }

    public static void Log(string message, string context = null)
    {
        try
        {
            lock (_sync)
            {
                using (var sw = File.AppendText(_logFile))
                {
                    sw.WriteLine("[{0}] {1}: {2}", DateTime.Now.ToString("s"), context ?? "Info", message);
                    sw.Flush();
                }
            }
        }
        catch
        {
        }
    }
}
