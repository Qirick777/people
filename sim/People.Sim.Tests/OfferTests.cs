using People.Sim;
using Xunit;

public class OfferTests
{
    private static (World w, int lord, Group g, int leader) WithOrg()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        w.LordOrg(lord);
        var clients = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h])).ToList();
        int leader = hh.Head[clients[0]].Index;
        var g = w.FoundGroup(leader, GroupKind.Watch);
        w.SetVisibility(g, Visibility.Public);
        foreach (int h in clients.Skip(1)) w.JoinGroup(hh.Head[h].Index, g);
        w.RecountOrgs();
        return (w, lord, g, leader);
    }

    [Fact]
    public void OfferNeedsConsentAndRefusalBlocksRetry()
    {
        var (w, lord, g, leader) = WithOrg();
        int lh = w.Households.Head[lord].Index;
        var c = new InteractionContext { Actor = lh, Target = leader, Third = -1 };
        var def = w.Def(InteractionId.OrgOffer);
        Assert.True(def.Can(w, c));
        def.Fail!(w, c);
        Assert.False(World.IsAttached(g));
        Assert.Equal(-1, g.Parent);
        Assert.False(def.Can(w, c));   // 다섯 해 동안 다시 묻지 않는다
    }

    [Fact]
    public void AttachedOrgKeepsItsOwnRootAndCanLeave()
    {
        var (w, lord, g, leader) = WithOrg();
        int lh = w.Households.Head[lord].Index;
        var c = new InteractionContext { Actor = lh, Target = leader, Third = -1 };
        w.Def(InteractionId.OrgOffer).Success!(w, c, new InteractionResult { Accepted = true });
        Assert.True(World.IsAttached(g));
        Assert.Equal(w.LordOrg(lord, false)!.Id, g.Parent);
        Assert.Same(g, w.RootOf(g));
        Assert.False(w.CanUseVerb(g, Verb.Collect, out _));
        w.SetVisibility(g, Visibility.Hidden);
        Assert.Equal(Visibility.Public, g.Visibility);
        var b = new InteractionContext { Actor = leader, Target = lh, Third = -1 };
        Assert.True(w.Def(InteractionId.OrgBreak).Can(w, b));
        w.Def(InteractionId.OrgBreak).Success!(w, b, new InteractionResult { Accepted = true });
        Assert.False(World.IsAttached(g));
        Assert.Equal(-1, g.Parent);
    }
}
