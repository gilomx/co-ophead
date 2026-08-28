using System.Collections.Generic;

namespace Coophead.Transport
{
    internal sealed class LoopbackInputTransport : IInputFrameTransport
    {
        private readonly Queue<PendingFrame> pending = new Queue<PendingFrame>();
        private readonly Queue<SceneCommand> scenes = new Queue<SceneCommand>();
        private readonly Queue<SessionContext> contexts = new Queue<SessionContext>();
        private readonly Queue<PlayerStateSnapshot> playerStates = new Queue<PlayerStateSnapshot>();

        public LoopbackInputTransport(uint latencyFrames)
        {
            LatencyFrames = latencyFrames;
        }

        public uint LatencyFrames { get; }

        public string Description => "loopback, " + LatencyFrames + " frames de latencia";
        public string Status => "conectado";
        public bool IsConnected => true;
        public int PingMilliseconds => 0;
        public int EstimatedPacketLossPercent => 0;

        public void Update()
        {
        }

        public void Reset()
        {
            pending.Clear();
            scenes.Clear();
            contexts.Clear();
            playerStates.Clear();
        }

        public void Dispose()
        {
            pending.Clear();
            scenes.Clear();
            contexts.Clear();
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

        public void SendContext(SessionContext context)
        {
            contexts.Enqueue(context);
        }

        public bool TryReceiveContext(out SessionContext context)
        {
            if (contexts.Count == 0)
            {
                context = default(SessionContext);
                return false;
            }
            context = contexts.Dequeue();
            return true;
        }

        public void SendPlayerState(PlayerStateSnapshot state) { playerStates.Enqueue(state); }

        public bool TryReceivePlayerState(out PlayerStateSnapshot state)
        {
            if (playerStates.Count == 0) { state = default(PlayerStateSnapshot); return false; }
            state = playerStates.Dequeue(); return true;
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
