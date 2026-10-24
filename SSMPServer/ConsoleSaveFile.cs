using Newtonsoft.Json;
using SSMP.Game.Server.Save;

namespace SSMPServer;

/// <summary>Standalone server save; accepts the legacy global_save_data spelling when reading.</summary>
internal class ConsoleSaveFile : ModSaveFile {
    [JsonProperty("global_save_data")]
    private SaveData LegacyGlobalSaveData { set => GlobalSaveData = value; }

    /// <inheritdoc cref="ModSaveFile.FromServerSaveData"/>
    public new static ConsoleSaveFile FromServerSaveData(ServerSaveData serverSaveData) {
        var snapshot = ModSaveFile.FromServerSaveData(serverSaveData);
        return new ConsoleSaveFile {
            GlobalSaveData = snapshot.GlobalSaveData,
            PlayerSaveData = snapshot.PlayerSaveData
        };
    }
}
