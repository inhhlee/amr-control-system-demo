namespace Amr.Simulator.Navigation;

public sealed record GridSnapshot(
    string Phase, GridCell Start, GridCell Goal, GridCell Current,
    int[][] Map, bool[][] FixedObstacles, GridCell[] Path, GridCell[] Traveled,
    int? RemainingDistance, int Moves, int Replans, long MapVersion,
    GridCell[] Waypoints, int NextWaypointIndex, GridCell? CurrentTarget, GridWaypointVisit[] ReachedWaypoints);

public sealed record GridWaypointVisit(int Index, GridCell Cell, int Moves);

/// <summary>호출 세션이 갱신·이동·읽기를 하나의 잠금으로 직렬화한다.</summary>
public sealed class GridNavigator
{
    private static readonly int[,] DefaultMap =
    {
        { 0, 0, 1, 0, 0, 0 },
        { 1, 0, 1, 0, 1, 0 },
        { 0, 0, 0, 0, 1, 0 },
        { 0, 1, 1, 0, 1, 0 },
        { 0, 0, 0, 0, 0, 0 }
    };
    public static int[,] CreateDefaultMap() => (int[,])DefaultMap.Clone();
    private readonly int[,] _originalMap;
    private int[,] _map;
    private readonly GridCell _start, _goal;
    private readonly GridCell[] _waypoints;
    private GridCell _current;
    private string _phase = "Ready";
    private List<GridCell> _path = [];
    private readonly List<GridCell> _traveled = [];
    private readonly List<GridWaypointVisit> _reached = [];
    private int _nextWaypointIndex;
    private int _moves, _replans;
    private long _mapVersion;

    public GridNavigator() : this(new(0, 0), [new(4, 5)]) { }

    public GridNavigator(GridCell start, IReadOnlyList<GridCell> waypoints, int[,]? map = null)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        if (waypoints.Count == 0) throw new ArgumentException("경유점과 목적지를 하나 이상 지정해야 합니다.", nameof(waypoints));
        _originalMap = map is null ? CreateDefaultMap() : (int[,])map.Clone();
        _map = (int[,])_originalMap.Clone();
        _ = GridPathfinder.FindPath(_map, start, start); // 크기·0/1 값·출발점을 검사한다.
        _waypoints = waypoints.ToArray();
        foreach (var cell in _waypoints.Prepend(start))
            if (cell.Row < 0 || cell.Row >= _map.GetLength(0) || cell.Col < 0 || cell.Col >= _map.GetLength(1) || _map[cell.Row, cell.Col] != 0)
                throw new ArgumentException("출발점·경유점·목적지는 맵 안의 통로여야 합니다.");
        _start = _current = start;
        _goal = _waypoints[^1];
        _traveled.Add(start);
        ReachTargetsAtCurrent();
        Replan();
    }

    public bool Start()
    {
        if (_phase is "Running" or "Waiting") return false;
        _current = _start;
        _traveled.Clear();
        _traveled.Add(_start);
        _moves = _replans = 0;
        _nextWaypointIndex = 0;
        _reached.Clear();
        _phase = "Running";
        ReachTargetsAtCurrent();
        Replan();
        return true;
    }

    public void Cancel()
    {
        if (_phase is "Running" or "Waiting") _phase = "Canceled";
    }

    public void Reset()
    {
        _map = (int[,])_originalMap.Clone();
        _mapVersion++;
        _current = _start;
        _phase = "Ready";
        _moves = _replans = 0;
        _nextWaypointIndex = 0;
        _reached.Clear();
        _traveled.Clear();
        _traveled.Add(_start);
        ReachTargetsAtCurrent();
        Replan();
    }

    public bool SetObstacle(GridCell cell, bool blocked)
    {
        if (cell.Row < 0 || cell.Row >= _map.GetLength(0) || cell.Col < 0 || cell.Col >= _map.GetLength(1))
            throw new ArgumentException("좌표가 맵 범위를 벗어났습니다.");
        if (_originalMap[cell.Row, cell.Col] == 1) throw new ArgumentException("설정 맵의 고정 장애물은 바꿀 수 없습니다.");
        if (cell == _current || cell == _start || cell == _goal)
            throw new ArgumentException("현재 위치·출발점·목적지에는 장애물을 놓을 수 없습니다.");
        int value = blocked ? 1 : 0;
        if (_map[cell.Row, cell.Col] == value) return false;
        _map[cell.Row, cell.Col] = value;
        _mapVersion++;
        Replan();
        return true;
    }

    public bool Step()
    {
        if (_phase is not ("Running" or "Waiting")) return false;
        // 현재 순서의 목표만 탐색한다. 뒤 순서 경유점을 지나도 먼저 완료하지 않는다.
        Replan();
        if (_path.Count < 2) return false;
        _current = _path[1];
        _traveled.Add(_current);
        _moves++;
        ReachTargetsAtCurrent();
        Replan();
        return true;
    }

    private void Replan()
    {
        if (_nextWaypointIndex == _waypoints.Length)
            _path = [_current];
        else
        {
            var target = _waypoints[_nextWaypointIndex];
            _path = _map[target.Row, target.Col] == 1 ? [] : GridPathfinder.FindPath(_map, _current, target);
        }
        _replans++;
        if (_phase is "Running" or "Waiting")
            _phase = _nextWaypointIndex == _waypoints.Length ? "Completed" : _path.Count == 0 ? "Waiting" : "Running";
    }

    private void ReachTargetsAtCurrent()
    {
        while (_nextWaypointIndex < _waypoints.Length && _current == _waypoints[_nextWaypointIndex])
        {
            _reached.Add(new(_nextWaypointIndex, _current, _moves));
            _nextWaypointIndex++;
        }
    }

    public GridSnapshot Read()
    {
        int[][] map = new int[_map.GetLength(0)][];
        bool[][] fixedObstacles = new bool[map.Length][];
        for (int row = 0; row < map.Length; row++)
        {
            map[row] = new int[_map.GetLength(1)];
            fixedObstacles[row] = new bool[_map.GetLength(1)];
            for (int col = 0; col < map[row].Length; col++)
            {
                map[row][col] = _map[row, col];
                fixedObstacles[row][col] = _originalMap[row, col] == 1;
            }
        }
        return new(_phase, _start, _goal, _current, map, fixedObstacles, _path.ToArray(), _traveled.ToArray(),
            _path.Count == 0 ? null : _path.Count - 1, _moves, _replans, _mapVersion,
            _waypoints.ToArray(), _nextWaypointIndex,
            _nextWaypointIndex < _waypoints.Length ? _waypoints[_nextWaypointIndex] : null, _reached.ToArray());
    }
}
