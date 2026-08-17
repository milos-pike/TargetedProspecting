using System;
using Vintagestory.API.Common;

namespace TargetedProspecting
{
    internal static class TargetedProspectingCooldown
    {
        internal const double SurveyCooldownDays = 1d;

        internal const string AttributeKey =
            "targetedprospecting-nextallowedtotaldays";

        internal static double GetRemainingDays(
            IWorldAccessor world,
            IPlayer player
        )
        {
            if (
                !player.Entity.WatchedAttributes.HasAttribute(
                    AttributeKey
                )
            )
            {
                return 0d;
            }

            double nextAllowedTotalDays =
                player.Entity.WatchedAttributes.GetDouble(
                    AttributeKey,
                    0d
                );

            return Math.Max(
                0d,
                nextAllowedTotalDays
                    - world.Calendar.TotalDays
            );
        }

        internal static void Start(
            IWorldAccessor world,
            IPlayer player
        )
        {
            double nextAllowedTotalDays =
                world.Calendar.TotalDays
                + SurveyCooldownDays;

            player.Entity.WatchedAttributes.SetDouble(
                AttributeKey,
                nextAllowedTotalDays
            );

            player.Entity.WatchedAttributes.MarkPathDirty(
                AttributeKey
            );
        }

        internal static void Reset(
            IPlayer player
        )
        {
            player.Entity.WatchedAttributes.RemoveAttribute(
                AttributeKey
            );

            player.Entity.WatchedAttributes.MarkPathDirty(
                AttributeKey
            );
        }
    }
}
