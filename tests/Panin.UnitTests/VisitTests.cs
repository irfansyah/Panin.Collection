using Panin.Application.Collection;
using Panin.Infrastructure.Collection;

namespace Panin.UnitTests;

public class ContactUpdateTests
{
    [Fact]
    public void Untouched_update_sends_only_nulls()
    {
        var a = new object?[40];
        new ContactUpdate().Fill(a, null);
        for (var i = 12; i <= 28; i++)
        {
            Assert.Null(a[i]);
        }

        for (var i = 30; i <= 36; i++)
        {
            Assert.Null(a[i]);
        }
    }

    [Fact]
    public void Changed_phone_sends_description_and_trimmed_text()
    {
        var u = new ContactUpdate { Mobile = "Berubah menjadi", MobileNew = " 0812 " };
        var a = new object?[40];
        u.Fill(a, null);
        Assert.Equal("Berubah menjadi", a[17]);
        Assert.Equal("0812", a[18]);
    }

    [Fact]
    public void Text_is_ignored_when_description_does_not_ask_for_it()
    {
        var u = new ContactUpdate { OfficePhone = "Tidak ada", OfficePhoneNew = "123" };
        var a = new object?[40];
        u.Fill(a, null);
        Assert.Equal("Tidak ada", a[15]);
        Assert.Null(a[16]);
    }

    [Fact]
    public void Address_move_fills_continuation_city_and_zip()
    {
        var u = new ContactUpdate
        {
            CorrespondenceAddress = "Pindah ke", CorrespondenceNew = "Jl. Baru 1", CorrespondenceNew2 = "RT 2", CorrespondenceCity = "3171", CorrespondenceZip = "12345",
            HouseNew2 = "diabaikan", HouseZip = "99999",
        };
        var a = new object?[40];
        u.Fill(a, "Terkirim");
        Assert.Equal("Pindah ke", a[19]);
        Assert.Equal("Jl. Baru 1", a[20]);
        Assert.Equal("RT 2", a[31]);
        Assert.Equal("3171", a[32]);
        Assert.Equal("12345", a[33]);
        Assert.Null(a[34]);
        Assert.Null(a[36]);
        Assert.Equal("Terkirim", a[30]);
    }

    [Fact]
    public void Collateral_text_only_for_lainnya()
    {
        var u = new ContactUpdate { CollateralStatus = "Ditempati", CollateralStatusNew = "x", CollateralCondition = "Lainnya", CollateralConditionNew = "retak" };
        var a = new object?[40];
        u.Fill(a, null);
        Assert.Equal("Ditempati", a[25]);
        Assert.Null(a[26]);
        Assert.Equal("Lainnya", a[27]);
        Assert.Equal("retak", a[28]);
    }

    [Fact]
    public void Required_text_and_lengths_are_validated()
    {
        Assert.Empty(new ContactUpdate().Validate());
        Assert.Single(new ContactUpdate { HousePhone = "Berubah menjadi" }.Validate());
        Assert.Single(new ContactUpdate { Mobile = "Berubah menjadi", MobileNew = new string('9', 21) }.Validate());
        Assert.Single(new ContactUpdate { CollateralCondition = "Lainnya" }.Validate());
    }
}

public class VisitTests
{
    private static VisitEntry Valid() => new()
    {
        AccountNo = "123",
        OfficerCode = "AO01",
        PurposeCode = "01",
        PersonVisited = "Budi",
        RelationshipCode = "00000",
        Notes = "Bertemu",
    };

    [Fact]
    public void Complete_entry_is_valid() => Assert.Empty(VisitRules.Validate(Valid()));

    [Fact]
    public void Mandatory_fields_are_reported_by_label()
    {
        var errors = VisitRules.Validate(new VisitEntry { AccountNo = "123" });
        foreach (var label in new[] { "Petugas kunjungan", "Tujuan", "Orang yang ditemui", "Hubungan", "Catatan" })
        {
            Assert.Contains(errors, e => e.StartsWith(label, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Next_action_date_without_next_action_is_rejected()
    {
        var e = Valid();
        e.NextActionAt = new DateTime(2026, 11, 1);
        Assert.Single(VisitRules.Validate(e));
    }

    [Fact]
    public void Arguments_follow_the_37_parameter_order()
    {
        var e = Valid();
        e.VisitAt = new DateTime(2026, 11, 2, 14, 30, 0);
        e.CustomerAddress = "Jl. A & B";
        e.ResultCode = "R0012";
        e.ResultAt = new DateTime(2026, 11, 3, 9, 0, 0);
        e.NextActionCode = "00005";
        e.Notes = "a < b";
        e.Update.LetterStatus = "R0024";

        var a = VisitService.BuildArguments("CBGX", e, "Terkirim");

        Assert.Equal(37, a.Length);
        Assert.Equal("CBGX", a[0]);
        Assert.Equal("123", a[1]);
        Assert.Equal("AO01", a[2]);
        Assert.Equal("00000", a[3]);
        Assert.Equal("a &lt; b", a[4]);
        Assert.Equal(new DateTime(2026, 11, 2), a[5]);
        Assert.Equal("Jl. A &amp; B", a[6]);
        Assert.Equal("R0012", a[7]);
        Assert.Equal("01", a[8]);
        Assert.Equal(e.ResultAt, a[9]);
        Assert.Equal("00005", a[10]);
        Assert.Equal("R0024", a[12]);
        Assert.Equal("Budi", a[29]);
        Assert.Equal("Terkirim", a[30]);
    }

    [Fact]
    public void Call_arguments_carry_the_contact_update()
    {
        var e = new CallEntry { AccountNo = "1", Notes = "n" };
        e.Update.OfficeAddress = "Pindah ke";
        e.Update.OfficeNew = "Jl. Kantor";
        e.Update.LetterStatus = "R0017";
        var a = CallService.BuildArguments("U", e, new DateTime(2026, 1, 1));
        Assert.Equal("Pindah ke", a[23]);
        Assert.Equal("Jl. Kantor", a[24]);
        Assert.Equal("R0017", a[12]);
        Assert.Equal("R0017", a[30]);
        Assert.Equal("n", a[29]);
    }
}
