using UnityEngine;

namespace Blazium.Games.Samples
{
    // One-shot client check. A shipping scene uses BlaziumTick and does not shut down in Start.
    public sealed class BlaziumClientSample : MonoBehaviour
    {
        private void Start()
        {
            string error;
            BlaziumInitResult result = BlaziumApi.Init(out error);
            if (result != BlaziumInitResult.OK)
            {
                Debug.LogError("Blazium init failed: " + (int)result + " " + error);
                return;
            }
            if (!BlaziumUser.IsLoggedOn())
            {
                BlaziumApi.Shutdown();
                Debug.LogError("Blazium init returned OK without a logged-on user");
                return;
            }
            BlaziumApi.RunCallbacks();
            Debug.Log("logged_on uid=" + BlaziumUser.UserUid()
                + " name=" + BlaziumUser.DisplayName()
                + " user=" + BlaziumUser.Username()
                + " game=" + BlaziumUser.GameUid());
            Debug.Log(BlaziumLobby.CreateLobby("public"));
            Debug.Log(BlaziumUser.ReportBug("player fell through the floor", "1.0.0", "Windows", "x64", "", "build-1", ""));
            BlaziumApi.Shutdown();
        }
    }
}
