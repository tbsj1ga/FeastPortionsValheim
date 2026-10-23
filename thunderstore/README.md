# FeastPortions

**Feasts you can take on the road.** In the game a feast is eaten at the table, and once one
portion is gone the serving tray can no longer take it back: it stays where it stands or is lost.
FeastPortions lets you carry it.

- **Take a portion:** Shift + E on a placed feast puts one portion into your inventory instead of
  eating it. A portion takes two servings, so a feast makes five portions to carry. A portion is an item of its own (*Plains Pie Picnic portion*, with a slice icon): it
  has the feast's health, stamina, eitr and regeneration, lasts 30 minutes (configurable; the
  feast lasts 50) and stacks to ten. Eat it from the inventory, the hotbar or a slot mod's food
  slots. It never stacks with feasts and cannot be set out as one.
- **Leftovers:** taking up a started feast with the serving tray gives you the servings left
  as portions (the game gives nothing). A whole feast still comes back as a whole feast.
- **Set them out again:** dropping portions sets them out as a feast with the servings they took,
  for the group to eat or to take back.
- **No empty trays:** the tray disappears once the last portion is taken or eaten.

Client-side: no version check, the host and a dedicated server need nothing, and the feast's
owner may be a player without the mod. Players without it eat from the table as usual.

## Settings

`BepInEx/config/j1ga.feastportions.cfg`, or in-game with ConfigurationManager (F1):

- `TakePortions` — Shift + E takes a portion.
- `LeftoversOnRemove` — the serving tray returns the portions left.
- `DropAsFeast` — dropped portions become a feast; off: they drop as ordinary items.
- `RemoveEmptyFeast` — the empty tray disappears after the last portion.
- `Cost` — servings of the feast one portion takes (2).
- `Duration` — how long a portion lasts, in minutes (30).
