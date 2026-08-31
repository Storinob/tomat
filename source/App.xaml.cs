using System.Windows;
namespace tomat
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            NativeOsdHider.StartMonitoring();
        }
    }
}