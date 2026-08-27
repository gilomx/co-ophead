namespace Coophead.Transport
{
    internal interface IInputFrameTransport
    {
        string Description { get; }
        void Reset();
        void Send(InputFrame frame);
        bool TryReceive(uint receiverTick, out InputFrame frame);
    }
}
