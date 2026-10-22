using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GHOST.Infrastructure.Persistence;
using GHOST.Models;
using Microsoft.EntityFrameworkCore;

namespace GHOST;

public partial class InvestigationNetworkWindow : Window
{
    private const double NodeWidth = 220;
    private const double NodeHeight = 84;
    private const double ColumnSpacing = 265;
    private const double RowSpacing = 145;

    private List<NetworkNodeView> _nodes = [];
    private List<NetworkConnectionView> _connections = [];

    public InvestigationNetworkWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshNetwork();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshNetwork();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RefreshNetwork()
    {
        try
        {
            using var db = new GhostDbContext();

            _nodes =
            [
                .. db.Cases.AsNoTracking()
                    .OrderBy(x => x.CreatedAt)
                    .Select(x => new NetworkNodeView("Case", x.Id, x.CaseNumber,
                        $"{x.Status.ToUpperInvariant()}  /  {x.Title}"))
                    .ToList(),
                .. db.Evidence.AsNoTracking()
                    .Where(x => !x.IsArchived)
                    .OrderBy(x => x.CollectedAt)
                    .Select(x => new NetworkNodeView("Evidence", x.Id, x.Title,
                        $"{x.EvidenceType.ToUpperInvariant()}  /  ID {x.Id}"))
                    .ToList(),
                .. db.Experiments.AsNoTracking()
                    .OrderBy(x => x.CreatedAt)
                    .Select(x => new NetworkNodeView("Experiment", x.Id, x.Title,
                        $"{x.Category.ToUpperInvariant()}  /  {x.Status.ToUpperInvariant()}"))
                    .ToList(),
                .. db.Observations.AsNoTracking()
                    .OrderBy(x => x.RecordedAt)
                    .Select(x => new NetworkNodeView("Observation", x.Id, x.Description,
                        $"RECORDED  /  ID {x.Id}"))
                    .ToList()
            ];

            var nodeLookup = _nodes.ToDictionary(x => x.Key, x => x);
            _connections = db.Relationships.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new RelationshipRecord(
                    x.Id, x.SourceType, x.SourceId, x.TargetType, x.TargetId,
                    x.RelationshipType, x.Description, x.Confidence, x.CreatedAt))
                .ToList()
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
                Padding = new Thickness(4, 2),
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
            MessageBox.Show(this,
                "Create at least two case, evidence, experiment, or observation records before linking them.",
                "GHOST // Network",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new Window
        {
            Title = "GHOST // Create Connection",
            Width = 600,
            Height = 620,
            MinWidth = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (Brush)FindResource("BgBrush"),
            Foreground = Brushes.White,
            FontFamily = (FontFamily)FindResource("HackerFont"),
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = "CREATE CONNECTION",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("NeonBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Link two saved records. A connection is an investigator-created assertion, not automatic proof.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        AddFieldLabel(panel, "SOURCE RECORD");
        var sourceBox = CreateNodeComboBox();
        sourceBox.ItemsSource = _nodes;
        sourceBox.SelectedIndex = 0;
        panel.Children.Add(sourceBox);

        AddFieldLabel(panel, "TARGET RECORD");
        var targetBox = CreateNodeComboBox();
        targetBox.ItemsSource = _nodes;
        targetBox.SelectedIndex = 1;
        panel.Children.Add(targetBox);

        AddFieldLabel(panel, "RELATIONSHIP TYPE");
        var relationBox = new ComboBox
        {
            Height = 36,
            ItemsSource = new[]
            {
                "Related to",
                "Supports",
                "Contradicts",
                "Derived from",
                "Part of",
                "Same source",
                "Observed with",
                "Hypothesis"
            },
            SelectedIndex = 0
        };
        panel.Children.Add(relationBox);

        AddFieldLabel(panel, "NOTES / SOURCE / REASONING");
        var descriptionBox = new TextBox
        {
            Height = 92,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        panel.Children.Add(descriptionBox);

        var confidenceHeader = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        confidenceHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        confidenceHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        confidenceHeader.Children.Add(new TextBlock
        {
            Text = "CONFIDENCE (INVESTIGATOR ESTIMATE)",
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center
        });
        var confidenceLabel = new TextBlock
        {
            Text = "80%",
            Foreground = (Brush)FindResource("CyanBrush"),
            FontSize = 10
        };
        Grid.SetColumn(confidenceLabel, 1);
        confidenceHeader.Children.Add(confidenceLabel);
        panel.Children.Add(confidenceHeader);

        var confidenceSlider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0.8,
            TickFrequency = 0.05,
            SmallChange = 0.05,
            LargeChange = 0.1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 4, 0, 0)
        };
        confidenceSlider.ValueChanged += (_, _) =>
            confidenceLabel.Text = $"{confidenceSlider.Value:P0}";
        panel.Children.Add(confidenceSlider);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };

        var cancelButton = new Button
        {
            Content = "CANCEL",
            Width = 100,
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        var saveButton = new Button { Content = "SAVE CONNECTION", Width = 170 };
        saveButton.Click += (_, _) =>
        {
            if (sourceBox.SelectedItem is not NetworkNodeView source ||
                targetBox.SelectedItem is not NetworkNodeView target)
            {
                MessageBox.Show(dialog, "Select both records.", "GHOST",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (source.Key == target.Key)
            {
                MessageBox.Show(dialog, "A record cannot be linked to itself.", "GHOST",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                using var db = new GhostDbContext();
                var relationship = new EvidenceRelationship
                {
                    SourceType = source.Type,
                    SourceId = source.Id,
                    TargetType = target.Type,
                    TargetId = target.Id,
                    RelationshipType = relationBox.SelectedItem?.ToString() ?? "Related to",
                    Description = descriptionBox.Text.Trim(),
                    Confidence = confidenceSlider.Value
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

                dialog.DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(dialog,
                    $"Could not save this connection.\n\n{ex.Message}",
                    "GHOST // Save error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        panel.Children.Add(buttons);

        dialog.Content = panel;

        if (dialog.ShowDialog() == true)
        {
            RefreshNetwork();
        }
    }

    private static ComboBox CreateNodeComboBox()
    {
        return new ComboBox
        {
            Height = 36,
            DisplayMemberPath = nameof(NetworkNodeView.DisplayText),
            FontSize = 10,
            Margin = new Thickness(0, 5, 0, 12)
        };
    }

    private static void AddFieldLabel(Panel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 9,
            Foreground = (Brush)Application.Current.MainWindow!.FindResource("MutedBrush"),
            Margin = new Thickness(0, 0, 0, 2)
        });
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

    private sealed record RelationshipRecord(
        int Id,
        string SourceType,
        int SourceId,
        string TargetType,
        int TargetId,
        string RelationshipType,
        string Description,
        double Confidence,
        DateTime CreatedAt);
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
