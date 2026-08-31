using System.Diagnostics;
using System.Windows;

namespace tomat
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            KillOldInstances();
            base.OnStartup(e);
            NativeOsdHider.StartMonitoring();
        }
        private void KillOldInstances()
        {
            Process currentProcess = Process.GetCurrentProcess();
            Process[] processes = Process.GetProcessesByName(currentProcess.ProcessName);
            
            foreach (Process p in processes)
            {
                if (p.Id != currentProcess.Id)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(1000);
                    }
                    catch 
                    { 
                        // a
                    }
                }
            }
        }
    }
}