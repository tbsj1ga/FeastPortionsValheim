# Changelog

**English** · [Русский](CHANGELOG-RU.md)

The version is set in one place — `FeastPortionsPlugin.Version` in `src/FeastPortionsPlugin.cs`.

## 0.1.0

- First release. Shift + E on a placed feast puts a portion into the inventory: an item of its
  own, named after the feast, with a slice icon. It eats like any food with the feast's values;
  it lasts 25 minutes (configurable) and stacks to ten. Portions never stack with feasts and
  cannot be set out as a feast.
- Taking up a started feast with the serving tray gives the portions left instead of nothing.
- Dropped portions are set out as a feast with that many portions.
- The empty serving tray disappears once the last portion is taken or eaten.
