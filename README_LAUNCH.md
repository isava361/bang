# Bang Online — Launch Guide

This project now hosts a lightweight web UI for the Bang! prototype.

## Run Locally

```bash
dotnet run
```

Then open `http://localhost:5000` (or the port shown in the console output).

## Web UI Flow

1. Enter a display name and join the table.
2. Have at least two players join (use multiple browsers or tabs).
3. Click **Start Game** to deal cards.
4. Click a card in your hand to play it. If a target is required, select a player.
5. Click **End Turn** to advance.

## Notes

- The rules are a streamlined version of Bang! to support online play; extend the card system as needed.
- Max players per room: 6.
