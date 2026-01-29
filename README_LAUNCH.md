# Bang Online — Launch Guide

This project ships as a single console executable. One player hosts a room, and up to five others join via IP/port.

## Build the .exe (Windows)

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

The executable will be created at:

```
bin/Release/net8.0/win-x64/publish/BangOnline.exe
```

## Host a Room

1. Launch `BangOnline.exe`.
2. Choose **Host room**.
3. Enter your display name and port (default: 5151).
4. Share your IP address and port with other players.
5. Once at least 2 players have joined, type `/start` to begin.

## Join a Room

1. Launch `BangOnline.exe`.
2. Choose **Join room**.
3. Enter the host IP address and port.
4. Enter your display name.

## In-Game Commands

- `/help` — Show command list.
- `/say <message>` — Send chat.
- `/state` — Refresh game state.
- `/play <index> [targetId]` — Play a card from your hand (targets required for Bang!/Cat Balou).
- `/end` — End your turn.
- `/quit` — Leave the room.

## Notes

- The rules are a streamlined version of Bang! to support online play; extend the card system as needed.
- Max players per room: 6.
