using GameServerCore.Enums;
using GameServerCore.Scripting.CSharp;
using GameServerLib.GameObjects.AttackableUnits;
using LeagueSandbox.GameServer.API;
using LeagueSandbox.GameServer.GameObjects.AttackableUnits.AI;
using LeagueSandbox.GameServer.GameObjects.AttackableUnits;
using LeagueSandbox.GameServer.Scripting.CSharp;
using System.Collections.Generic;
using System.Numerics;
using System;
using System.Linq;
using static LeagueSandbox.GameServer.API.ApiFunctionManager;
using LeagueSandbox.GameServer.Chatbox;
using LeagueSandbox.GameServer.Players;
using LeagueSandbox.GameServer.GameObjects.StatsNS;
using LeagueSandbox.GameServer.GameObjects.AttackableUnits.Buildings.AnimatedBuildings;
using LeagueSandbox.GameServer.Logging;
using log4net;
using LeagueSandbox.GameServer;
using Timer = System.Timers.Timer;
using LeagueSandbox.GameServer.Inventory;
using Spells;
using LeagueSandbox.GameServer.GameObjects.SpellNS;
using System.Threading.Tasks;
using GameServerCore.NetInfo;
using LeagueSandbox.GameServer.GameObjects;
using GameMaths;
using System.Diagnostics;
using static GameServerLib.GameObjects.AttackableUnits.DamageData;





namespace AIScripts
{
    public class Bot : IAIScript
    {
        public AIScriptMetaData AIScriptMetaData { get; set; } = new AIScriptMetaData();
        public Champion ChampionInstance { get; private set; }
        public BotState currentBotState { get; private set; } // Public getter, private setter

        private float TimeSinceLastDamage { get; set; }
        private const float combatDuration = 10.0f;
        private float combatTimer = 0.0f;
        private const float recallDelay = 5.0f;
        private bool isInCombat = false;
        private bool isUnderTower = false;
        private bool isRoaming = false;
        private const float healthThreshold = 0.3f;  // Adjust to simulate more cautious or aggressive play
        private const float minionDamageThreshold = 20.0f;
        private readonly Game game;
        private CastInfo CastInfo { get; set; }
        private readonly Spell spell1;
        private BotState _currentState = BotState.Idle;
        private readonly Stopwatch _minionDamageStopwatch = new Stopwatch();
        private readonly Stopwatch _significantDamageStopwatch = new Stopwatch();
        private readonly List<DamageData> damages = new List<DamageData>();
        private bool _hasTakenSignificantDamage = false;
        private const float SignificantDamageThreshold = 200.0f; // Define the damage threshold as appropriate
        private const double DamageTimeWindow = 5.0; // Time window in seconds
        private bool IsTakingChampionDamage = false;
        private bool _isTakingMinionDamage = false;
        private const double MinionDamageDuration = 5.0;





        private float _stateCooldown = 5.0f;  // Time before the bot can switch states
        private const float decisionCooldown = 5.0f; // 2 seconds cooldown between decisions
        private static Random random = new Random();

        public enum BotState
        {
            Idle,
            Farming,
            AttackingEnemy,
            Retreating,
            Recalling,
            Roaming,
            AttackingTower,
            TakingMinionDamage, // New state for taking damage from minions
            TakingChampionDamage, // New state for taking damage from champions
            LaneHandlingEnemyChampion, // New state for attacking weaker enemy champions
            LaneHandlingNoEnemyChampion, // New state for handling lane with minions only and 0 enemies
            HealingInBase, //TODO
            Jungling, //TODO
            DefendingTower, //TODO
            SlowPushing, //TODO
            Retaliating, //UNUSED
        }

        public void OnActivate(ObjAIBase owner)
        {
            if (owner is Champion champion)
            {
                ChampionInstance = champion;
                ChampionInstance.IsBot = true;

                ApiEventManager.OnTakeDamage.AddListener(this, ChampionInstance, OnTakeDamage, false);
                ChampionInstance.LevelUp();

                isInCombat = false;
                isUnderTower = false;
                isRoaming = false;
            }
        }
        public void OnUpdate(float diff)
        {
            if (ChampionInstance != null && !ChampionInstance.IsDead)
            {
                // Log the initial state before updating timers
             

                TimeSinceLastDamage += diff;
                combatTimer += diff;
                _stateCooldown = Math.Max(0.0f, _stateCooldown - diff);

                // Log the state after updating timers
 

                if (_stateCooldown <= 0.0f)
                {


                    DecideNextState();

                    _stateCooldown = decisionCooldown;  // Reset cooldown after decision
                }

                HandleCurrentState();

                // Optionally log the current state to check if it's being handled correctly
            }
            else
            {
                if (ChampionInstance == null)
                {
                }
                else if (ChampionInstance.IsDead)
                {
                    ChampionInstance.UpdateMoveOrder(OrderType.Stop); // Issue a stop command to halt movement/attacks

                }
            }
        }

        // Conditionals to trigger enums
        private void DecideNextState()
        {
            // 1. Low health: retreat or recall
            if (ChampionInstance.Stats.CurrentHealth / ChampionInstance.Stats.HealthPoints.Total < healthThreshold)
            {
                StopAttack();
                _currentState = BotState.Retreating;
                return;
            }

            // 2. If there are enemy champions nearby and health is sufficient: attack
            List<Champion> nearbyEnemies = GetNearbyChampions();
            if (nearbyEnemies.Count > 0 && ShouldBeAggressive())
            {
                _currentState = BotState.AttackingEnemy;
                return;
            }

            // 3. If under a tower and no champions are attacking you: attack the tower
            if (IsUnderTower(ChampionInstance.Position))
            {
                _currentState = BotState.AttackingTower;
                return;
            }

            // 4. If taking minion damage and health drops below a certain threshold: retreat
            if (IsUnderThreat())
            {
                _currentState = BotState.TakingMinionDamage;
                return;
            }

            // 5. If taking champion damage and health is low: retreat or re-evaluate the situation
            if (IsTakingChampionDamage)
            {
                _currentState = BotState.TakingChampionDamage;
                return;
            }

            // 6. Lane Handling: When the bot is in lane and minions are nearby, decide what to do
            Champion enemyChampion = GetClosestEnemyChampion();
            if (enemyChampion != null && ShouldBeAggressive())
            {
                _currentState = BotState.LaneHandlingEnemyChampion;
                return;
            }

            // 7. No enemies and minions nearby: farm
            List<AttackableUnit> nearbyMinions = GetNearbyMinions();
            if (nearbyMinions.Count > 0 && !AreEnemyChampionsNearby())
            {
                _currentState = BotState.LaneHandlingNoEnemyChampion;
                return;
            }

            // 8. Roam if no other activities are available
            _currentState = BotState.Roaming;
        }

        // Enums to action
        private void HandleCurrentState()
        {
            Champion enemyChampion = GetClosestEnemyChampion(); // Or whichever logic applies to choose an enemy champion
            switch (_currentState)
            {
                case BotState.LaneHandlingNoEnemyChampion:
                    LaneHandlingWithoutEnemies();
                    break;
                case BotState.AttackingEnemy:
                    Attack(GetClosestEnemyChampion());
                    break;
                case BotState.Retreating:
                    RetreatOrRecall();
                    break;
                case BotState.Recalling:
                    Recall();
                    break;
                case BotState.Roaming:
                    Roam();
                    break;
                case BotState.AttackingTower:
                    AttackTowers();
                    break;
                case BotState.TakingMinionDamage:
                    RetreatFromMinionDamage(); // Back off when taking damage from minions
                    break;
                case BotState.TakingChampionDamage:
                    LaneHandling(enemyChampion); // Scan for the closest enemy champion and adjust playstyle
                    break;
                case BotState.LaneHandlingEnemyChampion:
                    LaneHandling(enemyChampion); // Scan for the closest enemy champion and adjust playstyle
                    break;
                case BotState.Idle:
                default:
                    // Stay idle if no other state is active
                    break;
            }
        }

        private BotState _currentBotState; // Backing field for the state
        private Recall recallSpell;
        private Spell spell;

        public BotState CurrentBotState
        {
            get => _currentBotState;
            private set
            {
                if (_currentBotState != value) // Only log if the state is actually changing
                {
                    _currentBotState = value;
                }
            }
        }

        public void SetBotState(BotState newState)
        {
            if (_currentBotState != newState)
            {
                _currentBotState = newState; // This triggers the setter, logging the state change
            }
        }

        private bool ShouldRecall()
        {
            return TimeSinceLastDamage > recallDelay && ChampionInstance.Stats.CurrentHealth / ChampionInstance.Stats.HealthPoints.Total < healthThreshold;
        }

        Particle recallParticle;
        bool isRecalling = false; // Tracks if recall is in progress
        bool isTeleporting = false;
        bool canRecall = true;    // Tracks if the champion can start recall
        float damageCooldown = 1.0f; // 1 second cooldown after taking damage
        private float postRecallCooldown = 10.0f;

        // Recall method with movement cancellation
        private async void Recall()
        {
            if (!canRecall)
            {
                return; // Exit early if recall is on cooldown due to damage
            }

            if (isRecalling || isTeleporting)
            {
                return; // Exit early if a recall is already happening or in post-recall cooldown
            }

            // Set recall to in-progress
            isRecalling = true;

            // Stop the bot's movement or actions before starting recall
            ChampionInstance.UpdateMoveOrder(OrderType.Stop); // Issue a stop command to halt movement/attacks

            // Simulate delay for recall (e.g., 8 seconds)
            float recallDelay = 8.0f;

            // Add recall particle effect, only if not already added
            if (recallParticle == null)
            {
                recallParticle = AddParticleTarget(ChampionInstance, ChampionInstance, "TeleportHome", ChampionInstance, 8.0f, flags: 0);
            }

            // Introduce the delay (convert seconds to milliseconds for Task.Delay)
            var recallTask = Task.Delay((int)(recallDelay * 1000));

            // Monitor for movement during the recall delay
            while (!recallTask.IsCompleted)
            {
                await Task.Delay(100); // Check every 100ms

                // If the bot moves, cancel the recall
                if (!isRecalling)
                {
                    return;
                }
            }

            // Check if the bot is still alive before proceeding
            if (ChampionInstance.IsDead)
            {

                // Stop the recall particle and clean up
                recallParticle.SetToRemove();
                recallParticle = null; // Reset the particle
                isRecalling = false; // Reset the recall state
                return; // Exit early, no recall should happen
            }

            // Now proceed with the recall after the delay
            Vector2 basePosition = GetBasePosition();

            // Stop the movement again just before teleport (hard stop)
            ChampionInstance.UpdateMoveOrder(OrderType.Stop);

            // Trigger the actual recall (teleport to base)
            ChampionInstance.Recall();

            // Optionally, add a particle for the arrival at the base
            AddParticleTarget(ChampionInstance, ChampionInstance, "TeleportArrive", ChampionInstance, flags: 0);

            // Clean up and reset recall state
            recallParticle = null; // Clear the recall particle reference
            isRecalling = false; // Allow for future recalls

            // Enter post-recall state to prevent immediate recalling
            isTeleporting = true;

            // Apply post-recall cooldown
            await Task.Delay((int)(postRecallCooldown * 1000));
            isTeleporting = false; // Cooldown is over, bot can recall again
        }

        private void OnMoveCommandIssued()
        {
            // If the bot is recalling, cancel the recall
            if (isRecalling)
            {
                CancelRecall();
            }
        }

        private void CancelRecall()
        {

            // Stop the recall particle and clean up
            if (recallParticle != null)
            {
                recallParticle.SetToRemove();
                recallParticle = null; // Reset the particle
            }

            // Reset recall state
            isRecalling = false;
        }

        private void RetreatOrRecall()
        {
            List<Champion> nearbyEnemies = GetNearbyChampions();
            List<AttackableUnit> nearbyMinions = GetNearbyMinions(); // Get nearby minions

            if (nearbyEnemies.Count == 0 && nearbyMinions.Count == 0)
            {
                // No nearby enemies or minions, safe to recall
                Recall();
            }
            else
            {
                // If there are enemies or minions nearby, retreat instead of recalling
                MoveTowardsBase();
            }
        }


        private bool ShouldRoam()
        {
            // If the bot's health is below the threshold, it can't roam.
            if (ChampionInstance.Stats.CurrentHealth < healthThreshold)
            {
                return false;
            }

            // Existing logic for roaming when there are no nearby minions and a 10% chance
            return isRoaming || (GetNearbyMinions().Count == 0 && random.Next(0, 100) < 10);
        }

        private void Roam()
        {
            isRoaming = true;
            Vector2 roamTarget = GetRoamTarget();
            MoveToPosition(roamTarget);

            // Cancel roaming if under attack
            if (isInCombat)
            {
                isRoaming = false;
            }
        }

        private Vector2 GetRoamTarget()
        {
            // Select a random target location in another lane or near an objective
            List<Vector2> potentialTargets = new List<Vector2>
            {
                new Vector2(12000, 2000), // Top lane
                new Vector2(3000, 12000), // Bot lane
                new Vector2(7500, 7500)   // Mid lane or jungle
            };
            return potentialTargets[new Random().Next(0, potentialTargets.Count)];
        }

        // Method to handle taking damage
        // The OnTakeDamage method should match the expected delegate signature
        private void OnTakeDamage(GameServerLib.GameObjects.AttackableUnits.DamageData damageData)
        {
            // Cancel the recall if it's in progress
            if (isRecalling)
            {

                // Stop the recall particle and clean up
                if (recallParticle != null)
                {
                    recallParticle.SetToRemove();
                    recallParticle = null;
                }

                // Reset recall state
                isRecalling = false;
            }

            // Set recall to cooldown (can't be cast for 1 second)
            canRecall = false;

            AttackableUnit source = damageData.Attacker; // This gives you the attacker unit
            float damageAmount = damageData.Damage; // The amount of damage inflicted


            // Wait for the cooldown (1 second)
            Task.Delay((int)(damageCooldown * 1000)).ContinueWith(_ => canRecall = true);

            // If the bot is taking damage from minions
            if (source is LaneMinion)
            {
                LogMinionDamage(damageAmount);
                if (_currentState == BotState.LaneHandlingNoEnemyChampion)
                {
                    // Unnecessary damage: retreat or switch to a safer state
                    IsTakingMinionDamage(true);
                    _currentState = BotState.TakingMinionDamage; // Switch to new state
                }
            }
            // If the bot is taking damage from champions
            else if (source is Champion enemyChampion)
            {

                // Compare the bot's attack damage with the enemy champion's attack damage
                float botAttackDamage = ChampionInstance.Stats.AttackDamage.Total;
                float enemyAttackDamage = enemyChampion.Stats.AttackDamage.Total;

                if (botAttackDamage > enemyAttackDamage)
                {
                    // Bot is stronger, switch to retaliate
                    _currentState = BotState.Retaliating; // Switch to new state
                }
                else
                {
                    // Bot is weaker, retreat
                    _currentState = BotState.Retreating; // Switch to retreat state
                }
            }
        }

        private void Attack(AttackableUnit attacker)
        {
            if (attacker != null && !attacker.IsDead)
            {
                // Check if the bot should be retreating instead of attacking
                if (currentBotState == BotState.Retreating || ChampionInstance.Stats.CurrentHealth < healthThreshold)
                {
                    StopAttack();
                    isInCombat = false;
                    return;
                }

                Vector2 attackerPosition = attacker.Position;
                ChampionInstance.UpdateMoveOrder(OrderType.AttackMove);
                ChampionInstance.SetWaypoints(new List<Vector2> { attackerPosition });
                ChampionInstance.SetTargetUnit(attacker);
            }
        }

        private void Retaliate()
        {
            // Get the closest enemy champion
            Champion enemyChampion = GetClosestEnemyChampion();

            // Check if the enemy is valid and in range
            if (enemyChampion == null || !IsInAttackRange(enemyChampion))
            {
                MoveTowardsBase(); // Or some other repositioning logic
                return;
            }

            // Evaluate health conditions
            float botHealthPercentage = ChampionInstance.Stats.CurrentHealth / ChampionInstance.Stats.HealthPoints.Total;
            float enemyHealthPercentage = enemyChampion.Stats.CurrentHealth / enemyChampion.Stats.HealthPoints.Total;

            if (botHealthPercentage > 0.5f && enemyHealthPercentage < 0.5f)
            {
                Attack(enemyChampion);
            }
            else
            {
                // Implement kiting logic or defensive strategy
                LaneHandling(enemyChampion); // Call the defensive play method
            }
        }

        private void LaneHandling(Champion enemyChampion)
        {
            // Calculate the distance to the enemy
            float distanceToEnemy = Vector2.Distance(ChampionInstance.Position, enemyChampion.Position);
            float safeDistance = ChampionInstance.Stats.Range.Total + 200; // Example safe distance beyond attack range

            // If the bot is within a dangerous distance, move away
            if (distanceToEnemy < safeDistance)
            {

                // Calculate the direction to move away from the enemy
                Vector2 directionAwayFromEnemy = Vector2.Normalize(ChampionInstance.Position - enemyChampion.Position);
                Vector2 safePosition = ChampionInstance.Position + directionAwayFromEnemy * 200; // Move away by 200 units

                MoveToPosition(safePosition); // Use MoveToPosition to handle the move operation.
            }
            else
            {
                // If not too close, consider attacking or repositioning to maintain range
                if (IsInAttackRange(enemyChampion))
                {

                    Attack(enemyChampion);
                }
                else
                {

                    Vector2 moveCloser = new Vector2(100, 100); // Move closer by 100 units in both x and y directions
                    Vector2 newPosition = ChampionInstance.Position + moveCloser;
                    MoveToPosition(newPosition);
                }
            }
        }

        private void RetreatFromMinionDamage()
        {
            List<AttackableUnit> nearbyMinions = GetNearbyMinions(); // Get nearby minions

            if (IsTakingMinionDamage(true) && nearbyMinions.Count > 0)
            {
                // If taking minion damage and minions are nearby, retreat towards the base

                MoveTowardsBase(); // Simple retreat logic
            }
        }


        private Vector2 CalculateSafeRetreatPosition(List<Minion> nearbyMinions)
        {
            // Find the average position of all nearby minions
            Vector2 minionCenter = new Vector2(0, 0);
            foreach (var minion in nearbyMinions)
            {
                minionCenter += minion.Position;
            }
            minionCenter /= nearbyMinions.Count;

            // Calculate the direction away from the minions
            Vector2 directionAwayFromMinions = Vector2.Normalize(ChampionInstance.Position - minionCenter);

            // Randomize the retreat movement to make it unpredictable
            float randomFactor = (float)new Random().NextDouble() * 50.0f; // Randomizes within 50 units
            directionAwayFromMinions = Vector2.Normalize(directionAwayFromMinions + new Vector2(randomFactor, randomFactor));

            // Calculate the retreat position, within a small range to still gain XP but away from danger
            Vector2 retreatPosition = ChampionInstance.Position + directionAwayFromMinions * 300; // 300 units away

            // Ensure the retreat position is within experience range (adjust this range if needed)
            float maxExperienceRange = 1200.0f; // Adjust this as necessary
            if (Vector2.Distance(ChampionInstance.Position, minionCenter) > maxExperienceRange)
            {
                // If too far, limit the retreat to stay within experience range
                retreatPosition = ChampionInstance.Position + directionAwayFromMinions * 100; // Move only 100 units back
            }

            return retreatPosition;
        }

        private bool IsUnderThreat()
        {
            // 1. Check for nearby enemy champions that could attack the bot
            List<Champion> nearbyEnemies = GetNearbyChampions();
            foreach (var enemy in nearbyEnemies)
            {
                // Check if enemy is within attack range or could reach the bot quickly
                float distanceToEnemy = Vector2.Distance(ChampionInstance.Position, enemy.Position);
                if (distanceToEnemy < enemy.Stats.Range.Total + 100) // Add buffer for safety
                {

                    return true;
                }
            }

            // 3. Check if the bot is taking significant minion damage
            if (IsTakingMinionDamage(true))
            {
                return true;
            }

            // 4. Optional: Check if the bot has taken significant damage recently
            if (HasTakenSignificantDamageRecently())
            {
                return true;
            }

            // If none of the threat conditions are met, return false
            return false;
        }
        private bool HasTakenSignificantDamageRecently()
        {
            // Check if the bot took significant damage and it was within the last few seconds
            if (_hasTakenSignificantDamage && _significantDamageStopwatch.Elapsed.TotalSeconds <= DamageTimeWindow)
            {
                return true;
            }

            // If the time window has passed, reset the flag
            if (_significantDamageStopwatch.Elapsed.TotalSeconds > DamageTimeWindow)
            {
                _hasTakenSignificantDamage = false;
            }

            return false;
        }

        private void LogChampionDamage(float damageAmount)
        {
            // Check if the damage exceeds the significant damage threshold
            if (damageAmount >= SignificantDamageThreshold)
            {
                _hasTakenSignificantDamage = true;
                _significantDamageStopwatch.Restart(); // Reset and start the timer
            }

            // You can log other non-significant damage separately if needed
        }

        private void LogMinionDamage(float damageAmount)
        {
            // Start tracking minion damage
            _isTakingMinionDamage = true;

            // Reset and restart the stopwatch
            _minionDamageStopwatch.Restart();

            // Create a new DamageData object and add it to the class-level list
            DamageData damageData = new DamageData
            {
                Damage = damageAmount,
            };

            damages.Add(damageData);

        }

        private bool IsTakingMinionDamage(bool isTakingDamage)
        {
            // Check if the timer has exceeded the duration
            if (_isTakingMinionDamage)
            {
                // If the timer is running and within the allowed duration, return true
                if (_minionDamageStopwatch.Elapsed.TotalSeconds <= MinionDamageDuration)
                {
                    return true;
                }
                else
                {
                    // If the timer has exceeded the allowed duration, reset the flag
                    _isTakingMinionDamage = false;
                }
            }

            return false; // Return false if not taking minion damage or if the timer expired
        }

        // Helper method to check recent damage from minions
        private bool HasTakenDamageFromMinionsRecently()
        {
            // Define the time window (e.g., 5 seconds)
            double timeWindowInSeconds = 5.0;

            // Check if the stopwatch has been running less than the defined time window
            if (_minionDamageStopwatch.Elapsed.TotalSeconds <= timeWindowInSeconds)
            {
                // Verify if any of the damage sources were from minions (optional)
                foreach (var damage in damages)
                {
                    if (damage.Attacker.Equals("Minion"))
                    {
                        return true; // Minion damage within the last 5 seconds
                    }
                }
            }

            // No recent minion damage within the time window
            return false;
        }

        private bool IsInAttackRange(AttackableUnit unit)
        {
            float attackRange = ChampionInstance.Stats.Range.Total; // Get your attack range
            float distanceToEnemy = Vector2.Distance(ChampionInstance.Position, unit.Position);
            return distanceToEnemy <= attackRange;
        }


        private void StopAttack()
        {
            {
                // Log that the bot is stopping the attack

                // Clear the current attack target and stop any queued actions
                ChampionInstance.SetTargetUnit(null);
                ChampionInstance.UpdateMoveOrder(OrderType.Stop); // Issue a stop command to halt movement/attacks
                isInCombat = false;

                // Optionally reset waypoints if the bot was moving towards a target
                ChampionInstance.SetWaypoints(new List<Vector2>());

                // Change the bot's state to idle or retreating based on context
                currentBotState = BotState.Idle; // Or BotState.Retreating, depending on your game's logic
            }
        }

        private void MoveToPosition(Vector2 targetPosition)
        {
            Vector2 botPosition = ChampionInstance.Position;
            List<Vector2> waypoints = new List<Vector2> { botPosition, targetPosition };
            ChampionInstance.MoveOrder = OrderType.MoveTo;
            ChampionInstance.SetWaypoints(waypoints);
        }

        private void MoveTowardsBase()
        {
            Vector2 basePosition = GetBasePosition();
            {
                // move towards the base position
                MoveToPosition(basePosition);
            }
        }

        private bool ShouldBeAggressive()
        {
            return ChampionInstance.Stats.CurrentHealth > healthThreshold
                && ChampionInstance.Stats.CurrentMana > 0.3f
                && GetNearbyChampions().Count > 0;
        }

        private List<Champion> GetNearbyChampions()
        {
            Vector2 botPosition = ChampionInstance.Position;
            List<AttackableUnit> units = GetUnitsInRange(botPosition, 2000.0f, true);
            return units.OfType<Champion>().Where(champion => champion.Team != ChampionInstance.Team && !champion.IsDead).ToList();
        }

        private bool AreEnemyChampionsNearby(float range = 2000.0f)
        {
            Vector2 botPosition = ChampionInstance.Position;

            // Get the nearby units within the specified range
            List<AttackableUnit> units = GetUnitsInRange(botPosition, range, true);

            // Check if there are any nearby champions from the enemy team that are not dead
            bool areEnemiesNearby = units.OfType<Champion>()
                                         .Any(champion => champion.Team != ChampionInstance.Team && !champion.IsDead);

            return areEnemiesNearby;
        }

        private Champion GetClosestEnemyChampion()
        {
            Vector2 botPosition = ChampionInstance.Position;
            List<AttackableUnit> units = GetUnitsInRange(botPosition, 2000.0f, true);
            var nearbyChampions = units.OfType<Champion>().Where(champion => champion.Team != ChampionInstance.Team && !champion.IsDead).ToList();

            // Ensure there are champions in the list
            if (nearbyChampions.Any())
            {
                // Find the closest champion
                return nearbyChampions
                        .OrderBy(champion => Vector2.Distance(botPosition, champion.Position))
                        .FirstOrDefault();
            }
            return null; // Return null if no valid champions are found
        }

        private List<AttackableUnit> GetNearbyMinions()
        {
            Vector2 botPosition = ChampionInstance.Position;
            List<AttackableUnit> units = GetUnitsInRange(botPosition, 2000.0f, true);
            return units.Where(unit => unit is LaneMinion).ToList();
        }

        private float _lastMovementTime = 0f; // Store the last time the bot moved
        private const float MovementCooldown = 0.2f; // 0.2 seconds cooldown between movements. TODO: maybe make this as a part of decision making cooldown

        private void FarmMinions(List<Minion> minions)
        {
            // Get the current game time (you should replace this with the actual time system used in your game)
            float currentTime = GetCurrentGameTime();

            foreach (var minion in minions)
            {
                if (IsInAttackRange(minion))
                {
                    Attack(minion); // Method to attack the minion
                    return; // If attacking a minion, exit early (prevents multiple actions)
                }
                else if (currentTime - _lastMovementTime > MovementCooldown)
                {
                    // Only move if the cooldown period has passed
                    _lastMovementTime = currentTime; // Update last movement time

                    // Move closer to the minion if not in range
                    Vector2 directionToMinion = Vector2.Normalize(minion.Position - ChampionInstance.Position);
                    Vector2 moveCloser = ChampionInstance.Position + directionToMinion * 100; // Move closer by 100 units

                    MoveToPosition(moveCloser);
                }
            }
        }

        // Placeholder method for getting the current game time, you should replace this with actual game time logic
        private float GetCurrentGameTime()
        {
            return (float)DateTime.Now.TimeOfDay.TotalSeconds; // Use game-specific time instead of DateTime
        }

        private void LaneHandlingWithoutEnemies()
        {
            // 1. Check if the bot has taken damage from minions recently
            if (IsTakingMinionDamage(true))
            {
                Vector2 retreatPosition = ChampionInstance.Position + new Vector2(-200, 0); // Move back by 200 units
                MoveToPosition(retreatPosition); // Move away from minions
                return;
            }

            // 2. Check for nearby minions to farm
            var nearbyMinions = GetNearbyMinions().OfType<Minion>().ToList();
            if (nearbyMinions.Count > 0)
            {
                FarmMinions(nearbyMinions); // Method to handle farming logic
                return;
            }

            // 3. If no minions to farm, reposition to a safe position
            Vector2 safePosition = GetSafePosition(); // Method to determine a safe position
            MoveToPosition(safePosition);
        }

        // Helper method to get a safe position
        private Vector2 GetSafePosition()
        {
            // Logic to determine a safe position, e.g., moving back to a previous location
            return ChampionInstance.Position + new Vector2(-200, 0); // Example: move back by 200 units
        }

        private void FollowAlliedMinions()
        {
            Vector2 botPosition = ChampionInstance.Position;
            float detectionRange = 2000.0f;
            List<AttackableUnit> units = GetUnitsInRange(botPosition, detectionRange, true);
            List<AttackableUnit> alliedMinions = units.Where(unit => unit is Minion && unit.Team == ChampionInstance.Team).ToList();

            if (alliedMinions.Count > 0)
            {
                Vector2 averageMinionPosition = Vector2.Zero;
                foreach (var minion in alliedMinions)
                {
                    averageMinionPosition += minion.Position;
                }
                averageMinionPosition /= alliedMinions.Count;
                MoveToPosition(averageMinionPosition);
            }
        }

        private void AttackTowers()
        {
            Vector2 botPosition = ChampionInstance.Position;
            if (IsUnderTower(botPosition))
            {
                AttackableUnit nearestTower = GetNearestTower(botPosition);
                List<AttackableUnit> minionsInRange = GetUnitsInRange(nearestTower.Position, 550.0f, true)
                    .Where(unit => unit is Minion).ToList();

                if (minionsInRange.Count > 0)
                {

                    Attack(nearestTower);
                    _currentState = BotState.AttackingTower;
                }
                else
                {
                    _currentState = BotState.TakingMinionDamage;
                }
            }
        }

        private bool IsUnderTower(Vector2 position)
        {
            float towerRange = 775.0f;
            isUnderTower = true;
            return GetUnitsInRange(position, towerRange, true).Any(unit => unit is LaneTurret);
        }

        private AttackableUnit GetNearestTower(Vector2 position)
        {
            List<AttackableUnit> LaneTurret = GetTowers();
            return LaneTurret.OrderBy(tower => Vector2.Distance(position, tower.Position)).FirstOrDefault();
        }

        private List<AttackableUnit> GetTowers()
        {
            // Get a list of all towers in the game
            return GetUnitsInRange(Vector2.Zero, float.MaxValue, true)
                .Where(unit => unit is LaneTurret && unit.Team != ChampionInstance.Team).ToList();

        }

        private Vector2 GetBasePosition()
        {
            // Get the team's base position, depending on the team
            if (ChampionInstance.Team == TeamId.TEAM_BLUE)
            {
                return new Vector2(500, 500); // Blue team base
            }
            else
            {
                return new Vector2(14200, 14200); // Red team base
            }
        }

        private List<AttackableUnit> GetUnitsInRange(Vector2 position, float range, bool includeDeadUnits)
        {
            // Retrieves all units within the specified range
            return ApiFunctionManager.GetUnitsInRange(position, range, includeDeadUnits)
                .Where(unit => unit.Team != ChampionInstance.Team).ToList();
        }
    }
}