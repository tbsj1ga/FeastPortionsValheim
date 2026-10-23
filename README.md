# FeastPortions

**English** · [Русский](README-RU.md)

A Valheim mod: feasts you can carry. Take portions off a placed feast into the inventory, get the
leftovers back when you take a started feast up with the serving tray, and set portions out as a
feast again. Client-side only: the host and a dedicated server need nothing.

Version history is in `CHANGELOG.md`.

## How to use

- **Shift + E** on a placed feast (the hover shows *Take a portion*): one portion goes into the
  inventory instead of being eaten. Plain **E** eats as usual.
- A portion is the feast's own food item: the same health, stamina, eitr, regeneration and
  duration as eating at the table, and it decays the same way. Eat it from the inventory, the
  hotbar or the food slots of a slot mod. Portions stack to ten (a feast's portion count).
- **Serving tray on a started feast:** you get the portions left. What does not fit into the
  inventory stays where the feast stood, as a smaller feast. A whole feast comes back whole, as
  in the game.
- **Dropping portions** sets them out in front of you as a feast with that many portions (ten per
  feast), so the group can eat from it or you can take them back. Portions spilled from a
  destroyed chest or cart become a feast the same way.

## How it works

A placed feast is a piece with a `Feast` component. Its portions are a counter in the ZDO
(`ZDOVars.s_value`, starting at `m_eatStacks`), and each portion is the feast's own food item
(`m_foodItem`), eaten with `Player.EatFood`. The serving tray returns
`floor(1 × portions left / portions)` feasts (`Piece.DropResources`), which is 0 after the first
bite.

- **Portions.** Shift + E sends the feast's own `RPC_TryEat`; its owner counts a portion off and
  answers `RPC_EatConfirmation`, where this client puts the portion into the inventory instead of
  eating it. The owner needs no mod.
- **The inventory item.** At `ObjectDB.Awake` / `CopyOtherDB` the mod goes through every build
  tool's piece table, finds the feasts and lets their food items stack to the feast's portion
  count. If ObjectDB does not list a food item, the mod adds it (the inventory saves and loads
  items by ObjectDB name; the log says which were added).
- **Leftovers.** While `Player.RemovePiece` runs, a prefix on `WearNTear.Remove` (reached only once
  every check has passed) reads the portions left of a started feast and puts them into the
  inventory; the game then removes the feast as usual.
- **Dropping.** The food item's prefab is the feast piece itself, so the game would drop a
  portion as a full feast. The mod catches `Humanoid.DropItem` and `ItemDrop.DropItem` and sets
  out a feast with exactly the dropped portions, on the ground under the drop point.

## Compatibility

- Works alongside FeastMaster and other mods that change servings: the portion count is read from
  the feast.
- Feasts added by other mods are covered if they use the game's `Feast` component and a build
  tool's piece table.
- If the mod is removed, portions of a food item the game itself does not list in ObjectDB are
  dropped from the inventory on load (the log at start names such items).

Built against **Valheim 1.0.20** (network version 41), **BepInEx 5.4.23.5**
(BepInExPack_Valheim 5.4.2351).

## Who needs it

| Who | What |
|---|---|
| A player who wants to carry portions | installs it |
| Other players | nothing |
| The host, a dedicated server | nothing |

## Bugs and feedback

https://github.com/tbsj1ga/FeastPortionsValheim/issues

## Installation

Through r2modman / Thunderstore, or put `build/FeastPortions.dll` into

```
%AppData%\r2modmanPlus-local\Valheim\profiles\Valheim\BepInEx\plugins\FeastPortions\
```

(or `build.ps1 -Install`).

## Settings

`BepInEx/config/j1ga.feastportions.cfg`, or in-game with ConfigurationManager (F1).

| Section | Key | Default | What |
|---|---|---|---|
| 01 General | `TakePortions` | `true` | Shift + E on a feast puts a portion into the inventory |
| 01 General | `LeftoversOnRemove` | `true` | the serving tray returns the portions left of a started feast |
| 01 General | `DropAsFeast` | `true` | dropped portions are set out as a feast; off: portions cannot be dropped |

## Building

```
powershell -ExecutionPolicy Bypass -File .\build.ps1            # build and check references
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install   # ... and copy into plugins
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Package   # ... and make the Thunderstore zip
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Nexus     # ... zip, and the Nexus Mods kit
```

The compiler is `csc.exe` from the .NET Framework (C# 5); references come straight from the
game folder and the r2modman profile's `BepInEx\core`. After the build, `check-refs.ps1` checks
every reference and Harmony patch target against the game.

## Repository

Branch `main` on GitHub: https://github.com/tbsj1ga/FeastPortionsValheim.

## Credits and AI assistance

FeastPortions is written from scratch. This mod was developed with the help of an AI assistant
(Claude by Anthropic); the design decisions, in-game testing and releases are the author's.
