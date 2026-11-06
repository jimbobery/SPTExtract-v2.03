using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SPTExtract
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Global exception handlers to ensure all exceptions are logged
            Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) =>
            {
                Logger.Log(e.Exception, "UI thread exception");
                try { MessageBox.Show("An unexpected error occurred. Please see the log file for details.", "SPTExtract", MessageBoxButtons.OK); } catch { }
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Logger.Log(ex ?? new Exception("Unknown domain exception"), "Unhandled domain exception");
                try { MessageBox.Show("A fatal error occurred. Please see the log file for details.", "SPTExtract", MessageBoxButtons.OK); } catch { }
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Logger.Log(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };

            try
            {
                Application.Run(new frmSPTExract());
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Fatal exception in Main");
                try { MessageBox.Show("A fatal error occurred during startup. Please see the log file for details.", "SPTExtract", MessageBoxButtons.OK); } catch { }
            }
        }
    }
}
