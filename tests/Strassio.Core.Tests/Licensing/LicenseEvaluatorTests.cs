using System.Security.Cryptography;
using System.Text;
using Strassio.Licensing;

namespace Strassio.Core.Tests.Licensing;

/// <summary>Правила лицензии (SPEC 13.1–13.5): подпись, код компьютера, сроки, работа без сети.</summary>
public class LicenseEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static readonly HardwareCode Pc = HardwareCode.FromComponents(new[] { "uuid-1", "board-1", "cpu-1", "disk-1" });

    private static (ECDsa Signer, LicensePublicKey Key) NewKey()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters p = ecdsa.ExportParameters(false);
        string xy = Convert.ToBase64String(p.Q.X!.Concat(p.Q.Y!).ToArray());
        return (ecdsa, LicensePublicKey.FromBase64(xy));
    }

    private static SignedDocument Sign(ECDsa signer, string payload) => new()
    {
        Payload = payload,
        Signature = Convert.ToBase64String(
            signer.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
    };

    private static string Payload(HardwareCode hw, string plan = LicenseData.PlanFull, DateTime? expires = null, DateTime? issued = null)
    {
        DateTime issuedAt = issued ?? Now.AddDays(-1);
        string exp = expires.HasValue ? $"\"expiresAt\":\"{LicenseData.ToIso(expires.Value)}\"," : string.Empty;
        return "{\"serial\":\"STR-1\",\"hwid\":\"" + hw.ToStorage() + "\",\"plan\":\"" + plan + "\"," + exp +
               "\"issuedAt\":\"" + LicenseData.ToIso(issuedAt) + "\",\"nextCheckAt\":\"" + LicenseData.ToIso(issuedAt.AddDays(14)) + "\"}";
    }

    [Fact]
    public void NoFile_IsNone()
    {
        (_, LicensePublicKey key) = NewKey();
        Assert.Equal(LicenseState.None, LicenseEvaluator.Evaluate(null, Pc, Now, null, key).State);
    }

    [Fact]
    public void GoodLicense_IsValid_AndCanCreate()
    {
        (ECDsa signer, LicensePublicKey key) = NewKey();
        LicenseStatus s = LicenseEvaluator.Evaluate(Sign(signer, Payload(Pc)), Pc, Now, Now.AddDays(-1), key);
        Assert.Equal(LicenseState.Valid, s.State);
        Assert.True(s.CanCreate);
        Assert.False(s.NeedsOnlineCheck);
        Assert.Null(s.DaysLeft);
    }

    [Fact]
    public void EditedPayload_OrOtherKey_IsBadSignature()
    {
        (ECDsa signer, LicensePublicKey key) = NewKey();
        SignedDocument doc = Sign(signer, Payload(Pc));
        doc.Payload = doc.Payload.Replace("full", "fuls");
        Assert.Equal(LicenseState.BadSignature, LicenseEvaluator.Evaluate(doc, Pc, Now, null, key).State);

        (_, LicensePublicKey other) = NewKey();
        Assert.Equal(LicenseState.BadSignature, LicenseEvaluator.Evaluate(Sign(signer, Payload(Pc)), Pc, Now, null, other).State);
    }

    [Fact]
    public void OneChangedPart_StillSameComputer_TwoChanged_IsWrongComputer()
    {
        (ECDsa signer, LicensePublicKey key) = NewKey();
        SignedDocument doc = Sign(signer, Payload(Pc));
        var newDisk = HardwareCode.FromComponents(new[] { "uuid-1", "board-1", "cpu-1", "disk-2" });
        var otherPc = HardwareCode.FromComponents(new[] { "uuid-9", "board-9", "cpu-1", "disk-1" });
        Assert.Equal(LicenseState.Valid, LicenseEvaluator.Evaluate(doc, newDisk, Now, Now, key).State);
        Assert.Equal(LicenseState.WrongComputer, LicenseEvaluator.Evaluate(doc, otherPc, Now, Now, key).State);
    }

    [Fact]
    public void Trial_CountsDays_ThenExpires()
    {
        (ECDsa signer, LicensePublicKey key) = NewKey();
        SignedDocument doc = Sign(signer, Payload(Pc, LicenseData.PlanTrial, Now.AddDays(10)));
        LicenseStatus s = LicenseEvaluator.Evaluate(doc, Pc, Now, Now, key);
        Assert.Equal(LicenseState.Trial, s.State);
        Assert.Equal(10, s.DaysLeft);

        LicenseStatus later = LicenseEvaluator.Evaluate(doc, Pc, Now.AddDays(11), Now.AddDays(11), key);
        Assert.Equal(LicenseState.Expired, later.State);
        Assert.False(later.CanCreate);
    }

    [Fact]
    public void Offline_ChecksAfter14Days_StopsAfter30()
    {
        (ECDsa signer, LicensePublicKey key) = NewKey();
        SignedDocument doc = Sign(signer, Payload(Pc, issued: Now));
        LicenseStatus day15 = LicenseEvaluator.Evaluate(doc, Pc, Now.AddDays(15), Now, key);
        Assert.Equal(LicenseState.Valid, day15.State);
        Assert.True(day15.NeedsOnlineCheck);

        Assert.Equal(LicenseState.OfflineTooLong, LicenseEvaluator.Evaluate(doc, Pc, Now.AddDays(31), Now, key).State);
    }

    [Fact]
    public void HardwareCode_JunkValues_AreUnknown_AndStorageRoundTrips()
    {
        var hw = HardwareCode.FromComponents(new[] { "To be filled by O.E.M.", "  board-1 ", "00000000", null });
        Assert.Equal(HardwareCode.Unknown, hw.Hashes[0]);
        Assert.Equal(HardwareCode.Unknown, hw.Hashes[2]);
        Assert.Equal(HardwareCode.Unknown, hw.Hashes[3]);
        Assert.True(HardwareCode.TryParse(hw.ToStorage(), out HardwareCode? back));
        Assert.Equal(hw.ToStorage(), back!.ToStorage());
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", Pc.Display);

        // Известен только один общий признак — этого мало, чтобы признать компьютер тем же.
        Assert.False(hw.Matches(HardwareCode.FromComponents(new[] { "a", "board-1", "c", "d" })));
    }
}
