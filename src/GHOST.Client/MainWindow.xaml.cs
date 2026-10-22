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
    private UIElement? _activeModuleContent;

    public MainWindow()
    {
        InitializeComponent();

        // Fit the initial window to the usable desktop area, including common laptop screens.
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(1100, Math.Max(820, workArea.Width * 0.84));
        MinHeight = Math.Min(700, Math.Max(600, workArea.Height * 0.82));
        Width = Math.Min(1440, workArea.Width * 0.94);
        Height = Math.Min(900, workArea.Height * 0.92);
        Width = Math.Max(MinWidth, Width);
        Height = Math.Max(MinHeight, Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);

        _clockTimer.Tick += (_, _) => ClockText.Text = $"LOCAL // {DateTime.Now:HH:mm:ss}";
        _clockTimer.Start();

        Loaded += (_, _) =>
        {
            RefreshStats();
            RefreshNetwork();
        };
        Closed += (_, _) => _clockTimer.Stop();
    }

    private void ShowModuleView(UIElement content, string title)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        NetworkView.Visibility = Visibility.Collapsed;
        ModuleView.Visibility = Visibility.Visible;
        _activeModuleContent = content;

        var shell = new DockPanel();
        var header = new Grid
        {
            Height = 46,
            Margin = new Thickness(0, 0, 0, 14)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("NeonBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(heading, 0);
        header.Children.Add(heading);

        var backButton = new Button
        {
            Content = "← COMMAND CENTER",
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        backButton.Click += (_, _) => Dashboard_Click(this, new RoutedEventArgs());
        Grid.SetColumn(backButton, 1);
        header.Children.Add(backButton);

        DockPanel.SetDock(header, Dock.Top);
        shell.Children.Add(header);
        shell.Children.Add(content);
        ModuleContent.Content = shell;
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        NetworkView.Visibility = Visibility.Collapsed;
        ModuleView.Visibility = Visibility.Collapsed;
        ModuleContent.Content = null;
        _activeModuleContent = null;
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
        ClosedCasesCount.Text = db.Cases.Count(x => x.Status == "Closed").ToString();
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
        cancel.Click += (_, _) => Dashboard_Click(this, new RoutedEventArgs());

        var save = new Button { Content = "SAVE CASE", Width = 110 };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(title.Text))
            {
                MessageBox.Show(
                    this,
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

                Dashboard_Click(this, new RoutedEventArgs());
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
                    this,
                    $"The case could not be saved.\n\n{ex.Message}",
                    "GHOST // Save error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        ShowModuleView(panel, "NEW CASE");
    }

    private void Cases_Click(object sender, RoutedEventArgs e)
    {
        var panel = new DockPanel { Margin = new Thickness(20) };
        var headingPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        headingPanel.Children.Add(new TextBlock
        {
            Text = "CASE FILES // LOCAL ARCHIVE",
            FontSize = 20,
            Foreground = (Brush)FindResource("NeonBrush")
        });
        headingPanel.Children.Add(new TextBlock
        {
            Text = "Search investigations and safely change their status. Cases are retained for audit history.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 5, 0, 0)
        });
        DockPanel.SetDock(headingPanel, Dock.Top);
        panel.Children.Add(headingPanel);

        var search = new TextBox
        {
            Height = 36,
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Search by case number, title, or status"
        };
        DockPanel.SetDock(search, Dock.Top);
        panel.Children.Add(search);

        var list = new ListView
        {
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = Brushes.White,
            BorderBrush = (Brush)FindResource("LineBrush"),
            FontFamily = (FontFamily)FindResource("HackerFont"),
            DisplayMemberPath = nameof(CaseArchiveRow.DisplayText)
        };

        // Wire the search event only after both controls exist, because RefreshRows
        // reads from both search and list.
        search.TextChanged += (_, _) => RefreshRows();

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var statusButton = new Button
        {
            Content = "CLOSE / REOPEN CASE",
            MinWidth = 190,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var close = new Button { Content = "DONE", Width = 92, Margin = new Thickness(0) };
        close.Click += (_, _) => Dashboard_Click(this, new RoutedEventArgs());
        statusButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not CaseArchiveRow selected)
            {
                MessageBox.Show(this, "Select a case first.", "GHOST",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                using var db = new GhostDbContext();
                var item = db.Cases.FirstOrDefault(x => x.Id == selected.Id);
                if (item is null)
                {
                    RefreshRows();
                    return;
                }

                var newStatus = string.Equals(item.Status, "Closed", StringComparison.OrdinalIgnoreCase)
                    ? "Open"
                    : "Closed";
                item.Status = newStatus;
                db.AuditLogs.Add(new AuditEntry
                {
                    EntityType = "Case",
                    EntityId = item.Id,
                    Action = newStatus == "Closed" ? "Closed" : "Reopened",
                    Details = $"Case {item.CaseNumber} status changed to {newStatus}."
                });
                db.SaveChanges();
                RefreshRows();
                RefreshStats();
                RefreshNetwork();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"The case status could not be changed.\n\n{ex.Message}",
                    "GHOST // Update error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        footer.Children.Add(statusButton);
        footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);


        panel.Children.Add(list);

        void RefreshRows()
        {
            try
            {
                var term = search.Text.Trim();
                using var db = new GhostDbContext();
                var records = db.Cases
                    .AsNoTracking()
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList()
                    .Select(x => new CaseArchiveRow(
                        x.Id,
                        x.CaseNumber,
                        x.Title,
                        x.Status,
                        x.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")))
                    .Where(x => string.IsNullOrWhiteSpace(term) ||
                                x.CaseNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                x.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                x.Status.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var previous = (list.SelectedItem as CaseArchiveRow)?.Id;
                list.ItemsSource = records;
                list.SelectedItem = previous.HasValue
                    ? records.FirstOrDefault(x => x.Id == previous.Value)
                    : null;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"The case archive could not be loaded.\n\n{ex.Message}",
                    "GHOST // Archive error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        ShowModuleView(panel, "CASE FILES");
        RefreshRows();
    }

    private void Network_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        ModuleView.Visibility = Visibility.Collapsed;
        ModuleContent.Content = null;
        _activeModuleContent = null;
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

    private void FieldLab_Click(object sender, RoutedEventArgs e)
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "FIELD LAB // NOTES & MEASUREMENTS",
            FontSize = 19,
            Foreground = (Brush)FindResource("NeonBrush")
        });
        header.Children.Add(new TextBlock
        {
            Text = "Record what happened, where it happened, and any measurement. Every entry is timestamped and auditable.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var casePicker = new ComboBox
        {
            Height = 34,
            DisplayMemberPath = nameof(FieldCaseOption.DisplayText),
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Select the related case"
        };
        DockPanel.SetDock(casePicker, Dock.Top);
        root.Children.Add(casePicker);

        var form = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var typeBox = new ComboBox { Height = 34, Margin = new Thickness(0, 0, 0, 7) };
        typeBox.Items.Add("Field Note");
        typeBox.Items.Add("Measurement");
        typeBox.SelectedIndex = 0;
        var titleBox = new TextBox { Height = 34, ToolTip = "Short title", Margin = new Thickness(0, 0, 0, 7) };
        var locationBox = new TextBox { Height = 34, ToolTip = "Optional location or site description", Margin = new Thickness(0, 0, 0, 7) };
        var detailsBox = new TextBox
        {
            Height = 72,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Observation, circumstances, and method",
            Margin = new Thickness(0, 0, 0, 7)
        };
        var measurementRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
        var valueBox = new TextBox { Width = 150, Height = 34, ToolTip = "Optional numeric value", Margin = new Thickness(0, 0, 7, 0) };
        var unitBox = new TextBox { Width = 150, Height = 34, ToolTip = "Measurement unit" };
        measurementRow.Children.Add(valueBox);
        measurementRow.Children.Add(unitBox);
        var saveButton = new Button { Content = "SAVE FIELD ENTRY", Width = 190, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0) };
        form.Children.Add(typeBox);
        form.Children.Add(titleBox);
        form.Children.Add(locationBox);
        form.Children.Add(detailsBox);
        form.Children.Add(measurementRow);
        form.Children.Add(saveButton);
        DockPanel.SetDock(form, Dock.Top);
        root.Children.Add(form);

        var status = new TextBlock
        {
            Text = "Select a case to view and create field entries.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0)
        };
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        var entriesList = new ListView
        {
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = Brushes.White,
            BorderBrush = (Brush)FindResource("LineBrush"),
            FontFamily = (FontFamily)FindResource("HackerFont"),
            DisplayMemberPath = nameof(FieldTimelineRow.DisplayText)
        };
        root.Children.Add(entriesList);

        void RefreshEntries()
        {
            try
            {
                if (casePicker.SelectedItem is not FieldCaseOption selectedCase)
                {
                    entriesList.ItemsSource = Array.Empty<FieldTimelineRow>();
                    status.Text = "Create a case first, then select it here.";
                    return;
                }

                using var db = new GhostDbContext();
                var rows = db.TimelineEvents.AsNoTracking()
                    .Where(x => x.CaseFileId == selectedCase.Id)
                    .OrderByDescending(x => x.OccurredAt)
                    .ToList()
                    .Where(x => x.Title.StartsWith("FIELD NOTE:", StringComparison.OrdinalIgnoreCase) ||
                                x.Title.StartsWith("FIELD MEASUREMENT:", StringComparison.OrdinalIgnoreCase))
                    .Select(x => new FieldTimelineRow(
                        x.Id,
                        x.Title,
                        x.Details,
                        x.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")))
                    .ToList();

                entriesList.ItemsSource = rows;
                status.Text = rows.Count == 0
                    ? "No field entries for this case yet. Use the form above to record the first one."
                    : $"Loaded {rows.Count} field entries. New entries are kept in the case timeline.";
            }
            catch (Exception ex)
            {
                status.Text = $"Field entries could not be loaded: {ex.Message}";
            }
        }

        using (var db = new GhostDbContext())
        {
            casePicker.ItemsSource = db.Cases.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToList()
                .Select(x => new FieldCaseOption(x.Id, x.CaseNumber, x.Title))
                .ToList();
            casePicker.SelectedIndex = casePicker.Items.Count > 0 ? 0 : -1;
        }
        casePicker.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_activeModuleContent, root))
                RefreshEntries();
        };
        typeBox.SelectionChanged += (_, _) =>
        {
            var isMeasurement = typeBox.SelectedItem?.ToString() == "Measurement";
            valueBox.IsEnabled = isMeasurement;
            unitBox.IsEnabled = isMeasurement;
        };
        valueBox.IsEnabled = false;
        unitBox.IsEnabled = false;

        saveButton.Click += (_, _) =>
        {
            if (casePicker.SelectedItem is not FieldCaseOption selectedCase)
            {
                status.Text = "Create a case first, then select it here.";
                return;
            }
            if (string.IsNullOrWhiteSpace(titleBox.Text))
            {
                status.Text = "Enter a title for this field entry.";
                titleBox.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(detailsBox.Text))
            {
                status.Text = "Enter a description of the note or measurement.";
                detailsBox.Focus();
                return;
            }

            var isMeasurement = typeBox.SelectedItem?.ToString() == "Measurement";
            double? numericValue = null;
            if (isMeasurement && !string.IsNullOrWhiteSpace(valueBox.Text))
            {
                if (!double.TryParse(valueBox.Text.Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    status.Text = "Enter a valid numeric value, for example 12.5.";
                    valueBox.Focus();
                    return;
                }
                numericValue = parsed;
            }

            var entryTitle = isMeasurement
                ? "FIELD MEASUREMENT: " + titleBox.Text.Trim()
                : "FIELD NOTE: " + titleBox.Text.Trim();
            var entryDetails = detailsBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(locationBox.Text))
                entryDetails += Environment.NewLine + "Location: " + locationBox.Text.Trim();
            if (isMeasurement && numericValue.HasValue)
                entryDetails += Environment.NewLine + "Measurement: " +
                    numericValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    (string.IsNullOrWhiteSpace(unitBox.Text) ? "" : " " + unitBox.Text.Trim());

            try
            {
                using var db = new GhostDbContext();
                using var transaction = db.Database.BeginTransaction();
                var now = DateTime.UtcNow;
                var timelineEvent = new TimelineEvent
                {
                    CaseFileId = selectedCase.Id,
                    Title = entryTitle,
                    Details = entryDetails,
                    OccurredAt = now
                };
                db.TimelineEvents.Add(timelineEvent);
                db.SaveChanges();
                db.AuditLogs.Add(new AuditEntry
                {
                    EntityType = "FieldEntry",
                    EntityId = timelineEvent.Id,
                    Action = "Created",
                    Details = $"{entryTitle}; case {selectedCase.CaseNumber}; recorded at {now:O}"
                });
                db.SaveChanges();
                transaction.Commit();

                titleBox.Clear();
                locationBox.Clear();
                detailsBox.Clear();
                valueBox.Clear();
                unitBox.Clear();
                RefreshEntries();
                status.Text = "Field entry saved to the case timeline with an audit record.";
            }
            catch (Exception ex)
            {
                status.Text = $"Field entry could not be saved: {ex.Message}";
            }
        };

        ShowModuleView(root, "FIELD LAB");
        RefreshEntries();
    }

    private void DigitalLab_Click(object sender, RoutedEventArgs e)
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "DIGITAL LAB // EXPERIMENTS & OBSERVATIONS",
            FontSize = 19,
            Foreground = (Brush)FindResource("NeonBrush")
        });
        header.Children.Add(new TextBlock
        {
            Text = "Save a procedure, results, and observations against a case. All records stay local.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 5, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var casePicker = new ComboBox
        {
            Height = 34,
            DisplayMemberPath = nameof(LabCaseOption.DisplayText),
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Select the case for this lab"
        };
        DockPanel.SetDock(casePicker, Dock.Top);
        root.Children.Add(casePicker);

        var form = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        form.Children.Add(new TextBlock
        {
            Text = "NEW EXPERIMENT",
            FontSize = 9,
            Foreground = (Brush)FindResource("NeonBrush"),
            Margin = new Thickness(0, 0, 0, 7)
        });
        var titleBox = new TextBox
        {
            Height = 34,
            ToolTip = "Experiment title",
            Margin = new Thickness(0, 0, 0, 7)
        };
        var categoryBox = new ComboBox
        {
            Height = 34,
            Margin = new Thickness(0, 0, 0, 7)
        };
        categoryBox.Items.Add("File Analysis");
        categoryBox.Items.Add("Log Analysis");
        categoryBox.Items.Add("Network Analysis");
        categoryBox.Items.Add("Scientific");
        categoryBox.Items.Add("Other");
        categoryBox.SelectedIndex = 0;

        var hypothesisBox = new TextBox
        {
            Height = 45,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Objective or hypothesis",
            Margin = new Thickness(0, 0, 0, 7)
        };
        var procedureBox = new TextBox
        {
            Height = 52,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Procedure or method",
            Margin = new Thickness(0, 0, 0, 7)
        };
        var createButton = new Button
        {
            Content = "+ SAVE EXPERIMENT",
            Width = 190,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0)
        };
        form.Children.Add(titleBox);
        form.Children.Add(categoryBox);
        form.Children.Add(hypothesisBox);
        form.Children.Add(procedureBox);
        form.Children.Add(createButton);
        DockPanel.SetDock(form, Dock.Top);
        root.Children.Add(form);

        var observationPanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        observationPanel.Children.Add(new TextBlock
        {
            Text = "OBSERVATION FOR SELECTED EXPERIMENT",
            FontSize = 9,
            Foreground = (Brush)FindResource("CyanBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        var resultBox = new TextBox
        {
            Height = 38,
            ToolTip = "Experiment result / conclusion",
            Margin = new Thickness(0, 0, 0, 7)
        };
        observationPanel.Children.Add(resultBox);

        var observationBox = new TextBox
        {
            Height = 48,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Observation details",
            Margin = new Thickness(0, 0, 0, 7)
        };
        observationPanel.Children.Add(observationBox);

        var actionRow = new WrapPanel();
        var numericBox = new TextBox
        {
            Width = 120,
            Height = 34,
            ToolTip = "Optional numeric measurement",
            Margin = new Thickness(0, 0, 7, 7)
        };
        var unitBox = new TextBox
        {
            Width = 120,
            Height = 34,
            ToolTip = "Optional measurement unit",
            Margin = new Thickness(0, 0, 7, 7)
        };
        var saveResultButton = new Button
        {
            Content = "SAVE RESULT",
            MinWidth = 120,
            Margin = new Thickness(0, 0, 7, 7)
        };
        var addObservationButton = new Button
        {
            Content = "ADD OBSERVATION",
            MinWidth = 160,
            Margin = new Thickness(0, 0, 7, 7)
        };
        actionRow.Children.Add(numericBox);
        actionRow.Children.Add(unitBox);
        actionRow.Children.Add(saveResultButton);
        actionRow.Children.Add(addObservationButton);
        observationPanel.Children.Add(actionRow);
        DockPanel.SetDock(observationPanel, Dock.Bottom);
        root.Children.Add(observationPanel);

        var status = new TextBlock
        {
            Text = "Create or select an experiment to begin.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        var experimentList = new ListView
        {
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = Brushes.White,
            BorderBrush = (Brush)FindResource("LineBrush"),
            FontFamily = (FontFamily)FindResource("HackerFont"),
            DisplayMemberPath = nameof(ExperimentArchiveRow.DisplayText)
        };
        root.Children.Add(experimentList);

        void RefreshExperiments()
        {
            try
            {
                using var db = new GhostDbContext();
                var selectedCaseId = (casePicker.SelectedItem as LabCaseOption)?.Id;
                var previousExperimentId = (experimentList.SelectedItem as ExperimentArchiveRow)?.Id;
                var rows = selectedCaseId.HasValue
                    ? db.Experiments.AsNoTracking()
                        .Where(x => x.CaseFileId == selectedCaseId.Value)
                        .OrderByDescending(x => x.CreatedAt)
                        .ToList()
                        .Select(x => new ExperimentArchiveRow(
                            x.Id, x.CaseFileId, x.Title, x.Category, x.Status,
                            x.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), x.Results))
                        .ToList()
                    : new List<ExperimentArchiveRow>();

                experimentList.ItemsSource = rows;
                experimentList.SelectedItem = previousExperimentId.HasValue
                    ? rows.FirstOrDefault(x => x.Id == previousExperimentId.Value)
                    : rows.FirstOrDefault();

                status.Text = rows.Count == 0
                    ? "No experiments in this case yet. Use the form above to create one."
                    : $"Loaded {rows.Count} experiment(s). Select a row to save a result or add an observation.";
            }
            catch (Exception ex)
            {
                status.Text = $"Experiments could not be loaded: {ex.Message}";
            }
        }

        using (var db = new GhostDbContext())
        {
            casePicker.ItemsSource = db.Cases.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToList()
                .Select(x => new LabCaseOption(x.Id, x.CaseNumber, x.Title))
                .ToList();
            casePicker.SelectedIndex = casePicker.Items.Count > 0 ? 0 : -1;
        }

        casePicker.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_activeModuleContent, root))
                RefreshExperiments();
        };
        experimentList.SelectionChanged += (_, _) =>
        {
            if (experimentList.SelectedItem is ExperimentArchiveRow selected)
                resultBox.Text = selected.Results;
        };

        createButton.Click += (_, _) =>
        {
            if (casePicker.SelectedItem is not LabCaseOption selectedCase)
            {
                status.Text = "Create a case first, then select it here.";
                return;
            }
            if (string.IsNullOrWhiteSpace(titleBox.Text))
            {
                status.Text = "Enter an experiment title.";
                titleBox.Focus();
                return;
            }

            try
            {
                using var db = new GhostDbContext();
                using var transaction = db.Database.BeginTransaction();
                var now = DateTime.UtcNow;
                var experiment = new Experiment
                {
                    CaseFileId = selectedCase.Id,
                    Title = titleBox.Text.Trim(),
                    Category = categoryBox.SelectedItem?.ToString() ?? "Other",
                    Hypothesis = hypothesisBox.Text.Trim(),
                    Procedure = procedureBox.Text.Trim(),
                    Status = "Draft",
                    CreatedAt = now
                };
                db.Experiments.Add(experiment);
                db.SaveChanges();
                db.AuditLogs.Add(new AuditEntry
                {
                    EntityType = "Experiment",
                    EntityId = experiment.Id,
                    Action = "Created",
                    Details = $"Experiment created: {experiment.Title} ({experiment.Category})."
                });
                db.TimelineEvents.Add(new TimelineEvent
                {
                    CaseFileId = selectedCase.Id,
                    Title = "Experiment created: " + experiment.Title,
                    Details = $"Category: {experiment.Category}; status: {experiment.Status}.",
                    OccurredAt = now
                });
                db.SaveChanges();
                transaction.Commit();

                titleBox.Clear();
                hypothesisBox.Clear();
                procedureBox.Clear();
                experimentList.SelectedItem = null;
                RefreshExperiments();
                RefreshStats();
                RefreshNetwork();
                status.Text = "Experiment saved. Select it below to add a result or observation.";
            }
            catch (Exception ex)
            {
                status.Text = $"Experiment could not be saved: {ex.Message}";
            }
        };

        saveResultButton.Click += (_, _) =>
        {
            if (experimentList.SelectedItem is not ExperimentArchiveRow selected)
            {
                status.Text = "Select an experiment first.";
                return;
            }

            try
            {
                using var db = new GhostDbContext();
                using var transaction = db.Database.BeginTransaction();
                var experiment = db.Experiments.FirstOrDefault(x => x.Id == selected.Id);
                if (experiment is null)
                {
                    RefreshExperiments();
                    return;
                }

                experiment.Results = resultBox.Text.Trim();
                experiment.Status = string.IsNullOrWhiteSpace(experiment.Results) ? "Draft" : "Completed";
                db.AuditLogs.Add(new AuditEntry
                {
                    EntityType = "Experiment",
                    EntityId = experiment.Id,
                    Action = "Result Updated",
                    Details = $"Result updated for experiment {experiment.Title}."
                });
                db.TimelineEvents.Add(new TimelineEvent
                {
                    CaseFileId = experiment.CaseFileId,
                    Title = "Experiment result updated: " + experiment.Title,
                    Details = string.IsNullOrWhiteSpace(experiment.Results) ? "Result cleared." : experiment.Results,
                    OccurredAt = DateTime.UtcNow
                });
                db.SaveChanges();
                transaction.Commit();

                RefreshExperiments();
                RefreshStats();
                RefreshNetwork();
                status.Text = "Result saved; timeline and audit history updated.";
            }
            catch (Exception ex)
            {
                status.Text = $"Experiment result could not be saved: {ex.Message}";
            }
        };

        addObservationButton.Click += (_, _) =>
        {
            if (experimentList.SelectedItem is not ExperimentArchiveRow selected)
            {
                status.Text = "Select an experiment before adding an observation.";
                return;
            }
            if (string.IsNullOrWhiteSpace(observationBox.Text))
            {
                status.Text = "Enter an observation description.";
                observationBox.Focus();
                return;
            }

            double? numericValue = null;
            if (!string.IsNullOrWhiteSpace(numericBox.Text))
            {
                if (!double.TryParse(numericBox.Text.Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    status.Text = "Enter a valid numeric value, for example 12.5.";
                    numericBox.Focus();
                    return;
                }
                numericValue = parsed;
            }

            try
            {
                using var db = new GhostDbContext();
                using var transaction = db.Database.BeginTransaction();
                var experiment = db.Experiments.FirstOrDefault(x => x.Id == selected.Id);
                if (experiment is null)
                {
                    RefreshExperiments();
                    return;
                }

                var now = DateTime.UtcNow;
                var observation = new Observation
                {
                    ExperimentId = experiment.Id,
                    Description = observationBox.Text.Trim(),
                    NumericValue = numericValue,
                    Unit = unitBox.Text.Trim(),
                    RecordedAt = now
                };
                db.Observations.Add(observation);
                db.SaveChanges();
                db.AuditLogs.Add(new AuditEntry
                {
                    EntityType = "Observation",
                    EntityId = observation.Id,
                    Action = "Created",
                    Details = $"Observation recorded for experiment {experiment.Title}."
                });
                db.TimelineEvents.Add(new TimelineEvent
                {
                    CaseFileId = experiment.CaseFileId,
                    Title = "Observation recorded: " + experiment.Title,
                    Details = observation.Description + (numericValue.HasValue
                        ? $" | Value: {numericValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} {observation.Unit}"
                        : string.Empty),
                    OccurredAt = now
                });
                db.SaveChanges();
                transaction.Commit();

                observationBox.Clear();
                numericBox.Clear();
                unitBox.Clear();
                RefreshExperiments();
                RefreshStats();
                RefreshNetwork();
                status.Text = "Observation saved; timeline and audit history updated.";
            }
            catch (Exception ex)
            {
                status.Text = $"Observation could not be saved: {ex.Message}";
            }
        };

        ShowModuleView(root, "DIGITAL LAB");
        RefreshExperiments();
    }

    private void EvidenceVault_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock
        {
            Text = "EVIDENCE VAULT // LOCAL ARCHIVE",
            FontSize = 16,
            Foreground = (Brush)FindResource("NeonBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "This workspace stays inside the main GHOST window. The file-import workflow is the next module to complete.",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Planned: copy originals into the local vault, calculate SHA-256, verify stored copies, record provenance, and archive entries without deleting source files.",
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 760
        });
        ShowModuleView(panel, "EVIDENCE VAULT");
    }


    private void Terminal_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock
        {
            Text = "LOCAL CONSOLE // NOT ACTIVE",
            FontSize = 16,
            Foreground = (Brush)FindResource("NeonBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "The terminal module is not implemented yet. This view does not execute system shell commands.",
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        ShowModuleView(panel, "GHOST TERMINAL");
    }


    private void ComingSoon_Click(object sender, RoutedEventArgs e)
    {
        var title = (sender as Button)?.Content?.ToString() ?? "WORKSPACE MODULE";
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock
        {
            Text = "MODULE NOT IMPLEMENTED YET",
            FontSize = 16,
            Foreground = (Brush)FindResource("AmberBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "This feature is planned for a later milestone. Navigation stays in the current GHOST window; no extra page or window is opened.",
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 720
        });
        ShowModuleView(panel, title);
    }


    private sealed record FieldCaseOption(int Id, string CaseNumber, string Title)
    {
        public string DisplayText => $"{CaseNumber} / {Title}";
    }

    private sealed record FieldTimelineRow(int Id, string Title, string Details, string OccurredAt)
    {
        public string DisplayText =>
            $"{OccurredAt} | {Title} | {(Details.Length > 105 ? Details[..102] + "..." : Details)}";
    }

    private sealed record LabCaseOption(int Id, string CaseNumber, string Title)
    {
        public string DisplayText => $"{CaseNumber} / {Title}";
    }

    private sealed record ExperimentArchiveRow(
        int Id,
        int CaseFileId,
        string Title,
        string Category,
        string Status,
        string CreatedAt,
        string Results)
    {
        public string DisplayText =>
            $"{Status.ToUpperInvariant(),-10} | {Category,-16} | {Title} | {CreatedAt}";
    }

    private sealed record RecentCaseSummary(
        string CaseNumber,
        string Title,
        string Status,
        string CreatedAt);

    private sealed record CaseArchiveRow(
        int Id,
        string CaseNumber,
        string Title,
        string Status,
        string CreatedAt)
    {
        public string DisplayText =>
            $"{CaseNumber}    |    {Status.ToUpperInvariant(),-6}    |    {Title}    |    {CreatedAt}";
    }
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
