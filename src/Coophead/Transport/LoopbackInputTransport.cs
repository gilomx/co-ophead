using System.Collections.Generic;

namespace Coophead.Transport
{
    internal sealed class LoopbackInputTransport : IInputFrameTransport
    {
        private readonly Queue<PendingFrame> pending = new Queue<PendingFrame>();
        private readonly Queue<SceneCommand> scenes = new Queue<SceneCommand>();

        public LoopbackInputTransport(uint latencyFrames)
        {
            LatencyFrames = latencyFrames;
        }

        public uint LatencyFrames { get; }

        public string Description => "loopback, " + LatencyFrames + " frames de latencia";
        public string Status => "conectado";
        public bool IsConnected => true;
        public int PingMilliseconds => 0;

        public void Update()
        {
        }

        public void Reset()
        {
            pending.Clear();
            scenes.Clear();
        }

        public void Dispose()
        {
            pending.Clear();
            scenes.Clear();
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

        public void SendScene(SceneCommand command)
        {
            scenes.Enqueue(command);
        }

        public bool TryReceiveScene(out SceneCommand command)
        {
            if (scenes.Count == 0)
            {
                command = default(SceneCommand);
                return false;
            }
            command = scenes.Dequeue();
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
