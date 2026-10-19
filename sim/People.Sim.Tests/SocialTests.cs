using People.Sim;
using Xunit;

public class SocialTests
{
    [Fact]
    public void LoyaltyColorsHowEventsAreFelt()
    {
        var s = new PersonStore(4);
        int i = s.Add(true, 0, PersonId.None, PersonId.None, 0).Index;
        s.LoyalUid[i] = 7;
        s.Loyalty[i] = 0.9f;
        Social.Add(s, i, 7, -20f);
        Assert.Equal(-12f, Social.Opinion(s, i, 7), 3);   // 충성하면 나쁜 일을 작게 느낀다
        s.Loyalty[i] = 0.1f;
        Social.Add(s, i, 7, 10f);
        Assert.Equal(-6f, Social.Opinion(s, i, 7), 3);    // 충성이 낮으면 좋은 일도 작게 느낀다
    }

    [Fact]
    public void FanaticRelationDoesNotFallOrFade()
    {
        var s = new PersonStore(4);
        int i = s.Add(true, 0, PersonId.None, PersonId.None, 0).Index;
        Social.Add(s, i, 7, 80f);
        s.LoyalUid[i] = 7;
        s.Loyalty[i] = 0.95f;
        s.Fanatic[i] = 1;
        Social.Add(s, i, 7, -30f);
        Social.Decay(s, i, 0.5f);
        Assert.Equal(80f, Social.Opinion(s, i, 7), 3);
        Assert.Equal(30f, s.Doubt[i], 3);                  // 나쁜 일은 의심으로 쌓인다
    }

    private static (PersonStore s, int a, int b, int c) Trio()
    {
        var s = new PersonStore(8);
        int a = s.Add(true, 0, PersonId.None, PersonId.None, 0).Index;
        int b = s.Add(true, 0, PersonId.None, PersonId.None, 0).Index;
        int c = s.Add(true, 0, PersonId.None, PersonId.None, 0).Index;
        s.Uid[a] = 1; s.Uid[b] = 2; s.Uid[c] = 3;
        s.LoyalUid[a] = 2; s.LoyalIdx[a] = b; s.Loyalty[a] = 0.95f;
        return (s, a, b, c);
    }

    [Fact]
    public void LordsHatredSlowsWarmingToHisEnemy()
    {
        var (s, a, b, c) = Trio();
        Social.Add(s, b, 3, -100f);                       // B 는 C 를 몹시 미워한다
        Social.Add(s, a, 3, 10f);
        Assert.True(Social.Opinion(s, a, 3) < 6f);        // 좋은 일이 덜 와닿는다
        Social.Add(s, a, 3, -10f);
        Assert.True(Social.Opinion(s, a, 3) < -10f);      // 나쁜 일은 더 와닿는다
    }

    [Fact]
    public void LordsFavourSpeedsWarmingToHisFriend()
    {
        var (s, a, b, c) = Trio();
        Social.Add(s, b, 3, 100f);
        Social.Add(s, a, 3, 10f);
        Assert.True(Social.Opinion(s, a, 3) > 15f);
    }

    [Fact]
    public void CharmingPeopleAreLikedFaster()
    {
        var (s, a, b, c) = Trio();
        s.LoyalUid[a] = -1;
        s.Set(b, Trait.Charisma, 0.9f); s.Set(b, Trait.Looks, 0.9f);
        s.Set(c, Trait.Charisma, 0.1f); s.Set(c, Trait.Looks, 0.1f);
        Social.AddFrom(s, a, b, 10f);
        Social.AddFrom(s, a, c, 10f);
        Assert.True(Social.Opinion(s, a, 2) > Social.Opinion(s, a, 3) * 1.5f);
    }
}
