﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace PKHeX.Core.AutoMod
{
    public static class BattleTemplateLegality
    {
        public const string ANALYSIS_INVALID = "No possible encounter could be found. Specific analysis for this set is unavailable.";
        private static string SPECIES_UNAVAILABLE_FORM => "{0} with form {1} is unavailable in the game.";
        private static string SPECIES_UNAVAILABLE => "{0} is unavailable in the game.";
        private static string INVALID_MOVES => "{0} cannot learn the following move(s) in the game: {1}.";
        private static string ALL_MOVES_INVALID => "All the requested moves for this Pokémon are invalid.";
        private static string LEVEL_INVALID => "Requested level is lower than the minimum possible level for {0}. Minimum required level is {1}";
        private static string ONLY_HIDDEN_ABILITY_AVAILABLE => "You can only obtain {0} with hidden ability in this game.";
        private static string HIDDEN_ABILITY_UNAVAILABLE => "You cannot obtain {0} with hidden ability in this game.";

        public static string SetAnalysis(this RegenTemplate set, SaveFile sav)
        {
            var species_name = SpeciesName.GetSpeciesNameGeneration(set.Species, (int)LanguageID.English, sav.Generation);
            var analysis = set.Form == 0 ? string.Format(SPECIES_UNAVAILABLE, species_name)
                                     : string.Format(SPECIES_UNAVAILABLE_FORM, species_name, set.FormName);

            // Species checks
            var gv = (GameVersion)sav.Game;
            if (!gv.ExistsInGame(set.Species, set.Form))
                return analysis; // Species does not exist in the game

            // Species exists -- check if it has atleast one move. If it has no moves and it didn't generate, that makes the mon still illegal in game (moves are set to legal ones)
            var moves = set.Moves.Where(z => z != 0);
            var count = moves.Count();

            // Reusable data
            var blank = sav.BlankPKM;
            var batchedit = APILegality.AllowBatchCommands && set.Regen.HasBatchSettings;
            var destVer = (GameVersion)sav.Game;
            if (destVer <= 0)
                destVer = sav.Version;
            var gamelist = APILegality.FilteredGameList(blank, destVer, batchedit ? set.Regen.Batch.Filters : null);

            // Move checks
            List<IEnumerable<int>> move_combinations = new();
            for (int i = count; i >= 1; i--)
                move_combinations.AddRange(GetKCombs(moves, i));

            int[] original_moves = new int[4];
            set.Moves.CopyTo(original_moves, 0);
            int[] successful_combination = GetValidMoves(set, sav, move_combinations, blank, gamelist);
            if (!new HashSet<int>(original_moves.Where(z => z != 0)).SetEquals(successful_combination))
            {
                var invalid_moves = string.Join(", ", original_moves.Where(z => !successful_combination.Contains(z) && z != 0).Select(z => $"{(Move)z}"));
                return successful_combination.Length > 0 ? string.Format(INVALID_MOVES, species_name, invalid_moves) : ALL_MOVES_INVALID;
            }
            set.Moves = original_moves;

            // All moves possible, get encounters
            blank.ApplySetDetails(set);
            blank.SetRecordFlags();
            var encounters = EncounterMovesetGenerator.GenerateEncounters(pk: blank, moves: original_moves, gamelist);
            if (set.Regen.EncounterFilters != null)
                encounters = encounters.Where(enc => BatchEditing.IsFilterMatch(set.Regen.EncounterFilters, enc));

            // Level checks, check if level is impossible to achieve
            if (encounters.All(z => !APILegality.IsRequestedLevelValid(set, z)))
                return string.Format(LEVEL_INVALID, species_name, encounters.Min(z => z.LevelMin));
            encounters = encounters.Where(enc => APILegality.IsRequestedLevelValid(set, enc));

            // Ability checks
            var abilityreq = APILegality.GetRequestedAbility(blank, set);
            if (abilityreq == AbilityRequest.NotHidden && encounters.All(z => z is EncounterStatic { Ability: 4 }))
                return string.Format(ONLY_HIDDEN_ABILITY_AVAILABLE, species_name);
            if (abilityreq == AbilityRequest.Hidden && encounters.All(z => z.Generation is 3 or 4) && destVer.GetGeneration() < 8)
                return string.Format(HIDDEN_ABILITY_UNAVAILABLE, species_name);

            return ANALYSIS_INVALID;
        }

        private static int[] GetValidMoves(RegenTemplate set, SaveFile sav, List<IEnumerable<int>> move_combinations, PKM blank, GameVersion[] gamelist)
        {
            int[] successful_combination = new int[0];
            foreach (var combination in move_combinations)
            {
                if (combination.Count() <= successful_combination.Length)
                    continue;
                var new_moves = combination.Concat(Enumerable.Repeat(0, 4 - combination.Count()));
                set.Moves = new_moves.ToArray();
                blank.ApplySetDetails(set);
                blank.SetRecordFlags();
                
                if (sav.Generation <= 2)
                    blank.EXP = 0; // no relearn moves in gen 1/2 so pass level 1 to generator

                var encounters = EncounterMovesetGenerator.GenerateEncounters(pk: blank, moves: set.Moves, gamelist);
                if (set.Regen.EncounterFilters != null)
                    encounters = encounters.Where(enc => BatchEditing.IsFilterMatch(set.Regen.EncounterFilters, enc));
                if (encounters.Any())
                    successful_combination = combination.ToArray();
            }
            return successful_combination;
        }

        private static IEnumerable<IEnumerable<T>> GetKCombs<T>(IEnumerable<T> list, int length) where T : IComparable
        {
            if (length == 1) return list.Select(t => new T[] { t });
            return GetKCombs(list, length - 1)
                .SelectMany(t => list.Where(o => o.CompareTo(t.Last()) > 0),
                    (t1, t2) => t1.Concat(new T[] { t2 }));
        }
    }
}
