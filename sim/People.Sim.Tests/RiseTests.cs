using People.Sim;
using Xunit;

public class RiseTests
{
    [Fact]
    public void SwearThenSuppressEndsOneOfTwoWays()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        int k = Enumerable.Range(0, hh.HighWater).First(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]));
        int n = w.Enfeoff(lord, k, -1, 5);
        Assert.True(n >= 5);
        w.BreakAway(lord, k);   // 떨어져 나간 세력이 된다
        Assert.Equal(-1, w.PatronOf(k));
        int lh = hh.Head[lord].Index, kh = hh.Head[k].Index;
        // 스스로 서약하기(떨어져 나간 뒤라 주군이 나쁘게 본다. 받아 줄 만큼 풀어 둔다)
        Social.Add(p, lh, p.Uid[kh], 90f);
        // 떼어 준 뒤 주군 쪽이 서약을 받을 만큼 크지 않은 세계(남은 집 셋 아래)는 건너뛴다
        if (!w.Def(InteractionId.RiseSwear).Can(w, new InteractionContext { Actor = kh, Target = lh, Third = -1 })) return;
        for (int t = 0; t < 50 && w.PatronOf(k) != lord; t++)
            w.Interact(InteractionId.RiseSwear, new InteractionContext { Actor = kh, Target = lh, Third = -1 });
        Assert.Equal(lord, w.PatronOf(k));
        Assert.Equal(Legitimacy.Granted, w.LegitOf(k));
        // 무력 진압: 이기면 집을 거두고, 지면 홀로 선다
        int before = w.ClientCount(lord);
        var r = w.Interact(InteractionId.RiseSuppress, new InteractionContext { Actor = lh, Target = kh, Third = -1 });
        if (r.Accepted) Assert.True(w.ClientCount(lord) >= before + n - 1);
        else { Assert.Equal(-1, w.PatronOf(k)); Assert.Equal(Legitimacy.Self, w.LegitOf(k)); }
    }
}
