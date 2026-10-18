# Local testing

`games_unity` is a Unity package. CI builds the C# project and runs the contract test. It does not open the Unity editor.

```powershell
dotnet build BlaziumGames.csproj -c Release
python tests/contract_test.py
```

The package covers init, callbacks, the user, lobbies, a Steam ticket, bug reports, ownership, achievements, stats, events, presence, patch notices, items, and the dedicated server. The client and server samples show a lobby call, an in-game bug, and a server progress read. `ReportBug` sends the in-game report.
