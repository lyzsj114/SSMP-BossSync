using SSMP.Networking;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Data;
using SSMP.Networking.Packet.Update;
using SSMP.Networking.Client;
using SSMP.Util;
using System.Reflection;

internal static class PacketDispatchRegression {
    internal static void Run(Action<bool, string> check) {
        var dispatch = new QueueDispatcher();
        var registry = new PacketHandlerRegistry<int, Action>("test update", dispatch);
        var calls = 0;
        // The login callback was already queued when an update arrived on the network thread.
        dispatch.Dispatch(() => registry.Register(1, () => calls++));
        registry.Execute(1, callback => callback());
        dispatch.Drain();
        check(calls == 1, "Early PlayerUpdate resolves handler after queued login registration");
        registry.Execute(1, callback => callback());
        registry.Deregister(1);
        dispatch.Drain();
        check(calls == 1, "Disconnect cancels an already queued update");
        registry.Register(1, () => calls++);
        registry.Execute(1, callback => callback());
        registry.Deregister(1);
        registry.Register(1, () => calls += 100);
        dispatch.Drain();
        check(calls == 1, "Queued update from old session cannot reach a replacement handler");
        registry.Execute(1, callback => callback());
        dispatch.Drain();
        check(calls == 101, "New session updates dispatch normally after reconnect");
        CheckRealConnectionQueue(check);
    }

    private static void CheckRealConnectionQueue(Action<bool, string> check) {
        var mainQueue = (List<Action>)typeof(ThreadUtil).GetField("ActionsToRun", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        void Drain() {
            while (mainQueue.Count > 0) {
                var work = mainQueue.ToArray(); mainQueue.Clear();
                foreach (var action in work) action();
            }
        }
        var packets = new PacketManager();
        using var client = new NetClient(packets);
        var status = typeof(NetClient).GetProperty(nameof(NetClient.ConnectionStatus))!;
        var accept = typeof(NetClient).GetMethod("OnServerInfoReceived", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var connectCalls = 0;
        var updateCalls = 0;
        client.ConnectEvent += _ => {
            connectCalls++;
            packets.RegisterClientUpdatePacketHandler<PlayerUpdate>(ClientUpdatePacketId.PlayerUpdate, _ => updateCalls++);
        };
        status.SetValue(client, ClientConnectionStatus.Connecting);
        accept.Invoke(client, [new ServerInfo { ConnectionResult = ServerConnectionResult.Accepted }]);
        check(client.IsConnected && connectCalls == 0, "Accepted connection queues initialization before processing updates");
        var update = new ClientUpdatePacket();
        update.GetPacketData()[ClientUpdatePacketId.PlayerUpdate] = new PlayerUpdate();
        packets.HandleClientUpdatePacket(update);
        Drain();
        check(connectCalls == 1 && updateCalls == 1, "Actual NetClient login queue registers before first PlayerUpdate dispatch");
        client.Disconnect(); Drain();
        packets.DeregisterClientUpdatePacketHandler(ClientUpdatePacketId.PlayerUpdate);
        status.SetValue(client, ClientConnectionStatus.Connecting);
        accept.Invoke(client, [new ServerInfo { ConnectionResult = ServerConnectionResult.Accepted }]);
        client.Disconnect(); Drain();
        check(connectCalls == 1, "Cancelled accepted connection does not run stale login initialization");
    }

    private sealed class QueueDispatcher : IPacketHandlerRegistryDispatcher {
        private readonly Queue<Action> actions = new();
        public void Dispatch(Action action) => actions.Enqueue(action);
        internal void Drain() { while (actions.Count > 0) actions.Dequeue()(); }
    }
}
