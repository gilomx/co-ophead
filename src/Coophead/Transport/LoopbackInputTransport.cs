using System.Collections.Generic;

namespace Coophead.Transport
{
    internal sealed class LoopbackInputTransport : IInputFrameTransport
    {
        private readonly Queue<PendingFrame> pending = new Queue<PendingFrame>();

        public LoopbackInputTransport(uint latencyFrames)
        {
            LatencyFrames = latencyFrames;
        }

        public uint LatencyFrames { get; }

        public string Description => "loopback, " + LatencyFrames + " frames de latencia";

        public void Reset()
        {
            pending.Clear();
        }

        public void Send(InputFrame frame)
        {
            pending.Enqueue(new PendingFrame(frame.Tick + LatencyFrames, frame));
        }

        public bool TryReceive(uint receiverTick, out InputFrame frame)
        {
            if (pending.Count == 0 || pending.Peek().DeliveryTick > receiverTick)
            {
                frame = default(InputFrame);
                return false;
            }

            frame = pending.Dequeue().Frame;
            return true;
        }

        private struct PendingFrame
        {
            public PendingFrame(uint deliveryTick, InputFrame frame)
            {
                DeliveryTick = deliveryTick;
                Frame = frame;
            }

            public uint DeliveryTick;
            public InputFrame Frame;
        }
    }
}
