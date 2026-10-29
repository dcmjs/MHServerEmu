using System.Globalization;
using System.Runtime.InteropServices;
using MHServerEmu.Commands;
using MHServerEmu.Core.Config;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Logging.Targets;
using MHServerEmu.Core.Metrics;
using MHServerEmu.Core.Network;
using MHServerEmu.DatabaseAccess;
using MHServerEmu.DatabaseAccess.Json;
using MHServerEmu.DatabaseAccess.SQLite;
using MHServerEmu.Frontend;
using MHServerEmu.Games;
using MHServerEmu.Games.Common;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.LiveTuning;
using MHServerEmu.Games.MTXStore;
using MHServerEmu.Games.Network.InstanceManagement;
using MHServerEmu.Grouping;
using MHServerEmu.Leaderboards;
using MHServerEmu.PlayerManagement;
using MHServerEmu.WebFrontend;

namespace MHServerEmu
{
#if OS_WINDOWS
    // Default precision for thread sleep timing on Windows is 1000/64=15.625 ms.
    // This is not precise enough for our needs, so we request higher resolution timing.
    // According to MS docs, this should be per-process as of Windows 10 2004.
    // https://learn.microsoft.com/en-us/windows/win32/api/timeapi/nf-timeapi-timebeginperiod
    // Also see this for more context:
    // https://randomascii.wordpress.com/2020/10/04/windows-timer-resolution-the-great-rule-change/
    internal static class WinMM
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        public static extern int TimeBeginPeriod(int uPeriod);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        public static extern int TimeEndPeriod(int uPeriod);
    }
#endif

    public class ServerApp
    {
        private enum State
        {
            Created,
            Running,
            Shutdown,
        }

#if DEBUG
        public const string BuildConfiguration = "Debug";
#elif RELEASE
        public const string BuildConfiguration = "Release";
#endif

        public static readonly string VersionInfo = $"Version {AssemblyHelper.GetAssemblyInformationalVersion()} | {AssemblyHelper.ParseAssemblyBuildTime():yyyy.MM.dd HH:mm:ss} UTC | {BuildConfiguration} | Game Version {Game.Version}";

        private static readonly Logger Logger = LogManager.CreateLogger();
        private State _state = State.Created;
        private int _isShuttingDown;
        private readonly ManualResetEventSlim _shutdownEvent = new(false);
        private PosixSignalRegistration[] _signalRegistrations;

        public static ServerApp Instance { get; } = new();
        public DateTime StartupTime { get; private set; }

        private ServerApp() { }

        public void Run()
        {
            // Prevent duplicate runs
            if (_state != State.Created)
                throw new InvalidOperationException();
            _state = State.Running;

#if OS_WINDOWS
            WinMM.TimeBeginPeriod(1);
#endif

            StartupTime = DateTime.Now;
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            Console.ForegroundColor = ConsoleColor.Yellow;
            PrintBanner();
            PrintVersionInfo();
            Console.ResetColor();

            // Init loggers before anything else
            InitLoggers();

            Logger.Info("MHServerEmu starting...");

            // Make sure the MetricsManager is instantiated before we do anything else.
            MetricsManager.Instance.Initialize();

            // Our encoding is not going to work unless we are running on a little-endian system
            if (BitConverter.IsLittleEndian == false)
            {
                Logger.Fatal("This computer's architecture uses big-endian byte order, which is not compatible with MHServerEmu.");
                Console.ReadLine();
                return;
            }

            // Initialize everything else and start the servers
            if (InitSystems() == false)
            {
                Console.ReadLine();
                return;
            }

            // Initialize the command system
            CommandManager.Instance.SetClientOutput(new FrontendClientChatOutput());
            ICommandParser.Instance = new CommandParser();

            // Create and register game services
            ServerManager serverManager = ServerManager.Instance;
            serverManager.Initialize();

            serverManager.RegisterGameService(new GameInstanceService(), GameServiceType.GameInstance);
            serverManager.RegisterGameService(new LeaderboardService(), GameServiceType.Leaderboard);
            serverManager.RegisterGameService(new PlayerManagerService(), GameServiceType.PlayerManager);
            serverManager.RegisterGameService(new GroupingManagerService(), GameServiceType.GroupingManager);
            serverManager.RegisterGameService(new FrontendServer(), GameServiceType.Frontend);
            serverManager.RegisterGameService(new WebFrontendService(), GameServiceType.WebFrontend);

            serverManager.RunServices();

            // We can't set Live Tuning event message on the Grouping Manager until after we initialize it, so do it here I guess.
            LiveTuningEventScheduler.Instance.SendEventMessageTextToGroupingManager();

            // Shut down cleanly when the process is asked to stop (Ctrl+C, kill, service manager, etc.) so that player data gets saved.
            _signalRegistrations =
            [
                PosixSignalRegistration.Create(PosixSignal.SIGINT, OnShutdownSignal),
                PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnShutdownSignal),
            ];

            // Begin processing console input. This is done on a background thread so that we
            // can exit as soon as the server shuts down without waiting for another line of input.
            Logger.Info("Type '!commands' for a list of available commands");
            Thread consoleThread = new(ProcessConsoleInput) { Name = "Console Input", IsBackground = true, CurrentCulture = CultureInfo.InvariantCulture };
            consoleThread.Start();

            _shutdownEvent.Wait();

            foreach (PosixSignalRegistration registration in _signalRegistrations)
                registration.Dispose();

#if OS_WINDOWS
            // Technically this isn't really needed, but MS docs say we should call it.
            WinMM.TimeEndPeriod(1);
#endif
        }

        /// <summary>
        /// Shuts down all services and exits the application.
        /// </summary>
        public void Shutdown()
        {
            // Shutdown can be requested from multiple sources (commands, signals), but it should happen only once.
            if (Interlocked.Exchange(ref _isShuttingDown, 1) != 0)
                return;

            ServerManager.Instance.ShutdownServices();
            _state = State.Shutdown;
            _shutdownEvent.Set();
        }

        /// <summary>
        /// Reads and executes commands from console input until the server shuts down.
        /// </summary>
        private void ProcessConsoleInput()
        {
            while (_state == State.Running)
            {
                string input = Console.ReadLine();
                if (_state != State.Running)
                    break;

                // Running the server in a non-interactive container without an stdin can spam the console with null inputs.
                if (input == null)
                {
                    Thread.Sleep(1);
                    continue;
                }

                CommandManager.Instance.TryParse(input);
            }
        }

        /// <summary>
        /// Handles SIGINT and SIGTERM by shutting down the server.
        /// </summary>
        private void OnShutdownSignal(PosixSignalContext context)
        {
            // If we are already shutting down, let the signal terminate the process as usual (e.g. when Ctrl+C is pressed twice).
            if (Volatile.Read(ref _isShuttingDown) != 0)
                return;

            Logger.Info($"Received {context.Signal}, shutting down...");
            context.Cancel = true;
            Task.Run(Shutdown);
        }

        /// <summary>
        /// Prints a fancy ASCII banner to console.
        /// </summary>
        private void PrintBanner()
        {
            Console.WriteLine(@"  __  __ _    _  _____                          ______                 ");
            Console.WriteLine(@" |  \/  | |  | |/ ____|                        |  ____|                ");
            Console.WriteLine(@" | \  / | |__| | (___   ___ _ ____   _____ _ __| |__   _ __ ___  _   _ ");
            Console.WriteLine(@" | |\/| |  __  |\___ \ / _ \ '__\ \ / / _ \ '__|  __| | '_ ` _ \| | | |");
            Console.WriteLine(@" | |  | | |  | |____) |  __/ |   \ V /  __/ |  | |____| | | | | | |_| |");
            Console.WriteLine(@" |_|  |_|_|  |_|_____/ \___|_|    \_/ \___|_|  |______|_| |_| |_|\__,_|");
            Console.WriteLine();
        }

        /// <summary>
        /// Prints formatted version info to console.
        /// </summary>
        private void PrintVersionInfo()
        {
            Console.WriteLine($" {VersionInfo}");
            Console.WriteLine();
        }

        /// <summary>
        /// Handles unhandled exceptions.
        /// </summary>
        private void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception;

            if (e.IsTerminating)
            {
                DateTime now = DateTime.Now;

                string crashReportDir = Path.Combine(FileHelper.ServerRoot, "CrashReports");
                if (Directory.Exists(crashReportDir) == false)
                    Directory.CreateDirectory(crashReportDir);

                string crashReportFilePath = Path.Combine(crashReportDir, $"ServerCrash_{now.ToString(FileHelper.FileNameDateFormat)}.txt");

                using (StreamWriter writer = new(crashReportFilePath))
                {
                    writer.WriteLine($"{VersionInfo}\n");
                    writer.WriteLine($"Local Server Time: {now:yyyy.MM.dd HH:mm:ss.fff}\n");
                    writer.WriteLine($"Exception:\n{exception}\n");
                    writer.WriteLine($"Server Status:\n{ServerManager.Instance.GetServerStatusString()}\n");
                    writer.WriteLine($"Performance Metrics:\n{MetricsManager.Instance.GeneratePerformanceReport(MetricsReportFormat.PlainText)}\n");
                }

                Logger.FatalException(exception, $"MHServerEmu terminating because of unhandled exception, report saved to {crashReportFilePath}");
                ServerManager.Instance.ShutdownServices();
            }
            else
            {
                Logger.ErrorException(exception, "Caught unhandled exception.");
            }

            Console.ReadLine();
        }

        /// <summary>
        /// Initializes log targets.
        /// </summary>
        private void InitLoggers()
        {
            var config = ConfigManager.Instance.GetConfig<LoggingConfig>();

            LogManager.Enabled = config.EnableLogging;

            // Attach console log target
            if (config.EnableConsole)
            {
                ConsoleTarget target = new(config.GetConsoleSettings());
                LogManager.AttachTarget(target);
            }

            // Attach file log target
            if (config.EnableFile)
            {
                FileTarget target = new(config.GetFileSettings(), $"MHServerEmu_{StartupTime.ToString(FileHelper.FileNameDateFormat)}", config.FileSplitOutput, false);
                LogManager.AttachTarget(target);
            }

            if (config.SynchronousMode)
                Logger.Debug($"Synchronous logging enabled");
        }

        /// <summary>
        /// Initializes systems needed to run the servers.
        /// </summary>
        private bool InitSystems()
        {
            // JsonDBManager saves a single account in a JSON file
            var config = ConfigManager.Instance.GetConfig<PlayerManagerConfig>();
            IDBManager.Instance = config.UseJsonDBManager ? JsonDBManager.Instance : SQLiteDBManager.Instance;

            // LiveTuningManager uses data from LiveTuningEventScheduler initialization,
            // and LiveTuningEventScheduler needs GameDatabase to be initialized to get TimeZone from GlobalsPrototype.
            return PakFileSystem.Instance.Initialize()
                && ProtocolDispatchTable.Instance.Initialize()
                && GameDatabase.IsInitialized
                && LiveTuningEventScheduler.Instance.Initialize()
                && LiveTuningManager.Instance.Initialize()
                && CatalogManager.Instance.Initialize()
                && IDBManager.Instance.Initialize();
        }
    }
}
