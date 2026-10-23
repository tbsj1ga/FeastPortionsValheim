# FeastPortions

**English** · [Русский](README-RU.md)

A Valheim mod: feasts you can carry. Take portions off a placed feast into the inventory and get
the leftovers back when you take a started feast up with the serving tray. Client-side only: the
host and a dedicated server need nothing.

Version history is in `CHANGELOG.md`.

## How to use

- **Shift + E** on a placed feast (the hover shows *Take a portion (2 servings)*): one portion
  goes into the inventory instead of being eaten. It takes two of the feast's servings (`Cost`):
  a feast of ten makes five portions to carry. With a single serving left you can only eat it
  there. Plain **E** eats one serving as usual.
- A portion is an item of its own — *Plains Pie Picnic portion*, its icon the feast's with one
  slice in colour. It has the feast's health, stamina, eitr and regeneration and decays the same
  way, but lasts 30 minutes instead of the feast's 50 (`Duration`). Eat it from the inventory, the
  hotbar or the food slots of a slot mod. Portions stack to ten (a feast's portion count), never
  with feasts, and cannot be set out as a feast.
- **Serving tray on a started feast:** you get the servings left as portions, two servings each;
  an odd serving left over is lost. What does not fit into the inventory is set out where you
  stand. A whole feast comes back whole, as in the game.
- **Dropping portions** sets them out in front of you as a feast with the servings they took (two
  per portion, ten per feast), so the group can eat from it or you can take them back. Portions spilled from a
  destroyed chest or cart become a feast the same way.
- **The empty tray** disappears once the last portion is taken or eaten (the game leaves it
  standing).

## How it works

A placed feast is a piece with a `Feast` component. Its portions are a counter in the ZDO
(`ZDOVars.s_value`, starting at `m_eatStacks`), and each portion is the feast's own food item
(`m_foodItem`), eaten with `Player.EatFood`. The serving tray returns
`floor(1 × portions left / portions)` feasts (`Piece.DropResources`), which is 0 after the first
bite.

- **The portion item.** One per feast, `<feast>_Portion`: a copy of the feast kit's world object
  carrying a copy of the feast food's data under its own name, slice icon (the feast's icon
  copied through a render texture) and duration. It cannot be the food item itself: the inventory
  stacks items and counts build materials by `SharedData.m_name`, and the feast's food shares its
  name with the kit the tray places. The portions are registered in `ObjectDB` (the inventory
  saves items by prefab name) and `ZNetScene`.
- **Taking a portion.** Shift + E sends the feast's own `RPC_TryEat` once per serving; its owner
  counts each off and answers each with `RPC_EatConfirmation`, where this client puts the portion into the inventory
  instead of eating it. The owner needs no mod.
- **Leftovers.** While `Player.RemovePiece` runs, a prefix on `WearNTear.Remove` (reached only once
  every check has passed) reads the servings left of a started feast and puts them into the
  inventory as portions; the game then removes the feast as usual.
- **Dropping.** `Humanoid.DropItem` and `ItemDrop.DropItem` set dropped portions out as a feast
  with exactly the servings they took, on the ground under the drop point (with `DropAsFeast` off,
  they drop as ordinary items).
- **The empty tray.** When the portion taken or eaten was the last one, the client asks the
  feast's owner to remove it (`WearNTear.Remove`, dropping nothing).

## Compatibility

- Works alongside FeastMaster and other mods that change servings: the portion count is read from
  the feast.
- Feasts added by other mods are covered if they use the game's `Feast` component.
- If the mod is removed, portions disappear from inventories. With `DropAsFeast` off, a portion
  lying on the ground is seen only by players who have the mod.

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
| 01 General | `DropAsFeast` | `true` | dropped portions are set out as a feast; off: they drop as ordinary items |
| 01 General | `RemoveEmptyFeast` | `true` | the empty tray disappears after the last portion is taken or eaten |
| 02 Portions | `Cost` | `2` | servings of the feast one portion takes (eating at the table: one) |
| 02 Portions | `Duration` | `30` | how long a portion lasts once eaten, in minutes (a feast: 50) |

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
