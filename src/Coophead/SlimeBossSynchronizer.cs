using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Coophead.Transport;

namespace Coophead
{
    internal static class SlimeBossSynchronizer
    {
        private const byte SmallActor = 1;
        private const byte BigActor = 2;
        private const byte TombstoneActor = 4;
        private const float SnapshotTimeoutSeconds = 0.5f;
        private const float MinVelocitySampleSeconds = 0.008f;
        private const float VelocityStallSeconds = 0.25f;
        private const float MaxExtrapolationSeconds = 0.12f;
        private const float VelocityFilterWeight = 0.3f;
        private const float MaxInferredSpeed = 900f;
        private const float MaxExtrapolationDistance = 32f;
        private const float ActorEmergencySnapDistance = 160f;
        private const float ActorFollowSpeed = 30f;
        private const float PlayerImpactFollowSpeed = 30f;
        private const float PlayerImpactSnapDistance = 30f;

        private static readonly System.Reflection.FieldInfo PropertiesField =
            AccessTools.Field(typeof(SlimeLevel), "properties");
        private static readonly System.Reflection.FieldInfo SmallSlimeField =
            AccessTools.Field(typeof(SlimeLevel), "smallSlime");
        private static readonly System.Reflection.FieldInfo BigSlimeField =
            AccessTools.Field(typeof(SlimeLevel), "bigSlime");
        private static readonly System.Reflection.FieldInfo TombstoneField =
            AccessTools.Field(typeof(SlimeLevel), "tombStone");
        private static readonly System.Reflection.FieldInfo SlimeStateField =
            AccessTools.Field(typeof(SlimeLevelSlime),
                "<state>k__BackingField");
        private static readonly System.Reflection.FieldInfo TombstoneStateField =
            AccessTools.Field(typeof(SlimeLevelTombstone),
                "<state>k__BackingField");
        private static readonly System.Reflection.FieldInfo BossCurrentHealthField =
            AccessTools.Field(typeof(LevelProperties.Slime),
                "<CurrentHealth>k__BackingField");
        private static readonly System.Reflection.MethodInfo SetSuperMeterMethod =
            AccessTools.PropertySetter(typeof(PlayerStatsManager), "SuperMeter");
        private static readonly System.Reflection.MethodInfo SuperChangedMethod =
            AccessTools.Method(typeof(PlayerStatsManager), "OnSuperChanged",
                new[] { typeof(bool) });
        private static readonly System.Reflection.MethodInfo PlayerStatsDeathMethod =
            AccessTools.Method(typeof(PlayerStatsManager), "OnStatsDeath");
        private static readonly System.Reflection.MethodInfo SlimeBossDeathMethod =
            AccessTools.Method(typeof(SlimeLevelSlime), "OnBossDeath");
        private static readonly System.Reflection.MethodInfo TombstoneBossDeathMethod =
            AccessTools.Method(typeof(SlimeLevelTombstone), "OnBossDeath");
        private static readonly System.Reflection.FieldInfo PlayerMotorLastPositionField =
            AccessTools.Field(typeof(LevelPlayerMotor), "lastPosition");
        private static readonly System.Reflection.FieldInfo PlayerMotorLastPositionFixedField =
            AccessTools.Field(typeof(LevelPlayerMotor), "lastPositionFixed");
        private static readonly System.Reflection.FieldInfo PlayerMotorHitManagerField =
            AccessTools.Field(typeof(LevelPlayerMotor), "hitManager");
        private static readonly System.Reflection.FieldInfo PlayerHitDirectionField =
            PlayerMotorHitManagerField == null ? null :
                AccessTools.Field(PlayerMotorHitManagerField.FieldType,
                    "direction");
        private static readonly System.Reflection.MethodInfo PlayerHitResetMethod =
            PlayerMotorHitManagerField == null ? null :
                AccessTools.Method(PlayerMotorHitManagerField.FieldType, "Reset");
        private static readonly System.Reflection.FieldInfo PlayerMotorVelocityManagerField =
            AccessTools.Field(typeof(LevelPlayerMotor), "velocityManager");
        private static readonly System.Reflection.FieldInfo PlayerHitVelocityField =
            PlayerMotorVelocityManagerField == null ? null :
                AccessTools.Field(PlayerMotorVelocityManagerField.FieldType, "hit");
        private static readonly System.Reflection.FieldInfo PlayerIsRevivingField =
            AccessTools.Field(typeof(AbstractPlayerController), "_isReviving");
        private static readonly System.Reflection.FieldInfo DeathEffectPlayerIdField =
            AccessTools.Field(typeof(PlayerDeathEffect), "playerId");
        private static readonly System.Reflection.FieldInfo DeathEffectExitingField =
            AccessTools.Field(typeof(PlayerDeathEffect), "exiting");
        private static readonly System.Reflection.MethodInfo DeathEffectParryMethod =
            AccessTools.Method(typeof(PlayerDeathEffect), "OnParrySwitch");

        private static BossStateSnapshot latest;
        private static bool hasLatest;
        private static uint lastTick;
        private static bool hasLastTick;
        private static string latestScene = string.Empty;
        private static float latestReceivedRealtime;
        private static Vector2 inferredVelocity;
        private static byte lastLoggedPhase = byte.MaxValue;
        private static byte lastLoggedActors = byte.MaxValue;
        private static bool firstSnapshotLogged;
        private static bool defeatLogged;
        private static bool authorityArmed;
        private static byte playerHealthBaselineMask;
        private static byte authoritativeDeathAppliedMask;
        private static AuthoritativePlayerLifeState playerOneLifeState;
        private static AuthoritativePlayerLifeState playerTwoLifeState;
        private static bool applyingAuthoritativePlayerDamage;
        private static bool applyingAuthoritativeBossEvent;
        private static bool bossDefeatEffectsApplied;
        private static bool bossHealthBaselineApplied;
        private static byte lastAppliedActors;
        private static bool playerTwoImpactReconciliationActive;

        internal static bool ShouldSuppressClientSimulation
        {
            get
            {
                return RemoteInputLab.IsClientSession &&
                    RemoteInputLab.IsConnected &&
                    Level.Current is SlimeLevel && authorityArmed;
            }
        }

        internal static bool ApplyingAuthoritativeBossEvent =>
            applyingAuthoritativeBossEvent;

        internal static bool ShouldSuppressLocalPlayerDamage
        {
            get
            {
                return ShouldSuppressClientSimulation &&
                    !applyingAuthoritativePlayerDamage;
            }
        }

        internal static void Reset()
        {
            latest = default(BossStateSnapshot);
            hasLatest = false;
            lastTick = 0;
            hasLastTick = false;
            latestScene = string.Empty;
            latestReceivedRealtime = 0f;
            inferredVelocity = Vector2.zero;
            lastLoggedPhase = byte.MaxValue;
            lastLoggedActors = byte.MaxValue;
            firstSnapshotLogged = false;
            defeatLogged = false;
            authorityArmed = false;
            playerHealthBaselineMask = 0;
            authoritativeDeathAppliedMask = 0;
            playerOneLifeState = AuthoritativePlayerLifeState.Unknown;
            playerTwoLifeState = AuthoritativePlayerLifeState.Unknown;
            applyingAuthoritativePlayerDamage = false;
            applyingAuthoritativeBossEvent = false;
            bossDefeatEffectsApplied = false;
            bossHealthBaselineApplied = false;
            lastAppliedActors = 0;
            playerTwoImpactReconciliationActive = false;
        }

        internal static void CaptureAndSend(IInputFrameTransport transport,
            uint tick, uint transitionId)
        {
            var level = Level.Current as SlimeLevel;
            if (level == null || !level.Started || transport == null ||
                !transport.IsConnected || tick == 0 ||
                RemoteInputLab.SceneTransitionActive ||
                LevelLoadGate.IsHoldingGameplay)
                return;

            var properties = GetProperties(level);
            if (properties == null)
                return;

            SlimeLevelSlime small;
            SlimeLevelSlime big;
            SlimeLevelTombstone tombstone;
            GetActors(level, out small, out big, out tombstone);

            byte actors;
            Component activeActor;
            byte actionState;
            SelectHostActors(small, big, tombstone, out actors,
                out activeActor, out actionState);
            if (activeActor == null || actors == 0)
                return;

            var animator = activeActor.GetComponent<Animator>();
            var animatorState = animator == null ? default(AnimatorStateInfo) :
                animator.GetCurrentAnimatorStateInfo(0);
            var position = activeActor.transform.position;
            var scale = activeActor.transform.localScale;
            var currentHealth = Mathf.Clamp(properties.CurrentHealth,
                0f, properties.TotalHealth);
            var flags = BossStateFlags.Active;
            if (properties.CurrentHealth <= 0f)
                flags |= BossStateFlags.Defeated;

            transport.SendBossState(new BossStateSnapshot
            {
                Tick = tick,
                TransitionId = transitionId,
                LevelId = (int)Levels.Slime,
                Flags = flags,
                Phase = (byte)properties.CurrentState.stateName,
                ActiveActor = actors,
                ActionState = actionState,
                CurrentHealth = currentHealth,
                TotalHealth = properties.TotalHealth,
                X = position.x,
                Y = position.y,
                ScaleX = scale.x,
                ScaleY = scale.y,
                AnimatorStateHash = animator == null ? 0 :
                    animatorState.fullPathHash,
                AnimatorNormalizedTime = animator == null ? 0f :
                    animatorState.normalizedTime,
            });
        }

        internal static void ProcessIncoming(IInputFrameTransport transport)
        {
            if (!RemoteInputLab.IsClientSession || transport == null)
                return;

            BossStateSnapshot state;
            while (transport.TryReceiveBossState(out state))
            {
                if (state.LevelId != (int)Levels.Slime ||
                    (state.Flags & BossStateFlags.Active) == 0)
                    continue;
                if (hasLastTick && !IsNewerTick(state.Tick, lastTick))
                    continue;

                var sceneName = SceneManager.GetActiveScene().name;
                if (sceneName != "scene_level_slime")
                    continue;
                var level = Level.Current as SlimeLevel;
                if (level == null || !level.Started ||
                    RemoteInputLab.SceneTransitionActive ||
                    LevelLoadGate.IsHoldingGameplay)
                    continue;
                if (state.TransitionId == 0 ||
                    state.TransitionId != RemoteInputLab.CurrentSceneEpoch ||
                    state.Phase > 3 || !IsValidActorMask(state.ActiveActor) ||
                    state.ActionState > 3)
                    continue;

                if (!authorityArmed)
                    ArmClientAuthority();

                var now = Time.realtimeSinceStartup;
                if (hasLatest && latestScene == sceneName &&
                    PrimaryActor(latest.ActiveActor) ==
                        PrimaryActor(state.ActiveActor))
                {
                    var elapsed = now - latestReceivedRealtime;
                    if (elapsed >= MinVelocitySampleSeconds &&
                        elapsed <= VelocityStallSeconds)
                    {
                        var measuredVelocity = new Vector2(
                            (state.X - latest.X) / elapsed,
                            (state.Y - latest.Y) / elapsed);
                        measuredVelocity = Vector2.ClampMagnitude(
                            measuredVelocity, MaxInferredSpeed);
                        inferredVelocity = Vector2.Lerp(inferredVelocity,
                            measuredVelocity, VelocityFilterWeight);
                    }
                    else
                        inferredVelocity = Vector2.zero;
                }
                else
                {
                    inferredVelocity = Vector2.zero;
                }

                latest = state;
                hasLatest = true;
                lastTick = state.Tick;
                hasLastTick = true;
                latestScene = sceneName;
                latestReceivedRealtime = now;

                if (!firstSnapshotLogged)
                {
                    firstSnapshotLogged = true;
                    Plugin.Log.LogMessage("[BossSync] Goopy: primer snapshot " +
                        "autoritativo recibido (HP=" +
                        state.CurrentHealth.ToString("0.0") + "/" +
                        state.TotalHealth.ToString("0.0") + ").");
                }
                if (state.Phase != lastLoggedPhase ||
                    state.ActiveActor != lastLoggedActors)
                {
                    lastLoggedPhase = state.Phase;
                    lastLoggedActors = state.ActiveActor;
                    Plugin.Log.LogMessage("[BossSync] Goopy: fase=" +
                        state.Phase + " actores=" + state.ActiveActor +
                        " acción=" + state.ActionState + ".");
                }
                if (!defeatLogged &&
                    (state.Flags & BossStateFlags.Defeated) != 0)
                {
                    defeatLogged = true;
                    Plugin.Log.LogMessage("[BossSync] Goopy: KO confirmado " +
                        "por el host.");
                }
            }
        }

        internal static void ApplyLatest()
        {
            if (!ShouldSuppressClientSimulation || !hasLatest ||
                RemoteInputLab.SessionOverlayVisible ||
                RemoteInputLab.SceneTransitionActive ||
                latestScene != SceneManager.GetActiveScene().name)
                return;

            var snapshotAge = Time.realtimeSinceStartup -
                latestReceivedRealtime;
            if (snapshotAge > VelocityStallSeconds)
                inferredVelocity = Vector2.zero;
            if (snapshotAge > SnapshotTimeoutSeconds)
                return;

            var level = Level.Current as SlimeLevel;
            if (level == null || !level.Started)
                return;
            var properties = GetProperties(level);
            if (properties == null)
                return;

            ApplyBossHealth(properties);

            SlimeLevelSlime small;
            SlimeLevelSlime big;
            SlimeLevelTombstone tombstone;
            GetActors(level, out small, out big, out tombstone);
            ApplyActorLifecycle(big, tombstone);
            SetActorVisible(small, (latest.ActiveActor & SmallActor) != 0);
            SetActorVisible(big, (latest.ActiveActor & BigActor) != 0);
            SetActorVisible(tombstone,
                (latest.ActiveActor & TombstoneActor) != 0);

            Component actor = null;
            if ((latest.ActiveActor & TombstoneActor) != 0)
                actor = tombstone;
            else if ((latest.ActiveActor & SmallActor) != 0)
                actor = small;
            else if ((latest.ActiveActor & BigActor) != 0)
                actor = big;
            if (actor == null)
                return;

            ApplyActorState(actor, small, big, tombstone);
            ApplyBossDefeatEffects(actor, tombstone);
            lastAppliedActors = latest.ActiveActor;
        }

        internal static void ApplyAuthoritativePlayerState(
            PlayerStateSnapshot state)
        {
            if (!ShouldSuppressClientSimulation || Level.Current == null ||
                !Level.Current.Started)
            {
                playerHealthBaselineMask = 0;
                authoritativeDeathAppliedMask = 0;
                playerOneLifeState = AuthoritativePlayerLifeState.Unknown;
                playerTwoLifeState = AuthoritativePlayerLifeState.Unknown;
                playerTwoImpactReconciliationActive = false;
                return;
            }

            ApplyOrBaselinePlayer(PlayerId.PlayerOne, 1, state);
            var playerTwoDamaged = ApplyOrBaselinePlayer(
                PlayerId.PlayerTwo, 2, state);
            ApplyPlayerTwoImpactReconciliation(state, playerTwoDamaged);
        }

        private static void ArmClientAuthority()
        {
            var level = Level.Current as SlimeLevel;
            if (level == null)
                return;
            authorityArmed = true;
            SlimeLevelSlime small;
            SlimeLevelSlime big;
            SlimeLevelTombstone tombstone;
            GetActors(level, out small, out big, out tombstone);
            level.StopAllCoroutines();
            if (small != null)
                small.StopAllCoroutines();
            if (big != null)
                big.StopAllCoroutines();
            if (tombstone != null)
                tombstone.StopAllCoroutines();
            Plugin.Log.LogMessage("[BossSync] Goopy queda bajo autoridad " +
                "del host; IA local detenida.");
        }

        private static bool IsValidActorMask(byte actors)
        {
            return actors == SmallActor || actors == BigActor ||
                actors == TombstoneActor ||
                actors == (BigActor | TombstoneActor);
        }

        private static void SelectHostActors(SlimeLevelSlime small,
            SlimeLevelSlime big, SlimeLevelTombstone tombstone,
            out byte actors, out Component activeActor, out byte actionState)
        {
            actors = 0;
            activeActor = null;
            actionState = 0;

            var tombstoneStarted = tombstone != null &&
                tombstone.state != SlimeLevelTombstone.State.Init;
            if (tombstoneStarted)
            {
                actors |= TombstoneActor;
                if (big != null)
                    actors |= BigActor;
                activeActor = tombstone;
                actionState = (byte)tombstone.state;
                return;
            }
            if (small != null)
            {
                actors = SmallActor;
                activeActor = small;
                actionState = (byte)small.state;
                return;
            }
            if (big != null)
            {
                actors = BigActor;
                activeActor = big;
                actionState = (byte)big.state;
            }
        }

        private static void ApplyBossHealth(LevelProperties.Slime properties)
        {
            var authoritativeHealth = Mathf.Clamp(latest.CurrentHealth,
                0f, properties.TotalHealth);

            // Antes de armar la autoridad el invitado puede haber simulado unos
            // pocos frames. Si en ellos registró daño propio, no debemos dejarlo
            // permanentemente por delante del host.
            if (!bossHealthBaselineApplied)
            {
                bossHealthBaselineApplied = true;
                if (properties.CurrentHealth < authoritativeHealth)
                {
                    if (BossCurrentHealthField != null)
                        BossCurrentHealthField.SetValue(properties,
                            authoritativeHealth);
                    return;
                }
            }

            var damage = properties.CurrentHealth - authoritativeHealth;
            if (damage > 0.001f)
            {
                // DealDamage conserva el timeline, los cambios de fase y el KO del
                // nivel. Los métodos que arrancan IA/transformaciones visuales están
                // anulados sólo en el invitado y la imagen llega por snapshot.
                properties.DealDamage(damage);
            }
        }

        private static void ApplyActorState(Component actor,
            SlimeLevelSlime small, SlimeLevelSlime big,
            SlimeLevelTombstone tombstone)
        {
            var prediction = Time.realtimeSinceStartup - latestReceivedRealtime;
            if (RemoteInputLab.PingMilliseconds > 0)
                prediction += RemoteInputLab.PingMilliseconds * 0.0005f;
            prediction = Mathf.Clamp(prediction, 0f,
                MaxExtrapolationSeconds);
            var extrapolation = Vector2.ClampMagnitude(
                inferredVelocity * prediction, MaxExtrapolationDistance);
            var target = new Vector3(
                latest.X + extrapolation.x,
                latest.Y + extrapolation.y,
                actor.transform.position.z);
            var distance = Vector2.Distance(actor.transform.position, target);
            var actorChanged = PrimaryActor(lastAppliedActors) !=
                PrimaryActor(latest.ActiveActor);
            if (actorChanged || distance > ActorEmergencySnapDistance)
                actor.transform.position = target;
            else
                actor.transform.position = Vector3.Lerp(
                    actor.transform.position, target,
                    Mathf.Clamp01(Time.unscaledDeltaTime * ActorFollowSpeed));

            var scale = actor.transform.localScale;
            scale.x = latest.ScaleX;
            scale.y = latest.ScaleY;
            actor.transform.localScale = scale;

            if (actor == tombstone && TombstoneStateField != null)
                TombstoneStateField.SetValue(tombstone,
                    (SlimeLevelTombstone.State)latest.ActionState);
            else if ((actor == small || actor == big) &&
                SlimeStateField != null)
                SlimeStateField.SetValue(actor,
                    (SlimeLevelSlime.State)latest.ActionState);

            var animator = actor.GetComponent<Animator>();
            if (animator == null || latest.AnimatorStateHash == 0)
                return;
            if (!animator.enabled)
                animator.enabled = true;
            var localState = animator.GetCurrentAnimatorStateInfo(0);
            if (actorChanged ||
                localState.fullPathHash != latest.AnimatorStateHash)
            {
                animator.Play(latest.AnimatorStateHash, 0,
                    latest.AnimatorNormalizedTime);
                animator.Update(0f);
            }
        }

        private static void SetActorVisible(Component actor, bool visible)
        {
            if (actor == null)
                return;
            if (actor.gameObject.activeSelf != visible)
                actor.gameObject.SetActive(visible);
            var colliders = actor.GetComponents<Collider2D>();
            for (var i = 0; i < colliders.Length; i++)
                colliders[i].enabled = visible;
        }

        private static void ApplyActorLifecycle(SlimeLevelSlime big,
            SlimeLevelTombstone tombstone)
        {
            if ((latest.ActiveActor & TombstoneActor) != 0 &&
                tombstone != null)
            {
                var exploder = tombstone.GetComponent<LevelBossDeathExploder>();
                if (exploder != null && !exploder.enabled)
                    exploder.enabled = true;
            }

            if ((lastAppliedActors & BigActor) != 0 &&
                (latest.ActiveActor & BigActor) == 0 && big != null)
            {
                // La caída de la lápida destruye al slime grande en el host. En el
                // invitado conservamos la referencia, pero lo ocultamos tras crear
                // el mismo efecto de explosión cuando sea posible.
                applyingAuthoritativeBossEvent = true;
                try { big.Explode(); }
                catch { }
                finally { applyingAuthoritativeBossEvent = false; }
            }
        }

        private static void ApplyBossDefeatEffects(Component actor,
            SlimeLevelTombstone tombstone)
        {
            if (bossDefeatEffectsApplied ||
                (latest.Flags & BossStateFlags.Defeated) == 0)
                return;
            applyingAuthoritativeBossEvent = true;
            try
            {
                if (actor == tombstone && TombstoneBossDeathMethod != null)
                    TombstoneBossDeathMethod.Invoke(tombstone, null);
                else
                {
                    var slime = actor as SlimeLevelSlime;
                    if (slime != null && SlimeBossDeathMethod != null)
                        SlimeBossDeathMethod.Invoke(slime, null);
                }
                bossDefeatEffectsApplied = true;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[BossSync] No se pudo reproducir el KO " +
                    "visual de Goopy: " + ex.Message);
            }
            finally
            {
                applyingAuthoritativeBossEvent = false;
            }
        }

        private static bool ApplyOrBaselinePlayer(PlayerId id, byte mask,
            PlayerStateSnapshot state)
        {
            if ((state.PresentMask & mask) == 0)
                return false;
            var targetHealth = id == PlayerId.PlayerOne ?
                state.PlayerOneHealth : state.PlayerTwoHealth;
            var targetSuperMeter = id == PlayerId.PlayerOne ?
                state.PlayerOneSuperMeter : state.PlayerTwoSuperMeter;
            var authoritativeX = id == PlayerId.PlayerOne ?
                state.PlayerOneX : state.PlayerTwoX;
            var authoritativeY = id == PlayerId.PlayerOne ?
                state.PlayerOneY : state.PlayerTwoY;
            var authoritativeHitDirection = id == PlayerId.PlayerTwo ?
                state.PlayerTwoHitDirection : (sbyte)0;
            var lifeState = GetAuthoritativeLifeState(id, mask, state);
            var previousLifeState = GetTrackedLifeState(id);
            if ((playerHealthBaselineMask & mask) != 0)
            {
                return ApplyPlayerHealth(id, mask,
                    targetHealth, targetSuperMeter, lifeState,
                    previousLifeState, authoritativeX, authoritativeY,
                    authoritativeHitDirection);
            }

            AbstractPlayerController player;
            try { player = PlayerManager.GetPlayer(id); }
            catch { return false; }
            if (player == null || player.stats == null)
                return false;
            if (lifeState == AuthoritativePlayerLifeState.Dead)
            {
                player.stats.SetHealth(0);
                EnsurePlayerDeath(player, mask);
            }
            else if (lifeState == AuthoritativePlayerLifeState.Reviving &&
                (player.IsDead || IsPlayerReviving(player) ||
                    FindPlayerDeathEffect(id, out _, out _) != null))
            {
                BeginAuthoritativeRevive(player, mask, targetHealth,
                    authoritativeX, authoritativeY);
            }
            else if (player.stats.Health != targetHealth)
            {
                player.stats.SetHealth(targetHealth);
            }
            SetPlayerSuperMeter(player.stats, id, targetSuperMeter);
            playerHealthBaselineMask |= mask;
            SetTrackedLifeState(id, lifeState);
            ReportLifeTransition(id, previousLifeState, lifeState,
                targetHealth, "baseline");
            Plugin.Log.LogInfo("[BossSync] " + id +
                " alineado con el host (HP=" + targetHealth + ").");
            return false;
        }

        private static bool ApplyPlayerHealth(PlayerId id, byte mask,
            byte targetHealth, float targetSuperMeter,
            AuthoritativePlayerLifeState lifeState,
            AuthoritativePlayerLifeState previousLifeState,
            float authoritativeX, float authoritativeY,
            sbyte authoritativeHitDirection)
        {
            AbstractPlayerController player;
            try { player = PlayerManager.GetPlayer(id); }
            catch { return false; }
            if (player == null || player.stats == null)
                return false;

            var currentHealth = player.stats.Health;
            var damageApplied = lifeState !=
                AuthoritativePlayerLifeState.Reviving &&
                targetHealth < currentHealth;
            if (damageApplied)
            {
                AlignPlayerToAuthority(player, authoritativeX,
                    authoritativeY, true);
                var hits = currentHealth - targetHealth;
                for (var i = 0; i < hits && player.stats.Health > 0; i++)
                {
                    applyingAuthoritativePlayerDamage = true;
                    try
                    {
                        if (player.damageReceiver != null)
                        {
                            player.damageReceiver.Vulnerable();
                            player.damageReceiver.TakeDamage(
                                new DamageDealer.DamageInfo(1f,
                                    DamageDealer.Direction.Neutral,
                                    player.transform.position,
                                    DamageDealer.DamageSource.Enemy));
                        }
                        else
                        {
                            player.stats.SetHealth(player.stats.Health - 1);
                        }
                    }
                    finally
                    {
                        applyingAuthoritativePlayerDamage = false;
                    }
                }
                SetPlayerHitDirection(player, authoritativeHitDirection);
                if (lifeState != AuthoritativePlayerLifeState.Dead &&
                    player.stats.Health != targetHealth)
                    player.stats.SetHealth(targetHealth);
                Plugin.Log.LogMessage("[BossSync] Golpe de Goopy confirmado " +
                    "para " + id + " (HP=" + targetHealth + ").");
            }

            if (lifeState == AuthoritativePlayerLifeState.Dead)
            {
                if (player.stats.Health != 0)
                    player.stats.SetHealth(0);
                EnsurePlayerDeath(player, mask);
            }
            else if (lifeState == AuthoritativePlayerLifeState.Reviving)
            {
                if (previousLifeState == AuthoritativePlayerLifeState.Dead ||
                    (previousLifeState !=
                        AuthoritativePlayerLifeState.Reviving &&
                    (player.IsDead || IsPlayerReviving(player) ||
                        FindPlayerDeathEffect(id, out _, out _) != null)))
                {
                    BeginAuthoritativeRevive(player, mask, targetHealth,
                        authoritativeX, authoritativeY);
                }
                else if (targetHealth > 0 &&
                    player.stats.Health != targetHealth)
                {
                    player.stats.SetHealth(targetHealth);
                }
            }
            else
            {
                if (previousLifeState == AuthoritativePlayerLifeState.Dead)
                {
                    // Si se perdió el snapshot Reviving, todavía recorremos una
                    // sola vez el ghost local antes de adoptar Alive.
                    BeginAuthoritativeRevive(player, mask, targetHealth,
                        authoritativeX, authoritativeY);
                }
                if (targetHealth > player.stats.Health)
                    player.stats.SetHealth(targetHealth);
                if (previousLifeState ==
                        AuthoritativePlayerLifeState.Reviving ||
                    previousLifeState == AuthoritativePlayerLifeState.Dead)
                {
                    AlignPlayerToAuthority(player, authoritativeX,
                        authoritativeY, true);
                    authoritativeDeathAppliedMask = (byte)
                        (authoritativeDeathAppliedMask & ~mask);
                    Plugin.Log.LogInfo("[BossSync] " + id +
                        " confirmó Alive sin repetir OnRevive.");
                }
            }
            SetPlayerSuperMeter(player.stats, id, targetSuperMeter);
            SetTrackedLifeState(id, lifeState);
            ReportLifeTransition(id, previousLifeState, lifeState,
                targetHealth, "snapshot");
            return damageApplied;
        }

        private static void ApplyPlayerTwoImpactReconciliation(
            PlayerStateSnapshot state, bool damageApplied)
        {
            if ((state.PresentMask & 2) == 0 ||
                (state.DeadMask & 2) != 0)
            {
                playerTwoImpactReconciliationActive = false;
                return;
            }

            var hostIsHit = (state.PlayerTwoMotionFlags &
                PlayerMotionFlags.Hit) != 0;
            if (!playerTwoImpactReconciliationActive &&
                !damageApplied && !hostIsHit)
                return;

            AbstractPlayerController player;
            try { player = PlayerManager.GetPlayer(PlayerId.PlayerTwo); }
            catch { return; }
            if (player == null)
                return;

            if (damageApplied || hostIsHit)
                playerTwoImpactReconciliationActive = true;

            if (hostIsHit)
            {
                AlignPlayerToAuthority(player, state.PlayerTwoX,
                    state.PlayerTwoY, damageApplied);
                SetPlayerHitDirection(player,
                    state.PlayerTwoHitDirection);
                return;
            }

            AlignPlayerToAuthority(player, state.PlayerTwoX,
                state.PlayerTwoY, true);
            ResetPlayerHitState(player);
            playerTwoImpactReconciliationActive = false;
            Plugin.Log.LogInfo("[BossSync] P2 cerró el golpe en la " +
                "posición confirmada por el host.");
        }

        private static void AlignPlayerToAuthority(
            AbstractPlayerController player, float x, float y, bool exact)
        {
            if (player == null)
                return;
            var current = player.transform.position;
            var target = new Vector3(x, y, current.z);
            var distance = Vector2.Distance(current, target);
            var corrected = exact || distance > PlayerImpactSnapDistance ?
                target : Vector3.Lerp(current, target,
                    Mathf.Clamp01(Time.unscaledDeltaTime *
                        PlayerImpactFollowSpeed));
            player.transform.position = corrected;

            var levelPlayer = player as LevelPlayerController;
            var motor = levelPlayer == null ? null : levelPlayer.motor;
            if (motor == null)
                return;
            if (PlayerMotorLastPositionField != null)
                PlayerMotorLastPositionField.SetValue(motor,
                    (Vector2)player.transform.position);
            if (PlayerMotorLastPositionFixedField != null)
                PlayerMotorLastPositionFixedField.SetValue(motor,
                    (Vector2)player.transform.localPosition);
        }

        private static void SetPlayerHitDirection(
            AbstractPlayerController player, sbyte direction)
        {
            if (player == null ||
                PlayerMotorHitManagerField == null ||
                PlayerHitDirectionField == null)
                return;
            try
            {
                var levelPlayer = player as LevelPlayerController;
                var motor = levelPlayer == null ? null : levelPlayer.motor;
                var hitManager = motor == null ? null :
                    PlayerMotorHitManagerField.GetValue(motor);
                if (hitManager != null)
                    PlayerHitDirectionField.SetValue(hitManager,
                        (int)direction);
            }
            catch { }
        }

        private static void ResetPlayerHitState(
            AbstractPlayerController player)
        {
            try
            {
                var levelPlayer = player as LevelPlayerController;
                var motor = levelPlayer == null ? null : levelPlayer.motor;
                if (motor == null)
                    return;
                if (PlayerMotorHitManagerField != null &&
                    PlayerHitResetMethod != null)
                {
                    var hitManager = PlayerMotorHitManagerField.GetValue(motor);
                    if (hitManager != null)
                        PlayerHitResetMethod.Invoke(hitManager, null);
                }
                if (PlayerMotorVelocityManagerField != null &&
                    PlayerHitVelocityField != null)
                {
                    var velocityManager =
                        PlayerMotorVelocityManagerField.GetValue(motor);
                    if (velocityManager != null)
                        PlayerHitVelocityField.SetValue(velocityManager, 0f);
                }
            }
            catch { }
        }

        private static void EnsurePlayerDeath(AbstractPlayerController player,
            byte mask)
        {
            if (player == null || player.stats == null ||
                (authoritativeDeathAppliedMask & mask) != 0)
                return;
            bool ghostExiting;
            int ghostMatches;
            var existingGhost = FindPlayerDeathEffect(player.id,
                out ghostExiting, out ghostMatches);
            if (existingGhost != null)
            {
                authoritativeDeathAppliedMask |= mask;
                Plugin.Log.LogInfo("[BossSync] Muerte de " + player.id +
                    " ya representada por el ghost local (exiting=" +
                    ghostExiting + ", coincidencias=" + ghostMatches + ").");
                return;
            }
            if (!player.gameObject.activeSelf)
            {
                authoritativeDeathAppliedMask |= mask;
                return;
            }
            try
            {
                if (PlayerStatsDeathMethod != null)
                    PlayerStatsDeathMethod.Invoke(player.stats, null);
                authoritativeDeathAppliedMask |= mask;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[BossSync] No se pudo reproducir la " +
                    "muerte de " + player.id + ": " + ex.Message);
            }
        }

        private static void BeginAuthoritativeRevive(
            AbstractPlayerController player,
            byte mask, byte targetHealth, float x, float y)
        {
            if (player == null || player.stats == null)
                return;
            var position = new Vector3(x, y, player.transform.position.z);
            bool ghostExiting;
            int ghostMatches;
            var ghost = FindPlayerDeathEffect(player.id, out ghostExiting,
                out ghostMatches);
            try
            {
                if (ghost != null)
                {
                    if (ghostMatches > 1)
                    {
                        Plugin.Log.LogWarning("[BossSync] Se encontraron " +
                            ghostMatches + " ghosts para " + player.id +
                            "; sólo se accionará uno para evitar revives duplicadas.");
                    }
                    if (ghostExiting)
                    {
                        Plugin.Log.LogInfo("[BossSync] Revive de " + player.id +
                            " ya estaba predicha por el ghost local; se adopta " +
                            "la confirmación del host sin repetirla.");
                    }
                    else if (DeathEffectParryMethod != null)
                    {
                        DeathEffectParryMethod.Invoke(ghost, null);
                        Plugin.Log.LogMessage("[BossSync] Revive de " + player.id +
                            " inició el pipeline nativo del ghost.");
                    }
                    else
                    {
                        Plugin.Log.LogWarning("[BossSync] El ghost de " +
                            player.id + " existe, pero OnParrySwitch no está " +
                            "disponible; no se invocará OnRevive por duplicado.");
                    }
                }
                else if (IsPlayerReviving(player))
                {
                    Plugin.Log.LogInfo("[BossSync] Revive de " + player.id +
                        " ya estaba en curso sin ghost visible; sólo se confirma.");
                }
                else if (!player.IsDead && player.gameObject.activeSelf &&
                    player.stats.Health > 0)
                {
                    Plugin.Log.LogInfo("[BossSync] Revive de " + player.id +
                        " ya terminó localmente antes del snapshot del host; " +
                        "no se vuelve a ejecutar.");
                }
                else
                {
                    DirectReviveFallback(player, position, targetHealth);
                }
                if (targetHealth > 0 && player.stats.Health != targetHealth)
                    player.stats.SetHealth(targetHealth);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[BossSync] No se pudo iniciar la " +
                    "reanimación de " + player.id + ": " + ex.Message);
            }
        }

        private static void DirectReviveFallback(
            AbstractPlayerController player, Vector3 position,
            byte targetHealth)
        {
            Plugin.Log.LogWarning("[BossSync] No existe ghost para " +
                player.id + "; se usará el fallback directo de revive una vez.");
            var parameterTypes = new[] { typeof(Vector3) };
            var preRevive = AccessTools.Method(player.GetType(),
                "OnPreRevive", parameterTypes);
            var revive = AccessTools.Method(player.GetType(),
                "OnRevive", parameterTypes);
            if (preRevive != null && revive != null)
            {
                preRevive.Invoke(player, new object[] { position });
                revive.Invoke(player, new object[] { position });
                return;
            }

            if (!player.gameObject.activeSelf)
                player.gameObject.SetActive(true);
            player.stats.SetHealth(Mathf.Max(1, targetHealth));
            player.stats.OnRevive();
        }

        private static PlayerDeathEffect FindPlayerDeathEffect(PlayerId id,
            out bool exiting, out int matches)
        {
            exiting = false;
            matches = 0;
            if (DeathEffectPlayerIdField == null)
                return null;
            PlayerDeathEffect first = null;
            try
            {
                var effects = UnityEngine.Object.FindObjectsOfType<
                    PlayerDeathEffect>();
                for (var i = 0; i < effects.Length; i++)
                {
                    var effect = effects[i];
                    if (effect == null ||
                        (PlayerId)DeathEffectPlayerIdField.GetValue(effect) != id)
                        continue;
                    matches++;
                    if (first != null)
                        continue;
                    first = effect;
                    if (DeathEffectExitingField != null)
                        exiting = (bool)DeathEffectExitingField.GetValue(effect);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[BossSync] No se pudo inspeccionar el " +
                    "ghost de " + id + ": " + ex.Message);
            }
            return first;
        }

        private static bool IsPlayerReviving(
            AbstractPlayerController player)
        {
            if (player == null || PlayerIsRevivingField == null)
                return false;
            try
            {
                return (bool)PlayerIsRevivingField.GetValue(player);
            }
            catch
            {
                return false;
            }
        }

        private static AuthoritativePlayerLifeState GetAuthoritativeLifeState(
            PlayerId id, byte mask, PlayerStateSnapshot state)
        {
            if ((state.DeadMask & mask) != 0)
                return AuthoritativePlayerLifeState.Dead;
            var motionFlags = id == PlayerId.PlayerOne ?
                state.PlayerOneMotionFlags : state.PlayerTwoMotionFlags;
            return (motionFlags & PlayerMotionFlags.Reviving) != 0 ?
                AuthoritativePlayerLifeState.Reviving :
                AuthoritativePlayerLifeState.Alive;
        }

        private static AuthoritativePlayerLifeState GetTrackedLifeState(
            PlayerId id)
        {
            return id == PlayerId.PlayerOne ? playerOneLifeState :
                playerTwoLifeState;
        }

        private static void SetTrackedLifeState(PlayerId id,
            AuthoritativePlayerLifeState state)
        {
            if (id == PlayerId.PlayerOne)
                playerOneLifeState = state;
            else
                playerTwoLifeState = state;
        }

        private static void ReportLifeTransition(PlayerId id,
            AuthoritativePlayerLifeState previous,
            AuthoritativePlayerLifeState current, byte health,
            string source)
        {
            if (previous == current)
                return;
            bool ghostExiting;
            int ghostMatches;
            FindPlayerDeathEffect(id, out ghostExiting, out ghostMatches);
            Plugin.Log.LogMessage("[BossSync] Vida autoritativa " + id +
                ": " + previous + " -> " + current + " (HP=" + health +
                ", origen=" + source + ", ghosts=" + ghostMatches +
                ", ghostExiting=" + ghostExiting + ").");
        }

        private static void SetPlayerSuperMeter(PlayerStatsManager stats,
            PlayerId id, float value)
        {
            if (stats == null || SetSuperMeterMethod == null)
                return;
            value = Mathf.Clamp(value, 0f, stats.SuperMeterMax);
            if (RemoteInputLab.ShouldDeferRemotePlayerSuperMeter(id,
                stats.SuperMeter, value))
                return;
            if (Mathf.Abs(stats.SuperMeter - value) < 0.01f)
                return;
            try
            {
                SetSuperMeterMethod.Invoke(stats, new object[] { value });
                if (SuperChangedMethod != null)
                    SuperChangedMethod.Invoke(stats, new object[] { false });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[BossSync] No se pudo alinear el super: " +
                    ex.Message);
            }
        }

        private static LevelProperties.Slime GetProperties(SlimeLevel level)
        {
            return PropertiesField == null || level == null ? null :
                PropertiesField.GetValue(level) as LevelProperties.Slime;
        }

        private static void GetActors(SlimeLevel level,
            out SlimeLevelSlime small, out SlimeLevelSlime big,
            out SlimeLevelTombstone tombstone)
        {
            small = SmallSlimeField == null ? null :
                SmallSlimeField.GetValue(level) as SlimeLevelSlime;
            big = BigSlimeField == null ? null :
                BigSlimeField.GetValue(level) as SlimeLevelSlime;
            tombstone = TombstoneField == null ? null :
                TombstoneField.GetValue(level) as SlimeLevelTombstone;
        }

        private static byte PrimaryActor(byte actors)
        {
            if ((actors & TombstoneActor) != 0)
                return TombstoneActor;
            if ((actors & SmallActor) != 0)
                return SmallActor;
            return (byte)(actors & BigActor);
        }

        private static bool IsNewerTick(uint candidate, uint current)
        {
            return candidate != current &&
                unchecked((int)(candidate - current)) > 0;
        }

        private enum AuthoritativePlayerLifeState : byte
        {
            Unknown,
            Alive,
            Dead,
            Reviving,
        }
    }
}
