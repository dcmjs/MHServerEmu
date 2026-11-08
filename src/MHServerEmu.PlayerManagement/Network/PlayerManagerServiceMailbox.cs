using System.Net;
using Gazillion;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.RateLimiting;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Games.GameData;
using MHServerEmu.PlayerManagement.Auth;
using MHServerEmu.PlayerManagement.Players;
using MHServerEmu.PlayerManagement.Regions;

namespace MHServerEmu.PlayerManagement.Network
{
    internal sealed class PlayerManagerServiceMailbox : ServiceMailbox
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly TimeLeakyBucketCollection<ulong> _playerLookupByNameRateLimiter = new(TimeSpan.FromSeconds(60), 20);

        private readonly PlayerManagerService _playerManager;

        public PlayerManagerServiceMailbox(PlayerManagerService playerManager)
        {
            _playerManager = playerManager;
        }

        protected override void HandleServiceMessage(IGameServiceMessage message)
        {
            switch (message)
            {
                case ServiceMessage.AddClient addClient:
                    OnAddClient(addClient);
                    break;

                case ServiceMessage.RemoveClient removeClient:
                    OnRemoveClient(removeClient);
                    break;

                case ServiceMessage.GameInstanceOp gameInstanceOp:
                    OnGameInstanceOp(gameInstanceOp);
                    break;

                case ServiceMessage.GameInstanceClientOp gameInstanceClientOp:
                    OnGameInstanceClientOp(gameInstanceClientOp);
                    break;

                case ServiceMessage.CreateRegionResult createRegionResult:
                    OnCreateRegionResult(createRegionResult);
                    break;

                case ServiceMessage.SetRegionPlayerAccess setRegionPlayerAccess:
                    OnSetRegionPlayerAccess(setRegionPlayerAccess);
                    break;

                case ServiceMessage.RequestRegionShutdown requestRegionShutdown:
                    OnRequestRegionShutdown(requestRegionShutdown);
                    break;

                case ServiceMessage.ChangeRegionRequest changeRegionRequest:
                    OnChangeRegionRequest(changeRegionRequest);
                    break;

                case ServiceMessage.RegionTransferFinished regionTransferFinished:
                    OnRegionTransferFinished(regionTransferFinished);
                    break;

                case ServiceMessage.ClearPrivateStoryRegions clearPrivateStoryRegions:
                    OnClearPrivateStoryRegions(clearPrivateStoryRegions);
                    break;

                case ServiceMessage.SetDifficultyTierPreference setDifficultyTierPreference:
                    OnSetDifficultyTierPreference(setDifficultyTierPreference);
                    break;

                case ServiceMessage.PlayerDataUpdated playerDataUpdated:
                    OnPlayerDataUpdated(playerDataUpdated);
                    break;

                case ServiceMessage.PlayerLookupByNameRequest playerLookupByNameRequest:
                    OnPlayerLookupByNameRequest(playerLookupByNameRequest);
                    break;

                case ServiceMessage.PlayerNameChanged playerNameChanged:
                    OnPlayerNameChanged(playerNameChanged);
                    break;

                case ServiceMessage.CommunityStatusUpdate communityStatusUpdate:
                    OnCommunityStatusUpdate(communityStatusUpdate);
                    break;

                case ServiceMessage.CommunityStatusRequest communityStatusRequest:
                    OnCommunityStatusRequest(communityStatusRequest);
                    break;

#if GAME_VERSION_1_52 || GAME_VERSION_1_53
                // V48_FIXME
                case ServiceMessage.PartyOperationRequest partyOperationRequest:
                    OnPartyOperationRequest(partyOperationRequest);
                    break;

                case ServiceMessage.PartyBoostUpdate partyBoostUpdate:
                    OnPartyBoostUpdate(partyBoostUpdate);
                    break;
#endif

                case ServiceMessage.GuildMessageToPlayerManager guildMessageFromGame:
                    OnGuildMessageFromGame(guildMessageFromGame);
                    break;

                case ServiceMessage.MatchRegionRequestQueueCommand matchRegionRequestQueueCommand:
                    OnMatchRegionRequestQueueCommand(matchRegionRequestQueueCommand);
                    break;

                case ServiceMessage.AuthRequest authRequest:
                    OnAuthRequest(authRequest);
                    break;

                case ServiceMessage.SessionVerificationRequest sessionVerificationRequest:
                    OnSessionVerificationRequest(sessionVerificationRequest);
                    break;

                case ServiceMessage.MTXStoreESBalanceRequest mtxStoreESBalanceRequest:
                    OnMTXStoreESBalanceRequest(mtxStoreESBalanceRequest);
                    break;

                case ServiceMessage.MTXStoreESBalanceGameResponse mtxStoreESBalanceGameResponse:
                    OnMTXStoreESBalanceGameResponse(mtxStoreESBalanceGameResponse);
                    break;

                case ServiceMessage.MTXStoreESConvertRequest mtxStoreESConvertRequest:
                    OnMTXStoreESConvertRequest(mtxStoreESConvertRequest);
                    break;

                case ServiceMessage.MTXStoreESConvertGameResponse mtxStoreESConvertGameResponse:
                    OnMTXStoreESConvertGameResponse(mtxStoreESConvertGameResponse);
                    break;

                case ServiceMessage.MTXStoreAddFreeGRequest mtxStoreAddFreeGRequest:
                    OnMTXStoreAddFreeGRequest(mtxStoreAddFreeGRequest);
                    break;

                case ServiceMessage.MTXStoreAddFreeGGameResponse mtxStoreAddFreeGGameResponse:
                    OnMTXStoreAddFreeGGameResponse(mtxStoreAddFreeGGameResponse);
                    break;

                case ServiceMessage.MTXStoreAddFreeCurrencyRequest mtxStoreAddFreeCurrencyRequest:
                    OnMTXStoreAddFreeCurrencyRequest(mtxStoreAddFreeCurrencyRequest);
                    break;

                case ServiceMessage.MTXStoreAddFreeCurrencyGameResponse mtxStoreAddFreeCurrencyGameResponse:
                    OnMTXStoreAddFreeCurrencyGameResponse(mtxStoreAddFreeCurrencyGameResponse);
                    break;

                case ServiceMessage.MTXStoreItemSearchRequest mtxStoreItemSearchRequest:
                    OnMTXStoreItemSearchRequest(mtxStoreItemSearchRequest);
                    break;

                case ServiceMessage.MTXStoreItemSearchGameResponse mtxStoreItemSearchGameResponse:
                    OnMTXStoreItemSearchGameResponse(mtxStoreItemSearchGameResponse);
                    break;

                case ServiceMessage.MTXStoreAddFreeItemRequest mtxStoreAddFreeItemRequest:
                    OnMTXStoreAddFreeItemRequest(mtxStoreAddFreeItemRequest);
                    break;

                case ServiceMessage.MTXStoreAddFreeItemGameResponse mtxStoreAddFreeItemGameResponse:
                    OnMTXStoreAddFreeItemGameResponse(mtxStoreAddFreeItemGameResponse);
                    break;

                case ServiceMessage.AccountOperationRequest accountOperationRequest:
                    OnAccountOperationRequest(accountOperationRequest);
                    break;

                case ServiceMessage.SetWhitelistEnabled setWhitelistEnabled:
                    OnSetWhitelistEnabled(setWhitelistEnabled);
                    break;

                default:
                    Verify.IsTrue(false, $"Unhandled service message type {message.GetType().Name}");
                    break;
            }
        }

        #region Handlers

        private void OnAddClient(in ServiceMessage.AddClient addClient)
        {
            IFrontendClient client = addClient.Client;
            _playerManager.ClientManager.AddClient(client);
        }

        private void OnRemoveClient(in ServiceMessage.RemoveClient removeClient)
        {
            IFrontendClient client = removeClient.Client;
            _playerManager.ClientManager.RemoveClient(client);
        }

        private void OnGameInstanceOp(in ServiceMessage.GameInstanceOp gameInstanceOp)
        {
            GameInstanceOpType type = gameInstanceOp.Type;
            ulong gameId = gameInstanceOp.GameId;

            switch (type)
            {
                case GameInstanceOpType.CreateResponse:
                    _playerManager.GameHandleManager.OnInstanceCreateResponse(gameId);
                    break;

                case GameInstanceOpType.ShutdownNotice:
                    _playerManager.GameHandleManager.OnInstanceShutdownNotice(gameId);
                    break;

                default:
                    Verify.IsTrue(false, $"Unhandled operation type {type}");
                    break;
            }
        }

        private void OnGameInstanceClientOp(in ServiceMessage.GameInstanceClientOp gameInstanceClientOp)
        {
            IFrontendClient client = gameInstanceClientOp.Client;
            ulong gameId = gameInstanceClientOp.GameId;

            PlayerHandle player = _playerManager.ClientManager.GetPlayer(client.DbId);
            if (!Verify.IsNotNull(player, $"No handle found for client [{client}]"))
                return;

            switch (gameInstanceClientOp.Type)
            {
                case GameInstanceClientOpType.AddResponse:
                    player.FinishAddToGame(gameId);
                    break;

                case GameInstanceClientOpType.RemoveResponse:
                    player.FinishRemoveFromGame(gameId);
                    break;

                default:
                    Verify.IsTrue(false, $"Unhandled operation type {gameInstanceClientOp.Type}");
                    break;
            }
        }

        private void OnCreateRegionResult(in ServiceMessage.CreateRegionResult createRegionResponse)
        {
            RegionHandle region = _playerManager.WorldManager.GetRegion(createRegionResponse.RegionId);
            if (!Verify.IsNotNull(region, $"Region 0x{createRegionResponse.RegionId:X} not found"))
                return;

            region.OnInstanceCreateResponse(createRegionResponse.Success);
        }

        private void OnSetRegionPlayerAccess(in ServiceMessage.SetRegionPlayerAccess setRegionPlayerAccess)
        {
            RegionHandle region = _playerManager.WorldManager.GetRegion(setRegionPlayerAccess.RegionId);
            region?.SetPlayerAccess(setRegionPlayerAccess.PlayerAccess);
        }

        private void OnRequestRegionShutdown(in ServiceMessage.RequestRegionShutdown requestRegionShutdown)
        {
            RegionHandle region = _playerManager.WorldManager.GetRegion(requestRegionShutdown.RegionId);
            if (!Verify.IsNotNull(region, $"Region 0x{requestRegionShutdown.RegionId:X} not found"))
                return;

            region.RequestShutdown();
        }

        private void OnChangeRegionRequest(in ServiceMessage.ChangeRegionRequest changeRegionRequest)
        {
            ulong requestingGameId = changeRegionRequest.Header.RequestingGameId;
            ulong playerDbId = changeRegionRequest.Header.RequestingPlayerGuid;
            TeleportContextEnum context = changeRegionRequest.Header.Type;

            PlayerHandle player = _playerManager.ClientManager.GetPlayer(playerDbId);
            if (!Verify.IsNotNull(player, $"No player handle for dbid 0x{playerDbId:X}"))
                return;

            if (changeRegionRequest.DestTarget != null)
            {
                player.BeginRegionTransferToTarget(requestingGameId, context, changeRegionRequest.DestTarget, changeRegionRequest.CreateRegionParams);
            }
            else if (changeRegionRequest.DestLocation != null)
            {
                player.BeginRegionTransferToLocation(requestingGameId, context, changeRegionRequest.DestLocation);
            }
            else if (changeRegionRequest.DestPlayerDbId != 0)
            {
                player.BeginRegionTransferToPlayer(requestingGameId, changeRegionRequest.DestPlayerDbId);
            }
            else
            {
                Verify.IsTrue(false, $"ChangeRegionRequest for player [{this}] does not include transfer params");
                player.CancelRegionTransfer(requestingGameId, RegionTransferFailure.eRTF_GenericError);
            }
        }

        private void OnRegionTransferFinished(in ServiceMessage.RegionTransferFinished regionTransferFinished)
        {
            ulong playerDbId = regionTransferFinished.PlayerDbId;
            ulong transferId = regionTransferFinished.TransferId;

            PlayerHandle player = _playerManager.ClientManager.GetPlayer(playerDbId);
            if (!Verify.IsNotNull(player, $"No handle found for playerDbId 0x{playerDbId:X}"))
                return;

            player.FinishRegionTransfer(transferId);
        }

        private void OnClearPrivateStoryRegions(in ServiceMessage.ClearPrivateStoryRegions clearPrivateStoryRegions)
        {
            ulong playerDbId = clearPrivateStoryRegions.PlayerDbId;

            PlayerHandle player = _playerManager.ClientManager.GetPlayer(playerDbId);
            if (!Verify.IsNotNull(player, $"No handle found for playerDbId 0x{playerDbId:X}"))
                return;

            player.WorldView.ClearPrivateStoryRegions();
        }

        private void OnSetDifficultyTierPreference(in ServiceMessage.SetDifficultyTierPreference setDifficultyTierPreference)
        {
            ulong playerDbId = setDifficultyTierPreference.PlayerDbId;
            PrototypeId difficultyTierProtoRef = (PrototypeId)setDifficultyTierPreference.DifficultyTierProtoId;

            PlayerHandle player = _playerManager.ClientManager.GetPlayer(playerDbId);
            if (!Verify.IsNotNull(player, $"No handle found for playerDbId 0x{playerDbId:X}"))
                return;

            player.SetDifficultyTierPreference(difficultyTierProtoRef);
        }

        private void OnPlayerDataUpdated(in ServiceMessage.PlayerDataUpdated playerDataUpdated)
        {
            ulong playerDbId = playerDataUpdated.PlayerDbId;

            PlayerHandle player = _playerManager.ClientManager.GetPlayer(playerDbId);
            if (!Verify.IsNotNull(player, $"No handle found for playerDbId 0x{playerDbId:X}"))
                return;

            player.SavePlayerData();
        }

        private void OnPlayerLookupByNameRequest(in ServiceMessage.PlayerLookupByNameRequest playerLookupByNameRequest)
        {
            ulong gameId = playerLookupByNameRequest.GameId;
            ulong playerDbId = playerLookupByNameRequest.PlayerDbId;
            ulong remoteJobId = playerLookupByNameRequest.RemoteJobId;
            string requestPlayerName = playerLookupByNameRequest.RequestPlayerName;

            ulong resultPlayerDbId;
            string resultPlayerName;

            // Rate limit this because it's based on client input, and we may be querying the database. It's okay for this query to fail.
            if (_playerLookupByNameRateLimiter.AddTime(playerDbId))
            {
                PlayerNameCache.Instance.TryGetPlayerDbId(requestPlayerName, out resultPlayerDbId, out resultPlayerName);
            }
            else
            {
                Logger.Warn($"OnPlayerLookupByNameRequest(): Rate limit exceeded for player 0x{playerDbId:X}");
                resultPlayerDbId = 0;
                resultPlayerName = string.Empty;
            }

            ServiceMessage.PlayerLookupByNameResult response = new(gameId, playerDbId, remoteJobId, resultPlayerDbId, resultPlayerName);
            ServerManager.Instance.SendMessageToService(GameServiceType.GameInstance, response);
        }

        private void OnPlayerNameChanged(in ServiceMessage.PlayerNameChanged playerNameChanged)
        {
            ulong playerDbId = playerNameChanged.PlayerDbId;
            string oldPlayerName = playerNameChanged.OldPlayerName;
            string newPlayerName = playerNameChanged.NewPlayerName;

            _playerManager.ClientManager.OnPlayerNameChanged(playerDbId, oldPlayerName, newPlayerName);
            PlayerNameCache.Instance.OnPlayerNameChanged(playerDbId);
            _playerManager.CommunityRegistry.OnPlayerNameChanged(playerDbId, newPlayerName);
        }

        private void OnCommunityStatusUpdate(in ServiceMessage.CommunityStatusUpdate communityStatusUpdate)
        {
            CommunityMemberBroadcast broadcast = communityStatusUpdate.Broadcast;
            if (!Verify.IsNotNull(broadcast)) return;
            
            _playerManager.CommunityRegistry.ReceiveMemberBroadcast(broadcast);
        }

        private void OnCommunityStatusRequest(in ServiceMessage.CommunityStatusRequest communityStatusRequest)
        {
            ulong gameId = communityStatusRequest.GameId;
            ulong playerDbId = communityStatusRequest.PlayerDbId;
            List<ulong> members = communityStatusRequest.Members;

            _playerManager.CommunityRegistry.RequestMemberBroadcast(gameId, playerDbId, members);
        }

#if GAME_VERSION_1_52 || GAME_VERSION_1_53
        // V48_FIXME
        private void OnPartyOperationRequest(in ServiceMessage.PartyOperationRequest partyOperationRequest)
        {
            PartyOperationPayload request = partyOperationRequest.Request;

            using var playersToNotifyHandle = HashSetPool<PlayerHandle>.Get(out HashSet<PlayerHandle> playersToNotify);

            GroupingOperationResult result = _playerManager.PartyManager.DoPartyOperation(ref request, playersToNotify);

            if (!Verify.IsTrue(playersToNotify.Count != 0)) return;

            foreach (PlayerHandle player in playersToNotify)
            {
                if (player.CurrentGame == null)
                    continue;

                ulong gameId = player.CurrentGame.Id;
                ulong playerDbId = player.PlayerDbId;

                ServiceMessage.PartyOperationRequestServerResult message = new(gameId, playerDbId, request, result);
                ServerManager.Instance.SendMessageToService(GameServiceType.GameInstance, message);
            }
        }

        private void OnPartyBoostUpdate(in ServiceMessage.PartyBoostUpdate partyBoostUpdate)
        {
            ulong playerDbId = partyBoostUpdate.PlayerDbId;
            List<ulong> boosts = partyBoostUpdate.Boosts;

            PlayerHandle player = _playerManager.ClientManager.GetPlayer(playerDbId);
            if (!Verify.IsNotNull(player, $"No handle found for playerDbId 0x{playerDbId:X}"))
                return;

            player.SetPartyBoosts(boosts);
            player.CurrentParty?.UpdateMember(player);
        }
#endif

        private void OnGuildMessageFromGame(in ServiceMessage.GuildMessageToPlayerManager guildMessageFromGame)
        {
            GuildMessageSetToPlayerManager messages = guildMessageFromGame.Messages;
            if (!Verify.IsNotNull(messages)) return;

            _playerManager.GuildManager.OnGuildMessage(messages);
        }

        private void OnMatchRegionRequestQueueCommand(in ServiceMessage.MatchRegionRequestQueueCommand matchRegionRequestQueueCommand)
        {
            ulong playerDbId = matchRegionRequestQueueCommand.PlayerDbId;
            PrototypeId regionRef = (PrototypeId)matchRegionRequestQueueCommand.RegionProtoId;
            PrototypeId difficultyTierRef = (PrototypeId)matchRegionRequestQueueCommand.DifficultyTierProtoId;
            PrototypeId metaStateRef = (PrototypeId)matchRegionRequestQueueCommand.MetaStateProtoId;
            RegionRequestQueueCommandVar command = matchRegionRequestQueueCommand.Command;
            ulong regionRequestGroupId = matchRegionRequestQueueCommand.RegionRequestGroupId;
            ulong targetPlayerDbId = matchRegionRequestQueueCommand.TargetPlayerDbId;
            int teamSizeOverride = matchRegionRequestQueueCommand.TeamSizeOverride;

            PlayerHandle player = _playerManager.ClientManager.GetPlayer(playerDbId);
            player?.ReceiveRegionRequestQueueCommand(regionRef, difficultyTierRef, metaStateRef, command, regionRequestGroupId, targetPlayerDbId, teamSizeOverride);
        }

        private void OnAuthRequest(in ServiceMessage.AuthRequest authRequest)
        {
            AuthStatusCode statusCode = _playerManager.SessionManager.TryCreateSession(authRequest.LoginDataPB, out AuthTicket authTicket);

            ServiceMessage.AuthResponse response = new(authRequest.RequestId, (int)statusCode, authTicket);
            ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
        }

        private void OnSessionVerificationRequest(in ServiceMessage.SessionVerificationRequest sessionVerificationRequest)
        {
            IFrontendClient client = sessionVerificationRequest.Client;
            ClientCredentials clientCredentials = sessionVerificationRequest.ClientCredentials;

            if (_playerManager.SessionManager.VerifyClientCredentials(client, clientCredentials) == false)
            {
                Logger.Warn($"OnClientCredentials(): Failed to verify client credentials, disconnecting client [{client}]");
                client.Disconnect();
                return;
            }

            _playerManager.LoginQueueManager.EnqueueNewClient(client);
        }

        private void OnMTXStoreESBalanceRequest(in ServiceMessage.MTXStoreESBalanceRequest mtxStoreESBalanceRequest)
        {
            ulong requestId = mtxStoreESBalanceRequest.RequestId;
            string email = mtxStoreESBalanceRequest.Email;
            string token = mtxStoreESBalanceRequest.Token;

            PlayerHandle player = null;

            VerifyPlatformTicketResult vptResult = _playerManager.SessionManager.VerifyPlatformTicket(email, token, out ulong playerDbId);
            if (Verify.IsTrue(vptResult == VerifyPlatformTicketResult.Success, $"Failed to verify platform ticket when processing ES balance request! result={vptResult}"))
                player = _playerManager.ClientManager.GetPlayer(playerDbId);

            if (player == null || player.State != PlayerHandleState.InGame)
            {
                ServiceMessage.MTXStoreESBalanceResponse response = new(requestId, (int)HttpStatusCode.Forbidden);
                ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
                return;
            }

            Logger.Trace($"Authenticated ES balance request from player [{player}]");

            ulong gameId = player.CurrentGame.Id;

            // Route the request to game instance to get up to date balance and conversion ratio
            ServiceMessage.MTXStoreESBalanceGameRequest gameRequest = new(requestId, gameId, playerDbId);
            ServerManager.Instance.SendMessageToService(GameServiceType.GameInstance, gameRequest);
        }

        private void OnMTXStoreESBalanceGameResponse(in ServiceMessage.MTXStoreESBalanceGameResponse mtxStoreESBalanceGameResponse)
        {
            ulong requestId = mtxStoreESBalanceGameResponse.RequestId;
            int currentBalance = mtxStoreESBalanceGameResponse.CurrentBalance;
            float conversionRate = mtxStoreESBalanceGameResponse.ConversionRatio;
            int conversionStep = mtxStoreESBalanceGameResponse.ConversionStep;
            bool freeGEnabled = mtxStoreESBalanceGameResponse.FreeGEnabled;
            ServiceMessage.MTXStoreCurrencyBalance[] freeCurrencies = mtxStoreESBalanceGameResponse.FreeCurrencies;
            bool freeItemsEnabled = mtxStoreESBalanceGameResponse.FreeItemsEnabled;
            int freeItemsMaxCount = mtxStoreESBalanceGameResponse.FreeItemsMaxCount;

            // We should have already handled authentication before routing the request to the game instance, so just route the result back.
            ServiceMessage.MTXStoreESBalanceResponse response = new(requestId, (int)HttpStatusCode.OK, currentBalance, conversionRate, conversionStep, freeGEnabled, freeCurrencies,
                freeItemsEnabled, freeItemsMaxCount);
            ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
        }

        private void OnMTXStoreESConvertRequest(in ServiceMessage.MTXStoreESConvertRequest mtxStoreESConvertRequest)
        {
            ulong requestId = mtxStoreESConvertRequest.RequestId;
            string email = mtxStoreESConvertRequest.Email;
            string token = mtxStoreESConvertRequest.Token;
            int amount = mtxStoreESConvertRequest.Amount;

            PlayerHandle player = null;

            VerifyPlatformTicketResult vptResult = _playerManager.SessionManager.VerifyPlatformTicket(email, token, out ulong playerDbId);
            if (Verify.IsTrue(vptResult == VerifyPlatformTicketResult.Success, $"Failed to verify platform ticket when processing ES convert request! result={vptResult}"))
                player = _playerManager.ClientManager.GetPlayer(playerDbId);

            if (player == null || player.State != PlayerHandleState.InGame)
            {
                ServiceMessage.MTXStoreESConvertResponse response = new(requestId, (int)HttpStatusCode.Forbidden);
                ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
                return;
            }

            Logger.Trace($"Authenticated ES conversion request from player [{player}]");

            ulong gameId = player.CurrentGame.Id;

            // Route the conversion request to the game instance the player is currently in to do the conversion.
            ServiceMessage.MTXStoreESConvertGameRequest gameRequest = new(requestId, gameId, playerDbId, amount);
            ServerManager.Instance.SendMessageToService(GameServiceType.GameInstance, gameRequest);
        }

        private void OnMTXStoreESConvertGameResponse(in ServiceMessage.MTXStoreESConvertGameResponse mtxStoreESConvertGameResponse)
        {
            ulong requestId = mtxStoreESConvertGameResponse.RequestId;
            bool result = mtxStoreESConvertGameResponse.Result;

            // We should have already handled authentication before routing the request to the game instance, so just route the result back.
            ServiceMessage.MTXStoreESConvertResponse response = new(requestId, result ? (int)HttpStatusCode.OK : (int)HttpStatusCode.InternalServerError);
            ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
        }

        private void OnMTXStoreAddFreeGRequest(in ServiceMessage.MTXStoreAddFreeGRequest mtxStoreAddFreeGRequest)
        {
            ulong requestId = mtxStoreAddFreeGRequest.RequestId;
            string email = mtxStoreAddFreeGRequest.Email;
            string token = mtxStoreAddFreeGRequest.Token;
            int amount = mtxStoreAddFreeGRequest.Amount;

            PlayerHandle player = null;

            VerifyPlatformTicketResult vptResult = _playerManager.SessionManager.VerifyPlatformTicket(email, token, out ulong playerDbId);
            if (Verify.IsTrue(vptResult == VerifyPlatformTicketResult.Success, $"Failed to verify platform ticket when processing free G request! result={vptResult}"))
                player = _playerManager.ClientManager.GetPlayer(playerDbId);

            if (player == null || player.State != PlayerHandleState.InGame)
            {
                ServiceMessage.MTXStoreAddFreeGResponse response = new(requestId, (int)HttpStatusCode.Forbidden);
                ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
                return;
            }

            Logger.Trace($"Authenticated free G request from player [{player}]");

            ulong gameId = player.CurrentGame.Id;

            // The game instance checks whether free G is enabled, since that's where MTXStoreConfig lives.
            ServiceMessage.MTXStoreAddFreeGGameRequest gameRequest = new(requestId, gameId, playerDbId, amount);
            ServerManager.Instance.SendMessageToService(GameServiceType.GameInstance, gameRequest);
        }

        private void OnMTXStoreAddFreeGGameResponse(in ServiceMessage.MTXStoreAddFreeGGameResponse mtxStoreAddFreeGGameResponse)
        {
            ulong requestId = mtxStoreAddFreeGGameResponse.RequestId;
            bool result = mtxStoreAddFreeGGameResponse.Result;

            ServiceMessage.MTXStoreAddFreeGResponse response = new(requestId, result ? (int)HttpStatusCode.OK : (int)HttpStatusCode.Forbidden);
            ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
        }

        private void OnMTXStoreAddFreeCurrencyRequest(in ServiceMessage.MTXStoreAddFreeCurrencyRequest mtxStoreAddFreeCurrencyRequest)
        {
            ulong requestId = mtxStoreAddFreeCurrencyRequest.RequestId;
            string email = mtxStoreAddFreeCurrencyRequest.Email;
            string token = mtxStoreAddFreeCurrencyRequest.Token;
            ulong currencyProtoRef = mtxStoreAddFreeCurrencyRequest.CurrencyProtoRef;
            int amount = mtxStoreAddFreeCurrencyRequest.Amount;

            PlayerHandle player = null;

            VerifyPlatformTicketResult vptResult = _playerManager.SessionManager.VerifyPlatformTicket(email, token, out ulong playerDbId);
            if (Verify.IsTrue(vptResult == VerifyPlatformTicketResult.Success, $"Failed to verify platform ticket when processing free currency request! result={vptResult}"))
                player = _playerManager.ClientManager.GetPlayer(playerDbId);

            if (player == null || player.State != PlayerHandleState.InGame)
            {
                ServiceMessage.MTXStoreAddFreeCurrencyResponse response = new(requestId, (int)HttpStatusCode.Forbidden);
                ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
                return;
            }

            Logger.Trace($"Authenticated free currency request from player [{player}]");

            ulong gameId = player.CurrentGame.Id;

            // The game instance checks whether free currency is enabled and the currency is valid, since that's where the config and game data live.
            ServiceMessage.MTXStoreAddFreeCurrencyGameRequest gameRequest = new(requestId, gameId, playerDbId, currencyProtoRef, amount);
            ServerManager.Instance.SendMessageToService(GameServiceType.GameInstance, gameRequest);
        }

        private void OnMTXStoreAddFreeCurrencyGameResponse(in ServiceMessage.MTXStoreAddFreeCurrencyGameResponse mtxStoreAddFreeCurrencyGameResponse)
        {
            ulong requestId = mtxStoreAddFreeCurrencyGameResponse.RequestId;
            bool result = mtxStoreAddFreeCurrencyGameResponse.Result;
            int balance = mtxStoreAddFreeCurrencyGameResponse.Balance;

            ServiceMessage.MTXStoreAddFreeCurrencyResponse response = new(requestId, result ? (int)HttpStatusCode.OK : (int)HttpStatusCode.Forbidden, balance);
            ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
        }

        private void OnMTXStoreItemSearchRequest(in ServiceMessage.MTXStoreItemSearchRequest mtxStoreItemSearchRequest)
        {
            ulong requestId = mtxStoreItemSearchRequest.RequestId;

            PlayerHandle player = GetInGamePlayerForAddGPanel(mtxStoreItemSearchRequest.Email, mtxStoreItemSearchRequest.Token, "item search");
            if (player == null)
            {
                ServiceMessage.MTXStoreItemSearchResponse response = new(requestId, (int)HttpStatusCode.Forbidden);
                ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
                return;
            }

            // The game instance checks whether free items are enabled, since that's where the config and game data live.
            ServiceMessage.MTXStoreItemSearchGameRequest gameRequest = new(requestId, player.CurrentGame.Id, player.PlayerDbId, mtxStoreItemSearchRequest.Query);
            ServerManager.Instance.SendMessageToService(GameServiceType.GameInstance, gameRequest);
        }

        private void OnMTXStoreItemSearchGameResponse(in ServiceMessage.MTXStoreItemSearchGameResponse mtxStoreItemSearchGameResponse)
        {
            ulong requestId = mtxStoreItemSearchGameResponse.RequestId;
            int statusCode = mtxStoreItemSearchGameResponse.Result ? (int)HttpStatusCode.OK : (int)HttpStatusCode.Forbidden;

            ServiceMessage.MTXStoreItemSearchResponse response = new(requestId, statusCode, mtxStoreItemSearchGameResponse.Items, mtxStoreItemSearchGameResponse.Truncated);
            ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
        }

        private void OnMTXStoreAddFreeItemRequest(in ServiceMessage.MTXStoreAddFreeItemRequest mtxStoreAddFreeItemRequest)
        {
            ulong requestId = mtxStoreAddFreeItemRequest.RequestId;

            PlayerHandle player = GetInGamePlayerForAddGPanel(mtxStoreAddFreeItemRequest.Email, mtxStoreAddFreeItemRequest.Token, "free item");
            if (player == null)
            {
                ServiceMessage.MTXStoreAddFreeItemResponse response = new(requestId, (int)HttpStatusCode.Forbidden);
                ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
                return;
            }

            // The game instance checks whether free items are enabled and the item is valid.
            ServiceMessage.MTXStoreAddFreeItemGameRequest gameRequest = new(requestId, player.CurrentGame.Id, player.PlayerDbId,
                mtxStoreAddFreeItemRequest.ItemProtoRef, mtxStoreAddFreeItemRequest.Count);
            ServerManager.Instance.SendMessageToService(GameServiceType.GameInstance, gameRequest);
        }

        private void OnMTXStoreAddFreeItemGameResponse(in ServiceMessage.MTXStoreAddFreeItemGameResponse mtxStoreAddFreeItemGameResponse)
        {
            ulong requestId = mtxStoreAddFreeItemGameResponse.RequestId;
            int statusCode = mtxStoreAddFreeItemGameResponse.Result ? (int)HttpStatusCode.OK : (int)HttpStatusCode.Forbidden;

            ServiceMessage.MTXStoreAddFreeItemResponse response = new(requestId, statusCode, mtxStoreAddFreeItemGameResponse.Given);
            ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
        }

        /// <summary>
        /// Authenticates a request from the add G panel. Returns the player if they are in game, or <see langword="null"/>.
        /// </summary>
        private PlayerHandle GetInGamePlayerForAddGPanel(string email, string token, string requestName)
        {
            PlayerHandle player = null;

            VerifyPlatformTicketResult vptResult = _playerManager.SessionManager.VerifyPlatformTicket(email, token, out ulong playerDbId);
            if (Verify.IsTrue(vptResult == VerifyPlatformTicketResult.Success, $"Failed to verify platform ticket when processing {requestName} request! result={vptResult}"))
                player = _playerManager.ClientManager.GetPlayer(playerDbId);

            if (player == null || player.State != PlayerHandleState.InGame)
                return null;

            Logger.Trace($"Authenticated {requestName} request from player [{player}]");
            return player;
        }

        private void OnAccountOperationRequest(in ServiceMessage.AccountOperationRequest accountOperationRequest)
        {
            ulong requestId = accountOperationRequest.RequestId;
            AccountOperation operation = accountOperationRequest.Operation;
            string email = accountOperationRequest.Email;
            string playerName = accountOperationRequest.PlayerName;
            string password = accountOperationRequest.Password;
            AccountUserLevel userLevel = (AccountUserLevel)accountOperationRequest.UserLevel;
            AccountFlags flags = (AccountFlags)accountOperationRequest.Flags;

            int resultCode = (int)AccountOperationResult.GenericFailure;

            switch (operation)
            {
                case AccountOperation.Create:
                    if (!Verify.IsTrue(string.IsNullOrWhiteSpace(email) == false)) break;
                    if (!Verify.IsTrue(string.IsNullOrWhiteSpace(playerName) == false)) break;
                    if (!Verify.IsTrue(string.IsNullOrWhiteSpace(password) == false)) break;

                    resultCode = (int)AccountManager.CreateAccount(email, playerName, password);
                    break;

                case AccountOperation.SetPlayerName:
                    if (!Verify.IsTrue(string.IsNullOrWhiteSpace(email) == false)) break;
                    if (!Verify.IsTrue(string.IsNullOrWhiteSpace(playerName) == false)) break;

                    resultCode = (int)AccountManager.ChangeAccountPlayerName(email, playerName);
                    break;

                case AccountOperation.SetPassword:
                    if (!Verify.IsTrue(string.IsNullOrWhiteSpace(email) == false)) break;
                    if (!Verify.IsTrue(string.IsNullOrWhiteSpace(password) == false)) break;

                    resultCode = (int)AccountManager.ChangeAccountPassword(email, password);
                    break;

                case AccountOperation.SetUserLevel:
                    if (!Verify.IsTrue(string.IsNullOrWhiteSpace(email) == false)) break;
                    if (!Verify.IsTrue(userLevel >= AccountUserLevel.User && userLevel <= AccountUserLevel.Admin)) break;

                    resultCode = (int)AccountManager.SetAccountUserLevel(email, userLevel);
                    break;

                case AccountOperation.SetFlag:
                    if (!Verify.IsTrue(string.IsNullOrWhiteSpace(email) == false)) break;
                    if (!Verify.IsTrue(flags != 0)) break;

                    resultCode = (int)AccountManager.SetFlag(email, flags);
                    break;

                case AccountOperation.ClearFlag:
                    if (!Verify.IsTrue(string.IsNullOrWhiteSpace(email) == false)) break;
                    if (!Verify.IsTrue(flags != 0)) break;

                    resultCode = (int)AccountManager.ClearFlag(email, flags);
                    break;

                default:
                    Verify.IsTrue(false, $"Unhandled account operation {operation}");
                    break;
            }

            ServiceMessage.AccountOperationResponse response = new(requestId, resultCode);
            ServerManager.Instance.SendMessageToService(GameServiceType.WebFrontend, response);
        }

        private void OnSetWhitelistEnabled(in ServiceMessage.SetWhitelistEnabled setWhitelistEnabled)
        {
            _playerManager.SessionManager.SetWhitelistEnabled(setWhitelistEnabled.Enable);
        }

        #endregion
    }
}
