using People.Sim;
using Xunit;

public class TalkTests
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
    public void DowryMakesAMatchMoreAcceptable()
    {
        var w = Run(3, 60);
        var p = w.People; var hh = w.Households;
        for (int x = 0; x < p.HighWater; x++)
        {
            if (!p.IsAliveIndex(x) || Age(w, x) < 16 || p.Male[x]) continue;   // 셈만 본다(혼인 여부는 따지지 않는다)
            int h = p.Household[x];
            int head = hh.Head[h].Index;
            if (head == x) continue;
            for (int y = 0; y < p.HighWater; y++)
            {
                if (!p.IsAliveIndex(y) || !p.Male[y] || p.Household[y] == h || Age(w, y) < 16) continue;
                float none = w.MatchAccept(head, y, x, 0f), big = w.MatchAccept(head, y, x, 300f);
                Assert.True(big > none, $"지참금 없이 {none}, 많이 {big}");
                return;
            }
        }
        Assert.Fail("혼담을 볼 짝이 없다");
    }

    [Fact]
    public void DivorceSendsWifeAwayAndFreesBoth()
    {
        var w = Run(3, 60);
        var p = w.People;
        for (int f = 0; f < p.HighWater; f++)
        {
            if (!p.IsAliveIndex(f) || p.Male[f] || !p.IsValid(p.Husband[f])) continue;
            int m = p.Husband[f].Index;
            int wives = p.WifeCount[m];
            p.Piety[m] = 0f;
            var c = new InteractionContext { Actor = m, Target = f, Third = -1 };
            bool done = false;
            for (int k = 0; k < 40 && !done; k++) done = w.Interact(InteractionId.Divorce, c).Accepted;
            Assert.True(done);
            Assert.False(p.IsValid(p.Husband[f]));
            Assert.Equal(wives - 1, p.WifeCount[m]);
            return;
        }
        Assert.Fail("부부가 없다");
    }
}
