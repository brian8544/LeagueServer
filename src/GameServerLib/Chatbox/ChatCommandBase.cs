﻿using GameServerCore.Domain;

namespace LeagueSandbox.GameServer.Chatbox
{
    public abstract class ChatCommandBase : IChatCommand, IUpdate
    {
        protected readonly ChatCommandManager ChatCommandManager;

        public abstract string Command { get; }
        public abstract string Syntax { get; }
        public bool IsHidden { get; set; }
        public bool IsDisabled { get; set; }
        protected Game Game { get; }

        protected ChatCommandBase(ChatCommandManager chatCommandManager, Game game)
        {
            ChatCommandManager = chatCommandManager;
            Game = game;
        }

        public abstract void Execute(int userId, bool hasReceivedArguments, string arguments = "");

        public void ShowSyntax(int userId)
        {
            var msg = $"{ChatCommandManager.CommandStarterCharacter}{Syntax}";
            ChatCommandManager.SendDebugMsgFormatted(DebugMsgType.SYNTAX, msg, userId);
        }

        public virtual void Update(float diff)
        {
        }
    }
}
