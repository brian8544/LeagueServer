﻿using GameServerCore.Scripting.CSharp;
using LeagueSandbox.GameServer.GameObjects.AttackableUnits;
using LeagueSandbox.GameServer.GameObjects.SpellNS;
using LeagueSandbox.GameServer.GameObjects;
using LeagueSandbox.GameServer.Scripting.CSharp;

namespace Spells
{
    public class AncientGolemInitialAttack : ISpellScript { public SpellScriptMetadata ScriptMetadata { get; private set; } = new SpellScriptMetadata(); public void OnActivate(AttackableUnit unit, Buff buff, Spell ownerSpell) { } }
    public class AncientGolemBasicAttack : ISpellScript { public SpellScriptMetadata ScriptMetadata { get; private set; } = new SpellScriptMetadata(); public void OnActivate(AttackableUnit unit, Buff buff, Spell ownerSpell) { } }
    public class AncientGolemBasicAttack2 : ISpellScript { public SpellScriptMetadata ScriptMetadata { get; private set; } = new SpellScriptMetadata(); public void OnActivate(AttackableUnit unit, Buff buff, Spell ownerSpell) { } }
    public class AncientGolemBasicAttack3 : ISpellScript { public SpellScriptMetadata ScriptMetadata { get; private set; } = new SpellScriptMetadata(); public void OnActivate(AttackableUnit unit, Buff buff, Spell ownerSpell) { } }
}