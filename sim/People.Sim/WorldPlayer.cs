namespace People.Sim;

/// <summary>
/// 플레이어 가족(한 집). 모든 사람은 같은 규칙으로 움직이고, 플레이어 집은 자동 결정 대신 플레이어 결정을 쓸 뿐이다.
/// 집이 끊기면 아이들을 맡은 집(없으면 가장 가까운 혈족 집)으로 따라간다. 연대기는 플레이어 집 사람의 일만 적는다.
/// </summary>
public sealed partial class World
{
    private int _player = -1, _playerFounded = -1;

    public int PlayerHousehold
    {
        get => _player >= 0 && Households.IsAlive(_player) && Households.FoundedDay[_player] == _playerFounded ? _player : -1;
        set
        {
            _player = value;
            _playerFounded = value >= 0 && Households.IsAlive(value) ? Households.FoundedDay[value] : -1;
        }
    }

    private bool IsPlayer(int i) => _player >= 0 && People.Household[i] == PlayerHousehold;

    public readonly List<(int day, string text)> Chronicle = new();
    public int ChronicleCount => Chronicle.Count;

    private bool _fork;

    /// <summary>플레이어 집에 갈림길(가장의 죽음, 집이 끊김)이 생겼으면 true 를 한 번 돌려준다. 화면은 여기서 멈춘다.</summary>
    public bool TakeFork()
    {
        bool f = _fork;
        _fork = false;
        return f;
    }

    private void Chron(int person, string text)
    {
        if (!IsPlayer(person)) return;
        Chronicle.Add((Day, text));
        if (Chronicle.Count > 2000) Chronicle.RemoveRange(0, 500);
    }

    /// <summary>플레이어 집이 끊겼다: 맡아 줄 집으로 옮긴다.</summary>
    private void FollowPlayer(int heir)
    {
        int h = _player;
        if (heir < 0 && Households.IsAlive(h) && People.IsValid(Households.Head[h]))
            heir = ClosestKinHousehold(Households.Head[h].Index, h);
        _fork = true;
        Chronicle.Add((Day, heir >= 0 ? "집이 끊겨 가까운 혈족의 집을 이어 맡는다" : "집이 끊겼다. 이을 혈족이 없다"));
        PlayerHousehold = heir;
    }

    public static string SeasonName(Season s) => s switch { Season.Spring => "봄", Season.Summer => "여름", Season.Autumn => "가을", _ => "겨울" };

    public string DateText => $"{Calendar.Year + 1}년 {SeasonName(Calendar.Season)} {Calendar.DayOfSeason + 1}일";

    private static bool Batchim(string s)
    {
        if (s.Length == 0) return false;
        char c = s[^1];
        return c >= '가' && c <= '힣' && (c - '가') % 28 != 0;
    }

    /// <summary>이름 + 이/가</summary>
    public static string Subj(string s) => s + (Batchim(s) ? "이" : "가");
    /// <summary>이름 + 과/와</summary>
    public static string With(string s) => s + (Batchim(s) ? "과" : "와");

    private static string SecretName(SecretType t) => t switch { SecretType.Affair => "불륜", SecretType.HiddenFather => "숨긴 아버지", SecretType.Abortion => "낙태", _ => "하룻밤 거래" };
}
