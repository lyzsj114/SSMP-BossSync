using SSMP.Api.Command.Server;
using SSMP.Game.Server;
using SSMP.Game.Settings;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Data;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;
using SSMPServer.Command;
using SSMPServer.Logging;
using SSMP.Game.Server.Save;
using SSMP.Util;
using SSMP.Logging;
using Newtonsoft.Json;

namespace SSMPServer;

/// <summary>
/// Specialization of the server manager for the console program.
/// </summary>
internal class ConsoleServerManager : ServerManager {
    private readonly object _saveFileLock = new();
    private string? _saveFilePath;
    private Timer? _saveTimer;

    /// <summary>
    /// The exit command for exiting the server.
    /// </summary>
    private readonly IServerCommand _exitCommand;
    /// <summary>
    /// The console settings command for changing console settings.
    /// </summary>
    private readonly IServerCommand _consoleSettingsCommand;
    /// <summary>
    /// The log command for changing log levels.
    /// </summary>
    private readonly IServerCommand _logCommand;

    public ConsoleServerManager(
        NetServer netServer,
        PacketManager packetManager,
        ServerSettings serverSettings,
        ConsoleLogger consoleLogger
    ) : base(netServer, packetManager, serverSettings) {
        _exitCommand = new ExitCommand(this);
        _consoleSettingsCommand = new ConsoleSettingsCommand(this, InternalServerSettings);
        _logCommand = new LogCommand(consoleLogger);
    }

    /// <inheritdoc />
    public override void Initialize() {
        base.Initialize();

        // Start loading addons
        AddonManager.LoadAddons();
        ServerShutdownEvent += SaveOnShutdown;

        // Register a callback for when the application is closed to stop the server
        AppDomain.CurrentDomain.ProcessExit += (_, _) => {
            if (Environment.ExitCode == 5) {
                return;
            }

            Stop();
        };
    }

    /// <summary>
    /// Starts a server with the given port.
    /// </summary>
    /// <param name="port">The port the server should run on.</param>
    /// <param name="fullSynchronisation">Whether full synchronisation should be enabled.</param>
    /// <param name="transportServer">The transport server to use.</param>
    public override void Start(int port, bool fullSynchronisation, IEncryptedTransportServer transportServer) {
        if (fullSynchronisation) InitializeSaveFile();
        base.Start(port, fullSynchronisation, transportServer);
        if (fullSynchronisation) {
            _saveTimer = new Timer(_ => WriteToSaveFile(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
    }

    /// <inheritdoc />
    protected override void RegisterCommands() {
        base.RegisterCommands();

        CommandManager.RegisterCommand(_exitCommand);
        CommandManager.RegisterCommand(_consoleSettingsCommand);
        CommandManager.RegisterCommand(_logCommand);
    }

    /// <inheritdoc />
    protected override void DeregisterCommands() {
        base.DeregisterCommands();
        
        CommandManager.DeregisterCommand(_exitCommand);
        CommandManager.DeregisterCommand(_consoleSettingsCommand);
        CommandManager.DeregisterCommand(_logCommand);
    }

    private void SaveOnShutdown() {
        _saveTimer?.Dispose();
        _saveTimer = null;
        WriteToSaveFile();
    }

    /// <inheritdoc />
    protected override void OnSaveUpdate(ushort id, SaveUpdate packet) {
        base.OnSaveUpdate(id, packet);
    }

    // Load before opening the listener so a reconnect cannot receive an empty world.
    private void InitializeSaveFile() {
        _saveFilePath = Path.Combine(FileUtil.GetCurrentPath(), "save.json");
        if (File.Exists(_saveFilePath)) {
            // Fail startup on an unreadable save instead of replacing it with a fresh world.
            var saved = JsonConvert.DeserializeObject<ConsoleSaveFile>(File.ReadAllText(_saveFilePath))
                ?? throw new InvalidDataException("The standalone save.json is empty or invalid.");
            var restored = saved.ToServerSaveData();
            lock (ServerSaveData) {
                ServerSaveData.GlobalSaveData = restored.GlobalSaveData;
                ServerSaveData.PlayerSaveData = restored.PlayerSaveData;
            }
            Logger.Info("Loaded standalone server save.json");
        } else {
            WriteToSaveFile();
        }
    }

    private void WriteToSaveFile() {
        if (_saveFilePath == null) return;
        lock (_saveFileLock) {
            try {
                var snapshot = ConsoleSaveFile.FromServerSaveData(ServerSaveData);
                var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
                var temporaryPath = _saveFilePath + ".tmp";
                File.WriteAllText(temporaryPath, json);
                if (File.Exists(_saveFilePath)) File.Replace(temporaryPath, _saveFilePath, _saveFilePath + ".bak");
                else File.Move(temporaryPath, _saveFilePath);
            } catch (Exception e) {
                Logger.Error($"Could not write standalone save.json: {e}");
            }
        }
    }
}
