# Blazium Games for Unity

Unity package for [Blazium Games](https://blazium.games), a game publishing platform operated by Divine Games, Inc. The package name is `com.blazium.games`. It covers init, callbacks, the logged-on user, lobbies, a Steam ticket, bug reports, ownership, achievements, stats, events, presence, patch notices, items, and the dedicated server.

## Install

Download `com.blazium.games-<version>.tgz` from [Releases](https://github.com/blazium-games/games_unity/releases) and use Package Manager (**Add package from tarball**). Put `blazium_api64.dll` or `libblazium_api.so` from `redistributable-win64.zip` or `redistributable-linux64.zip` in `Assets/Plugins/x86_64`. Put `game.blaze` beside the built player. Add one `BlaziumTick`. Do not also call `RunCallbacks` every frame, and do not call `BlaziumApi.Init` and `BlaziumGameServer.Init` in the same process. macOS has no player library.

The public `ReportBug` method sends the in-game report. An empty build id and an empty screenshot path omit those parts. The screenshot path check lives in the engine host.

The long guide is [Engines](https://docs.blazium.games/docs/engines).

## Scripted API

`BlaziumApi.RequestToken("sapi")` asks the host for a short-lived token for this game only. The package never mints a token and never sees the login token. `BlaziumSapi` calls `https://sapi.blazium.online/v1/{game_uid}/{logic}` with that token, retries a 429 or 503 with the same `Idempotency-Key`, and asks for a fresh token once after a 401. Run it from a coroutine:

```csharp
var sapi = BlaziumUnitySapi.Create();
var result = new BlaziumSapiResult();
yield return sapi.Call("score_submit", "POST", "{\"score\":10}", result);
if (!result.Success) Debug.Log(result.ErrorKey + ": " + result.Message);
```

Pass `anonymous: true` for logic marked `auth = anonymous`. A WebGL build asks the play origin's `POST /blazium/token` instead of a host. The audience `lobby` is also available from `RequestToken`. Tokens expire in minutes and are refused for everything else, so never store one. This needs `blazium_api64.dll` / `libblazium_api.so` from SDK 2.1.0 or newer; an older library returns `sdk_too_old`.

## Player sign-in without the launcher (2.2.0)

`BlaziumPlayerSession` signs a player in from any Unity platform with C# only: no `blazium_api64.dll`, no launcher, no `game.blaze`. It shows a link, a QR code, or a short code, waits for the player to finish on blazium.games, renews the session, and returns scoped tokens for `sapi`, `lobby` and `server`, plus chat access for the Blazium IRC server. A returning player is restored from the store.

```csharp
var session = BlaziumUnitySignIn.Create("<game_uid>");
yield return session.Restore();
if (!session.LoggedOn) yield return session.SignIn();      // show session.LoginUrl / UserCode while State == Waiting
var sapi = BlaziumUnitySignIn.CreateSapi(session);          // BlaziumSapi that uses the session
```

The session is never written to `PlayerPrefs` as plain text (`PlayerPrefs` is **not encrypted**). The default store seals it with Windows DPAPI on Windows and keeps it in memory only on other platforms, so the player signs in again on the next run. On a phone or console pass `new BlaziumPlayerPrefsStore(gameUid, protector)` with an `IBlaziumSecretProtector` over the Keystore or the Keychain, or implement `IBlaziumSessionStore` over protected save data. A plain-text session left by 2.2.0 is erased the first time the store is used. The `Player sign-in` sample (Package Manager, Samples) shows the full flow. The runtime is netstandard2.0; the logic is covered by `tests/SapiTests` (run with `dotnet run`), the Unity-only parts compile only inside Unity and were not run in an editor here. When a native library is present `BlaziumSapi` still prefers it, so a game that runs both in and out of the launcher needs no change.

## Platform

- Docs: [docs.blazium.games](https://docs.blazium.games) and the map [llms.txt](https://docs.blazium.games/llms.txt)
- Support: [blazium-games/support](https://github.com/blazium-games/support/issues). Status: [status.blazium.games](https://status.blazium.games)

## This repo

A push to `master` publishes the UPM tarball. A `templates-*` tag is the engine host, not this library. This package does not proxy a Discord activity.

## License

Licensed under the MIT License — see [LICENSE](LICENSE).
