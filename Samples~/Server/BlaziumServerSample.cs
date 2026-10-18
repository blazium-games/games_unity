using UnityEngine;

namespace Blazium.Games.Samples
{
    public sealed class BlaziumServerSample : MonoBehaviour
    {
        private void Start()
        {
            string error;
            BlaziumInitResult result = BlaziumGameServer.Init(27015, "1.0.0", out error);
            if (result != BlaziumInitResult.OK)
            {
                Debug.LogError("Blazium server init failed: " + (int)result + " " + error);
                return;
            }
            BlaziumGameServer.LogOn();
            Debug.Log("server id=" + BlaziumGameServer.ServerId());
            Debug.Log(BlaziumGameServer.Progress("server-key", "player-uid"));
            BlaziumGameServer.Shutdown();
        }
    }
}
