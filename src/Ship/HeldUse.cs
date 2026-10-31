using System;
using System.Collections.Concurrent;
using Vintagestory.API.Common;

namespace Hansa
{
    /// <summary>
    /// (0.1.0-dev.7, the user on the caravel: "Trying to use telescope from Astra on deck and almost everywhere it tries to
    /// use cannon instead") What's in hand and its own right-click (pure: the client and the offline checks use it).
    /// </summary>
    public static class HeldUse
    {
        static readonly ConcurrentDictionary<Type, bool> overrides = new();

        /// <summary>
        /// Has this item a right-click of its own, not one of hers: an item (not a block), not one of Hansa's (the charter,
        /// the boat, the surveyor's chalk), that takes a right-click first (heldPriorityInteract: Astra's telescopes), is
        /// eaten or drunk, or whose class has its own OnHeldInteractStart. Aboard, such an item keeps its use unless the
        /// crosshair is on one of her fixtures.
        /// </summary>
        public static bool OwnRightClick(CollectibleObject c)
        {
            if (c == null || c is Block || c.Code?.Domain == "ships") return false;
            if (c.HeldPriorityInteract || c.NutritionProps != null) return true;
            return overrides.GetOrAdd(c.GetType(), t =>
            {
                var m = t.GetMethod(nameof(CollectibleObject.OnHeldInteractStart), new[]
                {
                    typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection), typeof(bool), typeof(EnumHandHandling).MakeByRefType()
                });
                return m != null && m.DeclaringType != typeof(CollectibleObject) && m.DeclaringType != typeof(Item);
            });
        }
    }
}
