# FeastPortions

**Feasts you can take on the road.** In the game a feast is eaten at the table, and once one
portion is gone the serving tray can no longer take it back: it stays where it stands or is lost.
FeastPortions lets you carry it.

- **Take a portion:** Shift + E on a placed feast puts one portion into your inventory instead of
  eating it. A portion is an item of its own (*Plains Pie Picnic portion*, with a slice icon): it
  has the feast's health, stamina, eitr and regeneration, lasts 25 minutes (configurable; the
  feast lasts 50) and stacks to ten. Eat it from the inventory, the hotbar or a slot mod's food
  slots. It never stacks with feasts and cannot be set out as one.
- **Leftovers:** taking up a started feast with the serving tray gives you the portions left
  (the game gives nothing). A whole feast still comes back as a whole feast.
- **No empty trays:** the tray disappears once the last portion is taken or eaten.

Client-side: no version check, the host and a dedicated server need nothing, and the feast's
owner may be a player without the mod. Players without it eat from the table as usual; a portion
dropped on the ground is seen only by players who have the mod.

## Settings

`BepInEx/config/j1ga.feastportions.cfg`, or in-game with ConfigurationManager (F1):

- `TakePortions` — Shift + E takes a portion.
- `LeftoversOnRemove` — the serving tray returns the portions left.
- `RemoveEmptyFeast` — the empty tray disappears after the last portion.
- `Duration` — how long a portion lasts, in minutes (25).
