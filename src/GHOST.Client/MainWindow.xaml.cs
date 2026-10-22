using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GHOST.Infrastructure.Persistence;
using GHOST.Models;
using Microsoft.EntityFrameworkCore;

namespace GHOST;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshStats();
    }

    private void RefreshStats()
    {
        using var db = new GhostDbContext();
        CasesCount.Text = db.Cases.Count(x => x.Status == "Open").ToString();
        ExperimentsCount.Text = db.Experiments.Count().ToString();
        EvidenceCount.Text = db.Evidence.Count(x => !x.IsArchived).ToString();
        LinksCount.Text = db.Relationships.Count().ToString();
        ClockText.Text = $"LOCAL // {DateTime.Now:HH:mm:ss}";
    }

    private void NewCase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window { Title = "GHOST // New Case", Width = 480, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, Background = (Brush)FindResource("BgBrush"), Foreground = Brushes.White, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "CREATE INVESTIGATION", FontSize = 18, Foreground = (Brush)FindResource("NeonBrush"), Margin = new Thickness(0,0,0,14) });
        panel.Children.Add(new TextBlock { Text = "Case title", Margin = new Thickness(0,0,0,5) });
        var title = new TextBox { Height = 34, Padding = new Thickness(8), Background = (Brush)FindResource("PanelAltBrush"), Foreground = Brushes.White, BorderBrush = (Brush)FindResource("LineBrush") };
        panel.Children.Add(title);
        panel.Children.Add(new TextBlock { Text = "Description", Margin = new Thickness(0,12,0,5) });
        var description = new TextBox { Height = 76, Padding = new Thickness(8), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = (Brush)FindResource("PanelAltBrush"), Foreground = Brushes.White, BorderBrush = (Brush)FindResource("LineBrush") };
        panel.Children.Add(description);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,16,0,0) };
        var cancel = new Button { Content = "CANCEL", Width = 90, Margin = new Thickness(0,0,8,0) }; cancel.Click += (_, _) => dialog.Close();
        var save = new Button { Content = "SAVE CASE", Width = 110 };
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(title.Text)) { MessageBox.Show(dialog, "Enter a case title.", "GHOST", MessageBoxButton.OK, MessageBoxImage.Information); return; } using var db = new GhostDbContext(); var item = new CaseFile { Title = title.Text.Trim(), Description = description.Text.Trim(), CaseNumber = $"CASE-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100,999)}" }; db.Cases.Add(item); db.AuditLogs.Add(new AuditEntry { EntityType = "Case", EntityId = item.Id, Action = "Created", Details = $"Case created: {item.Title}" }); db.SaveChanges(); dialog.Close(); RefreshStats(); MessageBox.Show(this, $"Case {item.CaseNumber} saved locally.", "GHOST", MessageBoxButton.OK, MessageBoxImage.Information); };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons); dialog.Content = panel; dialog.ShowDialog();
    }

    private void Cases_Click(object sender, RoutedEventArgs e)
    {
        using var db = new GhostDbContext();
        var dialog = new Window { Title = "GHOST // Case Files", Width = 850, Height = 540, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, Background = (Brush)FindResource("BgBrush"), Foreground = Brushes.White };
        var panel = new DockPanel { Margin = new Thickness(20) };
        var heading = new TextBlock { Text = "CASE FILES // LOCAL ARCHIVE", FontSize = 20, Foreground = (Brush)FindResource("NeonBrush"), Margin = new Thickness(0,0,0,16) }; DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var list = new ListView { Background = (Brush)FindResource("PanelBrush"), Foreground = Brushes.White, BorderBrush = (Brush)FindResource("LineBrush") };
        list.ItemsSource = db.Cases.OrderByDescending(x => x.CreatedAt).AsNoTracking().Select(x => $"{x.CaseNumber}    |    {x.Status}    |    {x.Title}    |    {x.CreatedAt.ToLocalTime():g}").ToList(); panel.Children.Add(list);
        var close = new Button { Content = "CLOSE", Width = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) }; close.Click += (_, _) => dialog.Close(); DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close); dialog.Content = panel; dialog.ShowDialog();
    }

    private void Board_Click(object sender, RoutedEventArgs e)
    {
        PageEyebrow.Text = "INTELLIGENCE / RELATIONSHIP MAPPING"; PageTitle.Text = "Investigation Board";
        MessageBox.Show(this, "The interactive relationship editor is planned for the next milestone. The database already includes a Relationships table.", "GHOST // Board", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void Terminal_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, "Local command console is planned for a later milestone. No system shell commands are executed by this build.", "GHOST // Terminal", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void ComingSoon_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this, "This module is part of the GHOST roadmap and is not implemented in this foundation build yet.", "GHOST", MessageBoxButton.OK, MessageBoxImage.Information);
}
