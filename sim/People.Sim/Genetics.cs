namespace People.Sim;

/// <summary>수치형 특성. 표현형 0~1 (성별 영향으로 1을 조금 넘을 수 있다).</summary>
public enum Trait : byte
{
    Height, Strength, Constitution, Fertility, Longevity, Intelligence, Dexterity,
    Perception, Charisma, Looks, Diligence, Ambition, Stubbornness, Sociability,
    SkinTone, HairDark,
    PrefLooks, PrefHeight, PrefStrength, PrefWealth, PrefCharisma,
    Count
}

/// <summary>유무형 특성(한 자리).</summary>
public enum Marker : byte
{
    Twinning, RedHair, Blond, ColorBlind, Baldness, DiseaseA, DiseaseB, Frailty, ShortLife,
    Count
}

public enum Inheritance : byte { Dominant, Recessive, XRecessive, MaleLimitedDominant }

/// <summary>
/// 유전체 배치. 자리(locus)마다 대립유전자 두 바이트: [0] = 아버지에게서, [1] = 어머니에게서.
/// 남성의 X 자리는 [0] 이 비어 있다(Absent).
/// </summary>
public static class GenomeLayout
{
    public const byte Absent = 0xFF;
    public const byte MaxQuantAllele = 3;

    private static readonly int[] LociPerTrait = BuildLociPerTrait();
    private static readonly int[] TraitOffset;
    public static readonly int MarkerOffset;
    public static readonly int LocusCount;
    public static int BytesPerGenome => LocusCount * 2;

    public static readonly Inheritance[] MarkerInheritance =
    {
        Inheritance.Dominant,           // 쌍둥이 체질
        Inheritance.Recessive,          // 붉은 머리
        Inheritance.Recessive,          // 금발
        Inheritance.XRecessive,         // 색맹
        Inheritance.MaleLimitedDominant,// 대머리
        Inheritance.Recessive,          // 유전병 A
        Inheritance.Recessive,          // 유전병 B (불임)
        Inheritance.Recessive,          // 허약 체질 (체질 ×0.6)
        Inheritance.Recessive,          // 단명 (수명 ×0.5)
    };

    /// <summary>시작 인구에서 변이 대립유전자 빈도.</summary>
    public static readonly float[] MarkerStartFrequency = { 0.03f, 0.15f, 0.20f, 0.08f, 0.25f, 0.04f, 0.04f, 0.06f, 0.06f };

    static GenomeLayout()
    {
        TraitOffset = new int[(int)Trait.Count];
        int o = 0;
        for (int t = 0; t < (int)Trait.Count; t++)
        {
            TraitOffset[t] = o;
            o += LociPerTrait[t];
        }
        MarkerOffset = o;
        LocusCount = o + (int)Marker.Count;
    }

    private static int[] BuildLociPerTrait()
    {
        var n = new int[(int)Trait.Count];
        for (int t = 0; t < n.Length; t++) n[t] = 4;
        n[(int)Trait.SkinTone] = 3;
        n[(int)Trait.HairDark] = 2;
        for (var t = Trait.PrefLooks; t <= Trait.PrefCharisma; t++) n[(int)t] = 2;
        return n;
    }

    public static int Loci(Trait t) => LociPerTrait[(int)t];
    public static int Locus(Trait t, int i) => TraitOffset[(int)t] + i;
    public static int Locus(Marker m) => MarkerOffset + (int)m;
    public static bool IsXLinked(int locus) =>
        locus == Locus(Marker.ColorBlind);
}

/// <summary>유전 계산. 유전체는 PersonStore 의 큰 바이트 배열 안 구간으로 다룬다.</summary>
public static class Genetics
{
    /// <summary>시작 인구용 무작위 유전체.</summary>
    public static void Randomize(Span<byte> g, bool male, ref Rng rng)
    {
        for (int t = 0; t < (int)Trait.Count; t++)
        {
            for (int i = 0; i < GenomeLayout.Loci((Trait)t); i++)
            {
                int l = GenomeLayout.Locus((Trait)t, i);
                g[l * 2] = (byte)rng.NextInt(GenomeLayout.MaxQuantAllele + 1);
                g[l * 2 + 1] = (byte)rng.NextInt(GenomeLayout.MaxQuantAllele + 1);
            }
        }
        for (int m = 0; m < (int)Marker.Count; m++)
        {
            int l = GenomeLayout.MarkerOffset + m;
            float f = GenomeLayout.MarkerStartFrequency[m];
            g[l * 2] = (byte)(rng.Chance(f) ? 1 : 0);
            g[l * 2 + 1] = (byte)(rng.Chance(f) ? 1 : 0);
            if (male && GenomeLayout.IsXLinked(l)) g[l * 2] = GenomeLayout.Absent;
        }
    }

    /// <summary>부모 유전체에서 자식 유전체를 만든다(멘델 유전, 성염색체, 돌연변이).</summary>
    public static void Inherit(ReadOnlySpan<byte> father, ReadOnlySpan<byte> mother, Span<byte> child,
        bool childMale, float mutationRate, ref Rng rng)
    {
        int quantLoci = GenomeLayout.MarkerOffset;
        for (int l = 0; l < GenomeLayout.LocusCount; l++)
        {
            bool quant = l < quantLoci;
            byte fromMother = mother[l * 2 + rng.NextInt(2)];
            byte fromFather;
            if (GenomeLayout.IsXLinked(l))
            {
                // 아버지의 X 는 [1] 에 하나뿐이고 딸에게만 간다.
                fromFather = childMale ? GenomeLayout.Absent : father[l * 2 + 1];
            }
            else
            {
                fromFather = father[l * 2 + rng.NextInt(2)];
            }
            child[l * 2] = Mutate(fromFather, quant, mutationRate, ref rng);
            child[l * 2 + 1] = Mutate(fromMother, quant, mutationRate, ref rng);
        }
    }

    private static byte Mutate(byte a, bool quant, float rate, ref Rng rng)
    {
        if (a == GenomeLayout.Absent || !rng.Chance(rate)) return a;
        if (!quant) return (byte)(a ^ 1);
        int d = rng.NextBool() ? 1 : -1;
        return (byte)Math.Clamp(a + d, 0, GenomeLayout.MaxQuantAllele);
    }

    /// <summary>수치형 유전 점수 0~1.</summary>
    public static float Score(ReadOnlySpan<byte> g, Trait t)
    {
        int n = GenomeLayout.Loci(t);
        int sum = 0;
        for (int i = 0; i < n; i++)
        {
            int l = GenomeLayout.Locus(t, i);
            sum += g[l * 2] + g[l * 2 + 1];
        }
        return sum / (float)(n * 2 * GenomeLayout.MaxQuantAllele);
    }

    /// <summary>유무형 특성이 겉으로 드러나는가.</summary>
    public static bool Expressed(ReadOnlySpan<byte> g, Marker m, bool male)
    {
        int l = GenomeLayout.Locus(m);
        byte a = g[l * 2], b = g[l * 2 + 1];
        return GenomeLayout.MarkerInheritance[(int)m] switch
        {
            Inheritance.Dominant => a == 1 || b == 1,
            Inheritance.Recessive => a == 1 && b == 1,
            Inheritance.XRecessive => male ? b == 1 : a == 1 && b == 1,
            Inheritance.MaleLimitedDominant => male && (a == 1 || b == 1),
            _ => false,
        };
    }

    /// <summary>드러나지 않지만 변이 대립유전자를 가진 사람(보인자).</summary>
    public static bool Carrier(ReadOnlySpan<byte> g, Marker m, bool male)
    {
        int l = GenomeLayout.Locus(m);
        bool any = g[l * 2] == 1 || g[l * 2 + 1] == 1;
        return any && !Expressed(g, m, male);
    }

    /// <summary>성별 영향 계수 (spec 1.2).</summary>
    public static float SexFactor(Trait t, bool male) => t switch
    {
        Trait.Height => male ? 1.08f : 1f,
        Trait.Strength => male ? 1.15f : 1f,
        Trait.Longevity => male ? 1f : 1.05f,
        Trait.Ambition => male ? 1.05f : 1f,
        _ => 1f,
    };

    /// <summary>근교약세: 부모 혈연이 높을수록 깎이는 특성.</summary>
    public static bool AffectedByInbreeding(Trait t) =>
        t is Trait.Constitution or Trait.Strength or Trait.Fertility or Trait.Intelligence or Trait.Longevity;

    /// <summary>어릴 때 굶주림의 영향을 받는 특성.</summary>
    public static bool AffectedByChildhoodHunger(Trait t) =>
        t is Trait.Height or Trait.Strength or Trait.Constitution;
}
