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
using System.Globalization;
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
    private string _dashboardConsoleHistoryDraft = string.Empty;
    private bool _isTerminalModuleActive;
    private StackPanel? _terminalConsoleOutputPanel;
    private ScrollViewer? _terminalConsoleScrollViewer;
    private TextBox? _terminalConsoleInput;
    private TextBlock? _terminalConsolePromptText;
    private TextBlock? _terminalConsoleStatusText;
    private TextBlock? _terminalConsoleHostText;
    private readonly List<ConsoleTranscriptEntry> _consoleTranscript = [];
    private string PowerShellHistoryPath => IOPath.Combine(GetGhostDataDirectory(), "PowerShellHistory.txt");
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
        _isTerminalModuleActive = string.Equals(title, "GHOST TERMINAL", StringComparison.OrdinalIgnoreCase);
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
            Content = "← DASHBOARD",
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
                ? new SolidColorBrush(Color.FromRgb(18, 38, 47))
                : Brushes.Transparent;
            item.Control.BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(54, 112, 124))
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
        ClearConsoleTranscript();
        LoadPowerShellHistory();
        DashboardConsoleHostText.Text =
            $"{Environment.MachineName}  //  LOCAL SESSION  //  {Environment.ProcessorCount} CPU THREADS";
        AddDashboardConsoleLine("GHOST SHELL  /  NATIVE POWERSHELL", Color.FromRgb(99, 255, 160), true);
        AddDashboardConsoleLine("Full PowerShell commands run with the permissions of your Windows account.",
            Color.FromRgb(245, 196, 106));
        AddDashboardConsoleLine("Type 'ghost help' to list all GHOST commands; native PowerShell commands run as typed.",
            Color.FromRgb(136, 171, 148));
        AddDashboardConsoleLine(string.Empty, Color.FromRgb(99, 255, 160));
        StartDashboardPowerShell();
    }

    private void StartDashboardPowerShell()
    {
        if (_dashboardPowerShellProcess is { HasExited: false })
        {
            SetGhostConsoleStatus("CONNECTED");
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
                            SetGhostConsolePrompt($"PS {currentPath}>");
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

                    SetGhostConsoleStatus("STOPPED", Color.FromRgb(255, 142, 142));
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

            SetGhostConsoleStatus(IOPath.GetFileNameWithoutExtension(executable).ToUpperInvariant());
            SetGhostConsolePrompt($"PS {workingDirectory}>");
            AddDashboardConsoleLine($"Connected: {executable}", Color.FromRgb(99, 255, 160), true);
            AddDashboardConsoleLine($"Working directory: {workingDirectory}", Color.FromRgb(136, 171, 148));
        }
        catch (Exception ex)
        {
            SetGhostConsoleStatus("START FAILED", Color.FromRgb(255, 142, 142));
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

    private void DashboardConsoleQuickCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string command ||
            string.IsNullOrWhiteSpace(command))
            return;

        var input = GetCurrentConsoleInput();
        input.Text = command;
        ExecuteDashboardPowerShellCommand(input);
        input.Focus();
    }

    private void DashboardConsoleInput_KeyDown(object sender, KeyEventArgs e)
    {
        var input = sender as TextBox ?? GetCurrentConsoleInput();

        if (e.Key == Key.Enter)
        {
            ExecuteDashboardPowerShellCommand(input);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && _dashboardConsoleHistory.Count > 0)
        {
            if (_dashboardConsoleHistoryIndex >= _dashboardConsoleHistory.Count)
                _dashboardConsoleHistoryDraft = input.Text;

            if (_dashboardConsoleHistoryIndex > 0)
                _dashboardConsoleHistoryIndex--;

            input.Text = _dashboardConsoleHistory[_dashboardConsoleHistoryIndex];
            input.CaretIndex = input.Text.Length;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && _dashboardConsoleHistory.Count > 0)
        {
            if (_dashboardConsoleHistoryIndex < _dashboardConsoleHistory.Count - 1)
            {
                _dashboardConsoleHistoryIndex++;
                input.Text = _dashboardConsoleHistory[_dashboardConsoleHistoryIndex];
            }
            else
            {
                _dashboardConsoleHistoryIndex = _dashboardConsoleHistory.Count;
                input.Text = _dashboardConsoleHistoryDraft;
            }

            input.CaretIndex = input.Text.Length;
            e.Handled = true;
        }
    }

    private void DashboardConsoleRun_Click(object sender, RoutedEventArgs e)
        => ExecuteDashboardPowerShellCommand(GetCurrentConsoleInput());

    private void DashboardConsoleRestart_Click(object sender, RoutedEventArgs e)
    {
        StopDashboardPowerShell();
        ClearConsoleTranscript();
        _dashboardConsoleHistoryIndex = _dashboardConsoleHistory.Count;
        _dashboardConsoleHistoryDraft = string.Empty;
        StartDashboardPowerShell();
    }

    private void ExecuteDashboardPowerShellCommand(TextBox? sourceInput = null)
    {
        var input = sourceInput ?? GetCurrentConsoleInput();
        var command = input.Text.TrimEnd();
        if (string.IsNullOrWhiteSpace(command))
            return;

        input.Clear();
        if (_dashboardConsoleHistory.Count == 0 ||
            !string.Equals(_dashboardConsoleHistory[^1], command, StringComparison.Ordinal))
        {
            _dashboardConsoleHistory.Add(command);
            if (_dashboardConsoleHistory.Count > 500)
                _dashboardConsoleHistory.RemoveAt(0);
            SavePowerShellHistory();
        }

        _dashboardConsoleHistoryIndex = _dashboardConsoleHistory.Count;
        _dashboardConsoleHistoryDraft = string.Empty;

        if (string.Equals(command.Trim(), "clear", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command.Trim(), "cls", StringComparison.OrdinalIgnoreCase))
        {
            ClearConsoleTranscript();
            AddDashboardConsoleLine("Console cleared.", Color.FromRgb(136, 171, 148));
            return;
        }

        AddDashboardConsoleLine($"{DashboardConsolePromptText.Text} {command}",
            Color.FromRgb(99, 255, 160), true);

        if (TryExecuteGhostApplicationCommand(command))
            return;

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

    private TextBox GetCurrentConsoleInput() =>
        _isTerminalModuleActive && _terminalConsoleInput is not null
            ? _terminalConsoleInput
            : DashboardConsoleInput;

    private bool TryExecuteGhostApplicationCommand(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 ||
            !string.Equals(parts[0], "ghost", StringComparison.OrdinalIgnoreCase))
            return false;

        var command = parts.Length > 1 ? parts[1].ToLowerInvariant() : "help";
        switch (command)
        {
            case "help":
            case "?":
                ExecuteGhostHelp();
                break;
            case "status":
                RunGhostReadCommand("status", ExecuteGhostStatus);
                break;
            case "cases":
                RunGhostReadCommand("cases", ExecuteGhostCases);
                break;
            case "case":
                RunGhostReadCommand("case", () => ExecuteGhostCaseCommand(input));
                break;
            case "evidence":
                RunGhostReadCommand("evidence", ExecuteGhostEvidence);
                break;
            case "labs":
                RunGhostReadCommand("labs", ExecuteGhostLabs);
                break;
            case "audit":
                RunGhostReadCommand("audit", () => ExecuteGhostAudit(parts));
                break;
            case "network":
                RunGhostReadCommand("network", ExecuteGhostNetwork);
                break;
            case "vault":
                RunGhostReadCommand("vault", ExecuteGhostVault);
                break;
            case "db":
                RunGhostReadCommand("db", ExecuteGhostDatabase);
                break;
            case "open":
                RunGhostReadCommand("open", () => ExecuteGhostOpen(parts));
                break;
            case "sysinfo":
            case "processes":
            case "ipconfig":
            case "diskspace":
            case "uptime":
            case "services":
            case "policy":
            case "history":
                RunGhostReadCommand(command, () => ExecuteGhostPowerShellTemplate(command));
                break;
            default:
                AddDashboardConsoleLine(
                    $"Unknown GHOST command: '{command}'. Type 'ghost help' to list all built-in GHOST commands.",
                    Color.FromRgb(255, 177, 107), true);
                break;
        }

        return true;
    }

    private void ExecuteGhostHelp()
    {
        AddDashboardConsoleLine("GHOST // ALL BUILT-IN APPLICATION COMMANDS", Color.FromRgb(99, 255, 160), true);
        AddDashboardConsoleLine("INVESTIGATION", Color.FromRgb(136, 171, 148), true);
        AddDashboardConsoleLine("ghost help                 Show all commands (aliases: 'ghost' and 'ghost ?')", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost status               App + live system snapshot", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost cases                List recent case files", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost case add \"<title>\" [--description \"<text>\"]  Create a case", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost case delete <id|case-number> --confirm  Delete a case and linked records", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost evidence             Evidence and archive totals", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost labs                 Experiments, observations, timeline", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost audit [count]        Recent audit events (default 8)", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost network              Graph node and relationship totals", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost vault                Check evidence file presence + SHA-256 metadata", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost db                   Active database provider, connection health + size", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost open <id|number>     Open a case inside GHOST", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("SYSTEM TOOLS", Color.FromRgb(136, 171, 148), true);
        AddDashboardConsoleLine("ghost sysinfo              Windows and device details", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost processes            Top 10 running processes", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost ipconfig             Network adapter configuration", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost diskspace            Available disk space", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost uptime               Last boot and uptime", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost services             Running Windows services", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost policy               PowerShell execution policy by scope", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("ghost history              Recent native PowerShell history", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("All GHOST app commands start with 'ghost'. Native PowerShell commands still run normally without that prefix.",
            Color.FromRgb(245, 196, 106));
    }

    private void RunGhostReadCommand(string commandName, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AddDashboardConsoleLine(
                $"GHOST {commandName.ToUpperInvariant()} ERROR  //  {ex.Message}",
                Color.FromRgb(255, 142, 142), true);
        }
    }

    private void ExecuteGhostStatus()
    {
        using var db = new GhostDbContext();
        AddDashboardConsoleLine("GHOST CORE // STATUS SNAPSHOT", Color.FromRgb(99, 255, 160), true);
        AddDashboardConsoleLine($"HOST          {Environment.MachineName}", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"POWERSHELL    {(_dashboardPowerShellProcess is { HasExited: false } ? "CONNECTED" : "STOPPED")}",
            Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"CPU           {CpuUsageText.Text}  /  {CpuUsageDetailText.Text}",
            Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"RAM           {MemoryUsageText.Text}  /  {MemoryUsageDetailText.Text}",
            Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"SYSTEM DRIVE  {DiskUsageText.Text}  /  {DiskUsageDetailText.Text}",
            Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"UPTIME        {FormatUptime(Environment.TickCount64)}",
            Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"CASES         {db.Cases.Count()}  |  OPEN {db.Cases.Count(x => x.Status == "Open")}",
            Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"ACTIVE VAULT  {db.Evidence.Count(x => !x.IsArchived)} RECORDS",
            Color.FromRgb(191, 255, 208));
    }

    private void ExecuteGhostCases()
    {
        using var db = new GhostDbContext();
        var cases = db.Cases.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .ToList();

        AddDashboardConsoleLine(
            $"CASE ARCHIVE // TOTAL {db.Cases.Count()} // OPEN {db.Cases.Count(x => x.Status == "Open")} // CLOSED {db.Cases.Count(x => x.Status == "Closed")}",
            Color.FromRgb(99, 255, 160), true);

        if (cases.Count == 0)
        {
            AddDashboardConsoleLine("No cases found. Use the Create Investigation action to start one.",
                Color.FromRgb(136, 171, 148));
            return;
        }

        foreach (var item in cases)
        {
            var statusColor = string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase)
                ? Color.FromRgb(99, 255, 160)
                : Color.FromRgb(136, 171, 148);
            AddDashboardConsoleLine(
                $"[{item.Status.ToUpperInvariant(),-6}] ID {item.Id,-4} {item.CaseNumber}  |  {item.Title}",
                statusColor);
        }

        AddDashboardConsoleLine("Use: ghost open <ID or case number>", Color.FromRgb(136, 171, 148));
    }

    private static int GetNextAvailableCaseId(GhostDbContext db)
    {
        var usedIds = db.Cases.AsNoTracking()
            .Select(x => x.Id)
            .OrderBy(x => x)
            .ToList();

        var candidate = 1;
        foreach (var id in usedIds)
        {
            if (id < candidate)
                continue;

            if (id > candidate)
                break;

            if (candidate == int.MaxValue)
                throw new InvalidOperationException("No available positive case ID remains.");

            candidate++;
        }

        return candidate;
    }

    private static string GenerateUniqueCaseNumber(GhostDbContext db)
    {
        string caseNumber;
        do
        {
            caseNumber = $"CASE-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";
        }
        while (db.Cases.Any(x => x.CaseNumber == caseNumber));

        return caseNumber;
    }

    private void ExecuteGhostCaseCommand(string input)
    {
        if (!TryParseGhostCommandArguments(input, out var arguments) ||
            arguments.Count < 3 ||
            !string.Equals(arguments[0], "ghost", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(arguments[1], "case", StringComparison.OrdinalIgnoreCase))
        {
            AddDashboardConsoleLine(
                "Usage: ghost case add \"<title>\" [--description \"<text>\"]  |  ghost case delete <id|case-number> --confirm",
                Color.FromRgb(245, 196, 106), true);
            return;
        }

        var action = arguments[2].ToLowerInvariant();
        if (action == "add")
        {
            var descriptionIndex = arguments.FindIndex(3,
                x => string.Equals(x, "--description", StringComparison.OrdinalIgnoreCase));
            var titleParts = descriptionIndex >= 0
                ? arguments.Skip(3).Take(descriptionIndex - 3)
                : arguments.Skip(3);
            var title = string.Join(" ", titleParts).Trim();
            var description = descriptionIndex >= 0
                ? string.Join(" ", arguments.Skip(descriptionIndex + 1)).Trim()
                : string.Empty;

            if (string.IsNullOrWhiteSpace(title))
            {
                AddDashboardConsoleLine(
                    "Usage: ghost case add \"<title>\" [--description \"<text>\"]",
                    Color.FromRgb(245, 196, 106), true);
                return;
            }

            if (title.Length > 200)
            {
                AddDashboardConsoleLine("Case title is too long. Maximum length is 200 characters.",
                    Color.FromRgb(255, 142, 142), true);
                return;
            }

            if (descriptionIndex >= 0 && string.IsNullOrWhiteSpace(description))
            {
                AddDashboardConsoleLine("Add a description after --description, or omit the option.",
                    Color.FromRgb(245, 196, 106), true);
                return;
            }

            using var db = new GhostDbContext();
            using var transaction = db.Database.BeginTransaction();

            var caseNumber = GenerateUniqueCaseNumber(db);

            var item = new CaseFile
            {
                Id = GetNextAvailableCaseId(db),
                Title = title,
                Description = description,
                CaseNumber = caseNumber
            };

            db.Cases.Add(item);
            db.SaveChanges();
            db.AuditLogs.Add(new AuditEntry
            {
                EntityType = "Case",
                EntityId = item.Id,
                Action = "Created",
                Details = $"Case created from GHOST terminal: {item.Title}"
            });
            db.SaveChanges();
            transaction.Commit();

            RefreshStats();
            RefreshNetwork();
            AddDashboardConsoleLine($"CASE CREATED // ID {item.Id} // {item.CaseNumber}",
                Color.FromRgb(99, 255, 160), true);
            AddDashboardConsoleLine($"TITLE       {item.Title}", Color.FromRgb(191, 255, 208));
            AddDashboardConsoleLine("Use 'ghost open <id|case-number>' to open this case.",
                Color.FromRgb(136, 171, 148));
            return;
        }

        if (action == "delete")
        {
            if (arguments.Count != 5 ||
                !string.Equals(arguments[4], "--confirm", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(arguments[3]))
            {
                AddDashboardConsoleLine(
                    "Usage: ghost case delete <id|case-number> --confirm  // confirmation is required",
                    Color.FromRgb(245, 196, 106), true);
                return;
            }

            var identifier = arguments[3].Trim();
            using var db = new GhostDbContext();
            CaseFile? match;
            if (int.TryParse(identifier, out var selectedId))
                match = db.Cases.AsNoTracking().FirstOrDefault(x => x.Id == selectedId);
            else
                match = db.Cases.AsNoTracking().ToList().FirstOrDefault(x =>
                    string.Equals(x.CaseNumber, identifier, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                AddDashboardConsoleLine($"No case matches '{identifier}'. Nothing was deleted.",
                    Color.FromRgb(255, 142, 142), true);
                return;
            }

            var caseFile = db.Cases.FirstOrDefault(x => x.Id == match.Id);
            if (caseFile is null)
            {
                AddDashboardConsoleLine("The selected case no longer exists. Run 'ghost cases' and try again.",
                    Color.FromRgb(255, 142, 142), true);
                return;
            }

            var deleted = DeleteCaseRecords(db, caseFile, "GHOST terminal");
            RefreshStats();
            RefreshNetwork();
            AddDashboardConsoleLine(
                $"CASE DELETED // ID {deleted.CaseId} // {deleted.CaseNumber}",
                Color.FromRgb(255, 142, 142), true);
            AddDashboardConsoleLine(
                $"REMOVED  {deleted.EvidenceCount} evidence records  |  {deleted.ExperimentCount} experiments  |  {deleted.ObservationCount} observations  |  {deleted.TimelineEventCount} timeline events  |  {deleted.RelationshipCount} relationships",
                Color.FromRgb(245, 196, 106));
            AddDashboardConsoleLine(
                "Historical audit events were retained under the deleted case identity. Evidence files remain on disk.",
                Color.FromRgb(136, 171, 148));
            return;
        }
        AddDashboardConsoleLine(
            "Unknown case action. Use 'ghost case add' or 'ghost case delete'.",
            Color.FromRgb(245, 196, 106), true);
    }

    private static CaseDeletionSummary DeleteCaseRecords(
        GhostDbContext db,
        CaseFile caseFile,
        string source)
    {
        using var transaction = db.Database.BeginTransaction();

        var evidence = db.Evidence.Where(x => x.CaseFileId == caseFile.Id).ToList();
        var experiments = db.Experiments.Where(x => x.CaseFileId == caseFile.Id).ToList();
        var experimentIds = experiments.Select(x => x.Id).ToHashSet();
        var observations = db.Observations
            .Where(x => experimentIds.Contains(x.ExperimentId))
            .ToList();
        var observationIds = observations.Select(x => x.Id).ToHashSet();
        var evidenceIds = evidence.Select(x => x.Id).ToHashSet();
        var timelineEvents = db.TimelineEvents.Where(x => x.CaseFileId == caseFile.Id).ToList();
        var timelineIds = timelineEvents.Select(x => x.Id).ToHashSet();

        bool IsDeletedRecord(string type, int id) => type.ToLowerInvariant() switch
        {
            "case" => id == caseFile.Id,
            "evidence" => evidenceIds.Contains(id),
            "experiment" => experimentIds.Contains(id),
            "observation" => observationIds.Contains(id),
            "timeline" or "timelineevent" or "fieldentry" => timelineIds.Contains(id),
            _ => false
        };

        var relatedRelationships = db.Relationships.ToList()
            .Where(x => IsDeletedRecord(x.SourceType, x.SourceId) ||
                        IsDeletedRecord(x.TargetType, x.TargetId))
            .ToList();
        var relationshipIds = relatedRelationships.Select(x => x.Id).ToHashSet();

        bool IsHistoricalAuditEntry(AuditEntry entry) => entry.EntityType.ToLowerInvariant() switch
        {
            "case" => entry.EntityId == caseFile.Id,
            "evidence" => evidenceIds.Contains(entry.EntityId),
            "experiment" => experimentIds.Contains(entry.EntityId),
            "observation" => observationIds.Contains(entry.EntityId),
            "fieldentry" or "timeline" or "timelineevent" => timelineIds.Contains(entry.EntityId),
            "relationship" => relationshipIds.Contains(entry.EntityId),
            _ => false
        };

        // Keep prior activity for the global audit, but detach it from a reusable numeric ID.
        var historicalAuditEntries = db.AuditLogs.ToList()
            .Where(IsHistoricalAuditEntry)
            .ToList();
        foreach (var entry in historicalAuditEntries)
        {
            var originalType = entry.EntityType;
            entry.EntityType = $"Archived/{caseFile.CaseNumber}/{originalType}";
            entry.Details = $"[Historical case {caseFile.CaseNumber}] {entry.Details}";
        }

        db.Relationships.RemoveRange(relatedRelationships);
        db.Observations.RemoveRange(observations);
        db.Experiments.RemoveRange(experiments);
        db.Evidence.RemoveRange(evidence);
        db.TimelineEvents.RemoveRange(timelineEvents);
        db.Cases.Remove(caseFile);
        db.AuditLogs.Add(new AuditEntry
        {
            EntityType = "DeletedCase",
            EntityId = caseFile.Id,
            Action = "Deleted",
            Details = $"Deleted case {caseFile.CaseNumber} ({caseFile.Title}) from {source}. Removed {evidence.Count} evidence records, {experiments.Count} experiments, {observations.Count} observations, {timelineEvents.Count} timeline events and {relatedRelationships.Count} relationships. Historical activity was retained with the case number. Evidence files were retained on disk."
        });

        db.SaveChanges();
        transaction.Commit();

        return new CaseDeletionSummary(
            caseFile.Id,
            caseFile.CaseNumber,
            caseFile.Title,
            evidence.Count,
            experiments.Count,
            observations.Count,
            timelineEvents.Count,
            relatedRelationships.Count);
    }

    private static bool TryParseGhostCommandArguments(string input, out List<string> arguments)
    {
        arguments = [];
        var token = new StringBuilder();
        var inQuotes = false;
        var tokenStarted = false;

        foreach (var character in input)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                tokenStarted = true;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (tokenStarted)
                {
                    arguments.Add(token.ToString());
                    token.Clear();
                    tokenStarted = false;
                }

                continue;
            }

            token.Append(character);
            tokenStarted = true;
        }

        if (inQuotes)
            return false;

        if (tokenStarted)
            arguments.Add(token.ToString());

        return true;
    }

    private void ExecuteGhostEvidence()
    {
        using var db = new GhostDbContext();
        var all = db.Evidence.AsNoTracking().ToList();
        var active = all.Count(x => !x.IsArchived);
        var archived = all.Count(x => x.IsArchived);
        var noHash = all.Count(x => string.IsNullOrWhiteSpace(x.Sha256));

        AddDashboardConsoleLine("EVIDENCE VAULT // INVENTORY", Color.FromRgb(99, 255, 160), true);
        AddDashboardConsoleLine($"TOTAL RECORDS       {all.Count}", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"ACTIVE              {active}", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"ARCHIVED            {archived}", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"WITHOUT SHA-256     {noHash}", noHash == 0
            ? Color.FromRgb(99, 255, 160)
            : Color.FromRgb(245, 196, 106));
    }

    private void ExecuteGhostLabs()
    {
        using var db = new GhostDbContext();
        var experiments = db.Experiments.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt).Take(5).ToList();

        AddDashboardConsoleLine("LABS // RESEARCH INVENTORY", Color.FromRgb(99, 255, 160), true);
        AddDashboardConsoleLine($"EXPERIMENTS    {db.Experiments.Count()}", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"OBSERVATIONS   {db.Observations.Count()}", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"TIMELINE EVENTS {db.TimelineEvents.Count()}", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"RELATIONSHIPS  {db.Relationships.Count()}", Color.FromRgb(191, 255, 208));

        foreach (var item in experiments)
        {
            var title = item.Title.Length > 58 ? item.Title[..55] + "..." : item.Title;
            AddDashboardConsoleLine($"#{item.Id,-4} [{item.Status.ToUpperInvariant(),-10}] {title}",
                Color.FromRgb(136, 171, 148));
        }
    }

    private void ExecuteGhostAudit(string[] parts)
    {
        var count = 8;
        if (parts.Length > 2 &&
            (!int.TryParse(parts[2], out count) || count is < 1 or > 25))
        {
            AddDashboardConsoleLine("Usage: ghost audit [count]  //  count must be 1–25",
                Color.FromRgb(245, 196, 106));
            return;
        }

        using var db = new GhostDbContext();
        var entries = db.AuditLogs.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(count)
            .ToList();

        AddDashboardConsoleLine($"AUDIT LOG // SHOWING {entries.Count} OF {db.AuditLogs.Count()} EVENTS",
            Color.FromRgb(99, 255, 160), true);

        if (entries.Count == 0)
        {
            AddDashboardConsoleLine("No audit events are recorded yet.", Color.FromRgb(136, 171, 148));
            return;
        }

        foreach (var entry in entries)
        {
            AddDashboardConsoleLine(
                $"{entry.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {entry.EntityType}#{entry.EntityId}  {entry.Action}",
                Color.FromRgb(191, 255, 208), true);
            var details = string.IsNullOrWhiteSpace(entry.Details) ? "(no details)" : entry.Details;
            if (details.Length > 110)
                details = details[..107] + "...";
            AddDashboardConsoleLine($"  {details}", Color.FromRgb(136, 171, 148));
        }
    }

    private void ExecuteGhostNetwork()
    {
        using var db = new GhostDbContext();
        var cases = db.Cases.Count();
        var evidence = db.Evidence.Count(x => !x.IsArchived);
        var experiments = db.Experiments.Count();
        var observations = db.Observations.Count();
        var relationships = db.Relationships.Count();
        var nodes = cases + evidence + experiments + observations;

        AddDashboardConsoleLine("INVESTIGATION NETWORK // INDEX SNAPSHOT", Color.FromRgb(99, 255, 160), true);
        AddDashboardConsoleLine($"NODES         {nodes}", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"  CASES       {cases}", Color.FromRgb(136, 171, 148));
        AddDashboardConsoleLine($"  EVIDENCE    {evidence} (active)", Color.FromRgb(136, 171, 148));
        AddDashboardConsoleLine($"  EXPERIMENTS {experiments}", Color.FromRgb(136, 171, 148));
        AddDashboardConsoleLine($"  OBSERVATIONS {observations}", Color.FromRgb(136, 171, 148));
        AddDashboardConsoleLine($"RELATIONSHIPS {relationships}", Color.FromRgb(191, 255, 208));
    }

    private void ExecuteGhostVault()
    {
        using var db = new GhostDbContext();
        var items = db.Evidence.AsNoTracking().ToList();
        var missingFiles = new List<string>();
        var missingHashes = 0;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Sha256))
                missingHashes++;

            try
            {
                var path = GetEvidenceFilePath(item.RelativePath);
                if (!File.Exists(path))
                    missingFiles.Add(item.Title);
            }
            catch (Exception)
            {
                missingFiles.Add(item.Title + " [INVALID PATH]");
            }
        }

        AddDashboardConsoleLine("VAULT HEALTH // FILE PRESENCE CHECK", Color.FromRgb(99, 255, 160), true);
        AddDashboardConsoleLine($"RECORDS          {items.Count}", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"MISSING COPIES   {missingFiles.Count}", missingFiles.Count == 0
            ? Color.FromRgb(99, 255, 160)
            : Color.FromRgb(255, 142, 142));
        AddDashboardConsoleLine($"MISSING HASHES   {missingHashes}", missingHashes == 0
            ? Color.FromRgb(99, 255, 160)
            : Color.FromRgb(245, 196, 106));

        foreach (var missing in missingFiles.Take(5))
            AddDashboardConsoleLine($"  MISSING: {missing}", Color.FromRgb(255, 142, 142));

        AddDashboardConsoleLine("Presence-only check; files are not re-hashed or modified.",
            Color.FromRgb(136, 171, 148));
    }

    private void ExecuteGhostDatabase()
    {
        AddDashboardConsoleLine("DATABASE // MARIADB", Color.FromRgb(99, 255, 160), true);
        AddDashboardConsoleLine($"TARGET  {GhostDbContext.ActiveDatabaseTarget}", Color.FromRgb(136, 171, 148));

        using var db = new GhostDbContext();
        db.Database.OpenConnection();

        using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT DATABASE(), VERSION();";
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException("MariaDB did not return its database and server version.");

            var databaseName = reader.IsDBNull(0) ? "(no database selected)" : reader.GetString(0);
            var serverVersion = reader.IsDBNull(1) ? "unknown" : reader.GetString(1);
            AddDashboardConsoleLine("CONNECTION  OK", Color.FromRgb(99, 255, 160), true);
            AddDashboardConsoleLine($"DATABASE    {databaseName}", Color.FromRgb(191, 255, 208));
            AddDashboardConsoleLine($"SERVER      {serverVersion}", Color.FromRgb(136, 171, 148));
        }

        AddDashboardConsoleLine($"CASES       {db.Cases.Count()}", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"EVIDENCE    {db.Evidence.Count()}", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine($"EXPERIMENTS {db.Experiments.Count()}", Color.FromRgb(191, 255, 208));
        AddDashboardConsoleLine("Inspect these tables in HeidiSQL under the database shown above.",
            Color.FromRgb(136, 171, 148));
    }

    private void ExecuteGhostOpen(string[] parts)
    {
        if (parts.Length < 3)
        {
            AddDashboardConsoleLine("Usage: ghost open <ID or case number>", Color.FromRgb(245, 196, 106));
            return;
        }

        var selector = string.Join(" ", parts.Skip(2)).Trim().Trim('"');
        int? caseId = null;

        using (var db = new GhostDbContext())
        {
            if (int.TryParse(selector, out var parsedId))
            {
                caseId = db.Cases.AsNoTracking()
                    .Where(x => x.Id == parsedId)
                    .Select(x => (int?)x.Id)
                    .FirstOrDefault();
            }
            else
            {
                caseId = db.Cases.AsNoTracking()
                    .Where(x => x.CaseNumber == selector)
                    .Select(x => (int?)x.Id)
                    .FirstOrDefault();
            }
        }

        if (caseId is not int id)
        {
            AddDashboardConsoleLine($"Case not found: {selector}", Color.FromRgb(255, 142, 142), true);
            return;
        }

        AddDashboardConsoleLine($"Opening case ID {id} in the GHOST workspace...",
            Color.FromRgb(99, 255, 160), true);
        OpenCaseWorkspace(id);
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

        SetGhostConsoleStatus("STOPPED", Color.FromRgb(255, 142, 142));
    }

    private void AddDashboardConsoleLine(string text, Color color, bool isBold = false)
    {
        var entry = new ConsoleTranscriptEntry(text, color, isBold);
        _consoleTranscript.Add(entry);
        const int maxConsoleLines = 1000;
        if (_consoleTranscript.Count > maxConsoleLines)
            _consoleTranscript.RemoveAt(0);

        RenderConsoleLine(DashboardConsoleOutputPanel, DashboardConsoleScrollViewer, entry);
        if (_terminalConsoleOutputPanel is not null && _terminalConsoleScrollViewer is not null)
            RenderConsoleLine(_terminalConsoleOutputPanel, _terminalConsoleScrollViewer, entry);
    }

    private void RenderConsoleLine(StackPanel panel, ScrollViewer scrollViewer, ConsoleTranscriptEntry entry)
    {
        var line = new TextBlock
        {
            Text = entry.Text,
            FontFamily = (FontFamily)FindResource("HackerFont"),
            FontSize = 9,
            Foreground = new SolidColorBrush(entry.Color),
            FontWeight = entry.IsBold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 3)
        };
        panel.Children.Add(line);

        const int maxConsoleLines = 1000;
        while (panel.Children.Count > maxConsoleLines)
            panel.Children.RemoveAt(0);

        scrollViewer.ScrollToEnd();
    }

    private void ReplayConsoleTranscript(StackPanel panel, ScrollViewer scrollViewer)
    {
        panel.Children.Clear();
        foreach (var entry in _consoleTranscript)
            RenderConsoleLine(panel, scrollViewer, entry);
    }

    private void ClearConsoleTranscript()
    {
        _consoleTranscript.Clear();
        DashboardConsoleOutputPanel.Children.Clear();
        if (_terminalConsoleOutputPanel is not null)
            _terminalConsoleOutputPanel.Children.Clear();
    }

    private void SetGhostConsoleStatus(string status, Color? color = null)
    {
        var brush = new SolidColorBrush(color ?? Color.FromRgb(99, 255, 160));
        DashboardConsoleStatusText.Text = status;
        DashboardConsoleStatusText.Foreground = brush;
        if (_terminalConsoleStatusText is not null)
        {
            _terminalConsoleStatusText.Text = status;
            _terminalConsoleStatusText.Foreground = new SolidColorBrush(brush.Color);
        }
    }

    private void SetGhostConsolePrompt(string prompt)
    {
        DashboardConsolePromptText.Text = prompt;
        if (_terminalConsolePromptText is not null)
            _terminalConsolePromptText.Text = prompt;
    }

    private void SetGhostConsoleHost(string host)
    {
        DashboardConsoleHostText.Text = host;
        if (_terminalConsoleHostText is not null)
            _terminalConsoleHostText.Text = host;
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        _isTerminalModuleActive = false;
        SetActiveNavigation("Dashboard");
        NetworkView.Visibility = Visibility.Collapsed;
        ModuleView.Visibility = Visibility.Collapsed;
        ModuleContent.Content = null;
        _activeModuleContent = null;
        DashboardView.Visibility = Visibility.Visible;
        PageEyebrow.Text = "PRIVATE INVESTIGATION WORKSPACE";
        PageEyebrow.Visibility = Visibility.Collapsed;
        PageTitle.Text = "Dashboard";
        RefreshStats();
    }

    private void RefreshStats()
    {
        using var db = new GhostDbContext();

        CasesCount.Text = db.Cases.Count(x => x.Status == "Open").ToString();
        ClosedCasesCount.Text = db.Cases.Count(x => x.Status == "Closed").ToString();
        TotalCasesCount.Text = db.Cases.Count().ToString();
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
        var localNow = DateTime.Now;
        ClockText.Text = localNow.ToString("HH:mm:ss");
        ClockDateText.Text = localNow.ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
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
        var returnToCaseFiles = string.Equals(PageTitle.Text, "CASE FILES", StringComparison.OrdinalIgnoreCase);
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
            MaxLength = 200,
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
        cancel.Click += (_, _) =>
        {
            if (returnToCaseFiles)
                Cases_Click(this, new RoutedEventArgs());
            else
                Dashboard_Click(this, new RoutedEventArgs());
        };

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
                int createdCaseId;
                using (var db = new GhostDbContext())
                {
                    using var transaction = db.Database.BeginTransaction();
                    var item = new CaseFile
                    {
                        Id = GetNextAvailableCaseId(db),
                        Title = title.Text.Trim(),
                        Description = description.Text.Trim(),
                        CaseNumber = GenerateUniqueCaseNumber(db)
                    };

                    db.Cases.Add(item);
                    db.AuditLogs.Add(new AuditEntry
                    {
                        EntityType = "Case",
                        EntityId = item.Id,
                        Action = "Created",
                        Details = $"Case created: {item.Title} ({item.CaseNumber})."
                    });
                    db.SaveChanges();
                    transaction.Commit();
                    createdCaseId = item.Id;
                }

                RefreshStats();
                RefreshNetwork();
                OpenCaseWorkspace(createdCaseId);
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
        PageTitle.Text = "CASE FILES";

        var root = new Grid { Margin = new Thickness(18, 16, 18, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Page header: clear purpose, concise context, and a primary action.
        var header = new Grid { Margin = new Thickness(0, 0, 0, 13) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerCopy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headerCopy.Children.Add(new TextBlock
        {
            Text = "INVESTIGATION MANAGEMENT",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("CyanBrush")
        });
        headerCopy.Children.Add(new TextBlock
        {
            Text = "Case Files",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 3, 0, 0)
        });
        headerCopy.Children.Add(new TextBlock
        {
            Text = "Search the archive, review case context, and move directly into an investigation.",
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 12, 0)
        });
        header.Children.Add(headerCopy);

        var newCaseButton = new Button
        {
            Content = "＋  NEW CASE",
            MinWidth = 132,
            MinHeight = 38,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        newCaseButton.Click += (_, _) => NewCase_Click(this, new RoutedEventArgs());
        Grid.SetColumn(newCaseButton, 1);
        header.Children.Add(newCaseButton);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var summaryPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 11) };
        var openMetric = CreateMetricCard("OPEN CASES", "—", "Investigations in progress");
        var closedMetric = CreateMetricCard("CLOSED CASES", "—", "Marked complete or inactive");
        var totalMetric = CreateMetricCard("ALL CASES", "—", "Records in the local archive");
        var evidenceMetric = CreateMetricCard("ACTIVE EVIDENCE", "—", "Non-archived vault items");
        summaryPanel.Children.Add(openMetric);
        summaryPanel.Children.Add(closedMetric);
        summaryPanel.Children.Add(totalMetric);
        summaryPanel.Children.Add(evidenceMetric);
        Grid.SetRow(summaryPanel, 1);
        root.Children.Add(summaryPanel);

        var searchBox = new TextBox
        {
            Height = 36,
            MinWidth = 170,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Search case number, title, description, or status"
        };
        var statusFilter = new ComboBox
        {
            Width = 155,
            Height = 36,
            Margin = new Thickness(9, 0, 0, 0),
            ToolTip = "Filter the archive by case status"
        };
        statusFilter.Items.Add("All statuses");
        statusFilter.Items.Add("Open");
        statusFilter.Items.Add("Closed");
        statusFilter.SelectedIndex = 0;

        var filterBar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filterBar.Children.Add(searchBox);
        Grid.SetColumn(statusFilter, 1);
        filterBar.Children.Add(statusFilter);

        var resetButton = new Button
        {
            Content = "RESET",
            MinWidth = 78,
            MinHeight = 36,
            Margin = new Thickness(9, 0, 0, 0),
            Padding = new Thickness(10, 7, 10, 7),
            ToolTip = "Clear search and status filters"
        };
        resetButton.Click += (_, _) =>
        {
            searchBox.Clear();
            statusFilter.SelectedIndex = 0;
        };
        Grid.SetColumn(resetButton, 2);
        filterBar.Children.Add(resetButton);
        Grid.SetRow(filterBar, 2);
        root.Children.Add(filterBar);

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.02, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.98, GridUnitType.Star) });
        Grid.SetRow(workspace, 3);
        root.Children.Add(workspace);

        // Archive pane with readable, selectable case cards.
        var archiveDock = new DockPanel { LastChildFill = true };
        var archiveHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
        archiveHeader.Children.Add(new TextBlock
        {
            Text = "INVESTIGATION ARCHIVE",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("CyanBrush")
        });
        var archiveCountText = new TextBlock
        {
            Text = "Loading cases…",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        archiveHeader.Children.Add(archiveCountText);
        DockPanel.SetDock(archiveHeader, Dock.Top);
        archiveDock.Children.Add(archiveHeader);

        var list = new ListView
        {
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontFamily = (FontFamily)FindResource("HackerFont"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(0)
        };

        var caseTemplate = new DataTemplate(typeof(CaseArchiveRow));
        var caseCard = new FrameworkElementFactory(typeof(Border));
        caseCard.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        caseCard.SetValue(Border.BorderBrushProperty, FindResource("LineBrush"));
        caseCard.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        caseCard.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        caseCard.SetValue(Border.PaddingProperty, new Thickness(12, 10, 12, 10));

        var cardStack = new FrameworkElementFactory(typeof(StackPanel));
        var topLine = new FrameworkElementFactory(typeof(DockPanel));
        topLine.SetValue(DockPanel.LastChildFillProperty, true);

        var caseStatus = new FrameworkElementFactory(typeof(TextBlock));
        caseStatus.SetBinding(TextBlock.TextProperty, new Binding(nameof(CaseArchiveRow.Status)));
        caseStatus.SetValue(TextBlock.FontSizeProperty, 9d);
        caseStatus.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        caseStatus.SetValue(TextBlock.ForegroundProperty, FindResource("NeonBrush"));
        caseStatus.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        caseStatus.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 0, 0));
        caseStatus.SetValue(DockPanel.DockProperty, Dock.Right);
        topLine.AppendChild(caseStatus);

        var caseNumber = new FrameworkElementFactory(typeof(TextBlock));
        caseNumber.SetBinding(TextBlock.TextProperty, new Binding(nameof(CaseArchiveRow.CaseNumber)));
        caseNumber.SetValue(TextBlock.FontSizeProperty, 9d);
        caseNumber.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        caseNumber.SetValue(TextBlock.ForegroundProperty, FindResource("CyanBrush"));
        caseNumber.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        topLine.AppendChild(caseNumber);
        cardStack.AppendChild(topLine);

        var caseTitle = new FrameworkElementFactory(typeof(TextBlock));
        caseTitle.SetBinding(TextBlock.TextProperty, new Binding(nameof(CaseArchiveRow.Title)));
        caseTitle.SetValue(TextBlock.FontSizeProperty, 12d);
        caseTitle.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        caseTitle.SetValue(TextBlock.ForegroundProperty, FindResource("TextBrush"));
        caseTitle.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        caseTitle.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 0));
        cardStack.AppendChild(caseTitle);

        var caseMeta = new FrameworkElementFactory(typeof(TextBlock));
        caseMeta.SetBinding(TextBlock.TextProperty, new Binding(nameof(CaseArchiveRow.CreatedAt)));
        caseMeta.SetValue(TextBlock.FontSizeProperty, 9d);
        caseMeta.SetValue(TextBlock.ForegroundProperty, FindResource("MutedBrush"));
        caseMeta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 0));
        cardStack.AppendChild(caseMeta);

        caseCard.AppendChild(cardStack);
        caseTemplate.VisualTree = caseCard;
        list.ItemTemplate = caseTemplate;

        var caseItemStyle = new Style(typeof(ListViewItem), (Style)FindResource(typeof(ListViewItem)));
        caseItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        caseItemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 6)));
        caseItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        caseItemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        list.ItemContainerStyle = caseItemStyle;

        var emptyHint = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Margin = new Thickness(18)
        };
        emptyHint.Children.Add(new TextBlock
        {
            Text = "NO CASES MATCH",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        emptyHint.Children.Add(new TextBlock
        {
            Text = "Create a case or adjust the search and filters.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 235,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var listHost = new Grid();
        listHost.Children.Add(list);
        listHost.Children.Add(emptyHint);

        var archiveCardContent = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(listHost, Dock.Bottom);
        archiveCardContent.Children.Add(listHost);
        archiveCardContent.Children.Add(archiveHeader);
        var archiveCard = CreateModuleCard(archiveCardContent, new Thickness(0, 0, 9, 0));
        archiveCard.Padding = new Thickness(13);
        Grid.SetColumn(archiveCard, 0);
        workspace.Children.Add(archiveCard);

        // Case inspector: selected record, provenance context, linked-record totals, and actions.
        var detailDock = new DockPanel { LastChildFill = true };
        var actionBar = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var openButton = new Button
        {
            Content = "OPEN WORKSPACE",
            MinWidth = 145,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 8),
            IsEnabled = false
        };
        var statusButton = new Button
        {
            Content = "CLOSE CASE",
            MinWidth = 118,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 8),
            IsEnabled = false
        };
        var deleteButton = new Button
        {
            Content = "DELETE",
            MinWidth = 88,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 8),
            IsEnabled = false,
            Background = new SolidColorBrush(Color.FromRgb(45, 20, 27)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(111, 54, 66)),
            Foreground = new SolidColorBrush(Color.FromRgb(255, 160, 170))
        };
        actionBar.Children.Add(openButton);
        actionBar.Children.Add(statusButton);
        actionBar.Children.Add(deleteButton);
        DockPanel.SetDock(actionBar, Dock.Bottom);
        detailDock.Children.Add(actionBar);

        var detailScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var detailStack = new StackPanel { Margin = new Thickness(5, 4, 5, 8) };
        detailStack.Children.Add(new TextBlock
        {
            Text = "CASE INSPECTOR",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("CyanBrush")
        });
        var detailTitle = new TextBlock
        {
            Text = "Select a case",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };
        detailStack.Children.Add(detailTitle);
        var detailNumber = new TextBlock
        {
            Text = "NO CASE SELECTED",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("NeonBrush"),
            Margin = new Thickness(0, 7, 0, 13)
        };
        detailStack.Children.Add(detailNumber);
        detailStack.Children.Add(CreateFormLabel("DESCRIPTION"));
        var detailDescription = new TextBlock
        {
            Text = "Select an investigation in the archive to inspect its description and linked records.",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 17,
            Margin = new Thickness(0, 0, 0, 14)
        };
        detailStack.Children.Add(detailDescription);
        detailStack.Children.Add(CreateFormLabel("CASE METADATA"));
        var detailMeta = new TextBlock
        {
            Text = "Created and updated timestamps will appear here.",
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        detailStack.Children.Add(detailMeta);
        detailStack.Children.Add(CreateFormLabel("LINKED RECORDS"));
        var detailCounts = new TextBlock
        {
            Text = "Evidence: —    Experiments: —    Timeline entries: —",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        detailStack.Children.Add(detailCounts);
        detailStack.Children.Add(CreateFormLabel("LATEST CASE ACTIVITY"));
        var detailActivity = new TextBlock
        {
            Text = "Timeline activity will appear when a case is selected.",
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 16
        };
        detailStack.Children.Add(detailActivity);
        detailScroll.Content = detailStack;
        detailDock.Children.Add(detailScroll);

        var detailsCard = CreateModuleCard(detailDock);
        detailsCard.Padding = new Thickness(15);
        detailsCard.Margin = new Thickness(9, 0, 0, 0);
        Grid.SetColumn(detailsCard, 1);
        workspace.Children.Add(detailsCard);

        void RefreshSummary()
        {
            using var db = new GhostDbContext();
            var openCount = db.Cases.Count(x => x.Status == "Open");
            var closedCount = db.Cases.Count(x => x.Status == "Closed");
            var totalCount = db.Cases.Count();
            var evidenceCount = db.Evidence.Count(x => !x.IsArchived);
            var values = new[]
            {
                (openMetric, openCount.ToString(CultureInfo.InvariantCulture)),
                (closedMetric, closedCount.ToString(CultureInfo.InvariantCulture)),
                (totalMetric, totalCount.ToString(CultureInfo.InvariantCulture)),
                (evidenceMetric, evidenceCount.ToString(CultureInfo.InvariantCulture))
            };
            foreach (var (card, value) in values)
            {
                if (card.Child is StackPanel stack && stack.Children.Count > 1 && stack.Children[1] is TextBlock valueText)
                    valueText.Text = value;
            }
        }

        void UpdateSelectedCaseDetails()
        {
            var selected = list.SelectedItem as CaseArchiveRow;
            openButton.IsEnabled = selected is not null;
            statusButton.IsEnabled = selected is not null;
            deleteButton.IsEnabled = selected is not null;

            if (selected is null)
            {
                detailTitle.Text = "Select a case";
                detailNumber.Text = "NO CASE SELECTED";
                detailDescription.Text = "Select an investigation in the archive to inspect its description and linked records.";
                detailMeta.Text = "Created and updated timestamps will appear here.";
                detailCounts.Text = "Evidence: —    Experiments: —    Timeline entries: —";
                detailActivity.Text = "Timeline activity will appear when a case is selected.";
                statusButton.Content = "CLOSE CASE";
                return;
            }

            try
            {
                using var db = new GhostDbContext();
                var item = db.Cases.AsNoTracking().FirstOrDefault(x => x.Id == selected.Id);
                if (item is null)
                {
                    detailActivity.Text = "This case is no longer present in the database. Refresh the archive.";
                    return;
                }

                var evidenceCount = db.Evidence.Count(x => x.CaseFileId == item.Id && !x.IsArchived);
                var experimentCount = db.Experiments.Count(x => x.CaseFileId == item.Id);
                var timelineCount = db.TimelineEvents.Count(x => x.CaseFileId == item.Id);
                var latestActivity = db.TimelineEvents.AsNoTracking()
                    .Where(x => x.CaseFileId == item.Id)
                    .OrderByDescending(x => x.OccurredAt)
                    .Select(x => new { x.Title, x.OccurredAt })
                    .FirstOrDefault();

                detailTitle.Text = item.Title;
                detailNumber.Text = $"{item.CaseNumber}   /   {item.Status.ToUpperInvariant()}";
                detailDescription.Text = string.IsNullOrWhiteSpace(item.Description)
                    ? "No description has been recorded for this case."
                    : item.Description;
                detailMeta.Text = $"Created {item.CreatedAt.ToLocalTime():dd MMM yyyy, HH:mm}    •    Updated {item.UpdatedAt.ToLocalTime():dd MMM yyyy, HH:mm}";
                detailCounts.Text = $"Active evidence: {evidenceCount}    •    Experiments: {experimentCount}    •    Timeline entries: {timelineCount}";
                detailActivity.Text = latestActivity is null
                    ? "No timeline activity has been recorded for this case yet."
                    : $"{latestActivity.OccurredAt.ToLocalTime():dd MMM yyyy, HH:mm}   •   {latestActivity.Title}";
                statusButton.Content = string.Equals(item.Status, "Closed", StringComparison.OrdinalIgnoreCase)
                    ? "REOPEN CASE"
                    : "CLOSE CASE";
            }
            catch (Exception ex)
            {
                detailActivity.Text = $"Case details could not be loaded: {ex.Message}";
            }
        }

        void RefreshRows()
        {
            try
            {
                var term = searchBox.Text.Trim();
                var status = statusFilter.SelectedItem?.ToString() ?? "All statuses";
                using var db = new GhostDbContext();
                var allCases = db.Cases.AsNoTracking()
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList();
                var records = allCases
                    .Where(x =>
                        (status == "All statuses" || string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase)) &&
                        (string.IsNullOrWhiteSpace(term) ||
                         x.CaseNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                         x.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                         (!string.IsNullOrWhiteSpace(x.Description) && x.Description.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                         x.Status.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    .Select(x => new CaseArchiveRow(
                        x.Id,
                        x.CaseNumber,
                        x.Title,
                        x.Status,
                        x.CreatedAt.ToLocalTime().ToString("dd MMM yyyy  •  HH:mm", CultureInfo.InvariantCulture)))
                    .ToList();

                var previousId = (list.SelectedItem as CaseArchiveRow)?.Id;
                list.ItemsSource = records;
                list.SelectedItem = previousId.HasValue
                    ? records.FirstOrDefault(x => x.Id == previousId.Value)
                    : null;
                emptyHint.Visibility = records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                archiveCountText.Text = records.Count == allCases.Count
                    ? $"{records.Count} CASE(S) IN ARCHIVE"
                    : $"{records.Count} SHOWN  /  {allCases.Count} TOTAL";
                UpdateSelectedCaseDetails();
            }
            catch (Exception ex)
            {
                archiveCountText.Text = "ARCHIVE UNAVAILABLE";
                detailActivity.Text = $"The case archive could not be loaded: {ex.Message}";
            }
        }

        list.SelectionChanged += (_, _) => UpdateSelectedCaseDetails();
        searchBox.TextChanged += (_, _) => RefreshRows();
        statusFilter.SelectionChanged += (_, _) => RefreshRows();

        openButton.Click += (_, _) =>
        {
            if (list.SelectedItem is CaseArchiveRow selected)
                OpenCaseWorkspace(selected.Id);
            else
                detailActivity.Text = "Select a case first.";
        };

        statusButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not CaseArchiveRow selected)
                return;

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
                RefreshSummary();
                RefreshRows();
                RefreshStats();
                RefreshNetwork();
            }
            catch (Exception ex)
            {
                detailActivity.Text = $"Case status could not be changed: {ex.Message}";
            }
        };

        deleteButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not CaseArchiveRow selected)
                return;

            var confirmation = MessageBox.Show(
                this,
                $"Delete case {selected.CaseNumber} / {selected.Title}?\n\nThis removes its linked database records and relationships. Original evidence files stay on disk. This action cannot be undone.",
                "GHOST // Confirm case deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
                return;

            try
            {
                using var db = new GhostDbContext();
                var stored = db.Cases.FirstOrDefault(x => x.Id == selected.Id);
                if (stored is null)
                {
                    RefreshRows();
                    return;
                }

                var deleted = DeleteCaseRecords(db, stored, "CASE FILES");
                RefreshSummary();
                RefreshRows();
                RefreshStats();
                RefreshNetwork();
                detailActivity.Text = $"Case {deleted.CaseNumber} was deleted. Audit history was retained and evidence files remain on disk.";
            }
            catch (Exception ex)
            {
                detailActivity.Text = $"Case could not be deleted: {ex.Message}";
            }
        };

        ShowModuleView(root, "CASE FILES");
        RefreshSummary();
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
        var actionMargin = new Thickness(0, 0, 8, 8);
        var saveButton = new Button { Content = "SAVE CASE DETAILS", MinWidth = 155, Margin = actionMargin };
        var statusButton = new Button { Content = caseFile.Status == "Closed" ? "REOPEN CASE" : "CLOSE CASE", MinWidth = 115, Margin = actionMargin };
        var timelineEntryButton = new Button { Content = "＋ ADD TIMELINE ENTRY", MinWidth = 164, Margin = actionMargin };
        var openDigitalLab = new Button { Content = "OPEN DIGITAL LAB", MinWidth = 145, Margin = actionMargin };
        var openFieldLab = new Button { Content = "OPEN FIELD LAB", MinWidth = 135, Margin = actionMargin };
        var openEvidence = new Button { Content = "VIEW EVIDENCE", MinWidth = 125, Margin = actionMargin };
        var openNetwork = new Button { Content = "VIEW NETWORK", MinWidth = 125, Margin = actionMargin };
        var deleteWorkspaceCaseButton = new Button
        {
            Content = "DELETE CASE",
            MinWidth = 115,
            Margin = actionMargin,
            Background = new SolidColorBrush(Color.FromRgb(42, 23, 31)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(105, 55, 69)),
            Foreground = new SolidColorBrush(Color.FromRgb(255, 150, 156))
        };
        actions.Children.Add(saveButton);
        actions.Children.Add(statusButton);
        actions.Children.Add(timelineEntryButton);
        actions.Children.Add(openDigitalLab);
        actions.Children.Add(openFieldLab);
        actions.Children.Add(openEvidence);
        actions.Children.Add(openNetwork);
        actions.Children.Add(deleteWorkspaceCaseButton);
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

        timelineEntryButton.Click += (_, _) => OpenAddTimelineEntryForm(caseId);
        deleteWorkspaceCaseButton.Click += (_, _) =>
        {
            var confirmation = MessageBox.Show(
                this,
                $"Delete case {caseFile.CaseNumber} / {caseFile.Title}?\n\nLinked evidence, experiments, observations, timeline entries, and connections will be removed from the database. Original evidence files remain on disk. This cannot be undone.",
                "GHOST // Confirm case deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
                return;

            try
            {
                using var db = new GhostDbContext();
                var stored = db.Cases.FirstOrDefault(x => x.Id == caseId);
                if (stored is null)
                {
                    Cases_Click(this, new RoutedEventArgs());
                    return;
                }

                var deleted = DeleteCaseRecords(db, stored, "case workspace");
                RefreshStats();
                RefreshNetwork();
                Cases_Click(this, new RoutedEventArgs());
                MessageBox.Show(
                    this,
                    $"Case {deleted.CaseNumber} was deleted. Historical audit activity was retained and evidence files were left on disk.",
                    "GHOST // Case deleted",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                caseStatusMessage.Text = $"The case could not be deleted: {ex.Message}";
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

    private void OpenAddTimelineEntryForm(int caseId)
    {
        CaseFile? caseFile;
        using (var db = new GhostDbContext())
            caseFile = db.Cases.AsNoTracking().FirstOrDefault(x => x.Id == caseId);

        if (caseFile is null)
        {
            MessageBox.Show(this, "This case no longer exists. Refresh the case archive.", "GHOST",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Cases_Click(this, new RoutedEventArgs());
            return;
        }

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = "CASE JOURNAL // NEW TIMELINE ENTRY",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("NeonBrush")
        });
        heading.Children.Add(new TextBlock
        {
            Text = $"{caseFile.CaseNumber}  /  {caseFile.Title}  /  timestamp recorded automatically",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var content = new StackPanel();
        content.Children.Add(CreateFormLabel("ENTRY TITLE"));
        var titleBox = new TextBox
        {
            Height = 38,
            MaxLength = 180,
            Padding = new Thickness(9, 7, 9, 7),
            Background = (Brush)FindResource("PanelAltBrush"),
            Foreground = Brushes.White,
            BorderBrush = (Brush)FindResource("LineBrush")
        };
        content.Children.Add(titleBox);
        content.Children.Add(CreateFormLabel("DETAILS / OBSERVATIONS"));
        var detailsBox = new TextBox
        {
            Height = 180,
            Padding = new Thickness(9, 7, 9, 7),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (Brush)FindResource("PanelAltBrush"),
            Foreground = Brushes.White,
            BorderBrush = (Brush)FindResource("LineBrush")
        };
        content.Children.Add(detailsBox);

        var status = new TextBlock
        {
            Text = "Entries are timestamped, saved locally, and recorded in the audit history.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        content.Children.Add(status);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button
        {
            Content = "BACK TO CASE",
            MinWidth = 120,
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancel.Click += (_, _) => OpenCaseWorkspace(caseId);
        var save = new Button
        {
            Content = "SAVE TIMELINE ENTRY",
            MinWidth = 170
        };
        save.Click += (_, _) =>
        {
            var entryTitle = titleBox.Text.Trim();
            var details = detailsBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(entryTitle))
            {
                status.Text = "Enter a title for this timeline entry.";
                titleBox.Focus();
                return;
            }

            try
            {
                using (var db = new GhostDbContext())
                {
                    using var transaction = db.Database.BeginTransaction();
                    var stored = db.Cases.FirstOrDefault(x => x.Id == caseId);
                    if (stored is null)
                    {
                        status.Text = "This case no longer exists. Return to the archive and refresh.";
                        return;
                    }

                    var occurredAt = DateTime.UtcNow;
                    stored.UpdatedAt = occurredAt;
                    db.TimelineEvents.Add(new TimelineEvent
                    {
                        CaseFileId = caseId,
                        Title = entryTitle,
                        Details = details,
                        OccurredAt = occurredAt
                    });
                    var detailForAudit = details.Length > 180 ? details[..180] + "..." : details;
                    db.AuditLogs.Add(new AuditEntry
                    {
                        EntityType = "Case",
                        EntityId = caseId,
                        Action = "Timeline Entry Added",
                        Details = $"Timeline entry added to case {caseFile.CaseNumber}: {entryTitle}. {detailForAudit}"
                    });
                    db.SaveChanges();
                    transaction.Commit();
                }

                RefreshStats();
                RefreshNetwork();
                OpenCaseWorkspace(caseId);
            }
            catch (Exception ex)
            {
                status.Text = $"Timeline entry could not be saved: {ex.Message}";
            }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        content.Children.Add(buttons);

        var formCard = CreateModuleCard(content);
        Grid.SetRow(formCard, 1);
        root.Children.Add(formCard);
        ShowModuleView(root, "ADD TIMELINE ENTRY");
        SetActiveNavigation("Cases");
        titleBox.Focus();
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
        var root = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Background = (Brush)FindResource("HeroBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10)
        };
        var headerLayout = new Grid();
        headerLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerCopy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headerCopy.Children.Add(new TextBlock
        {
            Text = "GHOST  /  FORENSIC WORKSPACE",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("CyanBrush")
        });
        headerCopy.Children.Add(new TextBlock
        {
            Text = "Digital Lab",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("NeonBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        headerCopy.Children.Add(new TextBlock
        {
            Text = "Repeatable analysis. Traceable observations. Case-linked evidence.",
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        headerLayout.Children.Add(headerCopy);

        var labStatus = new Border
        {
            Background = (Brush)FindResource("DotGridBrush"),
            BorderBrush = (Brush)FindResource("NeonBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var labStatusContent = new StackPanel();
        labStatusContent.Children.Add(new TextBlock
        {
            Text = "●  LAB WORKSPACE",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("NeonBrush")
        });
        labStatusContent.Children.Add(new TextBlock
        {
            Text = "LOCAL  •  CASE-BOUND",
            FontSize = 8,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 5, 0, 0)
        });
        labStatus.Child = labStatusContent;
        Grid.SetColumn(labStatus, 1);
        headerLayout.Children.Add(labStatus);
        header.Child = headerLayout;

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var casePicker = new ComboBox
        {
            Height = 36,
            Width = 300,
            MaxDropDownHeight = 360,
            DisplayMemberPath = nameof(LabCaseOption.DisplayText),
            ToolTip = "Select an investigation to filter its experiments, observations, and available Evidence Vault items"
        };
        var searchBox = new TextBox
        {
            Height = 36,
            Width = 300,
            ToolTip = "Search titles, objectives, methods, categories, and conclusions",
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var categoryFilter = new ComboBox
        {
            Height = 36,
            Width = 145,
            MaxDropDownHeight = 300,
            ToolTip = "Show every category or narrow the archive to one analysis type"
        };
        foreach (var category in new[] { "All categories", "File Analysis", "Log Analysis", "Network Analysis", "Scientific", "Other" })
            categoryFilter.Items.Add(category);
        categoryFilter.SelectedIndex = 0;

        var statusFilter = new ComboBox
        {
            Height = 36,
            Width = 120,
            MaxDropDownHeight = 240,
            ToolTip = "Show all experiments, drafts, or completed experiments"
        };
        foreach (var item in new[] { "All statuses", "Draft", "Completed" })
            statusFilter.Items.Add(item);
        statusFilter.SelectedIndex = 0;

        StackPanel MakeToolbarField(string label, UIElement control)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            panel.Children.Add(CreateFormLabel(label));
            panel.Children.Add(control);
            return panel;
        }

        var toolbarFields = new WrapPanel { VerticalAlignment = VerticalAlignment.Bottom };
        toolbarFields.Children.Add(MakeToolbarField("INVESTIGATION", casePicker));
        toolbarFields.Children.Add(MakeToolbarField("SEARCH ARCHIVE", searchBox));
        toolbarFields.Children.Add(MakeToolbarField("ANALYSIS CATEGORY", categoryFilter));
        toolbarFields.Children.Add(MakeToolbarField("EXPERIMENT STATUS", statusFilter));

        var caseContextText = new TextBlock
        {
            Text = "Loading investigation context…",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var openCasesButton = new Button
        {
            Content = "MANAGE CASES  ↗",
            MinWidth = 135,
            MinHeight = 32,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(10, 7, 10, 7),
            ToolTip = "Open Case Files to create or manage investigations"
        };
        var caseContextRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(openCasesButton, Dock.Right);
        caseContextRow.Children.Add(openCasesButton);
        caseContextRow.Children.Add(caseContextText);
        var caseContextBorder = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(2, 10, 2, 0),
            Margin = new Thickness(0, 10, 0, 0),
            Child = caseContextRow
        };

        var toolbarContent = new StackPanel();
        toolbarContent.Children.Add(toolbarFields);
        toolbarContent.Children.Add(caseContextBorder);
        var toolbarCard = CreateModuleCard(toolbarContent, new Thickness(0, 0, 0, 8));
        toolbarCard.Padding = new Thickness(10, 8, 10, 9);
        openCasesButton.Click += (_, _) => Cases_Click(this, new RoutedEventArgs());

        Border BuildMetric(string heading, string caption, out TextBlock valueText)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = heading,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("MutedBrush")
            });
            valueText = new TextBlock
            {
                Text = "0",
                FontSize = 25,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush"),
                Margin = new Thickness(0, 3, 0, 1)
            };
            stack.Children.Add(valueText);
            stack.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 9,
                Foreground = (Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            var card = CreateModuleCard(stack, new Thickness(0, 0, 10, 0));
            card.Padding = new Thickness(13, 10, 13, 10);
            card.MinWidth = 155;
            return card;
        }

        var metricPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        metricPanel.Children.Add(BuildMetric("EXPERIMENTS", "In the selected investigation", out var experimentsMetric));
        metricPanel.Children.Add(BuildMetric("COMPLETED", "With a saved conclusion", out var completedMetric));
        metricPanel.Children.Add(BuildMetric("OBSERVATIONS", "Recorded measurements and notes", out var observationsMetric));
        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.43, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.57, GridUnitType.Star) });
        Grid.SetRow(workspace, 1);
        root.Children.Add(workspace);

        var leftDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toolbarCard, Dock.Top);
        leftDock.Children.Add(toolbarCard);
        var archiveHeader = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        archiveHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        archiveHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var archiveHeading = new StackPanel();
        archiveHeading.Children.Add(new TextBlock
        {
            Text = "EXPERIMENT ARCHIVE",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("CyanBrush")
        });
        archiveHeading.Children.Add(new TextBlock
        {
            Text = "Select a record to review its method, findings, and observations.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 8, 0)
        });
        var archiveCountText = new TextBlock
        {
            Text = "0 SHOWN  /  0 TOTAL",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("NeonBrush"),
            Margin = new Thickness(0, 7, 0, 0)
        };
        archiveHeading.Children.Add(archiveCountText);
        archiveHeader.Children.Add(archiveHeading);

        var clearFiltersButton = new Button
        {
            Content = "RESET FILTERS",
            MinHeight = 29,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 9,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Clear search and return to all experiments in this investigation"
        };
        clearFiltersButton.Click += (_, _) =>
        {
            searchBox.Clear();
            categoryFilter.SelectedIndex = 0;
            statusFilter.SelectedIndex = 0;
        };
        Grid.SetColumn(clearFiltersButton, 1);
        archiveHeader.Children.Add(clearFiltersButton);
        DockPanel.SetDock(archiveHeader, Dock.Top);
        leftDock.Children.Add(archiveHeader);

        var createHeader = new StackPanel { Orientation = Orientation.Horizontal };
        createHeader.Children.Add(new TextBlock
        {
            Text = "＋",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("NeonBrush"),
            Margin = new Thickness(0, 0, 8, 0)
        });
        createHeader.Children.Add(new TextBlock
        {
            Text = "NEW EXPERIMENT",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var createExpander = new Expander
        {
            Header = createHeader,
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 10),
            Background = (Brush)FindResource("PanelAltBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1),
            ToolTip = "Start a new experiment linked to the active investigation"
        };
        var createForm = new StackPanel();
        var titleBox = new TextBox
        {
            Height = 34,
            MaxLength = 200,
            ToolTip = "A concise name that explains what you are checking",
            Margin = new Thickness(0, 0, 0, 5)
        };
        var categoryBox = new ComboBox { Height = 34, Margin = new Thickness(0, 0, 0, 5) };
        foreach (var item in new[] { "File Analysis", "Log Analysis", "Network Analysis", "Scientific", "Other" })
            categoryBox.Items.Add(item);
        categoryBox.SelectedIndex = 0;
        var hypothesisBox = new TextBox
        {
            Height = 62,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "What question are you trying to answer?",
            Margin = new Thickness(0, 0, 0, 5)
        };
        var procedureBox = new TextBox
        {
            Height = 76,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Record the steps or method so the work can be understood later",
            Margin = new Thickness(0, 0, 0, 8)
        };
        var createButton = new Button
        {
            Content = "SAVE EXPERIMENT",
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 160
        };
        createForm.Children.Add(CreateFormLabel("EXPERIMENT TITLE"));
        createForm.Children.Add(titleBox);
        createForm.Children.Add(CreateFormLabel("ANALYSIS TYPE"));
        createForm.Children.Add(categoryBox);
        createForm.Children.Add(CreateFormLabel("OBJECTIVE / QUESTION"));
        createForm.Children.Add(hypothesisBox);
        createForm.Children.Add(CreateFormLabel("METHOD / STEPS"));
        createForm.Children.Add(procedureBox);
        createForm.Children.Add(createButton);
        createExpander.Content = createForm;
        DockPanel.SetDock(createExpander, Dock.Top);
        leftDock.Children.Add(createExpander);

        var experimentList = new ListView
        {
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(0)
        };

        var experimentTemplate = new DataTemplate(typeof(ExperimentArchiveRow));
        var archiveItemCard = new FrameworkElementFactory(typeof(Border));
        archiveItemCard.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        archiveItemCard.SetValue(Border.BorderBrushProperty, FindResource("LineBrush"));
        archiveItemCard.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        archiveItemCard.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        archiveItemCard.SetValue(Border.PaddingProperty, new Thickness(10, 9, 10, 9));

        var experimentItemStack = new FrameworkElementFactory(typeof(StackPanel));
        var experimentTitleItem = new FrameworkElementFactory(typeof(TextBlock));
        experimentTitleItem.SetBinding(TextBlock.TextProperty, new Binding(nameof(ExperimentArchiveRow.Title)));
        experimentTitleItem.SetValue(TextBlock.FontSizeProperty, 12d);
        experimentTitleItem.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        experimentTitleItem.SetValue(TextBlock.ForegroundProperty, FindResource("TextBrush"));
        experimentTitleItem.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        experimentItemStack.AppendChild(experimentTitleItem);

        var experimentMetaItem = new FrameworkElementFactory(typeof(TextBlock));
        experimentMetaItem.SetBinding(TextBlock.TextProperty, new Binding(nameof(ExperimentArchiveRow.MetaText)));
        experimentMetaItem.SetValue(TextBlock.FontSizeProperty, 9d);
        experimentMetaItem.SetValue(TextBlock.ForegroundProperty, FindResource("CyanBrush"));
        experimentMetaItem.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        experimentMetaItem.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));
        experimentItemStack.AppendChild(experimentMetaItem);

        var experimentObjectiveItem = new FrameworkElementFactory(typeof(TextBlock));
        experimentObjectiveItem.SetBinding(TextBlock.TextProperty, new Binding(nameof(ExperimentArchiveRow.ObjectivePreview)));
        experimentObjectiveItem.SetValue(TextBlock.FontSizeProperty, 9d);
        experimentObjectiveItem.SetValue(TextBlock.ForegroundProperty, FindResource("MutedBrush"));
        experimentObjectiveItem.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        experimentObjectiveItem.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 0));
        experimentObjectiveItem.SetValue(FrameworkElement.MaxHeightProperty, 30d);
        experimentItemStack.AppendChild(experimentObjectiveItem);

        archiveItemCard.AppendChild(experimentItemStack);
        experimentTemplate.VisualTree = archiveItemCard;
        experimentList.ItemTemplate = experimentTemplate;

        var listItemStyle = new Style(
            typeof(ListViewItem),
            (Style)FindResource(typeof(ListViewItem)));
        listItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        listItemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 6)));
        listItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        listItemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        experimentList.ItemContainerStyle = listItemStyle;

        var experimentListHost = new Grid();
        experimentListHost.Children.Add(experimentList);
        var emptyExperiments = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(22),
            IsHitTestVisible = false
        };
        emptyExperiments.Children.Add(new TextBlock
        {
            Text = "⌕",
            FontSize = 30,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Foreground = (Brush)FindResource("CyanBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        emptyExperiments.Children.Add(new TextBlock
        {
            Text = "NO EXPERIMENTS TO DISPLAY",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        emptyExperiments.Children.Add(new TextBlock
        {
            Text = "Create an experiment or reset the search and filters.\nRecords are scoped to the selected investigation.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0),
            MaxWidth = 250
        });
        experimentListHost.Children.Add(emptyExperiments);
        var experimentListFrame = new Border
        {
            Background = (Brush)FindResource("PanelAltBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Child = experimentListHost
        };
        leftDock.Children.Add(experimentListFrame);

        var leftCard = CreateModuleCard(leftDock, new Thickness(0, 0, 10, 0));
        leftCard.Padding = new Thickness(14);
        Grid.SetColumn(leftCard, 0);
        workspace.Children.Add(leftCard);

        var detailTabs = new TabControl { Margin = new Thickness(0) };
        var overviewTab = new TabItem { Header = "OVERVIEW" };
        var observationsTab = new TabItem { Header = "OBSERVATIONS" };
        var fileToolsTab = new TabItem { Header = "SAFE FILE TOOLS" };

        var overviewScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var overviewPanel = new StackPanel { Margin = new Thickness(12) };
        var selectedExperimentTitle = new TextBlock
        {
            Text = "Select an experiment",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("NeonBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        var selectedExperimentMeta = new TextBlock
        {
            Text = "No experiment selected",
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 5, 0, 14)
        };
        overviewPanel.Children.Add(selectedExperimentTitle);
        overviewPanel.Children.Add(selectedExperimentMeta);

        TextBlock AddDetailBlock(string label, string initialValue)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            block.Children.Add(CreateFormLabel(label));
            var value = new TextBlock
            {
                Text = initialValue,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextBrush"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17
            };
            block.Children.Add(value);
            overviewPanel.Children.Add(block);
            return value;
        }

        var hypothesisDetail = AddDetailBlock("OBJECTIVE / QUESTION", "Select an experiment to see its objective.");
        var procedureDetail = AddDetailBlock("METHOD / STEPS", "The recorded method will appear here.");
        overviewPanel.Children.Add(CreateFormLabel("RESULT / CONCLUSION"));
        var resultBox = new TextBox
        {
            MinHeight = 88,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Write what the experiment showed, including uncertainty or limitations"
        };
        overviewPanel.Children.Add(resultBox);
        var saveResultButton = new Button
        {
            Content = "SAVE CONCLUSION",
            MinWidth = 150,
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 15),
            IsEnabled = false
        };
        overviewPanel.Children.Add(saveResultButton);

        overviewPanel.Children.Add(new TextBlock
        {
            Text = "LINK EXISTING EVIDENCE",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("CyanBrush"),
            Margin = new Thickness(0, 5, 0, 2)
        });
        overviewPanel.Children.Add(new TextBlock
        {
            Text = "Associate a file already preserved in the Evidence Vault. This creates a traceable relationship; it does not change the file.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 5)
        });

        var evidenceLinkRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var evidencePicker = new ComboBox
        {
            Height = 34,
            Width = 285,
            DisplayMemberPath = nameof(EvidenceOption.DisplayText),
            ToolTip = "Choose evidence from the selected investigation"
        };
        var linkEvidenceButton = new Button
        {
            Content = "LINK TO EXPERIMENT",
            MinHeight = 34,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        evidenceLinkRow.Children.Add(evidencePicker);
        evidenceLinkRow.Children.Add(linkEvidenceButton);
        overviewPanel.Children.Add(evidenceLinkRow);

        var linkedEvidenceList = new ListBox
        {
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            MinHeight = 48,
            MaxHeight = 115,
            FontFamily = new FontFamily("Segoe UI"),
            ToolTip = "Evidence already linked to this experiment"
        };
        overviewPanel.Children.Add(linkedEvidenceList);
        overviewTab.Content = overviewScroll;
        overviewScroll.Content = overviewPanel;

        var observationsPanel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        var observationHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
        observationHeader.Children.Add(new TextBlock
        {
            Text = "OBSERVATION HISTORY",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("CyanBrush")
        });
        var observationCountText = new TextBlock
        {
            Text = "Select an experiment to view its observations.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        observationHeader.Children.Add(observationCountText);
        DockPanel.SetDock(observationHeader, Dock.Top);
        observationsPanel.Children.Add(observationHeader);

        var observationForm = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        observationForm.Children.Add(CreateFormLabel("WHAT DID YOU OBSERVE?"));
        var observationBox = new TextBox
        {
            Height = 66,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Record the finding in plain language"
        };
        observationForm.Children.Add(observationBox);
        observationForm.Children.Add(CreateFormLabel("OPTIONAL MEASUREMENT"));
        var measurementRow = new WrapPanel();
        var numericBox = new TextBox
        {
            Width = 125,
            Height = 34,
            ToolTip = "Numeric value, for example 12.5",
            Margin = new Thickness(0, 0, 8, 8)
        };
        var unitBox = new TextBox
        {
            Width = 125,
            Height = 34,
            ToolTip = "Unit, for example ms, MB, or %",
            Margin = new Thickness(0, 0, 8, 8)
        };
        measurementRow.Children.Add(numericBox);
        measurementRow.Children.Add(unitBox);
        observationForm.Children.Add(measurementRow);
        var addObservationButton = new Button
        {
            Content = "RECORD OBSERVATION",
            MinHeight = 34,
            MinWidth = 170,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = false
        };
        observationForm.Children.Add(addObservationButton);
        DockPanel.SetDock(observationForm, Dock.Bottom);
        observationsPanel.Children.Add(observationForm);

        var observationList = new ListView
        {
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            FontFamily = new FontFamily("Segoe UI")
        };
        var observationTemplate = new DataTemplate(typeof(ObservationArchiveRow));
        var observationItemStack = new FrameworkElementFactory(typeof(StackPanel));
        var observationDateItem = new FrameworkElementFactory(typeof(TextBlock));
        observationDateItem.SetBinding(TextBlock.TextProperty, new Binding(nameof(ObservationArchiveRow.RecordedAtText)));
        observationDateItem.SetValue(TextBlock.FontSizeProperty, 9d);
        observationDateItem.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        observationDateItem.SetValue(TextBlock.ForegroundProperty, FindResource("CyanBrush"));
        observationItemStack.AppendChild(observationDateItem);
        var observationDetailsItem = new FrameworkElementFactory(typeof(TextBlock));
        observationDetailsItem.SetBinding(TextBlock.TextProperty, new Binding(nameof(ObservationArchiveRow.DetailsText)));
        observationDetailsItem.SetValue(TextBlock.FontSizeProperty, 11d);
        observationDetailsItem.SetValue(TextBlock.ForegroundProperty, FindResource("TextBrush"));
        observationDetailsItem.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        observationDetailsItem.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0));
        observationItemStack.AppendChild(observationDetailsItem);
        observationTemplate.VisualTree = observationItemStack;
        observationList.ItemTemplate = observationTemplate;
        DockPanel.SetDock(observationList, Dock.Top);
        observationsPanel.Children.Add(observationList);
        observationsTab.Content = observationsPanel;

        var fileToolsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var fileToolsPanel = new StackPanel { Margin = new Thickness(12) };
        fileToolsPanel.Children.Add(new TextBlock
        {
            Text = "READ-ONLY FILE INSPECTION",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("CyanBrush")
        });
        fileToolsPanel.Children.Add(new TextBlock
        {
            Text = "Calculate a SHA-256 fingerprint, inspect basic file metadata, and preview a small portion of supported text files. GHOST does not execute the selected file or modify it.",
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 10)
        });

        var fileActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var analyzeFileButton = new Button
        {
            Content = "CHOOSE FILE & ANALYZE",
            MinHeight = 35,
            MinWidth = 180,
            Margin = new Thickness(0, 0, 8, 8)
        };
        var copyHashButton = new Button
        {
            Content = "COPY SHA-256",
            MinHeight = 35,
            MinWidth = 120,
            Margin = new Thickness(0, 0, 8, 8),
            IsEnabled = false
        };
        var saveFileFindingButton = new Button
        {
            Content = "SAVE FINDINGS AS OBSERVATION",
            MinHeight = 35,
            Margin = new Thickness(0, 0, 8, 8),
            IsEnabled = false
        };
        fileActions.Children.Add(analyzeFileButton);
        fileActions.Children.Add(copyHashButton);
        fileActions.Children.Add(saveFileFindingButton);
        fileToolsPanel.Children.Add(fileActions);

        var fileAnalysisSummary = new TextBlock
        {
            Text = "No file analyzed yet.",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        fileToolsPanel.Children.Add(CreateModuleCard(fileAnalysisSummary, new Thickness(0, 0, 0, 10)));
        fileToolsPanel.Children.Add(CreateFormLabel("TEXT PREVIEW (SUPPORTED FORMATS ONLY)"));
        var filePreviewBox = new TextBox
        {
            MinHeight = 145,
            MaxHeight = 270,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Text = "Text preview appears here for supported text formats. Binary files are not interpreted as text."
        };
        fileToolsPanel.Children.Add(filePreviewBox);
        fileToolsPanel.Children.Add(new TextBlock
        {
            Text = "Important: SHA-256 can help detect byte changes. By itself, it does not prove who created a file, where it came from, or that its contents are authentic.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });
        fileToolsScroll.Content = fileToolsPanel;
        fileToolsTab.Content = fileToolsScroll;

        detailTabs.Items.Add(overviewTab);
        detailTabs.Items.Add(observationsTab);
        detailTabs.Items.Add(fileToolsTab);
        var rightCard = CreateModuleCard(detailTabs);
        rightCard.Padding = new Thickness(8);
        rightCard.Margin = new Thickness(0);

        var rightDock = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(10, 0, 0, 0)
        };
        metricPanel.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(metricPanel, Dock.Top);
        rightDock.Children.Add(metricPanel);
        rightDock.Children.Add(rightCard);
        Grid.SetColumn(rightDock, 1);
        workspace.Children.Add(rightDock);

        var footerStatus = new TextBlock
        {
            Text = "Choose an investigation, then select an experiment or create one to begin.",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(1, 8, 1, 0)
        };
        Grid.SetRow(footerStatus, 2);
        root.Children.Add(footerStatus);

        int? selectedExperimentId = null;
        string? analyzedFilePath = null;
        string? analyzedFileHash = null;
        string? analyzedFileSize = null;
        string? analyzedFileModified = null;
        string? analyzedTextPreview = null;
        bool isRefreshingExperimentList = false;

        void RefreshInvestigationContext()
        {
            if (casePicker.SelectedItem is LabCaseOption selectedCase)
            {
                caseContextText.Text =
                    $"ACTIVE INVESTIGATION  •  {selectedCase.CaseNumber}  /  {selectedCase.Title}";
                caseContextText.Foreground = (Brush)FindResource("TextBrush");
                openCasesButton.Content = "MANAGE CASES  ↗";
                openCasesButton.ToolTip = "Open Case Files to review or manage investigations";
            }
            else
            {
                caseContextText.Text =
                    "NO ACTIVE INVESTIGATION  •  Create or select a case before recording experiments.";
                caseContextText.Foreground = (Brush)FindResource("AmberBrush");
                openCasesButton.Content = "OPEN CASE FILES  ↗";
                openCasesButton.ToolTip = "Open Case Files to create your first investigation";
            }
        }

        void RefreshMetricsAndArchive()
        {
            try
            {
                using var db = new GhostDbContext();
                var selectedCaseId = (casePicker.SelectedItem as LabCaseOption)?.Id;
                var entities = selectedCaseId.HasValue
                    ? db.Experiments.AsNoTracking()
                        .Where(x => x.CaseFileId == selectedCaseId.Value)
                        .OrderByDescending(x => x.CreatedAt)
                        .ToList()
                    : new List<Experiment>();

                var experimentIds = entities.Select(x => x.Id).ToList();
                var observationCounts = experimentIds.Count == 0
                    ? new Dictionary<int, int>()
                    : db.Observations.AsNoTracking()
                        .Where(x => experimentIds.Contains(x.ExperimentId))
                        .ToList()
                        .GroupBy(x => x.ExperimentId)
                        .ToDictionary(g => g.Key, g => g.Count());

                experimentsMetric.Text = entities.Count.ToString(CultureInfo.InvariantCulture);
                completedMetric.Text = entities.Count(x => string.Equals(x.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                    .ToString(CultureInfo.InvariantCulture);
                observationsMetric.Text = observationCounts.Values.Sum().ToString(CultureInfo.InvariantCulture);

                var rows = entities.Select(x =>
                {
                    var count = observationCounts.TryGetValue(x.Id, out var recorded) ? recorded : 0;
                    var created = x.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    var searchTextValue = string.Join(" ", x.Title, x.Category, x.Status, x.Hypothesis, x.Procedure, x.Results)
                        .ToLowerInvariant();
                    return new ExperimentArchiveRow(
                        x.Id, x.CaseFileId, x.Title, x.Category, x.Status, created,
                        x.Results, x.Hypothesis, x.Procedure, count, searchTextValue);
                }).ToList();

                var query = searchBox.Text.Trim().ToLowerInvariant();
                var selectedCategory = categoryFilter.SelectedItem?.ToString() ?? "All categories";
                var selectedStatus = statusFilter.SelectedItem?.ToString() ?? "All statuses";
                var filtered = rows.Where(x =>
                    (string.IsNullOrWhiteSpace(query) || x.SearchText.Contains(query, StringComparison.Ordinal)) &&
                    (selectedCategory == "All categories" || x.Category == selectedCategory) &&
                    (selectedStatus == "All statuses" || string.Equals(x.Status, selectedStatus, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var previousId = (experimentList.SelectedItem as ExperimentArchiveRow)?.Id;
                isRefreshingExperimentList = true;
                experimentList.ItemsSource = filtered;
                experimentList.SelectedItem = previousId.HasValue
                    ? filtered.FirstOrDefault(x => x.Id == previousId.Value) ?? filtered.FirstOrDefault()
                    : filtered.FirstOrDefault();
                isRefreshingExperimentList = false;

                archiveCountText.Text = $"{filtered.Count} SHOWN  /  {entities.Count} TOTAL";
                emptyExperiments.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                footerStatus.Text = selectedCaseId.HasValue
                    ? $"{filtered.Count} of {entities.Count} experiment(s) shown for the selected case."
                    : "Create a case first, then select it here to start a Digital Lab record.";

                DisplaySelectedExperiment(experimentList.SelectedItem as ExperimentArchiveRow);
            }
            catch (Exception ex)
            {
                isRefreshingExperimentList = false;
                footerStatus.Text = $"Digital Lab could not load its records: {ex.Message}";
            }
        }

        void RefreshEvidencePicker()
        {
            var oldEvidenceId = (evidencePicker.SelectedItem as EvidenceOption)?.Id;
            try
            {
                using var db = new GhostDbContext();
                var selectedCaseId = (casePicker.SelectedItem as LabCaseOption)?.Id;
                var evidenceRows = selectedCaseId.HasValue
                    ? db.Evidence.AsNoTracking()
                        .Where(x => x.CaseFileId == selectedCaseId.Value && !x.IsArchived)
                        .OrderBy(x => x.Title)
                        .ToList()
                        .Select(x => new EvidenceOption(x.Id, x.CaseFileId, x.Title, x.Sha256, x.RelativePath, x.IsArchived))
                        .ToList()
                    : new List<EvidenceOption>();

                evidencePicker.ItemsSource = evidenceRows;
                evidencePicker.SelectedItem = oldEvidenceId.HasValue
                    ? evidenceRows.FirstOrDefault(x => x.Id == oldEvidenceId.Value) ?? evidenceRows.FirstOrDefault()
                    : evidenceRows.FirstOrDefault();
                linkEvidenceButton.IsEnabled = selectedExperimentId.HasValue && evidenceRows.Count > 0;
            }
            catch (Exception ex)
            {
                footerStatus.Text = $"Evidence list could not be loaded: {ex.Message}";
            }
        }

        void RefreshLinkedEvidence()
        {
            if (!selectedExperimentId.HasValue)
            {
                linkedEvidenceList.ItemsSource = Array.Empty<string>();
                return;
            }

            try
            {
                using var db = new GhostDbContext();
                var links = db.Relationships.AsNoTracking()
                    .Where(x => x.SourceType == "Experiment" &&
                                x.SourceId == selectedExperimentId.Value &&
                                x.TargetType == "Evidence")
                    .ToList();
                var evidenceIds = links.Select(x => x.TargetId).Distinct().ToList();
                var evidenceRows = evidenceIds.Count == 0
                    ? new List<EvidenceItem>()
                    : db.Evidence.AsNoTracking().Where(x => evidenceIds.Contains(x.Id)).OrderBy(x => x.Title).ToList();

                linkedEvidenceList.ItemsSource = evidenceRows.Select(x =>
                    $"{x.Title}{(x.IsArchived ? "  •  ARCHIVED" : string.Empty)}\nSHA-256: {(string.IsNullOrWhiteSpace(x.Sha256) ? "not recorded" : x.Sha256)}")
                    .ToList();
            }
            catch (Exception ex)
            {
                footerStatus.Text = $"Linked evidence could not be loaded: {ex.Message}";
            }
        }

        void RefreshObservationList()
        {
            if (!selectedExperimentId.HasValue)
            {
                observationList.ItemsSource = Array.Empty<ObservationArchiveRow>();
                observationCountText.Text = "Select an experiment to view its observations.";
                return;
            }

            try
            {
                using var db = new GhostDbContext();
                var experimentId = selectedExperimentId.Value;
                var total = db.Observations.AsNoTracking().Count(x => x.ExperimentId == experimentId);
                var rows = db.Observations.AsNoTracking()
                    .Where(x => x.ExperimentId == experimentId)
                    .OrderByDescending(x => x.RecordedAt)
                    .Take(150)
                    .ToList()
                    .Select(x => new ObservationArchiveRow(
                        x.Id,
                        x.ExperimentId,
                        x.Description,
                        x.NumericValue,
                        x.Unit,
                        x.RecordedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)))
                    .ToList();

                observationList.ItemsSource = rows;
                observationCountText.Text = total > rows.Count
                    ? $"Showing the latest {rows.Count} of {total} observations."
                    : $"{total} observation(s) recorded. Newest entries appear first.";
            }
            catch (Exception ex)
            {
                observationCountText.Text = $"Observations could not be loaded: {ex.Message}";
            }
        }

        void DisplaySelectedExperiment(ExperimentArchiveRow? row)
        {
            selectedExperimentId = row?.Id;
            var hasSelection = row is not null;
            selectedExperimentTitle.Text = row?.Title ?? "Select an experiment";
            selectedExperimentMeta.Text = row is null
                ? "No experiment selected"
                : $"{row.Category}  •  {row.Status}  •  Created {row.CreatedAt}  •  {row.ObservationCount} observation(s)";
            hypothesisDetail.Text = row is null || string.IsNullOrWhiteSpace(row.Hypothesis)
                ? "No objective or question has been recorded."
                : row.Hypothesis;
            procedureDetail.Text = row is null || string.IsNullOrWhiteSpace(row.Procedure)
                ? "No method or steps have been recorded."
                : row.Procedure;
            resultBox.Text = row?.Results ?? string.Empty;
            saveResultButton.IsEnabled = hasSelection;
            addObservationButton.IsEnabled = hasSelection;
            linkEvidenceButton.IsEnabled = hasSelection && evidencePicker.Items.Count > 0;
            saveFileFindingButton.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(analyzedFileHash);
            RefreshObservationList();
            RefreshLinkedEvidence();
        }

        bool SaveObservation(int experimentId, string description, double? numericValue, string unit)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                footerStatus.Text = "Enter an observation description before saving.";
                return false;
            }

            try
            {
                using var db = new GhostDbContext();
                using var transaction = db.Database.BeginTransaction();
                var experiment = db.Experiments.FirstOrDefault(x => x.Id == experimentId);
                if (experiment is null)
                {
                    footerStatus.Text = "That experiment no longer exists. Refresh the archive and try again.";
                    return false;
                }

                var now = DateTime.UtcNow;
                var observation = new Observation
                {
                    ExperimentId = experiment.Id,
                    Description = description.Trim(),
                    NumericValue = numericValue,
                    Unit = unit.Trim(),
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
                        ? $" | Value: {numericValue.Value.ToString(CultureInfo.InvariantCulture)} {unit.Trim()}"
                        : string.Empty),
                    OccurredAt = now
                });
                db.SaveChanges();
                transaction.Commit();

                RefreshMetricsAndArchive();
                RefreshNetwork();
                footerStatus.Text = "Observation saved. The case timeline and audit history were updated.";
                return true;
            }
            catch (Exception ex)
            {
                footerStatus.Text = $"Observation could not be saved: {ex.Message}";
                return false;
            }
        }

        casePicker.ItemsSource = new List<LabCaseOption>();
        using (var db = new GhostDbContext())
        {
            var cases = db.Cases.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToList()
                .Select(x => new LabCaseOption(x.Id, x.CaseNumber, x.Title))
                .ToList();
            casePicker.ItemsSource = cases;
            casePicker.SelectedItem = _selectedCaseId.HasValue
                ? cases.FirstOrDefault(x => x.Id == _selectedCaseId.Value) ?? cases.FirstOrDefault()
                : cases.FirstOrDefault();
        }

        RefreshInvestigationContext();
        casePicker.SelectionChanged += (_, _) =>
        {
            if (!ReferenceEquals(_activeModuleContent, root))
                return;
            _selectedCaseId = (casePicker.SelectedItem as LabCaseOption)?.Id;
            RefreshInvestigationContext();
            RefreshEvidencePicker();
            RefreshMetricsAndArchive();
        };
        searchBox.TextChanged += (_, _) =>
        {
            if (ReferenceEquals(_activeModuleContent, root))
                RefreshMetricsAndArchive();
        };
        categoryFilter.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_activeModuleContent, root))
                RefreshMetricsAndArchive();
        };
        statusFilter.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_activeModuleContent, root))
                RefreshMetricsAndArchive();
        };
        experimentList.SelectionChanged += (_, _) =>
        {
            if (!isRefreshingExperimentList)
                DisplaySelectedExperiment(experimentList.SelectedItem as ExperimentArchiveRow);
        };

        createButton.Click += (_, _) =>
        {
            if (casePicker.SelectedItem is not LabCaseOption selectedCase)
            {
                footerStatus.Text = "Create a case first in CASE FILES, then select it here.";
                return;
            }
            if (string.IsNullOrWhiteSpace(titleBox.Text))
            {
                footerStatus.Text = "Enter an experiment title.";
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
                categoryBox.SelectedIndex = 0;
                createExpander.IsExpanded = false;
                RefreshEvidencePicker();
                RefreshMetricsAndArchive();
                RefreshStats();
                RefreshNetwork();
                footerStatus.Text = "Experiment saved. Select it to review the method, record a conclusion, or add observations.";
            }
            catch (Exception ex)
            {
                footerStatus.Text = $"Experiment could not be saved: {ex.Message}";
            }
        };

        saveResultButton.Click += (_, _) =>
        {
            if (experimentList.SelectedItem is not ExperimentArchiveRow selected)
            {
                footerStatus.Text = "Select an experiment before saving a conclusion.";
                return;
            }

            try
            {
                using var db = new GhostDbContext();
                using var transaction = db.Database.BeginTransaction();
                var experiment = db.Experiments.FirstOrDefault(x => x.Id == selected.Id);
                if (experiment is null)
                {
                    footerStatus.Text = "That experiment no longer exists. Refresh the archive and try again.";
                    RefreshMetricsAndArchive();
                    return;
                }

                experiment.Results = resultBox.Text.Trim();
                experiment.Status = string.IsNullOrWhiteSpace(experiment.Results) ? "Draft" : "Completed";
                db.AuditLogs.Add(new AuditEntry
                {
                    EntityType = "Experiment",
                    EntityId = experiment.Id,
                    Action = "Result Updated",
                    Details = $"Conclusion updated for experiment {experiment.Title}."
                });
                db.TimelineEvents.Add(new TimelineEvent
                {
                    CaseFileId = experiment.CaseFileId,
                    Title = "Experiment conclusion updated: " + experiment.Title,
                    Details = string.IsNullOrWhiteSpace(experiment.Results) ? "Conclusion cleared." : experiment.Results,
                    OccurredAt = DateTime.UtcNow
                });
                db.SaveChanges();
                transaction.Commit();

                RefreshMetricsAndArchive();
                RefreshStats();
                RefreshNetwork();
                footerStatus.Text = "Conclusion saved; the timeline and audit history were updated.";
            }
            catch (Exception ex)
            {
                footerStatus.Text = $"Conclusion could not be saved: {ex.Message}";
            }
        };

        addObservationButton.Click += (_, _) =>
        {
            if (!selectedExperimentId.HasValue)
            {
                footerStatus.Text = "Select an experiment before recording an observation.";
                return;
            }
            if (string.IsNullOrWhiteSpace(observationBox.Text))
            {
                footerStatus.Text = "Describe the observation before saving it.";
                observationBox.Focus();
                return;
            }

            double? numericValue = null;
            if (!string.IsNullOrWhiteSpace(numericBox.Text))
            {
                if (!double.TryParse(numericBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    footerStatus.Text = "Enter a valid number using a dot for decimals, for example 12.5.";
                    numericBox.Focus();
                    return;
                }
                numericValue = parsed;
            }

            if (SaveObservation(selectedExperimentId.Value, observationBox.Text, numericValue, unitBox.Text))
            {
                observationBox.Clear();
                numericBox.Clear();
                unitBox.Clear();
            }
        };

        linkEvidenceButton.Click += (_, _) =>
        {
            if (experimentList.SelectedItem is not ExperimentArchiveRow selected)
            {
                footerStatus.Text = "Select an experiment before linking evidence.";
                return;
            }
            if (evidencePicker.SelectedItem is not EvidenceOption evidence)
            {
                footerStatus.Text = "No available evidence is listed for this investigation.";
                return;
            }

            try
            {
                using var db = new GhostDbContext();
                using var transaction = db.Database.BeginTransaction();
                var experiment = db.Experiments.FirstOrDefault(x => x.Id == selected.Id);
                var evidenceItem = db.Evidence.FirstOrDefault(x => x.Id == evidence.Id);
                if (experiment is null || evidenceItem is null ||
                    experiment.CaseFileId != evidenceItem.CaseFileId || evidenceItem.IsArchived)
                {
                    footerStatus.Text = "The experiment and evidence must belong to the same case, and the evidence must be active.";
                    return;
                }

                var alreadyLinked = db.Relationships.Any(x =>
                    x.SourceType == "Experiment" && x.SourceId == experiment.Id &&
                    x.TargetType == "Evidence" && x.TargetId == evidenceItem.Id);
                if (alreadyLinked)
                {
                    footerStatus.Text = "This evidence is already linked to the selected experiment.";
                    return;
                }

                var now = DateTime.UtcNow;
                var link = new EvidenceRelationship
                {
                    SourceType = "Experiment",
                    SourceId = experiment.Id,
                    TargetType = "Evidence",
                    TargetId = evidenceItem.Id,
                    RelationshipType = "Analyzed in",
                    Description = "Evidence associated with the experiment from Digital Lab.",
                    Confidence = 0.5,
                    CreatedAt = now
                };
                db.Relationships.Add(link);
                db.SaveChanges();
                db.AuditLogs.Add(new AuditEntry
                {
                    EntityType = "Relationship",
                    EntityId = link.Id,
                    Action = "Created",
                    Details = $"Evidence '{evidenceItem.Title}' linked to experiment '{experiment.Title}'."
                });
                db.TimelineEvents.Add(new TimelineEvent
                {
                    CaseFileId = experiment.CaseFileId,
                    Title = "Evidence linked to experiment: " + experiment.Title,
                    Details = $"Evidence: {evidenceItem.Title}; relationship: Analyzed in.",
                    OccurredAt = now
                });
                db.SaveChanges();
                transaction.Commit();

                RefreshLinkedEvidence();
                RefreshNetwork();
                footerStatus.Text = "Evidence linked. The file was not modified; the relationship was added to the audit trail.";
            }
            catch (Exception ex)
            {
                footerStatus.Text = $"Evidence could not be linked: {ex.Message}";
            }
        };

        analyzeFileButton.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose a file for read-only inspection",
                Filter = "All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
                return;

            analyzeFileButton.IsEnabled = false;
            try
            {
                var snapshot = await Task.Run(() =>
                {
                    var info = new FileInfo(dialog.FileName);
                    if (!info.Exists)
                        throw new FileNotFoundException("The selected file no longer exists.", dialog.FileName);

                    string hash;
                    using (var stream = new FileStream(
                               dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read,
                               1024 * 1024, FileOptions.SequentialScan))
                    {
                        hash = Convert.ToHexString(SHA256.HashData(stream));
                    }

                    var extension = IOPath.GetExtension(dialog.FileName);
                    var supportedTextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ".txt", ".log", ".csv", ".json", ".xml", ".md", ".ini", ".cfg", ".yml", ".yaml"
                    };
                    var preview = "Preview is not available for this format. The file was not executed.";
                    if (supportedTextExtensions.Contains(extension))
                    {
                        using var reader = new StreamReader(
                            new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read),
                            Encoding.UTF8, true);
                        var buffer = new char[12000];
                        var read = reader.Read(buffer, 0, buffer.Length);
                        preview = new string(buffer, 0, read);
                        if (reader.Peek() >= 0)
                            preview += Environment.NewLine + "… preview truncated; the original file was not changed.";
                    }

                    return (
                        Info: info,
                        Hash: hash,
                        Size: info.Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes",
                        Modified: info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        Preview: preview);
                });

                analyzedFilePath = dialog.FileName;
                analyzedFileHash = snapshot.Hash;
                analyzedFileSize = snapshot.Size;
                analyzedFileModified = snapshot.Modified;
                analyzedTextPreview = snapshot.Preview;
                fileAnalysisSummary.Text =
                    $"FILE: {snapshot.Info.Name}\n" +
                    $"PATH: {analyzedFilePath}\n" +
                    $"SIZE: {snapshot.Size}\n" +
                    $"LAST MODIFIED: {snapshot.Modified}\n" +
                    $"SHA-256: {snapshot.Hash}";
                filePreviewBox.Text = snapshot.Preview;
                copyHashButton.IsEnabled = true;
                saveFileFindingButton.IsEnabled = selectedExperimentId.HasValue;
                footerStatus.Text = "Read-only inspection finished. No file was executed or modified.";
            }
            catch (Exception ex)
            {
                footerStatus.Text = $"File inspection failed: {ex.Message}";
            }
            finally
            {
                analyzeFileButton.IsEnabled = true;
            }
        };

        copyHashButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(analyzedFileHash))
            {
                footerStatus.Text = "Analyze a file first to calculate its SHA-256 fingerprint.";
                return;
            }

            try
            {
                Clipboard.SetText(analyzedFileHash);
                footerStatus.Text = "SHA-256 fingerprint copied to the clipboard.";
            }
            catch (Exception ex)
            {
                footerStatus.Text = $"Hash could not be copied: {ex.Message}";
            }
        };

        saveFileFindingButton.Click += (_, _) =>
        {
            if (!selectedExperimentId.HasValue ||
                string.IsNullOrWhiteSpace(analyzedFilePath) ||
                string.IsNullOrWhiteSpace(analyzedFileHash))
            {
                footerStatus.Text = "Select an experiment and analyze a file first.";
                return;
            }

            var finding =
                "Read-only file inspection\n" +
                $"File name: {IOPath.GetFileName(analyzedFilePath)}\n" +
                $"Path: {analyzedFilePath}\n" +
                $"Size: {analyzedFileSize}\n" +
                $"Last modified: {analyzedFileModified}\n" +
                $"SHA-256: {analyzedFileHash}\n" +
                "Limitation: this fingerprint can help detect byte changes, but does not prove source authenticity.";
            if (!string.IsNullOrWhiteSpace(analyzedTextPreview) &&
                !analyzedTextPreview.StartsWith("Preview is not available", StringComparison.Ordinal))
            {
                finding += "\n\nText preview excerpt (may be truncated):\n" + analyzedTextPreview;
            }

            SaveObservation(selectedExperimentId.Value, finding, null, string.Empty);
        };

        ShowModuleView(root, "DIGITAL LAB");
        RefreshEvidencePicker();
        RefreshMetricsAndArchive();
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

    private UIElement BuildGhostTerminalView()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var shellBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(5, 9, 6)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(41, 106, 62)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true
        };
        var shellGrid = new Grid();
        shellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        shellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        shellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        shellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shellGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });

        var titleBar = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(13, 23, 15)),
            LastChildFill = true
        };
        var restartButton = new Button
        {
            Content = "RESTART",
            Padding = new Thickness(9, 4, 9, 4),
            FontSize = 8,
            Margin = new Thickness(0, 4, 8, 4),
            Background = new SolidColorBrush(Color.FromRgb(20, 41, 26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(44, 104, 64)),
            Foreground = new SolidColorBrush(Color.FromRgb(143, 255, 177))
        };
        restartButton.Click += DashboardConsoleRestart_Click;
        DockPanel.SetDock(restartButton, Dock.Right);
        titleBar.Children.Add(restartButton);
        titleBar.Children.Add(new TextBlock
        {
            Text = "GHOST SHELL  /  NATIVE POWERSHELL",
            Foreground = new SolidColorBrush(Color.FromRgb(99, 255, 160)),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        });
        shellGrid.Children.Add(titleBar);

        var telemetry = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(7, 16, 9)),
            LastChildFill = false,
            Margin = new Thickness(10, 0, 10, 0)
        };
        _terminalConsoleHostText = new TextBlock
        {
            Text = $"{Environment.MachineName}  //  LOCAL SESSION  //  {Environment.ProcessorCount} CPU THREADS",
            Foreground = new SolidColorBrush(Color.FromRgb(136, 171, 148)),
            FontSize = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        telemetry.Children.Add(_terminalConsoleHostText);
        _terminalConsoleStatusText = new TextBlock
        {
            Text = _dashboardPowerShellProcess is { HasExited: false } ? "CONNECTED" : "STOPPED",
            Foreground = new SolidColorBrush(_dashboardPowerShellProcess is { HasExited: false }
                ? Color.FromRgb(99, 255, 160) : Color.FromRgb(255, 142, 142)),
            FontSize = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        DockPanel.SetDock(_terminalConsoleStatusText, Dock.Right);
        telemetry.Children.Add(_terminalConsoleStatusText);
        Grid.SetRow(telemetry, 1);
        shellGrid.Children.Add(telemetry);

        var quickActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(9, 4, 9, 4)
        };
        foreach (var quickCommand in new[]
        {
            ("LOCATION", "Get-Location"),
            ("PROCESSES", "Get-Process | Select-Object -First 5"),
            ("FILES", "Get-ChildItem -Force | Select-Object -First 8"),
            ("VERSION", "$PSVersionTable")
        })
        {
            var quickButton = new Button
            {
                Content = quickCommand.Item1,
                Tag = quickCommand.Item2,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 8,
                Background = new SolidColorBrush(Color.FromRgb(13, 29, 18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(37, 71, 46)),
                Foreground = new SolidColorBrush(Color.FromRgb(170, 220, 184))
            };
            quickButton.Click += (_, _) =>
            {
                if (_terminalConsoleInput is null)
                    return;
                _terminalConsoleInput.Text = quickCommand.Item2;
                ExecuteDashboardPowerShellCommand(_terminalConsoleInput);
                _terminalConsoleInput.Focus();
            };
            quickActions.Children.Add(quickButton);
        }
        Grid.SetRow(quickActions, 2);
        shellGrid.Children.Add(quickActions);

        _terminalConsoleOutputPanel = new StackPanel { Margin = new Thickness(11, 8, 9, 3) };
        _terminalConsoleScrollViewer = new ScrollViewer
        {
            Content = _terminalConsoleOutputPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(_terminalConsoleScrollViewer, 3);
        shellGrid.Children.Add(_terminalConsoleScrollViewer);

        var promptBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(10, 20, 13)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 87, 56)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(9, 7, 9, 7)
        };
        var promptGrid = new Grid();
        promptGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        promptGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        promptGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _terminalConsolePromptText = new TextBlock
        {
            Text = DashboardConsolePromptText.Text,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(99, 255, 160)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        promptGrid.Children.Add(_terminalConsolePromptText);
        _terminalConsoleInput = new TextBox
        {
            Height = 28,
            Padding = new Thickness(7, 4, 7, 4),
            Background = new SolidColorBrush(Color.FromRgb(5, 9, 6)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(37, 71, 46)),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Color.FromRgb(200, 248, 212)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(99, 255, 160)),
            FontSize = 9,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Runs in the shared native PowerShell session with your Windows account permissions."
        };
        _terminalConsoleInput.PreviewKeyDown += DashboardConsoleInput_KeyDown;
        Grid.SetColumn(_terminalConsoleInput, 1);
        promptGrid.Children.Add(_terminalConsoleInput);
        var runButton = new Button
        {
            Content = "RUN  ↵",
            Padding = new Thickness(9, 5, 9, 5),
            FontSize = 8,
            Margin = new Thickness(6, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(20, 41, 26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(44, 104, 64)),
            Foreground = new SolidColorBrush(Color.FromRgb(143, 255, 177))
        };
        runButton.Click += (_, _) =>
        {
            if (_terminalConsoleInput is not null)
                ExecuteDashboardPowerShellCommand(_terminalConsoleInput);
        };
        Grid.SetColumn(runButton, 2);
        promptGrid.Children.Add(runButton);
        promptBorder.Child = promptGrid;
        Grid.SetRow(promptBorder, 4);
        shellGrid.Children.Add(promptBorder);

        var footer = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(7, 16, 9)),
            Margin = new Thickness(10, 0, 10, 0)
        };
        footer.Children.Add(new TextBlock
        {
            Text = "GHOST COMMANDS PREFIXED  //  POWERSHELL RUNS AS YOUR WINDOWS USER",
            FontSize = 7,
            Foreground = new SolidColorBrush(Color.FromRgb(111, 157, 123)),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetRow(footer, 5);
        shellGrid.Children.Add(footer);
        shellBorder.Child = shellGrid;
        Grid.SetRow(shellBorder, 0);
        root.Children.Add(shellBorder);

        SetGhostConsoleHost($"{Environment.MachineName}  //  LOCAL SESSION  //  {Environment.ProcessorCount} CPU THREADS");
        SetGhostConsolePrompt(DashboardConsolePromptText.Text);
        if (_dashboardPowerShellProcess is { HasExited: false })
            SetGhostConsoleStatus("CONNECTED");
        else
            SetGhostConsoleStatus("STOPPED", Color.FromRgb(255, 142, 142));

        ReplayConsoleTranscript(_terminalConsoleOutputPanel, _terminalConsoleScrollViewer);
        return root;
    }

    private void Terminal_Click(object sender, RoutedEventArgs e)
    {
        var panel = BuildGhostTerminalView();
        ShowModuleView(panel, "GHOST TERMINAL");
        Dispatcher.BeginInvoke(new Action(() => _terminalConsoleInput?.Focus()));
    }

    private IReadOnlyList<GhostCommandTemplate> GetGhostCommandTemplates() =>
    [
        new("sysinfo", "Windows and device details",
            "Get-ComputerInfo | Select-Object WindowsProductName, OsArchitecture, CsName"),
        new("processes", "Top 10 running processes",
            "Get-Process | Sort-Object CPU -Descending | Select-Object -First 10 Name, Id, CPU"),
        new("ipconfig", "Network adapter configuration",
            "Get-NetIPConfiguration | Select-Object InterfaceAlias, IPv4Address, IPv4DefaultGateway"),
        new("diskspace", "Available disk space",
            "Get-PSDrive -PSProvider FileSystem | Select-Object Name, @{Name='UsedGB';Expression={[math]::Round($_.Used / 1GB, 2)}}, @{Name='FreeGB';Expression={[math]::Round($_.Free / 1GB, 2)}}"),
        new("uptime", "Last boot and uptime",
            "Get-CimInstance Win32_OperatingSystem | Select-Object CSName, @{Name='LastBoot';Expression={$_.LastBootUpTime}}, @{Name='Uptime';Expression={(Get-Date) - $_.LastBootUpTime}}"),
        new("services", "A sample of running Windows services",
            "Get-Service | Where-Object Status -eq 'Running' | Select-Object -First 15 Name, Status"),
        new("policy", "PowerShell execution policy by scope",
            "Get-ExecutionPolicy -List"),
        new("history", "Recent commands in this PowerShell session",
            "Get-History | Select-Object -Last 15")
    ];

    private void LoadPowerShellHistory()
    {
        try
        {
            if (File.Exists(PowerShellHistoryPath))
            {
                _dashboardConsoleHistory.Clear();
                _dashboardConsoleHistory.AddRange(File.ReadAllLines(PowerShellHistoryPath)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .TakeLast(500));
            }
        }
        catch (Exception ex)
        {
            AddDashboardConsoleLine($"PowerShell history could not be loaded: {ex.Message}",
                Color.FromRgb(255, 177, 107));
        }

        _dashboardConsoleHistoryIndex = _dashboardConsoleHistory.Count;
    }

    private void SavePowerShellHistory()
    {
        try
        {
            Directory.CreateDirectory(GetGhostDataDirectory());
            File.WriteAllLines(PowerShellHistoryPath, _dashboardConsoleHistory.TakeLast(500));
        }
        catch (Exception ex)
        {
            AddDashboardConsoleLine($"PowerShell history could not be saved: {ex.Message}",
                Color.FromRgb(255, 177, 107));
        }
    }

    private void ExecuteGhostPowerShellTemplate(string commandName)
    {
        var template = GetGhostCommandTemplates().FirstOrDefault(x =>
            string.Equals(x.Name, commandName, StringComparison.OrdinalIgnoreCase));
        if (template is null)
        {
            AddDashboardConsoleLine($"Unknown built-in system command: {commandName}. Type 'ghost help'.",
                Color.FromRgb(255, 142, 142), true);
            return;
        }

        var process = _dashboardPowerShellProcess;
        if (process is null || process.HasExited)
        {
            AddDashboardConsoleLine("PowerShell is not running. Press RESTART to reconnect.",
                Color.FromRgb(255, 177, 107), true);
            return;
        }

        try
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(template.Script));
            var invocation = $"Invoke-Expression ([System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{encoded}')))";
            process.StandardInput.WriteLine(invocation);
            process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            AddDashboardConsoleLine($"Could not run 'ghost {template.Name}': {ex.Message}",
                Color.FromRgb(255, 142, 142), true);
        }
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


    private sealed record ConsoleTranscriptEntry(string Text, Color Color, bool IsBold);
    private sealed record GhostCommandTemplate(string Name, string Description, string Script);

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
        string Results,
        string Hypothesis,
        string Procedure,
        int ObservationCount,
        string SearchText)
    {
        public string MetaText =>
            $"{Category}  •  {Status.ToUpperInvariant()}  •  {CreatedAt}  •  {ObservationCount} observation(s)";

        public string ObjectivePreview
        {
            get
            {
                var objective = Hypothesis?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(objective))
                    return "No objective or question recorded yet.";
                return objective.Length > 110 ? objective[..107] + "…" : objective;
            }
        }
    }

    private sealed record ObservationArchiveRow(
        int Id,
        int ExperimentId,
        string Description,
        double? NumericValue,
        string Unit,
        string RecordedAtText)
    {
        public string DetailsText => NumericValue.HasValue
            ? $"{Description}\nMeasurement: {NumericValue.Value.ToString(CultureInfo.InvariantCulture)} {Unit}".TrimEnd()
            : Description;
    }

    private sealed record EvidenceOption(
        int Id,
        int CaseFileId,
        string Title,
        string Sha256,
        string RelativePath,
        bool IsArchived)
    {
        public string DisplayText =>
            $"{Title}  •  SHA-256 {(string.IsNullOrWhiteSpace(Sha256) ? "not recorded" : Sha256[..Math.Min(12, Sha256.Length)] + "…")}";
    }

    private sealed record CaseDeletionSummary(
        int CaseId,
        string CaseNumber,
        string Title,
        int EvidenceCount,
        int ExperimentCount,
        int ObservationCount,
        int TimelineEventCount,
        int RelationshipCount);

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
