# Branding

Name, store texts and voice. Store text lives in the private `Store/` submodule (`steam/`, `play/`, `epic/` listing.json); this file holds the reasoning.

## Status (2026-11-13)

- **Name: open.** "Ecaty" stays the working name in `Store/store.json`. Naming paused by the user after five rounds; resume with fresh eyes, possibly with names shown in context (title on key art and a Steam capsule).
- **Store texts: written, name-neutral.** They do not use the game's name, so they survive the rename.
- Renaming code identifiers waits for the final name (BACKLOG decision 2026-11-11).

## Product essence (confirmed with the user)

- **For:** all ages, worldwide. Players who enjoy calm, goal-free making. Windows/Steam first, then Android, then Epic.
- **Core:** giving life to creations in three ways: real-world drawings, ready forms and shapes, and AI from text or voice. Worlds can be any size the runtime allows; behaviour and interaction depth likewise grow with the engine.
- **Feel:** a maker's bench. Small deliberate actions, instant visible response, little or no text, quiet sound.
- **Direction (not built):** share works, combine with other players' creations, multiplayer, sharing to social media, AR preview, 3D printing. A quick-recovery archive for creations is planned but not defined.
- The crystal tree is the access interface to the player's creations, not the core game.

## Naming criteria

- Works internationally: easy to say the same way in most languages, no odd or negative meaning, no reliance on English wordplay.
- All ages.
- Not already used by an existing product (game, app, toy, software, notable brand).
- May lean on any of: a soft rolling sound (the user liked how *Tobolo* sounds), life/breath/spirit (the user likes the word *anima*), play/toys/making, small wonder.
- Albanian or German words are welcome if they pass the above.

## Candidates checked (light web screen, not legal advice)

Steam was searched directly; Play, Epic, App Store, social handles and `.game` were only partly checked. Run official USPTO/EUIPO searches (classes 9, 28, 41, 42) on the final name before committing to it.

**Still possible:**

| Name | Origin, meaning | Notes |
|---|---|---|
| Lodra | Albanian "toys" | No product found; .com parked for sale; said "LOH-dra" everywhere. Assistant's recommendation. |
| Zemra | Albanian "the heart" | No product found; an Albanian media portal holds the .com. |
| Fole | Albanian "nest" | No product found; English readers may say "fool"; Italian/Portuguese "bellows". |
| Tobolo | invented | No product found; .com for sale. User liked the sound but not the name itself. |
| Wusel | German "bustling little creature" | No product found; .com/.de for sale. |
| Animola, Animolo, Kivimo, Pomble | invented | No product found. Animolo is close to the Play app "Animomo: Animate Drawings". |

**Rejected:**

- *English wordplay (not international):* Wakeling, Livelings, Hatchwork, Makerwild, Sketchling (.com taken), Doodlewake (alarm-clock app).
- *Existing product or brand:* Clayborn (Steam game), Animori (fresh US filing), Ovivo (Steam game), Nuvio (several apps), Animo (games, AU mark), Animobo (educational robots), Fara (two games), Bota (Play app), Flutura (Accenture software), Balta (carpet brand, studio), Fryma (machinery brand), Zana (Steam titles, AI company), Wichtel (many apps), Vivolo (company .com).
- *Near-mark or meaning risk:* Vivlet, Vivani, Kinlo, Wisple, Mavu, Lumola, Lumolo, Tomolo, Kimolo, Nimolo, Momolo, Pimola (an olive), Bima (insurance in Hindi), Pema (Tibetan religious name), Brumi, Murmel, Knete (slang for money), Tüftel (umlaut), Fluturo (reads as "futuro").

## Tagline

**Make a world. Bring it to life.**

## Voice

- Plain, warm, short sentences. Say what the player does, not what the game is.
- No hype words (amazing, epic, revolutionary, ultimate), no exclamation marks, no feature lists in capitals.
- Describe only what ships. AI is an optional helper, never required, never the headline.
- Avoid "tiny"/"miniature" as a promise of scale; worlds may grow.
- No technical terms (engine, ECS, Flecs, Script, model names) in player-facing text.

## Store texts

The current texts cover only implemented features: placing and moving shapes, setting them in motion, the optional typed/spoken helper, the crystal tree of saved worlds and resuming a world where you left off. Before each store submission, recheck them against STATUS.

Add to the texts once each is real and verified: bringing real drawings/photos to life (the player photo picker is pending), the creation tools, sharing and combining with others, multiplayer, AR preview, 3D printing, the archive.

| Store | File | Limits |
|---|---|---|
| Steam | `Store/steam/listing.json` | short 300 (current 243), about multiline |
| Google Play | `Store/play/listing.json` | title 30, short 80 (current 65), full 4000 |
| Epic | `Store/epic/listing.json` | not yet in Release Desk rules; verify in the Epic Developer Portal |

## Next branding tasks

Name (resume), then logo, palette, store art direction.
