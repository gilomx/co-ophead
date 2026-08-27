namespace Coophead.Transport
{
    internal interface IInputFrameTransport : System.IDisposable
    {
        string Description { get; }
        string Status { get; }
        bool IsConnected { get; }
        int PingMilliseconds { get; }
        void Reset();
        void Update();
        void Send(InputFrame frame);
        bool TryReceive(uint receiverTick, out InputFrame frame);
        void SendScene(SceneCommand command);
        bool TryReceiveScene(out SceneCommand command);
        void SendContext(SessionContext context);
        bool TryReceiveContext(out SessionContext context);
        void SendPlayerState(PlayerStateSnapshot state);
        bool TryReceivePlayerState(out PlayerStateSnapshot state);
    }
}
