namespace People.Sim;

/// <summary>
/// 충성. 대상은 가문이고, 관계는 그 가문의 지금 가장과 맺는다.
/// - 따르는 집의 가장은 보호해 주는 지배자 가문, 군인·징수관은 고용한 가문, 나머지 어른은 자기 집 가장의 가문.
/// - 관계가 충성을 민다. 보통 관계에서는 거의 움직이지 않고, 아주 좋거나 아주 나쁘면 빠르게 움직인다.
/// - 충성이 관계를 받아들이는 눈을 바꾼다(Social.Add). 높으면 좋은 일은 크게, 나쁜 일은 작게 느낀다.
/// - 광신에 이르면 대상과의 관계가 깎이지도 흐려지지도 않는다. 나쁜 일은 의심으로 쌓이고, 의심이 넘치면 광신이 깨지며 원한으로 뒤집힌다.
/// </summary>
public sealed partial class World
{
    private readonly HashSet<int> _raidedHouses = new();   // 지난 한 해 털린 집(보호받던 집이면 지배자를 원망한다)

    private enum LoyalKind : byte { None, Lord, Employer, Household }

    /// <summary>i 가 충성할 가구와 그 종류.</summary>
    private (int target, LoyalKind kind) LoyaltyTarget(int i)
    {
        var p = People;
        var hh = Households;
        if (_employer.TryGetValue(i, out int l) && hh.IsAlive(l)) return (l, LoyalKind.Employer);
        int h = p.Household[i];
        if (!hh.IsAlive(h)) return (-1, LoyalKind.None);
        if (hh.Head[h].Index == i && p.IsValid(hh.Head[h]))
        {
            int lord = PatronOf(h);
            return lord >= 0 ? (lord, LoyalKind.Lord) : (-1, LoyalKind.None);
        }
        return (h, LoyalKind.Household);
    }

    private void LoyaltyYear()
    {
        var p = People;
        var hh = Households;
        float clientSum = 0f;
        int clientN = 0, fanatics = 0;
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i) || AgeYears(i) < 16) continue;
            var (t, kind) = LoyaltyTarget(i);
            // 광신자는 자리가 바뀌어도(군인에서 물러나거나 가장이 되어도) 섬기던 집이 살아 있으면 그 집을 계속 섬긴다
            int old = p.LoyalHouse[i];
            if (p.Fanatic[i] != 0 && old >= 0 && old != t && hh.IsAlive(old) && hh.FoundedDay[old] == p.LoyalHouseFounded[i]
                && p.IsValid(hh.Head[old]) && hh.Head[old].Index != i)
            {
                t = old;
                kind = LoyalKind.Household;
            }
            var head = t >= 0 ? hh.Head[t] : PersonId.None;
            if (t < 0 || !p.IsValid(head) || head.Index == i)
            {
                if (p.Fanatic[i] != 0) Stats.FanaticsLost++;
                p.LoyalFam[i] = p.LoyalUid[i] = p.LoyalHouse[i] = -1;
                p.Fanatic[i] = 0; p.Doubt[i] = 0f;
                continue;
            }
            int hi = head.Index, hu = p.Uid[hi];
            p.LoyalIdx[i] = hi;
            int fam = Lineage.FamilyOf(hu);
            // 같은 집이 이어지거나(과부가 가장이 되어도) 같은 가문의 다른 집이 이으면 같은 섬김이다
            bool sameHouse = t == p.LoyalHouse[i] && hh.FoundedDay[t] == p.LoyalHouseFounded[i];
            bool same = sameHouse || (p.LoyalFam[i] >= 0 && fam == p.LoyalFam[i]);
            p.LoyalHouse[i] = t;
            p.LoyalHouseFounded[i] = hh.FoundedDay[t];
            if (p.Male[hi] || !same) p.LoyalFam[i] = fam;   // 가문은 성을 잇는 남자 가장으로 적는다
            if (!same)
            {
                if (p.Fanatic[i] != 0) Stats.FanaticsLost++;
                // 새로 섬기는 가문: 처음부터 다시
                p.LoyalUid[i] = hu;
                p.LoyalSince[i] = Day;
                p.Loyalty[i] = 0.5f;
                p.Fanatic[i] = 0; p.Doubt[i] = 0f;
            }
            else if (hu != p.LoyalUid[i])
            {
                // 같은 가문의 새 가장. 광신은 가문에 바친 것이라 새 가장에게도 옮겨 간다.
                p.LoyalUid[i] = hu;
                if (p.Fanatic[i] != 0) Social.Add(p, i, hu, 50f);
            }

            // 한 해 동안 섬긴 대상을 보는 눈
            float gain = kind switch
            {
                LoyalKind.Lord => 2f + 2f * MathF.Min(2f, DangerAt(hh.Village[p.Household[i]])),
                LoyalKind.Employer => 4f,
                _ => 2f + 2f * DoctrineOn(i, Doctrine.Ancestors),   // 조상 숭배 교리: 집안을 더 섬긴다
            };
            gain += 6f * (p.Get(hi, Trait.Charisma) - 0.5f) + 6f * p.Honor[hi];
            if (Surplus(p.Household[i]) < 0.6f) gain -= kind == LoyalKind.Lord ? 6f : 3f;
            Social.Add(p, i, hu, gain);

            if (p.Fanatic[i] != 0)
            {
                p.Doubt[i] *= Config.DoubtKeep;
                if (p.Doubt[i] >= Config.FanaticBreak) BreakFanatic(i, hu);
                else p.Loyalty[i] += 0.02f * (1f - p.Loyalty[i]);
            }
            else
            {
                float x = Math.Clamp(Social.Opinion(p, i, hu) / Config.LoyaltyScale, -1f, 1f);
                float f = (Config.LoyaltyLinear * x + Config.LoyaltyCubic * x * x * x) * (1.3f - 0.6f * p.Get(i, Trait.Stubbornness));
                p.Loyalty[i] = Math.Clamp(p.Loyalty[i] + (f > 0f ? f * (1f - p.Loyalty[i]) : f * p.Loyalty[i]), 0f, 1f);
                // 광신은 섬기는 사람을 직접 겪어야 생긴다: 같은 집, 고용된 사람, 지배자 조직의 측근.
                // 멀리서 섬기는 따르는 집 가장은 교리와 좋은 관계로 광신 바로 아래까지만 간다
                if (p.Loyalty[i] >= Config.FanaticAt && kind == LoyalKind.Lord && !(LordOrg(t, false)?.Inner.Contains(p.Uid[i]) ?? false))
                    p.Loyalty[i] = Config.FanaticAt - 0.01f;
                if (p.Loyalty[i] >= Config.FanaticAt) { p.Fanatic[i] = 1; p.Doubt[i] = 0f; Stats.NewFanatics++; Stats.FanaticYearsSum += (Day - p.LoyalSince[i]) / Config.DaysPerYear; }
            }
            if (p.Fanatic[i] != 0) fanatics++;
            if (kind == LoyalKind.Lord) { clientSum += p.Loyalty[i]; clientN++; }
        }
        Stats.ClientLoyalty = clientN > 0 ? clientSum / clientN : 0f;
        Stats.FanaticCount = fanatics;
        _raidedHouses.Clear();
    }

    /// <summary>광신이 깨진다: 쌓인 의심이 원한으로 뒤집히고 충성이 떨어진다.</summary>
    private void BreakFanatic(int i, int uid)
    {
        var p = People;
        float doubt = MathF.Min(100f, p.Doubt[i]);
        p.Fanatic[i] = 0;
        p.Doubt[i] = 0f;
        p.Loyalty[i] = Config.FanaticFallTo;
        int s = Social.Ensure(p, i, uid);
        p.RelAff[s] = -doubt;
        Social.Remember(p, i, MemoryType.Betrayed, uid, Day, -80);
        Stats.FanaticsBroken++;
    }

    /// <summary>
    /// 따르는 집이 떠날 마음을 얼마나 내는가(기본 확률에 곱한다). 충성 0.5면 1, 높을수록 작고, 광신이면 0.
    /// </summary>
    private float LeaveFactor(int h)
    {
        var head = Households.Head[h];
        if (!People.IsValid(head)) return 1f;
        int i = head.Index;
        if (People.LoyalUid[i] < 0) return 1f;
        if (People.Fanatic[i] != 0) return 0f;
        return 1.6f - 1.2f * People.Loyalty[i];
    }

    /// <summary>충성이 아주 낮은 가장은 위험과 상관없이 공납을 그만둔다.</summary>
    private bool Disloyal(int h)
    {
        var head = Households.Head[h];
        if (!People.IsValid(head) || People.LoyalUid[head.Index] < 0) return false;
        float l = People.Loyalty[head.Index] - 0.1f * DoctrineOn(head.Index, Doctrine.Equality);   // 평등 교리: 공납을 끊기 쉽다
        return l < 0.25f && _rng.Chance((0.25f - l) * 1.2f);
    }

    /// <summary>징수관이 떼어 먹는 몫에 곱한다. 충성할수록 덜 떼어 먹는다.</summary>
    private float SkimFactor(int person)
    {
        if (People.Fanatic[person] != 0) return 0.3f;
        return People.LoyalUid[person] < 0 ? 1f : 1.5f - People.Loyalty[person];
    }
}
