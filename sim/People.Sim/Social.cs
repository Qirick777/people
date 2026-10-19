namespace People.Sim;

/// <summary>기억의 종류. 관계 점수는 사건이 일어날 때 바뀌고, 기억은 이야기와 원한을 남긴다.</summary>
public enum MemoryType : byte
{
    None, Married, ChildBorn, Gift, Compliment, Chat, Insulted, Gossiped,
    Affair, SexTrade, SpouseAffair, Exposed, Raided, RaidedBy, LoanGiven, LoanTaken, Foreclosed,
    Protected, Betrayed, Count,
}

/// <summary>비밀의 종류.</summary>
public enum SecretType : byte { Affair, HiddenFather, SexTrade }

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
            else if (p.Fanatic[i] != 0) { p.Doubt[i] -= aff; aff = 0f; }
            else aff *= 1.5f - l;
        }
        int s = Ensure(p, i, uid);
        p.RelAff[s] = Math.Clamp(p.RelAff[s] + aff, -100f, 100f);
        p.RelFear[s] = Math.Clamp(p.RelFear[s] + fear, 0f, 100f);
        p.RelDebt[s] = Math.Clamp(p.RelDebt[s] + debt, -100f, 100f);
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
        MemoryType.Foreclosed => "빚 대신 땅을 빼앗겼다", MemoryType.Protected => "보호를 받기 시작했다", MemoryType.Betrayed => "배신당했다",
        _ => "",
    };
}
