namespace People.Sim;

/// <summary>
/// 세력 개편 R2(plan_reform): 세력의 인지도(0~1), 평판(−1~1), 공포(0~1).
/// - 홀로 선 조직은 맨 위 조직에, 지배자 세력은 지배자 집에 적는다(예전 공포·신용을 넓혔다. 신용 = 0.3 + 평판).
/// - 인지도: 해마다 √(사람 수) ÷ 20 + 드러난 무력 ÷ 100 쪽으로 10%씩 간다. 공개 정도가 상한(숨김 0.1, 반쯤 숨김 0.25, 중립 0.4).
///   큰 사건이 조금씩 올린다. 평판과 공포는 해마다 5%씩 0 쪽(공포는 0.1 쪽)으로 돌아간다.
/// - 사건: 구휼 +0.02, 털린 마을을 지켜 냄 +0.01, 털기 −0.05(사람이 죽으면 −0.1 더), 독립 선언·떼어 나가기(서약 어김) −0.15·−0.1, 전쟁에서 이김 공포 +0.05.
/// - 효과: 스스로 드는 확률 × (1 + 0.8 × 인지도 × 평판), 세력 우두머리가 거는 거래·구애·혼담 + 0.15 × 인지도 × 평판,
///   갈취·위협 + 0.2 × 공포, 털 집 고르기 무게 × (1 − 0.6 × 지켜 주는 지배자의 인지도 × 평판),
///   기득권이 느끼는 위협 × (1 + 2 × 악평) × (1 − 0.5 × 좋은 평판). 악평 = max(0, −평판) × 인지도.
/// </summary>
public sealed partial class World
{
    // 지배자 집 → 인지도·평판·공포 (세운 날로 같은 집인지 본다)
    private readonly Dictionary<int, (int founded, float fame, float standing, float dread)> _lordRep = new();

    public (float fame, float standing, float dread) RepOfLord(int lord)
    {
        if (_lordRep.TryGetValue(lord, out var r) && Households.IsAlive(lord) && Households.FoundedDay[lord] == r.founded) return (r.fame, r.standing, r.dread);
        return (0f, 0f, 0.1f);   // 처음: 신용 0.3(평판 0), 공포 0.1(예전과 같다)
    }

    public (float fame, float standing, float dread) RepOfRoot(Group root)
    {
        root = RootOf(root);
        return root.Home >= 0 ? RepOfLord(root.Home) : (root.Fame, root.Standing, root.Dread);
    }

    private void AddRepLord(int lord, float fame, float standing, float dread)
    {
        if (!Households.IsAlive(lord)) return;
        var (f, s, d) = RepOfLord(lord);
        _lordRep[lord] = (Households.FoundedDay[lord], Math.Clamp(f + fame, 0f, 1f), Math.Clamp(s + standing, -1f, 1f), Math.Clamp(d + dread, 0f, 1f));
    }

    private void AddRepRoot(Group root, float fame, float standing, float dread)
    {
        root = RootOf(root);
        if (root.Home >= 0) { AddRepLord(root.Home, fame, standing, dread); return; }
        root.Fame = Math.Clamp(root.Fame + fame, 0f, FameCap(root));
        root.Standing = Math.Clamp(root.Standing + standing, -1f, 1f);
        root.Dread = Math.Clamp(root.Dread + dread, 0f, 1f);
    }

    private static float FameCap(Group root) => root.Visibility switch
    {
        Visibility.Hidden => 0.1f, Visibility.Covert => 0.25f, Visibility.Neutral => 0.4f, _ => 1f,
    };

    /// <summary>예전 공포·신용(소속 저울이 쓴다). 신용 = 0.3 + 평판.</summary>
    public (float fear, float credit) RepOf(int lord)
    {
        var (_, s, d) = RepOfLord(lord);
        return (d, Math.Clamp(0.3f + s, 0f, 1f));
    }

    private void AddRep(int lord, float fear, float credit) => AddRepLord(lord, 0f, credit, fear);

    /// <summary>평판 곱(인지도 × 평판, −1~1). 모집·거래·표적에 쓴다.</summary>
    public float Renown(Group root) { var (f, s, _) = RepOfRoot(root); return f * s; }
    public float RenownOfLord(int lord) { var (f, s, _) = RepOfLord(lord); return f * s; }

    /// <summary>스스로 드는 확률에 곱하는 수(0.2~1.8).</summary>
    public float RecruitMult(Group root) => Math.Clamp(1f + 0.8f * Renown(root), 0.2f, 1.8f);

    /// <summary>기득권이 느끼는 위협에 곱하는 수: 악평은 키우고 좋은 평판은 줄인다.</summary>
    public float AlarmMult(Group root)
    {
        var (f, s, _) = RepOfRoot(root);
        float m = (1f + 2f * MathF.Max(0f, -s) * f) * (1f - 0.5f * MathF.Max(0f, s) * f);
        // 교단의 교리: 성전·평등은 경계를 키우고 신분 질서는 줄인다
        root = RootOf(root);
        if (root.ReligionId > 0 && root.ReligionId < _religions.Count)
        {
            var r = _religions[root.ReligionId];
            m *= 1f + 0.3f * RelStrength(r, Doctrine.HolyWar) + 0.3f * RelStrength(r, Doctrine.Equality) - 0.2f * RelStrength(r, Doctrine.Order);
        }
        return MathF.Max(0.1f, m);
    }

    /// <summary>사람 i 가 우두머리인 세력(지배자 집 가장이면 그 집, 홀로 선 조직 우두머리면 그 조직). 없으면 null.</summary>
    private (int lord, Group? root)? LedFaction(int i)
    {
        var p = People;
        int h = p.Household[i];
        if (Households.IsAlive(h) && ClientCount(h) > 0 && HeadIdx(h) == i) return (h, null);
        foreach (var g in GroupsOf(i)) if (IsTop(g) && g.Home < 0 && g.LeaderUid == p.Uid[i]) return (-1, g);
        return null;
    }

    private (float fame, float standing, float dread) RepOfLed((int lord, Group? root) f) => f.root != null ? RepOfRoot(f.root) : RepOfLord(f.lord);

    private void InitRep()
    {
        // 예법을 배운 사람이 거는 대화·구애는 잘 받아들여진다
        AcceptMods.Add((w, def, c, acc) => def.Category is InteractionCategory.Talk or InteractionCategory.Courtship ? acc + 0.1f * w.Polish(c.Actor, Lesson.Etiquette) : acc);
        // 세력 우두머리가 거는 거래·구애·혼담은 이름만큼, 갈취·위협은 공포만큼 잘 받아들여진다
        AcceptMods.Add((w, def, c, acc) =>
        {
            if (w.LedFaction(c.Actor) is not { } led) return acc;
            var (f, s, d) = w.RepOfLed(led);
            if (def.Id is InteractionId.Extort or InteractionId.Threaten) return acc + 0.2f * d;
            if (def.Category is InteractionCategory.Trade or InteractionCategory.Courtship or InteractionCategory.Family) return acc + 0.15f * f * s;
            return acc;
        });
    }

    /// <summary>인지도·평판 한 해 치(시험용).</summary>
    public void RecountRep() => RepYear();

    /// <summary>해마다: 인지도가 크기 쪽으로 가고, 평판·공포가 제자리로 조금 돌아간다.</summary>
    private void RepYear()
    {
        var hh = Households;
        float fameSum = 0f, standSum = 0f; int n = 0;
        foreach (var g in _groups)
        {
            if (!g.Alive || !IsTop(g) || g.Home >= 0) continue;
            float target = MathF.Min(FameCap(g), MathF.Sqrt(MathF.Max(0, g.Total)) / 20f + VisibleForce(g) / 100f);
            g.Fame += 0.1f * (target - g.Fame);
            g.Fame = MathF.Min(g.Fame, FameCap(g));
            g.Standing *= 0.95f;
            g.Dread = 0.1f + 0.95f * (g.Dread - 0.1f);
            fameSum += g.Fame; standSum += g.Standing; n++;
        }
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || ClientCount(h) == 0) continue;
            var (f, s, d) = RepOfLord(h);
            float target = MathF.Min(1f, MathF.Sqrt(BelowOf(h)) / 20f + 0.1f * TierOf(h));
            f += 0.1f * (target - f);
            _lordRep[h] = (hh.FoundedDay[h], f, s, d);   // 평판·공포가 식는 것은 소속 셈(BelongYear)에서 한다
            fameSum += f; standSum += s; n++;
        }
        foreach (var k in _lordRep.Keys.ToList()) if (!hh.IsAlive(k) || hh.FoundedDay[k] != _lordRep[k].founded) _lordRep.Remove(k);
        Stats.MeanFame = n > 0 ? fameSum / n : 0f;
        Stats.MeanStanding = n > 0 ? standSum / n : 0f;
    }
}
