namespace Coophead.Transport
{
    internal enum TransportDisconnectReason : byte
    {
        None = 0,
        Normal = 1,
        RemovePlayer = 2,
    }

    internal interface IInputFrameTransport : System.IDisposable
    {
        string Description { get; }
        string Status { get; }
        bool IsConnected { get; }
        bool PeerDisconnected { get; }
        TransportDisconnectReason PeerDisconnectReason { get; }
        int PingMilliseconds { get; }
        int EstimatedPacketLossPercent { get; }
        void Reset();
        void Update();
        void RequestDisconnect(TransportDisconnectReason reason);
        void Send(InputFrame frame);
        bool TryReceive(uint receiverTick, out InputFrame frame);
        uint SendScene(SceneCommand command);
        bool TryReceiveScene(out SceneCommand command);
        void SendContext(SessionContext context);
        bool TryReceiveContext(out SessionContext context);
        void SendPlayerState(PlayerStateSnapshot state);
        bool TryReceivePlayerState(out PlayerStateSnapshot state);
        void SendBossState(BossStateSnapshot state);
        bool TryReceiveBossState(out BossStateSnapshot state);
    }
}
