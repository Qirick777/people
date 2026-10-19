namespace People.Sim;

public enum InteractionId : byte { Chat, Compliment, Gossip, Gift, Affair, SexTrade, Count }

public enum InteractionCategory : byte { Talk, Courtship, Family, Trade, Rule, Scheme }

/// <summary>상호작용 한 번의 맥락. Third 는 험담 대상처럼 세 번째 사람(person 번호), Amount 는 값.</summary>
public struct InteractionContext
{
    public int Actor, Target, Third;
    public float Amount;
}

/// <summary>상호작용 결과.</summary>
public sealed class InteractionResult
{
    public bool Accepted, Pregnant;
    public float Amount;
}

/// <summary>
/// 상호작용 정의. 조건, 받아들일 확률, 성공·실패 효과를 담는다. NPC와 플레이어가 같은 정의를 쓴다.
/// Secret 이면 성공할 때 비밀이 생기고 들킬 수 있다. Sexual 이면 어른끼리만, 압박을 쓸 수 없다.
/// </summary>
public sealed class InteractionDef
{
    public InteractionId Id;
    public string Name = "";
    public InteractionCategory Category;
    public bool Secret, Sexual, AllowPressure;
    public Func<World, InteractionContext, bool> Can = (_, _) => true;
    public Func<World, InteractionContext, float> Accept = (_, _) => 1f;
    public Action<World, InteractionContext, InteractionResult>? Success;
    public Action<World, InteractionContext>? Fail;
}
