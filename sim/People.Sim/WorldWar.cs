namespace People.Sim;

/// <summary>
/// 홀로 선 지배자끼리의 전쟁. 이기면 상대가 내 봉신이 되고(땅과 아래 집은 그대로 거느린다), 지면 내 이름이 깎이고 경계의 집 몇이 상대에게 간다.
/// 두 쪽 다 군인을 잃는다. 힘 = 제 힘 + 바로 아래 봉신 힘의 0.6. 이길 확률은 힘의 제곱 비.
/// </summary>
public sealed partial class World
{
    private float RealmPower(int lord)
    {
        float s = LordPower(lord);
        foreach (int v in DirectClients(lord)) if (Households.IsAlive(v) && ClientCount(v) > 0) s += 0.6f * LordPower(v);
        return s;
    }

    public float WarOdds(int a, int t)
    {
        float x = RealmPower(a), y = RealmPower(t);
        return x * x / (x * x + y * y + 0.01f);
    }

    private bool CanWar(int a, int t)
    {
        var hh = Households;
        float r = 4f * Config.ProtectRange;
        return hh.IsAlive(a) && hh.IsAlive(t) && a != t && PatronOf(a) < 0 && PatronOf(t) < 0 && TierOf(a) >= 1 && TierOf(t) >= 1 && Dist2(a, t) <= r * r;
    }

    /// <summary>군인 가운데 frac 만큼이 죽는다.</summary>
    private int WarLosses(int lord, float frac)
    {
        var p = People;
        var dead = StaffOf(lord).Where(i => p.IsAliveIndex(i) && p.JobOf[i] == (byte)Job.Soldier && _employer.GetValueOrDefault(i, -1) == lord && _rng.Chance(frac)).ToList();
        foreach (int i in dead) { Stats.RaidDeaths++; Die(i, false); }
        return dead.Count;
    }

    private void WarResult(int ah, int th, bool won)
    {
        var hh = Households; var p = People;
        int a = p.Household[ah], t = p.Household[th];
        Stats.Wars++;
        int la = WarLosses(a, won ? 0.1f : 0.25f), lt = WarLosses(t, won ? 0.25f : 0.1f);
        var c = new InteractionContext { Actor = ah, Target = th };
        if (won)
        {
            Stats.WarWins++;
            AddRepLord(a, 0.05f, 0f, 0.05f); AddRepLord(t, 0.02f, 0f, 0f);
            if (!WouldLoop(t, a)) { _patron[t] = a; _patronFounded[t] = hh.FoundedDay[a]; EnsureRealmArrays(); _legit[t] = 0; Resnap(t); }
            if (p.IsAliveIndex(th)) { Social.Add(p, th, p.Uid[ah], -40f, 40f); p.Honor[th] -= 0.2f; }
            RecountClients(); RecountTree();
            RiseChron(c, x => $"{x}와 싸워 이겼다. {Subj(x)} 봉신이 되었다(군인 {la}명을 잃음)", x => $"{x}와 싸워 져서 그의 봉신이 되었다(군인 {lt}명을 잃음)");
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
            RiseChron(c, x => $"{x}에게 싸움을 걸었다가 졌다(군인 {la}명, 집 {moved}곳을 잃음)", x => $"{x}의 침공을 물리쳤다(군인 {lt}명을 잃음)");
        }
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
            int best = -1; float bo = 0.7f;
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
            // 받아들임 = 이겼다
            Id = InteractionId.War, Name = "전쟁", Category = InteractionCategory.Rule, Harsh = true,
            Can = (w, c) => w.HeadIdx(H(w, c.Actor)) == c.Actor && w.HeadIdx(H(w, c.Target)) == c.Target && w.CanWar(H(w, c.Actor), H(w, c.Target)),
            Accept = (w, c) => w.WarOdds(H(w, c.Actor), H(w, c.Target)),
            Success = (w, c, _) => w.WarResult(c.Actor, c.Target, true),
            Fail = (w, c) => w.WarResult(c.Actor, c.Target, false),
        };
    }
}
