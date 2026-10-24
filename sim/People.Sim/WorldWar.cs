namespace People.Sim;

/// <summary>
/// 홀로 선 지배자끼리의 전쟁. 선포하면 분쟁이 서고(C3, WorldConflict), 싸움으로 결말이 난다.
/// 이기면 상대가 내 봉신이 되고(땅과 아래 집은 그대로 거느린다), 지면 내 이름이 깎이고 경계의 집 몇이 상대에게 간다. 한 해를 끌면 휴전(다섯 해).
/// WarOdds(힘 = 제 힘 + 바로 아래 봉신 힘의 0.6, 동맹 0.3, 제곱 비)는 이제 NPC가 싸움을 걸지 정하는 어림이다.
/// </summary>
public sealed partial class World
{
    private float RealmPower(int lord)
    {
        float s = LordPower(lord);
        foreach (int v in DirectClients(lord)) if (Households.IsAlive(v) && ClientCount(v) > 0) s += 0.6f * LordPower(v);
        return s;
    }

    /// <summary>
    /// 싸움에 실제로 모일 힘의 어림(C7): 제 힘 + 봉신마다 (소집에 응할 확률 × 그 힘) + 동맹마다 (올 확률 × 친위대 뺀 군인 절반).
    /// 막는 쪽이면 아래 보통 집의 향병(0.3 + 0.5 × 충성 확률)과 거점 보정(1 + 0.15 × 큰 집 단계)을 더한다.
    /// </summary>
    private float MusterPower(int lord, bool defending)
    {
        var p = People; var hh = Households;
        int head = HeadIdx(lord);
        float s = LordPower(lord);
        foreach (int v in DirectClients(lord))
        {
            if (!hh.IsAlive(v)) continue;
            int vh = HeadIdx(v);
            if (vh < 0) continue;
            float loy = head >= 0 && p.LoyalUid[vh] == p.Uid[head] ? p.Loyalty[vh] : 0.3f;
            if (ClientCount(v) > 0)
            {
                float answer = Math.Clamp(loy + (IsTitled(v) ? 0.2f : 0f) + (NationOf(lord) != null ? 0.1f : 0f) - 0.3f * MathF.Min(1f, MathF.Sqrt(Dist2(v, lord)) / Config.VassalRange), 0f, 0.95f);
                s += answer * LordPower(v);
            }
            else if (defending && Dist2(v, lord) <= 4f * Config.ProtectRange * Config.ProtectRange)
                s += (0.3f + 0.5f * loy) * HouseholdPower(v);
        }
        foreach (var x in _pacts)
        {
            if (x.k != PactKind.Alliance) continue;
            int o = x.a == lord ? x.b : x.b == lord ? x.a : -1;
            if (o >= 0 && hh.IsAlive(o)) s += 0.5f * 0.5f * 0.7f * LordPower(o);
        }
        return defending ? s * (1f + 0.15f * ResidenceOf(lord)) : s;
    }

    public float WarOdds(int a, int t)
    {
        float x = MusterPower(a, false), y = MusterPower(t, true);
        return x * x / (x * x + y * y + 0.01f);
    }

    private bool CanWar(int a, int t)
    {
        var hh = Households;
        float r = 4f * Config.ProtectRange;
        return hh.IsAlive(a) && hh.IsAlive(t) && a != t && PatronOf(a) < 0 && PatronOf(t) < 0 && TierOf(a) >= 1 && TierOf(t) >= 1 && Dist2(a, t) <= r * r && !HasPact(a, t)   // 약조를 맺은 사이는 싸우지 않는다(R14)
            && !InTruce(a, t) && !InConflict(a) && !InConflict(t);   // 휴전 중이거나 이미 싸우는 중이면 못 건다(C3)
    }

    private readonly Dictionary<(int, int), int> _truce = new();
    private bool InTruce(int a, int b) => _truce.TryGetValue((Math.Min(a, b), Math.Max(a, b)), out int d) && Day - d < 5 * Config.DaysPerYear;

    /// <summary>전쟁의 결말(C3: 분쟁이 끝난 뒤). 사람은 싸움에서 이미 잃었다. won: 일으킨 쪽이 이겼다.</summary>
    private void WarResult(int ah, int th, bool won, string sum)
    {
        var hh = Households; var p = People;
        int a = p.Household[ah], t = p.Household[th];
        var c = new InteractionContext { Actor = ah, Target = th };
        if (won)
        {
            Stats.WarWins++;
            AddRepLord(a, 0.05f, 0f, 0.05f); AddRepLord(t, 0.02f, 0f, 0f);
            if (!WouldLoop(t, a)) { _patron[t] = a; _patronFounded[t] = hh.FoundedDay[a]; EnsureRealmArrays(); _legit[t] = 0; Resnap(t); }
            if (p.IsAliveIndex(th)) { Social.Add(p, th, p.Uid[ah], -40f, 40f); p.Honor[th] -= 0.2f; }
            RecountClients(); RecountTree();
            RiseChron(c, x => $"{x}와 싸워 이겼다. {Subj(x)} 봉신이 되었다{sum}", x => $"{x}와 싸워 져서 그의 봉신이 되었다{sum}");
        }
        else
        {
            if (p.IsAliveIndex(ah)) p.Honor[ah] -= 0.1f;
            if (p.IsAliveIndex(th)) Social.Add(p, th, p.Uid[ah], -50f);
            int moved = 0;
            for (int h = 0; h < hh.HighWater && moved < 5; h++)
            {
                if (!hh.IsAlive(h) || PatronOf(h) != a || Dist2(h, t) > Config.ProtectRange * Config.ProtectRange) continue;
                if (!_rng.Chance(0.3f * LeaveFactor(h))) continue;
                _patron[h] = t; _patronFounded[h] = hh.FoundedDay[t]; Resnap(h);
                moved++;
            }
            RecountClients();
            RiseChron(c, x => $"{x}에게 싸움을 걸었다가 졌다(집 {moved}곳을 잃음){sum}", x => $"{x}의 침공을 물리쳤다{sum}");
        }
    }

    /// <summary>휴전: 다섯 해 동안 서로 싸움을 걸지 않는다.</summary>
    private void Truce(int a, int t, string sum)
    {
        Stats.WarTruces++;
        _truce[(Math.Min(a, t), Math.Max(a, t))] = Day;
        int ah = HeadIdx(a), th = HeadIdx(t);
        if (ah >= 0) Chron(ah, $"한 해를 싸우고도 결판이 나지 않아 휴전했다{sum}");
        if (th >= 0) Chron(th, $"한 해를 버텨 휴전을 받아 냈다{sum}");
    }

    /// <summary>해마다: 거친 지배자가 가까운 약한 지배자에게 드물게 싸움을 건다.</summary>
    private void WarYear()
    {
        var hh = Households;
        for (int a = 0; a < hh.HighWater; a++)
        {
            if (!hh.IsAlive(a) || PatronOf(a) >= 0 || TierOf(a) < 1) continue;
            int ah = HeadIdx(a);
            if (ah < 0 || IsPlayer(ah)) continue;
            float agg = HeadTrait(a, Trait.Aggression);
            if (agg < 0.6f || !_rng.Chance(0.03f)) continue;
            int best = -1; float bo = 0.75f - 0.25f * (agg - 0.6f);   // 거칠수록 덜 확실해도 건다(공격성 1이면 0.65)
            for (int t = 0; t < hh.HighWater; t++)
            {
                if (!CanWar(a, t)) continue;
                float o = WarOdds(a, t);
                if (o > bo) { bo = o; best = t; }
            }
            if (best >= 0 && HeadIdx(best) is int th && th >= 0) Interact(InteractionId.War, new InteractionContext { Actor = ah, Target = th, Third = -1 });
        }
    }

    private void AddWarDefs(InteractionDef[] d)
    {
        int H(World w, int i) => w.People.Household[i];
        d[(int)InteractionId.War] = new InteractionDef
        {
            // 선포하면 분쟁이 선다(C3). 결말은 싸움으로 정해진다
            Id = InteractionId.War, Name = "전쟁", Category = InteractionCategory.Rule, Harsh = true,
            Can = (w, c) => w.HeadIdx(H(w, c.Actor)) == c.Actor && w.HeadIdx(H(w, c.Target)) == c.Target && w.CanWar(H(w, c.Actor), H(w, c.Target)),
            Accept = (w, c) => 1f,
            Success = (w, c, _) => w.StartWar(H(w, c.Actor), H(w, c.Target)),
        };
    }
}
