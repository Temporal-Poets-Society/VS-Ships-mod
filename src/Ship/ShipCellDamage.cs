using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Func2 = System.Func<int, bool>;

namespace Hansa
{
    /// <summary>
    /// (0.3.28, step D2; docs/slots.md §11) Her deck cells and her pieces taking shots: a deck cell's state follows its damage
    /// part's hurt as a hull panel's does (cracked at Cracked: drawn cracked, still solid; stove in at Holed: out, an opening
    /// people fall through, on the main deck a downflooding point), but a cell something stands on stops at cracked, and a
    /// cell out stays out until it's laid again (laying it, or taking it up, leaves it sound). A partition, a door, a window
    /// or a railing keeps its hurt with it: cracked it's drawn cracked; shot away it's gone (its planks splinters).
    /// </summary>
    public partial class EntityShip
    {
        /// <summary>What a blow on her needs beyond her parts' damage: her cells out, cells held by what's on them, her pieces.</summary>
        public ShotHooks DamageHooks()
        {
            if (DamageParts == null) return null;
            return PieceDamage.Hooks(DamageParts, Grid, Pieces, Mounts, Slots != null ? n => !Slots.Filled(n) : null, Slots != null ? CellHeld : null);
        }

        /// <summary>Is a (filled) deck cell held by what's on it: a gun laid over it, a piece or a fixture of hers on it, under it or through it.</summary>
        public bool CellHeld(int n)
        {
            var l = SlotLayout;
            if (Slots == null || n < 0 || n >= l.Count || !l.Slots[n].IsCell || !Slots.Filled(n)) return false;
            return TakeProblem(n) is "hansa:cell-under-gun" or "hansa:cell-under-piece" or "hansa:cell-under-fixture";
        }

        /// <summary>
        /// Server: her filled deck cells' states as their hurt has them (cracked, stove in; a held one stops at cracked). A
        /// cell that's out stays out (taken up, a design opening, stove in) until laid. True if one changed.
        /// </summary>
        public bool SyncCellsFromHurt()
        {
            var dl = DamageParts;
            if (Slots == null || PartDamage == null || dl == null || !dl.CellSlotted) return false;
            var d = dl.D;
            bool changed = false;
            for (int n = 0; n < Slots.Count; n++)
            {
                int cp = dl.CellPart(n);
                if (cp < 0 || cp >= PartDamage.Count || !Slots.Filled(n)) continue;
                var want = HullPanelRules.StateFor(d, PartDamage.Hurt[cp]);
                if (want == SlotState.Empty && CellHeld(n))
                {
                    PartDamage.Hurt[cp] = PieceDamage.HeldCap(d);
                    want = SlotState.Cracked;
                }
                if (Slots[n] == want) continue;
                Slots.Set(n, want);
                changed = true;
            }
            return changed;
        }

        /// <summary>A deck cell laid or taken up: sound (its damage part's hurt gone, the scars on it).</summary>
        void CellMended(int n)
        {
            int cp = DamageParts?.CellPart(n) ?? -1;
            if (cp < 0 || PartDamage == null || cp >= PartDamage.Count) return;
            if (PartDamage.Hurt[cp] <= 0 && PartDamage.Wear[cp] <= 0) return;
            PartDamage.Hurt[cp] = 0;
            PartDamage.Wear[cp] = 0;
            if (Api?.Side == EnumAppSide.Server) SaveDamage(true);
        }

        /// <summary>Server, after a blow: the pieces it hurt kept (saved), those shot away gone, the sound and the notice.</summary>
        void AfterPieceHits(DamageResult res)
        {
            if (res == null || res.PieceHits.Count == 0 || DamageParts == null) return;
            var d = DamageParts.D;
            bool look = false;
            var gone = new List<ShipPiece>();
            string notice = null;
            object noticeArg = null;
            foreach (var h in res.PieceHits)
            {
                var p = Pieces.Get(h.Id);
                if (p == null) continue;
                if (h.After >= d.Holed) { if (!gone.Contains(p)) gone.Add(p); }
                else if ((h.Before >= d.Cracked) != (h.After >= d.Cracked)) { look = true; notice ??= "cracked"; noticeArg ??= PieceName(p); }
            }
            foreach (var p in gone)
            {
                PieceSound(p, new AssetLocation("sounds/effect/woodgrind"));
                notice = "gone";
                noticeArg = PieceName(p);
                World.Logger.Audit("[hansa] {0} #{1}: the {2} (piece {3}) shot away", GetName(), EntityId, p.Stack?.Collectible?.Code, p.Id);
                Pieces.Remove(p.Id);
            }
            if (gone.Count > 0 || look) Changed(); else SavePieces();
            if (notice != null && World.ElapsedMilliseconds - lastDamageNoticeMs > 5000)
            {
                lastDamageNoticeMs = World.ElapsedMilliseconds;
                if (notice == "gone") NotifyAboard("hansa:damage-piece-gone", noticeArg);
                else NotifyAboard("hansa:damage-notice", "hansa:state-cracked", noticeArg);
            }
        }

        /// <summary>The repair the held item is for on a piece (a cracked partition: boards and nails), or null.</summary>
        public RepairJob PieceRepairFor(int id, string held) =>
            DamageParts != null && DamageOn && Pieces.Get(id) is ShipPiece p && PieceDamage.Takes(p) ? PieceDamage.For(DamageParts.D, p.Hurt, held) : null;

        /// <summary>Mend a piece with what's in hand and the inventory (held right-click: the repair tool's way). Server.</summary>
        public void TryRepairPiece(IServerPlayer player, int id, string held)
        {
            var ep = player?.Entity;
            if (ep == null || !ep.Alive || DamageParts == null) return;
            var p = Pieces.Get(id);
            if (p == null) return;
            if (GoneDown) { player.SendIngameError("hansa-repair", Lang.GetL(player.LanguageCode, "hansa:repair-gone-down")); return; }
            var c = p.Center;
            if (!InReach(player, c[0], c[1], c[2])) { player.SendIngameError("hansa-repair", Lang.GetL(player.LanguageCode, "hansa:repair-too-far")); return; }
            var jobs = PieceDamage.Needs(DamageParts.D, p.Hurt);
            var job = PieceDamage.For(DamageParts.D, p.Hurt, held) ?? jobs.FirstOrDefault();
            string name = PieceName(p);
            if (job == null) { player.SendIngameError("hansa-repair", Lang.GetL(player.LanguageCode, "hansa:repair-nothing")); return; }
            if (!Creative(player))
            {
                var inv = player.InventoryManager;
                var slots = new List<ItemSlot>();
                if (inv.ActiveHotbarSlot != null) slots.Add(inv.ActiveHotbarSlot);
                foreach (string invName in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
                    if (inv.GetOwnInventory(invName) is IInventory own)
                        foreach (var s in own) if (s != null && !slots.Contains(s)) slots.Add(s);
                var have = slots.Select(s => (s?.Itemstack?.Collectible?.Code?.ToString(), s?.Itemstack?.StackSize ?? 0)).ToList();
                if (!ShipRepairs.Plan(job, have, out int[] take, out _))
                {
                    player.SendIngameError("hansa-repair", Lang.GetL(player.LanguageCode, "hansa:repair-needs", name, KitsText(job)));
                    return;
                }
                for (int i = 0; i < slots.Count; i++)
                {
                    if (take[i] <= 0) continue;
                    slots[i].TakeOut(take[i]);
                    slots[i].MarkDirty();
                }
            }
            p.Hurt = Math.Min(p.Hurt, Math.Max(0, job.HurtAfter));
            Changed();
            PieceSound(p, new AssetLocation("sounds/block/planks"));
            player.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(player.LanguageCode, "hansa:repair-done-planks", name), EnumChatType.Notification);
        }

        /// <summary>A piece's damage for the look-at text: its state, and what mends it.</summary>
        void PieceDamageInfo(int id, System.Text.StringBuilder sb)
        {
            if (DamageParts == null || !DamageOn || Pieces.Get(id) is not ShipPiece p || !PieceDamage.Takes(p)) return;
            var d = DamageParts.D;
            var st = PieceDamage.StateOf(d, p.Hurt);
            if (st == PartState.Sound) return;
            string state = Lang.Get("hansa:state-" + st.ToString().ToLowerInvariant());
            sb.AppendLine(char.ToUpperInvariant(state[0]) + state.Substring(1));
            foreach (var job in PieceDamage.Needs(d, p.Hurt).Take(1)) sb.AppendLine(Lang.Get("hansa:repair-hint-planks", KitsText(job)));
        }

        /// <summary>What a deck cell is called: "Main deck planking, amidships" (its underside from below).</summary>
        string CellText(ShipPart part)
        {
            var map = Def?.PartMap;
            var l = SlotLayout;
            if (map == null || l == null || part.CellDeck >= Def.Decks.Length) return null;
            string deck = Lang.Get("hansa:deck-" + Def.Decks[part.CellDeck].Code);
            double x = l.GX + part.CellI + 0.5;
            string at = Lang.Get("hansa:part-at-" + map.LengthZone(map.StationAt(x)));
            return Lang.Get(part.CellUnder ? "hansa:part-cell-under" : "hansa:part-cell", deck, at);
        }

        /// <summary>A deck cell's damage part by its slot id ("cell-main:5:-2" in <c>.hansa damage part</c>), or -1.</summary>
        int CellPartByName(string id) => SlotLayout?.IndexOf(id) is int n && n >= 0 ? DamageParts?.CellPart(n) ?? -1 : -1;
    }
}
