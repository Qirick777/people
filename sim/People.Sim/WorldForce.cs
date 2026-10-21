namespace People.Sim;

/// <summary>
/// 세력 개편 R9(plan_reform): 무력.
/// - 자경단·군대·용병단·도적 떼는 무력 하나로 세운다. 일은 지킴(장소), 공격(대상), 호송, 용병이다.
///   공격 대상: 범죄자(쫓기), 적대 세력(치기), 민간인(털기). 이름은 가장 무거운 일이 붙인다(경비대·자경단·치안대·군대·도적 떼·용병단).
/// - 공격 뒤에 포획과 사살 가운데 하나를 고른다(갈래마다). 사살은 맞선 사람이 죽을 확률을 1.5배, 포획은 0.6배로 한다.
///   포획한 사람을 붙잡아 두는 것은 R10에서 붙인다. NPC 우두머리는 공격성 0.6을 넘으면 사살을 고른다.
///   범죄자를 쫓는 갈래가 사살이면 잡은 범인의 30%를 그 자리에서 죽인다(평판 −0.005, 공포 +0.01).
/// - 적대 세력: 다섯 해 안의 전쟁 상대, 지배자가 의심 50 넘게 쌓은 상대, 직접 지정한 집. 그 집과 그 나무의 집만 친다.
///   치기는 털기와 같은 싸움이지만 죄가 되지 않고, 평판을 깎지 않는다(이름 +0.03, 공포 +0.02). 거리 제한이 없다.
/// - 기득권이 보는 무게는 공격 대상으로 정한다: 민간인 1.3, 적대 세력 1.2, 용병 1.2, 지킴 1, 범죄자 0.9.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<(int a, int b), int> _warFoes = new();   // 전쟁한 두 집 → 그날

    public static bool IsForce(GroupKind k) => k is GroupKind.Watch or GroupKind.Bandits or GroupKind.Army or GroupKind.Sellswords;
    public static bool IsAttackVerb(Verb v) => v is Verb.Chase or Verb.Attack or Verb.Raid;

    public void SetAttackMode(Group g, bool kill) => g.Kill = kill;

    public void SetHostile(Group root, int house, bool on)
    {
        root = RootOf(root);
        if (on) root.Hostile.Add(house); else root.Hostile.Remove(house);
    }

    private void MarkWar(int a, int b) => _warFoes[(Math.Min(a, b), Math.Max(a, b))] = Day;

    /// <summary>맨 위 조직 root 의 적대 집과 그 까닭.</summary>
    public Dictionary<int, string> HostilesOf(Group root)
    {
        root = RootOf(root);
        var hh = Households;
        var d = new Dictionary<int, string>();
        int me = LandHomeOf(root);
        foreach (int h in root.Hostile) if (hh.IsAlive(h) && h != me) d[h] = "지정";
        if (me < 0) return d;
        foreach (var ((a, b), day) in _warFoes)
        {
            if (Day - day > 5 * Config.DaysPerYear) continue;
            int foe = a == me ? b : b == me ? a : -1;
            if (foe >= 0 && hh.IsAlive(foe)) d.TryAdd(foe, "전쟁 상대");
        }
        foreach (var r in _rivals.Values)
            if (r.Lord == me && r.Suspicion >= 50f && hh.IsAlive(r.Riser) && r.Riser != me) d.TryAdd(r.Riser, "의심");
        return d;
    }

    private bool IsHostileHouse(ICollection<int> foes, int h)
    {
        if (foes.Contains(h)) return true;
        int l = PatronOf(h);
        if (l >= 0 && foes.Contains(l)) return true;
        foreach (int f in foes) if (InTreeOf(h, f)) return true;
        return false;
    }

    /// <summary>적대 집이 사는 마을, 가까운 순.</summary>
    public List<int> HostileVillages(Group root)
    {
        var foes = HostilesOf(root).Keys.ToList();
        var hh = Households; var vl = VillageList;
        var list = new List<int>();
        if (foes.Count == 0) return list;
        for (int v = 0; v < vl.Count; v++)
            if (vl.Alive[v] && VillageHouses(v).Any(h => IsHostileHouse(foes, h))) list.Add(v);
        int from = root.Village;
        if (from >= 0 && from < vl.Count)
            list.Sort((x, y) => Dist(x).CompareTo(Dist(y)));
        return list;
        float Dist(int v) => MathF.Pow(vl.CenterX[v] - vl.CenterX[from], 2) + MathF.Pow(vl.CenterY[v] - vl.CenterY[from], 2);
    }

    /// <summary>계절마다: 치기 일을 맡은 갈래가 적대 세력의 집을 친다.</summary>
    private void AttackSeason()
    {
        foreach (var g in _groups.ToList())
        {
            if (!g.Alive) continue;
            float w = 0f; int village = -1;
            foreach (var t in g.Tasks) if (t.Verb == Verb.Attack) { w += t.Weight; village = t.Village; }
            if (w <= 0f) continue;
            int leader = FindAlive(g.LeaderUid);
            if (leader < 0 || (IsPlayer(leader) && !g.Manual)) continue;
            var foes = HostilesOf(g).Keys.ToList();
            if (foes.Count == 0) continue;
            if (village < 0 || !VillageHouses(village).Any(h => IsHostileHouse(foes, h)))
            {
                var vs = HostileVillages(g);
                if (vs.Count == 0) continue;
                village = vs[0];
            }
            for (int k = 0, tries = 1 + g.Members / 4; k < tries; k++)
            {
                if (!g.Alive || !_rng.Chance(0.5f * w * WorkFactor(g) * g.CmdFactor)) continue;
                leader = FindAlive(g.LeaderUid);
                if (leader < 0 || !Households.IsAlive(People.Household[leader])) break;
                Stats.Attacks++;
                TryRaid(People.Household[leader], 10f + 8f * g.Members, g, village, h => IsHostileHouse(foes, h), true);
            }
        }
    }

    /// <summary>싸움에서 맞선 사람이 죽을 배율: 사살 1.5, 포획 0.6, 떼가 아니면 1.</summary>
    private static float KillMult(Group? gang) => gang == null ? 1f : gang.Kill ? 1.5f : 0.6f;

    /// <summary>잡은 범인을 그 자리에서 죽였나(범죄자를 쫓는 갈래가 사살일 때 30%).</summary>
    private bool ChaseKill(Crime c, int culprit)
    {
        if (c.Village < 0 || c.Lord < 0) return false;
        bool kill = false;
        var root = LordOrg(c.Lord, false);
        if (root != null) kill = Subtree(root).Any(u => u.Kill && u.Tasks.Any(t => t.Verb == Verb.Chase && t.Village == c.Village));
        if (!kill) kill = _groups.Any(g => g.Alive && g.Home < 0 && g.Kill && g.Village == c.Village && g.Tasks.Any(t => t.Verb == Verb.Chase && t.Village == c.Village));
        if (!kill || !_rng.Chance(0.3f)) return false;
        Chron(culprit, $"{SubjName(culprit)} {Ro(CrimeName(c.Type))} 쫓기다 그 자리에서 죽었다");
        Stats.ChaseKills++;
        AddRepLord(c.Lord, 0f, -0.005f, 0.01f);
        Die(culprit, false);
        return true;
    }

    /// <summary>공격 대상으로 정한 기득권 눈의 무게. 무력 일이 없으면 -1.</summary>
    private float ForceWeight(Group root)
    {
        float s = 0f, n = 0f;
        foreach (var u in Subtree(root))
        {
            if (u != root && RootOf(u).Id != root.Id) continue;
            foreach (var t in u.Tasks)
            {
                float f = t.Verb switch { Verb.Raid => 1.3f, Verb.Attack or Verb.Mercenary => 1.2f, Verb.Guard or Verb.Escort => 1f, Verb.Chase => 0.9f, _ => -1f };
                if (f < 0f) continue;
                s += f * t.Weight * MathF.Max(1, u.Members); n += t.Weight * MathF.Max(1, u.Members);
            }
        }
        return n > 0f ? s / n : -1f;
    }

    // 시험용
    public void AttackNow() => AttackSeason();
    public void MarkWarNow(int a, int b) => MarkWar(a, b);
}
