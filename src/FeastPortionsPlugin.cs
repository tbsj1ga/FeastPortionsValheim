using System;
using System.Collections.Generic;
using System.Reflection;
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
    // (ZDOVars.s_value, starting at m_eatStacks), and every portion is the feast's own food item
    // (m_foodItem) eaten with Player.EatFood. The game lets you take a feast back with the tray
    // only whole: Piece.DropResources returns floor(1 x portions left / portions).
    //
    // The portion is an item of its own, one per feast ("<feast>_Portion"): a copy of the feast
    // kit's world object (the kit is what the tray places) carrying a copy of the feast food's
    // data under its own name, icon and duration. It cannot be the food item itself: the
    // inventory stacks items and counts build materials by SharedData.m_name, and the feast's
    // food and its kit share one name - a portion would join the kits and set out a whole feast.
    // The portion prefabs are registered in ObjectDB (the inventory saves items by prefab name)
    // and in ZNetScene (so a dropped portion lies in the world).
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
            public GameObject Piece;        // the placeable feast
            public ItemDrop Food;           // the feast's own food item
            public int Portions;
            public GameObject Portion;      // the portion's prefab
            public ItemDrop.ItemData.SharedData Shared;
        }

        private sealed class Bite
        {
            public float Time;
            public bool Take;               // into the inventory, not eaten
            public bool Last;               // the feast had one portion left
        }

        private static ConfigEntry<bool> _takePortions;
        private static ConfigEntry<bool> _leftovers;
        private static ConfigEntry<bool> _removeEmpty;
        private static ConfigEntry<bool> _dropAsFeast;
        private static ConfigEntry<float> _minutes;

        private static readonly Dictionary<string, FeastInfo> _byPiece = new Dictionary<string, FeastInfo>(StringComparer.Ordinal);
        private static readonly Dictionary<string, FeastInfo> _byFood = new Dictionary<string, FeastInfo>(StringComparer.Ordinal);
        private static readonly Dictionary<string, FeastInfo> _byPortion = new Dictionary<string, FeastInfo>(StringComparer.Ordinal);
        private static readonly Dictionary<ZDOID, Bite> _bites = new Dictionary<ZDOID, Bite>();
        private static GameObject _root;
        private static bool _removing;

        private Harmony _harmony;

        private void Awake()
        {
            _takePortions = Config.Bind("01 General", "TakePortions", true,
                "Shift + E (alt use) on a placed feast puts one portion into the inventory instead of eating it.");
            _leftovers = Config.Bind("01 General", "LeftoversOnRemove", true,
                "Taking up a started feast with the serving tray gives the portions left (the game gives nothing). A whole feast still comes back whole.");
            _dropAsFeast = Config.Bind("01 General", "DropAsFeast", true,
                "Dropped portions are set out as a feast with that many portions. Off: they drop as ordinary items.");
            _removeEmpty = Config.Bind("01 General", "RemoveEmptyFeast", true,
                "The empty serving tray disappears once you take or eat the last portion (the game leaves it standing).");
            _minutes = Config.Bind("02 Portions", "Duration", 25f,
                new ConfigDescription("How long a portion lasts once eaten, in minutes. A feast eaten at the table lasts 50.",
                    new AcceptableValueRange<float>(1f, 120f)));
            _minutes.SettingChanged += delegate { foreach (FeastInfo f in _byPiece.Values) Dress(f); };

            _harmony = new Harmony(Guid);
            // one patch class at a time: a patch the game no longer fits is logged and skipped
            // instead of taking every other patch down with it
            foreach (Type t in typeof(FeastPortionsPlugin).GetNestedTypes(BindingFlags.NonPublic))
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try { _harmony.CreateClassProcessor(t).Patch(); }
                catch (Exception e) { Logger.LogError("Patch " + t.Name + " failed: " + e.Message); }
            }
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

        // ---- the portions ----

        // An inactive holder: copies made under it stay asleep (no ZNetView.Awake) and serve
        // as prefabs; the game's own Instantiate wakes its copies.
        private static Transform Root()
        {
            if (_root == null)
            {
                _root = new GameObject("FeastPortions_Prefabs");
                _root.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_root);
            }
            return _root.transform;
        }

        private static readonly MethodInfo _memberwiseClone =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        // The portion of one feast piece, made the first time the piece is seen.
        private static FeastInfo Learn(GameObject piece)
        {
            if (piece == null) return null;
            FeastInfo info;
            if (_byPiece.TryGetValue(piece.name, out info)) return info;
            Feast feast = piece.GetComponent<Feast>();
            if (feast == null) return null;
            ItemDrop food = feast.m_foodItem != null ? feast.m_foodItem : piece.GetComponent<ItemDrop>();
            if (food == null || food.m_itemData == null || food.m_itemData.m_shared == null || food.m_itemData.m_shared.m_food <= 0f) return null;
            Piece build = piece.GetComponent<Piece>();
            ItemDrop kit = null;
            if (build != null && build.m_resources != null)
                foreach (Piece.Requirement req in build.m_resources)
                    if (req != null && req.m_resItem != null) { kit = req.m_resItem; break; }
            if (kit == null) return null;

            GameObject go = UnityEngine.Object.Instantiate(kit.gameObject, Root(), false);
            go.name = piece.name + "_Portion";
            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData shared = (ItemDrop.ItemData.SharedData)_memberwiseClone.Invoke(food.m_itemData.m_shared, null);
            drop.m_itemData.m_shared = shared;
            drop.m_itemData.m_stack = 1;
            drop.m_itemData.m_dropPrefab = go;

            info = new FeastInfo();
            info.Piece = piece;
            info.Food = food;
            info.Portions = Math.Max(1, feast.m_eatStacks);
            info.Portion = go;
            info.Shared = shared;
            if (shared.m_icons != null && shared.m_icons.Length > 0 && shared.m_icons[0] != null)
                shared.m_icons = new Sprite[] { SliceIcon(shared.m_icons[0]) };
            Dress(info);
            _byPiece[piece.name] = info;
            _byFood[food.gameObject.name] = info;
            _byPortion[go.name] = info;
            return info;
        }

        // Name, description, stack and duration of a portion (again when the language or the
        // config changes; items in inventories share this data).
        private static void Dress(FeastInfo info)
        {
            ItemDrop.ItemData.SharedData food = info.Food.m_itemData.m_shared;
            string feast = Localization.instance != null ? Localization.instance.Localize(food.m_name) : food.m_name;
            string desc = Localization.instance != null ? Localization.instance.Localize(food.m_description) : food.m_description;
            info.Shared.m_name = T(feast + " portion", "Кусочек: " + feast);
            info.Shared.m_description = T("One portion of a feast, to eat on the road.", "Кусочек пира, чтобы съесть в пути.") + "\n\n" + desc;
            info.Shared.m_maxStackSize = info.Portions;
            info.Shared.m_foodBurnTime = Mathf.Max(1f, _minutes.Value) * 60f;
        }

        // The feast's icon as a slice: one wedge in full colour, the rest of the dish faded.
        // The icon sits in a sprite atlas that cannot be read directly, so it is copied through
        // a render texture. Any failure keeps the feast's own icon.
        private static Sprite SliceIcon(Sprite src)
        {
            try
            {
                Rect r = src.textureRect;
                Texture tex = src.texture;
                RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
                Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
                t.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                Color[] px = t.GetPixels();
                float cx = w * 0.5f, cy = h * 0.5f;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        // the wedge points down and right: from -75 to +15 degrees around the centre
                        float a = Mathf.Atan2(y - cy, x - cx) * Mathf.Rad2Deg;
                        if (a >= -75f && a <= 15f) continue;
                        Color c = px[y * w + x];
                        float g = c.grayscale;
                        px[y * w + x] = new Color(g, g, g, c.a * 0.3f);
                    }
                t.SetPixels(px);
                t.Apply();
                t.name = src.name + "_portion";
                return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), src.pixelsPerUnit);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[" + Name + "] portion icon kept as the feast's: " + e.Message);
                return src;
            }
        }

        // Every feast the build tools in this ObjectDB can set out; their portions go into it.
        private static void Register(ObjectDB db)
        {
            if (db == null || db.m_items == null) return;
            foreach (GameObject go in db.m_items.ToArray())
            {
                if (go == null) continue;
                ItemDrop tool = go.GetComponent<ItemDrop>();
                if (tool == null || tool.m_itemData == null || tool.m_itemData.m_shared == null) continue;
                PieceTable table = tool.m_itemData.m_shared.m_buildPieces;
                if (table == null || table.m_pieces == null) continue;
                foreach (GameObject piece in table.m_pieces) Learn(piece);
            }
            bool added = false;
            foreach (FeastInfo info in _byPiece.Values)
            {
                Dress(info);
                if (db.m_items.Contains(info.Portion)) continue;
                db.m_items.Add(info.Portion);
                added = true;
            }
            if (added) AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(db, null);
        }

        private static readonly FieldInfo _namedPrefabs = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");

        private static void Register(ZNetScene scene)
        {
            if (scene == null || scene.m_prefabs == null) return;
            foreach (GameObject go in scene.m_prefabs.ToArray()) if (go != null && go.GetComponent<Feast>() != null) Learn(go);
            Dictionary<int, GameObject> named = _namedPrefabs.GetValue(scene) as Dictionary<int, GameObject>;
            foreach (FeastInfo info in _byPiece.Values)
            {
                if (!scene.m_prefabs.Contains(info.Portion)) scene.m_prefabs.Add(info.Portion);
                if (named != null) named[info.Portion.name.GetStableHashCode()] = info.Portion;
            }
        }

        private static FeastInfo InfoOf(Feast feast)
        {
            FeastInfo info;
            return _byPiece.TryGetValue(Utils.GetPrefabName(feast.gameObject), out info) ? info : Learn(ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Utils.GetPrefabName(feast.gameObject)) : null);
        }

        private static int PortionsLeft(Feast feast)
        {
            ZNetView nview = feast.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return feast.m_eatStacks;
            return nview.GetZDO().GetInt(ZDOVars.s_value, feast.m_eatStacks);
        }

        // Up to count portions into the player's inventory, the rest on the ground at the player's
        // feet; returns how many went into the inventory.
        private static int Give(Player player, FeastInfo info, int count)
        {
            Inventory inv = player.GetInventory();
            int given = 0;
            while (given < count && inv.AddItem(info.Portion.name, 1, 1, 0, 0L, "", false, true) != null) given++;
            if (given < count)
            {
                ItemDrop.ItemData item = info.Portion.GetComponent<ItemDrop>().m_itemData.Clone();
                item.m_dropPrefab = info.Portion;
                item.m_stack = count - given;
                Transform t = player.transform;
                ItemDrop.DropItem(item, item.m_stack, t.position + t.forward * 0.7f + Vector3.up, t.rotation);
                player.Message(MessageHud.MessageType.Center, "$inventory_full", 0, null, false);
            }
            return given;
        }

        private static FeastInfo InfoOf(ItemDrop.ItemData item)
        {
            FeastInfo info;
            if (item == null || item.m_dropPrefab == null) return null;
            return _byPortion.TryGetValue(item.m_dropPrefab.name, out info) ? info : null;
        }

        // Sets out a feast (or several, one per full feast) with the given portions at a point.
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

        // Takes an emptied feast away: the owner removes it, dropping nothing.
        private static void RemoveEmpty(Feast feast)
        {
            if (!_removeEmpty.Value || feast == null) return;
            WearNTear wnt = feast.GetComponent<WearNTear>();
            if (wnt != null) wnt.Remove(true);
        }

        // ---- registration ----

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

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake_Patch
        {
            private static void Postfix(ZNetScene __instance) { Register(__instance); }
        }

        // An early build put the feast's food item itself into inventories: those become portions.
        [HarmonyPatch(typeof(Inventory), "Load", new Type[] { typeof(ZPackage) })]
        private static class Inventory_Load_Patch
        {
            private static void Postfix(Inventory __instance)
            {
                foreach (ItemDrop.ItemData item in __instance.GetAllItems())
                {
                    FeastInfo info;
                    if (item == null || item.m_dropPrefab == null || !_byFood.TryGetValue(item.m_dropPrefab.name, out info)) continue;
                    item.m_shared = info.Shared;
                    item.m_dropPrefab = info.Portion;
                }
            }
        }

        // ---- taking and eating ----

        [HarmonyPatch(typeof(Feast), "Interact")]
        private static class Feast_Interact_Patch
        {
            private static bool Prefix(Feast __instance, Humanoid human, bool hold, bool alt, ref bool __result)
            {
                ZNetView nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || hold) return true;
                int left = PortionsLeft(__instance);
                Bite bite = new Bite();
                bite.Time = Time.time;
                bite.Last = left == 1;

                if (!alt || !_takePortions.Value)
                {
                    // eating at the table: noted only to clear the tray after the last portion
                    _bites[nview.GetZDO().m_uid] = bite;
                    return true;
                }

                __result = false;
                Player player = human as Player;
                if (player == null || player != Player.m_localPlayer || left <= 0) return false;
                if (Vector3.Distance(player.transform.position, __instance.transform.position) >= __instance.m_useDistance) return false;
                FeastInfo info = InfoOf(__instance);
                if (info == null) return false;
                if (!player.GetInventory().CanAddItem(info.Portion, 1))
                {
                    player.Message(MessageHud.MessageType.Center, "$inventory_full", 0, null, false);
                    return false;
                }
                bite.Take = true;
                _bites[nview.GetZDO().m_uid] = bite;
                nview.InvokeRPC("RPC_TryEat", new object[0]);
                __result = true;
                return false;
            }
        }

        // The owner counted a portion off for this player: into the inventory when taken, eaten
        // otherwise; after the last one the empty tray goes away.
        [HarmonyPatch(typeof(Feast), "RPC_EatConfirmation")]
        private static class Feast_RPC_EatConfirmation_Patch
        {
            private static bool Prefix(Feast __instance, out Bite __state)
            {
                __state = null;
                ZNetView nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return true;
                ZDOID id = nview.GetZDO().m_uid;
                Bite bite;
                if (!_bites.TryGetValue(id, out bite)) return true;
                _bites.Remove(id);
                if (Time.time - bite.Time > 5f) return true;
                __state = bite;
                if (!bite.Take) return true;
                Player player = Player.m_localPlayer;
                FeastInfo info = InfoOf(__instance);
                if (player == null || info == null) return true;
                if (Give(player, info, 1) > 0)
                    player.Message(MessageHud.MessageType.TopLeft, "$msg_added " + info.Shared.m_name, 1, info.Shared.m_icons[0], false);
                return false;
            }

            private static void Postfix(Feast __instance, Bite __state)
            {
                if (__state != null && __state.Last) RemoveEmpty(__instance);
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
                    + T("Take a portion", "Взять кусочек");
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
                if (info == null || !_dropAsFeast.Value) return true;
                __result = false;
                Vector3 at;
                if (!Ground(__instance.transform.position + __instance.transform.forward * 1.2f, out at))
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

        // Anything else dropping portions into the world (a full inventory, a destroyed chest or
        // cart spilling its contents): a feast with that many portions as well.
        [HarmonyPatch(typeof(ItemDrop), "DropItem")]
        private static class ItemDrop_DropItem_Patch
        {
            private static bool Prefix(ItemDrop.ItemData item, int amount, Vector3 position, Quaternion rotation, ref ItemDrop __result)
            {
                FeastInfo info = InfoOf(item);
                if (info == null || !_dropAsFeast.Value) return true;
                if (amount <= 0 || amount > item.m_stack) amount = item.m_stack;
                Vector3 at;
                if (!Ground(position, out at)) at = position;
                SetOut(info, amount, at, Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
                __result = null;
                return false;
            }
        }

        // ---- the leftovers when taking a started feast up ----

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
                if (given > 0)
                    player.Message(MessageHud.MessageType.TopLeft, "$msg_added " + info.Shared.m_name, given, info.Shared.m_icons[0], false);
            }
        }
    }
}
