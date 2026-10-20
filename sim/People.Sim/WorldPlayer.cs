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

    /// <summary>옮겨 맡을 수 있는 혈족 범위(촌수). 기본 4, 설정으로 6까지.</summary>
    public int KinRange { get; set; } = 4;

    private int _pendingSwitch = -1, _pendingFounded = -1;

    /// <summary>집 h 의 가장이나 가장의 배우자가 지금 맡은 가장에서 몇 촌인가(가까운 쪽). 혈족이 아니면 -1.</summary>
    public int KinChonOfHousehold(int h)
    {
        int me = PlayerHousehold;
        if (me < 0 || !Households.IsAlive(h) || !People.IsValid(Households.Head[me])) return -1;
        int anchor = People.Uid[Households.Head[me].Index];
        var head = Households.Head[h];
        if (!People.IsValid(head)) return -1;
        int hi = head.Index;
        int best = Lineage.Chon(anchor, People.Uid[hi]);
        int sp = SpouseOf(hi);
        if (sp >= 0)
        {
            int c = Lineage.Chon(anchor, People.Uid[sp]);
            if (c >= 0 && (best < 0 || c < best)) best = c;
        }
        return best;
    }

    /// <summary>옮겨 맡을 수 있는 혈족 집들(지금 맡은 집 제외)과 촌수.</summary>
    public List<(int h, int chon)> KinHouseholds()
    {
        var list = new List<(int, int)>();
        int me = PlayerHousehold;
        if (me < 0) return list;
        for (int h = 0; h < Households.HighWater; h++)
        {
            if (h == me || !Households.IsAlive(h)) continue;
            int c = KinChonOfHousehold(h);
            if (c > 0 && c <= KinRange) list.Add((h, c));
        }
        list.Sort((a, b) => a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : b.Item1.CompareTo(a.Item1));
        return list;
    }

    /// <summary>혈족 집으로 옮겨 맡기를 예약한다. 다음 계절이 시작될 때 옮긴다(갈림길을 피해 옮겨 가지 못하게).</summary>
    public bool RequestSwitch(int h)
    {
        int c = KinChonOfHousehold(h);
        if (c <= 0 || c > KinRange) return false;
        _pendingSwitch = h;
        _pendingFounded = Households.FoundedDay[h];
        return true;
    }

    public int PendingSwitch => _pendingSwitch >= 0 && Households.IsAlive(_pendingSwitch) && Households.FoundedDay[_pendingSwitch] == _pendingFounded ? _pendingSwitch : -1;

    /// <summary>계절이 바뀔 때 부른다: 예약한 옮기기를 한다.</summary>
    private void ApplySwitch()
    {
        int h = PendingSwitch;
        _pendingSwitch = -1;
        if (h < 0 || PlayerHousehold < 0) return;
        PlayerHousehold = h;
        var head = Households.Head[h];
        Chronicle.Add((Day, $"{(People.IsValid(head) ? Names.Full(Lineage, People.Uid[head.Index]) : "")}의 집을 맡았다"));
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

    /// <summary>마지막 한글 글자의 받침(0 없음). 따옴표·괄호·숫자는 건너뛰고 본다(숫자는 읽는 소리로).</summary>
    private static int FinalJong(string s)
    {
        for (int k = s.Length - 1; k >= 0; k--)
        {
            char c = s[k];
            if (c >= '가' && c <= '힣') return (c - '가') % 28;
            // 숫자: 영(ㅇ) 일(ㄹ) 이(없음) 삼(ㅁ) 사 오 육(ㄱ) 칠(ㄹ) 팔(ㄹ) 구
            if (c >= '0' && c <= '9') return "\u0015\u0008\u0000\u0010\u0000\u0000\u0001\u0008\u0008\u0000"[c - '0'];
            if (c is '\'' or '"' or ')' or ']' or ' ' or '’' or '”') continue;
            return 0;
        }
        return 0;
    }

    private static bool Batchim(string s) => FinalJong(s) != 0;

    /// <summary>이름 + 이/가</summary>
    public static string Subj(string s) => s + (Batchim(s) ? "이" : "가");
    /// <summary>이름 + 과/와</summary>
    public static string With(string s) => s + (Batchim(s) ? "과" : "와");
    /// <summary>이름 + 을/를</summary>
    public static string Obj(string s) => s + (Batchim(s) ? "을" : "를");
    /// <summary>이름 + 으로/로(ㄹ 받침은 로)</summary>
    public static string Ro(string s) { int j = FinalJong(s); return s + (j != 0 && j != 8 ? "으로" : "로"); }
    /// <summary>이름 + 은/는</summary>
    public static string Topic(string s) => s + (Batchim(s) ? "은" : "는");

    private static string SecretName(SecretType t) => t switch { SecretType.Affair => "불륜", SecretType.HiddenFather => "숨긴 아버지", SecretType.Abortion => "낙태", SecretType.Crime => "숨은 범죄", SecretType.CoverUp => "덮은 사건", _ => "하룻밤 거래" };
}
