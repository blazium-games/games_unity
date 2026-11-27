using UnityEngine;

namespace Blazium.Games.Samples
{
    // Sign a player in with no Blazium launcher and no native library: web, mobile, console, or a
    // build you ship on your own. The player opens the link (or scans a QR of it, or types the code
    // at blazium.games/connect on another device) and signs in or creates an account there.
    public sealed class BlaziumPlayerSignInSample : MonoBehaviour
    {
        // The game's uid from your store page.
        public string GameUid = "";

        private BlaziumPlayerSession session;

        private void Start()
        {
            // The default store seals the session with DPAPI on Windows and keeps it in memory only
            // elsewhere. On a phone or console pass new BlaziumPlayerPrefsStore(GameUid, protector)
            // with a protector over the Keystore or the Keychain to keep the sign-in between runs.
            session = BlaziumUnitySignIn.Create(GameUid);
            session.Changed += OnChanged;
            // A returning player whose sealed session was kept is signed in again with no steps.
            StartCoroutine(Begin());
        }

        private System.Collections.IEnumerator Begin()
        {
            yield return session.Restore();
            if (!session.LoggedOn)
            {
                yield return session.SignIn();
            }
            if (session.LoggedOn)
            {
                // Renews the sign-in a little before it runs out, for as long as the game runs.
                yield return session.KeepFresh(60f);
            }
        }

        private void OnChanged()
        {
            switch (session.State)
            {
                case BlaziumSignInState.Waiting:
                    // Show session.LoginUrl as a button and a QR, and session.UserCode with
                    // session.VerificationUrl for a device with no browser.
                    Debug.Log("Open " + session.LoginUrl + " or enter " + session.UserCode + " at " + session.VerificationUrl);
                    break;
                case BlaziumSignInState.Pending:
                    Debug.Log("Finish the step on the website: " + session.Gate);
                    break;
                case BlaziumSignInState.Complete:
                    Debug.Log("Signed in as " + session.DisplayName + " (" + session.UserUid + ")");
                    StartCoroutine(CallScriptedApi());
                    break;
                case BlaziumSignInState.Failed:
                    Debug.LogWarning("Sign-in failed: " + session.Error);
                    break;
            }
        }

        private System.Collections.IEnumerator CallScriptedApi()
        {
            BlaziumSapi sapi = BlaziumUnitySignIn.CreateSapi(session);
            BlaziumSapiResult result = new BlaziumSapiResult();
            yield return sapi.Call("hello", "POST", "{}", result);
            Debug.Log("hello -> " + (result.Success ? result.Body : result.ErrorKey));
        }

        // Your sign-out button.
        public void SignOut()
        {
            StartCoroutine(session.Logout());
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.Changed -= OnChanged;
                session.Cancel();
            }
        }
    }
}
