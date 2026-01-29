# Bang Online — Launch Guide

This project now hosts a lightweight web UI for the Bang! prototype.

## Run Locally

```bash
dotnet run
```

Then open `http://localhost:5000`.

### Play From Different Locations

Run the server so it listens on all interfaces, then share your public or LAN IP:

```bash
dotnet run --urls http://0.0.0.0:5000
```

Players can join using `http://<host-ip>:5000` from their own browsers.

## Web UI Flow

1. Enter a display name and join the table.
2. Have at least two players join (use multiple browsers or tabs).
3. Click **Start Game** to deal cards.
4. Click a card in your hand to play it. If a target is required, select a player.
5. Click **End Turn** to advance.

## Notes

- The rules are a streamlined version of Bang! to support online play; extend the card system as needed.
- Max players per room: 6.
