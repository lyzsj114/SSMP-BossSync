using System;
using System.Collections.Generic;
using SSMP.Game.Client.Save;
using SSMP.Logging;
using SSMP.Networking.Packet.Data;

namespace SSMP.Game.Server.Save;

/// <summary>
/// Class that holds save data from a server. This consists of global data relating to the world and individual
/// data specific to each player. This class is only used for storing the save data while the server is running;
/// serialization of this data to the save file is done with <see cref="T:SSMP.Game.Server.Save.ModSaveFile" />.
/// </summary>
internal class ServerSaveData
{
	/// <summary>
	/// Name of the variable in PlayerData that denotes a Steel Soul save file.
	/// </summary>
	private const string SteelSoulVarName = "permadeathMode";

	/// <summary>
	/// The index that corresponds with the Steel Soul variable.
	/// </summary>
	private static readonly ushort SteelSoulIndex;

	/// <summary>
	/// The global save data for the server. E.g. broken walls, open doors, etc.
	/// </summary>
	public Dictionary<ushort, byte[]> GlobalSaveData { get; set; } = new Dictionary<ushort, byte[]>();

	/// <summary>
	/// The player specific save data mapped to player's auth keys.
	/// </summary>
	public Dictionary<string, Dictionary<ushort, byte[]>> PlayerSaveData { get; set; } = new Dictionary<string, Dictionary<ushort, byte[]>>();

	/// <summary>
	/// The auth key of the player hosting this server. Used as the template when a player connects for the first time.
	/// </summary>
	public string? HostAuthKey { get; set; }

	/// <summary>
	/// Static constructor for initializing the indices for the Steel Soul variable.
	/// </summary>
	static ServerSaveData()
	{
		if (!SaveDataMapping.Instance.PlayerDataIndices.TryGetValue("permadeathMode", out SteelSoulIndex))
		{
			Logger.Warn("Could not find index for steel soul variable");
		}
	}

	/// <summary>
	/// Get save data that contains global save data and player specific save data for the player with the given auth
	/// key.
	/// </summary>
	/// <param name="authKey">The auth key that corresponds to the player for the player specific data.</param>
	/// <returns>A dictionary mapping save data indices to byte encoded values.</returns>
    public CurrentSave GetCurrentSaveData(string authKey) {
        lock (this) return GetCurrentSaveDataCore(authKey);
    }

    private CurrentSave GetCurrentSaveDataCore(string authKey)
	{
		CurrentSave currentSave = new CurrentSave();
		if (!PlayerSaveData.TryGetValue(authKey, out Dictionary<ushort, byte[]> value))
		{
			if (TryCloneHostPlayerSaveData(authKey, out value))
			{
				currentSave.NewForPlayer = false;
				Logger.Info("Cloned host player save data for a first-time player");
			}
			else
			{
				value = new Dictionary<ushort, byte[]>();
				currentSave.NewForPlayer = true;
				Logger.Debug("No save data for player yet, marking in CurrentSave");
			}
		}
        // Upgrade legacy entries once: those versions could not store compound inventory fields.
        if (HostAuthKey != null && PlayerSaveData.TryGetValue(HostAuthKey, out var hostSave)) {
            var restored = 0;
            foreach (var entry in hostSave) {
                if (value.ContainsKey(entry.Key)) continue;
                if (SaveDataMapping.Instance.PlayerDataIndices.TryGetValue(entry.Key, out var fieldName) &&
                    SaveDataMapping.Instance.PlayerDataVarProperties.TryGetValue(fieldName, out var props) && props.Sync && props.Json) {
                    value[entry.Key] = (byte[]) entry.Value.Clone();
                    restored++;
                }
            }
            if (restored > 0) Logger.Info($"Restored {restored} missing compound fields in legacy player save from host");
        }
		Dictionary<ushort, byte[]> dictionary = new Dictionary<ushort, byte[]>(GlobalSaveData);
		foreach (KeyValuePair<ushort, byte[]> item in value)
		{
			if (!ShouldSendPlayerSaveData(item.Key))
			{
				Logger.Debug($"Skipping non-synced player save field with index: {item.Key}");
			}
			else
			{
				dictionary[item.Key] = item.Value;
			}
		}
		currentSave.SaveData = dictionary;
		return currentSave;
	}

	/// <summary>
	/// Clone the host's player-specific save data for a player connecting for the first time.
	/// </summary>
	/// <param name="authKey">The auth key of the connecting player.</param>
	/// <param name="playerSaveData">The cloned save data, or an empty dictionary if cloning was not possible.</param>
	/// <returns>Whether the host's save data was cloned.</returns>
	private bool TryCloneHostPlayerSaveData(string authKey, out Dictionary<ushort, byte[]> playerSaveData)
	{
		playerSaveData = new Dictionary<ushort, byte[]>();
		if (string.IsNullOrEmpty(HostAuthKey) || string.Equals(authKey, HostAuthKey, StringComparison.OrdinalIgnoreCase) || !PlayerSaveData.TryGetValue(HostAuthKey, out Dictionary<ushort, byte[]> value) || value.Count == 0)
		{
			return false;
		}
		foreach (KeyValuePair<ushort, byte[]> item in value)
		{
			if (ShouldSendPlayerSaveData(item.Key))
			{
				playerSaveData[item.Key] = (byte[])item.Value.Clone();
			}
		}
		PlayerSaveData[authKey] = playerSaveData;
		return true;
	}

	/// <summary>
	/// Check whether a player-specific save field is still enabled for synchronisation.
	/// </summary>
	private static bool ShouldSendPlayerSaveData(ushort index)
	{
		if (!SaveDataMapping.Instance.PlayerDataIndices.TryGetValue(index, out string value) || value == null || !SaveDataMapping.Instance.PlayerDataVarProperties.TryGetValue(value, out SaveDataMapping.VarProperties value2))
		{
			return true;
		}
		return value2.Sync;
	}

	/// <summary>
	/// Whether the global save data in this instance is for Steel Soul mode.
	/// </summary>
	/// <returns>True if the save data is for Steel Soul mode, false otherwise.</returns>
	public bool IsSteelSoul()
	{
		if (!GlobalSaveData.TryGetValue(SteelSoulIndex, out byte[] value))
		{
			return false;
		}
		if (value.Length != 0)
		{
			return value[0] != 0;
		}
		return false;
	}
}
