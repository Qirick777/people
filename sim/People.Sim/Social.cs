namespace People.Sim;

/// <summary>기억의 종류. 관계 점수는 사건이 일어날 때 바뀌고, 기억은 이야기와 원한을 남긴다.</summary>
public enum MemoryType : byte
{
    None, Married, ChildBorn, Gift, Compliment, Chat, Insulted, Gossiped,
    Affair, SexTrade, SpouseAffair, Exposed, Raided, RaidedBy, LoanGiven, LoanTaken, Foreclosed,
    Protected, Betrayed, Threatened, Divorced, Forced, Disowned, Assaulted, Punished, Count,
}

/// <summary>비밀의 종류.</summary>
public enum SecretType : byte { Affair, HiddenFather, SexTrade, Abortion, Crime, CoverUp }

/// <summary>비밀 하나. A·B 는 당사자 uid, Child 는 숨긴 아이 uid. Known 은 아는 사람 uid(당사자 포함).</summary>
public sealed class Secret
{
    public SecretType Type;
    public int A, B, Child = -1, Day;
    public bool Exposed;
    public readonly List<int> Known = new();
}

/// <summary>관계·기억 칸을 다루는 함수들. 칸은 PersonStore 의 고정 배열이다(사람당 24 + 16).</summary>
public static class Social
{
    public static int Find(PersonStore p, int i, int uid)
    {
        int b = i * PersonStore.RelSlots;
        for (int k = 0; k < PersonStore.RelSlots; k++) if (p.RelUid[b + k] == uid) return b + k;
        return -1;
    }

    /// <summary>uid 의 칸을 찾거나 만든다. 칸이 차면 가장 약한 관계를 잊는다.</summary>
    public static int Ensure(PersonStore p, int i, int uid)
    {
        int s = Find(p, i, uid);
        if (s >= 0) return s;
        int b = i * PersonStore.RelSlots, weakest = b;
        float w = float.MaxValue;
        for (int k = 0; k < PersonStore.RelSlots; k++)
        {
            int j = b + k;
            if (p.RelUid[j] < 0) { weakest = j; break; }
            float m = MathF.Abs(p.RelAff[j]) + MathF.Abs(p.RelFear[j]) + MathF.Abs(p.RelDebt[j]);
            if (m < w) { w = m; weakest = j; }
        }
        p.RelUid[weakest] = uid;
        p.RelAff[weakest] = p.RelFear[weakest] = p.RelDebt[weakest] = 0f;
        return weakest;
    }

    public static void Add(PersonStore p, int i, int uid, float aff, float fear = 0f, float debt = 0f)
    {
        if (uid < 0) return;
        if (uid == p.LoyalUid[i])
        {
            // 충성이 받아들이는 눈을 바꾼다. 광신이면 나쁜 일은 관계 대신 의심으로 쌓인다.
            float l = p.Loyalty[i];
            if (aff >= 0f) aff *= 0.5f + l;
            else if (p.Fanatic[i] != 0) { p.Doubt[i] -= aff * (1f + p.GetSkill(i, Skill.Letters)); aff = 0f; }   // 글을 배운 광신자는 의심이 빨리 쌓인다
            else aff *= 1.5f - l;
        }
        else aff *= Borrowed(p, i, uid, aff);
        int s = Ensure(p, i, uid);
        p.RelAff[s] = Math.Clamp(p.RelAff[s] + aff, -100f, 100f);
        p.RelFear[s] = Math.Clamp(p.RelFear[s] + fear, 0f, 100f);
        p.RelDebt[s] = Math.Clamp(p.RelDebt[s] + debt, -100f, 100f);
    }

    /// <summary>
    /// 빌려 온 눈: i 가 섬기는 사람(B)이 uid(C)를 아주 싫어하면 C 에게서 받는 좋은 일은 덜, 나쁜 일은 더 느낀다.
    /// B 가 C 를 아주 좋아하면 거꾸로다. 세기 = (충성 − 0.6)/0.4 × (B 의 관계가 ±30 을 넘은 정도). 한 다리만 건넌다.
    /// </summary>
    public static float Borrowed(PersonStore p, int i, int uid, float aff)
    {
        float l = p.Loyalty[i];
        int b = p.LoyalIdx[i];
        if (l < 0.6f || b < 0 || !p.IsAliveIndex(b) || p.Uid[b] != p.LoyalUid[i]) return 1f;
        float o = Opinion(p, b, uid);
        if (MathF.Abs(o) < 30f) return 1f;
        float k = MathF.Min(1f, (l - 0.6f) / 0.3f) * MathF.Min(1f, (MathF.Abs(o) - 30f) / 60f);
        bool hates = o < 0f;
        if (aff >= 0f) return hates ? 1f - 0.6f * k : 1f + 0.8f * k;
        return hates ? 1f + 0.8f * k : 1f - 0.6f * k;
    }

    /// <summary>
    /// 사람(j)에게서 받는 일: 끌리는 사람에게는 좋은 일이 더 크게 와닿는다. 외모(이성이면 i 의 외모 선호만큼 더)와 말솜씨.
    /// 말솜씨 좋은 사람의 잘못은 조금 덜 밉다. 이 위에 충성과 빌려 온 눈이 얹힌다.
    /// </summary>
    public static void AddFrom(PersonStore p, int i, int j, float aff, float fear = 0f, float debt = 0f)
    {
        if (aff > 0f) aff *= Charm(p, i, j);
        else if (aff < 0f) aff *= 1f - 0.4f * (p.Get(j, Trait.Charisma) - 0.5f);
        Add(p, i, p.Uid[j], aff, fear, debt);
    }

    /// <summary>i 가 j 에게 끌리는 정도에 따른 배율(약 0.7~1.4).</summary>
    public static float Charm(PersonStore p, int i, int j)
    {
        float looks = p.Get(j, Trait.Looks) - 0.5f, cha = p.Get(j, Trait.Charisma) - 0.5f;
        float lw = p.Male[i] != p.Male[j] ? 0.3f + 0.6f * p.Get(i, Trait.PrefLooks) : 0.25f;
        return Math.Clamp(1f + 0.8f * cha + lw * looks * 2f * 0.5f, 0.6f, 1.5f);
    }

    /// <summary>i 가 uid 를 얼마나 좋게 보는가(호감 + 은혜의 절반). 모르는 사이면 0.</summary>
    public static float Opinion(PersonStore p, int i, int uid)
    {
        int s = Find(p, i, uid);
        return s < 0 ? 0f : p.RelAff[s] + 0.5f * p.RelDebt[s];
    }

    public static float Fear(PersonStore p, int i, int uid)
    {
        int s = Find(p, i, uid);
        return s < 0 ? 0f : p.RelFear[s];
    }

    /// <summary>해마다 관계가 흐려진다(0 쪽으로). 광신하는 대상과의 관계는 흐려지지 않는다.</summary>
    public static void Decay(PersonStore p, int i, float keep)
    {
        int b = i * PersonStore.RelSlots;
        int frozen = p.Fanatic[i] != 0 ? p.LoyalUid[i] : -1;
        for (int k = 0; k < PersonStore.RelSlots; k++)
        {
            int j = b + k;
            if (p.RelUid[j] < 0 || (frozen >= 0 && p.RelUid[j] == frozen)) continue;
            p.RelAff[j] *= keep; p.RelFear[j] *= keep; p.RelDebt[j] *= keep;
            if (MathF.Abs(p.RelAff[j]) + p.RelFear[j] + MathF.Abs(p.RelDebt[j]) < 0.5f) p.RelUid[j] = -1;
        }
    }

    public static void Remember(PersonStore p, int i, MemoryType t, int other, int day, int weight)
    {
        int k = p.MemNext[i];
        int j = i * PersonStore.MemSlots + k;
        p.MemType[j] = (byte)t; p.MemOther[j] = other; p.MemDay[j] = day;
        p.MemWeight[j] = (sbyte)Math.Clamp(weight, -100, 100);
        p.MemNext[i] = (byte)((k + 1) % PersonStore.MemSlots);
    }

    /// <summary>최근 기억부터.</summary>
    public static IEnumerable<(MemoryType type, int other, int day, int weight)> Memories(PersonStore p, int i)
    {
        int n = PersonStore.MemSlots;
        for (int d = 1; d <= n; d++)
        {
            int k = (p.MemNext[i] - d + n) % n;
            int j = i * n + k;
            if (p.MemType[j] == 0) continue;
            yield return ((MemoryType)p.MemType[j], p.MemOther[j], p.MemDay[j], p.MemWeight[j]);
        }
    }

    /// <summary>uid 를 향한 원한: 기억 중 그 사람에게 당한 나쁜 일의 무게 합(양수).</summary>
    public static int Grudge(PersonStore p, int i, int uid)
    {
        int g = 0, n = PersonStore.MemSlots;
        for (int k = 0; k < n; k++)
        {
            int j = i * n + k;
            if (p.MemType[j] != 0 && p.MemOther[j] == uid && p.MemWeight[j] < 0) g -= p.MemWeight[j];
        }
        return g;
    }

    public static string MemoryText(MemoryType t) => t switch
    {
        MemoryType.Married => "혼인했다", MemoryType.ChildBorn => "아이를 얻었다", MemoryType.Gift => "선물을 받았다",
        MemoryType.Compliment => "칭찬을 들었다", MemoryType.Chat => "이야기를 나눴다", MemoryType.Insulted => "험담을 들었다",
        MemoryType.Gossiped => "뒷말을 했다", MemoryType.Affair => "몰래 만났다", MemoryType.SexTrade => "하룻밤 거래를 했다",
        MemoryType.SpouseAffair => "배우자의 불륜을 알았다", MemoryType.Exposed => "비밀이 드러났다", MemoryType.Raided => "약탈당했다",
        MemoryType.RaidedBy => "약탈했다", MemoryType.LoanGiven => "식량을 빌려주었다", MemoryType.LoanTaken => "식량을 빌렸다",
        MemoryType.Foreclosed => "빚 대신 땅을 빼앗겼다", MemoryType.Protected => "보호를 받기 시작했다", MemoryType.Betrayed => "배신당했다", MemoryType.Threatened => "위협을 받았다", MemoryType.Divorced => "갈라섰다", MemoryType.Forced => "억지로 혼인했다", MemoryType.Disowned => "의절했다", MemoryType.Assaulted => "얻어맞았다", MemoryType.Punished => "벌을 받았다",
        _ => "",
    };
}
