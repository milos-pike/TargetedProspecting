using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TargetedProspecting
{
    internal sealed class TargetedProspectingScanTiming
    {
        private readonly long startedTimestamp;
        private long sliceWorkTimestampCount;
        private long maximumSliceTimestampCount;
        private long completionWorkTimestampCount;

        internal TargetedProspectingScanTiming(
            long scanVolumeBlockCount
        )
        {
            ScanVolumeBlockCount =
                scanVolumeBlockCount;

            startedTimestamp =
                Stopwatch.GetTimestamp();
        }

        internal long ScanVolumeBlockCount { get; }

        internal int SliceCount { get; private set; }

        internal double ElapsedMs =>
            ToMilliseconds(
                Stopwatch.GetTimestamp()
                    - startedTimestamp
            );

        internal double SliceWorkMs =>
            ToMilliseconds(
                sliceWorkTimestampCount
            );

        internal double MaxSliceMs =>
            ToMilliseconds(
                maximumSliceTimestampCount
            );

        internal double CompletionWorkMs =>
            ToMilliseconds(
                completionWorkTimestampCount
            );

        internal double WorkMs =>
            SliceWorkMs + CompletionWorkMs;

        internal double MaxBlockingMs =>
            Math.Max(
                MaxSliceMs,
                CompletionWorkMs
            );

        internal long StartWork()
        {
            return Stopwatch.GetTimestamp();
        }

        internal void RecordSlice(
            long sliceStartedTimestamp
        )
        {
            long elapsedTimestampCount =
                Math.Max(
                    0L,
                    Stopwatch.GetTimestamp()
                        - sliceStartedTimestamp
                );

            sliceWorkTimestampCount +=
                elapsedTimestampCount;

            SliceCount++;

            maximumSliceTimestampCount =
                Math.Max(
                    maximumSliceTimestampCount,
                    elapsedTimestampCount
                );
        }

        internal void RecordCompletion(
            long completionStartedTimestamp
        )
        {
            completionWorkTimestampCount =
                Math.Max(
                    0L,
                    Stopwatch.GetTimestamp()
                        - completionStartedTimestamp
                );
        }

        private static double ToMilliseconds(
            long timestampCount
        )
        {
            return timestampCount
                * 1000d
                / Stopwatch.Frequency;
        }
    }

    internal static class TargetedProspectingTestLogger
    {
        private const string Prefix =
            "[TargetedProspecting/Test]";

        private static readonly UTF8Encoding Utf8WithoutBom =
            new(
                encoderShouldEmitUTF8Identifier: false
            );

        private static readonly object SessionLogSync =
            new();

        private static ICoreServerAPI? serverApi;
        private static ILogger? logger;
        private static string? sessionLogPath;
        private static long eventCount;
        private static long errorCount;

        internal static bool Enabled
        {
            get;
            private set;
        }

        internal static void Initialize(
            ICoreServerAPI api
        )
        {
            lock (SessionLogSync)
            {
                serverApi = api;
                logger = api.Logger;
                Enabled = false;
                sessionLogPath = null;
                eventCount = 0;
                errorCount = 0;
            }
        }

        internal static bool StartSession()
        {
            Exception? startException = null;
            bool started = false;

            lock (SessionLogSync)
            {
                if (Enabled || sessionLogPath != null)
                {
                    return false;
                }

                ICoreServerAPI? api = serverApi;

                if (api == null)
                {
                    return false;
                }

                eventCount = 0;
                errorCount = 0;

                try
                {
                    CreateSessionLog(
                        DateTimeOffset.Now
                    );

                    Enabled = true;

                    WriteInformation(
                        () => BuildEnvironmentMessage(api)
                    );

                    started =
                        Enabled
                        && sessionLogPath != null;
                }
                catch (Exception exception)
                {
                    sessionLogPath = null;
                    Enabled = false;
                    startException = exception;
                }
            }

            if (startException != null)
            {
                WriteSessionLogException(
                    "session-log-create",
                    startException
                );
            }

            return started;
        }

        private static void CreateSessionLog(
            DateTimeOffset sessionStartedAt
        )
        {
            string logDirectoryPath =
                Path.Combine(
                    GamePaths.Logs,
                    "TargetedProspecting"
                );

            GamePaths.EnsurePathExists(
                logDirectoryPath
            );

            DateTimeOffset logFileTimestamp =
                sessionStartedAt;

            while (true)
            {
                string logFileName =
                    string.Concat(
                        "targetedprospecting-",
                        logFileTimestamp.ToString(
                            "yyyyMMdd-HHmmss",
                            CultureInfo.InvariantCulture
                        ),
                        ".log"
                    );

                string candidateLogPath =
                    Path.Combine(
                        logDirectoryPath,
                        logFileName
                    );

                try
                {
                    using (
                        FileStream stream =
                            new(
                                candidateLogPath,
                                FileMode.CreateNew,
                                FileAccess.Write,
                                FileShare.Read
                            )
                    )
                    using (
                        StreamWriter writer =
                            new(
                                stream,
                                Utf8WithoutBom
                            )
                    )
                    {
                        writer.WriteLine(
                            string.Concat(
                                FormatSessionTimestamp(
                                    sessionStartedAt
                                ),
                                " event=logger-start",
                                " logFormatVersion=1"
                            )
                        );
                    }

                    sessionLogPath =
                        candidateLogPath;
                    eventCount = 1;
                    errorCount = 0;

                    return;
                }
                catch (IOException)
                    when (File.Exists(candidateLogPath))
                {
                    logFileTimestamp =
                        logFileTimestamp.AddSeconds(1);
                }
            }
        }

        private static string BuildEnvironmentMessage(
            ICoreServerAPI api
        )
        {
            string? modVersion =
                api.ModLoader
                    .GetMod("targetedprospecting")
                    ?.Info?.Version;

            using Process currentProcess =
                Process.GetCurrentProcess();

            double processWorkingSetMB =
                currentProcess.WorkingSet64
                / (1024d * 1024d);

            string runtimeGameVersion =
                typeof(GameVersion)
                    .GetField(nameof(GameVersion.ShortGameVersion))
                    ?.GetRawConstantValue()
                    ?.ToString()
                ?? GameVersion.ShortGameVersion;

            string runtimeApiVersion =
                typeof(GameVersion)
                    .GetField(nameof(GameVersion.APIVersion))
                    ?.GetRawConstantValue()
                    ?.ToString()
                ?? GameVersion.APIVersion;

            return string.Concat(
                "event=environment",
                " gameVersion=",
                Quote(runtimeGameVersion),
                " apiVersion=",
                Quote(runtimeApiVersion),
                " modVersion=",
                Quote(modVersion),
                " dedicatedServer=",
                api.Server.IsDedicated
                    ? "true"
                    : "false",
                " osDescription=",
                Quote(RuntimeInformation.OSDescription),
                " osArchitecture=",
                Quote(
                    RuntimeInformation.OSArchitecture
                        .ToString()
                ),
                " processArchitecture=",
                Quote(
                    RuntimeInformation.ProcessArchitecture
                        .ToString()
                ),
                " frameworkDescription=",
                Quote(
                    RuntimeInformation.FrameworkDescription
                ),
                " processorCount=",
                FormatInvariant(
                    Environment.ProcessorCount
                ),
                " processWorkingSetMB=",
                FormatInvariant(
                    processWorkingSetMB,
                    "0.0"
                )
            );
        }

        internal static void WriteInformation(
            Func<string> messageFactory
        )
        {
            WriteSessionEvent(
                messageFactory,
                isError: false
            );
        }

        internal static void WriteError(
            Func<string> contextFactory
        )
        {
            string message =
                BuildErrorMessage(
                    "error",
                    contextFactory()
                );

            WriteStandardError(message);

            WriteSessionEvent(
                () => message,
                isError: true
            );
        }

        internal static void WriteException(
            Exception exception,
            Func<string> contextFactory
        )
        {
            string message =
                string.Concat(
                    BuildErrorMessage(
                        "exception",
                        contextFactory()
                    ),
                    " exceptionType=",
                    Quote(
                        exception.GetType().FullName
                    ),
                    " exceptionMessage=",
                    Quote(
                        exception.Message
                    ),
                    " stackTrace=",
                    Quote(
                        exception.StackTrace
                    )
                );

            WriteStandardError(message);

            WriteSessionEvent(
                () => message,
                isError: true
            );
        }

        internal static void Shutdown(
            string reason
        )
        {
            Exception? writeException = null;

            lock (SessionLogSync)
            {
                if (sessionLogPath == null)
                {
                    Enabled = false;
                    return;
                }

                long finalEventCount =
                    eventCount + 1;

                try
                {
                    File.AppendAllText(
                        sessionLogPath,
                        string.Concat(
                            FormatSessionTimestamp(
                                DateTimeOffset.Now
                            ),
                            " event=logger-stop reason=",
                            reason,
                            " eventCount=",
                            FormatInvariant(
                                finalEventCount
                            ),
                            " errorCount=",
                            FormatInvariant(
                                errorCount
                            ),
                            Environment.NewLine
                        ),
                        Utf8WithoutBom
                    );

                    eventCount =
                        finalEventCount;
                }
                catch (Exception exception)
                {
                    writeException =
                        exception;
                }
                finally
                {
                    sessionLogPath = null;
                    Enabled = false;
                }
            }

            if (writeException != null)
            {
                WriteSessionLogException(
                    "session-log-stop",
                    writeException
                );
            }
        }

        private static void WriteSessionEvent(
            Func<string> messageFactory,
            bool isError
        )
        {
            if (!Enabled)
            {
                return;
            }

            Exception? writeException = null;

            lock (SessionLogSync)
            {
                if (sessionLogPath == null)
                {
                    return;
                }

                try
                {
                    File.AppendAllText(
                        sessionLogPath,
                        string.Concat(
                            FormatSessionTimestamp(
                                DateTimeOffset.Now
                            ),
                            " ",
                            messageFactory(),
                            Environment.NewLine
                        ),
                        Utf8WithoutBom
                    );

                    eventCount++;

                    if (isError)
                    {
                        errorCount++;
                    }
                }
                catch (Exception exception)
                {
                    sessionLogPath = null;
                    Enabled = false;
                    writeException =
                        exception;
                }
            }

            if (writeException != null)
            {
                WriteSessionLogException(
                    "session-log-write",
                    writeException
                );
            }
        }

        private static string BuildErrorMessage(
            string eventName,
            string context
        )
        {
            return string.Concat(
                "level=error event=",
                eventName,
                string.IsNullOrWhiteSpace(context)
                    ? string.Empty
                    : string.Concat(
                        " ",
                        context
                    )
            );
        }

        private static string FormatSessionTimestamp(
            DateTimeOffset timestamp
        )
        {
            return timestamp.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fffzzz",
                CultureInfo.InvariantCulture
            );
        }

        private static void WriteStandardError(
            string message
        )
        {
            logger?.Error(
                string.Concat(
                    Prefix,
                    " ",
                    message
                )
            );
        }

        private static void WriteSessionLogException(
            string operation,
            Exception exception
        )
        {
            WriteException(
                exception,
                () =>
                    string.Concat(
                        "operation=",
                        Quote(operation)
                    )
            );
        }

        internal static string? CreateSurveyId()
        {
            if (!Enabled)
            {
                return null;
            }

            return Guid.NewGuid().ToString("N");
        }

        internal static void WriteSurveyStarted(
            string? surveyId,
            IWorldAccessor world,
            IPlayer player,
            ItemStack? propickStack,
            ItemStack? sampleStack,
            Block targetBlockBefore,
            BlockPos surveyPosition,
            string mineralCode,
            bool cooldownStarted,
            bool satietyCostApplied,
            float satietyMaximum,
            float satietyBefore,
            float satietyCostCalculated,
            float satietyAfter,
            bool includeGemBonus
        )
        {
            if (!Enabled || surveyId == null)
            {
                return;
            }

            Block targetBlockAfter =
                world.BlockAccessor.GetBlock(
                    surveyPosition
                );

            string? languageCode =
                player is IServerPlayer serverPlayer
                    ? serverPlayer.LanguageCode
                    : null;

            double nextAllowedTotalDays =
                cooldownStarted
                    ? player.Entity.WatchedAttributes
                        .GetDouble(
                            TargetedProspectingCooldown
                                .AttributeKey,
                            0d
                        )
                    : 0d;

            WriteInformation(
                () =>
                    string.Concat(
                        "event=survey-started",
                        " surveyId=",
                        Quote(surveyId),
                        " side=server",
                        " playerName=",
                        Quote(player.PlayerName),
                        " playerUid=",
                        Quote(player.PlayerUID),
                        " gameMode=",
                        Quote(
                            player.WorldData.CurrentGameMode
                                .ToString()
                        ),
                        " language=",
                        Quote(languageCode),
                        BuildItemFields(
                            "mainHand",
                            propickStack
                        ),
                        BuildItemFields(
                            "offHand",
                            sampleStack
                        ),
                        " mineralCode=",
                        Quote(mineralCode),
                        " targetBlockBefore=",
                        Quote(
                            targetBlockBefore.Code?.ToString()
                        ),
                        " targetBlockAfter=",
                        Quote(
                            targetBlockAfter.Code?.ToString()
                        ),
                        " targetBlockX=",
                        FormatInvariant(
                            surveyPosition.X
                        ),
                        " targetBlockY=",
                        FormatInvariant(
                            surveyPosition.Y
                        ),
                        " targetBlockZ=",
                        FormatInvariant(
                            surveyPosition.Z
                        ),
                        " targetDimension=",
                        FormatInvariant(
                            surveyPosition.dimension
                        ),
                        " blockBreakCompleted=true",
                        " cooldownStarted=",
                        cooldownStarted
                            ? "true"
                            : "false",
                        " worldTotalDays=",
                        FormatInvariant(
                            world.Calendar.TotalDays,
                            "0.######"
                        ),
                        " cooldownNextAllowedTotalDays=",
                        cooldownStarted
                            ? FormatInvariant(
                                nextAllowedTotalDays,
                                "0.######"
                            )
                            : "null",
                        " satietyCostApplied=",
                        satietyCostApplied
                            ? "true"
                            : "false",
                        " satietyMaximum=",
                        FormatInvariant(
                            satietyMaximum,
                            "0.###"
                        ),
                        " satietyBefore=",
                        FormatInvariant(
                            satietyBefore,
                            "0.###"
                        ),
                        " satietyCostCalculated=",
                        FormatInvariant(
                            satietyCostCalculated,
                            "0.###"
                        ),
                        " satietyCostActual=",
                        FormatInvariant(
                            Math.Max(
                                0f,
                                satietyBefore
                                    - satietyAfter
                            ),
                            "0.###"
                        ),
                        " satietyAfter=",
                        FormatInvariant(
                            satietyAfter,
                            "0.###"
                        ),
                        " depositScanScheduled=true",
                        " gemScanScheduled=",
                        includeGemBonus
                            ? "true"
                            : "false"
                    )
            );
        }

        internal static void WriteSurveyRejected(
            string stage,
            string reason,
            IWorldAccessor world,
            IPlayer player,
            ItemStack? propickStack,
            ItemStack? sampleStack,
            BlockPos targetPosition,
            string? localizationKey,
            string? message,
            double? cooldownRemainingDays = null,
            double? cooldownRemainingHours = null
        )
        {
            if (
                !Enabled
                || world.Side != EnumAppSide.Server
            )
            {
                return;
            }

            string? languageCode =
                player is IServerPlayer serverPlayer
                    ? serverPlayer.LanguageCode
                    : null;

            Block targetBlock =
                world.BlockAccessor.GetBlock(
                    targetPosition
                );

            double? cooldownNextAllowedTotalDays =
                cooldownRemainingDays.HasValue
                    ? world.Calendar.TotalDays
                        + cooldownRemainingDays.Value
                    : null;

            WriteInformation(
                () =>
                    string.Concat(
                        "event=survey-rejected",
                        " stage=",
                        Quote(stage),
                        " reason=",
                        Quote(reason),
                        " side=server",
                        " playerName=",
                        Quote(player.PlayerName),
                        " playerUid=",
                        Quote(player.PlayerUID),
                        " gameMode=",
                        Quote(
                            player.WorldData.CurrentGameMode
                                .ToString()
                        ),
                        " language=",
                        Quote(languageCode),
                        BuildItemFields(
                            "mainHand",
                            propickStack
                        ),
                        BuildItemFields(
                            "offHand",
                            sampleStack
                        ),
                        " targetBlock=",
                        Quote(
                            targetBlock.Code?.ToString()
                        ),
                        " targetBlockX=",
                        FormatInvariant(
                            targetPosition.X
                        ),
                        " targetBlockY=",
                        FormatInvariant(
                            targetPosition.Y
                        ),
                        " targetBlockZ=",
                        FormatInvariant(
                            targetPosition.Z
                        ),
                        " targetDimension=",
                        FormatInvariant(
                            targetPosition.dimension
                        ),
                        " worldTotalDays=",
                        FormatInvariant(
                            world.Calendar.TotalDays,
                            "0.######"
                        ),
                        " cooldownRemainingDays=",
                        cooldownRemainingDays.HasValue
                            ? FormatInvariant(
                                cooldownRemainingDays.Value,
                                "0.######"
                            )
                            : "null",
                        " cooldownRemainingHours=",
                        cooldownRemainingHours.HasValue
                            ? FormatInvariant(
                                cooldownRemainingHours.Value,
                                "0.###"
                            )
                            : "null",
                        " cooldownNextAllowedTotalDays=",
                        cooldownNextAllowedTotalDays.HasValue
                            ? FormatInvariant(
                                cooldownNextAllowedTotalDays.Value,
                                "0.######"
                            )
                            : "null",
                        " messageSent=true",
                        " blockBreakCompleted=false",
                        " cooldownStarted=false",
                        " satietyCostApplied=false",
                        " depositScanScheduled=false",
                        " gemScanScheduled=false",
                        " localizationKey=",
                        Quote(localizationKey),
                        " message=",
                        Quote(message)
                    )
            );
        }

        internal static void WriteDepositScanCompleted(
            string? surveyId,
            IPlayer player,
            ItemStack? originalPropickStack,
            ItemStack? currentSlotStack,
            string mineralCode,
            int matchingBlockCount,
            int depositCountBeforeLimit,
            int maximumDepositCount,
            int returnedDepositCount,
            string outcome,
            bool durabilityCostApplied,
            int durabilityCost,
            int durabilityBefore,
            int durabilityAfter,
            string? localizationKey,
            string? message,
            int depositWaypointAddedCount = 0,
            TargetedProspectingScanTiming? scanTiming = null
        )
        {
            if (!Enabled || surveyId == null)
            {
                return;
            }

            string? languageCode =
                player is IServerPlayer serverPlayer
                    ? serverPlayer.LanguageCode
                    : null;

            WriteInformation(
                () =>
                    string.Concat(
                        "event=deposit-scan-completed",
                        " surveyId=",
                        Quote(surveyId),
                        " side=server",
                        " playerName=",
                        Quote(player.PlayerName),
                        " playerUid=",
                        Quote(player.PlayerUID),
                        " gameMode=",
                        Quote(
                            player.WorldData.CurrentGameMode
                                .ToString()
                        ),
                        " language=",
                        Quote(languageCode),
                        BuildItemFields(
                            "originalMainHand",
                            originalPropickStack
                        ),
                        BuildItemFields(
                            "currentSlot",
                            currentSlotStack
                        ),
                        " slotItemUnchanged=",
                        ReferenceEquals(
                            originalPropickStack,
                            currentSlotStack
                        )
                            ? "true"
                            : "false",
                        " mineralCode=",
                        Quote(mineralCode),
                        " matchingBlockCount=",
                        FormatInvariant(
                            matchingBlockCount
                        ),
                        " depositCountBeforeLimit=",
                        FormatInvariant(
                            depositCountBeforeLimit
                        ),
                        " maximumDepositCount=",
                        FormatInvariant(
                            maximumDepositCount
                        ),
                        " returnedDepositCount=",
                        FormatInvariant(
                            returnedDepositCount
                        ),
                        " scanVolumeBlockCount=",
                        scanTiming == null ? "null" : FormatInvariant(scanTiming.ScanVolumeBlockCount),
                        " scanElapsedMs=",
                        scanTiming == null ? "null" : FormatInvariant(scanTiming.ElapsedMs, "0.###"),
                        " scanSliceCount=",
                        scanTiming == null ? "null" : FormatInvariant(scanTiming.SliceCount),
                        " scanSliceWorkMs=",
                        scanTiming == null ? "null" : FormatInvariant(scanTiming.SliceWorkMs, "0.###"),
                        " scanMaxSliceMs=",
                        scanTiming == null ? "null" : FormatInvariant(scanTiming.MaxSliceMs, "0.###"),
                        " scanCompletionWorkMs=",
                        scanTiming == null ? "null" : FormatInvariant(scanTiming.CompletionWorkMs, "0.###"),
                        " scanWorkMs=",
                        scanTiming == null ? "null" : FormatInvariant(scanTiming.WorkMs, "0.###"),
                        " scanMaxBlockingMs=",
                        scanTiming == null ? "null" : FormatInvariant(scanTiming.MaxBlockingMs, "0.###"),
                        " depositWaypointsAttempted=",
                        outcome == "success"
                            ? "true"
                            : "false",
                        " depositWaypointCandidateCount=",
                        outcome == "success"
                            ? FormatInvariant(
                                returnedDepositCount
                            )
                            : "0",
                        " depositWaypointAddedCount=",
                        outcome == "success"
                            ? FormatInvariant(
                                depositWaypointAddedCount
                            )
                            : "0",
                        " depositWaypointNotAddedCount=",
                        outcome == "success"
                            ? FormatInvariant(
                                Math.Max(
                                    0,
                                    returnedDepositCount
                                        - depositWaypointAddedCount
                                )
                            )
                            : "0",
                        " depositWaypointAddedAny=",
                        depositWaypointAddedCount > 0
                            ? "true"
                            : "false",
                        " depositWaypointOutcome=",
                        Quote(
                            outcome != "success"
                                ? "not-attempted"
                                : depositWaypointAddedCount <= 0
                                    ? "none-added"
                                    : depositWaypointAddedCount
                                        >= returnedDepositCount
                                        ? "added-all"
                                        : "added-partial"
                        ),
                        " outcome=",
                        Quote(outcome),
                        " durabilityCostApplied=",
                        durabilityCostApplied
                            ? "true"
                            : "false",
                        " durabilityCostCalculated=",
                        FormatInvariant(
                            durabilityCost
                        ),
                        " durabilityCostActual=",
                        FormatInvariant(
                            Math.Max(
                                0,
                                durabilityBefore
                                    - durabilityAfter
                            )
                        ),
                        " durabilityBefore=",
                        FormatInvariant(
                            durabilityBefore
                        ),
                        " durabilityAfter=",
                        FormatInvariant(
                            durabilityAfter
                        ),
                        " localizationKey=",
                        Quote(localizationKey),
                        " message=",
                        Quote(message)
                    )
            );
        }

        internal static void WriteGemScanCompleted(
            string? surveyId,
            IPlayer player,
            int diamondBlockCount,
            int diamondDepositCount,
            string diamondDeposits,
            int diamondEligibleDepositCount,
            string diamondEligibleDeposits,
            int diamondDeduplicatedDepositCount,
            string diamondDeduplicatedDeposits,
            int emeraldBlockCount,
            int emeraldDepositCount,
            string emeraldDeposits,
            int emeraldEligibleDepositCount,
            string emeraldEligibleDeposits,
            int emeraldDeduplicatedDepositCount,
            string emeraldDeduplicatedDeposits,
            int peridotBlockCount,
            int peridotDepositCount,
            string peridotDeposits,
            int peridotEligibleDepositCount,
            string peridotEligibleDeposits,
            int peridotDeduplicatedDepositCount,
            string peridotDeduplicatedDeposits,
            string selectionMode,
            string? randomizedGemTypes,
            string? selectedMineralCode,
            string? selectedDeposit,
            int selectedDepositCandidateCount,
            bool depositSelectionRandomized,
            bool waypointAdded
        )
        {
            if (!Enabled || surveyId == null)
            {
                return;
            }

            string? languageCode =
                player is IServerPlayer serverPlayer
                    ? serverPlayer.LanguageCode
                    : null;

            WriteInformation(
                () =>
                    string.Concat(
                        "event=gem-scan-completed",
                        " surveyId=",
                        Quote(surveyId),
                        " side=server",
                        " playerName=",
                        Quote(player.PlayerName),
                        " playerUid=",
                        Quote(player.PlayerUID),
                        " gameMode=",
                        Quote(
                            player.WorldData.CurrentGameMode
                                .ToString()
                        ),
                        " language=",
                        Quote(languageCode),
                        " diamondBlockCount=",
                        FormatInvariant(
                            diamondBlockCount
                        ),
                        " diamondDepositCount=",
                        FormatInvariant(
                            diamondDepositCount
                        ),
                        " diamondDeposits=",
                        Quote(diamondDeposits),
                        " diamondEligibleDepositCount=",
                        FormatInvariant(
                            diamondEligibleDepositCount
                        ),
                        " diamondEligibleDeposits=",
                        Quote(diamondEligibleDeposits),
                        " diamondDeduplicatedDepositCount=",
                        FormatInvariant(
                            diamondDeduplicatedDepositCount
                        ),
                        " diamondDeduplicatedDeposits=",
                        Quote(diamondDeduplicatedDeposits),
                        " emeraldBlockCount=",
                        FormatInvariant(
                            emeraldBlockCount
                        ),
                        " emeraldDepositCount=",
                        FormatInvariant(
                            emeraldDepositCount
                        ),
                        " emeraldDeposits=",
                        Quote(emeraldDeposits),
                        " emeraldEligibleDepositCount=",
                        FormatInvariant(
                            emeraldEligibleDepositCount
                        ),
                        " emeraldEligibleDeposits=",
                        Quote(emeraldEligibleDeposits),
                        " emeraldDeduplicatedDepositCount=",
                        FormatInvariant(
                            emeraldDeduplicatedDepositCount
                        ),
                        " emeraldDeduplicatedDeposits=",
                        Quote(emeraldDeduplicatedDeposits),
                        " peridotBlockCount=",
                        FormatInvariant(
                            peridotBlockCount
                        ),
                        " peridotDepositCount=",
                        FormatInvariant(
                            peridotDepositCount
                        ),
                        " peridotDeposits=",
                        Quote(peridotDeposits),
                        " peridotEligibleDepositCount=",
                        FormatInvariant(
                            peridotEligibleDepositCount
                        ),
                        " peridotEligibleDeposits=",
                        Quote(peridotEligibleDeposits),
                        " peridotDeduplicatedDepositCount=",
                        FormatInvariant(
                            peridotDeduplicatedDepositCount
                        ),
                        " peridotDeduplicatedDeposits=",
                        Quote(peridotDeduplicatedDeposits),
                        " selectionMode=",
                        Quote(selectionMode),
                        " randomizedGemTypes=",
                        Quote(randomizedGemTypes),
                        " selectedMineralCode=",
                        Quote(selectedMineralCode),
                        " selectedDeposit=",
                        Quote(selectedDeposit),
                        " selectedDepositCandidateCount=",
                        FormatInvariant(
                            selectedDepositCandidateCount
                        ),
                        " depositSelectionRandomized=",
                        depositSelectionRandomized
                            ? "true"
                            : "false",
                        " waypointAdded=",
                        waypointAdded
                            ? "true"
                            : "false"
                    )
            );
        }

        internal static void WriteDisassemblyResult(
            IPlayer player,
            string? propickCode,
            string? propickMaterial,
            int propickDurabilityBefore,
            int propickDurabilityMaximum,
            string? sawCode,
            int? sawDurabilityBefore,
            int? sawDurabilityAfter,
            string? hammerCode,
            int? hammerDurabilityBefore,
            int? hammerDurabilityAfter,
            string? chiselCode,
            int? chiselDurabilityBefore,
            int? chiselDurabilityAfter,
            int calculatedNuggetCount,
            int? actualNuggetCount,
            string? outputNuggetCode,
            string outcome,
            string? reason
        )
        {
            if (
                !Enabled
                || player.Entity.World.Side
                    != EnumAppSide.Server
            )
            {
                return;
            }

            double? durabilityPercent =
                propickDurabilityMaximum > 0
                    ? propickDurabilityBefore
                        * 100.0
                        / propickDurabilityMaximum
                    : null;

            bool isError =
                !string.Equals(
                    outcome,
                    "success",
                    StringComparison.Ordinal
                );

            string message =
                string.Concat(
                    isError
                        ? "level=error "
                        : string.Empty,
                    "event=propick-disassembly",
                    " side=server",
                    " playerName=",
                    Quote(player.PlayerName),
                    " playerUid=",
                    Quote(player.PlayerUID),
                    " gameMode=",
                    Quote(
                        player.WorldData.CurrentGameMode
                            .ToString()
                    ),
                    " propickCode=",
                    Quote(propickCode),
                    " propickMaterial=",
                    Quote(propickMaterial),
                    " propickDurabilityBefore=",
                    FormatInvariant(
                        propickDurabilityBefore
                    ),
                    " propickDurabilityMaximum=",
                    FormatInvariant(
                        propickDurabilityMaximum
                    ),
                    " propickDurabilityPercent=",
                    durabilityPercent.HasValue
                        ? FormatInvariant(
                            durabilityPercent.Value,
                            "0.###"
                        )
                        : "null",
                    " sawCode=",
                    Quote(sawCode),
                    " sawDurabilityBefore=",
                    sawDurabilityBefore.HasValue
                        ? FormatInvariant(
                            sawDurabilityBefore.Value
                        )
                        : "null",
                    " sawDurabilityAfter=",
                    sawDurabilityAfter.HasValue
                        ? FormatInvariant(
                            sawDurabilityAfter.Value
                        )
                        : "null",
                    " hammerCode=",
                    Quote(hammerCode),
                    " hammerDurabilityBefore=",
                    hammerDurabilityBefore.HasValue
                        ? FormatInvariant(
                            hammerDurabilityBefore.Value
                        )
                        : "null",
                    " hammerDurabilityAfter=",
                    hammerDurabilityAfter.HasValue
                        ? FormatInvariant(
                            hammerDurabilityAfter.Value
                        )
                        : "null",
                    " chiselCode=",
                    Quote(chiselCode),
                    " chiselDurabilityBefore=",
                    chiselDurabilityBefore.HasValue
                        ? FormatInvariant(
                            chiselDurabilityBefore.Value
                        )
                        : "null",
                    " chiselDurabilityAfter=",
                    chiselDurabilityAfter.HasValue
                        ? FormatInvariant(
                            chiselDurabilityAfter.Value
                        )
                        : "null",
                    " calculatedNuggetCount=",
                    FormatInvariant(
                        calculatedNuggetCount
                    ),
                    " actualNuggetCount=",
                    actualNuggetCount.HasValue
                        ? FormatInvariant(
                            actualNuggetCount.Value
                        )
                        : "null",
                    " outputNuggetCode=",
                    Quote(outputNuggetCode),
                    " outcome=",
                    Quote(outcome),
                    " reason=",
                    Quote(reason)
                );

            if (isError)
            {
                WriteStandardError(message);
            }

            WriteSessionEvent(
                () => message,
                isError
            );
        }

        internal static void WriteCommandResult(
            string command,
            string result,
            string reason,
            string localizationKey,
            string message,
            IPlayer player,
            string languageCode,
            string? mineralCode = null
        )
        {
            if (!Enabled)
            {
                return;
            }

            WriteInformation(
                () =>
                {
                    ItemStack? mainHandStack =
                        player.InventoryManager
                            .ActiveHotbarSlot?.Itemstack;

                    ItemStack? offHandStack =
                        player.InventoryManager
                            .OffhandHotbarSlot?.Itemstack;

                    return string.Concat(
                        "event=command-result",
                        " side=server",
                        " command=",
                        Quote(command),
                        " result=",
                        Quote(result),
                        " reason=",
                        Quote(reason),
                        " playerName=",
                        Quote(player.PlayerName),
                        " playerUid=",
                        Quote(player.PlayerUID),
                        " gameMode=",
                        Quote(
                            player.WorldData.CurrentGameMode
                                .ToString()
                        ),
                        " language=",
                        Quote(languageCode),
                        BuildItemFields(
                            "mainHand",
                            mainHandStack
                        ),
                        BuildItemFields(
                            "offHand",
                            offHandStack
                        ),
                        " mineralCode=",
                        Quote(mineralCode),
                        " localizationKey=",
                        Quote(localizationKey),
                        " message=",
                        Quote(message)
                    );
                }
            );
        }

        private static string BuildItemFields(
            string fieldPrefix,
            ItemStack? stack
        )
        {
            if (stack == null)
            {
                return string.Concat(
                    " ",
                    fieldPrefix,
                    "Code=null ",
                    fieldPrefix,
                    "Class=null ",
                    fieldPrefix,
                    "StackSize=null ",
                    fieldPrefix,
                    "DurabilityCurrent=null ",
                    fieldPrefix,
                    "DurabilityMaximum=null ",
                    fieldPrefix,
                    "DurabilityPercent=null"
                );
            }

            CollectibleObject? collectible =
                stack.Collectible;

            int maximumDurability =
                collectible?.GetMaxDurability(
                    stack
                ) ?? 0;

            int currentDurability =
                collectible != null
                    && maximumDurability > 0
                    ? collectible.GetRemainingDurability(
                        stack
                    )
                    : 0;

            double durabilityPercent =
                maximumDurability > 0
                    ? currentDurability
                        * 100.0
                        / maximumDurability
                    : 0d;

            return string.Concat(
                " ",
                fieldPrefix,
                "Code=",
                Quote(
                    stack.Collectible?.Code?.ToString()
                ),
                " ",
                fieldPrefix,
                "Class=",
                Quote(
                    stack.Collectible?.GetType().FullName
                ),
                " ",
                fieldPrefix,
                "StackSize=",
                FormatInvariant(
                    stack.StackSize
                ),
                " ",
                fieldPrefix,
                "DurabilityCurrent=",
                maximumDurability > 0
                    ? FormatInvariant(
                        currentDurability
                    )
                    : "null",
                " ",
                fieldPrefix,
                "DurabilityMaximum=",
                maximumDurability > 0
                    ? FormatInvariant(
                        maximumDurability
                    )
                    : "null",
                " ",
                fieldPrefix,
                "DurabilityPercent=",
                maximumDurability > 0
                    ? FormatInvariant(
                        durabilityPercent,
                        "0.###"
                    )
                    : "null"
            );
        }

        internal static string FormatInvariant(
            IFormattable value,
            string? format = null
        )
        {
            return value.ToString(
                format,
                CultureInfo.InvariantCulture
            );
        }

        internal static string Quote(
            string? value
        )
        {
            if (value == null)
            {
                return "null";
            }

            return string.Concat(
                "\"",
                value
                    .Replace(
                        "\\",
                        "\\\\",
                        StringComparison.Ordinal
                    )
                    .Replace(
                        "\"",
                        "\\\"",
                        StringComparison.Ordinal
                    )
                    .Replace(
                        "\r",
                        "\\r",
                        StringComparison.Ordinal
                    )
                    .Replace(
                        "\n",
                        "\\n",
                        StringComparison.Ordinal
                    )
                    .Replace(
                        "\t",
                        "\\t",
                        StringComparison.Ordinal
                    ),
                "\""
            );
        }
    }
}
