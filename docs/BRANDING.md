# Branding

Name, store texts and voice. Store text lives in the private `Store/` submodule (`store.json`); this file holds the reasoning.

## Status (2026-11-13)

- **Name: open.** "Ecaty" stays the working name in `Store/store.json`. Naming paused by the user after five rounds; resume with fresh eyes, possibly with names shown in context (title on key art and a Steam capsule).
- **Store texts: written, name-neutral, creator-first** (user direction 2026-11-13). They do not use the game's name, so they survive the rename.
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

**Draw it, build it, or just say it, and give it life.** (Earlier draft: "Make a world. Bring it to life.")

## Voice

- **The player is the creator.** Lead with what they bring to life (drawings, ideas, stories, games) and sharing with friends, not with how the tools work.
- Always name what gets life: the creations, never the players. Avoid a loose "each one" after "You are the creator". The player gives life ("give it life").
- Life, not just motion: say "give it a life of its own", not "watch it move".
- Poetic and short over literal: "just say it", not "say it in your own words"; no obvious details ("on paper"); no ambiguous ones ("shape it by hand").
- Not only sharing: playing together with friends.
- Convey the feeling, not a manual: no step-by-step mechanics, no interface terms (sphere, tree, helper, typed/spoken).
- Plain, warm, short sentences.
- No hype words (amazing, epic, revolutionary, ultimate), no exclamation marks, no feature lists in capitals.
- AI is one way in among drawing and building by hand; never the headline, never named as AI in the short text.
- Avoid "tiny"/"miniature" as a promise of scale; worlds may grow.
- No technical terms (engine, ECS, Flecs, Script, model names) in player-facing text.

## Store texts

The texts describe the intended game at release (user direction 2026-11-13), not today's build. Store pages must match the shipped game, so before each submission check every claim against STATUS and cut or soften what is not real yet:

| Claim in the text | State 2026-11-13 |
|---|---|
| Build it and give it life | Partly: place, move, spin and the motion system are real; creation tools are designed, not built |
| Say it | Real (local/cloud AI, text and dictation) |
| Draw it (real-world drawings) | Not yet: player photo picker pending, drawing-to-creature quality untested |
| Games, adventures, stories of every kind | Not yet: rules/links are in creation-tools design, not built |
| Play together, visit friends' creations, share with anyone | Not built; no networking authorized yet |
| Everything stays yours, return any time | Real (saved worlds, resume) |

Texts live in `Store/store.json`: shared `short` and `description`, per-store text under `overrides.<store>` (Release Desk flags text over a store's limit).

| Store | Uses | Limits |
|---|---|---|
| Steam | shared short (210) and description | short 300 |
| Google Play | override short (72), shared description | title 30, short 80, full 4000 |
| Epic | shared description | about 10000 |

## Next branding tasks

Name (resume), then logo, palette, store art direction.
