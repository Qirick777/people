namespace People.Sim;

/// <summary>걷을 것을 내지 않았을 때 하는 일. 걷는 대상(세력원·세력 밖)과 상관없이 같은 목록이다.</summary>
public enum NonPay : byte { Forgive, Debt, Seize, Detain, Harm, Expel }

/// <summary>
/// 세력 개편 R6(plan_reform): 걷기 통합.
/// - 걷기는 지금의 공납 틀 하나로 돈다. 세력원(따르는 집)에게 걷는 것이 세금·헌금이고, 세력 밖 집에서 보호세를 받으면(보호세 일) 그 집이 세력원이 된다.
/// - 일손: 우두머리 혼자 직접 맡는 집(지금의 직접 범위) + 징수관 + 걷기(보호세) 일을 맡은 갈래 조직원(한 사람 20 + 글 × 10집 × 일 비중).
///   맡는 사람이 없는 집은 충성 0.6 넘는 집만 스스로 낸다(낼 확률 = 2 × (충성 − 0.6) × 형편, 헌금처럼).
/// - 맡은 집이 내지 않을 확률 = 0.1 × (1 − 충성 × 형편) × (1 − 압박) × (1 − 0.5 × 공포) × 본보기(최대 반으로). 맡은 집은 거의 낸다(많이 안 내면 지배자 군인이 줄어 마을이 털린다). 압박은 미납 대응의 무게(용서 0, 빚 0.1, 압류 0.3, 구금·해치기 0.5, 내쫓기 0.2).
/// - 내지 않으면 지배자가 정한 미납 대응을 한다. 맡는 사람이 없는 집에는 아무것도 할 수 없다.
///   용서: 충성 0.6 넘는 가장은 충성 +0.03(용서받았다), 아니면 그대로. 빚: 밀린 것 + 10%가 빚이 된다(두 해 넘으면 땅을 가져간다).
///   압류: 밀린 것 + 20%를 곡식·은화로 가져간다. 충성 −0.05, 그 마을 다른 집은 해마다 본보기만큼 더 낸다.
///   구금: 붙잡기(R10)가 오기 전까지는 압류에 공포 +0.01을 더한다. 해치기: 압류 + 충성 −0.15, 보는 눈 −30, 공포 +0.01, 평판 −0.01.
///   내쫓기: 세력원에서 뺀다(공포 +0.01).
/// - NPC 지배자: 아주 거칠면(공격성 0.8) 해치기, 거칠면(0.65) 압류, 그 밖은 용서(빚을 지우면 이자와 땅 빼앗기로 마을이 크게 마른다).
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, NonPay> _nonPay = new();        // 플레이어 지배자가 정한 미납 대응
    private readonly Dictionary<int, float> _example = new();        // 마을 → 지난해 본보기(압류·해치기 수, 해마다 반으로)

    public static readonly float[] NonPayPressure = { 0f, 0.1f, 0.3f, 0.5f, 0.5f, 0.2f };
    public static string NonPayName(NonPay n) => n switch
    {
        NonPay.Debt => "빚으로", NonPay.Seize => "압류", NonPay.Detain => "구금", NonPay.Harm => "해치기", NonPay.Expel => "내쫓기", _ => "용서",
    };

    public NonPay NonPayOf(int lord)
    {
        if (_nonPay.TryGetValue(lord, out var n)) return n;
        float agg = HeadTrait(lord, Trait.Aggression);
        return agg > 0.8f ? NonPay.Harm : agg > 0.65f ? NonPay.Seize : NonPay.Forgive;
    }

    public void SetNonPay(int lord, NonPay n) => _nonPay[lord] = n;

    /// <summary>걷기 일을 맡은 갈래 조직원(지배자 집이나 우두머리 집에 걷어다 준다)과 맡을 수 있는 집 수.</summary>
    private List<(int person, int span)> OrgCollectors(int lord)
    {
        var list = new List<(int, int)>();
        var p = People;
        foreach (var g in _groups)
        {
            if (!g.Alive || g.Tasks.All(t => t.Verb != Verb.Collect)) continue;
            var root = RootOf(g);
            if (LandHomeOf(root) != lord) continue;
            float w = g.Tasks.Where(t => t.Verb == Verb.Collect).Sum(t => t.Weight) * g.CmdFactor;
            foreach (int m in MembersFast(g))
                if (Adult(m) && p.Uid[m] != root.LeaderUid) list.Add((m, (int)((20f + 10f * p.GetSkill(m, Skill.Letters)) * w)));
        }
        return list;
    }

    /// <summary>
    /// 집 h 가 지배자 lord 에게 이번 공납을 낼까. covered: 맡는 사람이 있는가. 내면 true.
    /// 내지 않으면 미납 대응을 한다(집행할 사람이 없으면 빚).
    /// </summary>
    private bool WillPay(int h, int lord, bool covered, float owed)
    {
        var p = People; var hh = Households;
        int head = HeadIdx(h);
        float loy = head >= 0 && p.LoyalUid[head] >= 0 ? p.Loyalty[head] : 0.5f;
        float wealth = Math.Clamp(Surplus(h), 0f, 1f);
        int v = hh.Village[h];
        var np = NonPayOf(lord);
        // 맡은 집이 내지 않을 확률은 많아야 10%: 충성과 형편, 압박, 공포, 본보기가 줄인다. 맡는 사람이 없으면 충성 0.6 넘는 집만 스스로 낸다
        float pr = covered
            ? 1f - 0.1f * (1f - loy * wealth) * (1f - NonPayPressure[(int)np]) * (1f - 0.5f * RepOfLord(lord).dread) * MathF.Max(0.5f, 1f - 0.1f * _example.GetValueOrDefault(v))
            : MathF.Max(0f, loy - 0.6f) * 2f * wealth;
        if (_rng.Chance(Math.Clamp(pr, 0f, 1f))) return true;
        Stats.TaxRefused++;
        if (!covered) return false;   // 맡는 사람이 없으면 미납 대응도 할 수 없다
        switch (np)
        {
            case NonPay.Forgive:
                if (head >= 0 && loy >= 0.6f) p.Loyalty[head] = MathF.Min(1f, p.Loyalty[head] + 0.03f);
                break;
            case NonPay.Debt:
                if (owed > 0.5f) _debts.Add(new Debt { Debtor = h, DebtorFounded = hh.FoundedDay[h], Creditor = lord, CreditorFounded = hh.FoundedDay[lord], Owed = owed * 1.1f });
                Stats.TaxDebts++;
                break;
            case NonPay.Seize:
            case NonPay.Detain:
            case NonPay.Harm:
                {
                    float take = MathF.Min(owed * 1.2f, MathF.Max(0f, hh.Food[h] - hh.Need[h] * DaysToHarvest() * 0.5f));
                    hh.Food[h] -= take; hh.Food[lord] += take;
                    PayDues(h, take);
                    if (head >= 0) p.Loyalty[head] = MathF.Max(0f, p.Loyalty[head] - (np == NonPay.Harm ? 0.15f : 0.05f));
                    if (np != NonPay.Seize) AddRepLord(lord, 0.005f, np == NonPay.Harm ? -0.01f : 0f, 0.01f);
                    if (np == NonPay.Harm && head >= 0 && HeadIdx(lord) is int lh && lh >= 0) Social.Add(p, head, p.Uid[lh], -30f, 20f);
                    if (v >= 0) _example[v] = _example.GetValueOrDefault(v) + 1f;
                    Stats.TaxSeized++;
                    break;
                }
            case NonPay.Expel:
                _patron[h] = -1;
                _clientsOf[lord] = Math.Max(0, _clientsOf.GetValueOrDefault(lord) - 1);   // 따르는 집 수도 그 자리에서 고친다
                AddRepLord(lord, 0f, 0f, 0.01f);
                Stats.TaxExpelled++;
                break;
        }
        return false;
    }

    /// <summary>공납 한 번 판정(시험용).</summary>
    public bool TryCollect(int h, int lord, bool covered, float owed) => WillPay(h, lord, covered, owed);

    /// <summary>해마다(공납 뒤): 본보기는 반으로 식는다.</summary>
    private void CollectYearEnd()
    {
        foreach (var k in _example.Keys.ToList()) { float x = _example[k] * 0.5f; if (x < 0.05f) _example.Remove(k); else _example[k] = x; }
    }
}
