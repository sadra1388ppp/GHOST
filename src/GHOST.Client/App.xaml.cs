namespace GHOST;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        try { Infrastructure.Persistence.GhostLocalStore.Initialize(); }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"GHOST could not initialize local storage on your Desktop.\n\n{ex.Message}", "GHOST · Startup error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
