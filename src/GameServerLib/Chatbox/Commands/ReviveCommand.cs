﻿using LeagueSandbox.GameServer.Players;

namespace LeagueSandbox.GameServer.Chatbox.Commands
{
    public class ReviveCommand : ChatCommandBase
    {
        private readonly PlayerManager _playerManager;

        public override string Command => "revive";
        public override string Syntax => $"{Command}";

        public ReviveCommand(ChatCommandManager chatCommandManager, Game game)
            : base(chatCommandManager, game)
        {
            _playerManager = game.PlayerManager;
        }

        public override void Execute(int userId, bool hasReceivedArguments, string arguments = "")
        {
            var champ = _playerManager.GetPeerInfo(userId).Champion;
            if (!champ.IsDead && string.IsNullOrEmpty(arguments))
            {
                ChatCommandManager.SendDebugMsgFormatted(DebugMsgType.INFO, "Your champion is already alive.", userId);
                return;
            }

            ChatCommandManager.SendDebugMsgFormatted(DebugMsgType.INFO, "Your champion has revived!", userId);
            champ.Respawn();
        }
    }
}
