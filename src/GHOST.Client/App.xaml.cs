namespace GHOST;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Infrastructure.Persistence.GhostDbContext.Initialize();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"GHOST could not initialize the configured database. Check the database provider and connection settings.\n\n{ex.Message}",
                "GHOST · Startup error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            Shutdown(1);
        }
    }
}
