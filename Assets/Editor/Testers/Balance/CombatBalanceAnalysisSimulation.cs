using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Overburst.EditorBalance.Analysis
{
    // 기대값 시간 시뮬레이션. 치명은 확률로 나눠 더하고(반올림은 실제 함수로 각각), 시간은 실제 클립·구간 값으로 진행한다.
    public static partial class CombatBalanceAnalysisModel
    {
        public sealed class SimOutcome
        {
            public float weakNormal, weakCrit, weakExpected, cycleDuration, weakDps;
            public float chargeTime = float.NaN, chargePhases, prepDamage, prepHealthLeft, heavyDirect, heavyEnergy;
            public float heavyNormal, heavyCritical, heavyKillChance, derivedRaw, potentialDerived;
            public float tripleTime = float.NaN;
            public bool prepSurvived, heavyKilled, killed;
            public float killTime = float.NaN, heavyCount;
            public float weak, heavy, derived, dot;
            public float firstHeavyKills, weakTargets, heavyTargets;
            public float Total => weak + heavy + derived + dot;
        }

        sealed class Target
        {
            public float hp, max;
            public int stacks, derivedHits;
            public float expires, nextTick, interval, owner, frozenUntil;
            public int lastSequence = -1;
            public bool Alive => hp > 0f;
        }

        sealed class Sim
        {
            public PlayerBuild p;
            public EnemyView e;
            public OverburstElementTuning t;
            public WeaponItemData weapon;
            public WeaponElement element;
            public AnalysisConditions c;
            public readonly List<Target> targets = new List<Target>();
            public float energy, holdUntil, lastDecay;
            public int radiance, sequence, alive;
            public SimOutcome o = new SimOutcome();
            public float Crit01 => Mathf.Clamp01(p.crit / 100f);
            public float Capacity => element == WeaponElement.Light ? t.SafeLightOverchargeMaximum : Mathf.Max(1f, t.maximumEnergy);
            public float BaseMax => Mathf.Max(1f, t.maximumEnergy);
            public int Alive => alive;
        }

        static float ExpectedRounded(float baseDamage, float crit01, float critMultiplier)
            => Mathf.Lerp(CombatBalanceFormulas.RoundedHitDamage(baseDamage, false, critMultiplier),
                CombatBalanceFormulas.RoundedHitDamage(baseDamage, true, critMultiplier), crit01);

        static float Outgoing(Sim s, float damage, PlayerAttackKind kind)
            => CombatBalanceFormulas.ApplyPlayerOutgoing(damage, s.p.totals, true, s.e.grade, kind, 0f, 0f);

        static PlayerAttackKind WithElement(Sim s, PlayerAttackKind kind) => s.element == WeaponElement.None ? kind : kind | PlayerAttackKind.Elemental;

        // ── 상태·틱 (ElementalStatusController / OverburstElementState와 같은 순서)
        static void AdvanceTicks(Sim s, float until)
        {
            if (s.element != WeaponElement.Fire && s.element != WeaponElement.Electric) { ExpireAll(s, until); return; }
            foreach (var x in s.targets)
            {
                while (x.Alive && x.stacks > 0 && x.interval > 0f && x.nextTick <= until + 1e-5f && x.nextTick <= x.expires + 1e-5f)
                {
                    float d = CombatBalanceFormulas.StatusTickDamage(s.t, s.element, x.owner, x.stacks);
                    Deal(s, x, d, 3, x.nextTick);
                    x.nextTick += x.interval;
                }
                if (x.stacks > 0 && until >= x.expires && !(x.interval > 0f && x.nextTick <= x.expires + 1e-5f)) x.stacks = 0;
            }
        }

        static void ExpireAll(Sim s, float now)
        {
            foreach (var x in s.targets)
                if (x.stacks > 0 && now >= x.expires) { x.stacks = 0; x.frozenUntil = 0f; }
        }

        static void ApplyStatus(Sim s, Target x, float actualDirect, float now)
        {
            if (!OverburstElementRules.IsActive(s.element) || s.element == WeaponElement.Light || !x.Alive) return;
            if (x.lastSequence == s.sequence) return; // 공격 시퀀스당 대상 1중첩
            x.lastSequence = s.sequence;
            if (s.element == WeaponElement.Ice && x.frozenUntil > now && x.stacks > 0) return; // 빙결 중 추가·연장 없음
            if (x.stacks == 0) { x.interval = s.t.TickInterval(s.element); x.nextTick = now + x.interval; }
            x.stacks = Mathf.Min(Mathf.Max(1, s.t.maximumStacks), x.stacks + 1);
            x.expires = now + s.t.StatusDuration(s.element);
            x.owner = actualDirect;
            if (s.element == WeaponElement.Ice && x.stacks >= Mathf.Max(1, s.t.maximumStacks))
            {
                x.frozenUntil = now + Mathf.Max(.1f, s.t.freezeDuration);
                x.expires = x.frozenUntil;
            }
        }

        // kind: 0 약공 1 강공 2 파생 3 틱
        static float Deal(Sim s, Target x, float damage, int kind, float time)
        {
            if (!x.Alive || damage <= 0f) return 0f;
            float applied = Mathf.Min(x.hp, damage);
            x.hp -= damage;
            if (kind == 2) s.o.derivedRaw += damage;
            if (kind == 0) s.o.weak += applied; else if (kind == 1) s.o.heavy += applied; else if (kind == 2) s.o.derived += applied; else s.o.dot += applied;
            if (!x.Alive) s.alive--;
            if (s.alive == 0 && float.IsNaN(s.o.killTime)) { s.o.killTime = time; s.o.killed = true; }
            return applied;
        }

        static void GainEnergy(Sim s, int targetsHit, float now)
        {
            if (!OverburstElementRules.IsActive(s.element) || targetsHit <= 0) return;
            DecayLight(s, now);
            float anyCrit = 1f - Mathf.Pow(1f - s.Crit01, targetsHit);
            float gain = Mathf.Lerp(CombatBalanceFormulas.PhaseEnergyGain(s.t, false, 0f), CombatBalanceFormulas.PhaseEnergyGain(s.t, true, 0f), anyCrit);
            s.energy = Mathf.Min(s.Capacity, s.energy + gain);
            if (s.element != WeaponElement.Light) return;
            if (s.energy >= s.Capacity - .0001f) s.holdUntil = now + s.t.SafeLightOverchargeFullHold;
            if (s.energy > s.BaseMax + .0001f)
                s.radiance = Mathf.Min(s.t.SafeLightRadianceMaxStacks, s.radiance + s.t.SafeLightRadianceStacksPerPhase);
        }

        static void DecayLight(Sim s, float now)
        {
            if (s.element != WeaponElement.Light) return;
            float dt = Mathf.Max(0f, now - s.lastDecay); s.lastDecay = now;
            if (s.energy <= s.BaseMax + .0001f) { s.radiance = 0; return; }
            if (s.energy >= s.Capacity - .0001f && now < s.holdUntil) return;
            s.energy = Mathf.Max(s.BaseMax, s.energy - s.t.SafeLightOverchargeDecayPerSecond * dt);
            if (s.energy <= s.BaseMax + .0001f) s.radiance = 0;
        }

        static bool PrepReady(Sim s)
        {
            if (!OverburstElementRules.IsActive(s.element)) return false; // 무원소는 에너지가 없어 약공만 비교한다.
            if (s.element == WeaponElement.Light && s.c.lightTriple)
                return s.energy >= s.Capacity - .01f && s.radiance >= s.t.SafeLightRadianceMaxStacks;
            return s.energy >= s.BaseMax - .01f;
        }

        // 약공 한 Phase: 전방 호 안의 대상에 같은 기대 피해, 에너지 1회.
        static int WeakPhase(Sim s, float coefficient, float time, int targetsPerPhase)
        {
            float baseDamage = s.p.attack * coefficient * CombatBalanceFormulas.AttackDamageMultiplier(s.weapon, HeavyDefinition(s.weapon), false, false);
            float expected = ExpectedRounded(baseDamage, s.Crit01, s.p.critDamage);
            float dealt = Outgoing(s, expected, WithElement(s, PlayerAttackKind.Weak));
            int hit = 0;
            foreach (var x in s.targets)
            {
                if (hit >= targetsPerPhase) break;
                if (!x.Alive) continue;
                float applied = Deal(s, x, dealt, 0, time);
                ApplyStatus(s, x, applied, time);
                hit++;
            }
            GainEnergy(s, hit, time);
            return hit;
        }

        // 강공: 첫 폭발(치명 판정 포함) + 원소 후속. 반경 안 대상 수는 군집 밀도로 정한다.
        // local은 연쇄(불 폭발·번개 도약)가 이웃을 찾는 밀착 밀도다. 교전 중 적은 플레이어·서로에 붙어 있다.
        static void Heavy(Sim s, HeavyTiming h, float time, Func<float, int> targetsInRadius, Func<float, int> local)
        {
            DecayLight(s, time);
            bool active = OverburstElementRules.IsActive(s.element);
            float e = active ? Mathf.Clamp01(s.energy / s.BaseMax) : 0f;
            float attackDamage = s.p.attack * h.phaseMultiplier;
            float blast, radius;
            bool lightTriple = s.element == WeaponElement.Light && s.energy > s.BaseMax + .0001f;
            float overcharge = s.element != WeaponElement.Light ? 0f
                : Mathf.Clamp01((s.energy - s.BaseMax) / Mathf.Max(.0001f, s.Capacity - s.BaseMax));
            int radianceAtCommit = s.radiance;
            if (active)
            {
                float coefficient = CombatBalanceFormulas.DischargeEnergyCoefficient(s.t, e, 0f, 0f);
                float power = WeaponStatCalculator.GetElementalDischargePower(s.p.weaponItem, attackDamage);
                blast = CombatBalanceFormulas.HeavyFirstBlastDamage(attackDamage, e, coefficient, power);
                radius = CombatBalanceFormulas.DischargeRadius(s.t, e);
            }
            else
            {
                blast = attackDamage * CombatBalanceFormulas.AttackDamageMultiplier(s.weapon, HeavyDefinition(s.weapon), true, false);
                radius = s.t.minimumRadius;
            }
            s.o.heavyEnergy = s.energy;
            s.energy = 0f; s.radiance = 0; s.holdUntil = 0f;
            int firstHit = s.element == WeaponElement.Light ? (lightTriple ? 0 : 1) : 0;
            float slam = s.element == WeaponElement.Light
                ? blast * CombatBalanceFormulas.LightTripleHitScale(s.t, firstHit, radianceAtCommit, overcharge) : blast;
            float slamRadius = s.element == WeaponElement.Light ? radius * s.t.LightTripleRadiusScale(firstHit) : radius;
            float direct = Outgoing(s, ExpectedRounded(slam, s.Crit01, s.p.critDamage), WithElement(s, PlayerAttackKind.Heavy));
            if (s.o.heavyCount == 0f)
            {
                s.o.heavyDirect = direct;
                s.o.heavyNormal = CombatBalanceFormulas.RoundedHitDamage(slam, false, s.p.critDamage);
                s.o.heavyCritical = CombatBalanceFormulas.RoundedHitDamage(slam, true, s.p.critDamage);
            }
            s.o.heavyCount++;
            int m = Mathf.Max(1, targetsInRadius(slamRadius));
            var hitList = new List<Target>();
            foreach (var x in s.targets) { if (hitList.Count >= m) break; if (x.Alive) hitList.Add(x); }
            s.o.heavyTargets = Mathf.Max(s.o.heavyTargets, hitList.Count);
            int aliveBefore = s.Alive;
            // 방출 스냅샷은 피해 직전에 대상 상태를 읽는다(TryCaptureTarget).
            var snap = hitList.Select(x => (x, stacks: x.stacks, frozen: s.element == WeaponElement.Ice && x.frozenUntil > time && x.stacks > 0)).ToList();
            // 어둠 탄막은 강공 피해 전에 잠식 적을 모은다(DarkBarrageScheduler.Submit 주석). 강공으로 죽은 적 몫의 탄도 남는다.
            List<(Target x, int stacks)> darkCollected = null;
            if (s.element == WeaponElement.Dark)
            {
                int reach = Mathf.Max(1, targetsInRadius(radius * ReadDarkRule(s.t).searchMultiplier));
                darkCollected = s.targets.Take(reach).Where(v => v.Alive && v.stacks > 0).Select(v => (v, v.stacks)).ToList();
            }
            foreach (var (x, _, _) in snap) Deal(s, x, direct, 1, time + .0001f);
            float derivedKind(float d) => Outgoing(s, d, PlayerAttackKind.Elemental);
            // 첫 대상이 이 강공에서 받게 될 파생 피해(기대 피해로 먼저 죽었어도 실제로는 이어서 맞는 몫). 처치 확률 판정용.
            var primary = s.targets[0];
            s.o.potentialDerived = 0f;
            if (s.element == WeaponElement.Ice)
            {
                float shatter = derivedKind(CombatBalanceFormulas.IceShatterDamage(blast, Mathf.Max(0f, s.t.shatterBlastFraction)));
                foreach (var (x, _, frozen) in snap)
                {
                    if (frozen && x == primary) s.o.potentialDerived += shatter;
                    if (frozen) { Deal(s, x, shatter, 2, time + .1f); x.stacks = 0; x.frozenUntil = 0f; }
                }
            }
            else if (s.element == WeaponElement.Fire) FireChain(s, blast, snap.Where(v => v.stacks > 0).Select(v => (v.x, v.stacks)).ToList(), time, local);
            else if (s.element == WeaponElement.Electric) LightningChain(s, blast, e, snap.Where(v => v.stacks > 0).Select(v => (v.x, v.stacks)).ToList(), time, local);
            else if (s.element == WeaponElement.Dark) DarkFollowUp(s, blast, e, radius, time, targetsInRadius, darkCollected);
            else if (s.element == WeaponElement.Light)
                for (int hit = firstHit + 1; hit <= 2; hit++)
                {
                    float d = derivedKind(blast * CombatBalanceFormulas.LightTripleHitScale(s.t, hit, radianceAtCommit, overcharge));
                    s.o.potentialDerived += d;
                    int n = Mathf.Max(1, targetsInRadius(radius * s.t.LightTripleRadiusScale(hit)));
                    float at = time + LightTripleImpactScheduler.ResolveDelay(hit, firstHit);
                    int k = 0;
                    foreach (var x in s.targets) { if (k >= n) break; if (!x.Alive) continue; Deal(s, x, d, 2, at); k++; }
                }
            if (s.o.heavyCount == 1f) s.o.firstHeavyKills = aliveBefore - s.Alive;
        }

        static void FireChain(Sim s, float blast, List<(Target x, int stacks)> roots, float time, Func<float, int> inRadius)
        {
            var queue = new Queue<(Target, int, float)>();
            foreach (var r in roots) { r.x.stacks = 0; queue.Enqueue((r.x, r.stacks, time + ElementDischargeBatch.FirePropagationDelay)); }
            int guard = 0;
            while (queue.Count > 0 && guard++ < 512)
            {
                var (origin, stack, at) = queue.Dequeue();
                int s0 = Mathf.Clamp(stack, 1, 5);
                float d = Outgoing(s, CombatBalanceFormulas.FireChainDamage(blast, s0), PlayerAttackKind.Elemental);
                int neighbours = Mathf.Max(0, inRadius(CombatBalanceFormulas.FireChainRadius(s0)) - 1);
                int start = s.targets.IndexOf(origin);
                for (int k = 1, found = 0; k < s.targets.Count && found < neighbours; k++)
                {
                    var x = s.targets[(start + k) % s.targets.Count];
                    if (!x.Alive || x.derivedHits >= 2) continue;
                    found++;
                    int burning = x.stacks;
                    Deal(s, x, d, 2, at); x.derivedHits++;
                    if (burning > 0 && x.Alive) { x.stacks = 0; queue.Enqueue((x, burning, at + ElementDischargeBatch.FirePropagationDelay)); }
                }
            }
        }

        static void LightningChain(Sim s, float blast, float e, List<(Target x, int stacks)> roots, float time, Func<float, int> inRadius)
        {
            bool reachable = inRadius(CombatBalanceFormulas.LightningLinkRadius(e)) > 1;
            foreach (var (origin, stacks) in roots)
            {
                origin.stacks = 0;
                if (!reachable) continue;
                int hops = CombatBalanceFormulas.LightningMaxHops(stacks, e);
                int start = s.targets.IndexOf(origin);
                var visited = new HashSet<Target> { origin };
                for (int hop = 0, k = 1; hop < hops && k < s.targets.Count * 2; k++)
                {
                    var x = s.targets[(start + k) % s.targets.Count];
                    if (!x.Alive || x.derivedHits >= 2 || visited.Contains(x)) continue;
                    visited.Add(x);
                    float d = Outgoing(s, CombatBalanceFormulas.LightningHopDamage(s.t, blast, stacks, hop), PlayerAttackKind.Elemental);
                    Deal(s, x, d, 2, time + ElementDischargeBatch.LightningHopDelay * (hop + 1)); x.derivedHits++;
                    hop++;
                }
            }
        }

        static void DarkFollowUp(Sim s, float blast, float e, float radius, float time, Func<float, int> inRadius, List<(Target x, int stacks)> collected)
        {
            DarkRule rule = ReadDarkRule(s.t);
            int reach = Mathf.Max(1, inRadius(radius * rule.searchMultiplier));
            if (rule.barrage)
            {
                // DarkBarrageScheduler: 강공 피해 전에 모은 잠식 적(가까운 순, 대상 한도)마다 중첩 수만큼 탄, 완충이면 대형탄 1발.
                // 맞힐 적이 이미 죽었으면 탐색 범위 안 다른 적에게 돌린다(Retarget).
                var entries = collected.Take(rule.TargetLimit(e)).ToList();
                float shot = Outgoing(s, blast * rule.shotFraction, PlayerAttackKind.Elemental);
                float finisher = Outgoing(s, blast * rule.finisherFraction, PlayerAttackKind.Elemental);
                float at = time + .63f; // 다른 작업 Play 측정: 첫 명중이 착지 후 약 0.63초(05 10-01 절)
                var shots = new List<(Target x, float damage)>();
                foreach (var (x, n) in entries) { x.stacks = 0; for (int i = 0; i < n; i++) shots.Add((x, shot)); }
                if (e >= .999f) foreach (var (x, _) in entries) shots.Add((x, finisher));
                foreach (var (x, n) in entries) if (x == s.targets[0]) s.o.potentialDerived += shot * n + (e >= .999f ? finisher : 0f);
                foreach (var (x, damage) in shots)
                {
                    var target = x.Alive ? x : s.targets.Take(reach).FirstOrDefault(v => v.Alive);
                    if (target != null) Deal(s, target, damage, 2, at);
                }
            }
            else
            {
                int stackSum = collected.Sum(v => v.stacks);
                float d = Outgoing(s, blast * (rule.burstBase + rule.burstPerStack * Mathf.Min(stackSum, rule.burstCap)), PlayerAttackKind.Elemental);
                foreach (var x in s.targets.Where(v => v.Alive).Take(reach)) { x.stacks = 0; Deal(s, x, d, 2, time + rule.burstTime); }
            }
        }

        // 단일·군집 공통 루프. heavyEnabled=false면 약공만 친다.
        static SimOutcome Run(PlayerBuild p, EnemyView enemy, WeaponItemData weapon, AnalysisConditions c, CombatMode mode, bool heavyEnabled)
        {
            var s = new Sim { p = p, e = enemy, t = OverburstElementTuning.Current, weapon = weapon, element = p.element, c = c };
            int count = mode == CombatMode.Single ? 1 : Mathf.Max(2, c.crowdCount);
            for (int i = 0; i < count; i++) s.targets.Add(new Target { hp = enemy.maxHealth, max = enemy.maxHealth });
            s.alive = count;
            // 군집 입력은 Play로 잴 수 있는 값이다: 약공 판정당 대상 수, 완충 강공 첫 폭발 대상 수(반경² 비례), 연쇄 이웃 밀도.
            // 기본값은 10-01 Play 측정(소형 20마리, 적 AI 켬)의 약공·강공 피해 합으로 맞췄다.
            float maxRadius = Mathf.Max(.1f, s.t.maximumRadius);
            int InRadius(float r) => mode == CombatMode.Single ? 1
                : Mathf.Clamp(Mathf.RoundToInt(c.crowdHeavyTargets * r * r / (maxRadius * maxRadius)), 1, Math.Max(1, s.Alive));
            float weakCarry = 0f;
            int WeakTargets()
            {
                if (mode == CombatMode.Single) return 1;
                weakCarry += Mathf.Max(1f, c.crowdWeakTargets);
                int k = (int)weakCarry; weakCarry -= k;
                return Mathf.Clamp(k, 1, Math.Max(1, s.Alive));
            }
            float packing = Mathf.Max(.1f, c.packingDensity);
            int Local(float r) => mode == CombatMode.Single ? 1 : Mathf.Clamp(Mathf.RoundToInt(packing * Mathf.PI * r * r), 1, Math.Max(1, s.Alive));
            MeleeComboDefinition combo = weapon.GetMeleeComboDefinition();
            int stepCount = combo != null ? combo.StepCount : 0;
            if (stepCount == 0) return s.o;
            float clock = 0f; int step = 0; bool continuation = false; int weakPhases = 0; float weakTargetSum = 0f;
            bool prepDone = false;
            var heavyTiming = TimeHeavy(weapon, p.attackSpeed);
            int guard = 0;
            while (s.Alive > 0 && clock < c.maxSimulationSeconds && guard++ < 4000)
            {
                s.sequence++;
                float speed = CombatBalanceFormulas.ApplyRadianceAttackSpeed(p.stats, s.t, s.radiance).meleeAttackSpeedMultiplier;
                StepTiming timing = TimeComboStep(weapon, step, continuation, speed);
                bool readyThisStep = false; float readyAt = 0f;
                foreach (var ph in timing.phases)
                {
                    float at = clock + ph.time;
                    AdvanceTicks(s, at);
                    if (s.Alive == 0) break;
                    int hit = WeakPhase(s, ph.coefficient, at, WeakTargets());
                    weakPhases++; weakTargetSum += hit;
                    // 충전·준비 생존은 모든 원소 공통으로 기본 게이지 100 기준(설계 목표와 같은 기준)이다.
                    if (s.o.heavyCount == 0f && float.IsNaN(s.o.chargeTime) && OverburstElementRules.IsActive(s.element) && s.energy >= s.BaseMax - .01f)
                    {
                        s.o.chargeTime = at; s.o.chargePhases = weakPhases;
                        s.o.prepDamage = s.o.weak + s.o.dot;
                        s.o.prepHealthLeft = s.targets[0].Alive ? s.targets[0].hp / s.targets[0].max : 0f;
                        s.o.prepSurvived = s.targets[0].Alive;
                    }
                    if (!prepDone && heavyEnabled && PrepReady(s))
                    {
                        prepDone = true; readyThisStep = true; readyAt = at;
                        if (s.o.heavyCount == 0f && s.element == WeaponElement.Light && s.c.lightTriple) s.o.tripleTime = at;
                        break;
                    }
                }
                if (s.Alive == 0) break;
                if (readyThisStep)
                {
                    float heavyStart = Mathf.Max(readyAt, clock + timing.cancelAt);
                    AdvanceTicks(s, heavyStart + heavyTiming.impactAt);
                    if (s.Alive == 0) break;
                    bool firstHeavy = s.o.heavyCount == 0f;
                    float hpBefore = s.targets[0].hp;
                    Heavy(s, heavyTiming, heavyStart + heavyTiming.impactAt, InRadius, Local);
                    if (firstHeavy && mode == CombatMode.Single)
                    {
                        s.o.heavyKilled = !s.targets[0].Alive;
                        // 첫 강공 처치 확률: 비치명으로도 죽으면 1, 치명일 때만 죽으면 치명 확률, 치명으로도 못 죽이면 0.
                        // 파생은 기대 피해로 먼저 쓰러졌는지와 무관하게 이 대상이 받을 몫(potentialDerived)으로 본다.
                        float derived = s.o.potentialDerived;
                        float normal = Outgoing(s, s.o.heavyNormal, WithElement(s, PlayerAttackKind.Heavy));
                        float critical = Outgoing(s, s.o.heavyCritical, WithElement(s, PlayerAttackKind.Heavy));
                        s.o.heavyKillChance = hpBefore <= normal + derived ? 1f : hpBefore <= critical + derived ? s.Crit01 : 0f;
                    }
                    clock = heavyStart + heavyTiming.endAt;
                    prepDone = false; step = 0; continuation = false;
                    continue;
                }
                // 약공을 누른 채면 다음 입력 구간 시작에서 다음 타로 넘어간다(남은 판정은 취소). 4타 뒤에는 1타로 돌아간다.
                clock += timing.chainAt;
                step = (step + 1) % stepCount;
                continuation = true;
            }
            AdvanceTicks(s, clock);
            s.o.weakTargets = weakPhases > 0 ? weakTargetSum / weakPhases : 0f;
            // 약공 1판정·4타 1순환 기준값
            float bd = p.attack * CombatBalanceFormulas.AttackDamageMultiplier(weapon, HeavyDefinition(weapon), false, false);
            s.o.weakNormal = Outgoing(s, CombatBalanceFormulas.RoundedHitDamage(bd, false, p.critDamage), WithElement(s, PlayerAttackKind.Weak));
            s.o.weakCrit = Outgoing(s, CombatBalanceFormulas.RoundedHitDamage(bd, true, p.critDamage), WithElement(s, PlayerAttackKind.Weak));
            s.o.weakExpected = Mathf.Lerp(s.o.weakNormal, s.o.weakCrit, s.Crit01);
            float cycleDamage = 0f, cycleTime = 0f;
            for (int i = 0; i < stepCount; i++)
            {
                var st = TimeComboStep(weapon, i, true, p.attackSpeed);
                foreach (var ph in st.phases)
                    cycleDamage += Outgoing(s, ExpectedRounded(p.attack * ph.coefficient * CombatBalanceFormulas.AttackDamageMultiplier(weapon, HeavyDefinition(weapon), false, false),
                        s.Crit01, p.critDamage), WithElement(s, PlayerAttackKind.Weak));
                cycleTime += st.chainAt;
            }
            s.o.cycleDuration = cycleTime;
            s.o.weakDps = cycleTime > 0f ? cycleDamage / cycleTime : 0f;
            if (mode == CombatMode.Single && s.o.heavyCount == 0f && float.IsNaN(s.o.chargeTime)) { s.o.prepSurvived = false; }
            return s.o;
        }

        public static SimOutcome Simulate(PlayerBuild p, EnemyView enemy, WeaponItemData weapon, AnalysisConditions c, CombatMode mode)
            => Run(p, enemy, weapon, c, mode, true);

        public static float WeakOnlyKillTime(PlayerBuild p, EnemyView enemy, WeaponItemData weapon, AnalysisConditions c)
            => Run(p, enemy, weapon, c, CombatMode.Single, false).killTime;

        // 약공을 계속 누를 때 n번째 적중 판정이 나오는 시각(첫 입력 = 0초). 충전 시간 실측을 판정 수로 대조할 때 쓴다.
        public static float WeakPhaseTime(WeaponItemData weapon, float attackSpeed, int n)
        {
            var combo = weapon.GetMeleeComboDefinition();
            if (combo == null || combo.StepCount == 0 || n <= 0) return float.NaN;
            float clock = 0f; int count = 0;
            for (int i = 0; i < 64; i++)
            {
                int step = i % combo.StepCount;
                var t = TimeComboStep(weapon, step, i > 0, attackSpeed);
                foreach (var ph in t.phases) if (++count == n) return clock + ph.time;
                clock += t.chainAt;
            }
            return float.NaN;
        }

        // 적이 죽지 않는다고 볼 때 기본 게이지가 가득 차는 시간과 Phase 수(처치 여부와 무관한 순수 충전).
        // 빛은 tripleTime에 에너지 200·광휘 100(3연타 준비)까지의 시간을 함께 준다.
        public static SimOutcome ChargeOnly(PlayerBuild p, EnemyView enemy, WeaponItemData weapon, AnalysisConditions c)
        {
            var immortal = new EnemyView { id = enemy.id, name = enemy.name, grade = enemy.grade, enemyClass = enemy.enemyClass,
                level = enemy.level, maxHealth = 1e9f };
            var copy = c.Clone(); copy.maxSimulationSeconds = 40f; copy.lightTriple = true;
            return Run(p, immortal, weapon, copy, CombatMode.Single, true);
        }
    }
}
