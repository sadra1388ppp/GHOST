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
                $"GHOST requires MariaDB. Check that the MariaDB service is running, create the database/user in HeidiSQL, and configure %LOCALAPPDATA%\\GHOST\\database.json.\n\n{ex.Message}",
                "GHOST · Startup error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            Shutdown(1);
        }
    }
}
