# FeastPortions

**Feasts you can take on the road.** In the game a feast is eaten at the table, and once one
portion is gone the serving tray can no longer take it back: it stays where it stands or is lost.
FeastPortions lets you carry it.

- **Take a portion:** Shift + E on a placed feast puts one portion into your inventory instead of
  eating it. A portion is the feast's own food: the same health, stamina, eitr, regeneration and
  duration, eaten from the inventory, the hotbar or a slot mod's food slots. Stacks to ten.
- **Leftovers:** taking up a started feast with the serving tray gives you the portions left
  (the game gives nothing). A whole feast still comes back as a whole feast.
- **Set them out again:** dropping portions sets them out as a feast with that many portions,
  for the group to eat or to take back.

Client-side: no version check, the host and a dedicated server need nothing, and the feast's
owner may be a player without the mod. Players without it eat from the table as usual.

## Settings

`BepInEx/config/j1ga.feastportions.cfg`, or in-game with ConfigurationManager (F1):

- `TakePortions` — Shift + E takes a portion.
- `LeftoversOnRemove` — the serving tray returns the portions left.
- `DropAsFeast` — dropped portions become a feast; off: portions cannot be dropped.
