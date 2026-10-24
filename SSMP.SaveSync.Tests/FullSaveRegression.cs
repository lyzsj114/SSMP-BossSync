using System.Reflection;
using Newtonsoft.Json;
using SSMP.Game.Client.Save;
using SSMP.Game.Server.Save;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Data;
using SSMP.Util;

internal static class FullSaveRegression {
    internal static void Run(Action<bool, string> check, string? fixturePath) {
        var host = new PlayerData();
        var fields = typeof(PlayerData).GetFields().Where(f => !f.IsStatic &&
            SaveDataMapping.Instance.PlayerDataVarProperties.TryGetValue(f.Name, out var p) && p.Sync).ToArray();
        var encode = typeof(SaveManager).GetMethod("EncodeSaveDataValue", BindingFlags.Static | BindingFlags.NonPublic)!;
        var decode = typeof(SaveManager).GetMethod("DecodeSaveDataValue", BindingFlags.Static | BindingFlags.NonPublic)!;
        var hostBytes = new Dictionary<ushort, byte[]>();
        var errors = new List<string>();
        // Keep high bits set: converting a truncated Int32 to UInt64 would hide the crash but lose progression.
        const ulong highBits = 0xFEDCBA9876543210UL;
        foreach (var field in fields) {
            if (field.FieldType == typeof(ulong)) field.SetValue(host, highBits);
            try {
                var bytes = (byte[])encode.Invoke(null, [field.Name, field.GetValue(host)])!;
                hostBytes[SaveDataMapping.Instance.PlayerDataIndices[field.Name]] = bytes;
                var decoded = decode.Invoke(null, [field.Name, bytes]);
                if (decoded != null && !field.FieldType.IsInstanceOfType(decoded))
                    errors.Add(field.Name + ": " + decoded.GetType().Name + " -> " + field.FieldType.Name);
                if (field.FieldType == typeof(ulong) && !Equals(decoded, highBits))
                    errors.Add(field.Name + ": 64-bit value changed");
            } catch (Exception e) { errors.Add(field.Name + ": " + (e.InnerException ?? e).Message); }
        }
        if (errors.Count > 0) Console.WriteLine("FULL SAVE AUDIT:\n" + string.Join("\n", errors));
        check(errors.Count == 0, "Every enabled native PlayerData field encodes and decodes to its actual game type (" + fields.Length + " fields)");
        var current = new CurrentSave { SaveData = hostBytes };
        var packet = new Packet(); current.WriteData(packet);
        var wireCopy = new CurrentSave(); wireCopy.ReadData(new Packet(packet.ToArray()));
        var manager = new SaveManager(null!, null!);
        var native = manager.CreateSaveGameData(wireCopy, 3);
        check(native.playerData.profileID == 3, "Full native snapshot installs after wire round trip in chosen local slot");
        foreach (var field in fields.Where(f => f.FieldType == typeof(ulong)))
            check(Equals(field.GetValue(native.playerData), highBits), "Full snapshot preserves high memory-orb bits: " + field.Name);
        var legacyJson = "{\"playerSaveData\":{\"legacy\":{\"playerData\":{\"memoryOrbs_Clover_18_A\":-1}}}}";
        var migrated = JsonConvert.DeserializeObject<ModSaveFile>(legacyJson)!.ToServerSaveData();
        var migratedNative = manager.CreateSaveGameData(migrated.GetCurrentSaveData("legacy"), 3);
        check(migratedNative.playerData.memoryOrbs_Clover_18_A == uint.MaxValue,
            "Negative legacy bitmap migrates as low 32 bits without sign extension");
        var server = new ServerSaveData(); server.PlayerSaveData["full-save-fixture"] = hostBytes;
        var json = JsonConvert.SerializeObject(ModSaveFile.FromServerSaveData(server));
        if (fixturePath != null) File.WriteAllText(fixturePath, JsonConvert.SerializeObject(SSMPServer.ConsoleSaveFile.FromServerSaveData(server)));
        var reopened = JsonConvert.DeserializeObject<ModSaveFile>(json)!.ToServerSaveData();
        var restoredBytes = reopened.PlayerSaveData["full-save-fixture"];
        var lostOrChanged = hostBytes.Where(p => !restoredBytes.TryGetValue(p.Key, out var restored) || !p.Value.SequenceEqual(restored)).ToList();
        if (lostOrChanged.Count > 0) Console.WriteLine("JSON changed fields: " + string.Join(", ", lostOrChanged.Select(p => SaveDataMapping.Instance.PlayerDataIndices[p.Key])));
        check(lostOrChanged.Count == 0, "All enabled full-save fields survive server JSON round trip, including null strings");
        var reopenedNative = manager.CreateSaveGameData(reopened.GetCurrentSaveData("full-save-fixture"), 3);
        check(fields.Where(f => f.FieldType == typeof(ulong)).All(f => Equals(f.GetValue(reopenedNative.playerData), highBits)),
            "Full server save JSON reload preserves all UInt64 high bits");
        foreach (var field in fields.Where(f => f.FieldType == typeof(ulong))) {
            var normalized = EncodeUtil.EncodeSaveDataValue(EncodeUtil.DecodeSaveDataValue(field.Name, hostBytes[SaveDataMapping.Instance.PlayerDataIndices[field.Name]]), field.Name);
            check(normalized.SequenceEqual(hostBytes[SaveDataMapping.Instance.PlayerDataIndices[field.Name]]),
                "Server normalization preserves UInt64 bytes: " + field.Name);
        }
    }
}
