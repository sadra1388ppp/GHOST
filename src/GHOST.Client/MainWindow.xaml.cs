using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Data;
using System.Diagnostics;
using System.Text;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.IO;
using IOPath = System.IO.Path;
using Microsoft.Win32;
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
    private NetworkNodeView? _selectedNetworkNode;
    private ulong? _previousCpuIdleTime;
    private ulong? _previousCpuTotalTime;
    private bool _cpuCounterAvailable;
    private int _systemMonitorTicks;
    private bool _systemDriveInitialized;
    private const int SparklineSampleLimit = 48;
    private readonly List<double> _cpuUsageHistory = [];
    private readonly List<double> _memoryUsageHistory = [];
    private readonly List<double> _diskUsageHistory = [];
    private const string DashboardPowerShellPromptToken = "__GHOST_POWERSHELL_PROMPT__";
    private Process? _dashboardPowerShellProcess;
    private readonly List<string> _dashboardConsoleHistory = [];
    private int _dashboardConsoleHistoryIndex;
    private int? _selectedCaseId;
    private int? _selectedEvidenceId;
    private double _graphZoom = 1.0;

    public MainWindow()
    {
        InitializeComponent();
        SetActiveNavigation("Dashboard");

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

        _clockTimer.Tick += (_, _) => RefreshSystemMonitor();
        _clockTimer.Start();

        Loaded += (_, _) =>
        {
            RefreshStats();
            RefreshNetwork();
            RefreshSystemMonitor(forceDiskRefresh: true);
            InitializeDashboardConsole();
        };
        Closed += (_, _) =>
        {
            _clockTimer.Stop();
            StopDashboardPowerShell();
        };
    }

    private void ShowModuleView(UIElement content, string title)
    {
        SetActiveNavigation(title switch
        {
            "CASE FILES" or "NEW CASE" => "Cases",
            "DIGITAL LAB" => "DigitalLab",
            "FIELD LAB" => "FieldLab",
            "EVIDENCE VAULT" => "EvidenceVault",
            "GHOST TERMINAL" => "Terminal",
            _ => "Dashboard"
        });

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

    private void SetActiveNavigation(string active)
    {
        var items = new (string Key, Button Control)[]
        {
            ("Dashboard", NavDashboard),
            ("Cases", NavCases),
            ("DigitalLab", NavDigitalLab),
            ("FieldLab", NavFieldLab),
            ("EvidenceVault", NavEvidenceVault),
            ("Network", NavNetwork),
            ("Terminal", NavTerminal)
        };

        foreach (var item in items)
        {
            var selected = item.Key == active;
            item.Control.Background = selected
                ? new SolidColorBrush(Color.FromRgb(19, 51, 31))
                : Brushes.Transparent;
            item.Control.BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(70, 150, 95))
                : Brushes.Transparent;
            item.Control.Foreground = selected
                ? (Brush)FindResource("NeonBrush")
                : (Brush)FindResource("MutedBrush");
            item.Control.FontWeight = selected ? FontWeights.Bold : FontWeights.Medium;
            item.Control.Effect = selected
                ? new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(64, 255, 132),
                    BlurRadius = 11,
                    ShadowDepth = 0,
                    Opacity = 0.12
                }
                : null;
        }
    }

    private void InitializeDashboardConsole()
    {
        DashboardConsoleOutputPanel.Children.Clear();
        AddDashboardConsoleLine("GHOST SHELL  /  NATIVE POWERSHELL", Color.FromRgb(99, 255, 160), true);
        AddDashboardConsoleLine("Full PowerShell commands run with the permissions of your Windows account.",
            Color.FromRgb(245, 196, 106));
        AddDashboardConsoleLine("Use 'help' in PowerShell for command help, or type any normal command below.",
            Color.FromRgb(136, 171, 148));
        AddDashboardConsoleLine(string.Empty, Color.FromRgb(99, 255, 160));
        StartDashboardPowerShell();
    }

    private void StartDashboardPowerShell()
    {
        if (_dashboardPowerShellProcess is { HasExited: false })
        {
            DashboardConsoleStatusText.Text = "CONNECTED";
            return;
        }

        StopDashboardPowerShell();

        try
        {
            var executable = ResolvePowerShellExecutable();
            var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
                workingDirectory = Environment.CurrentDirectory;

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NoExit");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(
                $"function global:prompt {{ '{DashboardPowerShellPromptToken}' + (Get-Location).Path + [Environment]::NewLine }}");

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not string line)
                    return;

                if (line.StartsWith(DashboardPowerShellPromptToken, StringComparison.Ordinal))
                {
                    var currentPath = line[DashboardPowerShellPromptToken.Length..].TrimEnd('\r', '\n');
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (ReferenceEquals(_dashboardPowerShellProcess, process))
                            DashboardConsolePromptText.Text = $"PS {currentPath}>";
                    }));
                    return;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ReferenceEquals(_dashboardPowerShellProcess, process))
                        AddDashboardConsoleLine(line, Color.FromRgb(191, 255, 208));
                }));
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not string line)
                    return;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ReferenceEquals(_dashboardPowerShellProcess, process))
                        AddDashboardConsoleLine(line, Color.FromRgb(255, 142, 142));
                }));
            };

            process.Exited += (_, _) =>
            {
                var exitCode = -1;
                try
                {
                    exitCode = process.ExitCode;
                }
                catch (InvalidOperationException)
                {
                    // The app may be shutting down while the process exits.
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!ReferenceEquals(_dashboardPowerShellProcess, process))
                        return;

                    DashboardConsoleStatusText.Text = "STOPPED";
                    DashboardConsoleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 142, 142));
                    AddDashboardConsoleLine(
                        $"PowerShell exited (code {exitCode}). Press RESTART to open a new session.",
                        Color.FromRgb(255, 177, 107), true);
                }));
            };

            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("Windows did not start the PowerShell process.");
            }

            _dashboardPowerShellProcess = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            DashboardConsoleStatusText.Text = IOPath.GetFileNameWithoutExtension(executable).ToUpperInvariant();
            DashboardConsoleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(99, 255, 160));
            DashboardConsolePromptText.Text = $"PS {workingDirectory}>";
            AddDashboardConsoleLine($"Connected: {executable}", Color.FromRgb(99, 255, 160), true);
            AddDashboardConsoleLine($"Working directory: {workingDirectory}", Color.FromRgb(136, 171, 148));
        }
        catch (Exception ex)
        {
            DashboardConsoleStatusText.Text = "START FAILED";
            DashboardConsoleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 142, 142));
            AddDashboardConsoleLine($"Could not start PowerShell: {ex.Message}",
                Color.FromRgb(255, 142, 142), true);
            AddDashboardConsoleLine("Install Windows PowerShell (included with Windows) or PowerShell 7, then press RESTART.",
                Color.FromRgb(136, 171, 148));
        }
    }

    private static string ResolvePowerShellExecutable()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            var powershell7 = IOPath.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
            if (File.Exists(powershell7))
                return powershell7;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(IOPath.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmedDirectory = directory.Trim().Trim('"');
            if (trimmedDirectory.Length == 0)
                continue;

            try
            {
                var candidate = IOPath.Combine(trimmedDirectory, "pwsh.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries and keep searching.
            }
        }

        var windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsFolder))
        {
            var windowsPowerShell = IOPath.Combine(
                windowsFolder, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(windowsPowerShell))
                return windowsPowerShell;
        }

        return "powershell.exe";
    }

    private void DashboardConsoleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteDashboardPowerShellCommand();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && _dashboardConsoleHistory.Count > 0)
        {
            if (_dashboardConsoleHistoryIndex > 0)
                _dashboardConsoleHistoryIndex--;

            DashboardConsoleInput.Text = _dashboardConsoleHistory[_dashboardConsoleHistoryIndex];
            DashboardConsoleInput.CaretIndex = DashboardConsoleInput.Text.Length;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && _dashboardConsoleHistory.Count > 0)
        {
            if (_dashboardConsoleHistoryIndex < _dashboardConsoleHistory.Count - 1)
            {
                _dashboardConsoleHistoryIndex++;
                DashboardConsoleInput.Text = _dashboardConsoleHistory[_dashboardConsoleHistoryIndex];
            }
            else
            {
                _dashboardConsoleHistoryIndex = _dashboardConsoleHistory.Count;
                DashboardConsoleInput.Clear();
            }

            DashboardConsoleInput.CaretIndex = DashboardConsoleInput.Text.Length;
            e.Handled = true;
        }
    }

    private void DashboardConsoleRun_Click(object sender, RoutedEventArgs e)
        => ExecuteDashboardPowerShellCommand();

    private void DashboardConsoleRestart_Click(object sender, RoutedEventArgs e)
    {
        StopDashboardPowerShell();
        DashboardConsoleOutputPanel.Children.Clear();
        _dashboardConsoleHistoryIndex = _dashboardConsoleHistory.Count;
        StartDashboardPowerShell();
    }

    private void ExecuteDashboardPowerShellCommand()
    {
        var command = DashboardConsoleInput.Text.TrimEnd();
        if (string.IsNullOrWhiteSpace(command))
            return;

        DashboardConsoleInput.Clear();
        if (_dashboardConsoleHistory.Count == 0 ||
            !string.Equals(_dashboardConsoleHistory[^1], command, StringComparison.Ordinal))
        {
            _dashboardConsoleHistory.Add(command);
            if (_dashboardConsoleHistory.Count > 500)
                _dashboardConsoleHistory.RemoveAt(0);
        }

        _dashboardConsoleHistoryIndex = _dashboardConsoleHistory.Count;

        if (string.Equals(command.Trim(), "clear", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command.Trim(), "cls", StringComparison.OrdinalIgnoreCase))
        {
            DashboardConsoleOutputPanel.Children.Clear();
            AddDashboardConsoleLine("Console cleared.", Color.FromRgb(136, 171, 148));
            return;
        }

        AddDashboardConsoleLine($"{DashboardConsolePromptText.Text} {command}",
            Color.FromRgb(99, 255, 160), true);

        var process = _dashboardPowerShellProcess;
        if (process is null || process.HasExited)
        {
            AddDashboardConsoleLine("PowerShell is not running. Press RESTART to reconnect.",
                Color.FromRgb(255, 177, 107), true);
            return;
        }

        try
        {
            process.StandardInput.WriteLine(command);
            process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            AddDashboardConsoleLine($"Could not send input to PowerShell: {ex.Message}",
                Color.FromRgb(255, 142, 142), true);
        }
    }

    private void StopDashboardPowerShell()
    {
        var process = _dashboardPowerShellProcess;
        _dashboardPowerShellProcess = null;

        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.StandardInput.WriteLine("exit");
                    process.StandardInput.Flush();
                }
                catch (IOException)
                {
                    // Standard input can close first when PowerShell has already exited.
                }

                if (!process.WaitForExit(500))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(500);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // The process ended during shutdown.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Windows may deny access if the process already ended or cannot be queried.
        }
        finally
        {
            process.Dispose();
        }

        DashboardConsoleStatusText.Text = "STOPPED";
    }

    private void AddDashboardConsoleLine(string text, Color color, bool isBold = false)
    {
        var line = new TextBlock
        {
            Text = text,
            FontFamily = (FontFamily)FindResource("HackerFont"),
            FontSize = 9,
            Foreground = new SolidColorBrush(color),
            FontWeight = isBold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 3)
        };

        DashboardConsoleOutputPanel.Children.Add(line);

        const int maxConsoleLines = 1000;
        while (DashboardConsoleOutputPanel.Children.Count > maxConsoleLines)
            DashboardConsoleOutputPanel.Children.RemoveAt(0);

        DashboardConsoleScrollViewer.ScrollToEnd();
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation("Dashboard");
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
        var recentCases = db.Cases
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(4)
            .ToList();

        RecentCasesList.ItemsSource = recentCases
            .Select(x => new RecentCaseSummary(
                x.Id,
                x.CaseNumber,
                x.Title,
                x.Status,
                x.CreatedAt.ToLocalTime().ToString("dd MMM  HH:mm")))
            .ToList();

        EmptyCasesHint.Visibility = recentCases.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RefreshSystemMonitor(bool forceDiskRefresh = false)
    {
        ClockText.Text = $"LOCAL // {DateTime.Now:HH:mm:ss}";
        SystemHostText.Text = $"{Environment.MachineName}  /  {Environment.ProcessorCount} AVAILABLE CPUs";
        SystemUptimeText.Text = $"SYSTEM UPTIME / {FormatUptime(Environment.TickCount64)}";

        var cpuUsage = ReadCpuUsagePercent();
        var cpuDetail = _cpuCounterAvailable
            ? $"{Environment.ProcessorCount} available logical CPUs"
            : "Windows CPU counters unavailable";
        if (cpuUsage is null && _cpuCounterAvailable)
            cpuDetail = "Sampling CPU activity...";
        UpdateUsageMetric(
            CpuUsageText,
            CpuUsageDetailText,
            cpuUsage,
            cpuDetail,
            Color.FromRgb(99, 255, 160),
            _cpuCounterAvailable ? "--%" : "N/A");
        AddSparklineSample(_cpuUsageHistory, cpuUsage);
        RefreshSparkline(_cpuUsageHistory, CpuUsageSparklineFill, CpuUsageSparklineGlow, CpuUsageSparkline);

        try
        {
            var memory = new MemoryStatusEx
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };

            if (GlobalMemoryStatusEx(ref memory) && memory.TotalPhysicalMemory > 0)
            {
                var usedBytes = memory.TotalPhysicalMemory > memory.AvailablePhysicalMemory
                    ? memory.TotalPhysicalMemory - memory.AvailablePhysicalMemory
                    : 0UL;
                var percentage = usedBytes * 100d / memory.TotalPhysicalMemory;
                UpdateUsageMetric(
                    MemoryUsageText,
                    MemoryUsageDetailText,
                    percentage,
                    $"{FormatGiB(usedBytes)} / {FormatGiB(memory.TotalPhysicalMemory)} used",
                    Color.FromRgb(99, 255, 160),
                    "N/A");
                AddSparklineSample(_memoryUsageHistory, percentage);
                RefreshSparkline(_memoryUsageHistory, MemoryUsageSparklineFill, MemoryUsageSparklineGlow, MemoryUsageSparkline);
            }
            else
            {
                UpdateUsageMetric(
                    MemoryUsageText, MemoryUsageDetailText, null,
                    "Physical memory information unavailable",
                    Color.FromRgb(99, 255, 160), "N/A");
            }
        }
        catch (Exception)
        {
            UpdateUsageMetric(
                MemoryUsageText, MemoryUsageDetailText, null,
                "Physical memory information unavailable",
                Color.FromRgb(99, 255, 160), "N/A");
        }

        _systemMonitorTicks++;
        if (forceDiskRefresh || !_systemDriveInitialized || _systemMonitorTicks % 10 == 0)
            RefreshSystemDriveMetrics();
    }

    private double? ReadCpuUsagePercent()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            _cpuCounterAvailable = false;
            return null;
        }

        _cpuCounterAvailable = true;
        var idle = FileTimeToUInt64(idleTime);
        var total = FileTimeToUInt64(kernelTime) + FileTimeToUInt64(userTime);

        if (_previousCpuIdleTime is ulong previousIdle &&
            _previousCpuTotalTime is ulong previousTotal)
        {
            var totalDelta = total - previousTotal;
            var idleDelta = idle - previousIdle;

            _previousCpuIdleTime = idle;
            _previousCpuTotalTime = total;

            if (totalDelta == 0)
                return null;

            var busyDelta = totalDelta > idleDelta ? totalDelta - idleDelta : 0UL;
            return Math.Clamp(busyDelta * 100d / totalDelta, 0d, 100d);
        }

        // Save the first sample as a baseline; a percentage becomes valid on the next tick.
        _previousCpuIdleTime = idle;
        _previousCpuTotalTime = total;
        return null;
    }

    private void RefreshSystemDriveMetrics()
    {
        try
        {
            var systemRoot = IOPath.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(systemRoot))
                throw new InvalidOperationException("The Windows system drive could not be detected.");

            var drive = new DriveInfo(systemRoot);
            if (!drive.IsReady || drive.TotalSize <= 0)
                throw new IOException("The Windows system drive is not ready.");

            var totalBytes = (ulong)drive.TotalSize;
            // TotalFreeSpace reflects the whole volume; AvailableFreeSpace may be quota-limited.
            var freeBytes = (ulong)Math.Max(0L, drive.TotalFreeSpace);
            var usedBytes = totalBytes > freeBytes ? totalBytes - freeBytes : 0UL;
            var percentage = usedBytes * 100d / totalBytes;
            var driveLabel = systemRoot.TrimEnd('\\');

            UpdateUsageMetric(
                DiskUsageText,
                DiskUsageDetailText,
                percentage,
                $"{driveLabel} {FormatGiB(usedBytes)} / {FormatGiB(totalBytes)} used",
                Color.FromRgb(99, 255, 160),
                "N/A");
            AddSparklineSample(_diskUsageHistory, percentage);
            RefreshSparkline(_diskUsageHistory, DiskUsageSparklineFill, DiskUsageSparklineGlow, DiskUsageSparkline);
        }
        catch (Exception)
        {
            UpdateUsageMetric(
                DiskUsageText, DiskUsageDetailText, null,
                "System drive information unavailable",
                Color.FromRgb(99, 255, 160), "N/A");
        }

        _systemDriveInitialized = true;
    }

    private static void AddSparklineSample(List<double> history, double? sample)
    {
        if (sample is not double value || double.IsNaN(value) || double.IsInfinity(value))
            return;

        history.Add(Math.Clamp(value, 0d, 100d));
        if (history.Count > SparklineSampleLimit)
            history.RemoveAt(0);
    }

    private static void RefreshSparkline(
        IReadOnlyList<double> history,
        System.Windows.Shapes.Path fillPath,
        System.Windows.Shapes.Path glowPath,
        System.Windows.Shapes.Path linePath)
    {
        var line = BuildSparklineGeometry(history, closeToBaseline: false);
        var fill = BuildSparklineGeometry(history, closeToBaseline: true);
        linePath.Data = line;
        glowPath.Data = line;
        fillPath.Data = fill;
    }

    private static Geometry? BuildSparklineGeometry(
        IReadOnlyList<double> history,
        bool closeToBaseline)
    {
        if (history.Count < 2)
            return null;

        // Use the same 0–100 domain as the percentage readout; the curve is real history,
        // not an invented animation or an autoscaled chart that exaggerates small changes.
        const double width = 100d;
        const double height = 34d;
        const double topPadding = 4d;
        const double bottomPadding = 3d;
        var plotHeight = height - topPadding - bottomPadding;
        var points = new Point[history.Count];

        for (var i = 0; i < history.Count; i++)
        {
            var x = width * i / (history.Count - 1d);
            var y = height - bottomPadding - (Math.Clamp(history[i], 0d, 100d) / 100d * plotHeight);
            points[i] = new Point(x, y);
        }

        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = closeToBaseline,
            IsFilled = closeToBaseline
        };

        for (var i = 0; i < points.Length - 1; i++)
        {
            var previous = points[Math.Max(0, i - 1)];
            var current = points[i];
            var next = points[i + 1];
            var following = points[Math.Min(points.Length - 1, i + 2)];

            // Catmull–Rom-style control points create a smooth, curved sparkline.
            var controlOne = new Point(
                current.X + (next.X - previous.X) / 6d,
                current.Y + (next.Y - previous.Y) / 6d);
            var controlTwo = new Point(
                next.X - (following.X - current.X) / 6d,
                next.Y - (following.Y - current.Y) / 6d);

            figure.Segments.Add(new BezierSegment(controlOne, controlTwo, next, true));
        }

        if (closeToBaseline)
        {
            figure.Segments.Add(new LineSegment(new Point(width, height), true));
            figure.Segments.Add(new LineSegment(new Point(0d, height), true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static void UpdateUsageMetric(
        TextBlock valueText,
        TextBlock detailText,
        double? percentage,
        string detail,
        Color normalColor,
        string unavailableLabel)
    {
        detailText.Text = detail;

        if (percentage is not double rawPercentage || double.IsNaN(rawPercentage) ||
            double.IsInfinity(rawPercentage))
        {
            valueText.Text = unavailableLabel;
            var unavailableBrush = new SolidColorBrush(Color.FromRgb(108, 137, 156));
            valueText.Foreground = unavailableBrush;
            return;
        }

        var safePercentage = Math.Clamp(rawPercentage, 0d, 100d);
        var metricColor = safePercentage >= 90d
            ? Color.FromRgb(255, 101, 122)
            : safePercentage >= 75d
                ? Color.FromRgb(245, 196, 106)
                : normalColor;
        var brush = new SolidColorBrush(metricColor);

        valueText.Text = $"{safePercentage:0}%";
        valueText.Foreground = brush;
    }

    private static string FormatGiB(ulong bytes)
        => $"{bytes / (1024d * 1024d * 1024d):0.0} GiB";

    private static string FormatUptime(long milliseconds)
    {
        var uptime = TimeSpan.FromMilliseconds(Math.Max(0L, milliseconds));
        return uptime.TotalDays >= 1d
            ? $"{(int)uptime.TotalDays}d {uptime.Hours:00}h {uptime.Minutes:00}m"
            : $"{uptime.Hours:00}h {uptime.Minutes:00}m";
    }

    private static ulong FileTimeToUInt64(NativeFileTime time)
        => ((ulong)time.HighDateTime << 32) | time.LowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out NativeFileTime idleTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

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
        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is CaseArchiveRow selected)
                OpenCaseWorkspace(selected.Id);
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
        var openCaseButton = new Button
        {
            Content = "OPEN / WORK ON CASE",
            MinWidth = 190,
            Margin = new Thickness(0, 0, 8, 0)
        };
        openCaseButton.Click += (_, _) =>
        {
            if (list.SelectedItem is CaseArchiveRow selected)
                OpenCaseWorkspace(selected.Id);
            else
                MessageBox.Show(this, "Select a case to open its workspace.", "GHOST",
                    MessageBoxButton.OK, MessageBoxImage.Information);
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
                item.UpdatedAt = DateTime.UtcNow;
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
        footer.Children.Add(openCaseButton);
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

    private void RecentCasesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenRecentCaseButton.IsEnabled = RecentCasesList.SelectedItem is RecentCaseSummary;
    }

    private void OpenRecentCase_Click(object sender, RoutedEventArgs e)
    {
        if (RecentCasesList.SelectedItem is RecentCaseSummary selected)
            OpenCaseWorkspace(selected.Id);
    }

    private void RecentCase_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentCasesList.SelectedItem is RecentCaseSummary selected)
            OpenCaseWorkspace(selected.Id);
    }

    private void OpenCaseWorkspace(int caseId)
    {
        CaseFile? caseFile;
        List<EvidenceItem> evidence;
        List<Experiment> experiments;
        List<Observation> observations;
        List<TimelineEvent> timeline;
        List<EvidenceRelationship> allRelationships;
        List<AuditEntry> allAudit;

        using (var db = new GhostDbContext())
        {
            caseFile = db.Cases.AsNoTracking().FirstOrDefault(x => x.Id == caseId);
            if (caseFile is null)
            {
                MessageBox.Show(this, "This case no longer exists in the local database.", "GHOST",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshStats();
                return;
            }

            evidence = db.Evidence.AsNoTracking()
                .Where(x => x.CaseFileId == caseId)
                .OrderByDescending(x => x.CollectedAt)
                .ToList();
            experiments = db.Experiments.AsNoTracking()
                .Where(x => x.CaseFileId == caseId)
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
            var experimentIds = experiments.Select(x => x.Id).ToList();
            observations = db.Observations.AsNoTracking()
                .Where(x => experimentIds.Contains(x.ExperimentId))
                .OrderByDescending(x => x.RecordedAt)
                .ToList();
            timeline = db.TimelineEvents.AsNoTracking()
                .Where(x => x.CaseFileId == caseId)
                .OrderByDescending(x => x.OccurredAt)
                .ToList();
            allRelationships = db.Relationships.AsNoTracking().ToList();
            allAudit = db.AuditLogs.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }

        _selectedCaseId = caseId;
        SetActiveNavigation("Cases");

        var evidenceIds = evidence.Select(x => x.Id).ToHashSet();
        var experimentIdsSet = experiments.Select(x => x.Id).ToHashSet();
        var observationIds = observations.Select(x => x.Id).ToHashSet();
        var timelineIds = timeline.Select(x => x.Id).ToHashSet();

        string RelatedLabel(string type, int id)
        {
            return type switch
            {
                "Case" when id == caseFile.Id => $"{caseFile.CaseNumber} / {caseFile.Title}",
                "Evidence" when evidence.FirstOrDefault(x => x.Id == id) is EvidenceItem evidenceItem => evidenceItem.Title,
                "Experiment" when experiments.FirstOrDefault(x => x.Id == id) is Experiment experimentItem => experimentItem.Title,
                "Observation" when observations.FirstOrDefault(x => x.Id == id) is Observation observationItem => observationItem.Description,
                _ => $"{type.ToUpperInvariant()} #{id}"
            };
        }

        var evidenceRows = evidence.Select(x =>
            $"{(x.IsArchived ? "[ARCHIVED]" : "[ACTIVE]")} {x.Title}  |  {x.EvidenceType}  |  {x.CollectedAt.ToLocalTime():yyyy-MM-dd HH:mm}  |  SHA-256: {(string.IsNullOrWhiteSpace(x.Sha256) ? "not recorded" : x.Sha256)}").ToList();
        var experimentRows = experiments.Select(x =>
            $"[{x.Status.ToUpperInvariant()}] {x.Title}  |  {x.Category}  |  {x.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}\nObjective: {(string.IsNullOrWhiteSpace(x.Hypothesis) ? "Not recorded" : x.Hypothesis)}\nResult: {(string.IsNullOrWhiteSpace(x.Results) ? "Pending" : x.Results)}").ToList();
        experimentRows.AddRange(observations.Select(x =>
            $"[OBSERVATION] {x.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm}  |  {x.Description}" +
            (x.NumericValue.HasValue ? $"  |  {x.NumericValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} {x.Unit}" : string.Empty)));
        var timelineRows = timeline.Select(x =>
            $"{x.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  |  {x.Title}\n{x.Details}").ToList();

        var relatedRelationships = allRelationships
            .Where(x =>
                (x.SourceType == "Case" && x.SourceId == caseId) ||
                (x.TargetType == "Case" && x.TargetId == caseId) ||
                (x.SourceType == "Evidence" && evidenceIds.Contains(x.SourceId)) ||
                (x.TargetType == "Evidence" && evidenceIds.Contains(x.TargetId)) ||
                (x.SourceType == "Experiment" && experimentIdsSet.Contains(x.SourceId)) ||
                (x.TargetType == "Experiment" && experimentIdsSet.Contains(x.TargetId)) ||
                (x.SourceType == "Observation" && observationIds.Contains(x.SourceId)) ||
                (x.TargetType == "Observation" && observationIds.Contains(x.TargetId)))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => $"{x.RelationshipType.ToUpperInvariant()}  |  {RelatedLabel(x.SourceType, x.SourceId)}  →  {RelatedLabel(x.TargetType, x.TargetId)}\nConfidence: {x.Confidence:P0}  |  {x.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}  |  {x.Description}")
            .ToList();

        var auditRows = allAudit
            .Where(x =>
                (x.EntityType == "Case" && x.EntityId == caseId) ||
                (x.EntityType == "Evidence" && evidenceIds.Contains(x.EntityId)) ||
                (x.EntityType == "Experiment" && experimentIdsSet.Contains(x.EntityId)) ||
                (x.EntityType == "Observation" && observationIds.Contains(x.EntityId)) ||
                (x.EntityType == "FieldEntry" && timelineIds.Contains(x.EntityId)))
            .Take(120)
            .Select(x => $"{x.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  |  {x.EntityType.ToUpperInvariant()}  |  {x.Action}\n{x.Details}")
            .ToList();

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var summary = new StackPanel();
        summary.Children.Add(new TextBlock
        {
            Text = $"CASE FILE  /  {caseFile.CaseNumber}",
            FontSize = 9,
            Foreground = (Brush)FindResource("CyanBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        var titleBox = new TextBox
        {
            Text = caseFile.Title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Height = 44,
            Margin = new Thickness(0, 0, 0, 9),
            ToolTip = "Case title"
        };
        summary.Children.Add(titleBox);
        var descriptionBox = new TextBox
        {
            Text = caseFile.Description,
            Height = 74,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Case summary / scope / investigator notes"
        };
        summary.Children.Add(descriptionBox);

        var metadata = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var statusBadge = new TextBlock
        {
            Text = $"STATUS: {caseFile.Status.ToUpperInvariant()}",
            Foreground = string.Equals(caseFile.Status, "Closed", StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush(Color.FromRgb(255, 122, 131))
                : (Brush)FindResource("NeonBrush"),
            FontSize = 9,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        metadata.Children.Add(statusBadge);
        metadata.Children.Add(new TextBlock
        {
            Text = $"CREATED {caseFile.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}  /  LAST UPDATED {caseFile.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        summary.Children.Add(metadata);

        var actions = new WrapPanel();
        var saveButton = new Button { Content = "SAVE CASE DETAILS", MinWidth = 155, Margin = new Thickness(0, 0, 8, 0) };
        var statusButton = new Button { Content = caseFile.Status == "Closed" ? "REOPEN CASE" : "CLOSE CASE", MinWidth = 115, Margin = new Thickness(0, 0, 8, 0) };
        var openDigitalLab = new Button { Content = "OPEN DIGITAL LAB", MinWidth = 145, Margin = new Thickness(0, 0, 8, 0) };
        var openFieldLab = new Button { Content = "OPEN FIELD LAB", MinWidth = 135, Margin = new Thickness(0, 0, 8, 0) };
        var openEvidence = new Button { Content = "VIEW EVIDENCE", MinWidth = 125, Margin = new Thickness(0, 0, 8, 0) };
        var openNetwork = new Button { Content = "VIEW NETWORK", MinWidth = 125, Margin = new Thickness(0, 0, 8, 0) };
        actions.Children.Add(saveButton);
        actions.Children.Add(statusButton);
        actions.Children.Add(openDigitalLab);
        actions.Children.Add(openFieldLab);
        actions.Children.Add(openEvidence);
        actions.Children.Add(openNetwork);
        summary.Children.Add(actions);
        var caseStatusMessage = new TextBlock
        {
            Text = "Changes are saved locally and appear in the audit history.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        summary.Children.Add(caseStatusMessage);

        saveButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(titleBox.Text))
            {
                caseStatusMessage.Text = "A case title is required.";
                return;
            }
            try
            {
                using var db = new GhostDbContext();
                var stored = db.Cases.FirstOrDefault(x => x.Id == caseId);
                if (stored is null)
                {
                    caseStatusMessage.Text = "This case could not be found. Refresh the archive.";
                    return;
                }
                var oldTitle = stored.Title;
                stored.Title = titleBox.Text.Trim();
                stored.Description = descriptionBox.Text.Trim();
                stored.UpdatedAt = DateTime.UtcNow;
                db.AuditLogs.Add(new AuditEntry
                {
                    EntityType = "Case",
                    EntityId = stored.Id,
                    Action = "Updated",
                    Details = $"Case details updated. Title: {oldTitle} → {stored.Title}."
                });
                db.SaveChanges();
                RefreshStats();
                RefreshNetwork();
                caseStatusMessage.Text = "Case details saved and added to the audit history.";
            }
            catch (Exception ex)
            {
                caseStatusMessage.Text = $"Case details could not be saved: {ex.Message}";
            }
        };

        statusButton.Click += (_, _) =>
        {
            try
            {
                using var db = new GhostDbContext();
                var stored = db.Cases.FirstOrDefault(x => x.Id == caseId);
                if (stored is null) return;
                stored.Status = string.Equals(stored.Status, "Closed", StringComparison.OrdinalIgnoreCase) ? "Open" : "Closed";
                stored.UpdatedAt = DateTime.UtcNow;
                db.AuditLogs.Add(new AuditEntry
                {
                    EntityType = "Case",
                    EntityId = stored.Id,
                    Action = stored.Status == "Closed" ? "Closed" : "Reopened",
                    Details = $"Case {stored.CaseNumber} status changed to {stored.Status}."
                });
                db.SaveChanges();
                RefreshStats();
                RefreshNetwork();
                OpenCaseWorkspace(caseId);
            }
            catch (Exception ex)
            {
                caseStatusMessage.Text = $"Case status could not be changed: {ex.Message}";
            }
        };

        openDigitalLab.Click += (_, _) => DigitalLab_Click(this, new RoutedEventArgs());
        openFieldLab.Click += (_, _) => FieldLab_Click(this, new RoutedEventArgs());
        openEvidence.Click += (_, _) => EvidenceVault_Click(this, new RoutedEventArgs());
        openNetwork.Click += (_, _) =>
        {
            Network_Click(this, new RoutedEventArgs());
            _selectedNetworkNode = _nodes.FirstOrDefault(x => x.Type == "Case" && x.Id == caseId);
            if (_selectedNetworkNode is not null)
            {
                SelectedNodeTitle.Text = _selectedNetworkNode.Title;
                SelectedNodeDetails.Text = $"{_selectedNetworkNode.Type.ToUpperInvariant()} / RECORD ID {_selectedNetworkNode.Id}\n{_selectedNetworkNode.Subtitle}";
                OpenSelectedNodeButton.IsEnabled = true;
                DrawGraph();
            }
        };

        var card = CreateModuleCard(summary, new Thickness(0, 0, 0, 14));
        Grid.SetRow(card, 0);
        root.Children.Add(card);

        var metrics = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
        metrics.Children.Add(CreateMetricCard("EVIDENCE", evidence.Count(x => !x.IsArchived).ToString(), "ACTIVE VAULT RECORDS"));
        metrics.Children.Add(CreateMetricCard("EXPERIMENTS", experiments.Count.ToString(), "SAVED PROCEDURES"));
        metrics.Children.Add(CreateMetricCard("OBSERVATIONS", observations.Count.ToString(), "MEASUREMENTS / FINDINGS"));
        metrics.Children.Add(CreateMetricCard("TIMELINE", timeline.Count.ToString(), "CHRONOLOGICAL EVENTS"));
        Grid.SetRow(metrics, 1);
        root.Children.Add(metrics);

        var tabs = new TabControl { Margin = new Thickness(0, 0, 0, 0), MinHeight = 220 };
        tabs.Items.Add(CreateCaseTab("EVIDENCE", evidenceRows, "No evidence has been imported for this case yet."));
        tabs.Items.Add(CreateCaseTab("EXPERIMENTS & OBSERVATIONS", experimentRows, "No experiments or observations have been saved for this case."));
        tabs.Items.Add(CreateCaseTab("TIMELINE", timelineRows, "No timeline events have been recorded for this case."));
        tabs.Items.Add(CreateCaseTab("CONNECTIONS", relatedRelationships, "No saved relationships involve this case or its records."));
        tabs.Items.Add(CreateCaseTab("AUDIT HISTORY", auditRows, "No audit entries are available for this case yet."));
        Grid.SetRow(tabs, 2);
        root.Children.Add(tabs);

        ShowModuleView(root, "CASE FILES");
    }

    private TextBlock CreateFormLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(1, 7, 0, 4)
        };
    }

    private Border CreateModuleCard(UIElement content, Thickness margin = default)
    {
        return new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(16),
            Margin = margin,
            Child = content
        };
    }

    private Border CreateMetricCard(string title, string value, string caption)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, FontSize = 9, Foreground = (Brush)FindResource("MutedBrush") });
        body.Children.Add(new TextBlock { Text = value, FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("NeonBrush"), Margin = new Thickness(0, 5, 0, 2) });
        body.Children.Add(new TextBlock { Text = caption, FontSize = 8, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 10, 0),
            Child = body
        };
    }

    private TabItem CreateCaseTab(string title, List<string> entries, string emptyMessage)
    {
        UIElement content;
        if (entries.Count == 0)
        {
            content = new Border
            {
                Padding = new Thickness(18),
                Child = new TextBlock
                {
                    Text = emptyMessage,
                    Foreground = (Brush)FindResource("MutedBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }
        else
        {
            content = new ListBox
            {
                ItemsSource = entries,
                Background = (Brush)FindResource("PanelBrush"),
                Foreground = (Brush)FindResource("TextBrush"),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(5)
            };
        }

        return new TabItem { Header = title, Content = content, Padding = new Thickness(12, 7, 12, 7) };
    }

    private static string GetGhostDataDirectory() =>
        IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GHOST");

    private static string GetEvidenceFilePath(string relativePath)
    {
        var root = IOPath.GetFullPath(GetGhostDataDirectory());
        var fullPath = IOPath.GetFullPath(IOPath.Combine(root, relativePath ?? string.Empty));
        var relative = IOPath.GetRelativePath(root, fullPath);
        if (relative == "." ||
            relative == ".." ||
            relative.StartsWith(".." + IOPath.DirectorySeparatorChar, StringComparison.Ordinal) ||
            IOPath.IsPathRooted(relative))
        {
            throw new InvalidOperationException("The evidence path points outside the GHOST data folder.");
        }
        return fullPath;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string FormatFileSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = size;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private void Network_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation("Network");
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

            _selectedNetworkNode = null;
            DrawGraph();
            SelectedNodeTitle.Text = "Select a node on the map";
            SelectedNodeDetails.Text = "Click a node to inspect its local record, then open it to continue the investigation.";
            OpenSelectedNodeButton.IsEnabled = false;
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

        var visibleNodes = GetVisibleNetworkNodes();
        const int columns = 4;
        var typeOrder = new[] { "Case", "Evidence", "Experiment", "Observation" };
        var groupedNodes = typeOrder
            .Select(type => visibleNodes.Where(node => node.Type == type).ToList())
            .ToList();
        var rows = Math.Max(1, groupedNodes.Max(group => group.Count));
        GraphCanvas.Width = 28 + columns * ColumnSpacing + 20;
        GraphCanvas.Height = Math.Max(560, 65 + rows * RowSpacing);
        GraphCanvas.LayoutTransform = new ScaleTransform(_graphZoom, _graphZoom);
        EmptyGraphOverlay.Visibility = visibleNodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GraphEmptyTitle.Text = _nodes.Count == 0 ? "NO INVESTIGATION NODES" : "NO NODES MATCH THE FILTER";

        var positions = new Dictionary<string, Point>();
        for (var column = 0; column < groupedNodes.Count; column++)
        {
            for (var row = 0; row < groupedNodes[column].Count; row++)
            {
                var node = groupedNodes[column][row];
                positions[node.Key] = new Point(
                    28 + column * ColumnSpacing + NodeWidth / 2,
                    35 + row * RowSpacing + NodeHeight / 2);
            }
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
        foreach (var node in visibleNodes)
        {
            var center = positions[node.Key];
            var isSelectedNode = _selectedNetworkNode?.Key == node.Key;
            var card = new Border
            {
                Width = NodeWidth,
                Height = NodeHeight,
                Background = GetNodeBackground(node.Type),
                BorderBrush = isSelectedNode
                    ? (Brush)FindResource("NeonBrush")
                    : GetNodeAccent(node.Type),
                BorderThickness = new Thickness(isSelectedNode ? 2.2 : 1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(11, 8, 11, 8),
                Cursor = Cursors.Hand,
                ToolTip = node.DisplayText
            };
            card.MouseLeftButtonUp += (_, args) =>
            {
                args.Handled = true;
                _selectedNetworkNode = node;
                var degree = _connections.Count(connection =>
                    (connection.SourceType == node.Type && connection.SourceId == node.Id) ||
                    (connection.TargetType == node.Type && connection.TargetId == node.Id));
                SelectedNodeTitle.Text = node.Title;
                SelectedNodeDetails.Text = $"{node.Type.ToUpperInvariant()}  /  RECORD ID {node.Id}\n{node.Subtitle}\nCONNECTIONS // {degree}\n\nClick OPEN SELECTED RECORD to work on this item.";
                OpenSelectedNodeButton.IsEnabled = true;
                DrawGraph();
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

    private void OpenSelectedNode_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNetworkNode is not NetworkNodeView node)
            return;

        try
        {
            switch (node.Type)
            {
                case "Case":
                    OpenCaseWorkspace(node.Id);
                    break;

                case "Evidence":
                    using (var db = new GhostDbContext())
                    {
                        var item = db.Evidence.AsNoTracking().FirstOrDefault(x => x.Id == node.Id);
                        if (item is null)
                        {
                            RefreshNetwork();
                            return;
                        }
                        _selectedCaseId = item.CaseFileId;
                        _selectedEvidenceId = item.Id;
                    }
                    EvidenceVault_Click(this, new RoutedEventArgs());
                    break;

                case "Experiment":
                    using (var db = new GhostDbContext())
                    {
                        var item = db.Experiments.AsNoTracking().FirstOrDefault(x => x.Id == node.Id);
                        if (item is null) return;
                        _selectedCaseId = item.CaseFileId;
                    }
                    DigitalLab_Click(this, new RoutedEventArgs());
                    break;

                case "Observation":
                    using (var db = new GhostDbContext())
                    {
                        var item = db.Observations.AsNoTracking().FirstOrDefault(x => x.Id == node.Id);
                        if (item is null) return;
                        _selectedCaseId = db.Experiments.AsNoTracking()
                            .Where(x => x.Id == item.ExperimentId)
                            .Select(x => (int?)x.CaseFileId)
                            .FirstOrDefault();
                    }
                    DigitalLab_Click(this, new RoutedEventArgs());
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the selected record.\n\n{ex.Message}",
                "GHOST // Open record error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<NetworkNodeView> GetVisibleNetworkNodes()
    {
        var selectedType = (GraphTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All record types";
        var normalizedType = selectedType switch
        {
            "Cases" => "Case",
            "Evidence" => "Evidence",
            "Experiments" => "Experiment",
            "Observations" => "Observation",
            _ => string.Empty
        };
        var term = GraphSearchBox.Text?.Trim() ?? string.Empty;
        return _nodes
            .Where(node => string.IsNullOrEmpty(normalizedType) || node.Type == normalizedType)
            .Where(node => string.IsNullOrEmpty(term) ||
                           node.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                           node.Type.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                           node.Id.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
                           node.Subtitle.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void GraphFilter_Changed(object sender, RoutedEventArgs e)
    {
        // XAML can raise SelectionChanged while InitializeComponent is still constructing the graph.
        if (GraphCanvas is null || !IsLoaded)
            return;

        _selectedNetworkNode = null;
        OpenSelectedNodeButton.IsEnabled = false;
        SelectedNodeTitle.Text = "Select a node on the map";
        SelectedNodeDetails.Text = "Filters narrow the visible map. Select a node to inspect and open its record.";
        DrawGraph();
    }

    private void GraphZoomIn_Click(object sender, RoutedEventArgs e) => SetGraphZoom(_graphZoom + 0.15);

    private void GraphZoomOut_Click(object sender, RoutedEventArgs e) => SetGraphZoom(_graphZoom - 0.15);

    private void GraphFit_Click(object sender, RoutedEventArgs e)
    {
        GraphScrollViewer.UpdateLayout();
        var availableWidth = Math.Max(1, GraphScrollViewer.ViewportWidth - 16);
        var availableHeight = Math.Max(1, GraphScrollViewer.ViewportHeight - 16);
        var scale = Math.Min(availableWidth / Math.Max(1, GraphCanvas.Width),
                             availableHeight / Math.Max(1, GraphCanvas.Height));
        SetGraphZoom(Math.Clamp(scale, 0.45, 1.0));
        GraphScrollViewer.ScrollToHorizontalOffset(0);
        GraphScrollViewer.ScrollToVerticalOffset(0);
    }

    private void SetGraphZoom(double zoom)
    {
        _graphZoom = Math.Clamp(zoom, 0.45, 1.8);
        GraphCanvas.LayoutTransform = new ScaleTransform(_graphZoom, _graphZoom);
    }

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
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.92, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.08, GridUnitType.Star) });
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
        Grid.SetRow(header, 0);
        Grid.SetColumnSpan(header, 2);
        root.Children.Add(header);

        var casePicker = new ComboBox
        {
            Height = 34,
            DisplayMemberPath = nameof(FieldCaseOption.DisplayText),
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Select the related case"
        };
        var casePickerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        casePickerPanel.Children.Add(CreateFormLabel("LINKED CASE"));
        casePickerPanel.Children.Add(casePicker);
        Grid.SetRow(casePickerPanel, 1);
        Grid.SetColumnSpan(casePickerPanel, 2);
        root.Children.Add(casePickerPanel);

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
        form.Children.Add(CreateFormLabel("ENTRY TYPE"));
        form.Children.Add(typeBox);
        form.Children.Add(CreateFormLabel("ENTRY TITLE"));
        form.Children.Add(titleBox);
        form.Children.Add(CreateFormLabel("LOCATION / SITE (OPTIONAL)"));
        form.Children.Add(locationBox);
        form.Children.Add(CreateFormLabel("OBSERVATION / METHOD"));
        form.Children.Add(detailsBox);
        form.Children.Add(CreateFormLabel("MEASUREMENT (WHEN APPLICABLE)"));
        form.Children.Add(measurementRow);
        form.Children.Add(saveButton);
        var formCard = CreateModuleCard(form, new Thickness(0, 0, 12, 0));
        Grid.SetRow(formCard, 2);
        Grid.SetColumn(formCard, 0);
        root.Children.Add(formCard);

        var rightPanel = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetRow(rightPanel, 2);
        Grid.SetColumn(rightPanel, 1);
        root.Children.Add(rightPanel);

        var status = new TextBlock
        {
            Text = "Select a case to view and create field entries.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0)
        };
        DockPanel.SetDock(status, Dock.Bottom);
        rightPanel.Children.Add(status);

        var entriesList = new ListView
        {
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = Brushes.White,
            BorderBrush = (Brush)FindResource("LineBrush"),
            FontFamily = (FontFamily)FindResource("HackerFont"),
            DisplayMemberPath = nameof(FieldTimelineRow.DisplayText)
        };
        rightPanel.Children.Add(entriesList);

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
            var fieldCases = db.Cases.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToList()
                .Select(x => new FieldCaseOption(x.Id, x.CaseNumber, x.Title))
                .ToList();
            casePicker.ItemsSource = fieldCases;
            casePicker.SelectedItem = _selectedCaseId.HasValue
                ? fieldCases.FirstOrDefault(x => x.Id == _selectedCaseId.Value)
                : fieldCases.FirstOrDefault();
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
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.92, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.08, GridUnitType.Star) });
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
        Grid.SetRow(header, 0);
        Grid.SetColumnSpan(header, 2);
        root.Children.Add(header);

        var casePicker = new ComboBox
        {
            Height = 34,
            DisplayMemberPath = nameof(LabCaseOption.DisplayText),
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Select the case for this lab"
        };
        var casePickerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        casePickerPanel.Children.Add(CreateFormLabel("LINKED CASE"));
        casePickerPanel.Children.Add(casePicker);
        Grid.SetRow(casePickerPanel, 1);
        Grid.SetColumnSpan(casePickerPanel, 2);
        root.Children.Add(casePickerPanel);

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
        form.Children.Add(CreateFormLabel("EXPERIMENT TITLE"));
        form.Children.Add(titleBox);
        form.Children.Add(CreateFormLabel("ANALYSIS CATEGORY"));
        form.Children.Add(categoryBox);
        form.Children.Add(CreateFormLabel("OBJECTIVE / HYPOTHESIS"));
        form.Children.Add(hypothesisBox);
        form.Children.Add(CreateFormLabel("PROCEDURE / METHOD"));
        form.Children.Add(procedureBox);
        form.Children.Add(createButton);
        var formCard = CreateModuleCard(form, new Thickness(0, 0, 12, 0));
        Grid.SetRow(formCard, 2);
        Grid.SetColumn(formCard, 0);
        root.Children.Add(formCard);

        var rightPanel = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetRow(rightPanel, 2);
        Grid.SetColumn(rightPanel, 1);
        root.Children.Add(rightPanel);

        var observationPanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        observationPanel.Children.Add(new TextBlock
        {
            Text = "OBSERVATION FOR SELECTED EXPERIMENT",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("CyanBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        observationPanel.Children.Add(CreateFormLabel("RESULT / CONCLUSION"));
        var resultBox = new TextBox
        {
            Height = 38,
            ToolTip = "Experiment result / conclusion",
            Margin = new Thickness(0, 0, 0, 7)
        };
        observationPanel.Children.Add(resultBox);
        observationPanel.Children.Add(CreateFormLabel("OBSERVATION DETAILS"));

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
        observationPanel.Children.Add(CreateFormLabel("OPTIONAL MEASUREMENT"));
        actionRow.Children.Add(numericBox);
        actionRow.Children.Add(unitBox);
        actionRow.Children.Add(saveResultButton);
        actionRow.Children.Add(addObservationButton);
        observationPanel.Children.Add(actionRow);
        var observationCard = CreateModuleCard(observationPanel, new Thickness(0, 10, 0, 0));
        DockPanel.SetDock(observationCard, Dock.Bottom);
        rightPanel.Children.Add(observationCard);

        var status = new TextBlock
        {
            Text = "Create or select an experiment to begin.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(status, Dock.Bottom);
        rightPanel.Children.Add(status);

        var experimentList = new ListView
        {
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = Brushes.White,
            BorderBrush = (Brush)FindResource("LineBrush"),
            FontFamily = (FontFamily)FindResource("HackerFont"),
            DisplayMemberPath = nameof(ExperimentArchiveRow.DisplayText)
        };
        rightPanel.Children.Add(experimentList);

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
            var labCases = db.Cases.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToList()
                .Select(x => new LabCaseOption(x.Id, x.CaseNumber, x.Title))
                .ToList();
            casePicker.ItemsSource = labCases;
            casePicker.SelectedItem = _selectedCaseId.HasValue
                ? labCases.FirstOrDefault(x => x.Id == _selectedCaseId.Value)
                : labCases.FirstOrDefault();
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
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "EVIDENCE VAULT // INTEGRITY WORKSPACE",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("NeonBrush")
        });
        header.Children.Add(new TextBlock
        {
            Text = "Import a copy of source files, associate them with a case, and verify stored bytes with SHA-256. Source originals are never moved or modified.",
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var status = new TextBlock
        {
            Text = "Select a case to import evidence. Hash verification detects byte changes; it does not prove authorship or authenticity.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        var toolbar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };
        var casePicker = new ComboBox
        {
            Width = 300,
            Height = 38,
            Margin = new Thickness(0, 0, 10, 0),
            DisplayMemberPath = nameof(VaultCaseOption.DisplayText),
            ToolTip = "Choose a case to filter records or associate imported evidence"
        };
        DockPanel.SetDock(casePicker, Dock.Left);
        toolbar.Children.Add(casePicker);

        var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var importButton = new Button { Content = "+ IMPORT FILES", MinWidth = 130, Margin = new Thickness(0, 0, 7, 0) };
        var verifyButton = new Button { Content = "VERIFY SHA-256", MinWidth = 135, IsEnabled = false, Margin = new Thickness(0, 0, 7, 0) };
        var revealButton = new Button { Content = "REVEAL IN FOLDER", MinWidth = 145, IsEnabled = false, Margin = new Thickness(0, 0, 7, 0) };
        var archiveButton = new Button { Content = "ARCHIVE SELECTED", MinWidth = 145, IsEnabled = false, Margin = new Thickness(0, 0, 7, 0) };
        var openCaseButton = new Button { Content = "OPEN RELATED CASE", MinWidth = 150, IsEnabled = false, Margin = new Thickness(0, 0, 7, 0) };
        actions.Children.Add(importButton);
        actions.Children.Add(verifyButton);
        actions.Children.Add(revealButton);
        actions.Children.Add(archiveButton);
        actions.Children.Add(openCaseButton);
        toolbar.Children.Add(actions);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var search = new TextBox
        {
            Height = 36,
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Search evidence name, type, status, or hash"
        };
        DockPanel.SetDock(search, Dock.Top);
        root.Children.Add(search);

        var list = new ListView
        {
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            FontFamily = (FontFamily)FindResource("HackerFont"),
            SelectionMode = SelectionMode.Single
        };
        var columns = new GridView();
        columns.Columns.Add(new GridViewColumn { Header = "EVIDENCE", DisplayMemberBinding = new Binding(nameof(EvidenceVaultRow.Title)), Width = 210 });
        columns.Columns.Add(new GridViewColumn { Header = "CASE", DisplayMemberBinding = new Binding(nameof(EvidenceVaultRow.CaseLabel)), Width = 170 });
        columns.Columns.Add(new GridViewColumn { Header = "TYPE", DisplayMemberBinding = new Binding(nameof(EvidenceVaultRow.EvidenceType)), Width = 95 });
        columns.Columns.Add(new GridViewColumn { Header = "SIZE", DisplayMemberBinding = new Binding(nameof(EvidenceVaultRow.SizeLabel)), Width = 90 });
        columns.Columns.Add(new GridViewColumn { Header = "IMPORTED", DisplayMemberBinding = new Binding(nameof(EvidenceVaultRow.CollectedAtLabel)), Width = 140 });
        columns.Columns.Add(new GridViewColumn { Header = "STATE", DisplayMemberBinding = new Binding(nameof(EvidenceVaultRow.Status)), Width = 95 });
        columns.Columns.Add(new GridViewColumn { Header = "SHA-256", DisplayMemberBinding = new Binding(nameof(EvidenceVaultRow.HashPreview)), Width = 170 });
        list.View = columns;
        root.Children.Add(list);

        void RefreshRows()
        {
            try
            {
                var filterCaseId = (casePicker.SelectedItem as VaultCaseOption)?.Id ?? 0;
                var term = search.Text.Trim();
                using var db = new GhostDbContext();
                var caseLabels = db.Cases.AsNoTracking()
                    .ToDictionary(x => x.Id, x => $"{x.CaseNumber} / {x.Title}");
                var records = db.Evidence.AsNoTracking()
                    .OrderByDescending(x => x.CollectedAt)
                    .ToList()
                    .Where(x => filterCaseId == 0 || x.CaseFileId == filterCaseId)
                    .Select(x =>
                    {
                        var fullPath = string.Empty;
                        long size = 0;
                        try
                        {
                            fullPath = GetEvidenceFilePath(x.RelativePath);
                            if (File.Exists(fullPath))
                                size = new FileInfo(fullPath).Length;
                        }
                        catch
                        {
                            // An invalid or missing path is surfaced through the status/verification action.
                        }
                        var caseLabel = caseLabels.TryGetValue(x.CaseFileId, out var label)
                            ? label
                            : $"CASE #{x.CaseFileId}";
                        return new EvidenceVaultRow(
                            x.Id,
                            x.CaseFileId,
                            x.Title,
                            caseLabel,
                            string.IsNullOrWhiteSpace(x.EvidenceType) ? "FILE" : x.EvidenceType.ToUpperInvariant(),
                            File.Exists(fullPath) ? FormatFileSize(size) : "MISSING",
                            x.CollectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                            x.IsArchived ? "ARCHIVED" : "ACTIVE",
                            string.IsNullOrWhiteSpace(x.Sha256) ? "NO HASH" : x.Sha256[..Math.Min(12, x.Sha256.Length)].ToUpperInvariant(),
                            x.Sha256,
                            x.RelativePath);
                    })
                    .Where(x => string.IsNullOrWhiteSpace(term) ||
                                x.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                x.CaseLabel.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                x.EvidenceType.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                x.Status.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                x.Sha256.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var priorId = (list.SelectedItem as EvidenceVaultRow)?.Id ?? _selectedEvidenceId;
                list.ItemsSource = records;
                list.SelectedItem = priorId.HasValue
                    ? records.FirstOrDefault(x => x.Id == priorId.Value)
                    : null;
                if (list.SelectedItem is EvidenceVaultRow selected)
                    _selectedEvidenceId = selected.Id;
                else if (_selectedEvidenceId.HasValue && !records.Any(x => x.Id == _selectedEvidenceId.Value))
                    _selectedEvidenceId = null;
                status.Text = records.Count == 0
                    ? "No evidence records match this view. Use IMPORT FILES to add a protected vault copy."
                    : $"{records.Count} evidence record(s) shown. Original files remain untouched; use VERIFY SHA-256 to check the stored copy.";
            }
            catch (Exception ex)
            {
                status.Text = $"Evidence records could not be loaded: {ex.Message}";
            }
        }

        using (var db = new GhostDbContext())
        {
            var cases = db.Cases.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToList()
                .Select(x => new VaultCaseOption(x.Id, x.CaseNumber, x.Title))
                .ToList();
            var options = new List<VaultCaseOption> { new(0, "ALL CASES", "All cases — browse only") };
            options.AddRange(cases);
            casePicker.ItemsSource = options;
            casePicker.SelectedItem = _selectedCaseId.HasValue
                ? options.FirstOrDefault(x => x.Id == _selectedCaseId.Value) ?? options[0]
                : options[0];
        }

        casePicker.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_activeModuleContent, root))
                RefreshRows();
        };
        search.TextChanged += (_, _) =>
        {
            if (ReferenceEquals(_activeModuleContent, root))
                RefreshRows();
        };
        list.SelectionChanged += (_, _) =>
        {
            var selected = list.SelectedItem as EvidenceVaultRow;
            verifyButton.IsEnabled = selected is not null;
            revealButton.IsEnabled = selected is not null;
            archiveButton.IsEnabled = selected is not null;
            openCaseButton.IsEnabled = selected is not null;
            archiveButton.Content = selected?.Status == "ARCHIVED" ? "RESTORE TO ACTIVE" : "ARCHIVE SELECTED";
        };

        importButton.Click += (_, _) =>
        {
            if (casePicker.SelectedItem is not VaultCaseOption selectedCase || selectedCase.Id == 0)
            {
                status.Text = "Choose a specific case before importing files. ALL CASES is browse-only.";
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Import copies into GHOST Evidence Vault",
                Multiselect = true,
                CheckFileExists = true,
                Filter = "All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != true)
                return;

            var imported = 0;
            var lastId = 0;
            var dataRoot = GetGhostDataDirectory();
            var evidenceDirectory = IOPath.Combine(dataRoot, "Evidence");
            Directory.CreateDirectory(evidenceDirectory);

            try
            {
                using var db = new GhostDbContext();
                foreach (var sourcePath in dialog.FileNames)
                {
                    string? copiedPath = null;
                    using var transaction = db.Database.BeginTransaction();
                    try
                    {
                        var extension = IOPath.GetExtension(sourcePath);
                        var storedName = Guid.NewGuid().ToString("N") + extension;
                        copiedPath = IOPath.Combine(evidenceDirectory, storedName);
                        File.Copy(sourcePath, copiedPath, overwrite: false);
                        var hash = ComputeSha256(copiedPath);
                        var evidence = new EvidenceItem
                        {
                            CaseFileId = selectedCase.Id,
                            Title = IOPath.GetFileName(sourcePath),
                            EvidenceType = string.IsNullOrWhiteSpace(extension) ? "File" : extension.TrimStart('.').ToUpperInvariant(),
                            RelativePath = IOPath.Combine("Evidence", storedName),
                            Sha256 = hash,
                            Notes = string.Empty,
                            CollectedAt = DateTime.UtcNow
                        };
                        db.Evidence.Add(evidence);
                        db.SaveChanges();
                        db.AuditLogs.Add(new AuditEntry
                        {
                            EntityType = "Evidence",
                            EntityId = evidence.Id,
                            Action = "Imported",
                            Details = $"Imported a copy of {evidence.Title}; SHA-256 {hash}; source file was not modified."
                        });
                        db.SaveChanges();
                        transaction.Commit();
                        imported++;
                        lastId = evidence.Id;
                    }
                    catch
                    {
                        transaction.Rollback();
                        if (copiedPath is not null && File.Exists(copiedPath))
                        {
                            try { File.Delete(copiedPath); } catch { }
                        }
                        throw;
                    }
                }

                _selectedCaseId = selectedCase.Id;
                _selectedEvidenceId = lastId > 0 ? lastId : null;
                RefreshRows();
                RefreshStats();
                RefreshNetwork();
                status.Text = $"Imported {imported} file copy/copies. SHA-256 recorded; source originals were not moved or changed.";
            }
            catch (Exception ex)
            {
                RefreshRows();
                status.Text = $"Import stopped: {ex.Message}. Any completed imports remain recorded in the vault.";
            }
        };

        verifyButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not EvidenceVaultRow selected)
                return;

            try
            {
                var fullPath = GetEvidenceFilePath(selected.RelativePath);
                var exists = File.Exists(fullPath);
                var actualHash = exists ? ComputeSha256(fullPath) : string.Empty;
                var matched = exists &&
                    string.Equals(actualHash, selected.Sha256, StringComparison.OrdinalIgnoreCase);
                using var db = new GhostDbContext();
                db.AuditLogs.Add(new AuditEntry
                {
                    EntityType = "Evidence",
                    EntityId = selected.Id,
                    Action = !exists ? "Verification Failed" : matched ? "Hash Verified" : "Hash Mismatch",
                    Details = !exists
                        ? $"Verification failed: stored copy is missing for {selected.Title}."
                        : matched
                            ? $"SHA-256 verified for {selected.Title}: {actualHash}."
                            : $"SHA-256 mismatch for {selected.Title}. Expected {selected.Sha256}; actual {actualHash}."
                });
                db.SaveChanges();
                status.Foreground = matched
                    ? (Brush)FindResource("NeonBrush")
                    : (Brush)FindResource("AmberBrush");
                status.Text = !exists
                    ? "MISSING COPY — the vault file could not be found. The original source path is not changed."
                    : matched
                        ? $"HASH MATCH — {selected.Title}. The stored bytes match the recorded SHA-256."
                        : $"HASH MISMATCH — {selected.Title}. The stored copy differs from its recorded SHA-256; review the audit log.";
            }
            catch (Exception ex)
            {
                status.Foreground = (Brush)FindResource("AmberBrush");
                status.Text = $"Hash verification could not complete: {ex.Message}";
            }
        };

        revealButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not EvidenceVaultRow selected)
                return;
            try
            {
                var fullPath = GetEvidenceFilePath(selected.RelativePath);
                if (!File.Exists(fullPath))
                {
                    status.Text = "The stored copy is missing. Run SHA-256 verification to record this state.";
                    return;
                }
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"")
                {
                    UseShellExecute = true
                });
                status.Text = "Selected vault copy in File Explorer. GHOST does not execute evidence files.";
            }
            catch (Exception ex)
            {
                status.Text = $"Could not reveal this vault copy: {ex.Message}";
            }
        };

        archiveButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not EvidenceVaultRow selected)
                return;
            try
            {
                using var db = new GhostDbContext();
                var item = db.Evidence.FirstOrDefault(x => x.Id == selected.Id);
                if (item is null)
                {
                    RefreshRows();
                    return;
                }
                item.IsArchived = !item.IsArchived;
                db.AuditLogs.Add(new AuditEntry
                {
                    EntityType = "Evidence",
                    EntityId = item.Id,
                    Action = item.IsArchived ? "Archived" : "Restored",
                    Details = $"{(item.IsArchived ? "Archived" : "Restored")} evidence record {item.Title}; vault file retained."
                });
                db.SaveChanges();
                RefreshRows();
                RefreshStats();
                RefreshNetwork();
                status.Text = item.IsArchived
                    ? "Evidence archived. The file and metadata remain in the vault."
                    : "Evidence restored to the active vault.";
            }
            catch (Exception ex)
            {
                status.Text = $"Evidence status could not be changed: {ex.Message}";
            }
        };

        openCaseButton.Click += (_, _) =>
        {
            if (list.SelectedItem is EvidenceVaultRow selected)
                OpenCaseWorkspace(selected.CaseFileId);
        };

        ShowModuleView(root, "EVIDENCE VAULT");
        RefreshRows();
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


    private sealed record VaultCaseOption(int Id, string CaseNumber, string Title)
    {
        public string DisplayText => Id == 0 ? $"{CaseNumber} / {Title}" : $"{CaseNumber} / {Title}";
    }

    private sealed record EvidenceVaultRow(
        int Id,
        int CaseFileId,
        string Title,
        string CaseLabel,
        string EvidenceType,
        string SizeLabel,
        string CollectedAtLabel,
        string Status,
        string HashPreview,
        string Sha256,
        string RelativePath);

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
        int Id,
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
