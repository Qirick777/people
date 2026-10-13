using People.Sim;
using Xunit;

/// <summary>멘델 비율이 실제로 나오는지 큰 표본으로 확인한다.</summary>
public class GeneticsTests
{
    private static byte[] Blank() => new byte[GenomeLayout.BytesPerGenome];

    private static void SetMarker(byte[] g, Marker m, byte a, byte b)
    {
        int l = GenomeLayout.Locus(m);
        g[l * 2] = a;
        g[l * 2 + 1] = b;
    }

    [Fact]
    public void RecessiveCarriersGiveQuarterAffected()
    {
        var f = Blank(); var m = Blank(); var c = Blank();
        SetMarker(f, Marker.DiseaseA, 0, 1);
        SetMarker(m, Marker.DiseaseA, 1, 0);
        var rng = new Rng(7);
        int affected = 0, n = 40000;
        for (int i = 0; i < n; i++)
        {
            bool male = rng.NextBool();
            Genetics.Inherit(f, m, c, male, 0f, ref rng);
            if (Genetics.Expressed(c, Marker.DiseaseA, male)) affected++;
        }
        Assert.InRange(affected / (double)n, 0.235, 0.265);
    }

    [Fact]
    public void XLinkedCarrierMotherAffectsHalfOfSonsNoDaughters()
    {
        var f = Blank(); var m = Blank(); var c = Blank();
        SetMarker(f, Marker.ColorBlind, GenomeLayout.Absent, 0);
        SetMarker(m, Marker.ColorBlind, 0, 1);
        var rng = new Rng(9);
        int sons = 0, sonsAffected = 0, daughters = 0, daughtersAffected = 0, daughtersCarrier = 0;
        for (int i = 0; i < 40000; i++)
        {
            bool male = rng.NextBool();
            Genetics.Inherit(f, m, c, male, 0f, ref rng);
            bool e = Genetics.Expressed(c, Marker.ColorBlind, male);
            if (male) { sons++; if (e) sonsAffected++; }
            else
            {
                daughters++;
                if (e) daughtersAffected++;
                if (Genetics.Carrier(c, Marker.ColorBlind, false)) daughtersCarrier++;
            }
        }
        Assert.InRange(sonsAffected / (double)sons, 0.48, 0.52);
        Assert.Equal(0, daughtersAffected);
        Assert.InRange(daughtersCarrier / (double)daughters, 0.48, 0.52);
    }

    [Fact]
    public void AffectedFatherPassesXOnlyToDaughters()
    {
        var f = Blank(); var m = Blank(); var c = Blank();
        SetMarker(f, Marker.ColorBlind, GenomeLayout.Absent, 1);
        SetMarker(m, Marker.ColorBlind, 0, 0);
        var rng = new Rng(11);
        for (int i = 0; i < 2000; i++)
        {
            bool male = rng.NextBool();
            Genetics.Inherit(f, m, c, male, 0f, ref rng);
            if (male) Assert.False(Genetics.Expressed(c, Marker.ColorBlind, true));
            else Assert.True(Genetics.Carrier(c, Marker.ColorBlind, false));
        }
    }

    [Fact]
    public void DominantFromOneParentGivesHalf()
    {
        var f = Blank(); var m = Blank(); var c = Blank();
        SetMarker(f, Marker.Twinning, 1, 0);
        var rng = new Rng(13);
        int e = 0, n = 40000;
        for (int i = 0; i < n; i++)
        {
            bool male = rng.NextBool();
            Genetics.Inherit(f, m, c, male, 0f, ref rng);
            if (Genetics.Expressed(c, Marker.Twinning, male)) e++;
        }
        Assert.InRange(e / (double)n, 0.48, 0.52);
    }

    [Fact]
    public void BaldnessExpressedOnlyInMales()
    {
        var g = Blank();
        SetMarker(g, Marker.Baldness, 1, 1);
        Assert.True(Genetics.Expressed(g, Marker.Baldness, true));
        Assert.False(Genetics.Expressed(g, Marker.Baldness, false));
    }

    [Fact]
    public void QuantitativeChildAveragesNearParentMidpoint()
    {
        var rng = new Rng(17);
        var f = Blank(); var m = Blank(); var c = Blank();
        Genetics.Randomize(f, true, ref rng);
        Genetics.Randomize(m, false, ref rng);
        float mid = (Genetics.Score(f, Trait.Height) + Genetics.Score(m, Trait.Height)) / 2f;
        double sum = 0; int n = 20000;
        for (int i = 0; i < n; i++)
        {
            Genetics.Inherit(f, m, c, rng.NextBool(), 0f, ref rng);
            sum += Genetics.Score(c, Trait.Height);
        }
        Assert.InRange(sum / n, mid - 0.01, mid + 0.01);
    }
}
