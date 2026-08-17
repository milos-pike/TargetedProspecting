using System;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;

#nullable disable

namespace TargetedProspecting.Items
{
    internal static class TargetedProspectingDisassembly
    {
        private const string RecipeAttributeKey =
            "targetedProspectingDisassembly";
        private const int MetalBitsPerFullHead = 20;

        private static readonly ConditionalWeakTable<
            ItemStack,
            GeneratedOutputState
        > GeneratedOutputs = new();

        internal sealed class ConsumptionState
        {
            internal string PropickCode;
            internal string Material;
            internal int PropickDurabilityBefore;
            internal int PropickDurabilityMaximum;
            internal ToolState Saw;
            internal ToolState Hammer;
            internal ToolState Chisel;
            internal int CalculatedMetalBitCount;
            internal bool GeneratedOutputRecorded;
            internal int ActualMetalBitCount;
            internal string OutputMetalBitCode;
            internal string PreparationError;
        }

        internal sealed class ToolState
        {
            internal ItemSlot Slot;
            internal ItemStack OriginalStack;
            internal string Code;
            internal int DurabilityBefore;
            internal int DurabilityAfter;
        }

        private sealed class GeneratedOutputState
        {
            internal int Count;
            internal string Code;
        }

        internal static bool IsDisassemblyRecipe(IRecipeBase recipe)
        {
            return
                recipe is RecipeBase recipeBase
                && recipeBase.Attributes?[RecipeAttributeKey]
                    .AsBool(false) == true;
        }

        internal static bool TryGetSupportedMaterial(
            ItemStack propickStack,
            out string material
        )
        {
            material = propickStack?.Collectible?.Code?.Path switch
            {
                "prospectingpick-copper" => "copper",
                "prospectingpick-tinbronze" => "tinbronze",
                "prospectingpick-bismuthbronze" => "bismuthbronze",
                "prospectingpick-blackbronze" => "blackbronze",
                "prospectingpick-iron" => "iron",
                "prospectingpick-meteoriciron" => "meteoriciron",
                "prospectingpick-steel" => "steel",
                _ => null
            };

            return
                propickStack?.Collectible?.Code?.Domain == "game"
                && material != null;
        }

        internal static int CalculateMetalBitCount(ItemStack propickStack)
        {
            CollectibleObject collectible = propickStack?.Collectible;
            if (collectible == null)
            {
                return 1;
            }

            int maximumDurability =
                collectible.GetMaxDurability(propickStack);
            if (maximumDurability <= 0)
            {
                return 1;
            }

            int currentDurability = Math.Max(
                0,
                collectible.GetRemainingDurability(propickStack)
            );
            int count = (int)(
                MetalBitsPerFullHead
                * (long)currentDurability
                / maximumDurability
            );

            return Math.Clamp(count, 1, MetalBitsPerFullHead);
        }

        internal static ItemSlot FindIngredientSlot(
            ItemSlot[] allInputSlots,
            IRecipeBase recipe,
            string ingredientId
        )
        {
            IRecipeIngredient ingredient = null;
            foreach (IRecipeIngredient candidate in recipe.RecipeIngredients)
            {
                if (candidate?.Id == ingredientId)
                {
                    ingredient = candidate;
                    break;
                }
            }

            if (ingredient == null)
            {
                return null;
            }

            foreach (ItemSlot slot in allInputSlots)
            {
                ItemStack stack = slot?.Itemstack;
                if (
                    stack != null
                    && ingredient.SatisfiesAsIngredient(stack)
                    && stack.Collectible.MatchesForCrafting(
                        stack,
                        recipe,
                        ingredient
                    )
                )
                {
                    return slot;
                }
            }

            return null;
        }

        internal static ConsumptionState PrepareConsumption(
            ItemSlot[] allInputSlots,
            ItemSlot propickSlot,
            IRecipeBase recipe
        )
        {
            ItemStack propickStack = propickSlot?.Itemstack;
            ConsumptionState state = new()
            {
                PropickCode =
                    propickStack?.Collectible?.Code?.ToString(),
                CalculatedMetalBitCount =
                    CalculateMetalBitCount(propickStack),
                Saw = CaptureTool(
                    FindIngredientSlot(allInputSlots, recipe, "S")
                ),
                Hammer = CaptureTool(
                    FindIngredientSlot(allInputSlots, recipe, "H")
                ),
                Chisel = CaptureTool(
                    FindIngredientSlot(allInputSlots, recipe, "C")
                )
            };

            if (!TryGetSupportedMaterial(propickStack, out string material))
            {
                state.PreparationError = "unsupported-propick";
                return state;
            }

            state.Material = material;
            CollectibleObject collectible = propickStack.Collectible;
            state.PropickDurabilityMaximum =
                collectible.GetMaxDurability(propickStack);
            state.PropickDurabilityBefore =
                collectible.GetRemainingDurability(propickStack);

            if (state.PropickDurabilityMaximum <= 0)
            {
                state.PreparationError =
                    "invalid-propick-maximum-durability";
            }
            else if (
                state.Saw == null
                || state.Hammer == null
                || state.Chisel == null
            )
            {
                state.PreparationError = "crafting-tool-slot-not-found";
            }

            state.GeneratedOutputRecorded = TryTakeGeneratedOutput(
                propickStack,
                out state.ActualMetalBitCount,
                out state.OutputMetalBitCode
            );

            return state;
        }

        internal static void FinishConsumption(
            ConsumptionState state,
            IPlayer byPlayer
        )
        {
            if (state == null || byPlayer == null)
            {
                return;
            }

            bool creative =
                byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative;

            ApplyToolDurabilityCost(state.Saw, byPlayer, creative);
            ApplyToolDurabilityCost(state.Hammer, byPlayer, creative);
            ApplyToolDurabilityCost(state.Chisel, byPlayer, creative);

            if (byPlayer.Entity.World.Side != EnumAppSide.Server)
            {
                return;
            }

            string error = GetValidationError(state, creative);
            TargetedProspectingTestLogger.WriteDisassemblyResult(
                byPlayer,
                state.PropickCode,
                state.Material,
                state.PropickDurabilityBefore,
                state.PropickDurabilityMaximum,
                state.Saw?.Code,
                state.Saw?.DurabilityBefore,
                state.Saw?.DurabilityAfter,
                state.Hammer?.Code,
                state.Hammer?.DurabilityBefore,
                state.Hammer?.DurabilityAfter,
                state.Chisel?.Code,
                state.Chisel?.DurabilityBefore,
                state.Chisel?.DurabilityAfter,
                state.CalculatedMetalBitCount,
                state.GeneratedOutputRecorded
                    ? state.ActualMetalBitCount
                    : null,
                state.OutputMetalBitCode,
                error == null ? "success" : "error",
                error
            );
        }

        internal static void RecordGeneratedOutput(
            ItemStack propickStack,
            ItemStack outputStack
        )
        {
            if (propickStack == null || outputStack == null)
            {
                return;
            }

            GeneratedOutputState state =
                GeneratedOutputs.GetValue(
                    propickStack,
                    _ => new GeneratedOutputState()
                );
            state.Count = outputStack.StackSize;
            state.Code = outputStack.Collectible?.Code?.ToString();
        }

        private static bool TryTakeGeneratedOutput(
            ItemStack propickStack,
            out int count,
            out string code
        )
        {
            count = 0;
            code = null;

            if (
                propickStack == null
                || !GeneratedOutputs.TryGetValue(
                    propickStack,
                    out GeneratedOutputState state
                )
            )
            {
                return false;
            }

            count = state.Count;
            code = state.Code;
            GeneratedOutputs.Remove(propickStack);
            return true;
        }

        private static ToolState CaptureTool(ItemSlot slot)
        {
            ItemStack stack = slot?.Itemstack;
            if (stack?.Collectible == null)
            {
                return null;
            }

            int durability =
                stack.Collectible.GetRemainingDurability(stack);
            return new ToolState
            {
                Slot = slot,
                OriginalStack = stack,
                Code = stack.Collectible.Code?.ToString(),
                DurabilityBefore = durability,
                DurabilityAfter = durability
            };
        }

        private static void ApplyToolDurabilityCost(
            ToolState state,
            IPlayer byPlayer,
            bool creative
        )
        {
            if (state == null || creative)
            {
                return;
            }

            if (!ReferenceEquals(state.Slot.Itemstack, state.OriginalStack))
            {
                state.DurabilityAfter = -1;
                return;
            }

            state.OriginalStack.Collectible.DamageItem(
                byPlayer.Entity.World,
                byPlayer.Entity,
                state.Slot,
                1
            );
            state.DurabilityAfter = ReferenceEquals(
                state.Slot.Itemstack,
                state.OriginalStack
            )
                ? state.OriginalStack.Collectible
                    .GetRemainingDurability(state.OriginalStack)
                : 0;
        }

        private static string GetValidationError(
            ConsumptionState state,
            bool creative
        )
        {
            if (state.PreparationError != null)
            {
                return state.PreparationError;
            }

            if (!state.GeneratedOutputRecorded)
            {
                return "generated-output-not-recorded";
            }

            if (state.ActualMetalBitCount != state.CalculatedMetalBitCount)
            {
                return "output-count-mismatch";
            }

            if (
                state.OutputMetalBitCode
                != string.Concat("game:metalbit-", state.Material)
            )
            {
                return "output-code-mismatch";
            }

            if (
                !HasExpectedToolDurabilityChange(state.Saw, creative)
                || !HasExpectedToolDurabilityChange(state.Hammer, creative)
                || !HasExpectedToolDurabilityChange(state.Chisel, creative)
            )
            {
                return "crafting-tool-durability-mismatch";
            }

            return null;
        }

        private static bool HasExpectedToolDurabilityChange(
            ToolState state,
            bool creative
        )
        {
            if (state == null)
            {
                return false;
            }

            int expectedAfter = creative
                ? state.DurabilityBefore
                : Math.Max(0, state.DurabilityBefore - 1);
            return state.DurabilityAfter == expectedAfter;
        }
    }

    public sealed class
        CollectibleBehaviorTargetedProspectingDisassembly
        : CollectibleBehavior
    {
        public CollectibleBehaviorTargetedProspectingDisassembly(
            CollectibleObject collObj
        )
            : base(collObj)
        {
        }

        public override void OnCreatedByCrafting(
            ItemSlot[] allInputSlots,
            ItemSlot outputSlot,
            IRecipeBase byRecipe,
            ref EnumHandling bhHandling
        )
        {
            if (!TargetedProspectingDisassembly.IsDisassemblyRecipe(byRecipe))
            {
                return;
            }

            ItemSlot propickSlot =
                TargetedProspectingDisassembly.FindIngredientSlot(
                    allInputSlots,
                    byRecipe,
                    "P"
                );
            ItemStack propickStack = propickSlot?.Itemstack;
            if (
                !TargetedProspectingDisassembly.TryGetSupportedMaterial(
                    propickStack,
                    out string material
                )
            )
            {
                return;
            }

            ItemStack outputStack = outputSlot?.Itemstack;
            if (
                outputStack?.Collectible?.Code?.ToString()
                != string.Concat("game:metalbit-", material)
            )
            {
                return;
            }

            outputStack.StackSize =
                TargetedProspectingDisassembly.CalculateMetalBitCount(
                    propickStack
                );
            TargetedProspectingDisassembly.RecordGeneratedOutput(
                propickStack,
                outputStack
            );
        }
    }
}
