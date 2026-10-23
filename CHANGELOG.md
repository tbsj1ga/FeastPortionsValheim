# Changelog

**English** · [Русский](CHANGELOG-RU.md)

The version is set in one place — `FeastPortionsPlugin.Version` in `src/FeastPortionsPlugin.cs`.

## 0.1.1

- Portions stay out of chests, carts and ship storage (`KeepOutOfChests`, on by default; a
  tombstone still takes them). A player without the mod does not see a portion in a chest,
  and once they move anything in it the chest is saved without it. Turn it off in a group
  where everyone has the mod.

## 0.1.0

- First release. Shift + E on a placed feast puts a portion into the inventory: an item of its
  own, named after the feast, with a slice icon, taking two of the feast's servings. It eats like
  any food with the feast's values; it lasts 30 minutes (configurable) and stacks to ten. Portions never stack with feasts and
  cannot be set out as a feast.
- Taking up a started feast with the serving tray gives the portions left instead of nothing.
- Dropped portions are set out as a feast again, with the servings they took.
- The empty serving tray disappears once the last portion is taken or eaten.
