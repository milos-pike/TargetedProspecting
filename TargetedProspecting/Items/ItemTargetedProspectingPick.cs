using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

#nullable disable

namespace TargetedProspecting.Items
{
    public class ItemTargetedProspectingPick
        : ItemProspectingPick
    {
        private const string DiamondOreName =
            "diamond";

        private const string EmeraldOreName =
            "emerald";

        private const string PeridotOreName =
            "olivine_peridot";

        private const string WaypointKeyPrefix =
            "targetedprospecting:v1";

        private const string OreWaypointCategory =
            "ore";

        private const string GemWaypointCategory =
            "gem";

        private const int ScanColumnsPerSlice = 4;

        private static readonly HashSet<string>
            SupportedUngradedMineralCodes = new(
                StringComparer.OrdinalIgnoreCase
            )
            {
                "alum",
                "lapislazuli",
                "anthracite",
                "borax",
                "cinnabar",
                "lignite",
                "bituminouscoal",
                "sylvite",
                "quartz",
                "olivine",
                "sulfur"
            };

        private static readonly ConditionalWeakTable<
            EntityPlayer,
            TargetedBreakingState
        > TargetedBreakingStates = new();

        private static readonly ConditionalWeakTable<
            ItemStack,
            object
        > BlockBreakingDamageSuppressedStacks = new();

        private SkillItem[] targetedProspectingToolModes;
        private int targetedProspectingModeIndex;

        private static string BuildWaypointKey(
            string category,
            string mineralCode,
            int dimension,
            int centerBlockX,
            int centerBlockZ
        )
        {
            return
                $"{WaypointKeyPrefix}|{category}|{mineralCode}|{dimension}|{centerBlockX}|{centerBlockZ}";
        }

        private static bool HasMatchingTargetedProspectingWaypoint(
            WaypointMapLayer waypointMapLayer,
            IServerPlayer serverPlayer,
            string waypointKey
        )
        {
            foreach (
                Waypoint existingWaypoint
                in waypointMapLayer.Waypoints
            )
            {
                if (
                    existingWaypoint.OwningPlayerUid
                        == serverPlayer.PlayerUID
                    &&
                    existingWaypoint.Text
                        == waypointKey
                )
                {
                    return true;
                }
            }

            return false;
        }
        private static bool TryApplySurveySatietyCost(
            IWorldAccessor world,
            IPlayer player,
            out float satietyMaximum,
            out float satietyBefore,
            out float satietyCostCalculated,
            out float satietyAfter
        )
        {
            satietyMaximum = 0f;
            satietyBefore = 0f;
            satietyCostCalculated = 0f;
            satietyAfter = 0f;

            if (world.Side != EnumAppSide.Server)
            {
                return false;
            }

            EnumGameMode gameMode =
                player.WorldData.CurrentGameMode;

            if (
                gameMode == EnumGameMode.Creative
                || gameMode == EnumGameMode.Spectator
            )
            {
                return false;
            }

            EntityBehaviorHunger hunger =
                player.Entity
                    .GetBehavior<EntityBehaviorHunger>();

            if (hunger == null)
            {
                return false;
            }

            float maximumSatiety =
                Math.Max(
                    0f,
                    hunger.MaxSaturation
                );

            satietyMaximum = maximumSatiety;
            satietyBefore = hunger.Saturation;

            float satietyCost =
                maximumSatiety * 0.5f;

            satietyCostCalculated = satietyCost;

            hunger.Saturation =
                Math.Max(
                    0f,
                    hunger.Saturation - satietyCost
                );

            satietyAfter = hunger.Saturation;

            return true;
        }
        private static int GetMaximumDepositCount(
            ItemStack propickStack
        )
        {
            string codePath =
                propickStack?.Collectible?.Code?.Path;

            switch (codePath)
            {
                case "prospectingpick-copper":
                    return 1;

                case "prospectingpick-tinbronze":
                case "prospectingpick-bismuthbronze":
                case "prospectingpick-blackbronze":
                    return 2;

                case "prospectingpick-iron":
                case "prospectingpick-meteoriciron":
                    return 3;

                case "prospectingpick-steel":
                    return 4;

                default:
                    return 0;
            }
        }
        private static int GetSurveyDurabilityCost(
            ItemStack propickStack,
            int returnedDepositCount
        )
        {
            if (returnedDepositCount > 0)
            {
                return returnedDepositCount * 75;
            }

            return 1;
        }
        private static bool TryApplySurveyDurabilityCost(
            IWorldAccessor world,
            IPlayer player,
            ItemSlot propickSlot,
            ItemStack propickStack,
            int returnedDepositCount,
            out int durabilityBefore,
            out int durabilityAfter,
            out int durabilityCost,
            out string interruptionMessage
        )
        {
            bool testLoggingEnabled =
                TargetedProspectingTestLogger.Enabled;

            durabilityBefore =
                testLoggingEnabled
                    ? propickStack.Collectible
                        .GetRemainingDurability(
                            propickStack
                        )
                    : 0;

            durabilityAfter =
                durabilityBefore;

            durabilityCost = 0;
            interruptionMessage = null;

            if (
                !ReferenceEquals(
                    propickSlot.Itemstack,
                    propickStack
                )
            )
            {
                if (testLoggingEnabled)
                {
                    durabilityCost =
                        GetSurveyDurabilityCost(
                            propickStack,
                            returnedDepositCount
                        );
                }

                if (player is IServerPlayer serverPlayer)
                {
                    interruptionMessage = Lang.GetL(
                        serverPlayer.LanguageCode,
                        "targetedprospecting:survey-interrupted"
                    );
                }
                else
                {
                    interruptionMessage = Lang.Get(
                        "targetedprospecting:survey-interrupted"
                    );
                }

                SendTargetedProspectingMessage(
                    world,
                    player,
                    interruptionMessage
                );

                return false;
            }

            if (
                player.WorldData.CurrentGameMode
                == EnumGameMode.Creative
            )
            {
                if (testLoggingEnabled)
                {
                    durabilityCost =
                        GetSurveyDurabilityCost(
                            propickStack,
                            returnedDepositCount
                        );
                }

                return true;
            }

            durabilityCost =
                GetSurveyDurabilityCost(
                    propickStack,
                    returnedDepositCount
                );

            propickStack.Collectible.DamageItem(
                world,
                player.Entity,
                propickSlot,
                durabilityCost
            );

            if (testLoggingEnabled)
            {
                ItemStack remainingStack =
                    propickSlot.Itemstack;

                durabilityAfter =
                    remainingStack == null
                        ? 0
                        : remainingStack.Collectible
                            .GetRemainingDurability(
                                remainingStack
                            );
            }

            return true;
        }
        public override void OnConsumedByCrafting(
            ItemSlot[] allInputSlots,
            ItemSlot stackInSlot,
            IRecipeBase recipe,
            IRecipeIngredient fromIngredient,
            IPlayer byPlayer,
            int quantity
        )
        {
            if (
                !TargetedProspectingDisassembly
                    .IsDisassemblyRecipe(recipe)
            )
            {
                base.OnConsumedByCrafting(
                    allInputSlots,
                    stackInSlot,
                    recipe,
                    fromIngredient,
                    byPlayer,
                    quantity
                );

                return;
            }

            TargetedProspectingDisassembly.ConsumptionState
                disassemblyState =
                    TargetedProspectingDisassembly
                        .PrepareConsumption(
                            allInputSlots,
                            stackInSlot,
                            recipe
                        );

            base.OnConsumedByCrafting(
                allInputSlots,
                stackInSlot,
                recipe,
                fromIngredient,
                byPlayer,
                quantity
            );

            TargetedProspectingDisassembly
                .FinishConsumption(
                    disassemblyState,
                    byPlayer
                );
        }

        private static void DrawTargetedSearchIcon(
            Cairo.Context context,
            int x,
            int y,
            float width,
            float height,
            double[] rgba
        )
        {
            double centerX =
                x + width / 2.0;

            double centerY =
                y + height / 2.0;

            double radius =
                Math.Min(width, height) / 2.0;

            context.Save();

            context.SetSourceRGBA(
                rgba[0],
                rgba[1],
                rgba[2],
                rgba[3]
            );

            context.LineWidth = 1.6;

            context.NewPath();
            context.Arc(
                centerX,
                centerY,
                radius * 0.82,
                0,
                Math.PI * 2
            );
            context.Stroke();

            context.NewPath();
            context.Arc(
                centerX,
                centerY,
                radius * 0.58,
                0,
                Math.PI * 2
            );
            context.Stroke();

            context.NewPath();
            context.Arc(
                centerX,
                centerY,
                radius * 0.34,
                0,
                Math.PI * 2
            );
            context.Stroke();

            context.NewPath();
            context.Arc(
                centerX,
                centerY,
                radius * 0.10,
                0,
                Math.PI * 2
            );
            context.Fill();

            context.Restore();
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            SkillItem[] vanillaModes =
                base.GetToolModes(null, null, null);

            targetedProspectingModeIndex = vanillaModes.Length;

            targetedProspectingToolModes =
                new SkillItem[vanillaModes.Length + 1];

            Array.Copy(
                vanillaModes,
                targetedProspectingToolModes,
                vanillaModes.Length
            );

            SkillItem targetedSearchMode = new SkillItem
            {
                Code = new AssetLocation(
                    "targetedprospecting:targeted-search"
                ),
                Name = Lang.Get(
                    "targetedprospecting:toolmode-targeted-search"
                )
            };

            if (api is ICoreClientAPI clientApi)
            {
                targetedSearchMode.WithIcon(
                    clientApi,
                    DrawTargetedSearchIcon
                );
            }

            targetedProspectingToolModes[targetedProspectingModeIndex] =
                targetedSearchMode;
        }

        public override SkillItem[] GetToolModes(
            ItemSlot slot,
            IClientPlayer forPlayer,
            BlockSelection blockSelection
        )
        {
            return targetedProspectingToolModes;
        }

        public override int GetToolMode(
            ItemSlot slot,
            IPlayer byPlayer,
            BlockSelection blockSelection
        )
        {
            int storedMode =
                slot.Itemstack.Attributes.GetInt(
                    "toolMode"
                );

            return Math.Min(
                targetedProspectingToolModes.Length - 1,
                Math.Max(0, storedMode)
            );
        }

        public override void SetToolMode(
            ItemSlot slot,
            IPlayer byPlayer,
            BlockSelection blockSelection,
            int toolMode
        )
        {
            BlockBreakingDamageSuppressedStacks.Remove(
                slot.Itemstack
            );

            slot.Itemstack.Attributes.SetInt(
                "toolMode",
                toolMode
            );
        }

        public override EnumItemDamageSource[] GetDamagedBy(
            ItemSlot slot
        )
        {
            ItemStack stack =
                slot?.Itemstack;

            if (
                stack == null
                ||
                !BlockBreakingDamageSuppressedStacks.TryGetValue(
                    stack,
                    out _
                )
            )
            {
                return base.GetDamagedBy(slot);
            }

            EnumItemDamageSource[] damagedBy =
                base.GetDamagedBy(slot);

            if (damagedBy == null)
            {
                return null;
            }

            List<EnumItemDamageSource> filteredSources =
                new List<EnumItemDamageSource>(
                    damagedBy.Length
                );

            foreach (
                EnumItemDamageSource damageSource
                in damagedBy
            )
            {
                if (
                    damageSource
                        != EnumItemDamageSource.BlockBreaking
                )
                {
                    filteredSources.Add(
                        damageSource
                    );
                }
            }

            return filteredSources.ToArray();
        }

        public override float OnBlockBreaking(
            IPlayer player,
            BlockSelection blockSelection,
            ItemSlot itemSlot,
            float remainingResistance,
            float dt,
            int counter
        )
        {
            int toolMode = GetToolMode(
                itemSlot,
                player,
                blockSelection
            );

            if (counter == 0)
            {
                BlockBreakingDamageSuppressedStacks.Remove(
                    itemSlot.Itemstack
                );
            }

            if (toolMode != targetedProspectingModeIndex)
            {
                return base.OnBlockBreaking(
                    player,
                    blockSelection,
                    itemSlot,
                    remainingResistance,
                    dt,
                    counter
                );
            }

            Block targetedBlock =
                player.Entity.World.BlockAccessor.GetBlock(
                    blockSelection.Position
                );

            if (!IsPropickable(targetedBlock))
            {
                if (
                    counter == 0
                    &&
                    player.WorldData.CurrentGameMode
                        == EnumGameMode.Creative
                    &&
                    itemSlot.Itemstack != null
                )
                {
                    BlockBreakingDamageSuppressedStacks.Add(
                        itemSlot.Itemstack,
                        new object()
                    );
                }

                return base.OnBlockBreaking(
                    player,
                    blockSelection,
                    itemSlot,
                    remainingResistance,
                    dt,
                    counter
                );
            }

            ItemStack propickStack =
                itemSlot.Itemstack;

            ItemStack sampleStack =
                player.InventoryManager
                    .OffhandHotbarSlot?.Itemstack;

            string validationError =
                GetValidationError(
                    player,
                    propickStack,
                    sampleStack
                );

            if (validationError != null)
            {
                if (counter == 0)
                {
                    SendTargetedProspectingMessage(
                        player.Entity.World,
                        player,
                        validationError
                    );

                    TargetedProspectingTestLogger
                        .WriteSurveyRejected(
                            "breaking-start",
                            "validation",
                            player.Entity.World,
                            player,
                            propickStack,
                            sampleStack,
                            blockSelection.Position,
                            null,
                            validationError
                        );
                }

                return remainingResistance;
            }
            EnumGameMode gameMode =
                player.WorldData.CurrentGameMode;

            if (
                gameMode != EnumGameMode.Creative
                && gameMode != EnumGameMode.Spectator
            )
            {
                double remainingCooldownDays =
                    TargetedProspectingCooldown.GetRemainingDays(
                        player.Entity.World,
                        player
                    );

                if (remainingCooldownDays > 0d)
                {
                    if (counter == 0)
                    {
                        double remainingHours =
                            remainingCooldownDays
                            * player.Entity.World.Calendar
                                .HoursPerDay;

                        string cooldownMessage;

                        if (player is IServerPlayer serverPlayer)
                        {
                            string formattedRemainingHours =
                                remainingHours.ToString(
                                    "0.0",
                                    System.Globalization.CultureInfo
                                        .GetCultureInfo(
                                            serverPlayer.LanguageCode
                                        )
                                );

                            cooldownMessage = Lang.GetL(
                                serverPlayer.LanguageCode,
                                "targetedprospecting:survey-cooldown",
                                formattedRemainingHours
                            );
                        }
                        else
                        {
                            cooldownMessage = Lang.Get(
                                "targetedprospecting:survey-cooldown",
                                remainingHours.ToString(
                                   "0.0",
                                   System.Globalization.CultureInfo
                                       .GetCultureInfo(
                                            Lang.CurrentLocale
                                        )
                                )
                            );
                        }

                        SendTargetedProspectingMessage(
                            player.Entity.World,
                            player,
                            cooldownMessage
                        );

                        TargetedProspectingTestLogger
                            .WriteSurveyRejected(
                                "breaking-start",
                                "cooldown",
                                player.Entity.World,
                                player,
                                propickStack,
                                sampleStack,
                                blockSelection.Position,
                                "targetedprospecting:survey-cooldown",
                                cooldownMessage,
                                remainingCooldownDays,
                                remainingHours
                            );
                    }

                    return remainingResistance;
                }
            }

            if (counter == 0)
            {
                bool preserveExistingState =
                    TargetedBreakingStates.TryGetValue(
                        player.Entity,
                        out TargetedBreakingState
                            existingBreakingState
                    )
                    &&
                    existingBreakingState.MatchesPosition(
                        blockSelection.Position
                    )
                    &&
                    !existingBreakingState.MatchesTool(
                        itemSlot
                    );

                if (!preserveExistingState)
                {
                    TargetedBreakingStates.Remove(
                        player.Entity
                    );

                    TargetedBreakingStates.Add(
                        player.Entity,
                        new TargetedBreakingState(
                            propickStack,
                            blockSelection.Position
                        )
                    );
                }
            }

            return base.OnBlockBreaking(
                player,
                blockSelection,
                itemSlot,
                remainingResistance,
                dt * 0.5f,
                counter
            );
        }

        public override bool OnBlockBrokenWith(
            IWorldAccessor world,
            Entity byEntity,
            ItemSlot itemSlot,
            BlockSelection blockSelection,
            float dropQuantityMultiplier = 1
        )
        {
            EntityPlayer entityPlayer =
                byEntity as EntityPlayer;

            if (entityPlayer == null)
            {
                return base.OnBlockBrokenWith(
                    world,
                    byEntity,
                    itemSlot,
                    blockSelection,
                    dropQuantityMultiplier
                );
            }

            IPlayer player =
                entityPlayer.Player;

            int toolMode = GetToolMode(
                itemSlot,
                player,
                blockSelection
            );

            TargetedBreakingStates.TryGetValue(
                entityPlayer,
                out TargetedBreakingState breakingState
            );

            TargetedBreakingStates.Remove(
                entityPlayer
            );

            if (
                breakingState != null
                &&
                breakingState.MatchesPosition(
                    blockSelection.Position
                )
                &&
                (
                    toolMode
                        != targetedProspectingModeIndex
                    ||
                    !breakingState.MatchesTool(
                        itemSlot
                    )
                )
            )
            {
                string interruptionMessage;

                if (player is IServerPlayer serverPlayer)
                {
                    interruptionMessage = Lang.GetL(
                        serverPlayer.LanguageCode,
                        "targetedprospecting:survey-interrupted"
                    );
                }
                else
                {
                    interruptionMessage = Lang.Get(
                        "targetedprospecting:survey-interrupted"
                    );
                }

                SendTargetedProspectingMessage(
                    world,
                    player,
                    interruptionMessage
                );

                TargetedProspectingTestLogger
                    .WriteSurveyRejected(
                        "break-complete",
                        "tool-changed",
                        world,
                        player,
                        itemSlot.Itemstack,
                        player.InventoryManager
                            .OffhandHotbarSlot?.Itemstack,
                        blockSelection.Position,
                        "targetedprospecting:survey-interrupted",
                        interruptionMessage
                    );

                return true;
            }

            if (toolMode != targetedProspectingModeIndex)
            {
                return base.OnBlockBrokenWith(
                    world,
                    byEntity,
                    itemSlot,
                    blockSelection,
                    dropQuantityMultiplier
                );
            }

            ItemStack propickStack =
                itemSlot.Itemstack;

            ItemStack sampleStack =
                player.InventoryManager
                    .OffhandHotbarSlot?.Itemstack;

            Block targetedBlock =
                world.BlockAccessor.GetBlock(
                    blockSelection.Position
                );

            if (!IsPropickable(targetedBlock))
            {
                TargetedProspectingTestLogger
                    .WriteSurveyRejected(
                        "break-complete",
                        "block-not-propickable",
                        world,
                        player,
                        propickStack,
                        sampleStack,
                        blockSelection.Position,
                        null,
                        null
                    );

                if (
                    player.WorldData.CurrentGameMode
                        == EnumGameMode.Creative
                )
                {
                    targetedBlock.OnBlockBroken(
                        world,
                        blockSelection.Position,
                        player,
                        dropQuantityMultiplier
                    );

                    return true;
                }

                return base.OnBlockBrokenWith(
                    world,
                    byEntity,
                    itemSlot,
                    blockSelection,
                    dropQuantityMultiplier
                );
            }

            string validationError =
                GetValidationError(
                    player,
                    propickStack,
                    sampleStack
                );

            if (validationError != null)
            {
                SendTargetedProspectingMessage(
                    world,
                    player,
                    validationError
                );

                TargetedProspectingTestLogger
                    .WriteSurveyRejected(
                        "break-complete",
                        "validation",
                        world,
                        player,
                        propickStack,
                        sampleStack,
                        blockSelection.Position,
                        null,
                        validationError
                    );

                return true;
            }

            string mineralCode =
                GetSampleMineralCode(
                    sampleStack
                );

            if (mineralCode == null)
            {
                string unknownMineralMessage;

                if (player is IServerPlayer serverPlayer)
                {
                    unknownMineralMessage = Lang.GetL(
                        serverPlayer.LanguageCode,
                        "targetedprospecting:unknown-mineral"
                    );
                }
                else
                {
                    unknownMineralMessage = Lang.Get(
                        "targetedprospecting:unknown-mineral"
                    );
                }

                SendTargetedProspectingMessage(
                    world,
                    player,
                    unknownMineralMessage
                );

                TargetedProspectingTestLogger
                    .WriteSurveyRejected(
                        "break-complete",
                        "unknown-mineral",
                        world,
                        player,
                        propickStack,
                        sampleStack,
                        blockSelection.Position,
                        "targetedprospecting:unknown-mineral",
                        unknownMineralMessage
                    );

                return true;
            }

            if (world.Side == EnumAppSide.Server)
            {
                EnumGameMode gameMode =
                    player.WorldData.CurrentGameMode;

                if (
                    gameMode != EnumGameMode.Creative
                    && gameMode != EnumGameMode.Spectator
                )
                {
                    double remainingCooldownDays =
                        TargetedProspectingCooldown.GetRemainingDays(
                            world,
                            player
                        );

                    if (remainingCooldownDays > 0d)
                    {
                        double remainingHours =
                            remainingCooldownDays
                            * world.Calendar.HoursPerDay;

                        string cooldownMessage;

                        if (player is IServerPlayer serverPlayer)
                        {
                            string formattedRemainingHours =
                                remainingHours.ToString(
                                    "0.0",
                                    System.Globalization.CultureInfo
                                        .GetCultureInfo(
                                            serverPlayer.LanguageCode
                                        )
                                );

                            cooldownMessage = Lang.GetL(
                                serverPlayer.LanguageCode,
                                "targetedprospecting:survey-cooldown",
                                formattedRemainingHours
                            );
                        }
                        else
                        {
                            cooldownMessage = Lang.Get(
                                "targetedprospecting:survey-cooldown",
                                remainingHours.ToString(
                                    "0.0",
                                    System.Globalization.CultureInfo
                                        .GetCultureInfo(
                                            Lang.CurrentLocale
                                        )
                                )
                            );
                        }

                        SendTargetedProspectingMessage(
                            world,
                            player,
                            cooldownMessage
                        );

                        TargetedProspectingTestLogger
                            .WriteSurveyRejected(
                                "break-complete",
                                "cooldown",
                                world,
                                player,
                                propickStack,
                                sampleStack,
                                blockSelection.Position,
                                "targetedprospecting:survey-cooldown",
                                cooldownMessage,
                                remainingCooldownDays,
                                remainingHours
                            );

                        return true;
                    }
                }
            }

            Block brokenBlock = targetedBlock;

            // The surveyed block is always destroyed
            // and never produces a drop.
            brokenBlock.OnBlockBroken(
                world,
                blockSelection.Position,
                player,
                0f
            );

            if (world.Side == EnumAppSide.Server)
            {
                string surveyId =
                    TargetedProspectingTestLogger
                        .CreateSurveyId();

                EnumGameMode gameMode =
                    player.WorldData.CurrentGameMode;

                bool cooldownStarted = false;

                if (
                    gameMode != EnumGameMode.Creative
                    && gameMode != EnumGameMode.Spectator
                )
                {
                    TargetedProspectingCooldown.Start(
                        world,
                        player
                    );

                    cooldownStarted = true;
                }

                bool satietyCostApplied =
                    TryApplySurveySatietyCost(
                        world,
                        player,
                        out float satietyMaximum,
                        out float satietyBefore,
                        out float satietyCostCalculated,
                        out float satietyAfter
                    );

                BlockPos surveyPosition =
                    blockSelection.Position.Copy();

                bool includeGemBonus =
                    IsSteelProspectingPick(
                        propickStack
                    );

                ScheduleScanSlice(
                    world,
                    _ =>
                    {
                        RunDepositScan(
                            world,
                            player,
                            surveyId,
                            itemSlot,
                            propickStack,
                            surveyPosition,
                            mineralCode,
                            satietyCostApplied
                        );

                        if (includeGemBonus)
                        {
                            RunGemBonusScan(
                                world,
                                player,
                                surveyId,
                                surveyPosition
                            );
                        }
                    }
                );

                TargetedProspectingTestLogger
                    .WriteSurveyStarted(
                        surveyId,
                        world,
                        player,
                        propickStack,
                        sampleStack,
                        brokenBlock,
                        surveyPosition,
                        mineralCode,
                        cooldownStarted,
                        satietyCostApplied,
                        satietyMaximum,
                        satietyBefore,
                        satietyCostCalculated,
                        satietyAfter,
                        includeGemBonus
                    );
            }

            return true;
        }

        private static bool IsPropickable(
            Block block
        )
        {
            return block?.Attributes?["propickable"]
                .AsBool(false) == true;
        }

        private static void SendSurveySatietyMessage(
            IWorldAccessor world,
            IPlayer player,
            bool satietyCostApplied
        )
        {
            if (!satietyCostApplied)
            {
                return;
            }

            string satietyMessage;

            if (player is IServerPlayer serverPlayer)
            {
                satietyMessage = Lang.GetL(
                    serverPlayer.LanguageCode,
                    "targetedprospecting:satiety-drained"
                );
            }
            else
            {
                satietyMessage = Lang.Get(
                    "targetedprospecting:satiety-drained"
                );
            }

            SendTargetedProspectingMessage(
                world,
                player,
                satietyMessage
            );
        }
        private static void ScheduleScanSlice(
    IWorldAccessor world,
    Action<float> callback
)
        {
            world.Api.Event.RegisterCallback(
                callback,
                1
            );
        }

        private static void ScanDepositSlice(
    IWorldAccessor world,
    BlockPos minPosition,
    BlockPos maxPosition,
    int sliceMinX,
    string mineralCode,
    HashSet<OrePosition> matchingPositions
)
        {
            BlockPos sliceMinPosition =
                minPosition.Copy();

            sliceMinPosition.X =
                sliceMinX;

            BlockPos sliceMaxPosition =
                maxPosition.Copy();

            sliceMaxPosition.X =
                Math.Min(
                    sliceMinX + ScanColumnsPerSlice - 1,
                    maxPosition.X
                );

            world.BlockAccessor.WalkBlocks(
                sliceMinPosition,
                sliceMaxPosition,
                (
                    Block block,
                    int x,
                    int y,
                    int z
                ) =>
                {
                    if (
                        IsMatchingDepositBlock(
                            block,
                            mineralCode
                        )
                    )
                    {
                        bool isHalite =
                            string.Equals(
                                mineralCode,
                                "halite",
                                StringComparison.OrdinalIgnoreCase
                            );

                        // Halite deposits can be very large.
                        // Normalize Y so each X/Z column is stored once.
                        matchingPositions.Add(
                            new OrePosition(
                                x,
                                isHalite ? 0 : y,
                                z
                            )
                        );
                    }
                }
            );
        }
        private static bool IsMatchingDepositBlock(
            Block block,
            string mineralCode
        )
        {
            if (
                block is BlockOre oreBlock
                &&
                string.Equals(
                    oreBlock.OreName,
                    mineralCode,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            return
                string.Equals(
                    mineralCode,
                    "halite",
                    StringComparison.OrdinalIgnoreCase
                )
                &&
                (
                    string.Equals(
                        block?.Code?.Path,
                        "rock-halite",
                        StringComparison.OrdinalIgnoreCase
                    )
                    ||
                    string.Equals(
                        block?.Code?.Path,
                        "crackedrock-halite",
                        StringComparison.OrdinalIgnoreCase
                    )
                );
        }

        private static void ScanGemSlice(
    IWorldAccessor world,
    BlockPos minPosition,
    BlockPos maxPosition,
    int sliceMinX,
    HashSet<OrePosition> diamondPositions,
    HashSet<OrePosition> emeraldPositions,
    HashSet<OrePosition> peridotPositions
)
        {
            BlockPos sliceMinPosition =
                minPosition.Copy();

            sliceMinPosition.X =
                sliceMinX;

            BlockPos sliceMaxPosition =
                maxPosition.Copy();

            sliceMaxPosition.X =
                Math.Min(
                    sliceMinX + ScanColumnsPerSlice - 1,
                    maxPosition.X
                );

            world.BlockAccessor.WalkBlocks(
                sliceMinPosition,
                sliceMaxPosition,
                (
                    Block block,
                    int x,
                    int y,
                    int z
                ) =>
                {
                    string gemMineralCode =
                        GetGemMineralCode(block);

                    HashSet<OrePosition> targetPositions =
                        null;

                    if (
                        string.Equals(
                            gemMineralCode,
                            DiamondOreName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        targetPositions =
                            diamondPositions;
                    }
                    else if (
                        string.Equals(
                            gemMineralCode,
                            EmeraldOreName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        targetPositions =
                            emeraldPositions;
                    }
                    else if (
                        string.Equals(
                            gemMineralCode,
                            PeridotOreName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        targetPositions =
                            peridotPositions;
                    }

                    if (targetPositions != null)
                    {
                        targetPositions.Add(
                            new OrePosition(
                                x,
                                y,
                                z
                            )
                        );
                    }
                }
            );
        }
        private static void ScanGemAreaIncrementally(
    IWorldAccessor world,
    BlockPos minPosition,
    BlockPos maxPosition,
    int sliceMinX,
    HashSet<OrePosition> diamondPositions,
    HashSet<OrePosition> emeraldPositions,
    HashSet<OrePosition> peridotPositions,
    Action completed
)
        {
            if (sliceMinX > maxPosition.X)
            {
                completed();
                return;
            }

            ScanGemSlice(
                world,
                minPosition,
                maxPosition,
                sliceMinX,
                diamondPositions,
                emeraldPositions,
                peridotPositions
            );

            ScheduleScanSlice(
                world,
                _ =>
                {
                    ScanGemAreaIncrementally(
                        world,
                        minPosition,
                        maxPosition,
                        sliceMinX + ScanColumnsPerSlice,
                        diamondPositions,
                        emeraldPositions,
                        peridotPositions,
                        completed
                    );
                }
            );
        }
        private static void ScanDepositAreaIncrementally(
    IWorldAccessor world,
    BlockPos minPosition,
    BlockPos maxPosition,
    int sliceMinX,
    string mineralCode,
    HashSet<OrePosition> matchingPositions,
    TargetedProspectingScanTiming scanTiming,
    Action completed
)
        {
            if (sliceMinX > maxPosition.X)
            {
                completed();
                return;
            }

            long sliceStartedTimestamp =
                scanTiming?.StartWork() ?? 0L;

            ScanDepositSlice(
                world,
                minPosition,
                maxPosition,
                sliceMinX,
                mineralCode,
                matchingPositions
            );

            scanTiming?.RecordSlice(
                sliceStartedTimestamp
            );

            ScheduleScanSlice(
                world,
                _ =>
                {
                    ScanDepositAreaIncrementally(
                        world,
                        minPosition,
                        maxPosition,
                        sliceMinX + ScanColumnsPerSlice,
                        mineralCode,
                        matchingPositions,
                        scanTiming,
                        completed
                    );
                }
            );
        }
        private static void RunDepositScan(
            IWorldAccessor world,
            IPlayer player,
            string surveyId,
            ItemSlot propickSlot,
            ItemStack propickStack,
            BlockPos surveyPosition,
            string mineralCode,
            bool satietyCostApplied
        )
        {
            int chunkSize =
                GlobalConstants.ChunkSize;

            int centerChunkX =
                FloorDivide(
                    surveyPosition.X,
                    chunkSize
                );

            int centerChunkZ =
                FloorDivide(
                    surveyPosition.Z,
                    chunkSize
                );

            int minX =
                (centerChunkX - 1) * chunkSize;

            int minZ =
                (centerChunkZ - 1) * chunkSize;

            int maxX =
                minX + chunkSize * 3 - 1;

            int maxZ =
                minZ + chunkSize * 3 - 1;

            BlockPos minPosition =
                surveyPosition.Copy();

            minPosition.X = minX;
            minPosition.Y = 0;
            minPosition.Z = minZ;

            BlockPos maxPosition =
                surveyPosition.Copy();

            maxPosition.X = maxX;
            maxPosition.Y =
                world.BlockAccessor.MapSizeY - 1;
            maxPosition.Z = maxZ;

            HashSet<OrePosition> matchingPositions =
                new HashSet<OrePosition>();

            long scanVolumeBlockCount =
                (long)(maxPosition.X - minPosition.X + 1)
                * (maxPosition.Y - minPosition.Y + 1)
                * (maxPosition.Z - minPosition.Z + 1);

            TargetedProspectingScanTiming scanTiming =
                TargetedProspectingTestLogger.Enabled
                    ? new TargetedProspectingScanTiming(
                        scanVolumeBlockCount
                    )
                    : null;

            ScanDepositAreaIncrementally(
                world,
                minPosition,
                maxPosition,
                minPosition.X,
                mineralCode,
                matchingPositions,
                scanTiming,
                () =>
                {
                    CompleteDepositScan(
                        world,
                        player,
                        surveyId,
                        propickSlot,
                        propickStack,
                        surveyPosition,
                        mineralCode,
                        matchingPositions,
                        satietyCostApplied,
                        scanTiming
                    );
                }
            );
        }
        private static void CompleteDepositScan(
            IWorldAccessor world,
            IPlayer player,
            string surveyId,
            ItemSlot propickSlot,
            ItemStack propickStack,
            BlockPos surveyPosition,
            string mineralCode,
            HashSet<OrePosition> matchingPositions,
            bool satietyCostApplied,
            TargetedProspectingScanTiming scanTiming
        )
        {
            long completionStartedTimestamp =
                scanTiming?.StartWork() ?? 0L;
            List<OreDeposit> deposits =
                FindDeposits(
                    matchingPositions,
                    surveyPosition
                );

            deposits.Sort(
                (
                    OreDeposit left,
                    OreDeposit right
                ) =>
                {
                    int distanceComparison =
                        left.NearestDistanceSquared
                            .CompareTo(
                                right.NearestDistanceSquared
                            );

                    if (distanceComparison != 0)
                    {
                        return distanceComparison;
                    }

                    return right.BlockCount.CompareTo(
                        left.BlockCount
                    );
                }
            );

            int depositCountBeforeLimit =
                deposits.Count;

            int maximumDepositCount =
                GetMaximumDepositCount(
                    propickSlot.Itemstack
                );

            if (deposits.Count == 0)
            {
                bool durabilityStepCompleted =
                    TryApplySurveyDurabilityCost(
                        world,
                        player,
                        propickSlot,
                        propickStack,
                        0,
                        out int durabilityBefore,
                        out int durabilityAfter,
                        out int durabilityCost,
                        out string interruptionMessage
                    );

                if (!durabilityStepCompleted)
                {
                    SendSurveySatietyMessage(
                        world,
                        player,
                        satietyCostApplied
                    );

                    scanTiming?.RecordCompletion(
                        completionStartedTimestamp
                    );
                    TargetedProspectingTestLogger
                        .WriteDepositScanCompleted(
                            surveyId,
                            player,
                            propickStack,
                            propickSlot.Itemstack,
                            mineralCode,
                            matchingPositions.Count,
                            depositCountBeforeLimit,
                            maximumDepositCount,
                            0,
                            "interrupted",
                            false,
                            durabilityCost,
                            durabilityBefore,
                            durabilityAfter,
                            "targetedprospecting:survey-interrupted",
                            interruptionMessage,
                            0,
                            scanTiming
                        );

                    return;
                }

                string noMatchingDepositsMessage;

                if (player is IServerPlayer serverPlayer)
                {
                    noMatchingDepositsMessage = Lang.GetL(
                        serverPlayer.LanguageCode,
                        "targetedprospecting:no-matching-deposits"
                    );
                }
                else
                {
                    noMatchingDepositsMessage = Lang.Get(
                        "targetedprospecting:no-matching-deposits"
                    );
                }

                SendTargetedProspectingMessage(
                    world,
                    player,
                    noMatchingDepositsMessage
                );

                SendSurveySatietyMessage(
                    world,
                    player,
                    satietyCostApplied
                );

                scanTiming?.RecordCompletion(
                    completionStartedTimestamp
                );
                TargetedProspectingTestLogger
                    .WriteDepositScanCompleted(
                        surveyId,
                        player,
                        propickStack,
                        propickSlot.Itemstack,
                        mineralCode,
                        matchingPositions.Count,
                        depositCountBeforeLimit,
                        maximumDepositCount,
                        0,
                        "no-matches",
                        durabilityAfter < durabilityBefore,
                        durabilityCost,
                        durabilityBefore,
                        durabilityAfter,
                        "targetedprospecting:no-matching-deposits",
                        noMatchingDepositsMessage,
                        0,
                        scanTiming
                    );

                return;
            }

            if (
                maximumDepositCount > 0
                &&
                deposits.Count > maximumDepositCount
            )
            {
                deposits.RemoveRange(
                    maximumDepositCount,
                    deposits.Count - maximumDepositCount
                );
            }

            bool durabilityStepCompletedForResults =
                TryApplySurveyDurabilityCost(
                    world,
                    player,
                    propickSlot,
                    propickStack,
                    deposits.Count,
                    out int resultDurabilityBefore,
                    out int resultDurabilityAfter,
                    out int resultDurabilityCost,
                    out string resultInterruptionMessage
                );

            if (!durabilityStepCompletedForResults)
            {
                SendSurveySatietyMessage(
                    world,
                    player,
                    satietyCostApplied
                );

                scanTiming?.RecordCompletion(
                    completionStartedTimestamp
                );
                TargetedProspectingTestLogger
                    .WriteDepositScanCompleted(
                        surveyId,
                        player,
                        propickStack,
                        propickSlot.Itemstack,
                        mineralCode,
                        matchingPositions.Count,
                        depositCountBeforeLimit,
                        maximumDepositCount,
                        deposits.Count,
                        "interrupted",
                        false,
                        resultDurabilityCost,
                        resultDurabilityBefore,
                        resultDurabilityAfter,
                        "targetedprospecting:survey-interrupted",
                        resultInterruptionMessage,
                        0,
                        scanTiming
                    );

                return;
            }

            string resultLocalizationKey;
            string resultMessage =
                SendDepositResultMessages(
                    world,
                    player,
                    deposits,
                    surveyPosition.dimension,
                    mineralCode,
                    out resultLocalizationKey
                );

            int depositWaypointAddedCount = AddDepositWaypoints(
                world,
                player,
                deposits,
                surveyPosition.dimension,
                mineralCode
            );

            SendSurveySatietyMessage(
                world,
                player,
                satietyCostApplied
            );

            scanTiming?.RecordCompletion(
                completionStartedTimestamp
            );
            TargetedProspectingTestLogger
                .WriteDepositScanCompleted(
                    surveyId,
                    player,
                    propickStack,
                    propickSlot.Itemstack,
                    mineralCode,
                    matchingPositions.Count,
                    depositCountBeforeLimit,
                    maximumDepositCount,
                    deposits.Count,
                    "success",
                    resultDurabilityAfter
                        < resultDurabilityBefore,
                    resultDurabilityCost,
                    resultDurabilityBefore,
                    resultDurabilityAfter,
                    resultLocalizationKey,
                    resultMessage,
                    depositWaypointAddedCount,
                    scanTiming
                );
        }
        private static void RunGemBonusScan(
            IWorldAccessor world,
            IPlayer player,
            string surveyId,
            BlockPos surveyPosition
        )
        {
            int chunkSize =
                GlobalConstants.ChunkSize;

            int centerChunkX =
                FloorDivide(
                    surveyPosition.X,
                    chunkSize
                );

            int centerChunkZ =
                FloorDivide(
                    surveyPosition.Z,
                    chunkSize
                );

            int minX =
                (centerChunkX - 1) * chunkSize;

            int minZ =
                (centerChunkZ - 1) * chunkSize;

            int maxX =
                minX + chunkSize * 3 - 1;

            int maxZ =
                minZ + chunkSize * 3 - 1;

            BlockPos minPosition =
                surveyPosition.Copy();

            minPosition.X = minX;
            minPosition.Y = 0;
            minPosition.Z = minZ;

            BlockPos maxPosition =
                surveyPosition.Copy();

            maxPosition.X = maxX;
            maxPosition.Y =
                world.BlockAccessor.MapSizeY - 1;
            maxPosition.Z = maxZ;

            HashSet<OrePosition> diamondPositions =
                new HashSet<OrePosition>();

            HashSet<OrePosition> emeraldPositions =
                new HashSet<OrePosition>();

            HashSet<OrePosition> peridotPositions =
                new HashSet<OrePosition>();



            ScanGemAreaIncrementally(
                world,
                minPosition,
                maxPosition,
                minPosition.X,
                diamondPositions,
                emeraldPositions,
                peridotPositions,
                () =>
                {
                    CompleteGemBonusScan(
                        world,
                        player,
                        surveyId,
                        surveyPosition,
                        diamondPositions,
                        emeraldPositions,
                        peridotPositions
                    );
                }
            );
        }
        private static void CompleteGemBonusScan(
    IWorldAccessor world,
    IPlayer player,
    string surveyId,
    BlockPos surveyPosition,
    HashSet<OrePosition> diamondPositions,
    HashSet<OrePosition> emeraldPositions,
    HashSet<OrePosition> peridotPositions
)
        {
            List<OreDeposit> diamondDeposits =
                FindAndSortDeposits(
                    diamondPositions,
                    surveyPosition
                );

            List<OreDeposit> emeraldDeposits =
                FindAndSortDeposits(
                    emeraldPositions,
                    surveyPosition
                );

            List<OreDeposit> peridotDeposits =
                FindAndSortDeposits(
                    peridotPositions,
                    surveyPosition
                );

            List<OreDeposit> eligibleDiamondDeposits =
                new List<OreDeposit>();
            List<OreDeposit> deduplicatedDiamondDeposits =
                new List<OreDeposit>();
            List<OreDeposit> eligibleEmeraldDeposits =
                new List<OreDeposit>();
            List<OreDeposit> deduplicatedEmeraldDeposits =
                new List<OreDeposit>();
            List<OreDeposit> eligiblePeridotDeposits =
                new List<OreDeposit>();
            List<OreDeposit> deduplicatedPeridotDeposits =
                new List<OreDeposit>();

            string selectionMode = "none";
            string randomizedGemTypes = null;
            string selectedMineralCode = null;
            OreDeposit selectedDeposit = null;
            bool waypointAdded = false;

            if (player is IServerPlayer serverPlayer)
            {
                WaypointMapLayer waypointMapLayer =
                    GetWaypointMapLayer(
                        world
                    );

                if (waypointMapLayer != null)
                {
                    SplitGemDepositsByWaypoint(
                        waypointMapLayer,
                        serverPlayer,
                        diamondDeposits,
                        surveyPosition.dimension,
                        DiamondOreName,
                        eligibleDiamondDeposits,
                        deduplicatedDiamondDeposits
                    );

                    SplitGemDepositsByWaypoint(
                        waypointMapLayer,
                        serverPlayer,
                        emeraldDeposits,
                        surveyPosition.dimension,
                        EmeraldOreName,
                        eligibleEmeraldDeposits,
                        deduplicatedEmeraldDeposits
                    );

                    SplitGemDepositsByWaypoint(
                        waypointMapLayer,
                        serverPlayer,
                        peridotDeposits,
                        surveyPosition.dimension,
                        PeridotOreName,
                        eligiblePeridotDeposits,
                        deduplicatedPeridotDeposits
                    );

                    List<OreDeposit> selectedDeposits = null;

                    if (eligibleDiamondDeposits.Count > 0)
                    {
                        selectionMode = "diamond-priority";
                        selectedMineralCode = DiamondOreName;
                        selectedDeposits =
                            eligibleDiamondDeposits;
                    }
                    else if (
                        eligibleEmeraldDeposits.Count > 0
                        &&
                        eligiblePeridotDeposits.Count > 0
                    )
                    {
                        selectionMode =
                            "emerald-peridot-random";
                        randomizedGemTypes =
                            $"{EmeraldOreName},{PeridotOreName}";

                        if (world.Rand.Next(2) == 0)
                        {
                            selectedMineralCode =
                                EmeraldOreName;
                            selectedDeposits =
                                eligibleEmeraldDeposits;
                        }
                        else
                        {
                            selectedMineralCode =
                                PeridotOreName;
                            selectedDeposits =
                                eligiblePeridotDeposits;
                        }
                    }
                    else if (eligibleEmeraldDeposits.Count > 0)
                    {
                        selectionMode = "emerald-only";
                        selectedMineralCode = EmeraldOreName;
                        selectedDeposits =
                            eligibleEmeraldDeposits;
                    }
                    else if (eligiblePeridotDeposits.Count > 0)
                    {
                        selectionMode = "peridot-only";
                        selectedMineralCode = PeridotOreName;
                        selectedDeposits =
                            eligiblePeridotDeposits;
                    }
                    else if (
                        diamondDeposits.Count > 0
                        ||
                        emeraldDeposits.Count > 0
                        ||
                        peridotDeposits.Count > 0
                    )
                    {
                        selectionMode =
                            "all-candidates-deduplicated";
                    }
                    else
                    {
                        selectionMode = "no-gems-found";
                    }

                    if (selectedDeposits != null)
                    {
                        int selectedDepositIndex =
                            selectedDeposits.Count == 1
                                ? 0
                                : world.Rand.Next(
                                    selectedDeposits.Count
                                );

                        selectedDeposit =
                            selectedDeposits[
                                selectedDepositIndex
                            ];

                        waypointAdded =
                            AddNearestDepositWaypoint(
                                world,
                                player,
                                new List<OreDeposit>
                                {
                                    selectedDeposit
                                },
                                surveyPosition.dimension,
                                selectedMineralCode,
                                "star1",
                                GemWaypointCategory
                            );
                    }
                }
                else
                {
                    selectionMode =
                        "waypoint-layer-unavailable";
                }
            }
            else
            {
                selectionMode =
                    "server-player-unavailable";
            }

            if (TargetedProspectingTestLogger.Enabled)
            {
                int selectedDepositCandidateCount =
                    selectedMineralCode == DiamondOreName
                        ? eligibleDiamondDeposits.Count
                        : selectedMineralCode == EmeraldOreName
                            ? eligibleEmeraldDeposits.Count
                            : selectedMineralCode == PeridotOreName
                                ? eligiblePeridotDeposits.Count
                                : 0;

                TargetedProspectingTestLogger
                    .WriteGemScanCompleted(
                        surveyId,
                        player,
                        diamondPositions.Count,
                        diamondDeposits.Count,
                        DescribeGemDeposits(
                            diamondDeposits
                        ),
                        eligibleDiamondDeposits.Count,
                        DescribeGemDeposits(
                            eligibleDiamondDeposits
                        ),
                        deduplicatedDiamondDeposits.Count,
                        DescribeGemDeposits(
                            deduplicatedDiamondDeposits
                        ),
                        emeraldPositions.Count,
                        emeraldDeposits.Count,
                        DescribeGemDeposits(
                            emeraldDeposits
                        ),
                        eligibleEmeraldDeposits.Count,
                        DescribeGemDeposits(
                            eligibleEmeraldDeposits
                        ),
                        deduplicatedEmeraldDeposits.Count,
                        DescribeGemDeposits(
                            deduplicatedEmeraldDeposits
                        ),
                        peridotPositions.Count,
                        peridotDeposits.Count,
                        DescribeGemDeposits(
                            peridotDeposits
                        ),
                        eligiblePeridotDeposits.Count,
                        DescribeGemDeposits(
                            eligiblePeridotDeposits
                        ),
                        deduplicatedPeridotDeposits.Count,
                        DescribeGemDeposits(
                            deduplicatedPeridotDeposits
                        ),
                        selectionMode,
                        randomizedGemTypes,
                        selectedMineralCode,
                        selectedDeposit == null
                            ? null
                            : DescribeGemDeposit(
                                selectedDeposit
                            ),
                        selectedDepositCandidateCount,
                        selectedDepositCandidateCount > 1,
                        waypointAdded
                    );
            }
        }

        private static void SplitGemDepositsByWaypoint(
            WaypointMapLayer waypointMapLayer,
            IServerPlayer serverPlayer,
            List<OreDeposit> deposits,
            int dimension,
            string mineralCode,
            List<OreDeposit> eligibleDeposits,
            List<OreDeposit> deduplicatedDeposits
        )
        {
            foreach (OreDeposit deposit in deposits)
            {
                int centerBlockX =
                    (int)Math.Round(
                        deposit.CenterX
                    );

                int centerBlockZ =
                    (int)Math.Round(
                        deposit.CenterZ
                    );

                string waypointKey =
                    BuildWaypointKey(
                        GemWaypointCategory,
                        mineralCode,
                        dimension,
                        centerBlockX,
                        centerBlockZ
                    );

                if (
                    HasMatchingTargetedProspectingWaypoint(
                        waypointMapLayer,
                        serverPlayer,
                        waypointKey
                    )
                )
                {
                    deduplicatedDeposits.Add(
                        deposit
                    );
                }
                else
                {
                    eligibleDeposits.Add(
                        deposit
                    );
                }
            }
        }

        private static string DescribeGemDeposits(
            List<OreDeposit> deposits
        )
        {
            if (deposits.Count == 0)
            {
                return "none";
            }

            List<string> descriptions =
                new List<string>(
                    deposits.Count
                );

            foreach (OreDeposit deposit in deposits)
            {
                descriptions.Add(
                    DescribeGemDeposit(
                        deposit
                    )
                );
            }

            return string.Join(
                ";",
                descriptions
            );
        }

        private static string DescribeGemDeposit(
            OreDeposit deposit
        )
        {
            return string.Concat(
                "x=",
                (int)Math.Round(
                    deposit.CenterX
                ),
                ",z=",
                (int)Math.Round(
                    deposit.CenterZ
                ),
                ",blocks=",
                deposit.BlockCount
            );
        }
        private static List<OreDeposit>
            FindAndSortDeposits(
                HashSet<OrePosition> matchingPositions,
                BlockPos surveyPosition
            )
        {
            List<OreDeposit> deposits =
                FindDeposits(
                    matchingPositions,
                    surveyPosition
                );

            deposits.Sort(
                (
                    OreDeposit left,
                    OreDeposit right
                ) =>
                {
                    int distanceComparison =
                        left.NearestDistanceSquared
                            .CompareTo(
                                right
                                    .NearestDistanceSquared
                            );

                    if (distanceComparison != 0)
                    {
                        return distanceComparison;
                    }

                    return right.BlockCount.CompareTo(
                        left.BlockCount
                    );
                }
            );

            return deposits;
        }

        private static List<OreDeposit> FindDeposits(
            HashSet<OrePosition> matchingPositions,
            BlockPos surveyPosition
        )
        {
            HashSet<OrePosition> unvisited =
                new HashSet<OrePosition>(
                    matchingPositions
                );

            List<OreDeposit> deposits =
                new List<OreDeposit>();

            Queue<OrePosition> positionsToVisit =
                new Queue<OrePosition>();

            while (unvisited.Count > 0)
            {
                OrePosition firstPosition =
                    GetFirstPosition(
                        unvisited
                    );

                unvisited.Remove(
                    firstPosition
                );

                positionsToVisit.Enqueue(
                    firstPosition
                );

                List<OrePosition> depositBlocks =
                    new List<OrePosition>();

                while (positionsToVisit.Count > 0)
                {
                    OrePosition currentPosition =
                        positionsToVisit.Dequeue();

                    depositBlocks.Add(
                        currentPosition
                    );

                    for (
                        int offsetX = -1;
                        offsetX <= 1;
                        offsetX++
                    )
                    {
                        for (
                            int offsetY = -1;
                            offsetY <= 1;
                            offsetY++
                        )
                        {
                            for (
                                int offsetZ = -1;
                                offsetZ <= 1;
                                offsetZ++
                            )
                            {
                                if (
                                    offsetX == 0
                                    &&
                                    offsetY == 0
                                    &&
                                    offsetZ == 0
                                )
                                {
                                    continue;
                                }

                                OrePosition neighbour =
                                    new OrePosition(
                                        currentPosition.X
                                            + offsetX,
                                        currentPosition.Y
                                            + offsetY,
                                        currentPosition.Z
                                            + offsetZ
                                    );

                                if (
                                    unvisited.Remove(
                                        neighbour
                                    )
                                )
                                {
                                    positionsToVisit.Enqueue(
                                        neighbour
                                    );
                                }
                            }
                        }
                    }
                }

                deposits.Add(
                    CreateDeposit(
                        depositBlocks,
                        surveyPosition
                    )
                );
            }

            return deposits;
        }

        private static OreDeposit CreateDeposit(
            List<OrePosition> blocks,
            BlockPos surveyPosition
        )
        {
            HashSet<OreColumn> columns =
                new HashSet<OreColumn>();

            foreach (
                OrePosition block
                in blocks
            )
            {
                columns.Add(
                    new OreColumn(
                        block.X,
                        block.Z
                    )
                );
            }

            double sumX = 0;
            double sumZ = 0;
            double nearestDistanceSquared =
                double.MaxValue;

            foreach (
                OreColumn column
                in columns
            )
            {
                sumX += column.X;
                sumZ += column.Z;

                double differenceX =
                    column.X
                    - surveyPosition.X;

                double differenceZ =
                    column.Z
                    - surveyPosition.Z;

                double distanceSquared =
                    differenceX * differenceX
                    +
                    differenceZ * differenceZ;

                if (
                    distanceSquared
                    < nearestDistanceSquared
                )
                {
                    nearestDistanceSquared =
                        distanceSquared;
                }
            }

            double centerX =
                sumX / columns.Count;

            double centerZ =
                sumZ / columns.Count;

            return new OreDeposit(
                blocks.Count,
                centerX,
                centerZ,
                nearestDistanceSquared
            );
        }
        private static string SendDepositResultMessages(
            IWorldAccessor world,
            IPlayer player,
            List<OreDeposit> deposits,
            int dimension,
            string mineralCode,
            out string localizationKey
        )
        {
            localizationKey = null;

            if (deposits.Count == 0)
            {
                return null;
            }

            localizationKey =
                deposits.Count == 1
                    ? "targetedprospecting:result-single-deposit"
                    : "targetedprospecting:result-multiple-deposits";

            List<string> resultMessages = new();

            for (
                int depositIndex = 0;
                depositIndex < deposits.Count;
                depositIndex++
            )
            {
                OreDeposit deposit =
                    deposits[depositIndex];

                int centerBlockX =
                    (int)Math.Floor(
                        deposit.CenterX
                    );

                int centerBlockZ =
                    (int)Math.Floor(
                        deposit.CenterZ
                    );

                BlockPos centerPosition =
                    new BlockPos(
                        centerBlockX,
                        0,
                        centerBlockZ,
                        dimension
                    );

                Vec3i localCenterPosition =
                    centerPosition.ToLocalPosition(
                        world.Api
                    );

                double localCenterX =
                    localCenterPosition.X
                    +
                    (
                        deposit.CenterX
                        - centerBlockX
                    );

                double localCenterZ =
                    localCenterPosition.Z
                    +
                    (
                        deposit.CenterZ
                        - centerBlockZ
                    );

                string depositName =
                    player is IServerPlayer mineralServerPlayer
                        ? Lang.GetL(
                            mineralServerPlayer.LanguageCode,
                            $"ore-{mineralCode}"
                        )
                        : Lang.Get(
                            $"ore-{mineralCode}"
                        );

                string resultMessage;

                if (player is IServerPlayer resultServerPlayer)
                {
                    if (deposits.Count == 1)
                    {
                        resultMessage = Lang.GetL(
                            resultServerPlayer.LanguageCode,
                            localizationKey,
                            $"{localCenterX:0}",
                            $"{localCenterZ:0}",
                            depositName
                        );
                    }
                    else
                    {
                        resultMessage = Lang.GetL(
                            resultServerPlayer.LanguageCode,
                            localizationKey,
                            depositIndex + 1,
                            deposits.Count,
                            $"{localCenterX:0}",
                            $"{localCenterZ:0}",
                            depositName
                        );
                    }
                }
                else
                {
                    if (deposits.Count == 1)
                    {
                        resultMessage = Lang.Get(
                            localizationKey,
                            $"{localCenterX:0}",
                            $"{localCenterZ:0}",
                            depositName
                        );
                    }
                    else
                    {
                        resultMessage = Lang.Get(
                            localizationKey,
                            depositIndex + 1,
                            deposits.Count,
                            $"{localCenterX:0}",
                            $"{localCenterZ:0}",
                            depositName
                        );
                    }
                }

                resultMessages.Add(resultMessage);

                SendTargetedProspectingMessage(
                    world,
                    player,
                    resultMessage
                );
            }

            return string.Join(" | ", resultMessages);
        }

        private static WaypointMapLayer GetWaypointMapLayer(
            IWorldAccessor world
        )
        {
            WorldMapManager worldMapManager =
                world.Api.ModLoader
                    .GetModSystem<WorldMapManager>();

            if (worldMapManager == null)
            {
                return null;
            }

            foreach (
                MapLayer mapLayer
                in worldMapManager.MapLayers
            )
            {
                if (
                    mapLayer
                    is WaypointMapLayer waypointMapLayer
                )
                {
                    return waypointMapLayer;
                }
            }

            return null;
        }
        private static bool AddNearestDepositWaypoint(
            IWorldAccessor world,
            IPlayer player,
            List<OreDeposit> deposits,
            int dimension,
            string mineralCode,
            string waypointIcon = "circle",
            string waypointCategory = OreWaypointCategory
        )
        {
            if (
                deposits.Count == 0
                ||
                player is not IServerPlayer serverPlayer
            )
            {
                return false;
            }

            WaypointMapLayer waypointMapLayer =
                GetWaypointMapLayer(
                    world
                );

            if (waypointMapLayer == null)
            {
                return false;
            }

            OreDeposit deposit =
                deposits[0];

            int centerBlockX =
                (int)Math.Round(
                    deposit.CenterX
                );

            int centerBlockZ =
                (int)Math.Round(
                    deposit.CenterZ
                );
            string waypointKey =
                BuildWaypointKey(
                    waypointCategory,
                    mineralCode,
                    dimension,
                    centerBlockX,
                    centerBlockZ
                );
            if (
                HasMatchingTargetedProspectingWaypoint(
                    waypointMapLayer,
                    serverPlayer,
                    waypointKey
                )
            )
            {
                return false;
            }

            BlockPos surfacePosition =
                new BlockPos(
                    centerBlockX,
                    0,
                    centerBlockZ,
                    dimension
                );

            Vec3i localSurfacePosition =
                surfacePosition.ToLocalPosition(
                    world.Api
                );

            int surfaceY =
                world.BlockAccessor
                    .GetTerrainMapheightAt(
                        surfacePosition
                    );

            Waypoint waypoint =
                new Waypoint
                {
                    Position =
                        new Vec3d(
                            centerBlockX + 0.5,
                            surfaceY + 1,
                            centerBlockZ + 0.5
                        ),
                    Title =
                        Lang.GetL(
                            serverPlayer.LanguageCode,
                            "targetedprospecting:waypoint-title",
                            Lang.GetL(
                                serverPlayer.LanguageCode,
                                $"ore-{mineralCode}"
                            ),
                            localSurfacePosition.X,
                            localSurfacePosition.Z
                        ),
                    Text =
                        waypointKey,
                    Color =
                        ColorUtil.ColorFromRgba(
                            40,
                            180,
                            255,
                            255
                        ),
                    Icon =
                        waypointIcon,
                    Pinned =
                        false,
                    ShowInWorld =
                        false,
                    Temporary =
                        false,
                    OwningPlayerUid =
                        serverPlayer.PlayerUID,
                    Guid =
                        Guid.NewGuid().ToString()
                };

            waypointMapLayer.AddWaypoint(
                waypoint,
                serverPlayer
            );

            return true;
        }
        private static int AddDepositWaypoints(
            IWorldAccessor world,
            IPlayer player,
            List<OreDeposit> deposits,
            int dimension,
            string mineralCode,
            string waypointIcon = "circle"
        )
        {
            int addedWaypointCount = 0;

            for (
                int depositIndex = 0;
                depositIndex < deposits.Count;
                depositIndex++
            )
            {
                List<OreDeposit> singleDeposit =
                    new List<OreDeposit>
                    {
                        deposits[depositIndex]
                    };

                bool waypointAdded =
                    AddNearestDepositWaypoint(
                        world,
                        player,
                        singleDeposit,
                        dimension,
                        mineralCode,
                        waypointIcon
                    );

                if (waypointAdded)
                {
                    addedWaypointCount++;
                }
            }

            return addedWaypointCount;
        }

        private static OrePosition GetFirstPosition(
            HashSet<OrePosition> positions
        )
        {
            foreach (
                OrePosition position
                in positions
            )
            {
                return position;
            }

            throw new InvalidOperationException(
                "The ore position collection is empty."
            );
        }

        private string GetValidationError(
            IPlayer player,
            ItemStack propickStack,
            ItemStack sampleStack
        )
        {
            if (
                GetMaximumDepositCount(
                    propickStack
                ) == 0
            )
            {
                if (player is IServerPlayer serverPlayer)
                {
                    return Lang.GetL(
                        serverPlayer.LanguageCode,
                        "targetedprospecting:unsupported-pick-material"
                    );
                }

                return Lang.Get(
                    "targetedprospecting:unsupported-pick-material"
                );
            }

            if (
                player.WorldData.CurrentGameMode
                != EnumGameMode.Creative
            )
            {
                int maxDurability =
                    GetMaxDurability(
                        propickStack
                    );

                int remainingDurability =
                    GetRemainingDurability(
                        propickStack
                    );

                double durabilityPercent =
                    maxDurability > 0
                        ? remainingDurability
                            * 100.0
                            / maxDurability
                        : 0;

                if (durabilityPercent < 80)
                {
                    if (player is IServerPlayer serverPlayer)
                    {
                        string formattedDurability =
                            durabilityPercent.ToString(
                                "0.#",
                                System.Globalization.CultureInfo.GetCultureInfo(
                                    serverPlayer.LanguageCode
                                )
                            );

                        return Lang.GetL(
                            serverPlayer.LanguageCode,
                            "targetedprospecting:require-durability",
                            formattedDurability
                        );
                    }

                    return Lang.Get(
                        "targetedprospecting:require-durability",
                        durabilityPercent.ToString(
                            "0.#",
                            System.Globalization.CultureInfo.GetCultureInfo(
                                Lang.CurrentLocale
                            )
                        )
                    );
                }
            }

            if (!IsValidOreSample(sampleStack))
            {
                if (player is IServerPlayer serverPlayer)
                {
                    return Lang.GetL(
                        serverPlayer.LanguageCode,
                        "targetedprospecting:require-reference-sample"
                    );
                }

                return Lang.Get(
                    "targetedprospecting:require-reference-sample"
                );
            }

            return null;
        }

        internal static string GetSampleMineralCode(
            ItemStack sampleStack
        )
        {
            string codePath =
                sampleStack?.Item?.Code?.Path;

            if (codePath == null)
            {
                return null;
            }

            if (
                codePath.StartsWith(
                    "nugget-",
                    StringComparison.Ordinal
                )
            )
            {
                return codePath.Substring(
                    "nugget-".Length
                );
            }

            if (
                string.Equals(
                    codePath,
                    "stone-halite",
                    StringComparison.Ordinal
                )
            )
            {
                return "halite";
            }

            if (
                codePath.StartsWith(
                    "ore-",
                    StringComparison.Ordinal
                )
            )
            {
                string[] codeParts =
                    codePath.Split('-');

                if (
                    codeParts.Length == 2
                    &&
                    SupportedUngradedMineralCodes.Contains(
                        codeParts[1]
                    )
                )
                {
                    return codeParts[1];
                }

                if (codeParts.Length >= 3)
                {
                    return codeParts[2];
                }
            }

            if (
                codePath.StartsWith(
                    "crystalizedore-",
                    StringComparison.Ordinal
                )
            )
            {
                string[] codeParts =
                    codePath.Split('-');

                if (codeParts.Length >= 3)
                {
                    return codeParts[2];
                }
            }

            return null;
        }

        private static bool IsSteelProspectingPick(
            ItemStack stack
        )
        {
            return string.Equals(
                stack?.Collectible?.Code?.Path,
                "prospectingpick-steel",
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static string GetGemMineralCode(
            Block block
        )
        {
            string codePath =
                block?.Code?.Path;

            if (
                codePath == null
                ||
                !codePath.StartsWith(
                    "ore-",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return null;
            }

            if (
                codePath.Contains(
                    "-diamond-",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return DiamondOreName;
            }

            if (
                codePath.Contains(
                    "-emerald-",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return EmeraldOreName;
            }

            if (
                codePath.Contains(
                    "-olivine_peridot-",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return PeridotOreName;
            }

            return null;
        }

        internal static bool IsValidOreSample(
            ItemStack stack
        )
        {
            return GetSampleMineralCode(stack) != null;
        }

        private static int FloorDivide(
            int value,
            int divisor
        )
        {
            int result =
                value / divisor;

            int remainder =
                value % divisor;

            if (
                remainder != 0
                &&
                value < 0
            )
            {
                result--;
            }

            return result;
        }

        private static void SendTargetedProspectingMessage(
            IWorldAccessor world,
            IPlayer player,
            string message
        )
        {
            if (
                world.Side == EnumAppSide.Server
                &&
                player is IServerPlayer serverPlayer
            )
            {
                serverPlayer.SendMessage(
                    GlobalConstants.InfoLogChatGroup,
                    message,
                    EnumChatType.Notification
                );
            }
        }

        private sealed class TargetedBreakingState
        {
            public TargetedBreakingState(
                ItemStack propickStack,
                BlockPos blockPosition
            )
            {
                PropickStack = propickStack;
                BlockPosition =
                    blockPosition.Copy();
            }

            public ItemStack PropickStack { get; }

            public BlockPos BlockPosition { get; }

            public bool MatchesTool(
                ItemSlot itemSlot
            )
            {
                return ReferenceEquals(
                    PropickStack,
                    itemSlot?.Itemstack
                );
            }

            public bool MatchesPosition(
                BlockPos blockPosition
            )
            {
                return
                    blockPosition != null
                    &&
                    BlockPosition.X == blockPosition.X
                    &&
                    BlockPosition.Y == blockPosition.Y
                    &&
                    BlockPosition.Z == blockPosition.Z
                    &&
                    BlockPosition.dimension
                        == blockPosition.dimension;
            }
        }

        private sealed class OreDeposit
        {
            public OreDeposit(
                int blockCount,
                double centerX,
                double centerZ,
                double nearestDistanceSquared
            )
            {
                BlockCount = blockCount;
                CenterX = centerX;
                CenterZ = centerZ;
                NearestDistanceSquared =
                    nearestDistanceSquared;
            }

            public int BlockCount { get; }

            public double CenterX { get; }

            public double CenterZ { get; }

            public double NearestDistanceSquared
            {
                get;
            }

        }

        private readonly struct OrePosition
            : IEquatable<OrePosition>
        {
            public OrePosition(
                int x,
                int y,
                int z
            )
            {
                X = x;
                Y = y;
                Z = z;
            }

            public int X { get; }

            public int Y { get; }

            public int Z { get; }

            public bool Equals(
                OrePosition other
            )
            {
                return
                    X == other.X
                    &&
                    Y == other.Y
                    &&
                    Z == other.Z;
            }

            public override bool Equals(
                object obj
            )
            {
                return
                    obj is OrePosition other
                    &&
                    Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;

                    hash =
                        hash * 31 + X;

                    hash =
                        hash * 31 + Y;

                    hash =
                        hash * 31 + Z;

                    return hash;
                }
            }
        }

        private readonly struct OreColumn
            : IEquatable<OreColumn>
        {
            public OreColumn(
                int x,
                int z
            )
            {
                X = x;
                Z = z;
            }

            public int X { get; }

            public int Z { get; }

            public bool Equals(
                OreColumn other
            )
            {
                return
                    X == other.X
                    &&
                    Z == other.Z;
            }

            public override bool Equals(
                object obj
            )
            {
                return
                    obj is OreColumn other
                    &&
                    Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return
                        X * 397
                        ^ Z;
                }
            }
        }
    }
}
