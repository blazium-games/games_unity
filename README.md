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

## Platform

- Docs: [docs.blazium.games](https://docs.blazium.games) and the map [llms.txt](https://docs.blazium.games/llms.txt)
- Support: [blazium-games/support](https://github.com/blazium-games/support/issues). Status: [status.blazium.games](https://status.blazium.games)

## This repo

A push to `master` publishes the UPM tarball. A `templates-*` tag is the engine host, not this library. This package does not proxy a Discord activity.

## License

Licensed under the MIT License — see [LICENSE](LICENSE).
