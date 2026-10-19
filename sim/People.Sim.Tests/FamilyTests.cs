using People.Sim;
using Xunit;

public class FamilyTests
{
    private static World Run(ulong seed, int years)
    {
        var w = new World(seed, 400);
        w.SeedCouples(5);
        for (int d = 0; d < years * w.Config.DaysPerYear; d++) w.Step();
        return w;
    }

    private static int Age(World w, int i) => (w.Day - w.People.BirthDay[i]) / w.Config.DaysPerYear;

    [Fact]
    public void AcknowledgedBastardTakesFathersName()
    {
        var L = new Lineage();
        int f = L.Add(true, 0, -1, -1), m = L.Add(false, 0, -1, -1);
        int child = L.Add(true, 1, f, m, -1);           // 사생아: 진짜 아버지 f, 족보 아버지 없음
        Assert.Equal(Birthright.Bastard, L.RightOf(child));
        Assert.Equal(L.FamilyOf(m), L.FamilyOf(child));
        L.Legitimize(child, f);
        Assert.Equal(Birthright.Legit, L.RightOf(child));
        Assert.Equal(L.FamilyOf(f), L.FamilyOf(child));
        Assert.Equal(f, L.Father(child));
    }

    [Fact]
    public void ContraceptionLowersConception()
    {
        var w = Run(3, 60);
        var p = w.People;
        for (int f = 0; f < p.HighWater; f++)
        {
            if (!p.IsAliveIndex(f) || p.Male[f] || !p.IsValid(p.Husband[f])) continue;
            float before = w.ConceptionFactor(f, p.Husband[f].Index);
            if (before <= 0f) continue;
            p.ContraUntil[f] = w.Day + 10;
            float after = w.ConceptionFactor(f, p.Husband[f].Index);
            Assert.True(after < before * 0.5f, $"전 {before}, 후 {after}");
            return;
        }
        Assert.Fail("임신할 수 있는 아내가 없다");
    }

    [Fact]
    public void DisownedChildLeavesAndIsMarked()
    {
        var w = Run(3, 60);
        var p = w.People; var hh = w.Households;
        for (int k = 0; k < p.HighWater; k++)
        {
            if (!p.IsAliveIndex(k) || Age(w, k) < 12 || !hh.IsAlive(p.Household[k])) continue;
            int h = p.Household[k];
            int head = hh.Head[h].Index;
            if (head == k || p.WifeCount[k] > 0) continue;
            var r = w.Interact(InteractionId.Disown, new InteractionContext { Actor = head, Target = k, Third = -1 });
            Assert.True(r.Accepted);
            Assert.True(w.Lineage.Disowned(p.Uid[k]));
            Assert.NotEqual(h, p.Household[k]);
            return;
        }
        Assert.Fail("의절할 식구가 없다");
    }
}
