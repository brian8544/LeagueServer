using GameServerCore.Enums;
using GameServerCore.NetInfo;
using LeagueSandbox.GameServer.Content;
using LeagueSandbox.GameServer.GameObjects.AttackableUnits.AI;
using LeagueSandbox.GameServer.Inventory;
using LeagueSandbox.GameServer.Players;
using System;
using System.Numerics;

namespace LeagueSandbox.GameServer.Chatbox.Commands
{
    public class SpawnbotCommand : ChatCommandBase
    {
        private readonly PlayerManager _playerManager;

        Game _game;
        public override string Command => "spawnai";
        public override string Syntax => $"{Command} champblue [champion], champpurple [champion]";

        public SpawnbotCommand(ChatCommandManager chatCommandManager, Game game)
            : base(chatCommandManager, game)
        {
            _game = game;
            _playerManager = game.PlayerManager;
        }

        public override void Execute(int userId, bool hasReceivedArguments, string arguments = "")
        {
            var split = arguments.ToLower().Split(' ');

            if (split.Length < 2)
            {
                ChatCommandManager.SendDebugMsgFormatted(DebugMsgType.SYNTAXERROR);
                ShowSyntax();
            }
            else if (split[1].StartsWith("champ"))
            {
                string championModel = "";

                split[1] = split[1].Replace("champ", "team_").ToUpper();
                if (!Enum.TryParse(split[1], out TeamId team) || team == TeamId.TEAM_NEUTRAL)
                {
                    ChatCommandManager.SendDebugMsgFormatted(DebugMsgType.SYNTAXERROR);
                    ShowSyntax();
                    return;
                }

                if (split.Length > 2)
                {
                    championModel = arguments.Split(' ')[2];

                    try
                    {
                        Game.Config.ContentManager.GetCharData(championModel);
                    }
                    catch (ContentNotFoundException)
                    {
                        ChatCommandManager.SendDebugMsgFormatted(DebugMsgType.SYNTAXERROR, "Character Name: " + championModel + " invalid.");
                        ShowSyntax();
                        return;
                    }

                    SpawnChampForTeam(team, userId, championModel);

                    return;
                }

                SpawnChampForTeam(team, userId, "Katarina");
            }


            
        }


        public void SpawnChampForTeam(TeamId team, int userId, string model)
        {
            var championPos = _playerManager.GetPeerInfo(userId).Champion.Position;

            var runesTemp = new RuneCollection();
            var talents = new TalentInventory();
            var clientInfoTemp = new ClientInfo("", team, 0, 0, 0, $"{model} Bot", new string[] { "SummonerHeal", "SummonerFlash" }, -1);

            _playerManager.AddPlayer(clientInfoTemp);

            var c = new Champion(
                Game,
                model,
                runesTemp,
                talents,
                clientInfoTemp,
                AIScript: "Bot",
                team: team
            );

            clientInfoTemp.Champion = c;

            c.SetPosition(championPos, false);
            c.StopMovement();
            c.UpdateMoveOrder(OrderType.Stop);
            c.LevelUp();

            Game.ObjectManager.AddObject(c);

            ChatCommandManager.SendDebugMsgFormatted(DebugMsgType.INFO, $"Spawned Bot {c.Name} as {c.Model} with NetID: {c.NetId}.");
        }
    }
}