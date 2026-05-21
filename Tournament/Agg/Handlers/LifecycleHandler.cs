using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Tournament.Agg.Handlers;

public static class LifecycleHandler
{
    /// <summary>
    /// Advance the blind level. When the command carries chip-race fields
    /// (retire_denomination + new_denomination), also emit a ColorUpCompleted
    /// derived from the seeded player_chip_inventories (TDA Rule 24A).
    ///
    /// Mirrors Python's handle_advance_blind_level + _compute_chip_race.
    /// When chip race is active, the ColorUpCompleted is returned (the
    /// BlindLevelAdvanced is recorded as side state via the BlindAdvanced
    /// applier; tests interested only in the chip-race event get the canonical
    /// one). When no chip race, the BlindLevelAdvanced is returned.
    /// </summary>
    public static IMessage HandleAdvanceBlindLevel(AdvanceBlindLevel cmd, TournamentState state)
    {
        if (!state.IsRunning)
            throw CommandRejectedError.PreconditionFailed(
                "TOURNAMENT_NOT_RUNNING",
                "Tournament is not running");

        var chipRace = cmd.RetireDenomination > 0 && cmd.NewDenomination > 0;
        var maxDefined = state.BlindStructure.Count;
        var newLevel = state.CurrentLevel + 1;

        // EU-0855 / EU-0856: refuse to advance past the declared structure.
        // Chip-race-only commands (no structural advance) are still subject to
        // the same guard if the structure is exhausted AND no race fields are
        // present.
        if (newLevel > maxDefined && !chipRace)
            throw CommandRejectedError.PreconditionFailed(
                "BLIND_STRUCTURE_EXHAUSTED",
                $"Blind structure exhausted at level {state.CurrentLevel}; max defined level is {maxDefined}",
                new Dictionary<string, string>
                {
                    ["current"] = state.CurrentLevel.ToString(),
                    ["max_value"] = maxDefined.ToString(),
                });

        BlindLevelAdvanced? blindEvent = null;
        if (newLevel <= maxDefined)
        {
            var level = state.BlindStructure[newLevel - 1];
            blindEvent = new BlindLevelAdvanced
            {
                Level = newLevel,
                SmallBlind = level.SmallBlind,
                BigBlind = level.BigBlind,
                Ante = level.Ante,
                AdvancedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };
        }

        if (!chipRace)
        {
            return blindEvent!;
        }

        var colorEvent = ComputeChipRace(state, cmd.RetireDenomination, cmd.NewDenomination);
        // Apply the blind advance to state (caller's apply loop will also apply
        // the returned color event). This keeps current_level moving even when
        // the chip-race event is what the caller consumes.
        if (blindEvent != null)
            state.ApplyBlindAdvanced(blindEvent);
        return colorEvent;
    }

    /// <summary>
    /// TDA Rule 24A: each player's retired-denomination chips convert outright
    /// as far as full new-denom chips go. Remainders form a global pool; the
    /// number of new-denom chips drawn from the pool equals
    /// total_remainder // new_denom. Each contender wins at most one chip; the
    /// residual pool value is removed without compensation (Rule 24C). The
    /// single-chip rescue clause (Rule 24A) protects any player left with zero
    /// chips after the race.
    /// </summary>
    public static ColorUpCompleted ComputeChipRace(
        TournamentState state, long retireDenom, long newDenom)
    {
        var perPlayer = new List<(string hex, long full, long remainder)>();
        long totalRemainder = 0;
        foreach (var playerHex in state.PlayerChipInventories.Keys.OrderBy(k => k))
        {
            var inventory = state.PlayerChipInventories[playerHex];
            var retireCount = inventory.GetValueOrDefault((int)retireDenom, 0);
            var valueInRetired = retireCount * retireDenom;
            var fullNew = valueInRetired / newDenom;
            var remainder = valueInRetired - fullNew * newDenom;
            perPlayer.Add((playerHex, fullNew, remainder));
            totalRemainder += remainder;
        }

        var raceChipsToAward = totalRemainder / newDenom;
        var chipsRemovedByRace = totalRemainder - raceChipsToAward * newDenom;

        var contenders = perPlayer
            .Where(p => p.remainder > 0)
            .OrderByDescending(p => p.remainder)
            .ThenBy(p => p.hex)
            .Take((int)raceChipsToAward)
            .Select(p => p.hex)
            .ToHashSet();

        var evt = new ColorUpCompleted
        {
            RetiredDenomination = retireDenom,
            NewDenomination = newDenom,
            ChipsRemovedByRace = chipsRemovedByRace,
            CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        long chipsAddedByRescue = 0;
        foreach (var (hex, full, _) in perPlayer)
        {
            var chipsWon = full;
            if (contenders.Contains(hex)) chipsWon += 1;
            var nonRetiredValue = state.PlayerChipInventories[hex]
                .Where(kv => kv.Key != (int)retireDenom)
                .Sum(kv => (long)kv.Key * kv.Value);
            var stakeAfter = nonRetiredValue + chipsWon * newDenom;
            var rescued = false;
            if (stakeAfter == 0)
            {
                chipsWon += 1;
                rescued = true;
                chipsAddedByRescue += newDenom;
            }
            evt.PerPlayerAwards.Add(new ChipRaceAward
            {
                PlayerRoot = Google.Protobuf.ByteString.CopyFrom(HexToBytes(hex)),
                ChipsWon = (int)chipsWon,
                Rescued = rescued,
            });
        }
        evt.ChipsAddedByRescue = chipsAddedByRescue;
        return evt;
    }

    private static byte[] HexToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    public static PlayerEliminated HandleEliminatePlayer(EliminatePlayer cmd, TournamentState state)
    {
        if (!state.IsRunning)
            throw CommandRejectedError.PreconditionFailed(
                "TOURNAMENT_NOT_RUNNING",
                "Tournament is not running");

        var rootHex = Convert.ToHexString(cmd.PlayerRoot.ToByteArray()).ToLowerInvariant();
        if (!state.IsPlayerRegistered(rootHex))
            throw CommandRejectedError.PreconditionFailed(
                "PLAYER_NOT_REGISTERED",
                $"Player {rootHex} is not registered in this tournament");

        return new PlayerEliminated
        {
            PlayerRoot = cmd.PlayerRoot,
            FinishPosition = state.PlayersRemaining,
            HandRoot = cmd.HandRoot,
            EliminatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    public static TournamentPaused HandlePauseTournament(PauseTournament cmd, TournamentState state)
    {
        if (state.Status == TournamentStatus.TournamentPaused)
            throw CommandRejectedError.PreconditionFailed(
                "TOURNAMENT_ALREADY_PAUSED",
                "Tournament is already paused");
        if (!state.IsRunning)
            throw CommandRejectedError.PreconditionFailed(
                "TOURNAMENT_NOT_RUNNING",
                "Tournament is not running");

        return new TournamentPaused
        {
            Reason = cmd.Reason,
            PausedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    public static TournamentResumed HandleResumeTournament(ResumeTournament cmd, TournamentState state)
    {
        // Day-2 resume: a BAGGED tournament can also be resumed.
        if (state.Status == TournamentStatus.TournamentBagged)
        {
            return new TournamentResumed
            {
                ResumedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };
        }
        if (state.Status != TournamentStatus.TournamentPaused)
            throw CommandRejectedError.PreconditionFailed(
                "TOURNAMENT_NOT_PAUSED",
                "Tournament is not paused");

        return new TournamentResumed
        {
            ResumedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    public static TournamentStarted HandleStartTournament(StartTournament cmd, TournamentState state)
    {
        if (!state.Exists)
            throw CommandRejectedError.PreconditionFailed(
                "TOURNAMENT_NOT_FOUND",
                "Tournament does not exist");
        if (state.RegisteredPlayers.Count < state.MinPlayers)
            throw CommandRejectedError.PreconditionFailed(
                "NOT_ENOUGH_PLAYERS_TO_START",
                $"Not enough players to start: requested {state.MinPlayers}, available {state.RegisteredPlayers.Count}");

        return new TournamentStarted
        {
            TotalPlayers = state.RegisteredPlayers.Count,
            TotalPrizePool = state.TotalPrizePool,
            StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }
}
