using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace FeastPortions
{
    // Feasts you can carry: take a portion off a placed feast into the inventory, and get the
    // portions left when you take a started feast up with the serving tray.
    //
    // A placed feast is a piece with a Feast component: its portions are a counter in the ZDO
    // (ZDOVars.s_value, starting at m_eatStacks), and every portion is the same food item - the
    // feast's own ItemDrop (m_foodItem) - eaten with Player.EatFood. The game lets you take a
    // feast back with the tray only whole: Piece.DropResources returns floor(1 x portions left /
    // portions), so one bite and the feast is gone for good.
    //
    // The portion in the inventory is that food item itself, so it eats like any food (inventory,
    // hotbar, food slots of slot mods) with the feast's exact values. Its prefab is the feast
    // piece, so whenever the game would drop it as a world item it would spawn a full feast
    // instead; dropping portions therefore sets out a feast with as many portions as were dropped.
    //
    // Taking a portion goes through the feast's own RPC_TryEat: its owner counts the portion off
    // and answers RPC_EatConfirmation, where this client puts the portion into the inventory
    // instead of eating it. Client-side only: the owner of the feast can be a player without the
    // mod, a host or a server need nothing.
    [BepInPlugin(Guid, Name, Version)]
    public class FeastPortionsPlugin : BaseUnityPlugin
    {
        public const string Guid = "j1ga.feastportions";
        public const string Name = "FeastPortions";
        public const string Version = "0.1.0";

        private sealed class FeastInfo
        {
            public GameObject Piece;    // the placeable feast
            public string Food;         // prefab name of the food item its portions are
            public int Portions;
        }

        private static ConfigEntry<bool> _takePortions;
        private static ConfigEntry<bool> _leftovers;
        private static ConfigEntry<bool> _dropAsFeast;

        private static readonly Dictionary<string, FeastInfo> _byFood = new Dictionary<string, FeastInfo>(StringComparer.Ordinal);
        private static readonly Dictionary<ZDOID, float> _pending = new Dictionary<ZDOID, float>();
        private static bool _removing;

        private Harmony _harmony;

        private void Awake()
        {
            _takePortions = Config.Bind("01 General", "TakePortions", true,
                "Shift + E (alt use) on a placed feast puts one portion into the inventory instead of eating it.");
            _leftovers = Config.Bind("01 General", "LeftoversOnRemove", true,
                "Taking up a started feast with the serving tray gives the portions left (the game gives nothing). A whole feast still comes back whole.");
            _dropAsFeast = Config.Bind("01 General", "DropAsFeast", true,
                "Dropping portions from the inventory sets them out as a feast with that many portions. Off: portions cannot be dropped.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(FeastPortionsPlugin).Assembly);
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }

        private static bool Russian()
        {
            return Localization.instance != null && Localization.instance.GetSelectedLanguage() == "Russian";
        }

        private static string T(string en, string ru)
        {
            return Russian() ? ru : en;
        }

        // ---- the feasts ----

        // Every feast the serving trays (or any tool) can set out, by the food item its portions
        // are. The food item becomes an inventory item: it goes into ObjectDB if the game left it
        // out (the inventory saves and loads items by ObjectDB prefab name) and stacks to the
        // feast's portion count.
        private static void Register(ObjectDB db)
        {
            if (db == null || db.m_items == null) return;
            List<GameObject> added = new List<GameObject>();
            foreach (GameObject go in db.m_items.ToArray())
            {
                if (go == null) continue;
                ItemDrop tool = go.GetComponent<ItemDrop>();
                if (tool == null || tool.m_itemData == null || tool.m_itemData.m_shared == null) continue;
                PieceTable table = tool.m_itemData.m_shared.m_buildPieces;
                if (table == null || table.m_pieces == null) continue;
                foreach (GameObject piece in table.m_pieces)
                {
                    if (piece == null) continue;
                    Feast feast = piece.GetComponent<Feast>();
                    if (feast == null) continue;
                    ItemDrop food = feast.m_foodItem != null ? feast.m_foodItem : piece.GetComponent<ItemDrop>();
                    if (food == null || food.m_itemData == null || food.m_itemData.m_shared == null) continue;
                    if (food.m_itemData.m_shared.m_food <= 0f) continue;

                    FeastInfo info = new FeastInfo();
                    info.Piece = piece;
                    info.Food = food.gameObject.name;
                    info.Portions = Math.Max(1, feast.m_eatStacks);
                    _byFood[info.Food] = info;

                    if (food.m_itemData.m_shared.m_maxStackSize < info.Portions)
                        food.m_itemData.m_shared.m_maxStackSize = info.Portions;
                    if (db.GetItemPrefab(info.Food) == null && !added.Contains(food.gameObject))
                        added.Add(food.gameObject);
                }
            }
            if (added.Count == 0) return;
            db.m_items.AddRange(added);
            AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(db, null);
            List<string> names = new List<string>();
            foreach (GameObject go in added) names.Add(go.name);
            Debug.Log("[" + Name + "] added to ObjectDB: " + string.Join(", ", names.ToArray()));
        }

        private static FeastInfo InfoOf(ItemDrop.ItemData item)
        {
            if (item == null || item.m_dropPrefab == null) return null;
            FeastInfo info;
            return _byFood.TryGetValue(item.m_dropPrefab.name, out info) ? info : null;
        }

        private static FeastInfo InfoOf(Feast feast)
        {
            ItemDrop food = feast.m_foodItem != null ? feast.m_foodItem : feast.GetComponent<ItemDrop>();
            if (food == null) return null;
            FeastInfo info;
            return _byFood.TryGetValue(Utils.GetPrefabName(food.gameObject), out info) ? info : null;
        }

        private static int PortionsLeft(Feast feast)
        {
            ZNetView nview = feast.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return feast.m_eatStacks;
            return nview.GetZDO().GetInt(ZDOVars.s_value, feast.m_eatStacks);
        }

        // Up to count portions into the player's inventory; returns how many went in.
        private static int Give(Player player, FeastInfo info, int count)
        {
            Inventory inv = player.GetInventory();
            int given = 0;
            while (given < count)
            {
                if (inv.AddItem(info.Food, 1, 1, 0, 0L, "", false, true) == null) break;
                given++;
            }
            return given;
        }

        private static bool HasRoom(Player player, FeastInfo info)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(info.Food) : null;
            return prefab != null && player.GetInventory().CanAddItem(prefab, 1);
        }

        // Sets out a feast (or several, ten portions each) with the given portions at a point.
        private static void SetOut(FeastInfo info, int portions, Vector3 pos, Quaternion rot)
        {
            while (portions > 0)
            {
                int n = Math.Min(portions, info.Portions);
                portions -= n;
                GameObject go = UnityEngine.Object.Instantiate(info.Piece, pos, rot);
                ZNetView nview = go.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid()) nview.GetZDO().Set(ZDOVars.s_value, n, false);
                pos += rot * new Vector3(0.6f, 0f, 0f);
            }
        }

        // The ground under a point (terrain, pieces, rocks), or false over water and the void.
        private static bool Ground(Vector3 from, out Vector3 point)
        {
            point = from;
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            RaycastHit hit;
            if (!Physics.Raycast(from + Vector3.up * 1.5f, Vector3.down, out hit, 6f, mask)) return false;
            if (ZoneSystem.instance != null && hit.point.y < ZoneSystem.instance.m_waterLevel - 0.3f) return false;
            point = hit.point;
            return true;
        }

        // ---- database ----

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class ObjectDB_Awake_Patch
        {
            private static void Postfix(ObjectDB __instance) { Register(__instance); }
        }

        [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
        private static class ObjectDB_CopyOtherDB_Patch
        {
            private static void Postfix(ObjectDB __instance) { Register(__instance); }
        }

        // ---- B: take a portion ----

        [HarmonyPatch(typeof(Feast), "Interact")]
        private static class Feast_Interact_Patch
        {
            private static bool Prefix(Feast __instance, Humanoid human, bool hold, bool alt, ref bool __result)
            {
                if (!alt || !_takePortions.Value)
                {
                    // eating normally: an unanswered take must not turn this bite into a portion
                    ZNetView own = __instance.GetComponent<ZNetView>();
                    if (own != null && own.IsValid()) _pending.Remove(own.GetZDO().m_uid);
                    return true;
                }
                __result = false;
                Player player = human as Player;
                if (hold || player == null || player != Player.m_localPlayer) return false;
                if (Vector3.Distance(player.transform.position, __instance.transform.position) >= __instance.m_useDistance) return false;
                if (PortionsLeft(__instance) <= 0) return false;
                FeastInfo info = InfoOf(__instance);
                ZNetView nview = __instance.GetComponent<ZNetView>();
                if (info == null || nview == null || !nview.IsValid()) return false;
                if (!HasRoom(player, info))
                {
                    player.Message(MessageHud.MessageType.Center, "$inventory_full", 0, null, false);
                    return false;
                }
                _pending[nview.GetZDO().m_uid] = Time.time;
                nview.InvokeRPC("RPC_TryEat", new object[0]);
                __result = true;
                return false;
            }
        }

        // The owner counted a portion off for this player: into the inventory instead of eaten.
        [HarmonyPatch(typeof(Feast), "RPC_EatConfirmation")]
        private static class Feast_RPC_EatConfirmation_Patch
        {
            private static bool Prefix(Feast __instance)
            {
                ZNetView nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return true;
                ZDOID id = nview.GetZDO().m_uid;
                float asked;
                if (!_pending.TryGetValue(id, out asked)) return true;
                _pending.Remove(id);
                if (Time.time - asked > 5f) return true;
                Player player = Player.m_localPlayer;
                FeastInfo info = InfoOf(__instance);
                if (player == null || info == null) return true;
                if (Give(player, info, 1) == 0)
                {
                    // the inventory filled up meanwhile: the portion is not lost, it stands as a feast
                    Vector3 at;
                    if (!Ground(player.transform.position + player.transform.forward, out at)) at = player.transform.position;
                    SetOut(info, 1, at, player.transform.rotation);
                    return false;
                }
                player.Message(MessageHud.MessageType.TopLeft, "$msg_added " + __instance.GetHoverName(), 1, null, false);
                return false;
            }
        }

        [HarmonyPatch(typeof(Feast), "GetHoverText")]
        private static class Feast_GetHoverText_Patch
        {
            private static void Postfix(Feast __instance, ref string __result)
            {
                if (!_takePortions.Value || string.IsNullOrEmpty(__result) || Player.m_localPlayer == null) return;
                if (Vector3.Distance(Player.m_localPlayer.transform.position, __instance.transform.position) >= __instance.m_useDistance) return;
                __result += Localization.instance.Localize("\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] ")
                    + T("Take a portion", "Взять кусок");
            }
        }

        // ---- A: the leftovers when taking a started feast up ----

        [HarmonyPatch(typeof(Player), "RemovePiece")]
        private static class Player_RemovePiece_Patch
        {
            private static void Prefix() { _removing = true; }
            private static Exception Finalizer(Exception __exception) { _removing = false; return __exception; }
        }

        // Called on the removing player's client once every check has passed; the owner of the
        // piece then destroys it and drops floor(portions left / portions) = 0 feasts.
        [HarmonyPatch(typeof(WearNTear), "Remove")]
        private static class WearNTear_Remove_Patch
        {
            private static void Prefix(WearNTear __instance)
            {
                if (!_removing || !_leftovers.Value) return;
                Feast feast = __instance.GetComponent<Feast>();
                Player player = Player.m_localPlayer;
                if (feast == null || player == null) return;
                FeastInfo info = InfoOf(feast);
                if (info == null) return;
                int left = PortionsLeft(feast);
                if (left <= 0 || left >= feast.m_eatStacks) return;
                int given = Give(player, info, left);
                if (given < left)
                {
                    // what does not fit stays where the feast stood
                    SetOut(info, left - given, __instance.transform.position, __instance.transform.rotation);
                    player.Message(MessageHud.MessageType.Center, "$inventory_full", 0, null, false);
                }
                if (given > 0)
                    player.Message(MessageHud.MessageType.TopLeft, "$msg_added " + feast.GetHoverName(), given, null, false);
            }
        }

        // ---- dropping portions ----

        // The player drops portions: they are set out as a feast in front of the player.
        [HarmonyPatch(typeof(Humanoid), "DropItem")]
        private static class Humanoid_DropItem_Patch
        {
            private static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item, int amount, ref bool __result)
            {
                FeastInfo info = InfoOf(item);
                if (info == null) return true;
                __result = false;
                Vector3 at;
                if (!_dropAsFeast.Value || !Ground(__instance.transform.position + __instance.transform.forward * 1.2f, out at))
                {
                    __instance.Message(MessageHud.MessageType.Center, "$msg_cantdrop", 0, null, false);
                    return false;
                }
                if (inventory == null) inventory = __instance.GetInventory();
                if (amount <= 0 || amount > item.m_stack) amount = item.m_stack;
                if (!inventory.RemoveItem(item, amount)) return false;
                Quaternion rot = Quaternion.Euler(0f, __instance.transform.rotation.eulerAngles.y + 180f, 0f);
                SetOut(info, amount, at, rot);
                __result = true;
                return false;
            }
        }

        // Anything else dropping portions into the world (a destroyed chest or cart spilling its
        // contents): a feast with that many portions instead of a full feast per stack.
        [HarmonyPatch(typeof(ItemDrop), "DropItem")]
        private static class ItemDrop_DropItem_Patch
        {
            private static bool Prefix(ItemDrop.ItemData item, int amount, Vector3 position, Quaternion rotation, ref ItemDrop __result)
            {
                FeastInfo info = InfoOf(item);
                if (info == null) return true;
                if (amount <= 0 || amount > item.m_stack) amount = item.m_stack;
                Vector3 at;
                if (!Ground(position, out at)) at = position;
                SetOut(info, amount, at, Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
                __result = null;
                return false;
            }
        }
    }
}
