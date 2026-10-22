using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shapes;
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

    private const double NodeWidth = 220;
    private const double NodeHeight = 84;
    private const double ColumnSpacing = 265;
    private const double RowSpacing = 145;

    private List<NetworkNodeView> _nodes = [];
    private List<NetworkConnectionView> _connections = [];

    public MainWindow()
    {
        InitializeComponent();

        _clockTimer.Tick += (_, _) => ClockText.Text = $"LOCAL // {DateTime.Now:HH:mm:ss}";
        _clockTimer.Start();

        Loaded += (_, _) =>
        {
            RefreshStats();
            RefreshNetwork();
        };
        Closed += (_, _) => _clockTimer.Stop();
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        NetworkView.Visibility = Visibility.Collapsed;
        DashboardView.Visibility = Visibility.Visible;
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
        DashboardView.Visibility = Visibility.Collapsed;
        NetworkView.Visibility = Visibility.Visible;
        RefreshNetwork();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshNetwork();

    private void RefreshNetwork()
    {
        try
        {
            using var db = new GhostDbContext();

            // Materialize records before building display labels. ToUpperInvariant is a
            // .NET presentation operation, not something we want translated into SQL.
            var cases = db.Cases.AsNoTracking().OrderBy(x => x.CreatedAt).ToList();
            var evidence = db.Evidence.AsNoTracking()
                .Where(x => !x.IsArchived)
                .OrderBy(x => x.CollectedAt)
                .ToList();
            var experiments = db.Experiments.AsNoTracking()
                .OrderBy(x => x.CreatedAt)
                .ToList();
            var observations = db.Observations.AsNoTracking()
                .OrderBy(x => x.RecordedAt)
                .ToList();

            _nodes =
            [
                .. cases.Select(x => new NetworkNodeView("Case", x.Id, x.CaseNumber,
                    $"{x.Status.ToUpperInvariant()}  /  {x.Title}")),
                .. evidence.Select(x => new NetworkNodeView("Evidence", x.Id, x.Title,
                    $"{x.EvidenceType.ToUpperInvariant()}  /  ID {x.Id}")),
                .. experiments.Select(x => new NetworkNodeView("Experiment", x.Id, x.Title,
                    $"{x.Category.ToUpperInvariant()}  /  {x.Status.ToUpperInvariant()}")),
                .. observations.Select(x => new NetworkNodeView("Observation", x.Id, x.Description,
                    $"RECORDED  /  ID {x.Id}"))
            ];

            var nodeLookup = _nodes.ToDictionary(x => x.Key, x => x);
            var savedRelationships = db.Relationships.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToList();

            _connections = savedRelationships
                .Select(x => new NetworkConnectionView(
                    x.Id,
                    x.RelationshipType,
                    GetNodeLabel(nodeLookup, x.SourceType, x.SourceId),
                    GetNodeLabel(nodeLookup, x.TargetType, x.TargetId),
                    x.SourceType, x.SourceId, x.TargetType, x.TargetId,
                    Math.Clamp(x.Confidence, 0, 1),
                    x.Description,
                    x.CreatedAt))
                .ToList();

            NodesCountText.Text = $"NODES // {_nodes.Count}";
            RelationshipsCountText.Text = $"LINKS // {_connections.Count}";
            NewRelationshipButton.IsEnabled = _nodes.Count >= 2;

            SourceRecordComboBox.ItemsSource = _nodes;
            TargetRecordComboBox.ItemsSource = _nodes;
            if (_nodes.Count > 0 && SourceRecordComboBox.SelectedIndex < 0)
                SourceRecordComboBox.SelectedIndex = 0;
            if (_nodes.Count > 1 && TargetRecordComboBox.SelectedIndex < 0)
                TargetRecordComboBox.SelectedIndex = 1;
            CreateConnectionStatus.Text = _nodes.Count >= 2
                ? "Choose two different records to create a connection."
                : "At least two records are required to create a connection.";

            ConnectionsList.ItemsSource = _connections;
            EmptyConnectionsHint.Visibility = _connections.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            EmptyGraphOverlay.Visibility = _nodes.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            DrawGraph();
            SelectedNodeTitle.Text = "Select a node on the map";
            SelectedNodeDetails.Text = "Click a node to inspect its local record.";
            ConnectionsList.SelectedItem = null;
            DeleteConnectionButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Could not load the investigation network.\n\n{ex.Message}",
                "GHOST // Network error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string GetNodeLabel(
        IReadOnlyDictionary<string, NetworkNodeView> nodes,
        string type,
        int id)
    {
        var key = $"{type}:{id}";
        return nodes.TryGetValue(key, out var node)
            ? $"{node.Type.ToUpperInvariant()} // {node.Title}"
            : $"{type.ToUpperInvariant()} // MISSING RECORD #{id}";
    }

    private void DrawGraph()
    {
        GraphCanvas.Children.Clear();

        const int columns = 4;
        var rows = Math.Max(1, (int)Math.Ceiling(_nodes.Count / (double)columns));
        GraphCanvas.Width = 1100;
        GraphCanvas.Height = Math.Max(560, 55 + rows * RowSpacing);

        var positions = new Dictionary<string, Point>();
        for (var i = 0; i < _nodes.Count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            positions[_nodes[i].Key] = new Point(
                28 + column * ColumnSpacing + NodeWidth / 2,
                35 + row * RowSpacing + NodeHeight / 2);
        }

        var edgeLabels = new List<(Point Point, string Text, Brush Brush)>();

        // Edges are rendered before nodes so they sit naturally behind the cards.
        foreach (var connection in _connections)
        {
            if (!positions.TryGetValue($"{connection.SourceType}:{connection.SourceId}", out var source) ||
                !positions.TryGetValue($"{connection.TargetType}:{connection.TargetId}", out var target))
            {
                continue;
            }

            var dx = target.X - source.X;
            var dy = target.Y - source.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 1)
            {
                continue;
            }

            var normalizedX = dx / distance;
            var normalizedY = dy / distance;
            var sourceScale = RectangleEdgeScale(normalizedX, normalizedY, NodeWidth / 2, NodeHeight / 2);
            var targetScale = RectangleEdgeScale(normalizedX, normalizedY, NodeWidth / 2, NodeHeight / 2);
            var start = new Point(source.X + normalizedX * sourceScale, source.Y + normalizedY * sourceScale);
            var end = new Point(target.X - normalizedX * targetScale, target.Y - normalizedY * targetScale);

            var isHypothesisOrConflict =
                connection.RelationshipType.Contains("Hypothesis", StringComparison.OrdinalIgnoreCase) ||
                connection.RelationshipType.Contains("Contradicts", StringComparison.OrdinalIgnoreCase);

            var edgeBrush = isHypothesisOrConflict
                ? new SolidColorBrush(Color.FromRgb(229, 158, 95))
                : new SolidColorBrush(Color.FromRgb(62, 132, 91));

            var line = new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                Stroke = edgeBrush,
                StrokeThickness = 1.7,
                Opacity = 0.88
            };

            if (isHypothesisOrConflict)
            {
                line.StrokeDashArray = new DoubleCollection { 5, 4 };
            }

            GraphCanvas.Children.Add(line);
            edgeLabels.Add((new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2),
                connection.RelationshipType, edgeBrush));
        }

        // Nodes are derived from real records; the layout is stable by record order.
        foreach (var node in _nodes)
        {
            var center = positions[node.Key];
            var card = new Border
            {
                Width = NodeWidth,
                Height = NodeHeight,
                Background = GetNodeBackground(node.Type),
                BorderBrush = GetNodeAccent(node.Type),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(11, 8, 11, 8),
                Cursor = Cursors.Hand,
                ToolTip = node.DisplayText
            };
            card.MouseLeftButtonUp += (_, args) =>
            {
                args.Handled = true;
                SelectedNodeTitle.Text = node.Title;
                SelectedNodeDetails.Text = $"{node.Type.ToUpperInvariant()}  /  RECORD ID {node.Id}\n{node.Subtitle}";
            };

            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new TextBlock
            {
                Text = $"{node.Type.ToUpperInvariant()}  /  ID {node.Id}",
                Foreground = GetNodeAccent(node.Type),
                FontSize = 8
            });
            content.Children.Add(new TextBlock
            {
                Text = node.Title,
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34,
                Margin = new Thickness(0, 6, 0, 0)
            });
            card.Child = content;

            Canvas.SetLeft(card, center.X - NodeWidth / 2);
            Canvas.SetTop(card, center.Y - NodeHeight / 2);
            GraphCanvas.Children.Add(card);
        }

        // Small labels are placed last so that relationship types stay readable.
        foreach (var label in edgeLabels)
        {
            var text = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 17, 12)),
                BorderBrush = label.Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Child = new TextBlock
                {
                    Text = label.Text.ToUpperInvariant(),
                    Foreground = label.Brush,
                    FontSize = 7,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                MaxWidth = 105
            };
            Canvas.SetLeft(text, label.Point.X - 50);
            Canvas.SetTop(text, label.Point.Y - 10);
            GraphCanvas.Children.Add(text);
        }
    }

    private static double RectangleEdgeScale(double nx, double ny, double halfWidth, double halfHeight)
    {
        var scaleX = Math.Abs(nx) < 0.0001 ? double.PositiveInfinity : halfWidth / Math.Abs(nx);
        var scaleY = Math.Abs(ny) < 0.0001 ? double.PositiveInfinity : halfHeight / Math.Abs(ny);
        return Math.Min(scaleX, scaleY);
    }

    private Brush GetNodeAccent(string type) => type switch
    {
        "Case" => (Brush)FindResource("NeonBrush"),
        "Evidence" => (Brush)FindResource("CyanBrush"),
        "Experiment" => new SolidColorBrush(Color.FromRgb(216, 233, 138)),
        "Observation" => new SolidColorBrush(Color.FromRgb(201, 167, 255)),
        _ => (Brush)FindResource("MutedBrush")
    };

    private Brush GetNodeBackground(string type) => type switch
    {
        "Case" => new SolidColorBrush(Color.FromRgb(19, 36, 26)),
        "Evidence" => new SolidColorBrush(Color.FromRgb(19, 32, 32)),
        "Experiment" => new SolidColorBrush(Color.FromRgb(23, 27, 22)),
        "Observation" => new SolidColorBrush(Color.FromRgb(21, 26, 37)),
        _ => (Brush)FindResource("PanelAltBrush")
    };

    private void NewRelationship_Click(object sender, RoutedEventArgs e)
    {
        if (_nodes.Count < 2)
        {
            CreateConnectionStatus.Text = "Create at least two case, evidence, experiment, or observation records first.";
            CreateConnectionPanel.Visibility = Visibility.Visible;
            return;
        }

        SourceRecordComboBox.ItemsSource = _nodes;
        TargetRecordComboBox.ItemsSource = _nodes;
        SourceRecordComboBox.SelectedIndex = 0;
        TargetRecordComboBox.SelectedIndex = 1;
        RelationshipTypeComboBox.SelectedIndex = 0;
        RelationshipNotesTextBox.Clear();
        ConfidenceSlider.Value = 0.8;
        CreateConnectionStatus.Text = "A connection records an investigator's assessment; it does not prove a conclusion.";
        CreateConnectionPanel.Visibility = Visibility.Visible;
    }

    private void CancelRelationship_Click(object sender, RoutedEventArgs e)
    {
        CreateConnectionPanel.Visibility = Visibility.Collapsed;
    }

    private void ConfidenceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ConfidenceValueText is not null)
        {
            ConfidenceValueText.Text = $"{ConfidenceSlider.Value:P0}";
        }
    }

    private void SaveRelationship_Click(object sender, RoutedEventArgs e)
    {
        if (SourceRecordComboBox.SelectedItem is not NetworkNodeView source ||
            TargetRecordComboBox.SelectedItem is not NetworkNodeView target)
        {
            CreateConnectionStatus.Text = "Select both the source record and the target record.";
            return;
        }

        if (source.Key == target.Key)
        {
            CreateConnectionStatus.Text = "A record cannot be linked to itself. Choose two different records.";
            return;
        }

        try
        {
            var relationItem = RelationshipTypeComboBox.SelectedItem as ComboBoxItem;
            var relationType = relationItem?.Content?.ToString() ?? "Related to";

            using var db = new GhostDbContext();
            var relationship = new EvidenceRelationship
            {
                SourceType = source.Type,
                SourceId = source.Id,
                TargetType = target.Type,
                TargetId = target.Id,
                RelationshipType = relationType,
                Description = RelationshipNotesTextBox.Text.Trim(),
                Confidence = ConfidenceSlider.Value
            };

            db.Relationships.Add(relationship);
            db.SaveChanges();

            db.AuditLogs.Add(new AuditEntry
            {
                EntityType = "Relationship",
                EntityId = relationship.Id,
                Action = "Created",
                Details = $"{relationship.RelationshipType}: {source.DisplayText} -> {target.DisplayText}"
            });
            db.SaveChanges();

            CreateConnectionPanel.Visibility = Visibility.Collapsed;
            RefreshNetwork();
            RefreshStats();
        }
        catch (Exception ex)
        {
            CreateConnectionStatus.Text = $"Could not save this connection: {ex.Message}";
        }
    }

    private void ConnectionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DeleteConnectionButton.IsEnabled = ConnectionsList.SelectedItem is NetworkConnectionView;

        if (ConnectionsList.SelectedItem is NetworkConnectionView connection)
        {
            SelectedNodeTitle.Text = connection.RelationshipType.ToUpperInvariant();
            SelectedNodeDetails.Text =
                $"{connection.SourceLabel}\n↓\n{connection.TargetLabel}\n\n" +
                $"Confidence: {connection.Confidence:P0}\n" +
                (string.IsNullOrWhiteSpace(connection.Description)
                    ? "No notes recorded."
                    : connection.Description);
        }
    }

    private void DeleteConnection_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionsList.SelectedItem is not NetworkConnectionView selected)
        {
            return;
        }

        var confirmation = MessageBox.Show(this,
            $"Delete this saved connection?\n\n{selected.SourceLabel}\n→ {selected.TargetLabel}",
            "GHOST // Confirm deletion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            using var db = new GhostDbContext();
            var relationship = db.Relationships.FirstOrDefault(x => x.Id == selected.Id);
            if (relationship is null)
            {
                RefreshNetwork();
                return;
            }

            db.Relationships.Remove(relationship);
            db.SaveChanges();

            db.AuditLogs.Add(new AuditEntry
            {
                EntityType = "Relationship",
                EntityId = selected.Id,
                Action = "Deleted",
                Details = $"Deleted connection: {selected.SourceLabel} -> {selected.TargetLabel}"
            });
            db.SaveChanges();

            RefreshNetwork();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Could not delete the connection.\n\n{ex.Message}",
                "GHOST // Delete error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

public sealed record NetworkNodeView(string Type, int Id, string Title, string Subtitle)
{
    public string Key => $"{Type}:{Id}";
    public string DisplayText => $"{Type.ToUpperInvariant()} // ID {Id}  ·  {Title}";
}

public sealed record NetworkConnectionView(
    int Id,
    string RelationshipType,
    string SourceLabel,
    string TargetLabel,
    string SourceType,
    int SourceId,
    string TargetType,
    int TargetId,
    double Confidence,
    string Description,
    DateTime CreatedAt)
{
    public string ConfidenceLabel => $"CONFIDENCE {Confidence:P0}";
    public string DescriptionPreview => string.IsNullOrWhiteSpace(Description)
        ? "NO NOTES ADDED"
        : Description.Trim().Length > 100 ? $"{Description.Trim()[..97]}..." : Description.Trim();
}
