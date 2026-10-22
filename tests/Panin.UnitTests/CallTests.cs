using Panin.Application.Collection;
using Panin.Infrastructure.Collection;
using Panin.Infrastructure.Data;

namespace Panin.UnitTests;

public class CallRulesTests
{
    private static CallEntry Valid() => new()
    {
        AccountNo = "123",
        PurposeCode = "01",
        ContactNo = "0812",
        RelationshipCode = "00000",
        ResultCode = "R0019",
        ReasonCode = "0012",
        Notes = "Sudah dihubungi",
    };

    [Fact]
    public void Complete_entry_is_valid() => Assert.Empty(CallRules.Validate(Valid()));

    [Fact]
    public void Mandatory_fields_are_reported_by_label()
    {
        var errors = CallRules.Validate(new CallEntry { AccountNo = "123" });
        foreach (var label in new[] { "Tujuan", "No. yang dihubungi", "Hubungan", "Janji Debitur", "Alasan tidak bayar", "Catatan" })
        {
            Assert.Contains(errors, e => e.StartsWith(label, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Promise_result_requires_date_payment_type_and_positive_amount()
    {
        var e = Valid();
        e.ResultCode = CallRules.PromiseResultCode;
        var errors = CallRules.Validate(e);
        Assert.Contains(errors, x => x.Contains("Tanggal Janji", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("Tipe Pembayaran", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("Jumlah", StringComparison.Ordinal));

        e.PromiseAt = new DateTime(2026, 11, 1);
        e.PaymentTypeCode = "00001";
        e.PtpAmount = 0;
        Assert.Single(CallRules.Validate(e));

        e.PtpAmount = 500_000;
        Assert.Empty(CallRules.Validate(e));
    }

    [Fact]
    public void Next_action_date_without_next_action_is_rejected()
    {
        var e = Valid();
        e.NextActionAt = new DateTime(2026, 11, 1);
        Assert.Single(CallRules.Validate(e));
    }

    [Fact]
    public void Notes_over_the_limit_are_rejected()
    {
        var e = Valid();
        e.Notes = new string('x', CallRules.MaxNotesLength + 1);
        Assert.Single(CallRules.Validate(e));
    }
}

public class CallArgumentsTests
{
    [Fact]
    public void Builds_forty_positional_arguments_in_procedure_order()
    {
        var e = new CallEntry
        {
            AccountNo = " 123 ",
            PurposeCode = "01",
            ContactNo = "0812",
            RelationshipCode = "02",
            ResultCode = "R0020",
            ReasonCode = "0012",
            SpokeTo = "Budi & Siti",
            Notes = "janji 'besok'",
            PromiseAt = new DateTime(2026, 11, 1, 9, 30, 0),
            PaymentTypeCode = "00001",
            PtpPersonId = "7",
            PtpAmount = 1250000.5m,
            NextActionCode = "00002",
            NextActionAt = new DateTime(2026, 11, 3),
        };

        var a = CallService.BuildArguments("USR1", e, new DateTime(2026, 10, 22, 14, 5, 0));

        Assert.Equal(40, a.Length);
        Assert.Equal("123", a[0]);
        Assert.Equal("USR1", a[1]);
        Assert.Equal("14:05", a[2]);
        Assert.Equal("R0020", a[3]);
        Assert.Equal("0012", a[4]);
        Assert.Equal(new DateTime(2026, 11, 3), a[5]);
        Assert.Equal("00002", a[6]);
        Assert.Equal("0812", a[7]);
        Assert.Equal("02", a[8]);
        Assert.Equal("Budi &amp; Siti", a[9]);
        Assert.Equal("01", a[10]);
        Assert.Equal(new DateTime(2026, 11, 1, 9, 30, 0), a[11]);
        Assert.Equal("janji `besok`", a[29]);
        Assert.Equal("1250000.5", a[37]);
        Assert.Equal("00001", a[38]);
        Assert.Equal("7", a[39]);
    }

    [Fact]
    public void Unused_legacy_update_slots_are_null_meaning_no_change()
    {
        var a = CallService.BuildArguments("U", new CallEntry { AccountNo = "1" }, DateTime.Now);
        foreach (var i in Enumerable.Range(12, 17).Concat(Enumerable.Range(30, 7)))
        {
            Assert.Null(a[i]);
        }
    }
}

public class CallSqlGuardTests
{
    private static readonly ReadOnlyDb Db = new(null!, new HashSet<string>(
        ["SP_GET_REASON", "SP_GET_ACTION", "SP_PTP_CHANGELIST", "SP_GET_CONTACT", "GET_RELATIONSHIP_TYPE", "GET_PAYMENT_TYPE", "SP_TREATMENTRESULT_MAXDAY", "SP_GET_TREATMENT_MENU"],
        StringComparer.OrdinalIgnoreCase), "t");

    [Theory]
    [InlineData(CallService.CanActSql)]
    [InlineData(CallService.HistorySql)]
    [InlineData("exec SP_PTP_CHANGELIST @1, @2")]
    [InlineData("exec SP_TREATMENTRESULT_MAXDAY @1, @2")]
    [InlineData("exec GET_PAYMENT_TYPE")]
    public void Read_paths_pass_the_read_only_guard(string sql) => Assert.True(Db.Check(sql));

    [Fact]
    public void Saving_is_blocked_on_a_read_only_database()
        => Assert.False(Db.Check("SET XACT_ABORT ON; BEGIN TRAN; exec SAVE_CALL_HISTORY @1; COMMIT TRAN"));
}
