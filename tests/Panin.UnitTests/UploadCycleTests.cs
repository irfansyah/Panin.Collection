using Panin.Worker;
using Panin.Worker.Upload;

namespace Panin.UnitTests;

public sealed class UploadCycleTests
{
    private static readonly DateTime Day = new(2026, 10, 22, 0, 0, 0, DateTimeKind.Local);

    private sealed class FakeFiles(params string[] names) : IUploadFiles
    {
        public List<string> Present { get; } = [.. names];

        public List<(string Name, string To, string NewName)> Moves { get; } = [];

        public IReadOnlyCollection<string> Names(string path) => Present;

        public void Move(string path, string name, string destinationDirectory, string newName) => Moves.Add((name, destinationDirectory, newName));
    }

    private sealed class FakeDb : IUploadDatabase
    {
        public bool Success { get; set; }

        public Exception? RunError { get; set; }

        public List<string> Log { get; } = [];

        public List<string> Paths { get; } = [];

        public Task<bool> HasSuccessOnAsync(DateTime day, CancellationToken ct) => Task.FromResult(Success);

        public Task LogSuccessAsync(string sender, DateTime time, CancellationToken ct)
        {
            Log.Add("OK");
            return Task.CompletedTask;
        }

        public Task LogFailAsync(string sender, DateTime time, string message, CancellationToken ct)
        {
            Log.Add("FAIL:" + message);
            return Task.CompletedTask;
        }

        public Task RunAsync(IReadOnlyList<string> pre, IReadOnlyList<string> post, string path, int timeoutSeconds, CancellationToken ct)
        {
            Paths.Add(path);
            return RunError is null ? Task.CompletedTask : Task.FromException(RunError);
        }
    }

    private static UploadOptions Options(UploadMode mode = UploadMode.Run) => new()
    {
        Mode = mode,
        Path = @"\\srv\upload",
        DonePath = @"\\srv\done",
        FailPath = @"\\srv\fail",
        RequiredFiles = ["AOC", "CUS"],
        OptionalFiles = ["MIS"],
        WindowStart = "05:00",
        WindowFinish = "17:00",
        IntervalMinutes = 5,
    };

    private static FakeFiles Complete() => new("AOC2210.TXT", "cus2210.txt", "MIS2210.TXT", "OTHER.TXT");

    [Fact]
    public async Task Off_does_nothing()
    {
        var db = new FakeDb();
        var outcome = await new UploadCycle(Options(UploadMode.Off), Complete(), db).StepAsync(Day.AddHours(9), default);
        Assert.Equal(CycleKind.Idle, outcome.Kind);
        Assert.Empty(db.Paths);
    }

    [Fact]
    public async Task Outside_window_is_idle()
    {
        var db = new FakeDb();
        var cycle = new UploadCycle(Options(), Complete(), db);
        Assert.Equal(CycleKind.Idle, (await cycle.StepAsync(Day.AddHours(4), default)).Kind);
        Assert.Equal(CycleKind.Idle, (await cycle.StepAsync(Day.AddHours(18), default)).Kind);
        Assert.Empty(db.Paths);
    }

    [Fact]
    public async Task Complete_files_run_log_success_and_move_to_done_with_separator()
    {
        var files = Complete();
        var db = new FakeDb();
        var outcome = await new UploadCycle(Options(), files, db).StepAsync(Day.AddHours(9).AddMinutes(30).AddSeconds(5), default);

        Assert.Equal(CycleKind.Succeeded, outcome.Kind);
        Assert.Equal([@"\\srv\upload\"], db.Paths);
        Assert.Equal(["OK"], db.Log);
        Assert.Equal(3, files.Moves.Count);
        Assert.All(files.Moves, m => Assert.Equal(@"\\srv\done", m.To));
        Assert.Contains(files.Moves, m => m.NewName == "AOC2210-20261022093005.TXT");
    }

    [Fact]
    public async Task Missing_required_waits_without_logging_or_moving()
    {
        var files = new FakeFiles("AOC2210.TXT");
        var db = new FakeDb();
        var outcome = await new UploadCycle(Options(), files, db).StepAsync(Day.AddHours(9), default);

        Assert.Equal(CycleKind.Waiting, outcome.Kind);
        Assert.Contains("CUS2210.TXT", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(db.Log);
        Assert.Empty(files.Moves);
    }

    [Fact]
    public async Task Missing_at_last_chance_logs_fail_once()
    {
        var db = new FakeDb();
        var cycle = new UploadCycle(Options(), new FakeFiles(), db);
        var late = Day.AddHours(16).AddMinutes(58);
        Assert.Equal(CycleKind.Failed, (await cycle.StepAsync(late, default)).Kind);
        Assert.Equal(CycleKind.Idle, (await cycle.StepAsync(late.AddMinutes(6), default)).Kind);
        Assert.Single(db.Log);
    }

    [Fact]
    public async Task Already_succeeded_today_is_skipped()
    {
        var db = new FakeDb { Success = true };
        var outcome = await new UploadCycle(Options(), Complete(), db).StepAsync(Day.AddHours(9), default);
        Assert.Equal(CycleKind.Idle, outcome.Kind);
        Assert.Empty(db.Paths);
    }

    [Fact]
    public async Task Failure_logs_moves_to_fail_and_stops_for_the_day()
    {
        var files = Complete();
        var db = new FakeDb { RunError = new InvalidOperationException("boom Last Query xyz") };
        var cycle = new UploadCycle(Options(), files, db);

        var first = await cycle.StepAsync(Day.AddHours(9), default);
        Assert.Equal(CycleKind.Failed, first.Kind);
        Assert.Equal("boom ", first.Message);
        Assert.Equal(["FAIL:boom "], db.Log);
        Assert.All(files.Moves, m => Assert.Equal(@"\\srv\fail", m.To));

        Assert.Equal(CycleKind.Idle, (await cycle.StepAsync(Day.AddHours(10), default)).Kind);
        Assert.Single(db.Paths);
    }

    [Fact]
    public async Task Interval_limits_checks()
    {
        var db = new FakeDb();
        var cycle = new UploadCycle(Options(), new FakeFiles(), db);
        var t = Day.AddHours(9);
        Assert.Equal(CycleKind.Waiting, (await cycle.StepAsync(t, default)).Kind);
        Assert.Equal(CycleKind.Idle, (await cycle.StepAsync(t.AddMinutes(2), default)).Kind);
        Assert.Equal(CycleKind.Waiting, (await cycle.StepAsync(t.AddMinutes(5), default)).Kind);
    }

    [Fact]
    public async Task Another_run_in_progress_retries_next_poll()
    {
        var db = new FakeDb { RunError = new UploadAlreadyRunningException() };
        var cycle = new UploadCycle(Options(), Complete(), db);
        var t = Day.AddHours(9);
        Assert.Equal(CycleKind.Idle, (await cycle.StepAsync(t, default)).Kind);
        db.RunError = null;
        Assert.Equal(CycleKind.Succeeded, (await cycle.StepAsync(t.AddSeconds(30), default)).Kind);
    }

    [Fact]
    public async Task Observe_reports_but_does_not_run_or_move()
    {
        var files = Complete();
        var db = new FakeDb();
        var outcome = await new UploadCycle(Options(UploadMode.Observe), files, db).StepAsync(Day.AddHours(9), default);
        Assert.Equal(CycleKind.Observed, outcome.Kind);
        Assert.Empty(db.Paths);
        Assert.Empty(files.Moves);
    }

    [Theory]
    [InlineData(@"\\srv\x", @"\\srv\x\")]
    [InlineData(@"\\srv\x\", @"\\srv\x\")]
    [InlineData("/data/x", "/data/x/")]
    public void Path_gets_trailing_separator(string input, string expected) => Assert.Equal(expected, UploadCycle.WithSeparator(input));

    [Fact]
    public void Validation_flags_problems_only_when_enabled()
    {
        Assert.Empty(new UploadOptions().Validate());
        var errors = new UploadOptions { Mode = UploadMode.Run, WindowStart = "17:00", WindowFinish = "05:00" }.Validate();
        Assert.Contains(errors, e => e.Contains("Path", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("setelah", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("DonePath", StringComparison.Ordinal));
    }
}
