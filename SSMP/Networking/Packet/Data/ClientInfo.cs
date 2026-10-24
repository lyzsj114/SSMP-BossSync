using System;
using System.Collections.Generic;
using SSMP.Api.Addon;

namespace SSMP.Networking.Packet.Data;

/// <summary>
/// Packet data for the client info data.
/// </summary>
internal class ClientInfo : IPacketData {
    internal const string SaveSyncMarker = "SSMP.SaveSync";
    internal const string SaveSyncProtocol = "2";
    public string? SaveSyncVersion { get; private set; }
    /// <inheritdoc />
    public bool IsReliable => true;

    /// <inheritdoc />
    public bool DropReliableDataIfNewerExists => false;

    /// <summary>
    /// The username of the client.
    /// </summary>
    public string Username { get; set; } = null!;

    /// <summary>
    /// The authentication key of the client.
    /// </summary>
    public string AuthKey { get; set; } = null!;

    /// <summary>
    /// A list of addon data of the client.
    /// </summary>
    public List<AddonData>? AddonData { get; set; }

    /// <inheritdoc />
    public void WriteData(IPacket packet) {
        packet.Write(Username);
        packet.Write(AuthKey);

        AddonData ??= [];

        if (AddonData.Count >= byte.MaxValue) throw new InvalidOperationException("Too many addons");
        var addonDataLength = (byte) (AddonData.Count + 1);

        packet.Write(addonDataLength);

        for (var i = 0; i < AddonData.Count; i++) {
            packet.Write(AddonData[i].Identifier);
            packet.Write(AddonData[i].Version);
        }
        // Old servers reject this unknown capability as an addon instead of silently corrupting saves.
        packet.Write(SaveSyncMarker);
        packet.Write(SaveSyncProtocol);
    }

    /// <inheritdoc />
    public void ReadData(IPacket packet) {
        Username = packet.ReadString();
        AuthKey = packet.ReadString();

        var addonDataLength = packet.ReadByte();

        AddonData = new List<AddonData>();
        SaveSyncVersion = null;

        for (var i = 0; i < addonDataLength; i++) {
            var id = packet.ReadString();
            var version = packet.ReadString();

            if (id.Length > Addon.MaxNameLength || version.Length > Addon.MaxVersionLength) {
                throw new ArgumentException("Identifier or version of addon exceeds max length");
            }

            if (id == SaveSyncMarker) {
                if (SaveSyncVersion != null) throw new ArgumentException("Duplicate save protocol marker");
                SaveSyncVersion = version;
            } else {
                AddonData.Add(new AddonData(id, version));
            }
        }
    }
}

/// <summary>
/// Addon data class denoting identifying information about an addon.
/// </summary>
internal class AddonData : IEquatable<AddonData> {
    /// <summary>
    /// The identifier of the addon (aka name).
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// The version of the addon.
    /// </summary>
    public string Version { get; }

    public AddonData(string identifier, string version) {
        Identifier = identifier;
        Version = version;
    }

    /// <inheritdoc/>
    public bool Equals(AddonData other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Identifier == other.Identifier && Version == other.Version;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) {
        if (ReferenceEquals(null, obj)) {
            return false;
        }

        if (ReferenceEquals(this, obj)) {
            return true;
        }

        if (obj.GetType() != GetType()) {
            return false;
        }

        return Equals((AddonData) obj);
    }

    /// <inheritdoc/>
    public override int GetHashCode() {
        unchecked {
            return Identifier.GetHashCode() * 397 ^ Version.GetHashCode();
        }
    }
}
