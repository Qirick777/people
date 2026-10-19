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
}
