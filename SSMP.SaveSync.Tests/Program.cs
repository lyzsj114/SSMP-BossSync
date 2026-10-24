using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SSMP.Game.Client.Save;
using SSMP.Game.Server.Save;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Data;
using SSMP.Serialization;
using SSMP.Util;

var passed = 0;
void Check(bool condition, string name) {
    if (!condition) throw new Exception("FAIL: " + name);
    passed++; Console.WriteLine("PASS: " + name);
}
ushort Index(string name) => SaveDataMapping.Instance.PlayerDataIndices[name];
object RoundTrip(string name, object data, Type type) => CompoundSaveCodec.ToGameValue(
    (JToken) EncodeUtil.DecodeSaveDataValue(name, EncodeUtil.EncodeSaveDataValue(data, name))!, type)!;

var tools = new ToolItemsData();
tools.SetData("Straight Pin", new ToolItemsData.Data { IsUnlocked = true, AmountLeft = 17, HasBeenSeen = true });
tools.SetData("Saw", new ToolItemsData.Data { IsUnlocked = true, AmountLeft = 4 });
var toolsCopy = (ToolItemsData) RoundTrip("Tools", tools, typeof(ToolItemsData));
Check(toolsCopy.GetData("Straight Pin").IsUnlocked && toolsCopy.GetData("Straight Pin").AmountLeft == 17, "Tool ownership and ammo survive game callbacks / wire round trip");
Check(toolsCopy.GetValidNames().Count == 2, "RuntimeData rebuilt from savedData");
toolsCopy.SetData("Saw", new ToolItemsData.Data { IsUnlocked = true, AmountLeft = 99 });
Check(tools.GetData("Saw").AmountLeft == 4, "Snapshots are deep copies");
var before = EncodeUtil.EncodeSaveDataValue(tools, "Tools");
tools.SetData("Needle", new ToolItemsData.Data { IsUnlocked = true });
Check(!before.SequenceEqual(EncodeUtil.EncodeSaveDataValue(tools, "Tools")), "In-place inventory mutation changes network snapshot");
var crests = new ToolCrestsData();
crests.SetData("Hunter", new ToolCrestsData.Data { IsUnlocked = true, Slots = [new() { EquippedTool = "Straight Pin", IsUnlocked = true }] });
var crestsCopy = (ToolCrestsData) RoundTrip("ToolEquips", crests, typeof(ToolCrestsData));
Check(crestsCopy.GetData("Hunter").Slots[0].EquippedTool == "Straight Pin", "Crest and nested equipped slot survive round trip");
var extra = new FloatingCrestSlotsData(); extra.SetData("extra", new() { EquippedTool = "Saw", IsUnlocked = true });
Check(((FloatingCrestSlotsData) RoundTrip("ExtraToolEquips", extra, typeof(FloatingCrestSlotsData))).GetData("extra").IsUnlocked, "Extra equipment slot survives round trip");
var liquids = new ToolItemLiquidsData(); liquids.SetData("Potion", new() { RefillsLeft = 3, UsedExtra = true });
Check(((ToolItemLiquidsData) RoundTrip("ToolLiquids", liquids, typeof(ToolItemLiquidsData))).GetData("Potion").RefillsLeft == 3, "Tool liquid counts survive round trip");
var collectables = new CollectableItemsData(); collectables.SetData("Pearl", new() { Amount = 8 });
Check(((CollectableItemsData) RoundTrip("Collectables", collectables, typeof(CollectableItemsData))).GetData("Pearl").Amount == 8, "Collectables survive round trip");
Check(((int[]) RoundTrip("CrawbellCurrency", new[] { 10, 20 }, typeof(int[])))[1] == 20, "Array-valued progress survives round trip");

var hostSave = new Dictionary<ushort, byte[]> {
    [Index("Tools")] = EncodeUtil.EncodeSaveDataValue(tools, "Tools"),
    [Index("ToolEquips")] = EncodeUtil.EncodeSaveDataValue(crests, "ToolEquips"),
    [Index("geo")] = EncodeUtil.EncodeSaveDataValue(1234, "geo"),
    [Index("respawnScene")] = EncodeUtil.EncodeSaveDataValue("Bone_01", "respawnScene"),
    [Index("profileID")] = EncodeUtil.EncodeSaveDataValue(9, "profileID")
};
var server = new ServerSaveData { HostAuthKey = "host" };
server.PlayerSaveData["host"] = hostSave;
var current = server.GetCurrentSaveData("fresh-client");
Check(!current.NewForPlayer && current.SaveData.ContainsKey(Index("Tools")), "New client clones host inventory");
Check(!current.SaveData.ContainsKey(Index("profileID")), "Host local save slot never transfers");
Check(!ReferenceEquals(hostSave[Index("Tools")], current.SaveData[Index("Tools")]), "Host and guest save buffers are independent");
server.PlayerSaveData["legacy-client"] = new() { [Index("geo")] = EncodeUtil.EncodeSaveDataValue(50, "geo") };
var legacy = server.GetCurrentSaveData("legacy-client");
Check(legacy.SaveData.ContainsKey(Index("Tools")) && (int)EncodeUtil.DecodeSaveDataValue("geo", legacy.SaveData[Index("geo")])! == 50, "Legacy entry gains missing inventory without replacing existing progress");
var ownedTools = new ToolItemsData(); ownedTools.SetData("Personal", new() { IsUnlocked = true });
server.PlayerSaveData["legacy-client"][Index("Tools")] = EncodeUtil.EncodeSaveDataValue(ownedTools, "Tools");
legacy = server.GetCurrentSaveData("legacy-client");
Check(((JToken)EncodeUtil.DecodeSaveDataValue("Tools", legacy.SaveData[Index("Tools")])!).ToString().Contains("Personal"), "Reconnect retains already-stored personal inventory");
var fileJson = JsonConvert.SerializeObject(ModSaveFile.FromServerSaveData(server));
var reloaded = JsonConvert.DeserializeObject<ModSaveFile>(fileJson)!.ToServerSaveData();
var reloadedTools = (ToolItemsData) CompoundSaveCodec.ToGameValue((JToken)EncodeUtil.DecodeSaveDataValue("Tools", reloaded.PlayerSaveData["fresh-client"][Index("Tools")])!, typeof(ToolItemsData))!;
Check(reloadedTools.GetData("Straight Pin").AmountLeft == 17, "Server modsav JSON restart round trip preserves typed inventory");

var packet = new Packet(); current.WriteData(packet);
var readPacket = new CurrentSave(); readPacket.ReadData(new Packet(packet.ToArray()));
Check(readPacket.SaveData[Index("Tools")].SequenceEqual(current.SaveData[Index("Tools")]), "CurrentSave packet serialization preserves payload");
var manager = new SaveManager(null!, null!);
var nativeSave = manager.CreateSaveGameData(readPacket, 2);
Check(nativeSave.playerData.profileID == 2 && nativeSave.playerData.respawnScene == "Bone_01", "Native load snapshot keeps selected local slot and server respawn scene");
Check(nativeSave.playerData.Tools.GetData("Straight Pin").IsUnlocked, "Native snapshot contains usable tool RuntimeData before ContinueGame");
Check(nativeSave.playerData.ToolEquips.GetData("Hunter").Slots[0].EquippedTool == "Straight Pin", "Native snapshot contains nested equipment before hero initialization");

foreach (var bad in new[] { new byte[0], Encoding.UTF8.GetBytes("{} {}"), new byte[65536], new byte[] { 0xff } }) {
    var rejected = false; try { CompoundSaveCodec.Decode(bad); } catch { rejected = true; }
    Check(rejected, "Malformed or oversized JSON rejected (length " + bad.Length + ")");
}
var oversize = new CurrentSave { SaveData = new() { [Index("Tools")] = new byte[65536] } };
var refused = false; try { oversize.WriteData(new Packet()); } catch { refused = true; }
Check(refused, "Oversized full-save field fails instead of silently truncating");
var infoPacket = new Packet();
new ClientInfo { Username = "tester", AuthKey = "test-key", AddonData = [new AddonData("Example", "1")] }.WriteData(infoPacket);
var infoRead = new ClientInfo(); infoRead.ReadData(new Packet(infoPacket.ToArray()));
Check(infoRead.SaveSyncVersion == ClientInfo.SaveSyncProtocol && infoRead.AddonData!.Count == 1, "Save protocol capability round trip preserves real addon ordering");
var oldPacket = new Packet(); oldPacket.Write("oldclient"); oldPacket.Write("test-key"); oldPacket.Write((byte)0);
var oldInfo = new ClientInfo(); oldInfo.ReadData(new Packet(oldPacket.ToArray()));
Check(oldInfo.SaveSyncVersion != ClientInfo.SaveSyncProtocol, "Old clients are distinguishable before full-sync handshake acceptance");
manager.IsSaveUpdateSendingSuspended = true;
manager.UpdateSaveWithData(new SaveUpdate { SaveDataIndex = Index("geo"), Value = EncodeUtil.EncodeSaveDataValue(4321, "geo") });
var queuedSnapshot = manager.CreateSaveGameData(current, 2);
Check(queuedSnapshot.playerData.geo == 4321, "Packets received during slot selection override the initial snapshot before scene initialization");
manager.CancelInitialSave();
Check(manager.CreateSaveGameData(current, 2).playerData.geo == 1234, "Cancelled connection discards queued packets");
var standaloneJson = JsonConvert.SerializeObject(SSMPServer.ConsoleSaveFile.FromServerSaveData(server));
var standaloneReload = JsonConvert.DeserializeObject<SSMPServer.ConsoleSaveFile>(standaloneJson)!.ToServerSaveData();
Check(standaloneReload.PlayerSaveData["fresh-client"][Index("Tools")].SequenceEqual(server.PlayerSaveData["fresh-client"][Index("Tools")]), "Standalone save.json round trip preserves compound inventory");
var boolKey = SaveDataMapping.Instance.PersistentBoolVarProperties.First(p => p.Value.Sync).Key;
var intKey = SaveDataMapping.Instance.PersistentIntVarProperties.First(p => p.Value.Sync).Key;
var scenePacket = new CurrentSave { SaveData = new() {
    [SaveDataMapping.Instance.PersistentBoolIndices[boolKey]] = [1],
    [SaveDataMapping.Instance.PersistentIntIndices[intKey]] = [255]
} };
var sceneSnapshot = manager.CreateSaveGameData(scenePacket, 2);
Check(sceneSnapshot.sceneData.PersistentBools.TryGetValue(boolKey.SceneName, boolKey.Id, out var boolState) && boolState.Value,
    "Native scene snapshot restores persistent switches before scene loads");
Check(sceneSnapshot.sceneData.PersistentInts.TryGetValue(intKey.SceneName, intKey.Id, out var intState) && intState.Value == -1,
    "Native scene snapshot restores signed persistent state sentinel");
var defaultPd = new PlayerData();
var jsonFieldCount = 0;
foreach (var entry in SaveDataMapping.Instance.PlayerDataVarProperties.Where(p => p.Value.Sync && p.Value.Json)) {
    var field = typeof(PlayerData).GetField(entry.Key!)!;
    var value = field.GetValue(defaultPd);
    var encoded = EncodeUtil.EncodeSaveDataValue(value, entry.Key);
    var restoredValue = RoundTrip(entry.Key!, value!, field.FieldType);
    Check(encoded.SequenceEqual(EncodeUtil.EncodeSaveDataValue(restoredValue, entry.Key)), "Game field schema round trip: " + entry.Key);
    jsonFieldCount++;
}
Check(jsonFieldCount == 17, "All enabled compound schemas covered");
FullSaveRegression.Run(Check, args.Length == 1 ? args[0] : null);
PacketDispatchRegression.Run(Check);
BossSyncRegression.Run(Check);
Console.WriteLine($"ALL {passed} CHECKS PASSED");
