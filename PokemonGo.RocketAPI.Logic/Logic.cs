﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AllEnum;
using PokemonGo.RocketAPI.Enums;
using PokemonGo.RocketAPI.Extensions;
using PokemonGo.RocketAPI.GeneratedCode;
using PokemonGo.RocketAPI.Logic.Utils;

namespace PokemonGo.RocketAPI.Logic
{
    public class Logic
    {
        private readonly Client _client;
        private readonly ISettings _clientSettings;
        private readonly Inventory _inventory;

        public Logic(ISettings clientSettings)
        {
            _clientSettings = clientSettings;
            _client = new Client(_clientSettings);
            _inventory = new Inventory(_client);
        }

        public async void Execute()
        {
            Logger.Write($"Starting Execute on login server: {_clientSettings.AuthType}", LogLevel.Info);
            
            if (_clientSettings.AuthType == AuthType.Ptc)
                await _client.DoPtcLogin(_clientSettings.PtcUsername, _clientSettings.PtcPassword);
            else if (_clientSettings.AuthType == AuthType.Google)
                await _client.DoGoogleLogin();

            while (true)
            {
                try
                {
                    await _client.SetServer();
                    await RepeatAction(10, async () => await ExecuteFarmingPokestopsAndPokemons(_client));
                    await TransferDuplicatePokemon();

                    /*
                * Example calls below
                *
                var profile = await _client.GetProfile();
                var settings = await _client.GetSettings();
                var mapObjects = await _client.GetMapObjects();
                var inventory = await _client.GetInventory();
                var pokemons = inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon).Where(p => p != null && p?.PokemonId > 0);
                */
                }
                catch (Exception ex)
                {
                    Logger.Write($"Exception: {ex}", LogLevel.Error);
                }

                await Task.Delay(10000);
            }
        }

        public async Task RepeatAction(int repeat, Func<Task> action)
        {
            for (int i = 0; i < repeat; i++)
                await action();
        }

        private async Task ExecuteFarmingPokestopsAndPokemons(Client client)
        {
            var mapObjects = await client.GetMapObjects();

            var pokeStops = mapObjects.MapCells.SelectMany(i => i.Forts).Where(i => i.Type == FortType.Checkpoint && i.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime());

            foreach (var pokeStop in pokeStops)
            {
                var update = await client.UpdatePlayerLocation(pokeStop.Latitude, pokeStop.Longitude);
                var fortInfo = await client.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                var fortSearch = await client.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);

                Logger.Write($"Farmed XP: {fortSearch.ExperienceAwarded}, Gems: { fortSearch.GemsAwarded}, Eggs: {fortSearch.PokemonDataEgg} Items: {StringUtils.GetSummedFriendlyNameOfItemAwardList(fortSearch.ItemsAwarded)}", LogLevel.Info);

                await Task.Delay(15000);
                await ExecuteCatchAllNearbyPokemons(client);
            }
        }

        private async Task ExecuteCatchAllNearbyPokemons(Client client)
        {
            var mapObjects = await client.GetMapObjects();

            var pokemons = mapObjects.MapCells.SelectMany(i => i.CatchablePokemons);

            foreach (var pokemon in pokemons)
            {
                var update = await client.UpdatePlayerLocation(pokemon.Latitude, pokemon.Longitude);
                var encounterPokemonResponse = await client.EncounterPokemon(pokemon.EncounterId, pokemon.SpawnpointId);

                CatchPokemonResponse caughtPokemonResponse;
                do
                {
                    caughtPokemonResponse = await client.CatchPokemon(pokemon.EncounterId, pokemon.SpawnpointId, pokemon.Latitude, pokemon.Longitude, MiscEnums.Item.ITEM_POKE_BALL); //note: reverted from settings because this should not be part of settings but part of logic
                }
                while (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchMissed);

                Logger.Write(caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchSuccess ? $"We caught a {pokemon.PokemonId} with CP {encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp}" : $"{pokemon.PokemonId} with CP {encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp} got away..", LogLevel.Info);
                await Task.Delay(5000);
            }
        }

        private async Task EvolveAllGivenPokemons(IEnumerable<Pokemon> pokemonToEvolve)
        {
            foreach (var pokemon in pokemonToEvolve)
            {
                EvolvePokemonOut evolvePokemonOutProto;
                do
                {
                    evolvePokemonOutProto = await _client.EvolvePokemon((ulong)pokemon.Id); 

                    if (evolvePokemonOutProto.Result == EvolvePokemonOut.Types.EvolvePokemonStatus.PokemonEvolvedSuccess)
                        Logger.Write($"Evolved {pokemon.PokemonType} successfully for {evolvePokemonOutProto.ExpAwarded}xp", LogLevel.Info);
                    else
                        Logger.Write($"Failed to evolve {pokemon.PokemonType}. EvolvePokemonOutProto.Result was {evolvePokemonOutProto.Result}, stopping evolving {pokemon.PokemonType}", LogLevel.Info);

                    await Task.Delay(3000);
                }
                while (evolvePokemonOutProto.Result == EvolvePokemonOut.Types.EvolvePokemonStatus.PokemonEvolvedSuccess);

                await Task.Delay(3000);
            }
        }

        private async Task TransferDuplicatePokemon()
        {
            Logger.Write($"Transfering duplicate Pokemon", LogLevel.Info);

            var duplicatePokemons = await _inventory.GetDuplicatePokemonToTransfer();
            foreach (var duplicatePokemon in duplicatePokemons)
            {
                var transfer = await _client.TransferPokemon(duplicatePokemon.Id);
                Logger.Write($"Transfer {duplicatePokemon.PokemonId} with {duplicatePokemon.Cp})", LogLevel.Info);
                await Task.Delay(500);
            }
        }
    }
}