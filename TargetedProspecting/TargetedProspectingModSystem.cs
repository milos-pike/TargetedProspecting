using TargetedProspecting.Items;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace TargetedProspecting
{
    public class TargetedProspectingModSystem : ModSystem
    {
        private bool serverSideStarted;

        public override void Start(ICoreAPI api)
        {
            api.RegisterItemClass(
                "targetedprospecting.prospectingpick",
                typeof(ItemTargetedProspectingPick)
            );

            api.RegisterCollectibleBehaviorClass(
                "targetedprospecting.disassemblyoutput",
                typeof(
                    CollectibleBehaviorTargetedProspectingDisassembly
                )
            );

        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            TargetedProspectingTestLogger.Initialize(api);
            serverSideStarted = true;

            api.ChatCommands.Create("targetedprospecting")
                .WithDescription(
                    Lang.Get(
                        "targetedprospecting:command-check-description"
                    )
                )
                .RequiresPlayer()
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(CheckTargetedProspectingSetup)
                .BeginSubCommand("cooldown")
                    .WithDescription(
                        Lang.Get(
                            "targetedprospecting:command-cooldown-description"
                        )
                    )
                    .BeginSubCommand("reset")
                        .WithDescription(
                            Lang.Get(
                                "targetedprospecting:command-cooldown-reset-description"
                            )
                        )
                        .RequiresPlayer()
                        .RequiresPrivilege(Privilege.chat)
                        .HandleWith(
                            ResetTargetedProspectingCooldown
                        )
                    .EndSubCommand()
                .EndSubCommand()
                .BeginSubCommand("logger")
                    .WithDescription(
                        Lang.Get(
                            "targetedprospecting:command-logger-description"
                        )
                    )
                    .RequiresPrivilege(Privilege.controlserver)
                    .BeginSubCommand("start")
                        .WithDescription(
                            Lang.Get(
                                "targetedprospecting:command-logger-start-description"
                            )
                        )
                        .RequiresPrivilege(Privilege.controlserver)
                        .HandleWith(
                            StartTargetedProspectingLogger
                        )
                    .EndSubCommand()
                    .BeginSubCommand("stop")
                        .WithDescription(
                            Lang.Get(
                                "targetedprospecting:command-logger-stop-description"
                            )
                        )
                        .RequiresPrivilege(Privilege.controlserver)
                        .HandleWith(
                            StopTargetedProspectingLogger
                        )
                    .EndSubCommand()
                .EndSubCommand();
        }

        public override void Dispose()
        {
            if (!serverSideStarted)
            {
                return;
            }

            serverSideStarted = false;

            TargetedProspectingTestLogger.Shutdown(
                "game-shutdown"
            );
        }

        private TextCommandResult StartTargetedProspectingLogger(
            TextCommandCallingArgs args
        )
        {
            if (TargetedProspectingTestLogger.Enabled)
            {
                return TextCommandResult.Success(
                    Lang.GetL(
                        args.LanguageCode,
                        "targetedprospecting:logger-already-active"
                    )
                );
            }

            if (!TargetedProspectingTestLogger.StartSession())
            {
                return TextCommandResult.Error(
                    Lang.GetL(
                        args.LanguageCode,
                        "targetedprospecting:logger-start-failed"
                    )
                );
            }

            return TextCommandResult.Success(
                Lang.GetL(
                    args.LanguageCode,
                    "targetedprospecting:logger-started"
                )
            );
        }

        private TextCommandResult StopTargetedProspectingLogger(
            TextCommandCallingArgs args
        )
        {
            if (!TargetedProspectingTestLogger.Enabled)
            {
                return TextCommandResult.Success(
                    Lang.GetL(
                        args.LanguageCode,
                        "targetedprospecting:logger-not-active"
                    )
                );
            }

            TargetedProspectingTestLogger.Shutdown(
                "disabled"
            );

            return TextCommandResult.Success(
                Lang.GetL(
                    args.LanguageCode,
                    "targetedprospecting:logger-stopped"
                )
            );
        }

        private TextCommandResult ResetTargetedProspectingCooldown(
            TextCommandCallingArgs args
        )
        {
            IPlayer player = args.Caller.Player;

            if (
                player.WorldData.CurrentGameMode
                != EnumGameMode.Creative
            )
            {
                string localizationKey =
                    "targetedprospecting:cooldown-reset-creative-only";

                string message =
                    Lang.GetL(
                        args.LanguageCode,
                        localizationKey
                    );

                TargetedProspectingTestLogger.WriteCommandResult(
                    "targetedprospecting cooldown reset",
                    "error",
                    "creative-mode-required",
                    localizationKey,
                    message,
                    player,
                    args.LanguageCode
                );

                return TextCommandResult.Error(
                    message
                );
            }

            TargetedProspectingCooldown.Reset(
                player
            );

            string successLocalizationKey =
                "targetedprospecting:cooldown-reset-success";

            string successMessage =
                Lang.GetL(
                    args.LanguageCode,
                    successLocalizationKey
                );

            TargetedProspectingTestLogger.WriteCommandResult(
                "targetedprospecting cooldown reset",
                "success",
                "cooldown-reset",
                successLocalizationKey,
                successMessage,
                player,
                args.LanguageCode
            );

            return TextCommandResult.Success(
                successMessage
            );
        }

        private TextCommandResult CheckTargetedProspectingSetup(
            TextCommandCallingArgs args
        )
        {
            IPlayer player = args.Caller.Player;

            ItemStack? mainHandStack =
                player.InventoryManager.ActiveHotbarSlot?.Itemstack;

            ItemStack? offHandStack =
                player.InventoryManager.OffhandHotbarSlot?.Itemstack;

            if (mainHandStack == null || !IsProspectingPick(mainHandStack))
            {
                string localizationKey =
                    "targetedprospecting:require-prospecting-pick";

                string message =
                    Lang.GetL(
                        args.LanguageCode,
                        localizationKey
                    );

                TargetedProspectingTestLogger.WriteCommandResult(
                    "targetedprospecting",
                    "error",
                    "invalid-prospecting-pick",
                    localizationKey,
                    message,
                    player,
                    args.LanguageCode
                );

                return TextCommandResult.Error(
                    message
                );
            }

            int maxDurability =
                mainHandStack.Collectible.GetMaxDurability(
                    mainHandStack
                );

            int remainingDurability =
                mainHandStack.Collectible.GetRemainingDurability(
                    mainHandStack
                );

            double durabilityPercent =
                maxDurability > 0
                    ? remainingDurability * 100.0 / maxDurability
                    : 0;

            if (durabilityPercent < 80)
            {
                string localizationKey =
                    "targetedprospecting:require-durability";

                string message =
                    Lang.GetL(
                        args.LanguageCode,
                        localizationKey,
                        durabilityPercent.ToString(
                            "0.#",
                            System.Globalization.CultureInfo.GetCultureInfo(
                                args.LanguageCode
                            )
                        )
                    );

                TargetedProspectingTestLogger.WriteCommandResult(
                    "targetedprospecting",
                    "error",
                    "low-durability",
                    localizationKey,
                    message,
                    player,
                    args.LanguageCode
                );

                return TextCommandResult.Error(
                    message
                );
            }

            if (
                offHandStack == null
                ||
                !ItemTargetedProspectingPick.IsValidOreSample(
                    offHandStack
                )
            )
            {
                string localizationKey =
                    "targetedprospecting:require-reference-sample";

                string message =
                    Lang.GetL(
                        args.LanguageCode,
                        localizationKey
                    );

                TargetedProspectingTestLogger.WriteCommandResult(
                    "targetedprospecting",
                    "error",
                    "invalid-reference-sample",
                    localizationKey,
                    message,
                    player,
                    args.LanguageCode
                );

                return TextCommandResult.Error(
                    message
                );
            }

            string samplePath =
                offHandStack.Collectible.Code.Path;

            string? mineralCode =
                ItemTargetedProspectingPick
                    .GetSampleMineralCode(
                        offHandStack
                    );

            string sampleName =
                mineralCode == null
                    ? samplePath
                    : Lang.GetL(
                        args.LanguageCode,
                        $"ore-{mineralCode}"
                    );

            string successLocalizationKey =
                "targetedprospecting:requirements-ready";

            string successMessage =
                Lang.GetL(
                    args.LanguageCode,
                    successLocalizationKey,
                    sampleName,
                    durabilityPercent.ToString(
                        "0.#",
                        System.Globalization.CultureInfo.GetCultureInfo(
                            args.LanguageCode
                        )
                    )
                );

            TargetedProspectingTestLogger.WriteCommandResult(
                "targetedprospecting",
                "success",
                "ready",
                successLocalizationKey,
                successMessage,
                player,
                args.LanguageCode,
                mineralCode
            );

            return TextCommandResult.Success(
                successMessage
            );
        }

        private static bool IsProspectingPick(ItemStack? stack)
        {
            return stack?.Collectible?.Code?.Path?
                .StartsWith("prospectingpick-") == true;
        }

    }
}