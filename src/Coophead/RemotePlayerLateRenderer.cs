using UnityEngine;

namespace Coophead
{
    [DefaultExecutionOrder(10000)]
    internal sealed class RemotePlayerLateRenderer : MonoBehaviour
    {
        private void LateUpdate()
        {
            RemoteInputLab.RenderBufferedRemotePlayersLate();
        }
    }
}
