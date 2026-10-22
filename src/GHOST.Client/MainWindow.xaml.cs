using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GHOST.Infrastructure.Persistence;
using GHOST.Models;
using Microsoft.EntityFrameworkCore;

namespace GHOST;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clockTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    public MainWindow()
    {
        InitializeComponent();

        _clockTimer.Tick += (_, _) => ClockText.Text = $"LOCAL // {DateTime.Now:HH:mm:ss}";
        _clockTimer.Start();

        Loaded += (_, _) => RefreshStats();
        Closed += (_, _) => _clockTimer.Stop();
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        PageEyebrow.Text = "PRIVATE INVESTIGATION WORKSPACE";
        PageEyebrow.Visibility = Visibility.Collapsed;
        PageTitle.Text = "Command Center";
        RefreshStats();
    }

    private void RefreshStats()
    {
        using var db = new GhostDbContext();

        CasesCount.Text = db.Cases.Count(x => x.Status == "Open").ToString();
        ExperimentsCount.Text = db.Experiments.Count().ToString();
        EvidenceCount.Text = db.Evidence.Count(x => !x.IsArchived).ToString();
        LinksCount.Text = db.Relationships.Count().ToString();
        ClockText.Text = $"LOCAL // {DateTime.Now:HH:mm:ss}";

        var recentCases = db.Cases
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(4)
            .ToList();

        RecentCasesList.ItemsSource = recentCases
            .Select(x => new RecentCaseSummary(
                x.CaseNumber,
                x.Title,
                x.Status,
                x.CreatedAt.ToLocalTime().ToString("dd MMM  HH:mm")))
            .ToList();

        EmptyCasesHint.Visibility = recentCases.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void NewCase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "GHOST // New Case",
            Width = 480,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (Brush)FindResource("BgBrush"),
            Foreground = Brushes.White,
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = "CREATE INVESTIGATION",
            FontSize = 18,
            Foreground = (Brush)FindResource("NeonBrush"),
            Margin = new Thickness(0, 0, 0, 14)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Case title",
            Margin = new Thickness(0, 0, 0, 5)
        });

        var title = new TextBox
        {
            Height = 34,
            Padding = new Thickness(8),
            Background = (Brush)FindResource("PanelAltBrush"),
            Foreground = Brushes.White,
            BorderBrush = (Brush)FindResource("LineBrush")
        };
        panel.Children.Add(title);
        panel.Children.Add(new TextBlock
        {
            Text = "Description",
            Margin = new Thickness(0, 12, 0, 5)
        });

        var description = new TextBox
        {
            Height = 76,
            Padding = new Thickness(8),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = (Brush)FindResource("PanelAltBrush"),
            Foreground = Brushes.White,
            BorderBrush = (Brush)FindResource("LineBrush")
        };
        panel.Children.Add(description);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var cancel = new Button
        {
            Content = "CANCEL",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancel.Click += (_, _) => dialog.Close();

        var save = new Button { Content = "SAVE CASE", Width = 110 };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(title.Text))
            {
                MessageBox.Show(
                    dialog,
                    "Enter a case title.",
                    "GHOST",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                using var db = new GhostDbContext();

                var item = new CaseFile
                {
                    Title = title.Text.Trim(),
                    Description = description.Text.Trim(),
                    CaseNumber = $"CASE-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}"
                };

                db.Cases.Add(item);
                db.SaveChanges();

                db.AuditLogs.Add(new AuditEntry
                {
                    EntityType = "Case",
                    EntityId = item.Id,
                    Action = "Created",
                    Details = $"Case created: {item.Title}"
                });
                db.SaveChanges();

                dialog.Close();
                RefreshStats();

                MessageBox.Show(
                    this,
                    $"Case {item.CaseNumber} saved locally.",
                    "GHOST",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    dialog,
                    $"The case could not be saved.\n\n{ex.Message}",
                    "GHOST // Save error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.ShowDialog();
    }

    private void Cases_Click(object sender, RoutedEventArgs e)
    {
        using var db = new GhostDbContext();

        var dialog = new Window
        {
            Title = "GHOST // Case Files",
            Width = 850,
            Height = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (Brush)FindResource("BgBrush"),
            Foreground = Brushes.White,
            FontFamily = (FontFamily)FindResource("HackerFont")
        };

        var panel = new DockPanel { Margin = new Thickness(20) };
        var heading = new TextBlock
        {
            Text = "CASE FILES // LOCAL ARCHIVE",
            FontSize = 20,
            Foreground = (Brush)FindResource("NeonBrush"),
            Margin = new Thickness(0, 0, 0, 16)
        };
        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);

        var list = new ListView
        {
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = Brushes.White,
            BorderBrush = (Brush)FindResource("LineBrush"),
            FontFamily = (FontFamily)FindResource("HackerFont")
        };

        list.ItemsSource = db.Cases
            .OrderByDescending(x => x.CreatedAt)
            .AsNoTracking()
            .Select(x => $"{x.CaseNumber}    |    {x.Status}    |    {x.Title}    |    {x.CreatedAt.ToLocalTime():g}")
            .ToList();

        panel.Children.Add(list);

        var close = new Button
        {
            Content = "CLOSE",
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        close.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(close, Dock.Bottom);
        panel.Children.Add(close);
        dialog.Content = panel;
        dialog.ShowDialog();
    }

    private void Network_Click(object sender, RoutedEventArgs e)
    {
        var window = new InvestigationNetworkWindow { Owner = this };
        window.ShowDialog();
        RefreshStats();
    }

    private void Terminal_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Local command console is planned for a later milestone. No system shell commands are executed by this build.",
            "GHOST // Terminal",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ComingSoon_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "This module is part of the GHOST roadmap and is not implemented in this foundation build yet.",
            "GHOST",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private sealed record RecentCaseSummary(
        string CaseNumber,
        string Title,
        string Status,
        string CreatedAt);
}
