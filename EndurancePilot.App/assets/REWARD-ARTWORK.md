Reward artwork is Fortnite game content belonging to Epic Games, Inc.
Source URLs and SHA-256 hashes are recorded in reward-sources.json.
STW resource art is sourced from PennyDB's public resource image collection.
Season/Battle Pass XP uses the Fortnite XP badge published by Gamewave.
These game assets are not original Endurance Pilot art or covered by any
license for Endurance Pilot's own code.

People XP uses the gold people-chip asset served under the historical
survivor_xp filename, matching the game's People XP reward card.
All images are bundled locally and uploaded inside the receipt PNG; Discord
does not need to load external image URLs. Unknown rewards use a verified
current-receipt card capture, then a text monogram if no capture exists.

When adding an asset, inspect its pixels: the source CDN returns a black
placeholder PNG with HTTP 200 for unknown names. Packaged self-checks reject
blank artwork and verify that every sample reward has a local image.
